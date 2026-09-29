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
    public void SetDefaultPersistsTheChosenTimeAndStepLimits()
    {
        var set = Run($"selection set-default --project \"{Project}\" --name Default --add-words motifa " +
                      "--time-limit-ms 750 --step-cap 321 --json");

        Assert.Equal(0, set.ExitCode);
        var read = Run($"selection show --project \"{Project}\" --json");

        Assert.Equal(0, read.ExitCode);
        using var document = JsonDocument.Parse(read.Output);
        var selection = document.RootElement.GetProperty("selection");
        Assert.Equal(750, selection.GetProperty("perWordLimitMs").GetInt64());
        Assert.Equal(321, selection.GetProperty("perWordStepLimit").GetProperty("steps").GetInt64());
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
