using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.HandAnalysis;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Assess;
using SIL.Motif.Host;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Store;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Cli;

[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group0)]
public sealed class AssessRoundTripArgvTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-assess-roundtrip-" + Guid.NewGuid().ToString("N"));
    private readonly string _workerRoot;
    private readonly PristineProjectFixture _pristine;

    public AssessRoundTripArgvTests(PristineProjectFixture pristine)
    {
        _pristine = pristine;
        _workerRoot = Path.Combine(_root, "worker");
        Directory.CreateDirectory(_workerRoot);
    }

    [Fact]
    public async Task AssessJsonPreservesReadingIdentityAcrossAChangeInFeatureValue()
    {
        var project = _pristine.CopyProjectFile();
        var seed = _pristine.Seed;
        var loader = new FwDataProjectLoader();
        Guid msaId = default, otherMsaId = default, otherSenseId = default;
        Guid featureId = default, oldValueId = default, newValueId = default, specId = default;
        using (var cache = loader.LoadCache(project))
        {
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            {
                var services = cache.ServiceLocator;
                var entry = services.GetInstance<ILexEntryRepository>().GetObject(seed.FirstEntryId);
                var sense = entry.SensesOS[0];
                var msa = (IMoStemMsa)sense.MorphoSyntaxAnalysisRA;
                msaId = msa.Guid;
                var otherSense = services.GetInstance<ILexSenseFactory>().Create();
                entry.SensesOS.Add(otherSense);
                otherSense.MorphoSyntaxAnalysisRA = msa;
                otherSense.Gloss.set_String(cache.DefaultAnalWs, SeededProject.FirstGloss);
                otherSenseId = otherSense.Guid;
                var wordform = services.GetInstance<IWfiWordformFactory>()
                    .Create(TsStringUtils.MakeString(SeededProject.FirstForm, cache.DefaultVernWs));
                foreach (var selected in new[] { sense, otherSense })
                {
                    var analysis = services.GetInstance<IWfiAnalysisFactory>().Create();
                    wordform.AnalysesOC.Add(analysis);
                    var bundle = services.GetInstance<IWfiMorphBundleFactory>().Create();
                    analysis.MorphBundlesOS.Add(bundle);
                    bundle.MorphRA = entry.LexemeFormOA;
                    bundle.MsaRA = msa;
                    bundle.SenseRA = selected;
                }
                otherMsaId = services.GetInstance<ILexSenseRepository>().GetObject(seed.SecondSenseId)
                    .MorphoSyntaxAnalysisRA.Guid;

                var feature = services.GetInstance<IFsClosedFeatureFactory>().Create();
                cache.LangProject.MsFeatureSystemOA.FeaturesOC.Add(feature);
                var oldValue = services.GetInstance<IFsSymFeatValFactory>().Create();
                var newValue = services.GetInstance<IFsSymFeatValFactory>().Create();
                feature.ValuesOC.Add(oldValue);
                feature.ValuesOC.Add(newValue);
                msa.MsFeaturesOA = services.GetInstance<IFsFeatStrucFactory>().Create();
                var spec = services.GetInstance<IFsClosedValueFactory>().Create();
                msa.MsFeaturesOA.FeatureSpecsOC.Add(spec);
                spec.FeatureRA = feature;
                spec.ValueRA = oldValue;
                (featureId, oldValueId, newValueId, specId) = (feature.Guid, oldValue.Guid, newValue.Guid, spec.Guid);
            });
            loader.Save(cache);
        }

        var first = new { form = seed.FirstLexemeFormId, msa = msaId, inflType = (string?)null, guessedString = (string?)null };
        var second = first with { form = seed.SecondLexemeFormId, msa = otherMsaId };
        var parser = CopyFakeParser(new
        {
            words = new[]
            {
                new { word = SeededProject.FirstForm, outcome = "complete", analyses = new[]
                {
                    new { morphs = new[] { first, second } },
                    new { morphs = new[] { first, second } },
                    new { morphs = new[] { second, first } },
                    new { morphs = new[] { first with { form = Guid.NewGuid() } } },
                } },
            },
        });
        var words = Path.Combine(_root, "identity-words.txt");
        File.WriteAllText(words, SeededProject.FirstForm + "\n");

        async Task<ReadingSignature?[]> Assess()
        {
            await CaptureBaseline(project);
            var result = await CliInProcess.RunAsync(_workerRoot, parser, false, "assess", project, "--words", words, "--json");
            Assert.True(result.ExitCode == 0, result.FailureDetails);
            using var json = JsonDocument.Parse(result.Output);
            var word = json.RootElement.GetProperty("words")[0];
            var readings = word.GetProperty("readings").EnumerateArray()
                .Select((reading, index) => (Index: reading.TryGetProperty("sourceIndex", out var source)
                    ? source.GetInt32() : index, Signature: reading.GetProperty("signature").Deserialize<ReadingSignature>()))
                .OrderBy(reading => reading.Index).Select(reading => reading.Signature).ToArray();
            var stored = word.GetProperty("storedAnalyses").EnumerateArray().ToArray();
            Assert.Equal(2, stored.Length);
            Assert.Equal(stored[0].GetProperty("morphs")[0].GetProperty("gloss").GetString(),
                stored[1].GetProperty("morphs")[0].GetProperty("gloss").GetString());
            var native = stored.Select(reading => reading.GetProperty("signature").Deserialize<ReadingSignature>()!).ToArray();
            Assert.NotEqual(native[0], native[1]);
            Assert.Equal(new[] { Id(seed.FirstSenseId), Id(otherSenseId) }.Order(StringComparer.Ordinal),
                native.Select(reading => reading.SenseId).Order(StringComparer.Ordinal));
            return readings;
        }

        var before = await Assess();
        using (var cache = loader.LoadCache(project))
        {
            var objects = cache.ServiceLocator.ObjectRepository;
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
                ((IFsClosedValue)objects.GetObject(specId)).ValueRA = (IFsSymFeatVal)objects.GetObject(newValueId));
            loader.Save(cache);
        }
        var after = await Assess();

        Assert.Equal(4, before.Length);
        Assert.Equal(4, after.Length);
        Assert.Null(before[3]);
        Assert.Null(after[3]);
        Assert.Equal(before[0], before[1]);
        Assert.Equal(after[0], after[1]);
        Assert.NotEqual(before[0], before[2]);
        Assert.NotEqual(before[0], after[0]);
        var morph = Assert.IsType<ReadingSignature>(before[0]).Morphemes[0];
        Assert.Equal(Id(seed.FirstEntryId), morph.EntryId);
        Assert.Equal(Id(seed.FirstLexemeFormId), morph.AllomorphId);
        Assert.Equal(Id(msaId), morph.MsaId);
        Assert.Equal(ReadingSenseStatus.Ambiguous, morph.SenseStatus);
        Assert.Null(morph.SenseId);
        Assert.Equal(new[] { Id(seed.FirstSenseId), Id(otherSenseId) }.Order(StringComparer.Ordinal), morph.SenseCandidates);
        Assert.Equal(Id(seed.SecondSenseId), before[0]!.Morphemes[1].SenseId);
        Assert.Empty(before[0]!.Morphemes[1].FeatureValues);
        var featureValue = Assert.Single(morph.FeatureValues);
        Assert.Equal(ReadingFeatureRole.Stem, featureValue.Role);
        Assert.Equal(Id(featureId), Assert.Single(featureValue.FeaturePath));
        Assert.Equal(Id(oldValueId), Assert.Single(featureValue.ValueIds));
        Assert.Equal(Id(newValueId), Assert.Single(Assert.Single(after[0]!.Morphemes[0].FeatureValues).ValueIds));

        static string Id(Guid guid) => CanonicalId.FromGuid(guid).Value;
    }

    [Theory]
    [InlineData("fail", "", "assess.parser-killed")]
    [InlineData("success", "", "assess.parser-killed")]
    [InlineData("fail", "memory allocation of 4096 bytes failed", "assess.out-of-memory")]
    public async Task AssessReportsAKilledParserAsARefusalAndStoresNothing(
        string mode, string standardError, string expectedCode)
    {
        var project = _pristine.CopyProjectFile();
        await CaptureBaseline(project);
        var memory = expectedCode == "assess.out-of-memory";
        var exitCode = memory ? 1 : OperatingSystem.IsWindows() ? -1 : 137;
        var parser = CopyFakeParser(new
        {
            mode = !memory && mode == "fail" ? "killed" : mode,
            killOnExit = !memory && mode == "success", exitCode = memory ? 1 : 0, standardError,
        });
        var words = Path.Combine(_root, "killed-words.txt");
        File.WriteAllText(words, "motifa\n");

        var start = CliProcess.CreateStartInfo(_workerRoot, parser, false,
            "assess", project, "--words", words, "--json");
        start.Environment[SIL.Motif.Host.Installation.MotifUpdateGate.TestNamespaceVariable] = Guid.NewGuid().ToString("N");
        var result = await CliProcess.RunAsync(start);

        Assert.True(result.ExitCode == 2, result.Error + result.Output);
        Assert.True(string.IsNullOrWhiteSpace(result.Output), result.Output);
        var failure = ProjectionJson.Deserialize<FailureEnvelope>(result.Error)!;
        Assert.Equal(expectedCode, failure.Code);
        Assert.Equal(FailureReason.Refused, failure.Reason);
        Assert.Equal(exitCode.ToString(CultureInfo.InvariantCulture), failure.Detail!["exitCode"]);
        Assert.Equal(0L, ReadAssessmentCount(project));
    }

    [Fact]
    public async Task AssessAsJsonStoresARunThatTimingAndOverviewRead()
    {
        var project = _pristine.CopyProjectFile();
        await CaptureBaseline(project);
        var setup = CliInProcess.Run(_workerRoot, null, false, "selection", "set-default", "--project", project,
            "--name", "Default", "--add-words", "motifa", "--json");
        Assert.True(setup.ExitCode == 0, setup.FailureDetails);

        var parser = CopyFakeParser(new { words = new[] { new { word = "motifa", outcome = "complete" } } });
        var assessed = await CliInProcess.RunAsync(_workerRoot, parser, false, "assess", project, "--json");

        Assert.True(assessed.ExitCode == 0, assessed.FailureDetails);
        var response = ProjectionJson.Deserialize<AssessCommandResponse>(assessed.Output)!;
        Assert.Contains("motifa", response.Selection.Words);
        Assert.NotEmpty(response.AssessmentIds);

        var timingResult = await CliInProcess.RunAsync(_workerRoot, null, false,
            "timing", "--project", project, "--json");
        Assert.True(timingResult.ExitCode == 0, timingResult.FailureDetails);
        var timing = ProjectionJson.Deserialize<TimingResponse>(timingResult.Output)!;
        Assert.Contains(timing.AssessmentId, response.AssessmentIds);
        Assert.Contains(timing.Words, row => row.Word == "motifa");

        var overviewResult = await CliInProcess.RunAsync(_workerRoot, null, false,
            "overview", "--project", project, "--json");
        Assert.True(overviewResult.ExitCode == 0, overviewResult.FailureDetails);
        var overview = ProjectionJson.Deserialize<OverviewResponse>(overviewResult.Output)!;
        Assert.Equal(timing.AssessmentId, overview.AssessmentId);
    }

    [Fact]
    public async Task AssessWithAWordsFileMeasuresOnlyThoseWords()
    {
        var project = _pristine.CopyProjectFile();
        await CaptureBaseline(project);
        var selection = CliInProcess.Run(_workerRoot, null, false, "selection", "set-default",
            "--project", project, "--name", "Default", "--add-words", "motifanalysed", "--json");
        Assert.True(selection.ExitCode == 0, selection.FailureDetails);
        var wordsPath = Path.Combine(_root, "words.txt");
        File.WriteAllLines(wordsPath, ["motifa", "motifb"]);
        var parser = CopyFakeParser(new
        {
            words = new[]
            {
                new { word = "motifa", outcome = "complete" },
                new { word = "motifb", outcome = "complete" },
            },
        });

        var assessed = await CliInProcess.RunAsync(_workerRoot, parser, false,
            "assess", project, "--words", wordsPath, "--json");

        Assert.True(assessed.ExitCode == 0, assessed.FailureDetails);
        var response = ProjectionJson.Deserialize<AssessCommandResponse>(assessed.Output)!;
        Assert.Equal(new[] { "motifa", "motifb" }, response.Selection.Words);
        Assert.Equal(new[] { "motifa", "motifb" }, response.Words.Select(row => row.Word));
        Assert.DoesNotContain("motifanalysed", response.Words.Select(row => row.Word));
    }

    [Fact]
    public async Task AssessReplacesUpdatesOnlyTheExplicitlyNamedRunsWords()
    {
        var project = _pristine.CopyProjectFile();
        await CaptureBaseline(project);
        var setup = CliInProcess.Run(_workerRoot, null, false, "selection", "set-default", "--project", project,
            "--name", "Default", "--add-words", "motifa,motifb", "--json");
        Assert.True(setup.ExitCode == 0, setup.FailureDetails);
        var parser = CopyFakeParser(new
        {
            words = new[]
            {
                new { word = "motifa", outcome = "complete" },
                new { word = "motifb", outcome = "complete" },
            },
        });
        var first = await CliInProcess.RunAsync(_workerRoot, parser, false, "assess", project, "--json");
        Assert.True(first.ExitCode == 0, first.FailureDetails);
        var firstResponse = ProjectionJson.Deserialize<AssessCommandResponse>(first.Output)!;
        var baseId = firstResponse.Words.First().Origin!.AssessmentId;
        var wordsPath = Path.Combine(_root, "reparse.txt");
        File.WriteAllLines(wordsPath, ["motifa"]);

        var replacement = await CliInProcess.RunAsync(_workerRoot, parser, false,
            "assess", project, "--words", wordsPath, "--replaces", baseId, "--json");

        Assert.True(replacement.ExitCode == 0, replacement.FailureDetails);
        var replacementResponse = ProjectionJson.Deserialize<AssessCommandResponse>(replacement.Output)!;
        var replacementId = Assert.Single(replacementResponse.Words).Origin!.AssessmentId;
        Assert.NotEqual(baseId, replacementId);
        var timingResult = CliInProcess.Run(_workerRoot, null, false,
            "timing", "--project", project, "--json");
        Assert.True(timingResult.ExitCode == 0, timingResult.FailureDetails);
        var timing = ProjectionJson.Deserialize<TimingResponse>(timingResult.Output)!;
        Assert.Equal(baseId, timing.AssessmentId);
        Assert.Equal(replacementId, timing.Words.Single(word => word.Word == "motifa").Origin!.AssessmentId);
        Assert.Equal(baseId, timing.Words.Single(word => word.Word == "motifb").Origin!.AssessmentId);
        var history = CliInProcess.Run(_workerRoot, null, false,
            "timing", "--project", project, "--assessment", baseId, "--json");
        Assert.True(history.ExitCode == 0, history.FailureDetails);
        Assert.All(ProjectionJson.Deserialize<TimingResponse>(history.Output)!.Words,
            word => Assert.Equal(baseId, word.Origin!.AssessmentId));
    }

    [Fact]
    public async Task WholeRunLimitExitsFiveAndDoesNotStorePartialResults()
    {
        var project = _pristine.CopyProjectFile();
        await CaptureBaseline(project);
        var wordsPath = Path.Combine(_root, "budget-words.txt");
        File.WriteAllText(wordsPath, "motifa" + Environment.NewLine);
        var heartbeat = Path.Combine(_root, "budget-heartbeat.txt");
        var parserIdPath = Path.Combine(_root, "budget-parser-id.txt");
        var parser = CopyFakeParser(new { heartbeatPath = heartbeat, processIdPath = parserIdPath });
        AssessCommand.BudgetDurationForTests.Value = TimeSpan.FromSeconds(8);
        try
        {
            var result = await Task.Run(() => CliInProcess.Run(_workerRoot, parser, false,
                "assess", project, "--words", wordsPath, "--max-seconds", "60", "--json"))
                .WaitAsync(Wait.Scaled(TimeSpan.FromMinutes(30)));
            Assert.True(result.ExitCode == 5, result.FailureDetails);
            var failure = ProjectionJson.Deserialize<FailureEnvelope>(result.Error)!;
            Assert.Equal("assessment.stopped-by-budget", failure.Code);
            Assert.Equal(FailureReason.StoppedByBudget, failure.Reason);
            Assert.Equal("0", failure.Detail!["wordsDone"]);
            Assert.Equal("1", failure.Detail["wordsNotDone"]);
            Assert.True(File.Exists(heartbeat), result.FailureDetails);
            Assert.NotNull(TryReadProcessId(parserIdPath));
            await AssertProcessStopped(TryReadProcessId(parserIdPath)!.Value);
            Assert.Equal(0L, ReadAssessmentCount(project));
            Assert.Equal(0L, ReadInvocationCount(project));
        }
        finally { AssessCommand.BudgetDurationForTests.Value = null; }
    }

    [Fact]
    public async Task TrialWaitReportsBeforeBudgetStopAndLeavesOneJob()
    {
        var project = _pristine.CopyProjectFile();
        await CaptureBaseline(project);
        var draft = CliInProcess.Run(_workerRoot, null, true, "new", "--project", project, "--draft", "budget");
        Assert.True(draft.ExitCode == 0, draft.FailureDetails);
        var operation = CliInProcess.Run(_workerRoot, null, true, "add-set-gloss", "--project", project,
            "--draft", "budget", "--target", "agent_AAECAwQFBgcICQoLDA0ODw", "--ws", "en", "--text", "hello");
        Assert.True(operation.ExitCode == 0, operation.FailureDetails);
        var label = CliInProcess.Run(_workerRoot, null, true, "label", "--project", project, "--draft", "budget", "Budget Trial");
        Assert.True(label.ExitCode == 0, label.FailureDetails);
        Assert.True(DraftRationale.Write(project, MotifProductVersion.CurrentText, "budget", "a rationale").Succeeded);
        var finalized = CliInProcess.Run(_workerRoot, null, true, "finalize", "--project", project, "--draft", "budget");
        Assert.True(finalized.ExitCode == 0, finalized.FailureDetails);
        const string marker = "-> Proposal ";
        var begin = finalized.Output.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        Assert.True(begin >= marker.Length, finalized.Output);
        var proposal = finalized.Output[begin..finalized.Output.IndexOf(' ', begin)];
        var wordsPath = Path.Combine(_root, "trial-budget-words.txt");
        File.WriteAllText(wordsPath, "motifa" + Environment.NewLine);
        var heartbeat = Path.Combine(_root, "trial-budget-heartbeat");
        var parserIdPath = Path.Combine(_root, "trial-budget-parser-id");
        var parser = CopyFakeParser(new { heartbeatPath = heartbeat, processIdPath = parserIdPath });
        AssessCommand.BudgetDurationForTests.Value = TimeSpan.FromSeconds(8);
        try
        {
            var result = await Task.Run(() => CliInProcess.Run(_workerRoot, parser, true,
                "trial", "--project", project, proposal, "--words", wordsPath, "--wait", "--json"))
                .WaitAsync(Wait.Scaled(TimeSpan.FromMinutes(30)));
            Assert.True(result.ExitCode == 5, result.FailureDetails);
            var failure = ProjectionJson.Deserialize<FailureEnvelope>(result.Error)!;
            Assert.Equal("assessment.stopped-by-budget", failure.Code);
            Assert.Equal("1", failure.Detail!["wordsNotDone"]);
            Assert.True(File.Exists(heartbeat), result.FailureDetails);
            Assert.NotNull(TryReadProcessId(parserIdPath));
            await AssertProcessStopped(TryReadProcessId(parserIdPath)!.Value);
            // The Capture also queued the Baseline prebuild (a Parsimony report job); the Trial left one job of its own.
            Assert.Equal(1L, ReadCount(project, "Jobs WHERE Kind = 'trial'"));
            Assert.Equal(0L, ReadAssessmentCount(project));
        }
        finally { AssessCommand.BudgetDurationForTests.Value = null; }
    }

    [Fact]
    public async Task InterruptingAssessStopsTheParserAndStoresNothing()
    {
        var project = _pristine.CopyProjectFile();
        await CaptureBaseline(project);
        var wordsPath = Path.Combine(_root, "words.txt");
        File.WriteAllText(wordsPath, "motifa" + Environment.NewLine);
        var heartbeat = Path.Combine(_root, "parser-heartbeat.txt");
        var parserIdPath = Path.Combine(_root, "parser-process-id.txt");
        var parser = CopyFakeParser(new { heartbeatPath = heartbeat, processIdPath = parserIdPath });
        var start = CliProcess.CreateStartInfo(_workerRoot, parser, false,
            "assess", project, "--words", wordsPath, "--json");
        start.Environment["MOTIF_TEST_ASSESS_BUDGET_PROJECT"] = project;
        using var launched = InterruptibleCli.Start(start);
        var process = launched.Process;

        int? parserId = null;
        await Wait.ForAsync(() =>
        {
            if (File.Exists(heartbeat) && TryReadProcessId(parserIdPath) is { } parsedId) parserId = parsedId;
            return parserId is not null || process.HasExited;
        }, TimeSpan.FromSeconds(30));
        Assert.True(parserId is not null,
            $"The fake parser did not start its held batch.{Environment.NewLine}" +
            launched.ReadStderr() + launched.ReadStdout());
        await Task.Delay(TimeSpan.FromSeconds(9));
        Assert.False(process.HasExited, "The CLI exited before cancellation was sent.");

        Assert.True(launched.Interrupt(), "Could not send Ctrl+Break to the CLI process group.");
        await launched.WaitForExitAsync().WaitAsync(Wait.Scaled(TimeSpan.FromMinutes(30)));
        var error = launched.ReadStderr();
        var output = launched.ReadStdout();
        Assert.True(launched.ExitCode == 2, $"Expected cancelled exit code 2.{Environment.NewLine}{error}");
        var failure = ProjectionJson.Deserialize<FailureEnvelope>(error)!;
        Assert.Equal("assessment.cancelled", failure.Code);
        Assert.Equal(FailureReason.Cancelled, failure.Reason);
        Assert.Empty(output);
        await AssertProcessStopped(parserId.Value);
        Assert.Equal(0L, ReadAssessmentCount(project));
        Assert.Equal(0L, ReadInvocationCount(project));
        var statsCacheRoot = Path.Combine(_workerRoot, "assessment-runs");
        if (Directory.Exists(statsCacheRoot))
        {
            Assert.Empty(Directory.EnumerateDirectories(statsCacheRoot));
            Assert.All(Directory.EnumerateFiles(statsCacheRoot), path => Assert.EndsWith(".owner", path, StringComparison.Ordinal));
        }
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private Task CaptureBaseline(string project)
    {
        var result = CliInProcess.Run(_workerRoot, null, false,
            "baseline", "capture", "--project", project, "--json");
        Assert.True(result.ExitCode == 0, result.FailureDetails);
        return Task.CompletedTask;
    }

    private string CopyFakeParser(object behavior)
    {
        var directory = Path.Combine(_root, "fake-pangloss-" + Guid.NewGuid().ToString("N"));
        var parser = FakeParser.Copy(directory);
        FakeParser.BehaveBesideExecutable(parser, behavior);
        return parser;
    }

    private static long ReadAssessmentCount(string projectPath) => ReadCount(projectPath, "Assessments");

    private static long ReadInvocationCount(string projectPath) => ReadCount(projectPath, "AssessmentInvocations");

    private static long ReadCount(string projectPath, string table)
    {
        var project = new ProjectLocator(Path.GetFullPath(projectPath), Path.GetFileNameWithoutExtension(projectPath));
        var databasePath = ProjectDatabaseCatalog.DatabasePathFor(project);
        using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table};";
        return (long)command.ExecuteScalar()!;
    }

    private static async Task AssertProcessStopped(int processId)
    {
        try
        {
            using var parser = Process.GetProcessById(processId);
            await parser.WaitForExitAsync().WaitAsync(Wait.Scaled(TimeSpan.FromMinutes(30)));
            Assert.True(parser.HasExited);
        }
        catch (ArgumentException) { }
    }

    private static int? TryReadProcessId(string path)
    {
        try
        {
            return int.TryParse(File.ReadAllText(path), NumberStyles.None, CultureInfo.InvariantCulture,
                out var processId) ? processId : null;
        }
        catch (IOException)
        {
            return null;
        }
    }
}
