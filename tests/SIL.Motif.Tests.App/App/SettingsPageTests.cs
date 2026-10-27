using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Automation;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Host.Preferences;
using Avalonia.VisualTree;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using Xunit;
using SIL.Motif.Tests.App.Walkthrough;
using Group = SIL.Motif.App.ViewModels.SettingsGroup;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class SettingsPageTests
{
    private sealed class SettingsUriLauncher : IUriLauncher
    {
        public Task<bool> LaunchAsync(Uri uri, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    [Fact]
    public void SettingsIsRegisteredOutsideThePrimaryRail()
    {
        var entry = PageRegistry.For(WorkspacePage.Settings);
        Assert.False(entry.ShowInRail);
        Assert.Equal("motif-page-settings", SIL.Motif.App.AutomationIds.ForPage(WorkspacePage.Settings));
        var help = new HelpPopupViewModel(new SettingsUriLauncher());
        help.ShowForPage(WorkspacePage.Settings);
        Assert.Equal("Settings", help.Title);
        Assert.All(PageRegistry.Entries.Where(item => item.Page != WorkspacePage.Settings),
            item => Assert.True(item.ShowInRail));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WorkspaceRegistryOpensSettingsWithWorkingResetControls(bool assignAfterViewCreation)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var previousTheme = Application.Current!.RequestedThemeVariant;
            var preferences = new MemoryUserPreferencesStore();
            var (workspace, frame) = FakeComposedWindow.Create(preferencesStore: preferences);
            var settings = WindowSettings(frame);
            var model = workspace.PageModel<SettingsPageModel>();
            model.Settings = assignAfterViewCreation ? null : settings;
            var entry = PageRegistry.For(WorkspacePage.Settings);
            var page = Assert.IsType<SettingsPage>(entry.CreateView(workspace.PageOf(WorkspacePage.Settings).Model));
            var window = new MotifTestWindow { Content = page, Width = 820, Height = 600 };
            try
            {
                window.Show();
                if (assignAfterViewCreation) model.Settings = settings;
                workspace.ShowPageCommand.Execute(WorkspacePage.Settings);
                PageScreenshots.Settle(window);
                Assert.Equal(WorkspacePage.Settings, workspace.CurrentPage);
                CaptureSettings(window, $"registry-{assignAfterViewCreation}-default");
                Assert.Same(settings, page.DataContext);
                Assert.Same(settings, page.Navigation.DataContext);
                Assert.DoesNotContain(workspace.VisiblePages, item => item.Page == WorkspacePage.Settings);
                var display = page.FindControl<StackPanel>("SettingsDisplaySection")!;
                var themeReset = display.GetVisualDescendants().OfType<Button>().Single(button =>
                    AutomationProperties.GetName(button) == "Reset theme to match the operating system");
                var zoomReset = display.GetVisualDescendants().OfType<Button>().Single(button =>
                    AutomationProperties.GetName(button) == "Reset zoom to 100%");
                Assert.False(themeReset.IsEnabled);
                Assert.False(zoomReset.IsEnabled);
                settings.ThemeChoice = WindowThemeChoice.Dark;
                settings.ZoomPercent = 150;
                PageScreenshots.Settle(window);
                Assert.True(themeReset.IsEnabled);
                Assert.True(zoomReset.IsEnabled);
                CaptureSettings(window, $"registry-{assignAfterViewCreation}-changed");
                HeadlessClick.Click(window, themeReset, "Reset theme to match the operating system");
                HeadlessClick.Click(window, zoomReset, "Reset zoom to 100%");
                PageScreenshots.Settle(window);
                Assert.Equal(UserTheme.System, preferences.Current.Theme);
                Assert.Equal(100, preferences.Current.ZoomPercent);
                Assert.False(themeReset.IsEnabled);
                Assert.False(zoomReset.IsEnabled);
                CaptureSettings(window, $"registry-{assignAfterViewCreation}-reset");
            }
            finally
            {
                Application.Current.RequestedThemeVariant = previousTheme;
                window.Close();
                if (!ReferenceEquals(window, frame)) frame.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(2));
    }

    [Fact]
    public void TheProductionFrameOpensItsRegisteredSettingsViewWithTheExistingOwnerAndWorkingReset()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var previousTheme = Application.Current!.RequestedThemeVariant;
            var preferences = new MemoryUserPreferencesStore();
            var (workspace, frame) = FakeComposedWindow.Create(preferencesStore: preferences);
            var settings = WindowSettings(frame);
            try
            {
                frame.Show();
                workspace.ShowPageCommand.Execute(WorkspacePage.Settings);
                PageScreenshots.Settle(frame);
                var page = frame.PageView<SettingsPage>();
                Assert.True(page.IsEffectivelyVisible);
                Assert.Equal(WorkspacePage.Settings, workspace.CurrentPage);
                Assert.DoesNotContain(workspace.VisiblePages, entry => entry.Page == WorkspacePage.Settings);
                CaptureSettings(frame, "production-frame-default");
                Assert.Same(settings, workspace.PageModel<SettingsPageModel>().Settings);
                Assert.Same(settings, page.DataContext);
                Assert.Same(settings, page.Navigation.DataContext);
                var display = page.FindControl<StackPanel>("SettingsDisplaySection")!;
                var reset = display.GetVisualDescendants().OfType<Button>().Single(button =>
                    AutomationProperties.GetName(button) == "Reset theme to match the operating system");
                Assert.False(reset.IsEnabled);
                settings.ThemeChoice = WindowThemeChoice.Dark;
                PageScreenshots.Settle(frame);
                Assert.True(reset.IsEnabled);
                CaptureSettings(frame, "production-frame-changed");
                HeadlessClick.Click(frame, reset, "Reset theme to match the operating system");
                PageScreenshots.Settle(frame);
                Assert.Equal(WindowThemeChoice.System, settings.ThemeChoice);
                Assert.Equal(UserTheme.System, preferences.Current.Theme);
                Assert.False(reset.IsEnabled);
                CaptureSettings(frame, "production-frame-reset");
            }
            finally
            {
                Application.Current.RequestedThemeVariant = previousTheme;
                frame.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(2));
    }

    [Fact]
    public void SettingsAssignmentOnlyUpdatesAttachedRegistryViewsAndNeverWidensTheContext()
    {
        Assert.DoesNotContain(typeof(WorkspaceContext).GetProperties(
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic), property => property.PropertyType == typeof(SettingsViewModel));
        Assert.DoesNotContain(typeof(WorkspaceContext).GetFields(
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic), field => field.FieldType == typeof(SettingsViewModel));
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var previousTheme = Application.Current!.RequestedThemeVariant;
            var (workspace, frame) = FakeComposedWindow.Create();
            var settings = WindowSettings(frame);
            var model = workspace.PageModel<SettingsPageModel>();
            model.Settings = settings;
            var page = Assert.IsType<SettingsPage>(PageRegistry.For(WorkspacePage.Settings).CreateView(model));
            var window = new MotifTestWindow { Content = page, Width = 820, Height = 600 };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);
                Assert.Same(settings, page.DataContext);
                window.Content = null;
                PageScreenshots.Settle(window);
                model.Settings = null;
                Assert.Same(settings, page.DataContext);
                window.Content = page;
                PageScreenshots.Settle(window);
                Assert.Null(page.DataContext);
                Assert.Null(page.Navigation.DataContext);
                model.Settings = settings;
                PageScreenshots.Settle(window);
                Assert.Same(settings, page.DataContext);
                Assert.Same(settings, page.Navigation.DataContext);
            }
            finally
            {
                Application.Current.RequestedThemeVariant = previousTheme;
                window.Close();
                if (!ReferenceEquals(window, frame)) frame.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(2));
    }

    [Fact]
    public void ShortcutListIsReadyBeforeSelectingASection()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, frame) = await PageScreenshots.OpenOverSampleData(parse: false);
            try
            {
                var settings = SettingsTestAccess.Model(frame);
                Assert.Equal(Group.Display, settings.SelectedGroup);
                Assert.NotEmpty(settings.ShortcutEntries);
                var writingSystems = settings.PageGroupOptions.Single(option => option.Group == Group.WritingSystems);
                var texts = settings.PageGroupOptions.Single(option => option.Group == Group.Texts);
                Assert.Equal(settings.PageGroupOptions.IndexOf(writingSystems) + 1,
                    settings.PageGroupOptions.IndexOf(texts));
                Assert.Contains(settings.PageGroupOptions, option => option.Group == Group.Texts);
            }
            finally
            {
                frame.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(2));
    }

    [Theory]
    [InlineData(640, 600)]
    [InlineData(1040, 780)]
    public void SectionNavigationScrollsOneListWithoutHidingOtherSections(int width, int height)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var preferences = new MemoryUserPreferencesStore();
            var (workspace, frame) = await PageScreenshots.OpenOverSampleData(parse: false, preferencesStore: preferences,
                configure: (client, _) => client.DefaultSelectionIs(new NamedSelectionProjection("Default",
                    [Guid.Parse("11111111-0000-0000-0000-000000000001")], [], string.Empty, string.Empty,
                    new SelectionParsingLimits(StepCap.Default, SelectionTimeLimitMode.Estimated, null), "1")));
            var settings = SettingsTestAccess.Model(frame);
            var page = SettingsTestAccess.Open(frame);
            var window = frame;
            window.Width = width;
            window.Height = height;
            try
            {
                window.Show();
                PageScreenshots.Settle(window);
                var scroll = page.FindControl<ScrollViewer>("SettingsListScroll")!;
                Assert.NotNull(scroll);
                Assert.Equal(8, settings.PageGroupOptions.Count);
                Assert.NotEmpty(settings.ShortcutEntries);
                Assert.Single(page.GetVisualDescendants().OfType<Border>()
                    .Single(border => border.Classes.Contains("settingsHeader"))
                    .GetVisualDescendants().OfType<Button>());
                Assert.DoesNotContain(page.GetVisualDescendants().OfType<Button>(),
                    button => button.IsEffectivelyVisible && button.Content?.ToString() == "Reset saved preferences");
                settings.OpenGroup(Group.About);
                PageScreenshots.Settle(window);
                Assert.True(scroll.Offset.Y > 0);
                var about = page.FindControl<StackPanel>("SettingsAboutSection")!;
                var aboutLocation = about.TranslatePoint(default, scroll)!.Value;
                Assert.True(aboutLocation.Y + Math.Min(about.Bounds.Height, scroll.Viewport.Height) <= scroll.Viewport.Height + 1,
                    "Choosing About must bring its contents into view.");
                foreach (var name in new[] { "Display", "KeyboardShortcuts", "Parsing", "ProposalChecks",
                             "WritingSystems", "Texts", "HelpAndReports", "About" })
                    Assert.True(page.FindControl<StackPanel>($"Settings{name}Section")!.IsVisible);
                settings.OpenGroup(Group.Texts);
                PageScreenshots.Settle(window);
                var section = page.FindControl<StackPanel>("SettingsTextsSection")!;
                var location = section.TranslatePoint(default, scroll)!.Value;
                Assert.InRange(location.Y, -1, scroll.Bounds.Height);
                Assert.True(location.Y + Math.Min(section.Bounds.Height, scroll.Viewport.Height) <= scroll.Viewport.Height + 1,
                    "Choosing Texts must bring its controls into view.");
                Assert.Equal(SIL.Motif.App.Services.SettingsGroup.Texts, preferences.Current.LastSettingsGroup);
                HeadlessClick.Click(window, page.FindControl<Button>("SettingsClose")!, "Leave Settings");
                page = SettingsTestAccess.Open(frame);
                Assert.Equal(Group.Texts, settings.SelectedGroup);
                var (reopenedWorkspace, reopenedFrame) = FakeComposedWindow.Create(preferencesStore: preferences);
                try
                {
                    Assert.Equal(Group.Texts, SettingsTestAccess.Model(reopenedFrame).SelectedGroup);
                }
                finally
                {
                    reopenedFrame.Close();
                    await reopenedWorkspace.DisposeAsync();
                }
                Assert.Same(settings, page.Navigation.DataContext);
                Assert.True(scroll.Extent.Width <= scroll.Viewport.Width + 1);
                Assert.DoesNotContain(page.GetVisualDescendants().OfType<Button>(),
                    button => button.Content?.ToString() is "Reset layout" or "Reset all");
                Assert.DoesNotContain(typeof(SettingsViewModel).GetProperties(),
                    property => property.Name is "ResetAllCommand" or "ResetLayoutCommand");
                Assert.Contains(page.GetVisualDescendants().OfType<TextBlock>(),
                    text => text.IsEffectivelyVisible && text.Text == settings.ParsingFooter);
                Assert.Contains(page.GetVisualDescendants().OfType<TextBlock>(),
                    text => text.IsEffectivelyVisible && text.Text == "After FieldWorks saves, press Refresh to see a change here.");
            }
            finally
            {
                window.Close();
                if (!ReferenceEquals(window, frame)) frame.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(2));
    }

    [Fact]
    public void ThemeAndZoomResetPersistTheirExistingDefaults()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var previousTheme = Application.Current!.RequestedThemeVariant;
            var preferences = new MemoryUserPreferencesStore();
            var (workspace, frame) = await PageScreenshots.OpenOverSampleData(parse: false, preferencesStore: preferences,
                configure: (client, _) => client.DefaultSelectionIs(new NamedSelectionProjection("Default",
                    [Guid.Parse("11111111-0000-0000-0000-000000000001")], [], string.Empty, string.Empty,
                    new SelectionParsingLimits(StepCap.Default, SelectionTimeLimitMode.Estimated, null), "1")));
            try
            {
                var settings = SettingsTestAccess.Model(frame);
                settings.ZoomPercent = 150;
                settings.ThemeChoice = WindowThemeChoice.Dark;
                Assert.True(settings.ResetThemeCommand.CanExecute(null));
                settings.ResetThemeCommand.Execute(null);
                settings.ResetZoomCommand.Execute(null);
                Assert.Equal(UserTheme.System, preferences.Current.Theme);
                Assert.Equal(100, preferences.Current.ZoomPercent);
                Assert.False(settings.ResetThemeCommand.CanExecute(null));
                Assert.False(settings.CanResetZoom);
            }
            finally
            {
                Application.Current.RequestedThemeVariant = previousTheme;
                frame.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(2));
    }
    [Fact]
    public void IndividualParsingAndCheckResetsKeepOtherChoicesAndPersist()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            FakeCommandClient? fake = null;
            var preferences = new MemoryUserPreferencesStore();
            var (workspace, frame) = await PageScreenshots.OpenOverSampleData(parse: false, preferencesStore: preferences,
                configure: (client, _) =>
                {
                    fake = client;
                    client.DefaultSelectionIs(new NamedSelectionProjection("Default", [Guid.Parse("11111111-0000-0000-0000-000000000001")], [],
                        string.Empty, string.Empty,
                        new SelectionParsingLimits(new StepCap(400000), SelectionTimeLimitMode.Explicit, 2000), "1"));
                });
            var checks = workspace.Context.DefaultProposalChecks;
            var previousTheme = Application.Current!.RequestedThemeVariant;
            try
            {
                var settings = SettingsTestAccess.Model(frame);
                settings.ZoomPercent = 150;
                settings.ThemeChoice = WindowThemeChoice.Dark;
                settings.ChooseStoredAnalysesCommand.Execute(StoredAnalysisComparison.All);
                settings.OpenGroup(Group.Parsing);
                Assert.True(settings.ResetStepLimitCommand.CanExecute(null));
                await settings.ResetStepLimitCommand.ExecuteAsync(null);
                Assert.Equal(StepCap.DefaultSteps, settings.StepLimit);
                Assert.Equal(2m, settings.TimeLimitSeconds);
                Assert.False(settings.IsUsingEstimate);
                Assert.True(settings.IsStoredAnalysesAll);
                var saved = await fake!.ReadDefaultSelectionAsync(
                    new ReadDefaultSelectionRequest(workspace.Context.ProjectPath!), CancellationToken.None);
                Assert.Equal(StepCap.Default, saved.Value!.Selection!.Limits.PerWordStepLimit);
                Assert.Equal(SelectionTimeLimitMode.Explicit, saved.Value.Selection.Limits.TimeMode);
                Assert.False(settings.ResetStepLimitCommand.CanExecute(null));
                settings.ResetStoredAnalysesCommand.Execute(null);
                Assert.Equal(StoredAnalysisComparison.Budget, checks.StoredAnalyses);
                Assert.False(settings.ResetStoredAnalysesCommand.CanExecute(null));
                Assert.Equal(150, settings.ZoomPercent);
                Assert.Equal(WindowThemeChoice.Dark, settings.ThemeChoice);
                settings.UseMotifEstimate();
                await settings.CommitParsingLimitsAsync();
                Assert.True(settings.IsUsingEstimate);
                Assert.False(settings.ShowUseEstimate);
            }
            finally
            {
                Application.Current.RequestedThemeVariant = previousTheme;
                frame.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(2));
    }

    [Theory]
    [InlineData(640, 600, 100)]
    [InlineData(640, 600, 150)]
    [InlineData(1040, 780, 100)]
    public void KeyboardNavigationReachesSectionsSearchAndBack(int width, int height, int zoom)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, frame) = await PageScreenshots.OpenOverSampleData(parse: false,
                configure: (client, _) => client.DefaultSelectionIs(new NamedSelectionProjection("Default",
                    [Guid.Parse("11111111-0000-0000-0000-000000000001")], [], string.Empty, string.Empty,
                    new SelectionParsingLimits(StepCap.Default, SelectionTimeLimitMode.Estimated, null), "1")));
            var settings = SettingsTestAccess.Model(frame);
            var page = SettingsTestAccess.Open(frame);
            var window = frame;
            window.Width = width;
            window.Height = height;
            settings.ZoomPercent = zoom;
            HeadlessClick.Click(frame, frame.FindControl<Button>("SettingsButton")!, "Settings");
            try
            {
                window.Show();
                PageScreenshots.Settle(window);
                var groups = page.Navigation.FindControl<ListBox>("SettingsGroups")!;
                page.FocusSelectedGroup();
                Assert.Same(groups, window.FocusManager!.GetFocusedElement());
                window.KeyPress(Key.End, RawInputModifiers.None, PhysicalKey.End, null);
                PageScreenshots.Settle(window);
                Assert.Equal(Group.About, settings.SelectedGroup);
                window.KeyPress(Key.Home, RawInputModifiers.None, PhysicalKey.Home, null);
                window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
                PageScreenshots.Settle(window);
                Assert.Equal(Group.KeyboardShortcuts, settings.SelectedGroup);
                page.FocusShortcutSearch();
                PageScreenshots.Settle(window);
                var search = page.FindControl<TextBox>("ShortcutSearchBox")!;
                Assert.Same(search, window.FocusManager.GetFocusedElement());
                search.Text = "zoom";
                Assert.NotEmpty(settings.ShortcutEntries);
                Assert.Single(page.GetVisualDescendants().OfType<Border>()
                    .Single(border => border.Classes.Contains("settingsHeader"))
                    .GetVisualDescendants().OfType<Button>());
                Assert.DoesNotContain(page.GetVisualDescendants().OfType<Button>(),
                    button => button.IsEffectivelyVisible && button.Content?.ToString() == "Reset saved preferences");
                Assert.All(settings.ShortcutEntries, entry => Assert.Contains("zoom", entry.Action, StringComparison.OrdinalIgnoreCase));
                var closes = 0;
                page.CloseRequested += (_, _) => closes++;
                window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                Assert.Equal(string.Empty, settings.ShortcutSearch);
                Assert.Equal(0, closes);
                window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                Assert.Equal(1, closes);
                page = SettingsTestAccess.Open(frame);
                if (page.Navigation.IsEffectivelyVisible && window.ClientSize.Width / settings.ZoomScale <
                    (double)Application.Current!.FindResource("Component.Shell.SidePanelCollapseWidth")!)
                {
                    var collapse = Assert.Single(frame.GetVisualDescendants().OfType<Button>(),
                        button => button.Name == "SidePanelCollapseButton" && button.IsEffectivelyVisible);
                    HeadlessClick.Click(frame, collapse, "Collapse side bar");
                    PageScreenshots.Settle(frame);
                }
                foreach (var group in settings.PageGroupOptions)
                {
                    groups.SelectedItem = group;
                    PageScreenshots.Settle(window);
                    var scroll = page.FindControl<ScrollViewer>("SettingsListScroll")!;
                    Assert.True(scroll.Extent.Width <= scroll.Viewport.Width + 1);
                }
                foreach (var name in new[] { "Reset zoom to 100%", "Match the operating system theme", "Step limit per word",
                             "Time limit per word in seconds", "Use all texts", "Open Help", "Report a problem", "Copy versions" })
                {
                    var target = page.GetVisualDescendants().OfType<Control>().Single(control =>
                        AutomationProperties.GetName(control) == name);
                    Assert.True(target.IsEffectivelyVisible, name);
                    target.BringIntoView();
                    PageScreenshots.Settle(window);
                    var scroll = page.FindControl<ScrollViewer>("SettingsListScroll")!;
                    var position = target.TranslatePoint(default, scroll)!.Value;
                    Assert.InRange(position.X, -1, scroll.Bounds.Width);
                    Assert.True(position.X + target.Bounds.Width <= scroll.Bounds.Width + 1, name);
                    Assert.InRange(position.Y, -1, scroll.Bounds.Height);
                    Assert.True(position.Y + target.Bounds.Height <= scroll.Bounds.Height + 1, name);
                }
                Assert.DoesNotContain(page.FindControl<StackPanel>("SettingsKeyboardShortcutsSection")!
                    .GetVisualDescendants().OfType<TextBox>(), control => !ReferenceEquals(control, search));
            }
            finally
            {
                window.Close();
                if (!ReferenceEquals(window, frame)) frame.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(2));
    }

    [Fact]
    public void WritingSystemSummaryShowsFontSourceAndMissingFontGuidanceBeforeDetails()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, frame) = await PageScreenshots.OpenOverSampleData(parse: false,
                configure: (client, _) => client.DefaultSelectionIs(new NamedSelectionProjection("Default",
                    [Guid.Parse("11111111-0000-0000-0000-000000000001")], [], string.Empty, string.Empty,
                    new SelectionParsingLimits(StepCap.Default, SelectionTimeLimitMode.Estimated, null), "1")));
            var overview = workspace.PageModel<OverviewPageModel>();
            overview.Overview = overview.Overview! with
            {
                WritingSystems = [new WritingSystemDisplay("v-missing", "Missing font language", "V", WritingSystemKind.Vernacular,
                    0, true, "Motif Missing Test Font", "ss01=1", false, new Dictionary<string, double> { ["Normal"] = 11 })
                {
                    StyleFonts = new Dictionary<string, WritingSystemStyleFont>
                    { ["Normal"] = new("Motif Missing Test Font", "ss01=1") },
                }],
            };
            var settings = SettingsTestAccess.Model(frame);
            var page = SettingsTestAccess.Open(frame);
            var window = frame;
            window.Width = 640;
            window.Height = 600;
            try
            {
                window.Show();
                settings.OpenGroup(Group.WritingSystems);
                PageScreenshots.Settle(window);
                var row = Assert.Single(settings.VernacularWritingSystems);
                Assert.Equal("Fallback", row.FontSourceText);
                Assert.True(row.IsFontMissing);
                Assert.True(settings.PageGroupOptions.Single(option => option.Group == Group.WritingSystems).HasWarning);
                var section = page.FindControl<StackPanel>("SettingsWritingSystemsSection")!;
                var details = Assert.Single(section.GetVisualDescendants().OfType<Expander>());
                Assert.False(details.IsExpanded);
                Assert.Contains(section.GetVisualDescendants().OfType<TextBlock>(),
                    text => text.IsEffectivelyVisible && text.Text == row.MissingFontCaption);
                details.IsExpanded = true;
                PageScreenshots.Settle(window);
                Assert.Contains(section.GetVisualDescendants().OfType<TextBlock>(),
                    text => text.IsEffectivelyVisible && text.Text == "11 pt (Normal)");
                Assert.Empty(workspace.Context.TextStyles.MissingFontNotices);
                var scroll = page.FindControl<ScrollViewer>("SettingsListScroll")!;
                Assert.True(scroll.Extent.Width <= scroll.Viewport.Width + 1);
            }
            finally
            {
                window.Close();
                if (!ReferenceEquals(window, frame)) frame.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(2));
    }

    [Fact]
    public void TextsSectionUsesAndPersistsTheExistingSavedSelection()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            FakeCommandClient? fake = null;
            var (workspace, frame) = await PageScreenshots.OpenOverSampleData(parse: false,
                configure: (client, _) =>
                {
                    fake = client;
                    client.DefaultSelectionIs(new NamedSelectionProjection("Default", [Guid.Parse("11111111-0000-0000-0000-000000000001")], [],
                        string.Empty, string.Empty,
                        new SelectionParsingLimits(StepCap.Default, SelectionTimeLimitMode.Estimated, null), "1"));
                });
            var settings = SettingsTestAccess.Model(frame);
            var page = SettingsTestAccess.Open(frame);
            var window = frame;
            window.Width = 820;
            window.Height = 600;
            try
            {
                window.Show();
                settings.OpenGroup(Group.Texts);
                PageScreenshots.Settle(window);
                var useAll = page.FindControl<Button>("UseAllTextsButton")!;
                Assert.True(useAll.IsEnabled);
                HeadlessClick.Click(window, useAll, "Use all texts");
                await settings.UseAllTextsCommand.ExecutionTask!;
                PageScreenshots.Settle(window);
                Assert.False(useAll.IsEnabled);
                Assert.True(workspace.Selection.AreAllTextsSelected);
                var saved = await fake!.ReadDefaultSelectionAsync(
                    new ReadDefaultSelectionRequest(workspace.Context.ProjectPath!), CancellationToken.None);
                Assert.Equal(workspace.Selection.Texts.Select(text => text.Id).Order(), saved.Value!.Selection!.TextIds.Order());
                Assert.Equal(StepCap.Default, saved.Value.Selection.Limits.PerWordStepLimit);
                await workspace.Context.Setup!.ProjectOpenedAsync(workspace.Context.ProjectPath!);
                Assert.True(workspace.Selection.AreAllTextsSelected);
            }
            finally
            {
                window.Close();
                if (!ReferenceEquals(window, frame)) frame.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(2));
    }

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void PageListUsesComponentInsetsAndThemeRoles(string theme)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var previous = Application.Current!.RequestedThemeVariant;
            var (workspace, frame) = await PageScreenshots.OpenOverSampleData(parse: false);
            var page = SettingsTestAccess.Open(frame);
            var window = frame;
            window.Width = 1920;
            window.Height = 1080;
            try
            {
                Application.Current.RequestedThemeVariant = theme == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
                window.Show();
                PageScreenshots.Settle(window);
                var list = page.GetVisualDescendants().OfType<StackPanel>()
                    .Single(panel => panel.Classes.Contains("settingsList"));
                Assert.Equal(Avalonia.Layout.HorizontalAlignment.Stretch, list.HorizontalAlignment);
                var inset = Assert.IsType<Border>(list.Parent);
                Assert.Equal((Thickness)Application.Current.FindResource("Component.Settings.PageInset")!, inset.Padding);
                Assert.Equal(default, list.Margin);
                var heading = page.FindControl<StackPanel>("SettingsDisplaySection")!.Children.OfType<TextBlock>().First();
                Assert.Equal((double)Application.Current.FindResource("Intent.Type.Label")!, heading.FontSize);
                Assert.True(Application.Current.TryGetResource("Intent.TextSecondary", window.ActualThemeVariant, out var brush));
                Assert.Equal(((ISolidColorBrush)brush!).Color,
                    ((ISolidColorBrush)heading.Foreground!).Color);
            }
            finally
            {
                Application.Current.RequestedThemeVariant = previous;
                window.Close();
                if (!ReferenceEquals(window, frame)) frame.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(2));
    }

    [Fact]
    public void RegisteredPageRemainsUsableAfterRememberingTexts()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, frame) = await PageScreenshots.OpenOverSampleData(parse: false);
            var settings = SettingsTestAccess.Model(frame);
            var page = SettingsTestAccess.Open(frame);
            var window = frame;
            window.Width = 640;
            window.Height = 600;
            try
            {
                window.Show();
                for (var reopen = 0; reopen < 2; reopen++)
                {
                    settings.OpenGroup(Group.Texts);
                    page = SettingsTestAccess.Open(frame);
                    page.FocusSelectedGroup();
                    PageScreenshots.Settle(window);
                    Assert.Equal(Group.Texts, settings.SelectedGroup);
                    Assert.Equal(Group.Texts, Assert.IsType<SettingsGroupOption>(
                        page.Navigation.FindControl<ListBox>("SettingsGroups")!.SelectedItem).Group);
                }
                Assert.Equal(8, settings.PageGroupOptions.Count);
            }
            finally
            {
                window.Close();
                if (!ReferenceEquals(window, frame)) frame.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(2));
    }

    [Fact]
    public void RefreshedWritingSystemsReflowAtTheCurrentWidth()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, frame) = await PageScreenshots.OpenOverSampleData(parse: false);
            var overview = workspace.PageModel<OverviewPageModel>();
            var settings = SettingsTestAccess.Model(frame);
            settings.ZoomPercent = 150;
            var page = SettingsTestAccess.Open(frame);
            var window = frame;
            window.Width = 640;
            window.Height = 600;
            try
            {
                window.Show();
                PageScreenshots.Settle(window);
                overview.Overview = overview.Overview! with
                {
                    WritingSystems = [new WritingSystemDisplay("sw", "Swahili", "SW", WritingSystemKind.Vernacular,
                        0, true, "Charis SIL", "", false, new Dictionary<string, double> { ["Normal"] = 12 })],
                };
                settings.OpenGroup(Group.WritingSystems);
                PageScreenshots.Settle(window);
                var row = Assert.Single(page.GetVisualDescendants().OfType<Grid>(),
                    grid => grid.Classes.Contains("settingsWritingSummary"));
                Assert.Single(row.ColumnDefinitions);
                Assert.Equal(3, row.RowDefinitions.Count);
                Assert.Equal(2, Grid.GetRow(row.Children[2]));
                var scroll = page.FindControl<ScrollViewer>("SettingsListScroll")!;
                Assert.True(scroll.Extent.Width <= scroll.Viewport.Width + 1);
            }
            finally
            {
                window.Close();
                if (!ReferenceEquals(window, frame)) frame.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(2));
    }

    private sealed class CheckChoicesStore(bool refuse) : IDefaultProposalChecksStore
    {
        public StoredAnalysisComparison StoredAnalyses { get; private set; } = StoredAnalysisComparison.Budget;

        public void SetStoredAnalyses(StoredAnalysisComparison mode)
        {
            if (refuse) throw new IOException("The test store refused this choice.");
            StoredAnalyses = mode;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StoredAnalysisChoicesKeepTheSavedPolicyWhenAChangeIsRefused(bool refuse)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var store = new CheckChoicesStore(refuse);
            var (workspace, frame) = FakeComposedWindow.Create(defaultProposalChecks: store);
            var settings = SettingsTestAccess.Model(frame);
            var page = SettingsTestAccess.Open(frame);
            var window = frame;
            window.Width = 820;
            window.Height = 600;
            try
            {
                window.Show();
                PageScreenshots.Settle(window);
                var all = page.FindControl<RadioButton>("StoredAnalysesAllChoice")!;
                var budget = page.FindControl<RadioButton>("StoredAnalysesBudgetChoice")!;
                all.BringIntoView();
                PageScreenshots.Settle(window);
                HeadlessClick.Click(window, all, "Every word, slower on large projects");
                PageScreenshots.Settle(window);
                Assert.Equal(refuse ? StoredAnalysisComparison.Budget : StoredAnalysisComparison.All, store.StoredAnalyses);
                Assert.Equal(refuse, settings.HasStoredAnalysesRefusal);
                Assert.Equal(refuse, budget.IsChecked);
                Assert.Equal(!refuse, all.IsChecked);
            }
            finally
            {
                window.Close();
                if (!ReferenceEquals(window, frame)) frame.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(2));
    }

    internal static SettingsViewModel WindowSettings(MainWindow frame) => SettingsTestAccess.Model(frame);

    internal static void CaptureSettings(Window window, string name)
    {
        if (Environment.GetEnvironmentVariable(ScreenshotFactAttribute.FolderVariable) is not { Length: > 0 } folder)
            return;
        Directory.CreateDirectory(folder);
        using var capture = window.CaptureRenderedFrame() ??
            throw new InvalidOperationException("The registered Settings page did not render.");
        capture.Save(Path.Combine(folder, name + ".png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

}
