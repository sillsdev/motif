using SIL.Motif.Commands.Preferences;
using Xunit;

namespace SIL.Motif.Tests.Commands.Preferences;

public sealed class AdvancedAiModePreferenceStoreTests
{
    [Fact]
    public void MissingPreferenceDefaultsOffAndUpdatesPersistAcrossStoreInstances()
    {
        var path = Path.Combine(Path.GetTempPath(), "motif-advanced-ai-tests", Guid.NewGuid().ToString("N"),
            "advanced-ai-mode.json");
        var store = new FileAdvancedAiModePreferenceStore(path);

        Assert.False(store.IsEnabled);
        store.SetEnabled(true);
        Assert.True(new FileAdvancedAiModePreferenceStore(path).IsEnabled);
        new FileAdvancedAiModePreferenceStore(path).SetEnabled(false);
        Assert.False(store.IsEnabled);

        Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
    }

    [Fact]
    public void InvalidPreferenceFailsClosed()
    {
        var path = Path.Combine(Path.GetTempPath(), "motif-advanced-ai-tests", Guid.NewGuid().ToString("N"),
            "advanced-ai-mode.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{\"advancedAiMode\":\"yes\"}");

        Assert.False(new FileAdvancedAiModePreferenceStore(path).IsEnabled);

        Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
    }
}
