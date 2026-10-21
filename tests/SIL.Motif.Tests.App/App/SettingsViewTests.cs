using SIL.Motif.App;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Preferences;
using SIL.Motif.Tests.App.Walkthrough;
using SIL.Motif.Worker;
using Xunit;
using SettingsGroup = SIL.Motif.App.ViewModels.SettingsGroup;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class SettingsViewTests(AvaloniaHeadlessFixture avalonia)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UseAllTextsRestoresAndPersistsTheFullSelection(bool allBoxesAlreadyChecked)
    {
        var folder = Environment.GetEnvironmentVariable(ScreenshotFactAttribute.FolderVariable);
        if (!string.IsNullOrWhiteSpace(folder)) Directory.CreateDirectory(folder);
        var settingsPath = NewSettingsPath();
        try
        {
            AvaloniaHeadlessFixture.RunUntilComplete(async () =>
            {
                FakeCommandClient? fake = null;
                var (workspace, window) = await PageScreenshots.OpenOverSampleData(parse: false,
                    configure: (client, _) =>
                    {
                        fake = client;
                        client.DefaultSelectionIs(new NamedSelectionProjection("Default", [SampleTextId], [],
                            string.Empty, string.Empty,
                            new SelectionParsingLimits(StepCap.Default, SelectionTimeLimitMode.Estimated, null), "1"));
                    }, settingsFilePath: settingsPath);
                try
                {
                    foreach (var text in workspace.Selection.Texts)
                        text.IsChecked = allBoxesAlreadyChecked || text.Id == SampleTextId;
                    var popup = OpenSettings(window);
                    SettingsTestAccess.Select(popup, SettingsGroup.Texts);
                    PageScreenshots.Settle(window);
                    var settings = Assert.IsType<SettingsViewModel>(popup.DataContext);
                    var useAllTexts = popup.FindControl<Button>("UseAllTextsButton")!;
                    Assert.Single(popup.GetVisualDescendants().OfType<Button>(),
                        button => AutomationProperties.GetName(button) == "Use all texts");
                    Assert.Single(popup.GetVisualDescendants().OfType<TextBlock>(),
                        text => text.IsEffectivelyVisible && text.Text == SIL.Motif.App.MotifAppStrings.TextsToParseLabel);
                    workspace.Selection.SearchText = "Hadithi";
                    Assert.Equal(allBoxesAlreadyChecked ? "2 of 2 Texts selected" : "1 of 2 Texts selected",
                        settings.TextSelectionSummary);
                    workspace.Selection.SearchText = string.Empty;
                    Assert.True(useAllTexts.IsEnabled);
                    if (!string.IsNullOrWhiteSpace(folder)) SavePageState(window, popup, folder, "settings-text-selection-all-before.png");

                    HeadlessClick.Click(window, useAllTexts, "Use all texts");
                    await settings.UseAllTextsCommand.ExecutionTask!;

                    var expectedTextIds = workspace.Selection.Texts.Select(text => text.Id).Order().ToArray();
                    Assert.Equal(expectedTextIds, workspace.Selection.ChosenTextIds.Order());
                    Assert.True(workspace.Selection.AreAllTextsSelected);
                    Assert.False(settings.UseAllTextsCommand.CanExecute(null));
                    Assert.False(settings.CanUseAllTexts);
                    PageScreenshots.Settle(window);
                    Assert.False(useAllTexts.IsEnabled);
                    var saved = await fake!.ReadDefaultSelectionAsync(
                        new ReadDefaultSelectionRequest(workspace.Context.ProjectPath!), CancellationToken.None);
                    Assert.True(saved.Succeeded, saved.Refusal?.Message);
                    Assert.Equal(expectedTextIds, saved.Value!.Selection!.TextIds.Order());
                    Assert.Equal("2", saved.Value.Selection.Revision);

                    await workspace.Context.Setup!.ProjectOpenedAsync(workspace.Context.ProjectPath!);
                    Assert.Equal(expectedTextIds, workspace.Selection.ChosenTextIds.Order());
                    Assert.False(useAllTexts.IsEnabled);
                    if (!string.IsNullOrWhiteSpace(folder)) SavePageState(window, popup, folder, "settings-text-selection-all-after.png");
                }
                finally
                {
                    window.Close();
                    await workspace.DisposeAsync();
                }
            }, TimeSpan.FromMinutes(2));
        }
        finally
        {
            DeleteSettingsPath(settingsPath);
        }
    }

    [Fact]
    public void TextSelectionSummaryUsesTheSingularForOneText()
    {
        var settingsPath = NewSettingsPath();
        try
        {
            AvaloniaHeadlessFixture.RunUntilComplete(async () =>
            {
                var (workspace, window) = await PageScreenshots.OpenOverSampleData(parse: false,
                    configure: (client, _) =>
                    {
                        client.ListTextsCompletesWith(new TextInventoryResponse(
                            [new TextChoiceSummary(SampleTextId, "One story")], HasBaseline: true));
                        client.DefaultSelectionIs(new NamedSelectionProjection("Default", [SampleTextId], [],
                            string.Empty, string.Empty,
                            new SelectionParsingLimits(StepCap.Default, SelectionTimeLimitMode.Estimated, null), "1"));
                    },
                    settingsFilePath: settingsPath);
                try
                {
                    workspace.Selection.SelectAllTexts();
                    var popup = OpenSettings(window);
                    SettingsTestAccess.Select(popup, SettingsGroup.Texts);
                    PageScreenshots.Settle(window);
                    var settings = Assert.IsType<SettingsViewModel>(popup.DataContext);
                    Assert.Equal("1 of 1 Text selected", settings.TextSelectionSummary);
                    var folder = Environment.GetEnvironmentVariable(ScreenshotFactAttribute.FolderVariable);
                    if (!string.IsNullOrWhiteSpace(folder))
                    {
                        Directory.CreateDirectory(folder);
                        SavePageState(window, popup, folder, "settings-one-text.png");
                    }
                }
                finally
                {
                    window.Close();
                    await workspace.DisposeAsync();
                }
            }, TimeSpan.FromMinutes(2));
        }
        finally
        {
            DeleteSettingsPath(settingsPath);
        }
    }

    [Fact]
    public void SettingsMarkupKeepsItsInteractiveFamiliesBound()
    {
        var settingsPath = NewSettingsPath();
        var priorTheme = ReadTheme();
        try
        {
            avalonia.Invoke(() =>
            {
                var (workspace, window) = FakeComposedWindow.Create(settingsFilePath: settingsPath);
                try
                {
                    window.Show();
                    PageScreenshots.Settle(window);
                    var page = OpenSettings(window);
                    Assert.Equal(SIL.Motif.App.MotifAppStrings.Display, ActiveGroupHeading(page));
                    HeadlessClick.Click(window, Button(page, "Zoom in"), "Zoom in");
                    HeadlessClick.Click(window, Button(page, "Dark theme"), "Dark theme");

                    var groups = page.Navigation.FindControl<ListBox>("SettingsGroups")!;
                    SettingsTestAccess.Select(page, SettingsGroup.KeyboardShortcuts);
                    PageScreenshots.Settle(window);
                    var search = page.FindControl<TextBox>("ShortcutSearchBox")!;
                    search.Text = "zoom";
                    PageScreenshots.Settle(window);
                    var clear = Button(page, "Clear shortcut search");
                    Assert.True(clear.IsEffectivelyVisible);
                    HeadlessClick.Click(window, clear, "Clear shortcut search");
                    Assert.Equal(string.Empty, search.Text);

                    SettingsTestAccess.Select(page, SettingsGroup.HelpAndReports);
                    PageScreenshots.Settle(window);
                    var details = Assert.Single(page.GetVisualDescendants().OfType<Expander>(),
                        control => AutomationProperties.GetName(control) == "What a report includes");
                    Assert.False(details.IsExpanded);
                    ClickControl(window, details, "What a report includes");
                    PageScreenshots.Settle(window);
                    Assert.True(details.IsExpanded);
                    var reportDescription = Assert.Single(details.GetVisualDescendants().OfType<TextBlock>(),
                        text => text.Classes.Contains("settingsReportList")).Text!;
                    var refusal = new SIL.Motif.Contract.Commands.Refusal(
                        SIL.Motif.Contract.Commands.RefusalCodes.AssessParserUnavailable,
                        FailureReason.Refused, "PanGloss is unavailable.",
                        new Dictionary<string, string> { ["parserNotFound"] = "true" });
                    var reportText = ProblemReport.FromRefusal(WindowRefusal.From(refusal)).ToText();
                    foreach (var field in new[]
                    {
                        "Motif version", "PanGloss version", "Operating system", "operation",
                        "refusal code", "exit status", "stack",
                    })
                    {
                        Assert.Contains(field, reportDescription, StringComparison.OrdinalIgnoreCase);
                        Assert.Contains(field, reportText, StringComparison.OrdinalIgnoreCase);
                    }
                    Assert.Contains("Safe failure facts", reportDescription, StringComparison.Ordinal);
                    Assert.Contains("Failure fact parserNotFound: true", reportText, StringComparison.Ordinal);

                    var openHelp = Button(page, "Open Help");
                    HeadlessClick.Click(window, openHelp, "Open Help");
                    Assert.Equal(WorkspacePage.Settings, workspace.CurrentPage);
                    Assert.True(page.IsEffectivelyVisible);
                    Assert.True(Assert.IsType<Flyout>(Button(window, "Help for the current page").Flyout).IsOpen);
                }
                finally
                {
                    window.Close();
                    workspace.DisposeAsync().AsTask().GetAwaiter().GetResult();
                }
            });
            using var saved = JsonDocument.Parse(File.ReadAllText(settingsPath));
            Assert.Equal(110, saved.RootElement.GetProperty("ZoomPercent").GetInt32());
            Assert.Equal("Dark", saved.RootElement.GetProperty("Theme").GetString());
        }
        finally
        {
            RestoreTheme(priorTheme);
            DeleteSettingsPath(settingsPath);
        }
    }

    [Fact]
    public void SettingsGroupsMoveWithArrowKeys()
    {
        var settingsPath = NewSettingsPath();
        try
        {
            avalonia.Invoke(() =>
            {
                var (workspace, window) = FakeComposedWindow.Create(settingsFilePath: settingsPath);
                try
                {
                    window.Show();
                    var page = OpenSettings(window);
                    var groups = page.Navigation.FindControl<ListBox>("SettingsGroups")!;
                    Assert.Equal(SettingsGroup.Display, Assert.IsType<SettingsGroupOption>(groups.SelectedItem).Group);
                    Assert.True(groups.Focus());

                    window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
                    PageScreenshots.Settle(window);
                    Assert.Equal(SettingsGroup.KeyboardShortcuts, Assert.IsType<SettingsGroupOption>(groups.SelectedItem).Group);
                    Assert.Equal(SIL.Motif.App.MotifAppStrings.KeyboardShortcuts, ActiveGroupHeading(page));

                    window.KeyPress(Key.End, RawInputModifiers.None, PhysicalKey.End, null);
                    PageScreenshots.Settle(window);
                    Assert.Equal(SettingsGroup.About, Assert.IsType<SettingsGroupOption>(groups.SelectedItem).Group);
                    Assert.Equal(SIL.Motif.App.MotifAppStrings.AboutMotif, ActiveGroupHeading(page));

                    window.KeyPress(Key.Home, RawInputModifiers.None, PhysicalKey.Home, null);
                    PageScreenshots.Settle(window);
                    Assert.Equal(SettingsGroup.Display, Assert.IsType<SettingsGroupOption>(groups.SelectedItem).Group);
                    Assert.Equal(SIL.Motif.App.MotifAppStrings.Display, ActiveGroupHeading(page));
                }
                finally
                {
                    window.Close();
                    workspace.DisposeAsync().AsTask().GetAwaiter().GetResult();
                }
            });
        }
        finally
        {
            DeleteSettingsPath(settingsPath);
        }
    }

    [Fact]
    public void WritingSystemsGroupUsesProjectOrderAndMissingFontLinkOpensItsRow()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await PageScreenshots.OpenOverSampleData(parse: false);
            try
            {
                var systems = Enumerable.Range(0, 8).Select(index => new WritingSystemDisplay(
                    $"v-{index}", $"Vernacular {index}", $"v{index}", WritingSystemKind.Vernacular,
                    index, index == 0, index == 7 ? "Motif Missing Test Font" : "", "ss01=1", false,
                    new Dictionary<string, double> { ["Normal"] = 11d })
                {
                    StyleFonts = new Dictionary<string, WritingSystemStyleFont>
                    {
                        ["Normal"] = new(index == 7 ? "Motif Missing Test Font" : "", "ss01=1"),
                    },
                }).Append(new WritingSystemDisplay("a-0", "Analysis", "a0", WritingSystemKind.Analysis,
                    0, true, "", "", true, new Dictionary<string, double> { ["Normal"] = 12d }))
                    .ToArray();
                var overview = workspace.PageModel<OverviewPageModel>().Overview!;
                workspace.PageModel<OverviewPageModel>().Overview = overview with { WritingSystems = systems };
                PageScreenshots.Settle(window);

                var page = OpenSettings(window);
                var viewModel = Assert.IsType<SettingsViewModel>(page.DataContext);
                Assert.Equal(new[] { "v-0", "v-1", "v-2", "v-3", "v-4", "v-5", "v-6", "v-7" },
                    viewModel.VernacularWritingSystems.Select(row => row.Id));
                Assert.True(viewModel.PageGroupOptions.Single(option =>
                    option.Group == SIL.Motif.App.ViewModels.SettingsGroup.WritingSystems)
                    .HasWarning);

                var groups = page.Navigation.FindControl<ListBox>("SettingsGroups")!;
                SettingsTestAccess.Select(page, SettingsGroup.WritingSystems);
                PageScreenshots.Settle(window);
                Assert.Equal(string.Format(SIL.Motif.App.MotifAppStrings.WritingSystemsInProjectFormat, "Sample"), ActiveGroupHeading(page));
                var details = Assert.Single(page.GetVisualDescendants().OfType<Expander>(),
                    control => AutomationProperties.GetName(control) == "Writing system details for Vernacular 7");
                Assert.False(details.IsExpanded);
                ClickControl(window, details, "Writing system details for Vernacular 7");
                Assert.True(details.IsExpanded);
                Assert.Contains(page.GetVisualDescendants().OfType<TextBlock>(), text =>
                    text.IsEffectivelyVisible && text.Text == "11 pt (Normal)");
                Assert.Contains(page.GetVisualDescendants().OfType<MarkGlyph>(), mark =>
                    mark.IsEffectivelyVisible && AutomationProperties.GetName(mark) == "A font is missing");
                Assert.DoesNotContain(workspace.Context.TextStyles.MissingFontNotices,
                    notice => notice.WritingSystemId == "v-7");

                HeadlessClick.Click(window, Button(page, "Leave Settings"), "Leave Settings");
                workspace.Context.TextStyles.MissingFontNotices.Add(
                    new WritingSystemFontNotice("v-7", "FieldWorks asks for a missing font."));
                PageScreenshots.Settle(window);
                var noticeOpener = window.FindControl<Button>("NoticeDetailsButton")!;
                HeadlessClick.Click(window, noticeOpener, "See project notices");
                var noticeFlyout = Assert.IsType<Flyout>(noticeOpener.Flyout);
                Assert.True(noticeFlyout.IsOpen);
                var noticeDetails = Assert.IsAssignableFrom<Control>(noticeFlyout.Content);
                ClickControl(window, Button(noticeDetails, "See writing systems"), "See writing systems");
                PageScreenshots.Settle(window);

                Assert.Equal(SIL.Motif.App.ViewModels.SettingsGroup.WritingSystems, viewModel.SelectedGroup);
                var writingSystemScroll = page.FindControl<ScrollViewer>("SettingsListScroll")!;
                Assert.True(writingSystemScroll.Offset.Y > 0);
            }
            finally
            {
                window.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void ThemeSegmentsMoveWithArrowsAndShareOneTabStop()
    {
        var settingsPath = NewSettingsPath();
        var priorTheme = ReadTheme();
        try
        {
            avalonia.Invoke(() =>
            {
                var (workspace, window) = FakeComposedWindow.Create(settingsFilePath: settingsPath);
                try
                {
                    window.Show();
                    var page = OpenSettings(window);
                    var light = Button(page, "Light theme");
                    var dark = Button(page, "Dark theme");
                    var system = Button(page, "Match the operating system theme");
                    Assert.Equal(-1, light.TabIndex);
                    Assert.Equal(-1, dark.TabIndex);
                    Assert.Equal(0, system.TabIndex);
                    Assert.True(system.Focus());

                    window.KeyPress(Key.Left, RawInputModifiers.None, PhysicalKey.ArrowLeft, null);
                    PageScreenshots.Settle(window);
                    Assert.True(dark.IsFocused);
                    Assert.Equal(0, dark.TabIndex);
                    Assert.Equal(-1, system.TabIndex);
                    Assert.Equal(ThemeVariant.Dark, Application.Current!.RequestedThemeVariant);

                    window.KeyPress(Key.Left, RawInputModifiers.None, PhysicalKey.ArrowLeft, null);
                    PageScreenshots.Settle(window);
                    Assert.True(light.IsFocused);
                    Assert.Equal(0, light.TabIndex);
                    Assert.Equal(-1, dark.TabIndex);
                    Assert.Equal(ThemeVariant.Light, Application.Current!.RequestedThemeVariant);

                    Assert.IsType<SettingsViewModel>(page.DataContext).ThemeChoice = WindowThemeChoice.System;
                    Assert.Equal(ThemeVariant.Default, Application.Current.RequestedThemeVariant);
                }
                finally
                {
                    window.Close();
                    workspace.DisposeAsync().AsTask().GetAwaiter().GetResult();
                }
            });
        }
        finally
        {
            RestoreTheme(priorTheme);
            DeleteSettingsPath(settingsPath);
        }
    }

    [Fact]
    public void CopyVersionsUsesTheProblemReportValuesAndConfirmsInPlace()
    {
        var settingsPath = NewSettingsPath();
        string? copied = null;
        string? confirmation = null;
        try
        {
            AvaloniaHeadlessFixture.RunUntilComplete(async () =>
            {
                var (workspace, window) = FakeComposedWindow.Create(settingsFilePath: settingsPath);
                try
                {
                    window.Show();
                    var page = OpenSettings(window);
                    SettingsTestAccess.Select(page, SettingsGroup.About);
                    PageScreenshots.Settle(window);
                    var copy = Button(page, "Copy versions");
                    HeadlessClick.Click(window, copy, "Copy versions");
                    for (var pass = 0; pass < 4; pass++)
                    {
                        await Task.Yield();
                        PageScreenshots.Settle(window);
                    }
                    confirmation = copy.Content?.ToString();
                    copied = await window.Clipboard!.TryGetTextAsync();
                }
                finally
                {
                    window.Close();
                    await workspace.DisposeAsync();
                }
            }, TimeSpan.FromMinutes(1));
            Assert.Equal("Copied", confirmation);
            Assert.Equal(ProblemReport.ForWindowAction().ToVersionText(), copied);
        }
        finally
        {
            DeleteSettingsPath(settingsPath);
        }
    }

    [Fact]
    public void ReportActionOpensTheExistingProblemReportPreview()
    {
        var settingsPath = NewSettingsPath();
        var previewOpened = false;
        try
        {
            AvaloniaHeadlessFixture.RunUntilComplete(async () =>
            {
                var (workspace, window) = FakeComposedWindow.Create(settingsFilePath: settingsPath);
                try
                {
                    window.Show();
                    var page = OpenSettings(window);
                    SettingsTestAccess.Select(page, SettingsGroup.HelpAndReports);
                    PageScreenshots.Settle(window);
                    HeadlessClick.Click(window, Button(page, "Report a problem"), "Report a problem");
                    for (var pass = 0; pass < 4; pass++)
                    {
                        await Task.Yield();
                        PageScreenshots.Settle(window);
                    }
                    previewOpened = window.CurrentProblemReportPreview is { IsVisible: true };
                    window.CurrentProblemReportPreview?.Close();
                }
                finally
                {
                    window.Close();
                    await workspace.DisposeAsync();
                }
            }, TimeSpan.FromMinutes(1));
            Assert.True(previewOpened);
        }
        finally
        {
            DeleteSettingsPath(settingsPath);
        }
    }

    [Fact]
    public void SettingsLinksOpenTheDocumentationAndMotifDataFolder()
    {
        var settingsPath = NewSettingsPath();
        var dataRoot = Path.Combine(Path.GetDirectoryName(settingsPath)!, "managed-data");
        var facts = new ApplicationFacts("0.2.0", "0.8.3", "Test operating system", false, true, dataRoot);
        var launcher = new RecordingUriLauncher();
        try
        {
            AvaloniaHeadlessFixture.RunUntilComplete(async () =>
            {
                var (workspace, window) = FakeComposedWindow.Create(settingsFilePath: settingsPath,
                    uriLauncher: launcher, applicationFacts: facts);
                try
                {
                    window.Show();
                    var page = OpenSettings(window);
                    SettingsTestAccess.Select(page, SettingsGroup.HelpAndReports);
                    PageScreenshots.Settle(window);
                    ClickControl(window, Button(page, "Open the Motif site"), "Open the Motif site");
                    for (var pass = 0; pass < 4; pass++)
                    {
                        await Task.Yield();
                        PageScreenshots.Settle(window);
                    }
                    Assert.Equal(new Uri("https://motif-docs.pages.dev"), Assert.Single(launcher.Launches));

                    page = OpenSettings(window);
                    SettingsTestAccess.Select(page, SettingsGroup.About);
                    PageScreenshots.Settle(window);
                    await Task.Delay(300);
                    ClickControl(window, Button(page, "Open Motif data folder"), "Open Motif data folder");
                    for (var pass = 0; pass < 4; pass++)
                    {
                        await Task.Yield();
                        PageScreenshots.Settle(window);
                    }
                    Assert.Equal(new Uri(dataRoot), launcher.Launches[1]);
                }
                finally
                {
                    window.Close();
                    await workspace.DisposeAsync();
                }
            }, TimeSpan.FromMinutes(1));
        }
        finally
        {
            DeleteSettingsPath(settingsPath);
        }
    }

    [Fact]
    public void UnsupportedPreferencesAreRefusedUntilExplicitlyReset()
    {
        var settingsPath = NewSettingsPath();
        const string original = "{\"SchemaVersion\":2,\"ZoomPercent\":125,\"Theme\":\"Dark\"}";
        File.WriteAllText(settingsPath, original);
        try
        {
            avalonia.Invoke(() =>
            {
                var (workspace, window) = FakeComposedWindow.Create(settingsFilePath: settingsPath);
                try
                {
                    window.Show();
                    var page = OpenSettings(window);
                    var refusal = Assert.Single(page.GetVisualDescendants().OfType<TextBlock>(), text =>
                        text.IsEffectivelyVisible && text.Text?.Contains("delete user-preferences.json", StringComparison.Ordinal) == true);
                    Assert.True(refusal.IsEffectivelyVisible);
                    HeadlessClick.Click(window, Button(page, "Zoom in"), "Zoom in");
                    Assert.Equal("110%", Assert.Single(page.GetVisualDescendants().OfType<TextBlock>(), text =>
                        text.Classes.Contains("settingsZoomValue")).Text);
                    Assert.Equal(original, File.ReadAllText(settingsPath));
                    HeadlessClick.Click(window, Button(page, "Reset saved preferences"), "Reset saved preferences");
                    Assert.Equal(100, new FileUserPreferencesStore(settingsPath).Current.ZoomPercent);
                    Assert.Equal(PreferenceState.Ready, new FileUserPreferencesStore(settingsPath).State);
                }
                finally
                {
                    window.Close();
                    workspace.DisposeAsync().AsTask().GetAwaiter().GetResult();
                }
            });
        }
        finally
        {
            DeleteSettingsPath(settingsPath);
        }
    }

    [Fact]
    public void ResetZoomReturnsToDefaultPersistsAcrossReloadAndIsDisabledAtDefault()
    {
        var settingsPath = NewSettingsPath();
        var priorTheme = ReadTheme();
        try
        {
            AvaloniaHeadlessFixture.RunUntilComplete(async () =>
            {
                var (workspace, window) = FakeComposedWindow.Create(settingsFilePath: settingsPath);
                try
                {
                    window.Show();
                    var popup = OpenSettings(window);
                    var settings = Assert.IsType<SettingsViewModel>(popup.DataContext);
                    var reset = Button(popup, "Reset zoom to 100%");
                    Assert.True(reset.IsEffectivelyVisible);
                    Assert.False(reset.IsEnabled);

                    settings.ZoomPercent = 125;
                    Assert.True(reset.IsEnabled);
                    Assert.Equal(125, new FileUserPreferencesStore(settingsPath).Current.ZoomPercent);
                    HeadlessClick.Click(window, reset, "Reset zoom to 100%");
                    Assert.Equal(100, settings.ZoomPercent);
                    Assert.True(reset.IsEffectivelyVisible);
                    Assert.False(reset.IsEnabled);
                    Assert.Equal(100, new FileUserPreferencesStore(settingsPath).Current.ZoomPercent);
                }
                finally
                {
                    window.Close();
                    await workspace.DisposeAsync();
                }

                var (reloadedWorkspace, reloadedWindow) = FakeComposedWindow.Create(settingsFilePath: settingsPath);
                try
                {
                    reloadedWindow.Show();
                    var reloadedPopup = OpenSettings(reloadedWindow);
                    var reloadedSettings = Assert.IsType<SettingsViewModel>(reloadedPopup.DataContext);
                    var reset = Button(reloadedPopup, "Reset zoom to 100%");
                    Assert.Equal(100, reloadedSettings.ZoomPercent);
                    Assert.True(reset.IsEffectivelyVisible);
                    Assert.False(reset.IsEnabled);
                }
                finally
                {
                    reloadedWindow.Close();
                    await reloadedWorkspace.DisposeAsync();
                }
            }, TimeSpan.FromMinutes(1));
        }
        finally
        {
            RestoreTheme(priorTheme);
            DeleteSettingsPath(settingsPath);
        }
    }

    [Fact]
    public void ParsingLimitFieldsShareAndCommitTheSetupSelection()
    {
        var settingsPath = NewSettingsPath();
        var priorTheme = ReadTheme();
        try
        {
            AvaloniaHeadlessFixture.RunUntilComplete(async () =>
            {
                FakeCommandClient? fake = null;
                var (workspace, window) = await PageScreenshots.OpenOverSampleData(parse: false,
                    configure: (client, _) =>
                    {
                        fake = client;
                        client.DefaultSelectionIs(new NamedSelectionProjection("Default", [SampleTextId], [],
                            string.Empty, string.Empty,
                            new SelectionParsingLimits(StepCap.Default, SelectionTimeLimitMode.Estimated, null), "1"));
                    }, settingsFilePath: settingsPath);
                try
                {
                    Assert.True(workspace.Context.Setup?.CanRunDefaultSelection);
                    var page = OpenSettings(window);
                    var groups = page.Navigation.FindControl<ListBox>("SettingsGroups")!;
                    SettingsTestAccess.Select(page, SettingsGroup.Parsing);
                    PageScreenshots.Settle(window);

                    var stepLimit = page.GetVisualDescendants().OfType<NumericUpDown>()
                        .Single(control => AutomationProperties.GetName(control) == "Step limit per word");
                    var timeLimit = page.GetVisualDescendants().OfType<NumericUpDown>()
                        .Single(control => AutomationProperties.GetName(control) == "Time limit per word in seconds");
                    var useEstimate = Button(page, "Use Motif's estimate");
                    var noStepLimit = Assert.Single(page.GetVisualDescendants().OfType<CheckBox>());
                    Assert.Equal(workspace.Selection.PerWordStepLimit, stepLimit.Value);
                    Assert.Equal(workspace.Selection.PerWordStepLimitUnbounded, noStepLimit.IsChecked);

                    Assert.False(useEstimate.IsEffectivelyVisible);
                    timeLimit.Value = 10m;
                    ClickControl(window, timeLimit, "Time limit per word in seconds");
                    PressEnter(window, timeLimit);
                    for (var pass = 0; pass < 4; pass++)
                    {
                        await Task.Yield();
                        PageScreenshots.Settle(window);
                    }
                    Assert.True(useEstimate.IsEffectivelyVisible);

                    ClickControl(window, useEstimate, "Use Motif's estimate");
                    Assert.Equal(8m, workspace.Selection.PerWordTimeLimitSeconds);
                    timeLimit.Value = 1m;
                    ClickControl(window, timeLimit, "Time limit per word in seconds");
                    PressEnter(window, timeLimit);
                    for (var pass = 0; pass < 4; pass++)
                    {
                        await Task.Yield();
                        PageScreenshots.Settle(window);
                    }
                    Assert.Equal(1m, workspace.Selection.PerWordTimeLimitSeconds);
                    Assert.True(useEstimate.IsEffectivelyVisible);

                    var settings = Assert.IsType<SettingsViewModel>(page.DataContext);
                    settings.StepLimit = 300000m;
                    Assert.False(settings.IsUsingMotifEstimate);
                    Assert.Equal(1m, settings.TimeLimitSeconds);
                    Assert.Null(await settings.CommitParsingLimitsAsync());
                    Assert.Equal(SelectionTimeLimitMode.Explicit,
                        fake!.SetSelectionLimitsRequests.Last().Limits.TimeMode);
                    settings.StepLimit = 200000m;
                    Assert.Null(await settings.CommitParsingLimitsAsync());

                    ClickControl(window, noStepLimit, "No step limit");
                    for (var pass = 0; pass < 4; pass++)
                    {
                        await Task.Yield();
                        PageScreenshots.Settle(window);
                    }
                    Assert.True(noStepLimit.IsChecked);
                    Assert.True(workspace.Selection.PerWordStepLimitUnbounded);
                    Assert.Null(workspace.Selection.PerWordTimeLimitSeconds);
                    Assert.False(timeLimit.IsEnabled);
                    Assert.False(useEstimate.IsEffectivelyVisible);
                    Assert.NotEmpty(fake!.SetSelectionLimitsRequests);

                    SettingsTestAccess.Select(page, SettingsGroup.Display);
                    SettingsTestAccess.Select(page, SettingsGroup.Parsing);
                    PageScreenshots.Settle(window);
                    ClickControl(window, noStepLimit, "No step limit");
                    for (var pass = 0; pass < 4; pass++)
                    {
                        await Task.Yield();
                        PageScreenshots.Settle(window);
                    }
                    var lastLimitSave = fake!.SetSelectionLimitsRequests.LastOrDefault();
                    var visibleRefusals = string.Join(" | ", page.GetVisualDescendants().OfType<TextBlock>()
                        .Where(text => text.IsEffectivelyVisible && text.Classes.Contains("settingsRefusal"))
                        .Select(text => text.Text));
                    Assert.False(noStepLimit.IsChecked,
                        $"The checkbox was restored after its save. Selection={workspace.Selection.PerWordStepLimitUnbounded}; " +
                        $"time={workspace.Selection.PerWordTimeLimitSeconds}; saves={fake.SetSelectionLimitsRequests.Count}; " +
                        $"last={lastLimitSave?.Limits.PerWordStepLimit}/{lastLimitSave?.Limits.ExplicitPerWordLimitMs}; " +
                        $"refusals={visibleRefusals}");
                    Assert.False(workspace.Selection.PerWordStepLimitUnbounded);
                    Assert.Equal(200000m, workspace.Selection.PerWordStepLimit);
                    Assert.True(timeLimit.IsEnabled);
                    Assert.Equal(1m, workspace.Selection.PerWordTimeLimitSeconds);
                    timeLimit.Value = null;
                    ClickControl(window, timeLimit, "Time limit per word in seconds");
                    PressEnter(window, timeLimit);
                    for (var pass = 0; pass < 4; pass++)
                    {
                        await Task.Yield();
                        PageScreenshots.Settle(window);
                    }
                    Assert.Contains(page.GetVisualDescendants().OfType<TextBlock>(), text =>
                        text.IsEffectivelyVisible && text.Text == "Enter a per-word time limit greater than zero.");
                    Assert.Equal(1m, workspace.Selection.PerWordTimeLimitSeconds);

                    ClickControl(window, stepLimit, "Step limit per word");
                    stepLimit.Value = 100000.5m;
                    PressEnter(window, stepLimit);
                    for (var pass = 0; pass < 4; pass++)
                    {
                        await Task.Yield();
                        PageScreenshots.Settle(window);
                    }

                    var refusal = page.GetVisualDescendants().OfType<TextBlock>().SingleOrDefault(text =>
                        text.IsEffectivelyVisible && text.Text ==
                        "The per-word step limit must be a positive whole number.");
                    Assert.True(refusal?.IsEffectivelyVisible == true,
                        $"The fractional step limit should be refused. Input={stepLimit.Value}; " +
                        $"selection={workspace.Selection.PerWordStepLimit}; " +
                        $"selection refusal={workspace.Selection.StepLimitValidationMessage}; " +
                        $"save count={fake.SetSelectionLimitsRequests.Count}; " +
                        $"last={fake.SetSelectionLimitsRequests.LastOrDefault()?.Limits.PerWordStepLimit}/" +
                        $"{fake.SetSelectionLimitsRequests.LastOrDefault()?.Limits.ExplicitPerWordLimitMs}; " +
                        $"visible refusals={string.Join(" | ", page.GetVisualDescendants().OfType<TextBlock>()
                            .Where(text => text.IsEffectivelyVisible && text.Classes.Contains("settingsRefusal"))
                            .Select(text => text.Text))}");
                    Assert.Equal(200000m, workspace.Selection.PerWordStepLimit);
                    LayoutAssertions.AssertCurrent(window);
                }
                finally
                {
                    window.Close();
                    await workspace.DisposeAsync();
                }
            }, TimeSpan.FromMinutes(2));
        }
        finally
        {
            RestoreTheme(priorTheme);
            DeleteSettingsPath(settingsPath);
        }
    }

    [Fact]
    public void AnalysisOptionsEditTheSameSavedSelectionLimitsAsSettings()
    {
        var settingsPath = NewSettingsPath();
        try
        {
            AvaloniaHeadlessFixture.RunUntilComplete(async () =>
            {
                FakeCommandClient? fake = null;
                var (workspace, window) = await PageScreenshots.OpenOverSampleData(parse: false,
                    configure: (client, _) =>
                    {
                        fake = client;
                        client.DefaultSelectionIs(new NamedSelectionProjection("Default", [SampleTextId], [],
                            string.Empty, string.Empty,
                            new SelectionParsingLimits(StepCap.Default, SelectionTimeLimitMode.Estimated, null), "1"));
                    }, settingsFilePath: settingsPath);
                try
                {
                    workspace.Context.OpenPage(WorkspacePage.Texts);
                    workspace.PageModel<TextsPageModel>().Tab = TextsTab.AnalyzeTexts;
                    PageScreenshots.Settle(window);
                    var panel = Assert.Single(window.GetVisualDescendants().OfType<SelectionPanel>());
                    Assert.True(panel.Settings is { HasProject: true, HasSavedSelection: true },
                        $"Inline Settings={panel.Settings is not null}; project={panel.Settings?.HasProject}; " +
                        $"saved={panel.Settings?.HasSavedSelection}; setup={workspace.Context.Setup?.CanRunDefaultSelection}; " +
                        $"setup path={workspace.Context.Setup?.ProjectPath}");
                    var options = panel.GetVisualDescendants().OfType<Expander>()
                        .Single(expander => expander.Header?.ToString() == MotifAppStrings.AnalysisOptions);
                    options.IsExpanded = true;
                    PageScreenshots.Settle(window);

                    var stepLimit = panel.GetVisualDescendants().OfType<NumericUpDown>()
                        .Single(control => AutomationProperties.GetName(control) == "Selection step limit per word");
                    stepLimit.Value = 300000m;
                    ClickControl(window, stepLimit, "Selection step limit per word");
                    PressEnter(window, stepLimit);
                    for (var pass = 0; pass < 4; pass++)
                    {
                        await Task.Yield();
                        PageScreenshots.Settle(window);
                    }

                    Assert.Equal(300000L, Assert.Single(fake!.SetSelectionLimitsRequests).Limits.PerWordStepLimit.Steps);
                    var useEstimate = Button(panel, "Use Motif's estimated time limit");
                    Assert.False(useEstimate.IsEffectivelyVisible);
                    var timeLimit = panel.GetVisualDescendants().OfType<NumericUpDown>()
                        .Single(control => AutomationProperties.GetName(control) == "Selection time limit per word in seconds");
                    timeLimit.Value = 1m;
                    ClickControl(window, timeLimit, "Selection time limit per word in seconds");
                    PressEnter(window, timeLimit);
                    for (var pass = 0; pass < 4; pass++)
                    {
                        await Task.Yield();
                        PageScreenshots.Settle(window);
                    }
                    Assert.True(useEstimate.IsEffectivelyVisible);
                    ClickControl(window, useEstimate, "Use Motif's estimated time limit");
                    for (var pass = 0; pass < 4; pass++)
                    {
                        await Task.Yield();
                        PageScreenshots.Settle(window);
                    }
                    Assert.Equal(SelectionTimeLimitMode.Estimated,
                        fake.SetSelectionLimitsRequests.Last().Limits.TimeMode);
                    var noStepLimit = panel.GetVisualDescendants().OfType<CheckBox>()
                        .Single(control => AutomationProperties.GetName(control) == "Selection has no step limit");
                    ClickControl(window, noStepLimit, "Selection has no step limit");
                    for (var pass = 0; pass < 4; pass++)
                    {
                        await Task.Yield();
                        PageScreenshots.Settle(window);
                    }
                    Assert.True(fake.SetSelectionLimitsRequests.Last().Limits.PerWordStepLimit.IsUnbounded);

                    var page = OpenSettings(window);
                    SettingsTestAccess.Select(page, SettingsGroup.Parsing);
                    PageScreenshots.Settle(window);
                    var settingsStep = page.GetVisualDescendants().OfType<NumericUpDown>()
                        .Single(control => AutomationProperties.GetName(control) == "Step limit per word");
                    Assert.Equal(300000m, settingsStep.Value);
                }
                finally
                {
                    window.Close();
                    await workspace.DisposeAsync();
                }
            }, TimeSpan.FromMinutes(2));
        }
        finally
        {
            DeleteSettingsPath(settingsPath);
        }
    }

    [Fact]
    public void ExistingPageCapturesFitAt100125And150PercentZoom()
    {
        var settingsPath = NewSettingsPath();
        var priorTheme = ReadTheme();
        try
        {
            AvaloniaHeadlessFixture.RunUntilComplete(async () =>
            {
                var (workspace, window) = await PageScreenshots.OpenOverSampleData(settingsFilePath: settingsPath);
                try
                {
                    var sidebarFooter = window.FindControl<StackPanel>("SidebarFooter")!;
                    var selectionSummary = window.FindControl<CopyableTextBlock>("StatusSelectionSummary")!;
                    Assert.True(double.IsNaN(sidebarFooter.Height));
                    Assert.Equal(TextWrapping.NoWrap, selectionSummary.TextWrapping);
                    Assert.Equal(TextTrimming.CharacterEllipsis, selectionSummary.TextTrimming);
                    window.Width = 1240;
                    window.Height = 780;
                    var page = OpenSettings(window);
                    var zoomIn = Button(page, "Zoom in");
                    var zoomRoot = window.FindControl<LayoutTransformControl>("RootLayoutTransform")!;
                    var zooms = new[] { (Percent: 100, Clicks: 0), (Percent: 125, Clicks: 2), (Percent: 150, Clicks: 1) };

                    foreach (var zoom in zooms)
                    {
                        for (var click = 0; click < zoom.Clicks; click++)
                            HeadlessClick.Click(window, zoomIn, "Zoom in");
                        PageScreenshots.Settle(window);
                        var zoomScale = Assert.IsType<ScaleTransform>(zoomRoot.LayoutTransform).ScaleX;
                        Assert.Equal(zoom.Percent / 100d, zoomScale, precision: 2);
                        Assert.Same(SettingsTestAccess.Model(window), page.DataContext);
                        SettingsTestAccess.AssertReachable(window, page.FindControl<Button>("SettingsClose")!);
                        SettingsTestAccess.AssertReachable(window, zoomIn);

                        foreach (var (name, route, tab) in PageScreenshots.Views())
                        {
                            workspace.PageModel<SIL.Motif.App.ViewModels.TextsPageModel>().Tab = tab;
                            if (route == SIL.Motif.App.ViewModels.WorkspacePage.TryAWord)
                            {
                                workspace.Context.TryWord("hawajafika");
                                await workspace.Assess.Trace.TryCommand.ExecutionTask!;
                            }
                            workspace.CurrentPage = route;
                            PageScreenshots.Settle(window);
                            try
                            {
                                LayoutAssertions.AssertCurrent(window);
                            }
                            catch (Exception failure)
                            {
                                throw new Xunit.Sdk.XunitException(
                                    $"The {name} capture clips or overflows at {zoom.Percent}% zoom: {failure.Message}");
                            }
                        }

                        if (zoom.Percent != 150)
                        {
                            page = OpenSettings(window);
                            zoomIn = Button(page, "Zoom in");
                        }
                    }
                }
                finally
                {
                    window.Close();
                    await workspace.DisposeAsync();
                }
            }, TimeSpan.FromMinutes(3));
        }
        finally
        {
            RestoreTheme(priorTheme);
            DeleteSettingsPath(settingsPath);
        }
    }

    [Fact]
    public void CopyingTheDataPathGivesTheExactPathWithoutBreakOpportunities()
    {
        var dataRoot = Path.Combine(Path.GetTempPath(), "linguist", "managed.data", "long-folder-name-0123456789");
        var facts = new ApplicationFacts("0.2.0", "0.8.3", "Linux", false, true, dataRoot);
        var settingsPath = NewSettingsPath();
        string? copied = null;
        try
        {
            AvaloniaHeadlessFixture.RunUntilComplete(async () =>
            {
                var (workspace, window) = FakeComposedWindow.Create(settingsFilePath: settingsPath,
                    applicationFacts: facts);
                try
                {
                    window.Width = 1040;
                    window.Height = 780;
                    window.Show();
                    var page = OpenSettings(window);
                    SettingsTestAccess.Select(page, SettingsGroup.About);
                    PageScreenshots.Settle(window);
                    var value = page.GetVisualDescendants().OfType<CopyableTextBlock>()
                        .Single(text => text.IsEffectivelyVisible
                            && text.Text?.Replace("\u200B", "", StringComparison.Ordinal) == dataRoot);
                    Assert.Contains('\u200B', value.Text!);
                    value.SelectAll();
                    Assert.Contains('\u200B', value.SelectedText);
                    var key = new KeyEventArgs
                    {
                        RoutedEvent = InputElement.KeyDownEvent,
                        Key = Key.C,
                        KeyModifiers = KeyModifiers.Control,
                    };
                    value.RaiseEvent(key);
                    Assert.True(key.Handled);
                    copied = await window.Clipboard!.TryGetTextAsync();
                }
                finally
                {
                    window.Close();
                }
            }, TimeSpan.FromSeconds(30));
        }
        finally
        {
            DeleteSettingsPath(settingsPath);
        }

        Assert.Equal(dataRoot, copied);
    }

    [Theory]
    [InlineData(640)]
    [InlineData(1040)]
    public void AboutGroupFitsLongRuntimeValuesAtCompactWidthsAnd150PercentZoom(int width)
    {
        const string operatingSystem = "Microsoft Windows 10.0.26300";
        var dataRoot = Path.Combine(Path.GetTempPath(), "linguist", "projects", "motif", "managed",
            "parser", "artifacts", "language", "review", "long-folder-name-0123456789");
        var facts = new ApplicationFacts("0.2.0", "0.8.3", operatingSystem, false, true, dataRoot);
        var settingsPath = NewSettingsPath();
        try
        {
            avalonia.Invoke(() =>
            {
                var (workspace, window) = FakeComposedWindow.Create(settingsFilePath: settingsPath,
                    applicationFacts: facts);
                try
                {
                    window.Width = width;
                    window.Height = 780;
                    window.Show();
                    var page = OpenSettings(window);
                    var settings = Assert.IsType<SettingsViewModel>(page.DataContext);
                    var groups = page.Navigation.FindControl<ListBox>("SettingsGroups")!;
                    SettingsTestAccess.Select(page, SettingsGroup.About);
                    settings.ZoomPercent = 150;

                    foreach (var (theme, themeName) in new[]
                    {
                        (WindowThemeChoice.Light, "light"),
                        (WindowThemeChoice.Dark, "dark"),
                    })
                    {
                        settings.ThemeChoice = theme;
                        PageScreenshots.Settle(window);

                        var scroll = page.FindControl<ScrollViewer>("SettingsListScroll")!;
                        var visibleText = page.GetVisualDescendants().OfType<TextBlock>()
                            .Where(text => text.IsEffectivelyVisible).ToArray();
                        var operatingSystemLabel = Assert.Single(visibleText,
                            text => text.Text == SIL.Motif.App.MotifAppStrings.OperatingSystem);
                        var operatingSystemValue = Assert.Single(visibleText,
                            text => text.Text == operatingSystem);
                        var dataRootLabel = Assert.Single(visibleText,
                            text => text.Text == SIL.Motif.App.MotifAppStrings.WhereMotifKeepsItsData);
                        var dataRootValue = Assert.Single(visibleText,
                            text => text.Text?.Replace("\u200B", "", StringComparison.Ordinal) == dataRoot);
                        var openFolder = Button(page, "Open Motif data folder");

                        Assert.Equal(TextWrapping.Wrap, operatingSystemValue.TextWrapping);
                        LayoutAssertions.AssertTextFits(operatingSystemValue);
                        Assert.True(scroll.Extent.Width <= scroll.Viewport.Width + 0.5,
                            "The About group overflows the Settings page horizontally.");
                        var operatingSystemLabelPoint = operatingSystemLabel.TranslatePoint(default, scroll);
                        var operatingSystemValuePoint = operatingSystemValue.TranslatePoint(default, scroll);
                        var compact = scroll.Viewport.Width <
                            (double)Application.Current!.FindResource("Component.Settings.AboutStackBreakpoint")!;
                        var dataRootLabelPoint = dataRootLabel.TranslatePoint(default, scroll)!.Value;
                        var dataRootValuePoint = dataRootValue.TranslatePoint(default, scroll)!.Value;
                        var openFolderPoint = openFolder.TranslatePoint(default, scroll)!.Value;
                        if (compact)
                        {
                            Assert.True(operatingSystemValuePoint!.Value.Y >= operatingSystemLabelPoint!.Value.Y +
                                operatingSystemLabel.Bounds.Height - 0.5,
                                "The operating-system value should follow its label when the page is narrow.");
                            Assert.True(dataRootValuePoint.Y >= dataRootLabelPoint.Y + dataRootLabel.Bounds.Height - 0.5,
                                "The data-folder path should follow its label when the page is narrow.");
                            Assert.True(openFolderPoint.Y >= dataRootValuePoint.Y + dataRootValue.Bounds.Height - 0.5,
                                "The folder action should appear below the path.");
                        }
                        else
                        {
                            Assert.True(operatingSystemValuePoint!.Value.X >= operatingSystemLabelPoint!.Value.X +
                                operatingSystemLabel.Bounds.Width - 0.5,
                                "The operating-system value should follow its label across the wide row.");
                            Assert.True(dataRootValuePoint.X >= dataRootLabelPoint.X + dataRootLabel.Bounds.Width - 0.5,
                                "The data-folder path should follow its label across the wide row.");
                            Assert.True(openFolderPoint.X >= dataRootValuePoint.X + dataRootValue.Bounds.Width - 0.5,
                                "The folder action should follow the path across the wide row.");
                        }
                        Assert.True(openFolderPoint.X >= -0.5 &&
                            openFolderPoint.X + openFolder.Bounds.Width <= scroll.Viewport.Width + 0.5,
                            "The folder action is clipped horizontally.");
                        LayoutAssertions.AssertCurrent(page);

                        if (Environment.GetEnvironmentVariable(ScreenshotFactAttribute.FolderVariable) is { Length: > 0 } folder)
                        {
                            Directory.CreateDirectory(folder);
                            using var frame = TopLevel.GetTopLevel(page)?.CaptureRenderedFrame() ??
                                throw new InvalidOperationException("No Settings frame rendered for the About capture.");
                            frame.Save(Path.Combine(folder,
                                $"settings-about-long-values-{width}-150-{themeName}.png"),
                                PngBitmapEncoderOptions.Default);
                        }
                    }
                }
                finally
                {
                    window.Close();
                    workspace.DisposeAsync().AsTask().GetAwaiter().GetResult();
                }
            });
        }
        finally
        {
            DeleteSettingsPath(settingsPath);
        }
    }

    [Fact]
    public void DefaultProposalChecksGroupShowsTheSavedChoiceAndSavesAChange()
    {
        var settingsPath = NewSettingsPath();
        var checksPath = Path.Combine(Path.GetDirectoryName(settingsPath)!, "default-proposal-checks.json");
        var store = new FileDefaultProposalChecksStore(checksPath);
        var priorTheme = ReadTheme();
        try
        {
            avalonia.Invoke(() =>
            {
                var (workspace, window) = FakeComposedWindow.Create(settingsFilePath: settingsPath,
                    defaultProposalChecks: store);
                try
                {
                    window.Show();
                    var page = OpenSettings(window);
                    var groups = page.Navigation.FindControl<ListBox>("SettingsGroups")!;
                    Assert.Contains(groups.Items.OfType<SettingsGroupOption>(),
                        option => option.Title == SIL.Motif.App.MotifAppStrings.DefaultProposalChecks);
                    SettingsTestAccess.Select(page, SettingsGroup.ProposalChecks);
                    PageScreenshots.Settle(window);
                    Assert.Equal(SIL.Motif.App.MotifAppStrings.DefaultProposalChecks, ActiveGroupHeading(page));

                    var budget = page.FindControl<RadioButton>("StoredAnalysesBudgetChoice")!;
                    var all = page.FindControl<RadioButton>("StoredAnalysesAllChoice")!;
                    var off = page.FindControl<RadioButton>("StoredAnalysesOffChoice")!;
                    Assert.All(new[] { budget, all, off }, choice => Assert.True(choice.IsEffectivelyVisible));
                    Assert.True(budget.IsChecked == true);
                    Assert.Equal(StoredAnalysisComparison.Budget, store.StoredAnalyses);

                    ClickControl(window, off, "Off");
                    PageScreenshots.Settle(window);
                    Assert.True(off.IsChecked == true);
                    Assert.False(budget.IsChecked == true);
                    Assert.Equal(StoredAnalysisComparison.Off, store.StoredAnalyses);

                    ClickControl(window, all, "Every word");
                    PageScreenshots.Settle(window);
                    Assert.Equal(StoredAnalysisComparison.All, store.StoredAnalyses);
                }
                finally
                {
                    window.Close();
                    workspace.DisposeAsync().AsTask().GetAwaiter().GetResult();
                }
            });
            using var reopened = JsonDocument.Parse(File.ReadAllText(checksPath));
            Assert.Equal("all", reopened.RootElement.GetProperty("storedAnalyses").GetString());

            avalonia.Invoke(() =>
            {
                var (workspace, window) = FakeComposedWindow.Create(settingsFilePath: settingsPath,
                    defaultProposalChecks: new FileDefaultProposalChecksStore(checksPath));
                try
                {
                    window.Show();
                    var page = OpenSettings(window);
                    PageScreenshots.Settle(window);
                    var settings = Assert.IsType<SettingsViewModel>(page.DataContext);
                    Assert.Equal(SIL.Motif.App.ViewModels.SettingsGroup.ProposalChecks, settings.SelectedGroup);
                    Assert.True(page.FindControl<RadioButton>("StoredAnalysesAllChoice")!.IsChecked == true);
                }
                finally
                {
                    window.Close();
                    workspace.DisposeAsync().AsTask().GetAwaiter().GetResult();
                }
            });
        }
        finally
        {
            RestoreTheme(priorTheme);
            DeleteSettingsPath(settingsPath);
        }
    }

    [Fact]
    public void DefaultProposalChecksGroupFitsAt100125And150PercentZoomInBothThemes()
    {
        var settingsPath = NewSettingsPath();
        var priorTheme = ReadTheme();
        try
        {
            avalonia.Invoke(() =>
            {
                var (workspace, window) = FakeComposedWindow.Create(settingsFilePath: settingsPath);
                try
                {
                    window.Width = 1040;
                    window.Height = 780;
                    window.Show();
                    var page = OpenSettings(window);
                    var settings = Assert.IsType<SettingsViewModel>(page.DataContext);
                    SettingsTestAccess.Select(page, SettingsGroup.ProposalChecks);
                    foreach (var theme in new[] { WindowThemeChoice.Light, WindowThemeChoice.Dark })
                    {
                        settings.ThemeChoice = theme;
                        foreach (var zoom in new[] { 100, 125, 150 })
                        {
                            settings.ZoomPercent = zoom;
                            PageScreenshots.Settle(window);
                            var scroll = page.FindControl<ScrollViewer>("SettingsListScroll")!;
                            Assert.True(scroll.Extent.Width <= scroll.Viewport.Width + 0.5,
                                $"The group overflows sideways at {zoom}% in {theme}.");
                            foreach (var name in new[]
                            {
                                "StoredAnalysesBudgetChoice", "StoredAnalysesAllChoice", "StoredAnalysesOffChoice",
                            })
                            {
                                var choice = page.FindControl<RadioButton>(name)!;
                                var bounds = choice.TranslatePoint(default, scroll);
                                Assert.NotNull(bounds);
                                Assert.True(bounds!.Value.X >= -0.5 && bounds.Value.X + choice.Bounds.Width <=
                                    scroll.Viewport.Width + 0.5, $"{name} is clipped sideways at {zoom}% in {theme}.");
                            }
                            LayoutAssertions.AssertCurrent(window);
                        }
                    }
                }
                finally
                {
                    window.Close();
                    workspace.DisposeAsync().AsTask().GetAwaiter().GetResult();
                }
            });
        }
        finally
        {
            RestoreTheme(priorTheme);
            DeleteSettingsPath(settingsPath);
        }
    }

    internal static void CaptureSettingsGroupsBothThemesAndWidthsWithSearchAndRefusal()
    {
        var folder = Environment.GetEnvironmentVariable(ScreenshotFactAttribute.FolderVariable)!;
        Directory.CreateDirectory(folder);
        var settingsPath = NewSettingsPath();
        var priorTheme = ReadTheme();
        try
        {
            AvaloniaHeadlessFixture.RunUntilComplete(async () =>
            {
                var (workspace, window) = await PageScreenshots.OpenOverSampleData(parse: false,
                    configure: (fake, _) => fake.DefaultSelectionIs(new NamedSelectionProjection("Default",
                        [SampleTextId], [], string.Empty, string.Empty, 1000, StepCap.Default)),
                    settingsFilePath: settingsPath);
                try
                {
                    var overview = workspace.PageModel<OverviewPageModel>().Overview!;
                    workspace.PageModel<OverviewPageModel>().Overview = overview with
                    {
                        WritingSystems = WritingSystemCaptureData(),
                    };
                    window.Height = 780;
                    var page = OpenSettings(window);
                    var groups = page.Navigation.FindControl<ListBox>("SettingsGroups")!;
                    var settings = Assert.IsType<SettingsViewModel>(page.DataContext);
                    foreach (var (themeName, theme, automationName) in new[]
                    {
                        ("light", ThemeVariant.Light, "Light theme"),
                        ("dark", ThemeVariant.Dark, "Dark theme"),
                    })
                    {
                        settings.ThemeChoice = theme == ThemeVariant.Dark
                            ? WindowThemeChoice.Dark : WindowThemeChoice.Light;
                        PageScreenshots.Settle(window);
                        Assert.Equal(theme, Application.Current!.RequestedThemeVariant);
                        foreach (var width in new[] { 1240, 1040 })
                        {
                            window.Width = width;
                            PageScreenshots.Settle(window);
                            for (var index = 0; index < GroupStates.Length; index++)
                            {
                                SettingsTestAccess.Select(page, GroupStates[index].Group);
                                PageScreenshots.Settle(window);
                                SavePageState(window, page, folder,
                                    $"settings-{GroupStates[index].Slug}-{width}-{themeName}.png");
                            }

                            if (themeName == "light" && width == 1240)
                            {
                                SettingsTestAccess.Select(page, SettingsGroup.Display);
                                PageScreenshots.Settle(window);
                                SavePageState(window, page, folder, "settings-display-reset-default.png");
                                settings.ZoomPercent = 125;
                                SavePageState(window, page, folder, "settings-display-reset-available.png");
                                HeadlessClick.Click(window, Button(page, "Reset zoom to 100%"), "Reset zoom to 100%");
                                SavePageState(window, page, folder, "settings-display-reset-disabled.png");
                            }

                            SettingsTestAccess.Select(page, SettingsGroup.KeyboardShortcuts);
                            var search = page.FindControl<TextBox>("ShortcutSearchBox")!;
                            search.Text = "zoom";
                            PageScreenshots.Settle(window);
                            SavePageState(window, page, folder, $"settings-shortcut-search-{width}-{themeName}.png");
                            search.Text = "no matching shortcut";
                            PageScreenshots.Settle(window);
                            SavePageState(window, page, folder,
                                $"settings-shortcut-no-results-{width}-{themeName}.png");
                            search.Text = string.Empty;

                            SettingsTestAccess.Select(page, SettingsGroup.Parsing);
                            PageScreenshots.Settle(window);
                            settings.StepLimit = 100000.5m;
                            await settings.CommitParsingLimitsAsync();
                            for (var pass = 0; pass < 4; pass++)
                            {
                                await Task.Yield();
                                PageScreenshots.Settle(window);
                            }
                            Assert.Contains(page.GetVisualDescendants().OfType<TextBlock>(), text =>
                                text.IsEffectivelyVisible && text.Text ==
                                "The per-word step limit must be a positive whole number.");
                            SavePageState(window, page, folder,
                                $"settings-parsing-refused-{width}-{themeName}.png");
                            settings.CancelParsingLimitEdit();

                            if (themeName != "light" || width != 1240) continue;
                            settings.IsUsingEstimate = false;
                            settings.TimeLimitSeconds = null;
                            await settings.CommitParsingLimitsAsync();
                            PageScreenshots.Settle(window);
                            SavePageState(window, page, folder, "settings-time-refused.png");
                            settings.CancelParsingLimitEdit();

                            settings.TimeLimitSeconds = 2.5m;
                            await settings.CommitParsingLimitsAsync();
                            PageScreenshots.Settle(window);
                            SavePageState(window, page, folder, "settings-time-explicit.png");
                            settings.UseMotifEstimate();
                            await settings.CommitParsingLimitsAsync();
                            PageScreenshots.Settle(window);
                            SavePageState(window, page, folder, "settings-time-estimate-reset.png");

                            settings.IsNoStepLimit = true;
                            await settings.CommitParsingLimitsAsync();
                            PageScreenshots.Settle(window);
                            SavePageState(window, page, folder, "settings-unbounded-limits.png");
                            settings.IsNoStepLimit = false;
                            await settings.CommitParsingLimitsAsync();

                            SettingsTestAccess.Select(page, SettingsGroup.About);
                            PageScreenshots.Settle(window);
                            settings.MarkVersionsCopied();
                            Assert.Equal("Copied", settings.CopyVersionsText);
                            PageScreenshots.Settle(window);
                            SavePageState(window, page, folder, "settings-copy-acknowledgement.png");

                            SettingsTestAccess.Select(page, SettingsGroup.Display);
                            settings.ZoomPercent = 150;
                            settings.ThemeChoice = WindowThemeChoice.Dark;
                            PageScreenshots.Settle(window);
                            var zoomButton = Button(page, "Zoom in");
                            ToolTip.SetTip(zoomButton, "Increase zoom");
                    ToolTip.SetIsOpen(zoomButton, true);
                            PageScreenshots.Settle(window);
                            SavePageState(window, page, folder, "settings-open-tooltip-150-dark.png");
                            ToolTip.SetIsOpen(zoomButton, false);
                            settings.ZoomPercent = UserPreferences.Defaults.ZoomPercent;
                            settings.ThemeChoice = WindowThemeChoice.Light;
                        }
                    }
                }
                finally
                {
                    window.Close();
                    await workspace.DisposeAsync();
                }
            }, TimeSpan.FromMinutes(3));
        }
        finally
        {
            RestoreTheme(priorTheme);
            DeleteSettingsPath(settingsPath);
        }
    }

    internal static void CaptureDarkSettingsWithTooltip()
    {
        var folder = Environment.GetEnvironmentVariable(ScreenshotFactAttribute.FolderVariable)!;
        Directory.CreateDirectory(folder);
        var settingsPath = NewSettingsPath();
        var priorTheme = ReadTheme();
        try
        {
            AvaloniaHeadlessFixture.RunUntilComplete(async () =>
            {
                var (workspace, window) = await PageScreenshots.OpenOverSampleData(parse: false,
                    settingsFilePath: settingsPath);
                try
                {
                    window.Height = 780;
                    var page = OpenSettings(window);
                    var settings = Assert.IsType<SettingsViewModel>(page.DataContext);
                    settings.ZoomPercent = 150;
                    settings.ThemeChoice = WindowThemeChoice.Dark;
                    PageScreenshots.Settle(window);
                    SavePageState(window, page, folder, "p3fix-dark-settings.png");

                    var zoom = Button(page, "Zoom in");
                    ToolTip.SetTip(zoom, "Increase zoom");
                    ToolTip.SetShowDelay(zoom, 0);
                    ToolTip.SetIsOpen(zoom, true);
                    PageScreenshots.Settle(window);
                    Assert.True(ToolTip.GetIsOpen(zoom));
                    var tip = Assert.Single(window.GetVisualDescendants().OfType<ToolTip>()
                        .Where(candidate => candidate.IsEffectivelyVisible));
                    var captureRoot = TopLevel.GetTopLevel(tip) ?? window;
                    using var frame = captureRoot.CaptureRenderedFrame() ??
                        throw new InvalidOperationException("No dark tooltip frame rendered.");
                    frame.Save(Path.Combine(folder, "p3fix-dark-tooltip.png"),
                        Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
                    ToolTip.SetIsOpen(zoom, false);
                }
                finally
                {
                    window.Close();
                    await workspace.DisposeAsync();
                }
            }, TimeSpan.FromMinutes(2));
        }
        finally
        {
            RestoreTheme(priorTheme);
            DeleteSettingsPath(settingsPath);
        }
    }

    internal static readonly (SettingsGroup Group, string Slug)[] GroupStates =
    [
        (SettingsGroup.Display, "display"), (SettingsGroup.KeyboardShortcuts, "keyboard"),
        (SettingsGroup.Parsing, "parsing"), (SettingsGroup.ProposalChecks, "proposal-checks"),
        (SettingsGroup.WritingSystems, "writing-systems"), (SettingsGroup.Texts, "texts"),
        (SettingsGroup.HelpAndReports, "help"), (SettingsGroup.About, "about"),
    ];
    internal static readonly Guid SampleTextId = Guid.Parse("11111111-0000-0000-0000-000000000001");

    private static WritingSystemDisplay[] WritingSystemCaptureData() =>
    [
        new("swh", "Swahili", "swh", WritingSystemKind.Vernacular, 0, true,
            "Motif Missing Test Font", "ss01=1", false,
            new Dictionary<string, double> { ["Normal"] = 11d })
        {
            StyleFonts = new Dictionary<string, WritingSystemStyleFont>
            {
                ["Normal"] = new("Motif Missing Test Font", "ss01=1"),
            },
        },
        new("ar", "Arabic", "ar", WritingSystemKind.Analysis, 0, true,
            "", "", true, new Dictionary<string, double> { ["Normal"] = 12d }),
    ];

    internal static SettingsPage OpenSettings(MainWindow window) => SettingsTestAccess.Open(window);

    private static void ClickControl(Window window, Control control, string accessibleName)
    {
        control.BringIntoView();
        PageScreenshots.Settle(window);
        HeadlessClick.Click(TopLevel.GetTopLevel(control) ?? window, control, accessibleName);
    }

    private static void PressEnter(Window window, Control control) =>
        (TopLevel.GetTopLevel(control) ?? window).KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);

    internal static Button Button(Control root, string accessibleName)
    {
        var button = root.GetVisualDescendants().OfType<Button>().Single(candidate =>
            AutomationProperties.GetName(candidate) == accessibleName);
        if (root is SettingsPage && TopLevel.GetTopLevel(root) is MainWindow window)
        {
            button.BringIntoView();
            PageScreenshots.Settle(window);
        }
        return button;
    }

    private static string? ActiveGroupHeading(SettingsPage page) => SettingsTestAccess.Section(page)
        .GetVisualDescendants().OfType<TextBlock>()
        .Single(text => text.Classes.Contains("settingsGroupHeading")).Text;

    internal static void SavePageState(MainWindow window, SettingsPage page, string folder, string fileName)
    {
        PageScreenshots.Settle(window);
        LayoutAssertions.BeforeCapture(window);
        PageScreenshots.Settle(window);
        LayoutAssertions.AssertCurrent(window);
        var captureRoot = TopLevel.GetTopLevel(page) ?? window;
        using var frame = captureRoot.CaptureRenderedFrame() ??
            throw new InvalidOperationException($"No settings frame rendered for {fileName}.");
        frame.Save(Path.Combine(folder, fileName), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    internal static string NewSettingsPath()
    {
        var folder = Path.Combine(Path.GetTempPath(), "motif-settings-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, "user-preferences.json");
    }

    internal static void DeleteSettingsPath(string path)
    {
        var folder = Path.GetDirectoryName(path);
        if (folder is not null && Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
    }

    private static ThemeVariant? ReadTheme()
    {
        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess()) return Application.Current?.RequestedThemeVariant;
        ThemeVariant? theme = null;
        AvaloniaHeadlessPlatform.Invoke(() => theme = Application.Current?.RequestedThemeVariant);
        return theme;
    }

    private static void RestoreTheme(ThemeVariant? theme)
    {
        if (theme is null) return;
        void Restore()
        {
            if (Application.Current is { } application) application.RequestedThemeVariant = theme;
        }
        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess()) Restore();
        else AvaloniaHeadlessPlatform.Invoke(Restore);
    }

    private sealed class RecordingUriLauncher : IUriLauncher
    {
        public List<Uri> Launches { get; } = [];

        public Task<bool> LaunchAsync(Uri uri, CancellationToken cancellationToken = default)
        {
            Launches.Add(uri);
            return Task.FromResult(true);
        }
    }
}

[Collection(AvaloniaHeadlessCollection.Name)]
[Trait("MotifTestLevel", "System")]
public sealed class SettingsScreenshotTests
{
    [ScreenshotFact]
    public void SettingsGroupsCaptureBothThemesAndWidthsWithSearchAndRefusal() =>
        SettingsViewTests.CaptureSettingsGroupsBothThemesAndWidthsWithSearchAndRefusal();

    [ScreenshotFact]
    public void DarkSettingsAndTooltipCapture() => SettingsViewTests.CaptureDarkSettingsWithTooltip();
}
