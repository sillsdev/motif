using System.Diagnostics;
using System.Globalization;
using Microsoft.Data.Sqlite;
using SIL.Motif.Contract.Projects;
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
        var assessed = await CliProcess.RunAsync(_workerRoot, parser, false, "assess", project, "--json");

        Assert.True(assessed.ExitCode == 0, assessed.FailureDetails);
        var response = ProjectionJson.Deserialize<AssessCommandResponse>(assessed.Output)!;
        Assert.Contains("motifa", response.Selection.Words);
        Assert.NotEmpty(response.AssessmentIds);

        var timingResult = CliInProcess.Run(_workerRoot, null, false,
            "timing", "--project", project, "--json");
        Assert.True(timingResult.ExitCode == 0, timingResult.FailureDetails);
        var timing = ProjectionJson.Deserialize<TimingResponse>(timingResult.Output)!;
        Assert.Contains(timing.AssessmentId, response.AssessmentIds);
        Assert.Contains(timing.Words, row => row.Word == "motifa");

        var overviewResult = CliInProcess.Run(_workerRoot, null, false,
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

        var assessed = await CliProcess.RunAsync(_workerRoot, parser, false,
            "assess", project, "--words", wordsPath, "--json");

        Assert.True(assessed.ExitCode == 0, assessed.FailureDetails);
        var response = ProjectionJson.Deserialize<AssessCommandResponse>(assessed.Output)!;
        Assert.Equal(new[] { "motifa", "motifb" }, response.Selection.Words);
        Assert.Equal(new[] { "motifa", "motifb" }, response.Words.Select(row => row.Word));
        Assert.DoesNotContain("motifanalysed", response.Words.Select(row => row.Word));
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
        using var launched = InterruptibleCli.Start(CliProcess.CreateStartInfo(_workerRoot, parser, false,
            "assess", project, "--words", wordsPath, "--json"));
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
            Assert.Empty(Directory.EnumerateFileSystemEntries(statsCacheRoot));
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
            "baseline", "capture", project, "--json");
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
