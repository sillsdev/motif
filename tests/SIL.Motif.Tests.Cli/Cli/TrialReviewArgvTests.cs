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
        Func<string, (TrialReviewObservation[] Before, TrialReviewObservation[] After)> observe, string[] words)
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
                words.Select(word => new ReviewPlannedCase("case-" + word, "key-" + word, word, "ws", null, [], [])).ToArray());
            var digest = "sha256:" + new string('a', 64);
            var baseline = new BaselineToken(key, digest, "v1", "2026-01-01T00:00:00Z", digest);
            var reference = ReviewInputArtifacts.Store(database, new ReviewInputArtifact(inputs, new Dictionary<string, byte[]>()) { Baseline = baseline });
            var proposalJson = JsonSerializer.Serialize(new { contractVersions = new Dictionary<string, string>(), proposalId = proposal, operations = Array.Empty<object>() });
            var input = new TrialJobInput(proposalJson, null, ReviewInputs: reference);
            new JobRepository(database).RecordInterrupted(job, key, "trial", JsonSerializer.Serialize(input, MotifJson.CreateOptions()),
                "{}", "2026-01-01T00:00:00Z", JobStatus.Failed);
            var (before, after) = observe("");
            TrialReviewEvidence.Save(database, new TrialReviewLedger(job, reference, before, after, []));
            return CommandOutcome<string>.Success("seeded");
        });
        Assert.True(seeded.Succeeded, seeded.Refusal?.Message);
        return (project, proposal, job, root);
    }

    private static TrialGate Gate(string[] words, ParseWordEvidence?[] before, ParseWordEvidence?[] after,
        string[]? expected = null)
    {
        var seed = SeedTrial(_ => (
            words.Select((word, i) => new TrialReviewObservation("case-" + word, word,
                before[i] is { Analyses.Count: > 0 } ? "analysed" : "no-analysis", before[i])).Where(o => o.Morphology is not null).ToArray(),
            words.Select((word, i) => new TrialReviewObservation("case-" + word, word,
                after[i] is { Analyses.Count: > 0 } ? "analysed" : "no-analysis", after[i])).Where(o => o.Morphology is not null).ToArray()), words);
        try
        {
            var read = TrialReviewReads.ReadJob(seed.Project, MotifProductVersion.CurrentText, seed.Proposal, seed.Job, expected);
            Assert.True(read.Succeeded, read.Refusal?.Message);
            return Assert.IsType<AvailableTrialResult>(read.Value).Gate!;
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
        Assert.Equal(Program.TrialLossExitCode, Program.TrialGateExitCode(gate));
        Assert.NotEqual(0, Program.TrialGateExitCode(gate));
        Assert.NotEqual(Program.TrialGateExitCode(gate), Program.TrialGateExitCode(gate with { State = TrialGateState.Unmeasured }));
        Assert.Equal(0, Program.TrialGateExitCode(gate with { State = TrialGateState.Green }));
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
