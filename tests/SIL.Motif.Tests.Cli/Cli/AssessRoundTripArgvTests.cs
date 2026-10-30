using System.Diagnostics;
using System.Globalization;
using Microsoft.Data.Sqlite;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Assess;
using SIL.Motif.Host;
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
                .WaitAsync(TimeSpan.FromSeconds(45));
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
                .WaitAsync(TimeSpan.FromSeconds(45));
            Assert.True(result.ExitCode == 5, result.FailureDetails);
            var failure = ProjectionJson.Deserialize<FailureEnvelope>(result.Error)!;
            Assert.Equal("assessment.stopped-by-budget", failure.Code);
            Assert.Equal("1", failure.Detail!["wordsNotDone"]);
            Assert.True(File.Exists(heartbeat), result.FailureDetails);
            Assert.NotNull(TryReadProcessId(parserIdPath));
            await AssertProcessStopped(TryReadProcessId(parserIdPath)!.Value);
            Assert.Equal(1L, ReadCount(project, "Jobs"));
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

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        int? parserId = null;
        while (!process.HasExited && DateTime.UtcNow < deadline)
        {
            if (File.Exists(heartbeat) && TryReadProcessId(parserIdPath) is { } parsedId)
            {
                parserId = parsedId;
                break;
            }
            await Task.Delay(25);
        }
        Assert.True(parserId is not null,
            $"The fake parser did not start its held batch.{Environment.NewLine}" +
            launched.ReadStderr() + launched.ReadStdout());
        await Task.Delay(TimeSpan.FromSeconds(9));
        Assert.False(process.HasExited, "The CLI exited before cancellation was sent.");

        Assert.True(launched.Interrupt(), "Could not send Ctrl+Break to the CLI process group.");
        await launched.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
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
            await parser.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
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
