using System.Text.Json;
using SIL.Motif.App.Services;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class UserPreferencesV2Tests
{
    private const string V1File =
        "{\"FormatVersion\":1,\"ZoomPercent\":150,\"Theme\":\"Dark\",\"LastSettingsGroup\":\"Parsing\"," +
        "\"Window\":{\"Width\":900,\"Height\":700,\"X\":10,\"Y\":20},\"Trace\":\"Expert\",\"HasSeenTechDemoNotice\":true}";

    [Fact]
    public void FormatOneFileIsUpgradedInMemoryAndKeepsEveryPreference()
    {
        using var folder = new TemporaryFolder();
        var path = folder.File("user-preferences.json");
        File.WriteAllText(path, V1File);

        var store = new FileUserPreferencesStore(path);

        Assert.Equal(PreferenceState.Ready, store.State);
        var current = store.Current;
        Assert.Equal(4, current.FormatVersion);
        Assert.Equal(150, current.ZoomPercent);
        Assert.Equal(UserTheme.Dark, current.Theme);
        Assert.Equal(SettingsGroup.Parsing, current.LastSettingsGroup);
        Assert.Equal(new WindowPlacement(900, 700, 10, 20), current.Window);
        Assert.Equal(TracePresentation.Expert, current.Trace);
        Assert.True(current.HasSeenTechDemoNotice);
        Assert.Empty(current.ExpandedSections);
        Assert.Empty(current.ProjectViews);
        Assert.Equal(V1File, File.ReadAllText(path));
    }

    [Fact]
    public void SavingAnUpgradedFileWritesTheLatestFormatThatReadsBackIdentically()
    {
        using var folder = new TemporaryFolder();
        var path = folder.File("user-preferences.json");
        File.WriteAllText(path, V1File);
        var store = new FileUserPreferencesStore(path);

        store.Update(p => p with { Theme = UserTheme.Light });

        using (var document = JsonDocument.Parse(File.ReadAllText(path)))
            Assert.Equal(4, document.RootElement.GetProperty("FormatVersion").GetInt32());
        var reread = new FileUserPreferencesStore(path);
        Assert.Equal(PreferenceState.Ready, reread.State);
        Assert.Equal(store.Current, reread.Current);
        Assert.Equal(150, reread.Current.ZoomPercent);
        Assert.Equal(UserTheme.Light, reread.Current.Theme);
    }

    [Fact]
    public void ExpandersAndProjectViewsRoundTripThroughTheFile()
    {
        using var folder = new TemporaryFolder();
        var path = folder.File("user-preferences.json");
        var store = new FileUserPreferencesStore(path);

        store.SetExpanded("Setup.MoreOptions", true);
        store.SetExpanded("Settings.MoreOptions", false);
        store.SetViewState("id:a", "Statistics", new ViewState("Count", true, "rare"));
        store.SetViewState("id:a", "TextWords", new ViewState(null, false, "Unassessed"));
        store.SetViewState("path:b", "Statistics", new ViewState("Form", false, null));

        var reread = new FileUserPreferencesStore(path);
        Assert.Equal(store.Current, reread.Current);
        Assert.True(reread.GetExpanded("Setup.MoreOptions"));
        Assert.False(reread.GetExpanded("Settings.MoreOptions"));
        Assert.Null(reread.GetExpanded("never.set"));
        Assert.Equal(new ViewState("Count", true, "rare"), reread.GetViewState("id:a", "Statistics"));
        Assert.Equal(new ViewState(null, false, "Unassessed"), reread.GetViewState("id:a", "TextWords"));
        Assert.Equal(new ViewState("Form", false, null), reread.GetViewState("path:b", "Statistics"));
        Assert.Null(reread.GetViewState("path:b", "TextWords"));
        Assert.Null(reread.GetViewState("id:other", "Statistics"));

        store.SetViewState("path:b", "Statistics", null);
        Assert.Null(new FileUserPreferencesStore(path).GetViewState("path:b", "Statistics"));
    }

    [Fact]
    public void MemoryStoreHoldsTheSameMaps()
    {
        var store = new MemoryUserPreferencesStore();
        store.SetExpanded("k", true);
        store.SetViewState("id:a", "v", new ViewState("c", false, "f"));
        Assert.True(store.GetExpanded("k"));
        Assert.Equal(new ViewState("c", false, "f"), store.GetViewState("id:a", "v"));
    }

    [Fact]
    public void NewerFormatFileIsRefusedAndNeverOverwritten()
    {
        using var folder = new TemporaryFolder();
        var path = folder.File("user-preferences.json");
        var newer = V1File.Replace("\"FormatVersion\":1", "\"FormatVersion\":4") + " ";
        File.WriteAllText(path, newer);
        var store = new FileUserPreferencesStore(path);

        Assert.Equal(PreferenceState.Refused, store.State);
        Assert.Equal(PreferenceWriteState.Refused, store.SetExpanded("k", true).State);
        store.Flush();
        Assert.Equal(newer, File.ReadAllText(path));
    }

    [Fact]
    public void ProjectKeyPrefersTheStableIdAndOtherwiseNormalisesThePath()
    {
        var guid = Guid.NewGuid();
        Assert.Equal("id:" + guid.ToString("D"),
            ProjectPreferenceKey.For(guid.ToString("B").ToUpperInvariant(), @"C:\a\p.fwdata"));
        Assert.Equal(ProjectPreferenceKey.For(guid.ToString(), @"C:\a\p.fwdata"),
            ProjectPreferenceKey.For(guid.ToString(), @"D:\elsewhere\p.fwdata"));
        if (OperatingSystem.IsWindows())
        {
            Assert.Equal("path:c:/data/p/p.fwdata", ProjectPreferenceKey.For(null, @"C:\Data\P\p.fwdata"));
            Assert.Equal(ProjectPreferenceKey.For(null, @"C:\Data\P\p.fwdata"),
                ProjectPreferenceKey.For("  ", "c:/DATA/p/P.fwdata/"));
        }

        var full = Path.Combine(Path.GetTempPath(), "Data", "P", "p.fwdata");
        var normalised = ProjectPreferenceKey.For(null, full);
        Assert.Equal(normalised, ProjectPreferenceKey.For(null, full + Path.DirectorySeparatorChar));
        Assert.Equal(normalised, ProjectPreferenceKey.For(null, Path.Combine(Path.GetDirectoryName(full)!, "x", "..", "p.fwdata")));
        Assert.NotEqual(ProjectPreferenceKey.For(null, "/x/a.fwdata"), ProjectPreferenceKey.For(null, "/x/b.fwdata"));
    }

    [Fact]
    public void PathsDifferingOnlyByCaseAreOneProjectExceptWhereTheFileSystemSeparatesThem()
    {
        var upper = Path.Combine(Path.GetTempPath(), "Data", "P.fwdata");
        var lower = Path.Combine(Path.GetTempPath(), "data", "p.fwdata");

        if (OperatingSystem.IsLinux())
            Assert.NotEqual(ProjectPreferenceKey.For(null, upper), ProjectPreferenceKey.For(null, lower));
        else
            Assert.Equal(ProjectPreferenceKey.For(null, upper), ProjectPreferenceKey.For(null, lower));
    }

    [Fact]
    public void StateSavedUnderTheOldLowerCasedPathKeyIsStillReadAndMovesToTheFullKeyOnSave()
    {
        var project = Path.Combine(Path.GetTempPath(), "Mixed", "Case", "p.fwdata");
        var oldKey = "path:" + Path.GetFullPath(project).Replace('\\', '/').ToLowerInvariant();
        var store = new MemoryUserPreferencesStore();
        store.SetViewState(oldKey, "Statistics", new ViewState("Count", true, "rare"));
        var memory = new ProjectViewMemory(store);
        memory.OpenProject(project);

        Assert.Equal(new ViewState("Count", true, "rare"), memory.Load(memory.ProjectKey, "Statistics"));

        memory.Save(memory.ProjectKey, "Statistics", new ViewState("Form", false, null));

        Assert.Equal(new ViewState("Form", false, null), memory.Load(memory.ProjectKey, "Statistics"));
        if (memory.ProjectKey != oldKey) Assert.Null(store.GetViewState(oldKey, "Statistics"));
    }

    [Fact]
    public void AStoredPathTheFileSystemRejectsStillGivesAKeyAndDoesNotThrow()
    {
        var bad = "/x/a\0b.fwdata ";
        var key = ProjectPreferenceKey.For(null, bad);
        Assert.StartsWith("path:", key);
        Assert.Equal(key, ProjectPreferenceKey.For(null, bad));
        Assert.NotEqual(key, ProjectPreferenceKey.For(null, "/x/a\0c.fwdata"));

        var memory = new ProjectViewMemory(new MemoryUserPreferencesStore());
        Assert.Null(Record.Exception(() => memory.OpenProject(bad)));
        Assert.Equal(key, memory.ProjectKey);
        Assert.True(memory.IsOpenPath(bad));
        Assert.False(memory.IsOpenPath("/x/other.fwdata"));
    }

    private sealed class TemporaryFolder : IDisposable
    {
        private readonly string _path = Directory.CreateTempSubdirectory("motif-user-preferences-v2-").FullName;

        public string File(string name) => Path.Combine(_path, name);

        public void Dispose() => Directory.Delete(_path, recursive: true);
    }

    [Fact]
    public void AnAllZeroGuidIsNoIdAndFallsBackToThePath()
    {
        var path = ProjectPreferenceKey.For(null, "/x/a.fwdata");
        Assert.Equal(path, ProjectPreferenceKey.For(Guid.Empty.ToString(), "/x/a.fwdata"));
        Assert.Equal(path, ProjectPreferenceKey.For("{00000000-0000-0000-0000-000000000000}", "/x/a.fwdata"));
        Assert.StartsWith("id:", ProjectPreferenceKey.For(Guid.NewGuid().ToString(), "/x/a.fwdata"));
    }

    [Fact]
    public void ProjectViewsAreCappedAndTheNewestProjectIsAlwaysKept()
    {
        var store = new MemoryUserPreferencesStore();
        for (var index = 0; index < UserPreferenceViewState.MaxProjects + 25; index++)
            store.SetViewState($"path:/p{index:D4}", "v", new ViewState(null, false, "f"));

        Assert.Equal(UserPreferenceViewState.MaxProjects, store.Current.ProjectViews.Count);
        Assert.NotNull(store.GetViewState($"path:/p{UserPreferenceViewState.MaxProjects + 24:D4}", "v"));

        store.SetViewState("path:/p0100", "other", new ViewState(null, false, "g"));
        Assert.Equal(UserPreferenceViewState.MaxProjects, store.Current.ProjectViews.Count);
    }

    [Fact]
    public void AStoredFilterIsCutToTheLimit()
    {
        var store = new MemoryUserPreferencesStore();
        store.SetViewState("id:a", "v", new ViewState("c", true, new string('x', 900)));

        var saved = store.GetViewState("id:a", "v");
        Assert.Equal(UserPreferenceViewState.MaxFilterLength, saved!.Filter!.Length);
        Assert.Equal("c", saved.SortColumn);
        Assert.True(saved.SortDescending);
    }
}
