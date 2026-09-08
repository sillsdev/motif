using System.Text.Json;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.Parser;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Assess;

/// <summary>
/// Drives the real <see cref="PanGlossStatsQueryProcess"/> against a fake parser executable.
/// </summary>
/// <remarks>
/// These cover the process boundary itself — argument building, draining both streams before waiting, exit
/// codes, and cancellation — rather than a substitute for it. The forwarding pin is the point of this file:
/// design decision 5 says Motif contributes exactly two arguments and forwards every caller-supplied one
/// verbatim, in order, so these assert on the exact argv the fake recorded rather than on any interpretation
/// of it.
/// </remarks>
public sealed class PanGlossStatsQueryTests : IDisposable
{
    // Kept in step with FakePanGloss's own Program.ArgvFileName; the fake is launched, never referenced.
    private const string ArgvFileName = "_pangloss-argv.json";

    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "motif-fake-pangloss-stats-" + Guid.NewGuid().ToString("N"));

    public PanGlossStatsQueryTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task ASuccessfulQueryReturnsBothStreams()
    {
        var (grammarPath, cachePath) = Project("ok");

        var output = await Run(grammarPath, cachePath, []);

        Assert.Contains("group", output.StandardOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithNoForwardedArguments_TheArgvIsExactlyStatsGrammarCacheAndCachePath()
    {
        var (grammarPath, cachePath) = Project("argv-bare");

        await Run(grammarPath, cachePath, []);

        Assert.Equal(["stats", grammarPath, "--cache", cachePath], ReadArgv(grammarPath));
    }

    [Fact]
    public async Task ForwardedArguments_AreAppendedAfterCachePath_InOrder()
    {
        var (grammarPath, cachePath) = Project("argv-forwarded");
        string[] forwarded = ["--group", "word", "--limit", "10"];

        await Run(grammarPath, cachePath, forwarded);

        Assert.Equal(
            ["stats", grammarPath, "--cache", cachePath, .. forwarded],
            ReadArgv(grammarPath));
    }

    [Fact]
    public async Task ForwardingIsByteForByte_BoundariesOrderDuplicatesCasingAndLeadingDashesAllSurvive()
    {
        // Motif never tokenizes, normalizes, or reorders the rest (design decision 5).
        var (grammarPath, cachePath) = Project("argv-verbatim");
        string[] forwarded =
        [
            "--Group", "Word And Spaces", "--group", "word", "-x", "--group", "word",
            "-onedash-value", "--EMPTY", "",
        ];

        await Run(grammarPath, cachePath, forwarded);

        var argv = ReadArgv(grammarPath);
        Assert.Equal(["stats", grammarPath, "--cache", cachePath, .. forwarded], argv);
        // Duplicates and casing are preserved exactly, not deduplicated or normalized.
        Assert.Equal(3, argv.Count(a => a == "--group" || a == "--Group"));
        Assert.Equal(2, argv.Count(a => a == "word"));
        Assert.Contains("-onedash-value", argv);
        Assert.Contains("", argv);
    }

    [Fact]
    public async Task AParserThatFailsIsDistinguishedFromOneThatProducedNoRows()
    {
        var (grammarPath, cachePath) = Project("fail");
        FakeParser.Behave(Path.GetDirectoryName(grammarPath)!,
            new { mode = "fail", exitCode = 3, standardError = "stats query exploded" });

        var failure = await Record.ExceptionAsync(() => Run(grammarPath, cachePath, []));

        Assert.NotNull(failure);
        Assert.Contains("stats query exploded", failure!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ALargeStandardErrorDoesNotDeadlock_BecauseBothStreamsAreDrainedBeforeWaiting()
    {
        // A full stderr pipe deadlocks WaitForExit unless both streams are drained concurrently first.
        var (grammarPath, cachePath) = Project("large-stderr");
        var hugeStandardError = new string('e', 500_000);
        FakeParser.Behave(Path.GetDirectoryName(grammarPath)!,
            new { mode = "fail", exitCode = 1, standardError = hugeStandardError });

        var failure = await Record.ExceptionAsync(() => Run(grammarPath, cachePath, []))
            .WaitAsync(TimeSpan.FromSeconds(30));

        Assert.NotNull(failure);
    }

    [Fact]
    public async Task CancellingTheQueryStopsTheParserProcess()
    {
        var (grammarPath, cachePath) = Project("slow");
        var heartbeat = Path.Combine(_root, "heartbeat.txt");
        FakeParser.Behave(Path.GetDirectoryName(grammarPath)!, new { heartbeatPath = heartbeat });
        using var cancellation = new CancellationTokenSource();

        var run = Run(grammarPath, cachePath, [], cancellation.Token);
        for (var i = 0; i < 200 && !File.Exists(heartbeat); i++) await Task.Delay(25);
        Assert.True(File.Exists(heartbeat), "The fake parser never started ticking.");
        await cancellation.CancelAsync();
        await Record.ExceptionAsync(() => run);

        var stoppedAt = File.ReadAllText(heartbeat);
        await Task.Delay(300);
        // Still ticking here would mean cancellation abandoned the process rather than killing it.
        Assert.Equal(stoppedAt, File.ReadAllText(heartbeat));
    }

    [Fact]
    public async Task ASuccessfulQuery_AssignsTheStartedProcessToTheSuppliedGovernor()
    {
        var (grammarPath, cachePath) = Project("governor");
        var governor = new RecordingGovernor();

        await new PanGlossStatsQueryProcess(FakeParser.ExecutablePath)
            .QueryAsync(grammarPath, cachePath, [], CancellationToken.None, governor);

        Assert.Single(governor.ContainedProcessIds);
    }

    private static Task<PanGlossStatsOutput> Run(
        string grammarPath, string cachePath, IReadOnlyList<string> forwardedArguments,
        CancellationToken cancellationToken = default) =>
        new PanGlossStatsQueryProcess(FakeParser.ExecutablePath)
            .QueryAsync(grammarPath, cachePath, forwardedArguments, cancellationToken);

    private (string GrammarPath, string CachePath) Project(string name)
    {
        var directory = Path.Combine(_root, name);
        Directory.CreateDirectory(directory);
        var grammarPath = Path.Combine(directory, "grammar.json");
        File.WriteAllText(grammarPath, "the fake parser never reads this.");
        var cachePath = Path.Combine(directory, "stats.cache");
        return (grammarPath, cachePath);
    }

    private static string[] ReadArgv(string grammarPath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(grammarPath))!;
        var argvPath = Path.Combine(directory, ArgvFileName);
        return JsonSerializer.Deserialize<string[]>(File.ReadAllText(argvPath))!;
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); }
        catch (DirectoryNotFoundException) { }
        catch (IOException) { }
    }
}
