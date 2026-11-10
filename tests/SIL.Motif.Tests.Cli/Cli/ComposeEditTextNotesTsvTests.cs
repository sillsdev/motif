using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Cli;

public sealed class ComposeEditTextNotesTsvTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-notes-tsv-" + Guid.NewGuid().ToString("N"));

    public ComposeEditTextNotesTsvTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public async Task NotesTsvAndIntentTogetherAreRefusedBeforeAnyProjectIsOpened()
    {
        var tsv = Path.Combine(_root, "notes.tsv");
        await File.WriteAllTextAsync(tsv, "Tracking\t1\tx\n");
        var start = CliProcess.CreateStartInfoWithAdvancedAiModePath(Path.Combine(_root, "worker"), null,
            developerCommands: false, Path.Combine(_root, "advanced-ai-mode.json"),
            "compose-edit-text-notes", "--project", Path.Combine(_root, "none.fwdata"), "--draft", "d",
            "--intent", "{}", "--notes-tsv", tsv, "--json");
        var run = await CliProcess.RunAsync(start);
        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("not both", run.Output + run.Error, StringComparison.Ordinal);
    }
}
