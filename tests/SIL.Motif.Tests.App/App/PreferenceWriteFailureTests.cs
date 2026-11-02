using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Responses;
using Xunit;
using ViewGroup = SIL.Motif.App.ViewModels.SettingsGroup;

namespace SIL.Motif.Tests.App;

/// <summary>A preference whose write failed keeps its session value in the store, and a later good write does not lose it.</summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class PreferenceWriteFailureTests
{
    private static WritingSystemDisplay Ws(string id, WritingSystemKind kind) =>
        new(id, id, id.ToUpperInvariant(), kind, 0, true, "Sans", "", false, new Dictionary<string, double> { ["Normal"] = 11 });

    // Each case changes one preference through the Settings page, then says whether the store holds that change.
    public static TheoryData<string> Preferences => new()
    {
        "Zoom", "Theme", "Group", "ShortcutList", "ReportIncludes", "VernacularDetails", "AnalysisDetails",
    };

    private static void Change(SettingsViewModel settings, string preference)
    {
        switch (preference)
        {
            case "Zoom": settings.ZoomPercent = 125; break;
            case "Theme": settings.ThemeChoice = WindowThemeChoice.Dark; break;
            case "Group": settings.OpenGroup(ViewGroup.About); break;
            case "ShortcutList": settings.ShortcutListShown = true; break;
            case "ReportIncludes": settings.ReportIncludesExpanded = true; break;
            case "VernacularDetails": settings.VernacularWritingSystems.Single().DetailsExpanded = true; break;
            case "AnalysisDetails": settings.AnalysisWritingSystems.Single().DetailsExpanded = true; break;
            default: throw new ArgumentOutOfRangeException(nameof(preference));
        }
    }

    private static bool Shown(SettingsViewModel settings, string preference) => preference switch
    {
        "Zoom" => settings.ZoomPercent == 125,
        "Theme" => settings.ThemeChoice == WindowThemeChoice.Dark,
        "Group" => settings.SelectedGroup == ViewGroup.About,
        "ShortcutList" => settings.ShortcutListShown,
        "ReportIncludes" => settings.ReportIncludesExpanded,
        "VernacularDetails" => settings.VernacularWritingSystems.Single().DetailsExpanded,
        "AnalysisDetails" => settings.AnalysisWritingSystems.Single().DetailsExpanded,
        _ => throw new ArgumentOutOfRangeException(nameof(preference)),
    };

    private static bool Saved(UserPreferences preferences, string preference) => preference switch
    {
        "Zoom" => preferences.ZoomPercent == 125,
        "Theme" => preferences.Theme == UserTheme.Dark,
        "Group" => preferences.LastSettingsGroup == SIL.Motif.App.Services.SettingsGroup.About,
        "ShortcutList" => preferences.ExpandedSections.GetValueOrDefault("Settings.ShortcutList"),
        "ReportIncludes" => preferences.ExpandedSections.GetValueOrDefault("Settings.ReportIncludes"),
        "VernacularDetails" => preferences.ExpandedSections.GetValueOrDefault("Settings.VernacularDetails"),
        "AnalysisDetails" => preferences.ExpandedSections.GetValueOrDefault("Settings.AnalysisDetails"),
        _ => throw new ArgumentOutOfRangeException(nameof(preference)),
    };

    // A directory where the file belongs makes every write fail until it is removed.
    private static string BlockedPath()
    {
        var folder = Directory.CreateTempSubdirectory("motif-preference-failure-").FullName;
        var path = Path.Combine(folder, "user-preferences.json");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void Run(string preference, Action<SettingsViewModel, FileUserPreferencesStore, string> body)
    {
        var path = BlockedPath();
        try
        {
            AvaloniaHeadlessFixture.RunUntilComplete(async () =>
            {
                var store = new FileUserPreferencesStore(path);
                var (workspace, window) = await PageScreenshots.OpenOverSampleData(parse: false, preferencesStore: store);
                try
                {
                    var overview = workspace.PageModel<OverviewPageModel>();
                    overview.Overview = overview.Overview! with
                    {
                        WritingSystems = [Ws("v1", WritingSystemKind.Vernacular), Ws("a1", WritingSystemKind.Analysis)],
                    };
                    body(SettingsTestAccess.Model(window), store, path);
                }
                finally
                {
                    window.Close();
                    await workspace.DisposeAsync();
                }
            }, TimeSpan.FromSeconds(120));
        }
        finally
        {
            if (Directory.Exists(Path.GetDirectoryName(path))) Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Theory]
    [MemberData(nameof(Preferences))]
    public void AFailedWriteLeavesTheRowAndTheStoreAgreeingForTheSession(string preference)
    {
        Run(preference, (settings, store, _) =>
        {
            Change(settings, preference);

            Assert.Equal(PreferenceState.Unavailable, store.State);
            Assert.True(Shown(settings, preference));
            Assert.True(Saved(store.Current, preference));
        });
    }

    [Theory]
    [MemberData(nameof(Preferences))]
    public void ALaterGoodWriteKeepsTheChoiceWhoseWriteFailed(string preference)
    {
        Run(preference, (settings, store, path) =>
        {
            Change(settings, preference);
            Assert.Equal(PreferenceState.Unavailable, store.State);

            Directory.Delete(path);
            if (preference == "Zoom") settings.ThemeChoice = WindowThemeChoice.Light;
            else settings.ZoomPercent = 150;

            Assert.Equal(PreferenceState.Ready, store.State);
            Assert.True(Shown(settings, preference));
            Assert.True(Saved(store.Current, preference));
            Assert.True(Saved(new FileUserPreferencesStore(path).Current, preference));
        });
    }
}
