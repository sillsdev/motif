using SIL.Motif.Commands.Preferences;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Cli;

public sealed class AdvancedAiModeTests
{
    [Fact]
    public async Task McpRejectsTheRemovedProjectLaunchOption()
    {
        var preferencePath = NewPreferencePath();
        new FileAdvancedAiModePreferenceStore(preferencePath).SetEnabled(false);

        var start = CliProcess.CreateStartInfoWithAdvancedAiModePath(
            Path.Combine(Path.GetTempPath(), "motif-mcp-off"), FakeParser.ExecutablePath, developerCommands: false,
            preferencePath, "mcp", "--project", "unused");
        var result = await CliProcess.RunAsync(start);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.StartsWith("Usage: motif mcp [--profile", result.Error);
        Assert.DoesNotContain("--project", result.Error);
    }

    [Theory]
    [InlineData("on", true)]
    [InlineData("off", false)]
    public async Task SettingsCommandPersistsTheSharedPreference(string choice, bool enabled)
    {
        var preferencePath = NewPreferencePath();
        var store = new FileAdvancedAiModePreferenceStore(preferencePath);
        store.SetEnabled(!enabled);

        var start = CliProcess.CreateStartInfoWithAdvancedAiModePath(
            Path.Combine(Path.GetTempPath(), "motif-settings"), FakeParser.ExecutablePath, developerCommands: false,
            preferencePath, "settings", "advanced-ai", choice);
        var result = await CliProcess.RunAsync(start);

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.Equal(enabled, store.IsEnabled);
    }

    [Theory]
    [InlineData(true, "Advanced AI mode is on.")]
    [InlineData(false, "Advanced AI mode is off.")]
    public async Task SettingsCommandReportsTheCurrentModeWithoutChangingIt(bool enabled, string expected)
    {
        var preferencePath = NewPreferencePath();
        var store = new FileAdvancedAiModePreferenceStore(preferencePath);
        store.SetEnabled(enabled);

        var start = CliProcess.CreateStartInfoWithAdvancedAiModePath(
            Path.Combine(Path.GetTempPath(), "motif-settings-status"), FakeParser.ExecutablePath,
            developerCommands: false, preferencePath, "settings", "advanced-ai");
        var result = await CliProcess.RunAsync(start);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(expected + Environment.NewLine, result.Output);
        Assert.Empty(result.Error);
        Assert.Equal(enabled, store.IsEnabled);
    }

    [Fact]
    public async Task SavingThePreferenceWhileOtherProcessesReadItNeverFails()
    {
        var path = NewPreferencePath();
        new FileAdvancedAiModePreferenceStore(path).SetEnabled(true);
        using var stop = new CancellationTokenSource();
        var readers = Enumerable.Range(0, 4).Select(index => Task.Run(() =>
        {
            var reader = new FileAdvancedAiModePreferenceStore(path);
            while (!stop.IsCancellationRequested) _ = reader.IsEnabled;
        })).ToArray();

        try
        {
            var writer = new FileAdvancedAiModePreferenceStore(path);
            for (var i = 0; i < 300; i++) writer.SetEnabled(true);
        }
        finally
        {
            await stop.CancelAsync();
            await Task.WhenAll(readers);
        }

        Assert.True(new FileAdvancedAiModePreferenceStore(path).IsEnabled);
    }

    private static string NewPreferencePath() => Path.Combine(Path.GetTempPath(), "motif-advanced-ai-cli-tests",
        Guid.NewGuid().ToString("N"), "advanced-ai-mode.json");
}
