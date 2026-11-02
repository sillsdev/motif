using System.Diagnostics;
using System.Text.Json;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker;
using Xunit;

namespace SIL.Motif.Tests.Cli;

public sealed class SelectionSetupArgvTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-selection-setup-" + Guid.NewGuid().ToString("N"));
    private readonly string _workerRoot = Path.Combine(Path.GetTempPath(), "motif-selection-setup-worker-" + Guid.NewGuid().ToString("N"));

    public SelectionSetupArgvTests()
    {
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(_workerRoot);
        File.WriteAllText(Project, string.Empty);
    }

    private string Project => Path.Combine(_root, "project.fwdata");

    [Fact]
    public void TextInventoryIsAvailableThroughTheCli()
    {
        var result = Run($"texts list --project \"{Project}\" --json");

        Assert.Equal(0, result.ExitCode);
        using var document = JsonDocument.Parse(result.Output);
        Assert.False(document.RootElement.GetProperty("hasBaseline").GetBoolean());
        Assert.Empty(document.RootElement.GetProperty("texts").EnumerateArray());
    }

    [Fact]
    public void AWordsFileWithCountsIsSavedWithTheSelectionAndShownBySource()
    {
        var file = Path.Combine(_root, "words.txt");
        File.WriteAllText(file, "motifa\t7\nmotifb\nmotifa\t3\n");

        var set = Run($"selection set-default --project \"{Project}\" --name Default --words-file \"{file}\" --json");
        Assert.Equal(0, set.ExitCode);
        var show = Run($"selection show --project \"{Project}\" --json");

        Assert.Equal(0, show.ExitCode);
        using var document = JsonDocument.Parse(show.Output);
        var list = document.RootElement.GetProperty("selection").GetProperty("wordList");
        Assert.Equal("words.txt", list.GetProperty("source").GetString());
        Assert.StartsWith("sha256:", list.GetProperty("sha256").GetString(), StringComparison.Ordinal);
        Assert.Equal(10, list.GetProperty("counts").GetProperty("motifa").GetInt32());
        Assert.False(list.GetProperty("counts").TryGetProperty("motifb", out _));
        var text = Run($"selection show --project \"{Project}\"");
        Assert.Contains("Word list:", text.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void AWordsFileRowWithABadCountIsRefusedWithItsLineNumber()
    {
        var file = Path.Combine(_root, "bad.txt");
        File.WriteAllText(file, "motifa\t7\nmotifb\tmany\n");

        var set = Run($"selection set-default --project \"{Project}\" --name Default --words-file \"{file}\"");

        Assert.NotEqual(0, set.ExitCode);
        Assert.Contains("Line 2 of the words file", set.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void SetDefaultAndSetLimitsPersistTheCompleteSelectionPolicy()
    {
        var set = Run($"selection set-default --project \"{Project}\" --name Default --add-words motifa " +
                      "--time-mode explicit --time-limit-ms 750 --parser-work-limit 321 --json");

        Assert.Equal(0, set.ExitCode);
        var firstRead = Run($"selection show --project \"{Project}\" --json");

        Assert.Equal(0, firstRead.ExitCode);
        using var firstDocument = JsonDocument.Parse(firstRead.Output);
        var firstSelection = firstDocument.RootElement.GetProperty("selection");
        var firstRevision = firstSelection.GetProperty("revision").GetString()!;
        Assert.Equal("Explicit", firstSelection.GetProperty("limits").GetProperty("timeMode").GetString());
        Assert.Equal(750, firstSelection.GetProperty("limits").GetProperty("explicitPerWordLimitMs").GetInt32());
        Assert.Equal(321, firstSelection.GetProperty("limits").GetProperty("perWordParserWorkLimit")
            .GetProperty("work").GetInt64());

        var changed = Run($"selection set-limits --project \"{Project}\" --name Default " +
            $"--expected-revision {firstRevision} --parser-work-limit 654 --time-mode estimated --json");

        Assert.Equal(0, changed.ExitCode);
        using var changedDocument = JsonDocument.Parse(changed.Output);
        var updated = changedDocument.RootElement;
        Assert.Equal("Default", updated.GetProperty("name").GetString());
        Assert.Equal("Estimated", updated.GetProperty("limits").GetProperty("timeMode").GetString());
        Assert.False(updated.GetProperty("limits").TryGetProperty("explicitPerWordLimitMs", out _));
        Assert.Equal(654, updated.GetProperty("limits").GetProperty("perWordParserWorkLimit")
            .GetProperty("work").GetInt64());
        Assert.Equal(1, updated.GetProperty("addedWords").GetArrayLength());

        var stale = Run($"selection set-limits --project \"{Project}\" --name Default " +
            $"--expected-revision {firstRevision} --parser-work-limit 777 --time-mode estimated --json");
        Assert.NotEqual(0, stale.ExitCode);
        Assert.Contains("selection.revision-conflict", stale.Output + stale.Error, StringComparison.Ordinal);

        var finalRead = Run($"selection show --project \"{Project}\" --json");
        Assert.Equal(0, finalRead.ExitCode);
        using var finalDocument = JsonDocument.Parse(finalRead.Output);
        var finalSelection = finalDocument.RootElement.GetProperty("selection");
        Assert.Equal(654, finalSelection.GetProperty("limits").GetProperty("perWordParserWorkLimit")
            .GetProperty("work").GetInt64());
    }

    [Fact]
    public void TheParsimonySelectionIsSavedApartFromTheDefaultSelectionAndShownWithIt()
    {
        var text = Guid.NewGuid();
        Assert.Equal(0, Run($"selection set-default --project \"{Project}\" --name Default --add-words motifa --json").ExitCode);

        var set = Run($"selection set-parsimony --project \"{Project}\" --name Evidence --texts {text:D} --json");

        Assert.True(set.ExitCode == 0, set.Output + set.Error);
        var show = Run($"selection show --project \"{Project}\" --json");
        using var document = JsonDocument.Parse(show.Output);
        Assert.Equal("Default", document.RootElement.GetProperty("selection").GetProperty("name").GetString());
        var parsimony = document.RootElement.GetProperty("parsimonySelection");
        Assert.Equal("Evidence", parsimony.GetProperty("name").GetString());
        Assert.Equal(text, parsimony.GetProperty("textIds")[0].GetGuid());

        var clash = Run($"selection set-parsimony --project \"{Project}\" --name Default --texts {text:D} --json");
        Assert.NotEqual(0, clash.ExitCode);
        Assert.Contains("selection.parsimony-name-is-default", clash.Output + clash.Error, StringComparison.Ordinal);
        var reuse = Run($"selection set-parsimony --project \"{Project}\" --name Default --json");
        Assert.Equal(0, reuse.ExitCode);
        var absent = Run($"selection set-parsimony --project \"{Project}\" --name Nobody --json");
        Assert.Contains("selection.not-found", absent.Output + absent.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void SkippingSetupPersistsWithoutCreatingADefaultSelection()
    {
        var skipped = Run($"setup skip --project \"{Project}\" --json");

        Assert.True(skipped.ExitCode == 0, $"Expected exit code 0; got {skipped.ExitCode}.{Environment.NewLine}{skipped.Error}");
        var read = Run($"selection show --project \"{Project}\" --json");

        Assert.True(read.ExitCode == 0, $"Expected exit code 0; got {read.ExitCode}.{Environment.NewLine}{read.Error}");
        using var document = JsonDocument.Parse(read.Output);
        Assert.True(document.RootElement.TryGetProperty("setupSkipped", out var setupSkipped), read.Output);
        Assert.True(setupSkipped.GetBoolean());
        Assert.False(document.RootElement.TryGetProperty("selection", out _));
    }

    private CliRun Run(string arguments)
    {
        var start = new ProcessStartInfo(BuildOutput.Cli)
        {
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.Environment[RunnerOptions.RootVariable] = _workerRoot;
        using var process = Process.Start(start)!;
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        var output = outputTask.GetAwaiter().GetResult();
        var error = errorTask.GetAwaiter().GetResult();
        Assert.True(process.WaitForExit(60000), "The CLI did not exit within its bound.");
        return new CliRun(process.ExitCode, output, error);
    }

    private sealed record CliRun(int ExitCode, string Output, string Error);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        try { Directory.Delete(_workerRoot, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
