using SIL.Motif.Commands.Preferences;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Cli;

public sealed class AdvancedAiModeTests
{
    [Fact]
    public async Task McpRefusesWithOnePlainInstructionWhenModeIsOff()
    {
        var preferencePath = NewPreferencePath();
        new FileAdvancedAiModePreferenceStore(preferencePath).SetEnabled(false);

        var start = CliProcess.CreateStartInfoWithAdvancedAiModePath(
            Path.Combine(Path.GetTempPath(), "motif-mcp-off"), FakeParser.ExecutablePath, developerCommands: false,
            preferencePath, "mcp", "--project", "unused");
        var result = await CliProcess.RunAsync(start);

        Assert.Equal(2, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Equal(FileAdvancedAiModePreferenceStore.EnableInstruction + Environment.NewLine, result.Error);
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

    private static string NewPreferencePath() => Path.Combine(Path.GetTempPath(), "motif-advanced-ai-cli-tests",
        Guid.NewGuid().ToString("N"), "advanced-ai-mode.json");
}
