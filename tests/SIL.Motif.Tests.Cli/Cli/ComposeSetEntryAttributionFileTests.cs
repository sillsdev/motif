using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Cli;

public sealed class ComposeSetEntryAttributionFileTests : IDisposable
{
    private const string Tag = "lang-sme@9ba7be7 GPL-3.0";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-entries-file-" + Guid.NewGuid().ToString("N"));

    public ComposeSetEntryAttributionFileTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private async Task<(int ExitCode, string Text)> RunAsync(string? list, params string[] arguments)
    {
        var file = Path.Combine(_root, "entries.txt");
        if (list is not null) await File.WriteAllTextAsync(file, list);
        var start = CliProcess.CreateStartInfoWithAdvancedAiModePath(Path.Combine(_root, "worker"), null,
            developerCommands: false, Path.Combine(_root, "advanced-ai-mode.json"),
            ["compose-set-entry-attribution", "--project", Path.Combine(_root, "none.fwdata"), "--draft", "d", .. arguments.Select(
                argument => argument == "<file>" ? file : argument)]);
        var run = await CliProcess.RunAsync(start);
        return (run.ExitCode, run.Output + run.Error);
    }

    [Fact]
    public async Task AnEntriesFileAndAnIntentTogetherAreRefusedBeforeAnyProjectIsOpened()
    {
        var run = await RunAsync($"{Guid.NewGuid():D}\n", "--intent", "{}", "--entries-file", "<file>", "--tag", Tag);

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("not both", run.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnEntriesFileWithoutATagAndAMissingFileAreRefused()
    {
        var noTag = await RunAsync($"{Guid.NewGuid():D}\n", "--entries-file", "<file>");
        Assert.NotEqual(0, noTag.ExitCode);
        Assert.Contains("--tag", noTag.Text, StringComparison.Ordinal);

        var missing = await RunAsync(null, "--entries-file", Path.Combine(_root, "absent.txt"), "--tag", Tag);
        Assert.NotEqual(0, missing.ExitCode);
        Assert.Contains("does not exist", missing.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnreadableLinesAreRefusedTogetherWithTheirLineNumbersBeforeAnyProjectIsOpened()
    {
        var run = await RunAsync($"{Guid.NewGuid():D}\n# note\nnot-an-entry\n\nalso bad\n", "--entries-file", "<file>", "--tag", Tag);

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("line 3: 'not-an-entry'", run.Text, StringComparison.Ordinal);
        Assert.Contains("line 5: 'also bad'", run.Text, StringComparison.Ordinal);
    }
}
