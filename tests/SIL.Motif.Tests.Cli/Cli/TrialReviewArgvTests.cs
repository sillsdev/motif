using System.Diagnostics;
using System.Text.Json;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Review;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Review;
using SIL.Motif.Host;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Runtime.Review;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Cli;

public sealed class TrialReviewArgvTests
{
    [Fact]
    public async Task NoTrialReturnsTheTypedResultAndAnExplicitStartTarget()
    {
        var root = Path.Combine(Path.GetTempPath(), "motif-cli-review-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var project = Path.Combine(root, "project.fwdata");
            File.WriteAllText(project, "");
            var start = new ProcessStartInfo(BuildOutput.Cli)
            { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var argument in new[] { "proposal", "trial", "--project", project, "proposal/one", "--json" })
                start.ArgumentList.Add(argument);
            start.Environment["MOTIF_DEVELOPER_COMMANDS"] = "1";
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            await Task.WhenAll(output, error, process.WaitForExitAsync()).WaitAsync(TimeSpan.FromSeconds(60));
            Assert.True(process.ExitCode == 0, await error);
            using var json = JsonDocument.Parse(await output);
            Assert.Equal("no-trial", json.RootElement.GetProperty("state").GetString());
            Assert.Equal("motif_trial", json.RootElement.GetProperty("next")[0].GetProperty("tool").GetString());
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task TheSavedReportReadsWithoutDeveloperCommands()
    {
        var root = Path.Combine(Path.GetTempPath(), "motif-cli-review-user-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var project = Path.Combine(root, "project.fwdata");
            File.WriteAllText(project, "");
            var start = new ProcessStartInfo(BuildOutput.Cli)
            { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var argument in new[] { "proposal", "trial", "--project", project, "proposal/one", "--json" })
                start.ArgumentList.Add(argument);
            start.Environment.Remove("MOTIF_DEVELOPER_COMMANDS");
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            await Task.WhenAll(output, error, process.WaitForExitAsync()).WaitAsync(TimeSpan.FromSeconds(60));
            Assert.True(process.ExitCode == 0, await error);
            using var json = JsonDocument.Parse(await output);
            Assert.Equal("no-trial", json.RootElement.GetProperty("state").GetString());
            Assert.False(File.Exists(Path.Combine(root, "project.motif.db")) || Directory.GetFiles(root, "*.db").Length > 0,
                "A read must not create the store.");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private const string Form = "11111111-1111-1111-1111-111111111111";
    private const string Msa = "33333333-3333-3333-3333-333333333333";

    private static ParseWordEvidence Analysed(string word) => new("fieldworks-parse-analysis/v1", 0, word, 0, false, false, false,
        [new ParseAnalysis([new ParseMorph(Form, Msa, null, null)])], []);

    private static ParseWordEvidence NoAnalysis(string word) => new("fieldworks-parse-analysis/v1", 0, word, 0, false, false, false, [], []);

    private static (string Project, string Proposal, string Job, string Root) SeedTrial(
        Func<string, (TrialReviewObservation[] Before, TrialReviewObservation[] After)> observe, string[] words,
        ReviewAssertion[]? assertions = null, string[]? expectedLosses = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "motif-cli-review-job-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var project = Path.Combine(root, "project.fwdata");
        File.WriteAllText(project, "");
        var proposal = CanonicalId.Mint("proposal/").Value;
        const string job = "job/cli-wait";
        var seeded = ProjectStoreCommand.Run<string>(project, MotifProductVersion.CurrentText, (database, locator) =>
        {
            var key = ProjectWorkspaceKey.Compute(locator);
            using (var connection = database.OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "INSERT INTO Proposals (ProposalId,CurrentIntentDigest,Status) VALUES ($id,'intent','proposed');";
                command.Parameters.AddWithValue("$id", proposal);
                command.ExecuteNonQuery();
            }
            var inputs = new FrozenReviewInputs(key, proposal, "scope", "manifest", null, [], [],
                words.Select(word => new ReviewPlannedCase("case-" + word, "key-" + word, word, "ws", null, [], assertions ?? [])).ToArray());
            var digest = "sha256:" + new string('a', 64);
            var baseline = new BaselineToken(key, digest, "v1", "2026-01-01T00:00:00Z", digest);
            var reference = ReviewInputArtifacts.Store(database, new ReviewInputArtifact(inputs, new Dictionary<string, byte[]>()) { Baseline = baseline });
            var proposalJson = JsonSerializer.Serialize(new { contractVersions = new Dictionary<string, string>(), proposalId = proposal, operations = Array.Empty<object>() });
            var input = new TrialJobInput(proposalJson, null, ReviewInputs: reference);
            var inputNode = JsonSerializer.SerializeToNode(input, MotifJson.CreateOptions())!.AsObject();
            inputNode["expectedLosses"] = JsonSerializer.SerializeToNode(expectedLosses);
            new JobRepository(database).RecordInterrupted(job, key, "trial", inputNode.ToJsonString(),
                "{}", "2026-01-01T00:00:00Z", JobStatus.Failed);
            var (before, after) = observe("");
            TrialReviewEvidence.Save(database, new TrialReviewLedger(job, reference, before, after, []));
            return CommandOutcome<string>.Success("seeded");
        });
        Assert.True(seeded.Succeeded, seeded.Refusal?.Message);
        return (project, proposal, job, root);
    }

    private static ParseWordEvidence AnalysedAs(string word, string form) => new("fieldworks-parse-analysis/v1", 0, word, 0, false, false, false,
        [new ParseAnalysis([new ParseMorph(form, Msa, null, null)])], []);

    private static ParseWordEvidence TimedOut(string word) => new("fieldworks-parse-analysis/v1", 0, word, 0, false, true, false, [], []);

    private static string OutcomeOf(ParseWordEvidence? evidence) =>
        evidence is { TimedOut: true } ? "timed-out" : evidence is { Analyses.Count: > 0 } ? "analysed" : "no-analysis";

    private static TrialGate Gate(string[] words, ParseWordEvidence?[] before, ParseWordEvidence?[] after,
        string[]? expected = null, ReviewAssertion[]? assertions = null)
    {
        var seed = SeedTrial(_ => (
            words.Select((word, i) => new TrialReviewObservation("case-" + word, word,
                OutcomeOf(before[i]), before[i])).Where(o => o.Morphology is not null).ToArray(),
            words.Select((word, i) => new TrialReviewObservation("case-" + word, word,
                OutcomeOf(after[i]), after[i])).Where(o => o.Morphology is not null).ToArray()), words, assertions, expected);
        try
        {
            var read = TrialReviewReads.ReadJob(seed.Project, MotifProductVersion.CurrentText, seed.Proposal, seed.Job);
            Assert.True(read.Succeeded, read.Refusal?.Message);
            return Assert.IsType<AvailableTrialResult>(read.Value).Gate!;
        }
        finally { Directory.Delete(seed.Root, recursive: true); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void StoredLossNamesSurviveDetailBudgetAndKeepTheTrialsExpectedLosses(bool expected, bool incomplete)
    {
        var words = Enumerable.Range(0, 5100).Select(index => "word-" + index.ToString("D4")).ToArray();
        var lost = words.Take(7).ToArray();
        var seed = SeedTrial(_ => (
            words.Select(word => new TrialReviewObservation("case-" + word, word, "analysed", Analysed(word))).ToArray(),
            words.Select(word => new TrialReviewObservation("case-" + word, word, "complete",
                lost.Contains(word) ? NoAnalysis(word) : Analysed(word))).Where(item => !incomplete || item.Word != words[^1]).ToArray()), words,
            [Approved(Form)], expected ? lost : []);
        try
        {
            var read = TrialReviewReads.ReadJob(seed.Project, MotifProductVersion.CurrentText, seed.Proposal, seed.Job);
            Assert.True(read.Succeeded, read.Refusal?.Message);
            var waited = Assert.IsType<AvailableTrialResult>(read.Value);
            Assert.Equal(DetailAvailability.Unavailable, waited.Report.DetailAvailability);
            Assert.Equal(7, waited.Gate!.LostWords);
            Assert.Equal(expected ? incomplete ? TrialGateState.Unmeasured : TrialGateState.Green : TrialGateState.Loss, waited.Gate.State);
            Assert.Equal(lost, expected ? waited.Gate.ExpectedLosses : waited.Gate.UnexpectedLosses);
            var saved = TrialReviewReads.ReadStored(seed.Project, new ReviewReadRequest("project", seed.Proposal));
            Assert.True(saved.Succeeded, saved.Refusal?.Message);
            var available = Assert.IsType<AvailableTrialResult>(saved.Value);
            Assert.NotNull(available.Gate);
            Assert.Equal(waited.Gate.State, available.Gate.State);
            Assert.Equal(lost, expected ? available.Gate.ExpectedLosses : available.Gate.UnexpectedLosses);
            using var output = new StringWriter();
            using var error = new StringWriter();
            var exit = SIL.Motif.Cli.Program.Run(
                ["proposal", "trial", "--project", seed.Project, seed.Proposal, "--json"], output, error,
                new Dictionary<string, string?>(StringComparer.Ordinal), static (_, _) => { });
            Assert.True(exit == 0, error.ToString());
            using var json = JsonDocument.Parse(output.ToString());
            Assert.Equal(expected ? incomplete ? "unmeasured" : "green" : "loss",
                json.RootElement.GetProperty("gate").GetProperty("state").GetString());
            var losses = json.RootElement.GetProperty("report").GetProperty("losses");
            Assert.Equal(lost, losses.GetProperty("newNoParseWords").EnumerateArray().Select(item => item.GetString()));
            Assert.Equal(lost, losses.GetProperty("newApprovedLossWords").EnumerateArray().Select(item => item.GetString()));
            Assert.Equal(lost, losses.GetProperty("newlyLostReadings").EnumerateArray().Select(item => item.GetProperty("word").GetString()));
            Assert.Contains("word-0000", SIL.Motif.Cli.Rendering.TrialReviewTextRenderer.Render(available));
        }
        finally { Directory.Delete(seed.Root, recursive: true); }
    }

    [Fact]
    public void StoppedWordsWithoutBaselineAnalysesKeepTheirNamesAndSpecificCauses()
    {
        var invalid = NoAnalysis("canada") with { InvalidShape = true,
            Refusal = new ParserRefusal("invalid-shape", "Unrecognized segment 'c' at position 0.") };
        var seed = SeedTrial(_ => ([new("case-canada", "canada", "skipped", invalid)],
            [new("case-canada", "canada", "skipped", invalid)]), ["canada"]);
        try
        {
            var read = TrialReviewReads.ReadJob(seed.Project, MotifProductVersion.CurrentText, seed.Proposal, seed.Job);
            Assert.True(read.Succeeded, read.Refusal?.Message);
            var available = Assert.IsType<AvailableTrialResult>(read.Value);
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(available, ReviewJson.Options));
            var stopped = json.RootElement.GetProperty("report").GetProperty("stoppedWords");
            Assert.Equal(2, stopped.GetArrayLength());
            Assert.All(stopped.EnumerateArray(), item =>
            {
                Assert.Equal("canada", item.GetProperty("word").GetString());
                Assert.Contains("invalid-evidence", item.GetProperty("reasons").EnumerateArray().Select(r => r.GetString()));
                Assert.Contains("Unrecognized segment 'c'", item.GetProperty("details")[0].GetString());
            });
            var page = TrialReviewReads.ReadStored(seed.Project, new ReviewReadRequest("", seed.Proposal,
                ReportId: available.Report.ReportId, Detail: DetailLevel.Full, Section: "incomplete"));
            Assert.True(page.Succeeded, page.Refusal?.Message);
            var row = Assert.IsType<SIL.Motif.Contract.Review.WordRow>(Assert.Single(Assert.IsType<AvailableTrialResult>(page.Value).Report.Page!.Items));
            using var rowJson = JsonDocument.Parse(JsonSerializer.Serialize(row, ReviewJson.Options));
            Assert.Contains("Unrecognized segment 'c'", rowJson.RootElement.GetProperty("after").GetProperty("stopDetails")[0].GetString());
            Assert.Contains("canada", SIL.Motif.Cli.Rendering.TrialReviewTextRenderer.Render(available));
            Assert.Contains("Unrecognized segment 'c'", SIL.Motif.Cli.Rendering.TrialReviewTextRenderer.Render(available));
        }
        finally { Directory.Delete(seed.Root, recursive: true); }
    }

    [Fact]
    public void AFinishedTrialJobReturnsItsSummaryReportWithAGate()
    {
        var seed = SeedTrial(_ => ([], []), ["word"]);
        try
        {
            var read = TrialReviewReads.ReadJob(seed.Project, MotifProductVersion.CurrentText, seed.Proposal, seed.Job);

            Assert.True(read.Succeeded, read.Refusal?.Message);
            var available = Assert.IsType<AvailableTrialResult>(read.Value);
            Assert.NotEmpty(available.Report.ReportId);
            Assert.NotNull(available.Report.Summary.Incomplete);
            Assert.NotNull(available.Report.Summary.Flags);
            Assert.Equal(TrialGateState.Unmeasured, available.Gate!.State);
        }
        finally { Directory.Delete(seed.Root, recursive: true); }
    }

    [Fact]
    public void AMeasuredWordThatLosesItsAnalysisIsALossEvenWithNoReviewSets()
    {
        var gate = Gate(["word"], [Analysed("word")], [NoAnalysis("word")]);

        Assert.Equal(TrialGateState.Loss, gate.State);
        Assert.Equal(1, gate.LostWords);
        Assert.Equal(["word"], gate.UnexpectedLosses);
    }

    [Fact]
    public void TypedWordsWithNoIdentityThatLoseTheirAnalysisExitNonZeroWithTheLossCode()
    {
        // The 0191 shape: words typed on the command line carry no entry identity and went from analysed to none.
        var words = new[] { "alpha", "beta", "gamma" };
        var gate = Gate(words, words.Select(Analysed).ToArray(), words.Select(NoAnalysis).ToArray());

        Assert.Equal(TrialGateState.Loss, gate.State);
        Assert.Equal(3, gate.LostWords);
        Assert.Equal(FailureEnvelope.TrialLossExitCode, SIL.Motif.Cli.Program.TrialGateExitCode(gate));
        Assert.NotEqual(0, SIL.Motif.Cli.Program.TrialGateExitCode(gate));
        Assert.NotEqual(SIL.Motif.Cli.Program.TrialGateExitCode(gate), SIL.Motif.Cli.Program.TrialGateExitCode(gate with { State = TrialGateState.Unmeasured }));
        Assert.Equal(0, SIL.Motif.Cli.Program.TrialGateExitCode(gate with { State = TrialGateState.Green }));
    }

    private static ReviewAssertion Approved(string form) => new(form, form,
        new ApprovedMorphology([new ApprovedMorph(form, Msa, null, [])]), ReferenceAuthority.Human,
        "actor", "revision", true, false, true, []);

    private const string OtherForm = "22222222-2222-2222-2222-222222222222";

    [Fact]
    public void ALostConfirmedReadingIsALossEvenWhenTheWordKeepsAnAnalysis()
    {
        var gate = Gate(["word"], [AnalysedAs("word", Form)], [AnalysedAs("word", OtherForm)],
            assertions: [Approved(Form)]);

        Assert.Equal(TrialGateState.Loss, gate.State);
        Assert.Equal(["word"], gate.UnexpectedLosses);
    }

    [Fact]
    public void AnExpectedLossExcusesALostConfirmedReading()
    {
        var gate = Gate(["word"], [AnalysedAs("word", Form)], [AnalysedAs("word", OtherForm)],
            expected: ["word"], assertions: [Approved(Form)]);

        Assert.Equal(TrialGateState.Green, gate.State);
        Assert.Equal(["word"], gate.ExpectedLosses);
    }

    [Fact]
    public void AWordStoppedAfterAnalysingInTheBaselineIsUnmeasuredAndNamed()
    {
        var gate = Gate(["slow", "fine"], [Analysed("slow"), Analysed("fine")], [TimedOut("slow"), Analysed("fine")]);

        Assert.Equal(TrialGateState.Unmeasured, gate.State);
        Assert.Equal(["slow"], gate.StoppedWords);
        Assert.Equal(SIL.Motif.Contract.Responses.FailureEnvelope.ExitCodeFor(FailureReason.Refused),
            SIL.Motif.Cli.Program.TrialGateExitCode(gate));
    }

    [Fact]
    public void AWordStoppedWithNoBaselineAnalysisDoesNotChangeTheGate()
    {
        var gate = Gate(["slow"], [NoAnalysis("slow")], [TimedOut("slow")]);

        Assert.Equal(TrialGateState.Green, gate.State);
        Assert.Empty(gate.StoppedWords);
    }

    [Fact]
    public void AnExpectedLossMatchesAcrossNormalisationForms()
    {
        var word = "caf\u00e9";
        var gate = Gate([word], [Analysed(word)], [NoAnalysis(word)], expected: ["cafe\u0301"]);

        Assert.Equal(TrialGateState.Green, gate.State);
        Assert.Equal([word], gate.ExpectedLosses);
    }

    [Fact]
    public void TheExpectLossFileIgnoresBlankLinesAndAMissingFileIsRefused()
    {
        var dir = Path.Combine(Path.GetTempPath(), "motif-expect-loss-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var file = Path.Combine(dir, "expected.txt");
            File.WriteAllText(file, "one\n\n  \ntwo\n");
            var listed = SIL.Motif.Commands.Assess.WordListFile.Resolve(null, file);
            Assert.True(listed.Succeeded);
            Assert.Equal(["one", "two"], listed.Value);
            Assert.False(SIL.Motif.Commands.Assess.WordListFile.Resolve(null, Path.Combine(dir, "absent.txt")).Succeeded);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void ExpectLossWithoutWaitIsExplained()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = SIL.Motif.Cli.Program.Run(
            ["trial", "--project", "absent.fwdata", "proposal", "--expect-loss", "expected.txt"], output, error,
            new Dictionary<string, string?>(StringComparer.Ordinal), static (_, _) => { });

        Assert.Equal(1, exitCode);
        Assert.Contains("--expect-loss needs --wait", error.ToString() + output.ToString());
    }

    [Fact]
    public void AListedExpectedLossIsReportedAndDoesNotFailTheGate()
    {
        var gate = Gate(["word"], [Analysed("word")], [NoAnalysis("word")], expected: ["word"]);

        Assert.Equal(TrialGateState.Green, gate.State);
        Assert.Equal(["word"], gate.ExpectedLosses);
        Assert.Empty(gate.UnexpectedLosses);
    }

    [Fact]
    public void ALossThatIsNotListedStillFailsWhenAnotherIsExpected()
    {
        var gate = Gate(["kept", "gone", "other"], [Analysed("kept"), Analysed("gone"), Analysed("other")],
            [Analysed("kept"), NoAnalysis("gone"), NoAnalysis("other")], expected: ["gone"]);

        Assert.Equal(TrialGateState.Loss, gate.State);
        Assert.Equal(["gone"], gate.ExpectedLosses);
        Assert.Equal(["other"], gate.UnexpectedLosses);
    }

    [Fact]
    public void AWordThatKeepsItsAnalysisIsGreen()
    {
        var gate = Gate(["word"], [Analysed("word")], [Analysed("word")]);

        Assert.Equal(TrialGateState.Green, gate.State);
    }

    [Fact]
    public async Task FullPageKeepsTheSummaryAndReturnsTheCapturedCase()
    {
        var root = Path.Combine(Path.GetTempPath(), "motif-cli-review-page-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var project = Path.Combine(root, "project.fwdata");
            File.WriteAllText(project, "");
            var proposal = CanonicalId.Mint("proposal/").Value;
            var reportId = ProjectStoreCommand.Run<string>(project, MotifProductVersion.CurrentText, (database, locator) =>
            {
                var key = ProjectWorkspaceKey.Compute(locator);
                using (var connection = database.OpenConnection())
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "INSERT INTO Proposals (ProposalId,CurrentIntentDigest,Status) VALUES ($id,'intent','proposed');";
                    command.Parameters.AddWithValue("$id", proposal);
                    command.ExecuteNonQuery();
                }
                var inputs = new FrozenReviewInputs(key, proposal, "scope", "manifest", null, [], [],
                    [new ReviewPlannedCase("case", "word-key", "word", "ws", null, [], []),
                        new ReviewPlannedCase("case-two", "word-key-two", "word-two", "ws", null, [], [])]);
                var digest = "sha256:" + new string('a', 64);
                var baseline = new BaselineToken(key, digest, "v1", "2026-01-01T00:00:00Z", digest);
                var reference = ReviewInputArtifacts.Store(database, new ReviewInputArtifact(inputs, new Dictionary<string, byte[]>()) { Baseline = baseline });
                var proposalJson = JsonSerializer.Serialize(new { contractVersions = new Dictionary<string, string>(), proposalId = proposal, operations = Array.Empty<object>() });
                var input = new TrialJobInput(proposalJson, null, ReviewInputs: reference);
                const string job = "job/cli-review";
                new JobRepository(database).RecordInterrupted(job, key, "trial", JsonSerializer.Serialize(input, MotifJson.CreateOptions()),
                    "{}", "2026-01-01T00:00:00Z", JobStatus.Failed);
                TrialReviewEvidence.Save(database, new TrialReviewLedger(job, reference, [], [], []));
                var id = new TrialReviewPublisher(database).PublishAsync(new ReviewPublishRequest(job)).GetAwaiter().GetResult();
                return CommandOutcome<string>.Success(id.Value);
            });
            Assert.True(reportId.Succeeded, reportId.Refusal?.Message);
            async Task<JsonDocument> Read(params string[] options)
            {
                var start = new ProcessStartInfo(BuildOutput.Cli)
                { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
                foreach (var arg in new[] { "proposal", "trial", "--project", project, proposal, "--report", reportId.Value!, "--json" }.Concat(options))
                    start.ArgumentList.Add(arg);
                start.Environment["MOTIF_DEVELOPER_COMMANDS"] = "1";
                using var process = Process.Start(start)!;
                var output = process.StandardOutput.ReadToEndAsync();
                var error = process.StandardError.ReadToEndAsync();
                await Task.WhenAll(output, error, process.WaitForExitAsync()).WaitAsync(TimeSpan.FromSeconds(60));
                Assert.True(process.ExitCode == 0, await error);
                return JsonDocument.Parse(await output);
            }
            using var summary = await Read();
            using var full = await Read("--detail", "full");
            var first = summary.RootElement.GetProperty("report");
            var second = full.RootElement.GetProperty("report");
            Assert.Equal(first.GetProperty("summary").GetRawText(), second.GetProperty("summary").GetRawText());
            Assert.Equal("case", second.GetProperty("page").GetProperty("items")[0].GetProperty("caseKey").GetString());
            Assert.Equal(2, second.GetProperty("page").GetProperty("totalMatching").GetInt32());
            using var pageOne = await Read("--detail", "full", "--limit", "1");
            var cursor = pageOne.RootElement.GetProperty("report").GetProperty("page").GetProperty("nextCursor").GetString();
            Assert.NotNull(cursor);
            using var pageTwo = await Read("--detail", "full", "--limit", "1", "--cursor", cursor!);
            Assert.Equal("case-two", pageTwo.RootElement.GetProperty("report").GetProperty("page")
                .GetProperty("items")[0].GetProperty("caseKey").GetString());
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData("--job", "job/one", "--report", "report/one")]
    [InlineData("--word", "word", "--detail", "summary")]
    [InlineData("--filter", "changed", "--detail", "summary")]
    [InlineData("--word", "word", "--case", "case/one")]
    public async Task InvalidSelectorCombinationsDoNoWork(string first, string firstValue, string second, string secondValue)
    {
        var start = new ProcessStartInfo(BuildOutput.Cli)
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (var argument in new[] { "proposal", "trial", "--project", "missing.fwdata", "proposal/one",
                     first, firstValue, second, secondValue }) start.ArgumentList.Add(argument);
        start.Environment["MOTIF_DEVELOPER_COMMANDS"] = "1";
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await Task.WhenAll(output, error, process.WaitForExitAsync()).WaitAsync(TimeSpan.FromSeconds(60));
        Assert.NotEqual(0, process.ExitCode);
        Assert.DoesNotContain("Project file not found", await output + await error, StringComparison.Ordinal);
    }
}
