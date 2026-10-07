using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.App.Walkthrough;
using SIL.Motif.Worker;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class SettingsViewTests(AvaloniaHeadlessFixture avalonia)
{
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
                    var popup = OpenSettings(window);
                    Assert.Equal("Display", ActiveGroupHeading(popup));
                    HeadlessClick.Click(window, Button(popup, "Zoom in"), "Zoom in");
                    HeadlessClick.Click(window, Button(popup, "Dark theme"), "Dark theme");

                    var groups = popup.FindControl<ListBox>("SettingsGroups")!;
                    groups.SelectedIndex = 1;
                    PageScreenshots.Settle(window);
                    var search = popup.FindControl<TextBox>("ShortcutSearchBox")!;
                    search.Text = "zoom";
                    PageScreenshots.Settle(window);
                    var clear = Button(popup, "Clear shortcut search");
                    Assert.True(clear.IsEffectivelyVisible);
                    HeadlessClick.Click(window, clear, "Clear shortcut search");
                    Assert.Equal(string.Empty, search.Text);

                    groups.SelectedIndex = 4;
                    PageScreenshots.Settle(window);
                    var details = Assert.Single(popup.GetVisualDescendants().OfType<Expander>());
                    Assert.False(details.IsExpanded);
                    HeadlessClick.Click(window, details, "What a report includes");
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

                    var openHelp = Button(popup, "Open Help");
                    HeadlessClick.Click(window, openHelp, "Open Help");
                    Assert.False(Assert.IsType<Flyout>(Button(window, "Settings").Flyout).IsOpen);
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
                    var popup = OpenSettings(window);
                    var groups = popup.FindControl<ListBox>("SettingsGroups")!;
                    Assert.Equal(0, groups.SelectedIndex);
                    Assert.True(groups.Focus());

                    window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
                    PageScreenshots.Settle(window);
                    Assert.Equal(1, groups.SelectedIndex);
                    Assert.Equal("Keyboard shortcuts", ActiveGroupHeading(popup));

                    window.KeyPress(Key.End, RawInputModifiers.None, PhysicalKey.End, null);
                    PageScreenshots.Settle(window);
                    Assert.Equal(5, groups.SelectedIndex);
                    Assert.Equal("About Motif", ActiveGroupHeading(popup));

                    window.KeyPress(Key.Home, RawInputModifiers.None, PhysicalKey.Home, null);
                    PageScreenshots.Settle(window);
                    Assert.Equal(0, groups.SelectedIndex);
                    Assert.Equal("Display", ActiveGroupHeading(popup));
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

                var popup = OpenSettings(window);
                var viewModel = Assert.IsType<SettingsViewModel>(popup.DataContext);
                Assert.Equal(new[] { "v-0", "v-1", "v-2", "v-3", "v-4", "v-5", "v-6", "v-7" },
                    viewModel.VernacularWritingSystems.Select(row => row.Id));
                Assert.True(viewModel.GroupOptions.Single(option =>
                    option.Group == SIL.Motif.App.ViewModels.SettingsGroup.WritingSystems)
                    .HasWarning);

                var groups = popup.FindControl<ListBox>("SettingsGroups")!;
                groups.SelectedIndex = 3;
                PageScreenshots.Settle(window);
                Assert.Equal("Writing systems in Sample", ActiveGroupHeading(popup));
                Assert.Contains(popup.GetVisualDescendants().OfType<TextBlock>(), text =>
                    text.IsEffectivelyVisible && text.Text == "11 pt (Normal)");
                Assert.Contains(popup.GetVisualDescendants().OfType<MarkGlyph>(), mark =>
                    mark.IsEffectivelyVisible && AutomationProperties.GetName(mark) == "A font is missing");
                Assert.DoesNotContain(workspace.Context.TextStyles.MissingFontNotices,
                    notice => notice.WritingSystemId == "v-7");

                HeadlessClick.Click(window, Button(popup, "Close Settings"), "Close Settings");
                workspace.Context.TextStyles.MissingFontNotices.Add(
                    new WritingSystemFontNotice("v-7", "FieldWorks asks for a missing font."));
                PageScreenshots.Settle(window);
                HeadlessClick.Click(window, Button(window, "See writing systems"), "See writing systems");
                PageScreenshots.Settle(window);

                Assert.Equal(SIL.Motif.App.ViewModels.SettingsGroup.WritingSystems, viewModel.SelectedGroup);
                var writingSystemScroll = popup.GetVisualDescendants().OfType<ScrollViewer>()
                    .Single(scroll => scroll.Classes.Contains("settingsContentScroll"));
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
                    var popup = OpenSettings(window);
                    var light = Button(popup, "Light theme");
                    var dark = Button(popup, "Dark theme");
                    var system = Button(popup, "Match the operating system theme");
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

                    Assert.IsType<SettingsViewModel>(popup.DataContext).ThemeChoice = WindowThemeChoice.System;
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
                    var popup = OpenSettings(window);
                    popup.FindControl<ListBox>("SettingsGroups")!.SelectedIndex = 5;
                    PageScreenshots.Settle(window);
                    var copy = Button(popup, "Copy versions");
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
                    var popup = OpenSettings(window);
                    popup.FindControl<ListBox>("SettingsGroups")!.SelectedIndex = 4;
                    PageScreenshots.Settle(window);
                    HeadlessClick.Click(window, Button(popup, "Report a problem"), "Report a problem");
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
        var launcher = new RecordingUriLauncher();
        try
        {
            AvaloniaHeadlessFixture.RunUntilComplete(async () =>
            {
                var (workspace, window) = FakeComposedWindow.Create(settingsFilePath: settingsPath,
                    uriLauncher: launcher);
                try
                {
                    window.Show();
                    var popup = OpenSettings(window);
                    popup.FindControl<ListBox>("SettingsGroups")!.SelectedIndex = 4;
                    PageScreenshots.Settle(window);
                    ClickFlyout(window, Button(popup, "Open the Motif site"), "Open the Motif site");
                    for (var pass = 0; pass < 4; pass++)
                    {
                        await Task.Yield();
                        PageScreenshots.Settle(window);
                    }
                    Assert.Equal(new Uri("https://motif-docs.pages.dev"), Assert.Single(launcher.Launches));

                    popup = OpenSettings(window);
                    popup.FindControl<ListBox>("SettingsGroups")!.SelectedIndex = 5;
                    PageScreenshots.Settle(window);
                    await Task.Delay(300);
                    ClickFlyout(window, Button(popup, "Open Motif data folder"), "Open Motif data folder");
                    for (var pass = 0; pass < 4; pass++)
                    {
                        await Task.Yield();
                        PageScreenshots.Settle(window);
                    }
                    Assert.Equal(new Uri(RunnerOptions.ResolveRoot()), launcher.Launches[1]);
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
                    var popup = OpenSettings(window);
                    var refusal = Assert.Single(popup.GetVisualDescendants().OfType<TextBlock>(), text =>
                        text.IsEffectivelyVisible && text.Text?.Contains("delete user-preferences.json", StringComparison.Ordinal) == true);
                    Assert.True(refusal.IsEffectivelyVisible);
                    HeadlessClick.Click(window, Button(popup, "Zoom in"), "Zoom in");
                    Assert.Equal("110%", Assert.Single(popup.GetVisualDescendants().OfType<TextBlock>(), text =>
                        text.Classes.Contains("settingsZoomValue")).Text);
                    Assert.Equal(original, File.ReadAllText(settingsPath));
                    HeadlessClick.Click(window, Button(popup, "Reset saved preferences"), "Reset saved preferences");
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
                    var popup = OpenSettings(window);
                    var groups = popup.FindControl<ListBox>("SettingsGroups")!;
                    groups.SelectedIndex = 2;
                    PageScreenshots.Settle(window);

                    var stepLimit = popup.GetVisualDescendants().OfType<NumericUpDown>()
                        .Single(control => AutomationProperties.GetName(control) == "Step limit per word");
                    var timeLimit = popup.GetVisualDescendants().OfType<NumericUpDown>()
                        .Single(control => AutomationProperties.GetName(control) == "Time limit per word in seconds");
                    var useEstimate = Button(popup, "Use Motif's estimate");
                    var noStepLimit = Assert.Single(popup.GetVisualDescendants().OfType<CheckBox>());
                    Assert.Equal(workspace.Selection.PerWordStepLimit, stepLimit.Value);
                    Assert.Equal(workspace.Selection.PerWordStepLimitUnbounded, noStepLimit.IsChecked);

                    Assert.False(useEstimate.IsEffectivelyVisible);
                    timeLimit.Value = 10m;
                    ClickFlyout(window, timeLimit, "Time limit per word in seconds");
                    PressEnter(window, timeLimit);
                    for (var pass = 0; pass < 4; pass++)
                    {
                        await Task.Yield();
                        PageScreenshots.Settle(window);
                    }
                    Assert.True(useEstimate.IsEffectivelyVisible);

                    ClickFlyout(window, useEstimate, "Use Motif's estimate");
                    Assert.Equal(8m, workspace.Selection.PerWordTimeLimitSeconds);
                    timeLimit.Value = 1m;
                    ClickFlyout(window, timeLimit, "Time limit per word in seconds");
                    PressEnter(window, timeLimit);
                    for (var pass = 0; pass < 4; pass++)
                    {
                        await Task.Yield();
                        PageScreenshots.Settle(window);
                    }
                    Assert.Equal(1m, workspace.Selection.PerWordTimeLimitSeconds);
                    Assert.True(useEstimate.IsEffectivelyVisible);

                    var settings = Assert.IsType<SettingsViewModel>(popup.DataContext);
                    settings.StepLimit = 300000m;
                    Assert.False(settings.IsUsingMotifEstimate);
                    Assert.Equal(1m, settings.TimeLimitSeconds);
                    Assert.Null(await settings.CommitParsingLimitsAsync());
                    Assert.Equal(SelectionTimeLimitMode.Explicit,
                        fake!.SetSelectionLimitsRequests.Last().Limits.TimeMode);
                    settings.StepLimit = 200000m;
                    Assert.Null(await settings.CommitParsingLimitsAsync());

                    ClickFlyout(window, noStepLimit, "No step limit");
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

                    groups.SelectedIndex = 0;
                    groups.SelectedIndex = 2;
                    PageScreenshots.Settle(window);
                    ClickFlyout(window, noStepLimit, "No step limit");
                    for (var pass = 0; pass < 4; pass++)
                    {
                        await Task.Yield();
                        PageScreenshots.Settle(window);
                    }
                    var lastLimitSave = fake!.SetSelectionLimitsRequests.LastOrDefault();
                    var visibleRefusals = string.Join(" | ", popup.GetVisualDescendants().OfType<TextBlock>()
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
                    ClickFlyout(window, timeLimit, "Time limit per word in seconds");
                    PressEnter(window, timeLimit);
                    for (var pass = 0; pass < 4; pass++)
                    {
                        await Task.Yield();
                        PageScreenshots.Settle(window);
                    }
                    Assert.Contains(popup.GetVisualDescendants().OfType<TextBlock>(), text =>
                        text.IsEffectivelyVisible && text.Text == "Enter a per-word time limit greater than zero.");
                    Assert.Equal(1m, workspace.Selection.PerWordTimeLimitSeconds);

                    ClickFlyout(window, stepLimit, "Step limit per word");
                    stepLimit.Value = 100000.5m;
                    PressEnter(window, stepLimit);
                    for (var pass = 0; pass < 4; pass++)
                    {
                        await Task.Yield();
                        PageScreenshots.Settle(window);
                    }

                    var refusal = popup.GetVisualDescendants().OfType<TextBlock>().SingleOrDefault(text =>
                        text.IsEffectivelyVisible && text.Text ==
                        "The per-word step limit must be a positive whole number.");
                    Assert.True(refusal?.IsEffectivelyVisible == true,
                        $"The fractional step limit should be refused. Input={stepLimit.Value}; " +
                        $"selection={workspace.Selection.PerWordStepLimit}; " +
                        $"selection refusal={workspace.Selection.StepLimitValidationMessage}; " +
                        $"save count={fake.SetSelectionLimitsRequests.Count}; " +
                        $"last={fake.SetSelectionLimitsRequests.LastOrDefault()?.Limits.PerWordStepLimit}/" +
                        $"{fake.SetSelectionLimitsRequests.LastOrDefault()?.Limits.ExplicitPerWordLimitMs}; " +
                        $"visible refusals={string.Join(" | ", popup.GetVisualDescendants().OfType<TextBlock>()
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
                        .Single(expander => expander.Header?.ToString() == "Analysis options");
                    options.IsExpanded = true;
                    PageScreenshots.Settle(window);

                    var stepLimit = panel.GetVisualDescendants().OfType<NumericUpDown>()
                        .Single(control => AutomationProperties.GetName(control) == "Selection step limit per word");
                    stepLimit.Value = 300000m;
                    ClickFlyout(window, stepLimit, "Selection step limit per word");
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
                    ClickFlyout(window, timeLimit, "Selection time limit per word in seconds");
                    PressEnter(window, timeLimit);
                    for (var pass = 0; pass < 4; pass++)
                    {
                        await Task.Yield();
                        PageScreenshots.Settle(window);
                    }
                    Assert.True(useEstimate.IsEffectivelyVisible);
                    ClickFlyout(window, useEstimate, "Use Motif's estimated time limit");
                    for (var pass = 0; pass < 4; pass++)
                    {
                        await Task.Yield();
                        PageScreenshots.Settle(window);
                    }
                    Assert.Equal(SelectionTimeLimitMode.Estimated,
                        fake.SetSelectionLimitsRequests.Last().Limits.TimeMode);
                    var noStepLimit = panel.GetVisualDescendants().OfType<CheckBox>()
                        .Single(control => AutomationProperties.GetName(control) == "Selection has no step limit");
                    ClickFlyout(window, noStepLimit, "Selection has no step limit");
                    for (var pass = 0; pass < 4; pass++)
                    {
                        await Task.Yield();
                        PageScreenshots.Settle(window);
                    }
                    Assert.True(fake.SetSelectionLimitsRequests.Last().Limits.PerWordStepLimit.IsUnbounded);

                    var popup = OpenSettings(window);
                    popup.FindControl<ListBox>("SettingsGroups")!.SelectedIndex = 2;
                    PageScreenshots.Settle(window);
                    var settingsStep = popup.GetVisualDescendants().OfType<NumericUpDown>()
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
                    var selectionSummary = sidebarFooter.Children.OfType<CopyableTextBlock>().Last();
                    Assert.True(double.IsNaN(sidebarFooter.Height));
                    Assert.Equal(TextWrapping.Wrap, selectionSummary.TextWrapping);
                    window.Width = 1240;
                    window.Height = 780;
                    var popup = OpenSettings(window);
                    var zoomIn = Button(popup, "Zoom in");
                    var gear = Button(window, "Settings");
                    var flyout = Assert.IsType<Flyout>(gear.Flyout);
                    var zoomRoot = window.FindControl<LayoutTransformControl>("RootLayoutTransform")!;
                    var zooms = new[] { (Percent: 100, Clicks: 0), (Percent: 125, Clicks: 2), (Percent: 150, Clicks: 1) };

                    foreach (var zoom in zooms)
                    {
                        for (var click = 0; click < zoom.Clicks; click++)
                            HeadlessClick.Click(window, zoomIn, "Zoom in");
                        PageScreenshots.Settle(window);
                        Assert.Equal(zoom.Percent / 100d, Assert.IsType<ScaleTransform>(zoomRoot.LayoutTransform).ScaleX,
                            precision: 2);
                        Assert.True(flyout.Popup.InheritsTransform);
                        if (zoom.Percent == 150)
                        {
                            var surface = popup.FindControl<Border>("SettingsSurface")!;
                            Assert.True(surface.Bounds.Height <= 452.1);
                        }
                        flyout.Hide();
                        PageScreenshots.Settle(window);

                        foreach (var (name, page, tab) in PageScreenshots.Views())
                        {
                            workspace.PageModel<SIL.Motif.App.ViewModels.TextsPageModel>().Tab = tab;
                            if (page == SIL.Motif.App.ViewModels.WorkspacePage.TryAWord)
                            {
                                workspace.Context.TryWord("hawajafika");
                                await workspace.Assess.Trace.TryCommand.ExecutionTask!;
                            }
                            workspace.CurrentPage = page;
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
                            popup = OpenSettings(window);
                            zoomIn = Button(popup, "Zoom in");
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
                    var popup = OpenSettings(window);
                    var groups = popup.FindControl<ListBox>("SettingsGroups")!;
                    var settings = Assert.IsType<SettingsViewModel>(popup.DataContext);
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
                            for (var index = 0; index < 6; index++)
                            {
                                groups.SelectedIndex = index;
                                PageScreenshots.Settle(window);
                                SavePopupState(window, popup, folder,
                                    $"settings-{GroupSlugs[index]}-{width}-{themeName}.png");
                            }

                            groups.SelectedIndex = 1;
                            var search = popup.FindControl<TextBox>("ShortcutSearchBox")!;
                            search.Text = "zoom";
                            PageScreenshots.Settle(window);
                            SavePopupState(window, popup, folder, $"settings-shortcut-search-{width}-{themeName}.png");
                            search.Text = "no matching shortcut";
                            PageScreenshots.Settle(window);
                            SavePopupState(window, popup, folder,
                                $"settings-shortcut-no-results-{width}-{themeName}.png");
                            search.Text = string.Empty;

                            groups.SelectedIndex = 2;
                            PageScreenshots.Settle(window);
                            settings.StepLimit = 100000.5m;
                            await settings.CommitParsingLimitsAsync();
                            for (var pass = 0; pass < 4; pass++)
                            {
                                await Task.Yield();
                                PageScreenshots.Settle(window);
                            }
                            Assert.Contains(popup.GetVisualDescendants().OfType<TextBlock>(), text =>
                                text.IsEffectivelyVisible && text.Text ==
                                "The per-word step limit must be a positive whole number.");
                            SavePopupState(window, popup, folder,
                                $"settings-parsing-refused-{width}-{themeName}.png");
                            settings.CancelParsingLimitEdit();

                            if (themeName != "light" || width != 1240) continue;
                            settings.IsUsingEstimate = false;
                            settings.TimeLimitSeconds = null;
                            await settings.CommitParsingLimitsAsync();
                            PageScreenshots.Settle(window);
                            SavePopupState(window, popup, folder, "settings-time-refused.png");
                            settings.CancelParsingLimitEdit();

                            settings.TimeLimitSeconds = 2.5m;
                            await settings.CommitParsingLimitsAsync();
                            PageScreenshots.Settle(window);
                            SavePopupState(window, popup, folder, "settings-time-explicit.png");
                            settings.UseMotifEstimate();
                            await settings.CommitParsingLimitsAsync();
                            PageScreenshots.Settle(window);
                            SavePopupState(window, popup, folder, "settings-time-estimate-reset.png");

                            settings.IsNoStepLimit = true;
                            await settings.CommitParsingLimitsAsync();
                            PageScreenshots.Settle(window);
                            SavePopupState(window, popup, folder, "settings-unbounded-limits.png");
                            settings.IsNoStepLimit = false;
                            await settings.CommitParsingLimitsAsync();

                            groups.SelectedIndex = 0;
                            PageScreenshots.Settle(window);
                            settings.MarkVersionsCopied();
                            Assert.Equal("Copied", settings.CopyVersionsText);
                            PageScreenshots.Settle(window);
                            SavePopupState(window, popup, folder, "settings-copy-acknowledgement.png");

                            settings.ZoomPercent = 150;
                            settings.ThemeChoice = WindowThemeChoice.Dark;
                            PageScreenshots.Settle(window);
                            var zoomButton = Button(popup, "Zoom in");
                            ToolTip.SetIsOpen(zoomButton, true);
                            PageScreenshots.Settle(window);
                            SavePopupState(window, popup, folder, "settings-open-tooltip-150-dark.png");
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

    internal static readonly string[] GroupSlugs = ["display", "keyboard", "parsing", "writing-systems", "help", "about"];
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

    internal static SettingsPopupView OpenSettings(MainWindow window)
    {
        var gear = Button(window, "Settings");
        if (gear.Flyout is not Flyout { IsOpen: true }) HeadlessClick.Click(window, gear, "Settings");
        var flyout = Assert.IsType<Flyout>(gear.Flyout);
        Assert.True(flyout.IsOpen);
        PageScreenshots.Settle(window);
        return Assert.IsType<SettingsPopupView>(flyout.Content);
    }

    private static void ClickFlyout(Window window, Control control, string accessibleName) =>
        HeadlessClick.Click(TopLevel.GetTopLevel(control) ?? window, control, accessibleName);

    private static void PressEnter(Window window, Control control) =>
        (TopLevel.GetTopLevel(control) ?? window).KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);

    internal static Button Button(Control root, string accessibleName) =>
        root.GetVisualDescendants().OfType<Button>().Single(button =>
            AutomationProperties.GetName(button) == accessibleName);

    private static string? ActiveGroupHeading(SettingsPopupView popup) => popup.GetVisualDescendants()
        .OfType<TextBlock>().Single(text => text.IsEffectivelyVisible && text.Classes.Contains("settingsGroupHeading")).Text;

    internal static void SavePopupState(MainWindow window, SettingsPopupView popup, string folder, string fileName)
    {
        PageScreenshots.Settle(window);
        LayoutAssertions.BeforeCapture(window);
        PageScreenshots.Settle(window);
        LayoutAssertions.AssertCurrent(window);
        var captureRoot = TopLevel.GetTopLevel(popup) ?? window;
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
}
