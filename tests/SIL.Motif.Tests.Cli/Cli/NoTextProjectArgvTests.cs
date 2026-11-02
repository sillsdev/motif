using System.Text.Json;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Cli;

[Collection(LcmCacheParallelCollections.Group0)]
public sealed class NoTextProjectArgvTests : IDisposable
{
    private readonly PristineProjectFixture _pristine;
    private readonly string _workerRoot = Path.Combine(
        Path.GetTempPath(), "motif-no-text-project-" + Guid.NewGuid().ToString("N"));

    public NoTextProjectArgvTests(PristineProjectFixture pristine)
    {
        _pristine = pristine;
        Directory.CreateDirectory(_workerRoot);
    }

    [Fact]
    public async Task AProjectWithoutTextsCanCaptureAndUseATypedSelection()
    {
        var project = _pristine.CopyProjectFile();
        var captured = await CliProcess.RunAsync(
            _workerRoot, null, developerCommands: false, "baseline", "capture", "--project", project, "--json");

        Assert.True(captured.ExitCode == 0, captured.FailureDetails);

        var inventory = await CliProcess.RunAsync(
            _workerRoot, null, developerCommands: false, "texts", "list", "--project", project, "--json");
        Assert.True(inventory.ExitCode == 0, inventory.FailureDetails);
        using (var document = JsonDocument.Parse(inventory.Output))
        {
            Assert.True(document.RootElement.GetProperty("hasBaseline").GetBoolean());
            Assert.Empty(document.RootElement.GetProperty("texts").EnumerateArray());
        }

        var saved = await CliProcess.RunAsync(
            _workerRoot, null, developerCommands: false, "selection", "set-default", "--project", project,
            "--name", "Default", "--add-words", "motifa", "--json");

        Assert.True(saved.ExitCode == 0, saved.FailureDetails);
        var shown = await CliProcess.RunAsync(
            _workerRoot, null, developerCommands: false, "selection", "show", "--project", project, "--json");
        Assert.True(shown.ExitCode == 0, shown.FailureDetails);
        using var selectionDocument = JsonDocument.Parse(shown.Output);
        var selection = selectionDocument.RootElement.GetProperty("selection");
        Assert.Empty(selection.GetProperty("textIds").EnumerateArray());
        Assert.Equal("motifa", Assert.Single(selection.GetProperty("addedWords").EnumerateArray()).GetString());
    }

    [Fact]
    public async Task BaselineCaptureWritesUnderXdgDataHomeWhenNoWorkerRootIsSet()
    {
        if (OperatingSystem.IsWindows()) return;
        var dataHome = Path.Combine(_workerRoot, "xdg-data-home");
        var home = Path.Combine(_workerRoot, "home");
        Directory.CreateDirectory(home);
        var project = _pristine.CopyProjectFile();
        var start = CliProcess.CreateStartInfo(
            _workerRoot, null, developerCommands: false, "baseline", "capture", "--project", project, "--json");
        start.Environment.Remove("MOTIF_WORKER_ROOT");
        start.Environment["XDG_DATA_HOME"] = dataHome;
        start.Environment["HOME"] = home;
        start.WorkingDirectory = home;

        var captured = await CliProcess.RunAsync(start);

        Assert.True(captured.ExitCode == 0, captured.Error + captured.Output);
        Assert.NotEmpty(Directory.GetDirectories(Path.Combine(dataHome, "SIL", "Motif", "baselines")));
        Assert.False(Directory.Exists(Path.Combine(home, ".local")));
        Assert.False(Directory.Exists(Path.Combine(home, "SIL")));
    }

    public void Dispose()
    {
        try { Directory.Delete(_workerRoot, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
