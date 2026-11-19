using System.Diagnostics;
using System.Text.Json;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Review;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.HandAnalysis;
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
        ReviewAssertion[]? assertions = null, string[]? expectedLosses = null, bool declaredGate = false,
        GainGatePolicy? gainGate = null, string[]? blockOn = null, bool addsObject = false)
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
            var inputs = new FrozenReviewInputs(key, proposal, "scope", "manifest", null,
                declaredGate ? [new ReviewSetBinding("gold", "Gold", "regression", "members", "expectations", "source", [])] : [], [],
                words.Select(word => new ReviewPlannedCase("case-" + word, "key-" + word, word, "ws", null,
                    declaredGate ? [new ReviewMembership("gold", "case-" + word, "regression", 1, null, null, [])] : [], assertions ?? [])).ToArray())
            {
                GainGate = gainGate, ParsimonyBlockOn = blockOn,
                DeclaredGate = declaredGate ? new TrialGateDeclaration("policy", "scope",
                    [new TrialGateSet("gold", "source", "members", "expectations")]) : null,
            };
            var digest = "sha256:" + new string('a', 64);
            var baseline = new BaselineToken(key, digest, "v1", "2026-01-01T00:00:00Z", digest);
            var reference = ReviewInputArtifacts.Store(database, new ReviewInputArtifact(inputs, new Dictionary<string, byte[]>()) { Baseline = baseline });
            var proposalJson = JsonSerializer.Serialize(new { contractVersions = addsObject
                    ? new Dictionary<string, string> { ["lexical"] = "1.0" } : new Dictionary<string, string>(), proposalId = proposal,
                operations = addsObject ? new object[] { new { operationId = CanonicalId.Mint("x/").Value,
                    kind = "lexical/lexDb/createEntries", entityId = CanonicalId.Mint("x/").Value, after = new { } } } : Array.Empty<object>() });
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

    private static ParseWordEvidence AnalysedAs(string word, string form, bool signed = false)
    {
        var reading = new ParseAnalysis([new ParseMorph(form, Msa, null, null)]);
        if (signed)
        {
            var formId = CanonicalId.FromGuid(Guid.Parse(form)).Value;
            var msaId = CanonicalId.FromGuid(Guid.Parse(Msa)).Value;
            reading = reading with { Signature = new ReadingSignature([new ReadingMorpheme(formId, null, [],
                ReadingSenseStatus.Unresolved, msaId, formId, null, true, null, [])]) };
        }
        return Analysed(word) with { Analyses = [reading] };
    }

    private static ParseWordEvidence TimedOut(string word) => new("fieldworks-parse-analysis/v1", 0, word, 0, false, true, false, [], []);

    private static string OutcomeOf(ParseWordEvidence? evidence) =>
        evidence is { InvalidShape: true } ? "skipped" : evidence is { TimedOut: true } ? "timed-out" : evidence is { Analyses.Count: > 0 } ? "analysed" : "no-analysis";

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
    [InlineData(false)]
    [InlineData(true)]
    public void InventoryRejectionOfAPreviouslyParsedWordIsANamedLoss(bool expected)
    {
        var gate = Gate(["sol"], [Analysed("sol")], [NoAnalysis("sol") with { InvalidShape = true }],
            expected ? ["sol"] : [], [Approved(Form)]);
        Assert.Equal(expected ? TrialGateState.Green : TrialGateState.Loss, gate.State);
        Assert.Equal(1, gate.LostWords);
        Assert.Equal(new[] { "sol" }, expected ? gate.ExpectedLosses : gate.UnexpectedLosses);
        Assert.Empty(gate.StoppedWords);
    }

    [Fact]
    public void MalformedInventoryRejectionCannotProveAnExpectedLoss()
    {
        var gate = Gate(["sol"], [Analysed("sol")], [NoAnalysis("wrong-word") with { InvalidShape = true }], ["sol"]);
        Assert.Equal(TrialGateState.Unmeasured, gate.State);
        Assert.Equal(0, gate.LostWords);
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
    public void ASavedTrialNamesTheCanonicalReadingsAndCopiesRetiredFromEachWaivedWord()
    {
        const string other = "22222222-2222-2222-2222-222222222222";
        var reader = new SIL.Motif.Host.PanGloss.ReadingSignatureReader(null);
        ParseAnalysis Backed(string form) => reader.Capture(new ParseAnalysis([new ParseMorph(form, Msa, null, null)]))
            with { MorphemeAcceptance = [ReadingAcceptance.LexiconBacked] };
        ParseWordEvidence Parsed(string word, params ParseAnalysis[] readings) =>
            new("fieldworks-parse-analysis/v1", 0, word, 0, false, false, false, readings, []);
        var a = Backed(Form);
        var b = Backed(other);
        var seed = SeedTrial(_ => (
            [new("case-cat", "cat", "analysed", Parsed("cat", a, a)), new("case-dog", "dog", "analysed", Parsed("dog", a, b)),
                new("case-fox", "fox", "analysed", Parsed("fox", a, b))],
            [new("case-cat", "cat", "analysed", Parsed("cat", a)), new("case-dog", "dog", "analysed", Parsed("dog", a)),
                new("case-fox", "fox", "analysed", Parsed("fox", a, b))]), ["cat", "dog", "fox"],
            expectedLosses: ["cat", "dog"]);
        try
        {
            var read = TrialReviewReads.ReadJob(seed.Project, MotifProductVersion.CurrentText, seed.Proposal, seed.Job);
            Assert.True(read.Succeeded, read.Refusal?.Message);
            using var output = new StringWriter();
            using var error = new StringWriter();
            var exit = SIL.Motif.Cli.Program.Run(["proposal", "trial", "--project", seed.Project, seed.Proposal, "--json"],
                output, error, new Dictionary<string, string?>(StringComparer.Ordinal), static (_, _) => { });
            Assert.True(exit == 0, error.ToString());
            using var json = JsonDocument.Parse(output.ToString());
            var report = json.RootElement.GetProperty("report");
            var losses = report.GetProperty("losses");
            var rows = losses.GetProperty("newlyLostReadings").EnumerateArray().ToArray();
            Assert.Equal(["cat", "dog"], rows.Select(row => row.GetProperty("word").GetString()));
            Assert.Equal(1, rows[0].GetProperty("count").GetInt64());
            Assert.Equal(1, rows[1].GetProperty("count").GetInt64());
            Assert.Equal(1, rows[0].GetProperty("remainingReadingCount").GetInt64());
            Assert.Equal(a.Signature!.Id, rows[0].GetProperty("readingIdentity").GetString());
            Assert.Equal(b.Signature!.Id, rows[1].GetProperty("readingIdentity").GetString());
            Assert.Equal(a.Signature.Morphemes[0].AllomorphId, rows[0].GetProperty("signature").GetProperty("morphemes")[0].GetProperty("allomorphId").GetString());
            var attestation = losses.GetProperty("retirementAttestation");
            Assert.True(attestation.GetProperty("complete").GetBoolean());
            Assert.Equal(report.GetProperty("reportDigest").GetString(), attestation.GetProperty("reportDigest").GetString());
            Assert.Equal(2, attestation.GetProperty("lostCopies").GetInt64());
            Assert.Equal(["cat", "dog"], attestation.GetProperty("waivedWords").EnumerateArray().Select(item => item.GetString()));
            var saved = Assert.IsType<AvailableTrialResult>(
                TrialReviewReads.ReadStored(seed.Project, new ReviewReadRequest("project", seed.Proposal)).Value);
            Assert.Empty(saved.Gate!.UnexpectedLosses);
            Assert.NotEqual(TrialGateState.Loss, saved.Gate.State);
            Assert.Contains("Retirement multiset of 2 waived word(s): complete; 2 lost copies",
                SIL.Motif.Cli.Rendering.TrialReviewTextRenderer.Render(saved));
            Assert.Contains("; 1 lost, 1 lexicon-backed remaining", SIL.Motif.Cli.Rendering.TrialReviewTextRenderer.Render(saved));
        }
        finally { Directory.Delete(seed.Root, recursive: true); }
    }

    private static JsonElement RunTrialJson(string project, string proposal, params string[] extra)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exit = SIL.Motif.Cli.Program.Run(["proposal", "trial", "--project", project, proposal, .. extra, "--json"],
            output, error, new Dictionary<string, string?>(StringComparer.Ordinal), static (_, _) => { });
        Assert.True(exit == 0, error + output.ToString());
        return JsonDocument.Parse(output.ToString()).RootElement.Clone();
    }

    [Theory]
    [InlineData("capped-after", "case-incomplete")]
    [InlineData("unsignatured", "reading-signature-unavailable")]
    [InlineData("unmeasured-waived", "waived-word-not-measured")]
    public void AnIncompleteRetirementAttestationSaysSoAndNamesWhy(string scenario, string reason)
    {
        var reader = new SIL.Motif.Host.PanGloss.ReadingSignatureReader(null);
        var a = reader.Capture(new ParseAnalysis([new ParseMorph(Form, Msa, null, null)]))
            with { MorphemeAcceptance = [ReadingAcceptance.LexiconBacked] };
        ParseWordEvidence Parsed(string word, params ParseAnalysis[] readings) =>
            new("fieldworks-parse-analysis/v1", 0, word, 0, false, false, false, readings, []);
        var bare = new ParseAnalysis([new ParseMorph(Form, Msa, null, null)]);
        var seed = SeedTrial(_ => (
            [new("case-cat", "cat", "analysed", Parsed("cat", scenario == "unsignatured" ? bare : a))],
            [new("case-cat", "cat", scenario == "capped-after" ? "capped" : "no-analysis",
                scenario == "capped-after" ? Parsed("cat", a) with { Capped = true } : Parsed("cat"))]), ["cat"],
            expectedLosses: scenario == "unmeasured-waived" ? ["cat", "ghost"] : ["cat"]);
        try
        {
            Assert.True(TrialReviewReads.ReadJob(seed.Project, MotifProductVersion.CurrentText, seed.Proposal, seed.Job).Succeeded);
            var attestation = RunTrialJson(seed.Project, seed.Proposal).GetProperty("report").GetProperty("losses")
                .GetProperty("retirementAttestation");
            Assert.False(attestation.GetProperty("complete").GetBoolean());
            Assert.Contains(reason, attestation.GetProperty("limitations").EnumerateArray().Select(item => item.GetString()));
            var saved = Assert.IsType<AvailableTrialResult>(
                TrialReviewReads.ReadStored(seed.Project, new ReviewReadRequest("project", seed.Proposal)).Value);
            Assert.Contains("incomplete (", SIL.Motif.Cli.Rendering.TrialReviewTextRenderer.Render(saved));
            Assert.Contains(reason, SIL.Motif.Cli.Rendering.TrialReviewTextRenderer.Render(saved));
        }
        finally { Directory.Delete(seed.Root, recursive: true); }
    }

    [Fact]
    public void RetirementRowsPageByByteBoundWithoutRefusalAndEveryPageReachesTheWholeMultiset()
    {
        var reader = new SIL.Motif.Host.PanGloss.ReadingSignatureReader(null);
        var a = reader.Capture(new ParseAnalysis([new ParseMorph(Form, Msa, null, null)]))
            with { MorphemeAcceptance = [ReadingAcceptance.LexiconBacked] };
        ParseWordEvidence Parsed(string word, params ParseAnalysis[] readings) =>
            new("fieldworks-parse-analysis/v1", 0, word, 0, false, false, false, readings, []);
        var words = Enumerable.Range(0, 150).Select(index => "w" + index.ToString("D4")).ToArray();
        var seed = SeedTrial(_ => (
            words.Select(word => new TrialReviewObservation("case-" + word, word, "analysed", Parsed(word, a))).ToArray(),
            words.Select(word => new TrialReviewObservation("case-" + word, word, "no-analysis", Parsed(word))).ToArray()), words,
            expectedLosses: words);
        try
        {
            Assert.True(TrialReviewReads.ReadJob(seed.Project, MotifProductVersion.CurrentText, seed.Proposal, seed.Job).Succeeded);
            var firstRead = RunTrialJson(seed.Project, seed.Proposal);
            var first = firstRead.GetProperty("report").GetProperty("losses");
            var attestation = first.GetProperty("retirementAttestation");
            Assert.True(attestation.GetProperty("complete").GetBoolean());
            Assert.Equal(150, attestation.GetProperty("rowCount").GetInt64());
            Assert.Equal(150, attestation.GetProperty("lostCopies").GetInt64());
            var pages = attestation.GetProperty("pageCount").GetInt64();
            Assert.True(pages > 1, "150 signature rows must not all travel inline.");
            Assert.Equal(1, attestation.GetProperty("page").GetInt64());
            var seen = new List<string>();
            for (var page = 1; page <= pages; page++)
            {
                var read = page == 1 ? firstRead : RunTrialJson(seed.Project, seed.Proposal, "--page", page.ToString());
                var rows = read.GetProperty("report").GetProperty("losses").GetProperty("newlyLostReadings").EnumerateArray().ToArray();
                Assert.Equal(page, read.GetProperty("report").GetProperty("losses").GetProperty("retirementAttestation")
                    .GetProperty("page").GetInt64());
                Assert.InRange(rows.Sum(row => row.GetRawText().Length), 1, 20_000);
                seen.AddRange(rows.Select(row => row.GetProperty("word").GetString()!));
            }
            Assert.Equal(words, seen);
            using var output = new StringWriter();
            using var error = new StringWriter();
            var beyond = SIL.Motif.Cli.Program.Run(["proposal", "trial", "--project", seed.Project, seed.Proposal, "--page",
                (pages + 1).ToString(), "--json"], output, error,
                new Dictionary<string, string?>(StringComparer.Ordinal), static (_, _) => { });
            Assert.NotEqual(0, beyond);
            Assert.Contains("difference.invalid-page", output.ToString() + error);
            var saved = Assert.IsType<AvailableTrialResult>(
                TrialReviewReads.ReadStored(seed.Project, new ReviewReadRequest("project", seed.Proposal)).Value);
            Assert.Contains("page 1 of " + pages, SIL.Motif.Cli.Rendering.TrialReviewTextRenderer.Render(saved));
        }
        finally { Directory.Delete(seed.Root, recursive: true); }
    }

    [Theory]
    [InlineData(false, GainGateMode.Hold, false, TrialGateState.Green)]
    [InlineData(true, GainGateMode.Hold, false, TrialGateState.Unmeasured)]
    [InlineData(true, GainGateMode.Advisory, false, TrialGateState.Green)]
    [InlineData(true, GainGateMode.Off, false, TrialGateState.Green)]
    [InlineData(true, GainGateMode.Advisory, true, TrialGateState.Unmeasured)]
    public void ADeclaredGateReusesExistingGainAndParsimonyPolicies(bool declared, GainGateMode mode,
        bool parsimony, TrialGateState expected)
    {
        var seed = SeedTrial(_ => ([new("case-word", "word", "analysed", Analysed("word"))],
            [new("case-word", "word", "analysed", Analysed("word"))]), ["word"],
            declaredGate: declared, gainGate: new GainGatePolicy(mode, 1.5),
            blockOn: parsimony ? ["R-word-negative-accepted"] : [], addsObject: true);
        try
        {
            var read = TrialReviewReads.ReadJob(seed.Project, MotifProductVersion.CurrentText, seed.Proposal, seed.Job);
            Assert.True(read.Succeeded, read.Refusal?.Message);
            var result = Assert.IsType<AvailableTrialResult>(read.Value);
            Assert.Equal(expected, result.Gate!.State);
            Assert.Equal(mode == GainGateMode.Hold, result.Report.Summary.Gains!.Gate.Hold);
        }
        finally { Directory.Delete(seed.Root, recursive: true); }
    }

    [Theory]
    [InlineData(1, true, false, true)]
    [InlineData(5100, true, false, true)]
    [InlineData(1, false, false, true)]
    [InlineData(1, true, true, true)]
    [InlineData(1, true, false, false)]
    public void ANewSecondReadingOnAnAttestedWordIsHeldUntilReviewed(int count, bool attested, bool reviewed, bool signed)
    {
        var words = Enumerable.Range(0, count).Select(index => "word-" + index.ToString("D4")).ToArray();
        var first = AnalysedAs(words[0], Form, signed);
        var second = AnalysedAs(words[0], OtherForm, signed).Analyses[0];
        ParseWordEvidence Before(string word) => first with { Word = word };
        var seed = SeedTrial(_ => (
            words.Select(word => new TrialReviewObservation("case-" + word, word, "analysed", Before(word))).ToArray(),
            words.Select(word => new TrialReviewObservation("case-" + word, word, "analysed", word == words[0]
                ? first with { Analyses = [first.Analyses[0], second] } : Before(word))).ToArray()), words,
            attested ? reviewed ? [Approved(Form), Approved(OtherForm)] : [Approved(Form)] : []);
        try
        {
            var read = TrialReviewReads.ReadJob(seed.Project, MotifProductVersion.CurrentText, seed.Proposal, seed.Job);
            Assert.True(read.Succeeded, read.Refusal?.Message);
            var result = Assert.IsType<AvailableTrialResult>(read.Value);
            var held = attested && !reviewed;
            Assert.Equal(held ? TrialGateState.Unmeasured : TrialGateState.Green, result.Gate!.State);
            if (count == 5100) Assert.Equal(DetailAvailability.Unavailable, result.Report.DetailAvailability);
            var saved = TrialReviewReads.ReadStored(seed.Project, new ReviewReadRequest("", seed.Proposal));
            var available = Assert.IsType<AvailableTrialResult>(saved.Value);
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(available, ReviewJson.Options));
            Assert.Equal(held && signed ? new[] { words[0] } : Array.Empty<string>(), json.RootElement.GetProperty("report")
                .GetProperty("heldWords").EnumerateArray().Select(item => item.GetString()));
            if (held && signed) Assert.Contains(words[0], SIL.Motif.Cli.Rendering.TrialReviewTextRenderer.Render(available));
            if (held && !signed)
            {
                Assert.Contains("identity", result.Gate.Reason);
                Assert.Equal(result.Gate.State, available.Gate!.State);
                Assert.Equal(result.Gate.Reason, available.Gate.Reason);
            }
        }
        finally { Directory.Delete(seed.Root, recursive: true); }
    }

    [Theory]
    [InlineData("duplicate-candidate", TrialGateState.Green)]
    [InlineData("duplicate-unsigned-candidate", TrialGateState.Green)]
    [InlineData("duplicate-mixed-candidate", TrialGateState.Green)]
    [InlineData("distinct-baseline", TrialGateState.Green)]
    [InlineData("distinct-mixed-baseline", TrialGateState.Green)]
    [InlineData("known-reading", TrialGateState.Green)]
    [InlineData("possible-new-reading", TrialGateState.Unmeasured)]
    public void AttestedGateUsesDistinctReadingsWithIncompleteIdentity(string scenario, TrialGateState expected)
    {
        const string word = "word";
        var known = AnalysedAs(word, Form, signed: true).Analyses[0];
        var other = AnalysedAs(word, OtherForm, signed: true).Analyses[0];
        var unsigned = known with { Signature = null };
        var unsignedOther = other with { Signature = null };
        (ParseAnalysis[] Before, ParseAnalysis[] After, ReviewAssertion Approval) evidence = scenario switch
        {
            "duplicate-candidate" => ([unsigned], [other, other], Approved(Msa)),
            "duplicate-unsigned-candidate" => ([unsigned], [unsignedOther, unsignedOther], Approved(Msa)),
            "duplicate-mixed-candidate" => ([unsigned], [other, unsignedOther], Approved(Msa)),
            "distinct-baseline" => ([known, other], [known, unsignedOther], Approved(Form)),
            "distinct-mixed-baseline" => ([known, unsignedOther], [known, unsignedOther], Approved(Form)),
            "known-reading" => ([known, unsigned], [known, other], Approved(OtherForm)),
            "possible-new-reading" => ([known, unsigned], [known, unsignedOther], Approved(Form)),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
        TrialReviewObservation Observe(ParseAnalysis[] readings) =>
            new("case-" + word, word, "analysed", Analysed(word) with { Analyses = readings });
        var seed = SeedTrial(_ => ([Observe(evidence.Before)], [Observe(evidence.After)]), [word], [evidence.Approval]);
        try
        {
            var read = TrialReviewReads.ReadJob(seed.Project, MotifProductVersion.CurrentText, seed.Proposal, seed.Job);
            Assert.True(read.Succeeded, read.Refusal?.Message);
            var result = Assert.IsType<AvailableTrialResult>(read.Value);
            Assert.Equal(expected, result.Gate!.State);
            Assert.Empty(result.Report.HeldWords!);
            Assert.Contains(result.Report.Limitations, limitation => limitation.Code == "reading-identity-unavailable");
        }
        finally { Directory.Delete(seed.Root, recursive: true); }
    }

    [Theory]
    [InlineData("timed-out", "time-limit")]
    [InlineData("capped", "work-limit")]
    [InlineData("memory-capped", "memory-limit")]
    [InlineData("skipped", null)]
    public void AttributedStopsWithoutMorphologySurviveTheDetailBudget(string outcome, string? reason)
    {
        var words = Enumerable.Range(0, 5100).Select(index => "word-" + index.ToString("D4")).ToArray();
        var seed = SeedTrial(_ => (
            words.Select(word => new TrialReviewObservation("case-" + word, word, "analysed", Analysed(word))).ToArray(),
            words.Skip(1).Select(word => new TrialReviewObservation("case-" + word, word, "analysed", Analysed(word)))
                .Select(item => item.Word == words[1] ? item with { Outcome = outcome, Morphology = null } : item)
                .ToArray()), words);
        try
        {
            var read = TrialReviewReads.ReadJob(seed.Project, MotifProductVersion.CurrentText, seed.Proposal, seed.Job);
            Assert.True(read.Succeeded, read.Refusal?.Message);
            var result = Assert.IsType<AvailableTrialResult>(read.Value);
            Assert.Equal(DetailAvailability.Unavailable, result.Report.DetailAvailability);
            var stopped = Assert.Single(result.Report.StoppedWords!);
            Assert.Equal(words[1], stopped.Word);
            Assert.Equal(outcome, stopped.Outcome);
            Assert.Equal(reason is null ? Array.Empty<string>() : new[] { reason }, stopped.Reasons);
            Assert.Equal(new[] { words[1] }, result.Gate!.StoppedWords);
            Assert.Equal(TrialGateState.Unmeasured, result.Gate.State);
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
    public void LegacyLossCountsWithoutRetainedNamesAreExplicitlyUnmeasured()
    {
        var seed = SeedTrial(_ => ([new("case-word", "word", "analysed", Analysed("word"))],
            [new("case-word", "word", "no-analysis", NoAnalysis("word"))]), ["word"]);
        try
        {
            var read = TrialReviewReads.ReadJob(seed.Project, MotifProductVersion.CurrentText, seed.Proposal, seed.Job);
            var report = Assert.IsType<AvailableTrialResult>(read.Value).Report with { Gate = null, Losses = null };
            var gate = TrialReviewReads.GateOf(new AvailableTrialResult(report), [], new(null, null));
            Assert.Equal(TrialGateState.Unmeasured, gate.State);
            Assert.Contains("rerun", gate.Reason!, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("unavailable", gate.Reason!, StringComparison.OrdinalIgnoreCase);
        }
        finally { Directory.Delete(seed.Root, recursive: true); }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LegacyBudgetDroppedReportsRequireAnExplicitRebuildOrRerun(bool retained)
    {
        var words = Enumerable.Range(0, 5100).Select(index => "word-" + index.ToString("D4")).ToArray();
        var seed = SeedTrial(_ => (
            words.Select(word => new TrialReviewObservation("case-" + word, word, "analysed", Analysed(word))).ToArray(),
            words.Select(word => new TrialReviewObservation("case-" + word, word, "complete",
                word == words[0] ? NoAnalysis(word) : Analysed(word))).ToArray()), words);
        try
        {
            var first = TrialReviewReads.ReadJob(seed.Project, MotifProductVersion.CurrentText, seed.Proposal, seed.Job);
            var available = Assert.IsType<AvailableTrialResult>(first.Value);
            using var database = ProjectMotifDatabase.Open(seed.Project);
            var legacy = available.Report with { Gate = null, Losses = null, HeldWords = null };
            var json = JsonSerializer.Serialize(legacy, ReviewJson.Options);
            using (var connection = database.OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "UPDATE ReviewReports SET SummaryJson=$json,SummaryDigest=$hash,ManifestDigest='legacy' WHERE ReportId=$id;";
                command.Parameters.AddWithValue("$json", json);
                command.Parameters.AddWithValue("$hash", Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(json))));
                command.Parameters.AddWithValue("$id", available.Report.ReportId);
                command.ExecuteNonQuery();
            }
            var read = TrialReviewReads.ReadJob(seed.Project, MotifProductVersion.CurrentText, seed.Proposal, seed.Job);
            Assert.True(read.Succeeded, read.Refusal?.Message);
            var old = Assert.IsType<AvailableTrialResult>(read.Value);
            Assert.Equal(TrialGateState.Unmeasured, old.Gate!.State);
            Assert.Contains("rerun", old.Gate.Reason!, StringComparison.OrdinalIgnoreCase);
            if (!retained)
                foreach (var file in Directory.GetFiles(database.FullPath + ".review-inputs")) File.Delete(file);
            var key = ProjectWorkspaceKey.Compute(new SIL.Motif.Contract.Projects.ProjectLocator(seed.Project, "project"));
            var rebuilt = await new TrialReviewCommands(database).ProduceAsync(new ReviewProduceRequest(key, seed.Proposal, seed.Job, null));
            if (retained)
            {
                Assert.True(rebuilt.Succeeded, rebuilt.Refusal?.Message);
                var fresh = Assert.IsType<AvailableTrialResult>(rebuilt.Value);
                Assert.Equal(TrialGateState.Loss, fresh.Gate!.State);
                Assert.Equal(new[] { words[0] }, fresh.Gate.UnexpectedLosses);
            }
            else Assert.False(rebuilt.Succeeded);
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

    [Theory]
    [InlineData("timed-out")]
    [InlineData("skipped")]
    public void AnUnavailableBaselineObservationCannotAuthorizeADeclaredGate(string outcome)
    {
        var seed = SeedTrial(_ => ([new("case-word", "word", outcome, null)],
            [new("case-word", "word", "analysed", Analysed("word"))]), ["word"], declaredGate: true);
        try
        {
            var result = TrialReviewReads.ReadJob(seed.Project, MotifProductVersion.CurrentText, seed.Proposal, seed.Job);
            Assert.True(result.Succeeded, result.Refusal?.Message);
            var available = Assert.IsType<AvailableTrialResult>(result.Value);
            Assert.Equal(TrialGateState.Unmeasured, available.Gate!.State);
            Assert.Contains(available.Report.StoppedWords!, item => item.Word == "word" && item.Side == Side.Before);
        }
        finally { Directory.Delete(seed.Root, recursive: true); }
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
                var proposalJson = JsonSerializer.Serialize(new { contractVersions = new Dictionary<string, string>(), proposalId = proposal,
                operations = Array.Empty<object>() });
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
