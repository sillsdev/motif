using System.Text.Json;
using SIL.Motif.Host.Parser;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Parser;

/// <summary>
/// Drives the real <see cref="PanGlossGrammarImportProcess"/> against a fake parser executable.
/// </summary>
/// <remarks>
/// These cover the process boundary itself — argument building, draining both streams before waiting, exit
/// codes, cancellation, and the "a report must exist to count as success" rule — rather than a substitute
/// for it. What is fake is only the parser on the far side, which is what lets the failure paths (an exit
/// code of zero with nothing written) be covered at all.
/// </remarks>
public sealed class PanGlossGrammarImportTests : IDisposable
{
    // Kept in step with FakePanGloss's own Program.ArgvFileName; the fake is launched, never referenced.
    private const string ArgvFileName = "_pangloss-argv.json";

    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "motif-fake-pangloss-import-" + Guid.NewGuid().ToString("N"));

    public PanGlossGrammarImportTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task ASuccessfulImportWritesTheGrammarFileTheCallerAsked_For()
    {
        var (fwDataPath, grammarJsonPath) = Project("ok");

        await Run(fwDataPath, grammarJsonPath);

        Assert.True(File.Exists(grammarJsonPath));
    }

    [Fact]
    public async Task TheProcessIsInvokedWithExactlyImportThenTheTwoPaths_InOrder()
    {
        var (fwDataPath, grammarJsonPath) = Project("argv");

        await Run(fwDataPath, grammarJsonPath);

        var argv = ReadArgv(fwDataPath);
        Assert.Equal(["import", fwDataPath, grammarJsonPath], argv);
    }

    [Fact]
    public async Task AZeroExitCodeWithNoGrammarFileWritten_IsRefusedRatherThanReadAsSuccess()
    {
        var (fwDataPath, grammarJsonPath) = Project("no-report");
        FakeParser.Behave(Path.GetDirectoryName(fwDataPath)!, new { mode = "noReport", exitCode = 0 });

        var failure = await Record.ExceptionAsync(() => Run(fwDataPath, grammarJsonPath));

        Assert.IsType<ParserUnavailableException>(failure);
        Assert.False(File.Exists(grammarJsonPath));
    }

    [Fact]
    public async Task MalformedGrammarContentStillCounts_AsSuccessBecauseImportNeverReadsIt()
    {
        // PanGloss owns the snapshot's format; this seam trusts only that bytes were written, never their content.
        var (fwDataPath, grammarJsonPath) = Project("malformed");
        FakeParser.Behave(Path.GetDirectoryName(fwDataPath)!, new { mode = "malformedReport" });

        await Run(fwDataPath, grammarJsonPath);

        Assert.True(File.Exists(grammarJsonPath));
    }

    [Fact]
    public async Task AParserThatFailsIsDistinguishedFromOneThatWroteNothing()
    {
        var (fwDataPath, grammarJsonPath) = Project("fail");
        FakeParser.Behave(Path.GetDirectoryName(fwDataPath)!,
            new { mode = "fail", exitCode = 3, standardError = "grammar import exploded" });

        var failure = await Record.ExceptionAsync(() => Run(fwDataPath, grammarJsonPath));

        Assert.NotNull(failure);
        Assert.Contains("grammar import exploded", failure!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ALargeStandardErrorDoesNotDeadlock_BecauseBothStreamsAreDrainedBeforeWaiting()
    {
        // A full stderr pipe deadlocks WaitForExit unless both streams are drained concurrently first.
        var (fwDataPath, grammarJsonPath) = Project("large-stderr");
        var hugeStandardError = new string('e', 500_000);
        FakeParser.Behave(Path.GetDirectoryName(fwDataPath)!,
            new { mode = "fail", exitCode = 1, standardError = hugeStandardError });

        var failure = await Record.ExceptionAsync(() => Run(fwDataPath, grammarJsonPath))
            .WaitAsync(TimeSpan.FromSeconds(30));

        Assert.NotNull(failure);
    }

    [Fact]
    public async Task CancellingTheImportStopsTheParserProcess()
    {
        var (fwDataPath, grammarJsonPath) = Project("slow");
        var heartbeat = Path.Combine(_root, "heartbeat.txt");
        FakeParser.Behave(Path.GetDirectoryName(fwDataPath)!, new { heartbeatPath = heartbeat });
        using var cancellation = new CancellationTokenSource();

        var run = Run(fwDataPath, grammarJsonPath, cancellation.Token);
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
    public async Task ASuccessfulImport_AssignsTheStartedProcessToTheSuppliedGovernor()
    {
        var (fwDataPath, grammarJsonPath) = Project("governor");
        var governor = new RecordingGovernor();

        await new PanGlossGrammarImportProcess(FakeParser.ExecutablePath)
            .ImportAsync(fwDataPath, grammarJsonPath, CancellationToken.None, governor);

        Assert.Single(governor.ContainedProcessIds);
    }

    private static Task Run(string fwDataPath, string grammarJsonPath, CancellationToken cancellationToken = default) =>
        new PanGlossGrammarImportProcess(FakeParser.ExecutablePath)
            .ImportAsync(fwDataPath, grammarJsonPath, cancellationToken);

    private (string FwDataPath, string GrammarJsonPath) Project(string name)
    {
        var directory = Path.Combine(_root, name);
        Directory.CreateDirectory(directory);
        var fwDataPath = Path.Combine(directory, "project.fwdata");
        File.WriteAllText(fwDataPath, "the fake parser never reads this.");
        var grammarJsonPath = Path.Combine(directory, "grammar.json");
        return (fwDataPath, grammarJsonPath);
    }

    private static string[] ReadArgv(string fwDataPath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(fwDataPath))!;
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
