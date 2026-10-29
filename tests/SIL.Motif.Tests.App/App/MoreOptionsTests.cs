using System.Text.Json;
using Avalonia.Controls;
using Avalonia.VisualTree;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.App.Walkthrough;
using SettingsGroup = SIL.Motif.App.ViewModels.SettingsGroup;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class MoreOptionsTests
{
    private static WritingSystemDisplay Ws(string id, WritingSystemKind kind) =>
        new(id, id, id.ToUpperInvariant(), kind, 0, true, "Sans", "", false, new Dictionary<string, double> { ["Normal"] = 11 });

    // Runs one Settings session over a preferences file, then closes the window.
    private static void Session(string path, Action<SettingsViewModel> body) =>
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await PageScreenshots.OpenOverSampleData(parse: false, settingsFilePath: path);
            try
            {
                var overview = workspace.PageModel<OverviewPageModel>();
                overview.Overview = overview.Overview! with
                {
                    WritingSystems = [Ws("v1", WritingSystemKind.Vernacular), Ws("a1", WritingSystemKind.Analysis)],
                };
                body(SettingsTestAccess.Model(window));
            }
            finally
            {
                window.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromSeconds(120));

    private static IReadOnlyDictionary<string, bool> Saved(string path) =>
        new FileUserPreferencesStore(path).Current.ExpandedSections;

    [Fact]
    public void FreshPreferencesShowEveryMoreOptionsExpanderCollapsed()
    {
        var path = SettingsViewTests.NewSettingsPath();
        try
        {
            Session(path, settings =>
            {
                Assert.False(settings.ShortcutListShown);
                Assert.False(settings.ReportIncludesExpanded);
                Assert.False(Assert.Single(settings.VernacularWritingSystems).DetailsExpanded);
                Assert.False(Assert.Single(settings.AnalysisWritingSystems).DetailsExpanded);
            });
            Assert.Empty(Saved(path));
        }
        finally { SettingsViewTests.DeleteSettingsPath(path); }
    }

    [Fact]
    public void ExpandedAndCollapsedStatePersistsAcrossNewSessions()
    {
        var path = SettingsViewTests.NewSettingsPath();
        try
        {
            Session(path, settings =>
            {
                settings.VernacularWritingSystems.Single().DetailsExpanded = true;
                settings.AnalysisWritingSystems.Single().DetailsExpanded = true;
                settings.ReportIncludesExpanded = true;
            });
            Session(path, settings =>
            {
                Assert.True(settings.VernacularWritingSystems.Single().DetailsExpanded);
                Assert.True(settings.AnalysisWritingSystems.Single().DetailsExpanded);
                Assert.True(settings.ReportIncludesExpanded);
                settings.VernacularWritingSystems.Single().DetailsExpanded = false;
                settings.ReportIncludesExpanded = false;
            });
            Session(path, settings =>
            {
                Assert.False(settings.VernacularWritingSystems.Single().DetailsExpanded);
                Assert.True(settings.AnalysisWritingSystems.Single().DetailsExpanded);
                Assert.False(settings.ReportIncludesExpanded);
            });
        }
        finally { SettingsViewTests.DeleteSettingsPath(path); }
    }

    [Fact]
    public void SelectingTheShortcutsGroupOrSearchingOpensTheListWithoutChangingTheRememberedCollapse()
    {
        var path = SettingsViewTests.NewSettingsPath();
        try
        {
            Session(path, settings =>
            {
                settings.ShortcutListShown = true;
                settings.ShortcutListShown = false;
            });
            Session(path, settings =>
            {
                Assert.False(settings.ShortcutListShown);
                settings.OpenGroup(SettingsGroup.KeyboardShortcuts);
                Assert.True(settings.ShortcutListShown);
                Assert.False(new FileUserPreferencesStore(path).Current.ExpandedSections["Settings.ShortcutList"]);
            });
            Session(path, settings =>
            {
                Assert.False(settings.ShortcutListShown);
                settings.ShortcutSearch = "zoom";
                Assert.True(settings.ShortcutListShown);
                Assert.False(new FileUserPreferencesStore(path).Current.ExpandedSections["Settings.ShortcutList"]);
            });
        }
        finally { SettingsViewTests.DeleteSettingsPath(path); }
    }

    [Fact]
    public void TogglingOneDetailsRowMovesItsSiblingsAndSavesOnce()
    {
        var path = SettingsViewTests.NewSettingsPath();
        try
        {
            AvaloniaHeadlessFixture.RunUntilComplete(async () =>
            {
                var counting = new CountingStore(new FileUserPreferencesStore(path));
                var (workspace, window) = await PageScreenshots.OpenOverSampleData(parse: false, preferencesStore: counting);
                try
                {
                    var overview = workspace.PageModel<OverviewPageModel>();
                    overview.Overview = overview.Overview! with
                    {
                        WritingSystems = [Ws("v1", WritingSystemKind.Vernacular), Ws("v2", WritingSystemKind.Vernacular)],
                    };
                    var rows = SettingsTestAccess.Model(window).VernacularWritingSystems;
                    rows[1].DetailsExpanded = true;
                    Assert.True(rows[0].DetailsExpanded);
                    Assert.Equal(1, counting.Updates);
                    var raised = new List<string?>();
                    rows[0].PropertyChanged += (_, e) => raised.Add(e.PropertyName);
                    rows[1].DetailsExpanded = false;
                    Assert.False(rows[0].DetailsExpanded);
                    Assert.Contains("DetailsExpanded", raised);
                    Assert.False(Saved(path)["Settings.VernacularDetails"]);
                    Assert.Single(Saved(path));
                    Assert.Equal(2, counting.Updates);
                }
                finally
                {
                    window.Close();
                    await workspace.DisposeAsync();
                }
            }, TimeSpan.FromSeconds(120));
        }
        finally { SettingsViewTests.DeleteSettingsPath(path); }
    }

    [Fact]
    public void TheReportIncludesExpanderWritesThroughToTheViewModel()
    {
        var path = SettingsViewTests.NewSettingsPath();
        try
        {
            AvaloniaHeadlessFixture.RunUntilComplete(async () =>
            {
                var (workspace, window) = await PageScreenshots.OpenOverSampleData(parse: false, settingsFilePath: path);
                try
                {
                    var page = SettingsTestAccess.Open(window);
                    var settings = SettingsTestAccess.Model(window);
                    settings.OpenGroup(SettingsGroup.HelpAndReports);
                    PageScreenshots.Settle(window);
                    var expander = page.GetVisualDescendants().OfType<Expander>()
                        .Single(e => e.Classes.Contains("settingsReportDetails"));
                    Assert.False(settings.ReportIncludesExpanded);
                    expander.IsExpanded = true;
                    Assert.True(settings.ReportIncludesExpanded);
                    Assert.True(Saved(path)["Settings.ReportIncludes"]);
                }
                finally
                {
                    window.Close();
                    await workspace.DisposeAsync();
                }
            }, TimeSpan.FromSeconds(120));
        }
        finally { SettingsViewTests.DeleteSettingsPath(path); }
    }

    private sealed class CountingStore(IUserPreferencesStore inner) : IUserPreferencesStore
    {
        public int Updates { get; private set; }
        public UserPreferences Current => inner.Current;
        public PreferenceState State => inner.State;
        public PreferenceProblem? Problem => inner.Problem;
        public event EventHandler? Changed { add => inner.Changed += value; remove => inner.Changed -= value; }
        public PreferenceWriteResult Update(Func<UserPreferences, UserPreferences> edit) { Updates++; return inner.Update(edit); }
        public PreferenceWriteResult Reset() => inner.Reset();
        public void Flush() => inner.Flush();
    }

    [Fact]
    public void KeysAreStableNonLocalizedAndSurviveAStoreSaveAndReload()
    {
        var path = SettingsViewTests.NewSettingsPath();
        try
        {
            Session(path, settings =>
            {
                settings.VernacularWritingSystems.Single().DetailsExpanded = true;
                settings.AnalysisWritingSystems.Single().DetailsExpanded = true;
                settings.AnalysisWritingSystems.Single().DetailsExpanded = false;
                settings.ReportIncludesExpanded = true;
                settings.ShortcutListShown = true;
            });
            var expected = new Dictionary<string, bool>
            {
                ["Settings.AnalysisDetails"] = false,
                ["Settings.ReportIncludes"] = true,
                ["Settings.ShortcutList"] = true,
                ["Settings.VernacularDetails"] = true,
            };
            Assert.Equal(expected.OrderBy(p => p.Key), Saved(path).OrderBy(p => p.Key));
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            Assert.Equal(expected.Keys.Order(), document.RootElement.GetProperty("ExpandedSections")
                .EnumerateObject().Select(p => p.Name).Order());
        }
        finally { SettingsViewTests.DeleteSettingsPath(path); }
    }
}
