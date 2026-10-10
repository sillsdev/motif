using SIL.Motif.App.Composition;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Commands.Preferences;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class AdvancedAiModeSurfaceTests
{
    [Fact]
    public void WindowPolicyReadsTheSharedPreferenceAndKeepsAdvancedCommandsHiddenUntilEnabled()
    {
        var path = Path.Combine(Path.GetTempPath(), "motif-advanced-ai-app-tests", Guid.NewGuid().ToString("N"),
            "advanced-ai-mode.json");
        var store = new FileAdvancedAiModePreferenceStore(path);
        var options = new MotifAppOptions("unused", null, null!, TimeProvider.System,
            AdvancedAiModePreferences: store);
        var futureAgentCommand = new CommandDescriptor(
            "future-composer", typeof(string), typeof(string), CommandSurface.AdvancedAi, AgentClass.Draft);

        Assert.False(options.SurfacePolicy.IsAvailable(futureAgentCommand));
        store.SetEnabled(true);
        Assert.True(options.SurfacePolicy.IsAvailable(futureAgentCommand));

        Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
    }
}
