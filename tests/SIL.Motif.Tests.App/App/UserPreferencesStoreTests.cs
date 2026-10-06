using Avalonia;
using SIL.Motif.App.Services;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class UserPreferencesStoreTests
{
    [Fact]
    public void RestoredBoundsAreConstrainedToTheSelectedScreenWorkArea()
    {
        var position = WindowPlacementPolicy.Constrain(new PixelPoint(-3000, 1600),
            new PixelRect(-1920, 0, 1920, 1080), new Size(800, 600), 1.5);

        Assert.Equal(new PixelPoint(-1920, 180), position);
    }

    [Fact]
    public void MissingFileUsesTheDeclaredDefaults()
    {
        using var folder = new TemporaryFolder();
        var store = new FileUserPreferencesStore(folder.File("user-preferences.json"));

        Assert.Equal(PreferenceState.Missing, store.State);
        Assert.Equal(UserPreferences.Defaults, store.Current);
    }

    [Fact]
    public async Task UpdatesFromTwoAppProcessesMergeFieldsUnderTheSharedLock()
    {
        using var folder = new TemporaryFolder();
        var path = folder.File("user-preferences.json");
        var first = new FileUserPreferencesStore(path);
        var second = new FileUserPreferencesStore(path);

        var writes = await Task.WhenAll(
            Task.Run(() => first.Update(preferences => preferences with { Theme = UserTheme.Dark })),
            Task.Run(() => second.Update(preferences => preferences with { Trace = TracePresentation.Expert })));

        Assert.All(writes, write => Assert.Equal(PreferenceWriteState.Saved, write.State));
        var saved = new FileUserPreferencesStore(path);
        Assert.Equal(UserTheme.Dark, saved.Current.Theme);
        Assert.Equal(TracePresentation.Expert, saved.Current.Trace);
    }

    [Fact]
    public void RefusedShapeIsNeverOverwrittenAndResetTouchesOnlyThisFile()
    {
        using var folder = new TemporaryFolder();
        var path = folder.File("user-preferences.json");
        var unrelated = folder.File("trace-view.json");
        const string original = "{\"SchemaVersion\":2,\"ZoomPercent\":125,\"Theme\":\"Dark\"}";
        File.WriteAllText(path, original);
        File.WriteAllText(unrelated, "keep this file");
        var store = new FileUserPreferencesStore(path);

        Assert.Equal(PreferenceState.Refused, store.State);
        Assert.Equal(PreferenceWriteState.Refused,
            store.Update(preferences => preferences with { ZoomPercent = 125 }).State);
        Assert.Equal(original, File.ReadAllText(path));

        Assert.Equal(PreferenceWriteState.Saved, store.Reset().State);
        Assert.Equal(UserPreferences.Defaults, new FileUserPreferencesStore(path).Current);
        Assert.Equal("keep this file", File.ReadAllText(unrelated));
    }

    [Theory]
    [InlineData("{\"FormatVersion\":1}")]
    [InlineData("{\"FormatVersion\":1,\"ZoomPercent\":100,\"Theme\":\"future\",\"LastSettingsGroup\":\"Display\",\"Window\":null,\"Trace\":\"Plain\",\"HasSeenTechDemoNotice\":false}")]
    [InlineData("{\"FormatVersion\":1,\"ZoomPercent\":100,\"Theme\":\"System\",\"LastSettingsGroup\":\"Display\",\"Window\":{\"Width\":0,\"Height\":700,\"X\":null,\"Y\":null},\"Trace\":\"Plain\",\"HasSeenTechDemoNotice\":false}")]
    public void UnsupportedOrIncompleteShapeIsRefused(string json)
    {
        using var folder = new TemporaryFolder();
        var path = folder.File("user-preferences.json");
        File.WriteAllText(path, json);

        var store = new FileUserPreferencesStore(path);

        Assert.Equal(PreferenceState.Refused, store.State);
        Assert.Equal(UserPreferences.Defaults, store.Current);
    }

    [Fact]
    public void UnavailableFileKeepsTheChoiceOnlyInTheCurrentSession()
    {
        using var folder = new TemporaryFolder();
        var path = folder.File("preferences-as-a-directory");
        Directory.CreateDirectory(path);
        var store = new FileUserPreferencesStore(path);

        var result = store.Update(preferences => preferences with { ZoomPercent = 125 });

        Assert.Equal(PreferenceWriteState.SessionOnly, result.State);
        Assert.Equal(PreferenceState.Unavailable, store.State);
        Assert.Equal(125, store.Current.ZoomPercent);
        Assert.NotNull(result.Problem);
    }

    private sealed class TemporaryFolder : IDisposable
    {
        private readonly string _path = Directory.CreateTempSubdirectory("motif-user-preferences-").FullName;

        public string File(string name) => Path.Combine(_path, name);

        public void Dispose() => Directory.Delete(_path, recursive: true);
    }
}
