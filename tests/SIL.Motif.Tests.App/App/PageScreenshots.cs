using SIL.Motif.Host.Assess;
using System.Text.Json;
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Commands.SelectionReading;
using SIL.Motif.Host.Texts;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Parser;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Worker.Store;
using SIL.Motif.Tests.App.Walkthrough;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>Runs only when <c>MOTIF_SCREENSHOTS</c> names a folder, so the ordinary suite never writes images.</summary>
public class ScreenshotFactAttribute : FactAttribute
{
    public const string FolderVariable = "MOTIF_SCREENSHOTS";

    public ScreenshotFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(FolderVariable)))
            Skip = $"Set {FolderVariable} to a folder to capture every page as images.";
    }
}

/// <summary>Theory form of <see cref="ScreenshotFactAttribute"/>, for a capture split into one test per case.</summary>
public sealed class ScreenshotTheoryAttribute : TheoryAttribute
{
    public ScreenshotTheoryAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ScreenshotFactAttribute.FolderVariable)))
            Skip = $"Set {ScreenshotFactAttribute.FolderVariable} to a folder to capture every page as images.";
    }
}

/// <summary>
/// Opens the real window over realistic data, then saves every page and Texts tab as an image, at a
/// collapsed-sidebar and the default width, in the light and the dark theme. The window renders with Skia and the
/// system's fonts, as the app does, so these show what a person would see, not a stub's layout.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class PageScreenshots
{
    private const string ProjectPath = @"C:\Users\linguist\FieldWorks\Projects\Sample\Sample.fwdata";
    internal static string SampleProjectPath => ProjectPath;
    private static readonly Guid Story = Guid.Parse("11111111-0000-0000-0000-000000000001");
    private static readonly Guid Letter = Guid.Parse("11111111-0000-0000-0000-000000000002");
    private static readonly TimeZoneInfo CaptureTimeZone = TimeZoneInfo.CreateCustomTimeZone(
        "Motif screenshot time", TimeSpan.FromHours(-4), "Motif screenshot time", "Motif screenshot time");
    internal static readonly DateTimeOffset CaptureStartAt =
        new(2026, 10, 2, 18, 47, 0, TimeSpan.FromHours(-4));
    internal static readonly DateTimeOffset BaselineSaveAt =
        new(2026, 9, 22, 5, 48, 0, TimeSpan.FromHours(-4));

    internal static FixedClock NewCaptureClock() => new(CaptureStartAt, CaptureTimeZone);

    public static TheoryData<string, int> Sizes => new()
    {
        { "light", 1040 }, { "light", 1240 }, { "dark", 1040 }, { "dark", 1240 },
    };

    [ScreenshotTheory]
    [MemberData(nameof(Sizes))]
    public void CaptureEveryPage(string theme, int width)
    {
        var folder = Environment.GetEnvironmentVariable(ScreenshotFactAttribute.FolderVariable)!;
        Directory.CreateDirectory(folder);
        var variant = theme == "dark" ? ThemeVariant.Dark : ThemeVariant.Light;

        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            using var culture = new CultureScope(CultureInfo.GetCultureInfo("en-US"));
            var (workspace, window) = await OpenOverSampleData();
            try
            {
                var overview = workspace.PageModel<OverviewPageModel>().Overview;
                Assert.NotNull(overview);
                OverviewPageWordsTests.AssertCaptureStopCounts(overview, "every page");
                Assert.Equal(9, overview.SelectionWordCount);

                Application.Current!.RequestedThemeVariant = variant;
                window.Width = width;
                window.Height = 780;
                foreach (var (name, page, tab) in Views())
                {
                    workspace.PageModel<TextsPageModel>().Tab = tab;
                    // Choosing a word on Texts primes Try a Word afresh, so the trace is run again here.
                    if (page == WorkspacePage.TryAWord) await TryTheSampleWord(workspace);
                    workspace.CurrentPage = page;
                    if (page == WorkspacePage.Texts && tab == TextsTab.AnalyzeTexts)
                        await AnalyzeTextsLayoutTests.SettleReaderAsync(workspace, window);
                    AssertSceneHasExpectedErrorState(window, workspace, name);
                    SavePageFrame(window, workspace, expectedPage: page,
                        expectedTab: page == WorkspacePage.Texts ? tab : null,
                        path: Path.Combine(folder, $"{name}-{width}-{theme}.png"));
                    if (page != WorkspacePage.TryAWord) continue;
                    window.Height = 1500;
                    AssertSceneHasExpectedErrorState(window, workspace, $"{name} tall");
                    SavePageFrame(window, workspace, expectedPage: page,
                        expectedTab: page == WorkspacePage.Texts ? tab : null,
                        path: Path.Combine(folder, $"{name}-{width}-{theme}-tall.png"));
                    foreach (var height in new[] { 780, 1500 })
                    {
                        workspace.Assess.Trace.IsExpert = true;
                        window.Height = height;
                        SavePageFrame(window, workspace, expectedPage: page,
                            expectedTab: page == WorkspacePage.Texts ? tab : null,
                            path: Path.Combine(folder, $"{name}-expert-{width}-{theme}-{height}.png"));
                    }
                    workspace.Assess.Trace.IsExpert = false;
                    window.Height = 780;
                }
            }
            finally
            {
                Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
                window.Close();
            }
        }, TimeSpan.FromMinutes(3));
    }

    [ScreenshotFact]
    public void CaptureRecentProjectChooser()
    {
        var folder = Environment.GetEnvironmentVariable(ScreenshotFactAttribute.FolderVariable)!;
        Directory.CreateDirectory(folder);
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var projectFolder = Path.Combine(folder, "MotifSyntheticTurkic");
            Directory.CreateDirectory(projectFolder);
            var projectPath = Path.Combine(projectFolder, "MotifSyntheticTurkic.fwdata");
            var fake = new FakeCommandClient();
            fake.KnownProjectsListIs([new KnownProjectSummary(projectPath, CaptureStartAt)
            {
                ProjectIdentity = "11111111-2222-4333-8444-555555555555",
                StateText = "Changes not applied yet",
                WordformCount = 104,
            }]);
            var selection = new SelectionViewModel(fake);
            var launcher = new Launcher();
            var workspace = new WorkspaceShellViewModel(
                new ProjectViewModel(fake, new Picker()), new BaselineViewModel(fake), selection,
                new AssessViewModel(fake, selection), new Folder(), new Drag(), fake,
                techDemoNotice: FirstRunNotice(), uriLauncher: launcher);
            var window = new MainWindow(rememberBounds: false, uriLauncher: launcher,
                preferencesStore: new MemoryUserPreferencesStore());
            window.Compose(workspace);
            window.Width = 1100;
            window.Height = 780;
            window.Show();
            try
            {
                await workspace.Project.LoadKnownProjectsAsync();
                workspace.TechDemoNotice?.GotItCommand.Execute(null);
                workspace.CurrentPage = WorkspacePage.Overview;
                var recent = Assert.Single(workspace.PageModel<OverviewPageModel>().ChooseProjectRows);
                Assert.Equal("MotifSyntheticTurkic", recent.Name);
                Assert.Equal("Changes not applied yet", recent.StateText);
                Assert.Equal("104 wordforms", recent.WordformCountText);
                Assert.True(recent.CanOpenFolder);
                Assert.Contains(window.GetVisualDescendants().OfType<CopyableTextBlock>(),
                    text => text.Text == recent.StateText);
                Assert.Contains(window.GetVisualDescendants().OfType<CopyableTextBlock>(),
                    text => text.Text == recent.WordformCountText);
                var folderButton = Assert.Single(window.GetVisualDescendants().OfType<HyperlinkButton>(),
                    button => Equals(button.Content, SIL.Motif.App.MotifAppStrings.OpenFolder));
                Assert.True(folderButton.IsEffectivelyEnabled,
                    $"Enabled={folderButton.IsEnabled}; command={folderButton.Command}; " +
                    $"parameter={folderButton.CommandParameter}; canExecute={folderButton.Command?.CanExecute(folderButton.CommandParameter)}; " +
                    $"visible={folderButton.IsVisible}; page={workspace.CurrentPage}; hasLauncher={workspace.Context.UriLauncher is not null}");
                Settle(window);
                Save(window, Path.Combine(folder, "recent-chooser-after.png"));
                Assert.True(folderButton.Focus(NavigationMethod.Tab));
                Assert.True(folderButton.IsFocused);
                Settle(window);
                Save(window, Path.Combine(folder, "recent-chooser-folder-focus.png"));
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                await workspace.OpenRecentProjectFolderCommand.ExecutionTask!;
                Assert.Equal(new Uri(projectFolder), launcher.LastUri);
                Assert.Null(workspace.OpenRecentProjectCommand.ExecutionTask);

                window.OpenFileMenu();
                Settle(window);
                window.OpenRecentMenu();
                Settle(window);
                var recentMenuItem = Assert.Single(window.RecentProjectItems);
                var menuRow = Assert.IsType<Grid>(recentMenuItem.Header);
                Assert.Contains(menuRow.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == recent.Name);
                Assert.Contains(menuRow.GetVisualDescendants().OfType<TextBlock>(),
                    text => text.Text == $"{recent.StateText} · {recent.WordformCountText}");
                var menuFolderLink = Assert.Single(menuRow.GetVisualDescendants().OfType<HyperlinkButton>());
                Assert.True(menuFolderLink.IsEffectivelyEnabled);
                Assert.Same(workspace.OpenRecentProjectFolderCommand, menuFolderLink.Command);
                Assert.Equal(recent.FolderPath, Assert.IsType<string>(menuFolderLink.CommandParameter));
                Settle(window);
                using (var frame = window.CaptureRenderedFrame()
                    ?? throw new InvalidOperationException("No frame rendered for recent-menu-after.png."))
                    frame.Save(Path.Combine(folder, "recent-menu-after.png"), PngBitmapEncoderOptions.Default);
                HeadlessClick.Click(window, menuFolderLink, "Open MotifSyntheticTurkic folder");
                await workspace.OpenRecentProjectFolderCommand.ExecutionTask!;
                Assert.Equal(new Uri(projectFolder), launcher.LastUri);
                Assert.Null(workspace.OpenRecentProjectCommand.ExecutionTask);
            }
            finally
            {
                window.Close();
                await workspace.DisposeAsync();
                Directory.Delete(projectFolder, recursive: true);
            }
        }, TimeSpan.FromMinutes(3));
    }

    [ScreenshotFact]
    public void CaptureRecentProjectMenuAtThin150()
    {
        var folder = Environment.GetEnvironmentVariable(ScreenshotFactAttribute.FolderVariable)!;
        Directory.CreateDirectory(folder);
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = new FakeCommandClient();
            fake.KnownProjectsListIs([new KnownProjectSummary(ProjectPath, CaptureStartAt)
            {
                ProjectIdentity = "11111111-2222-4333-8444-555555555555",
                StateText = "Changes not applied yet",
                WordformCount = 104,
            }]);
            var selection = new SelectionViewModel(fake);
            var workspace = new WorkspaceShellViewModel(
                new ProjectViewModel(fake, new Picker()), new BaselineViewModel(fake), selection,
                new AssessViewModel(fake, selection), new Folder(), new Drag(), fake);
            var preferences = new MemoryUserPreferencesStore();
            preferences.Update(current => current with { ZoomPercent = 150 });
            var window = new MainWindow(rememberBounds: false, uriLauncher: null, preferencesStore: preferences);
            window.Compose(workspace);
            window.Width = 960;
            window.Height = 780;
            window.Show();
            try
            {
                await workspace.Project.LoadKnownProjectsAsync();
                workspace.CurrentPage = WorkspacePage.Overview;
                var menu = window.FindInWindowOrWorkspace<Menu>("MainMenu")!;
                var compactMenu = menu.Items.OfType<MenuItem>().FirstOrDefault(item =>
                    AutomationProperties.GetAutomationId(item) == SIL.Motif.App.AutomationIds.MenuButton);
                if (compactMenu is not null)
                {
                    compactMenu.IsSubMenuOpen = true;
                    Settle(window);
                }
                window.OpenFileMenu();
                Settle(window);
                var recentItem = window.OpenRecentMenu();
                Settle(window);

                var submenu = recentItem.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.Popup>().Single();
                var submenuPresenter = Assert.IsAssignableFrom<Control>(submenu.Child);
                var submenuBounds = new Rect(submenuPresenter.TranslatePoint(default, window)!.Value,
                    submenuPresenter.Bounds.Size);
                Assert.True(submenuBounds.Left >= 0 && submenuBounds.Right <= window.ClientSize.Width,
                    $"The recent-project menu spans x {submenuBounds.Left:0.#}-{submenuBounds.Right:0.#} " +
                    $"outside the {window.ClientSize.Width:0.#} px window.");
                var recentMenuItem = Assert.Single(window.RecentProjectItems);
                var menuRow = Assert.IsType<Grid>(recentMenuItem.Header);
                var folderLink = Assert.Single(menuRow.GetVisualDescendants().OfType<HyperlinkButton>());
                var folderBounds = new Rect(folderLink.TranslatePoint(default, window)!.Value, folderLink.Bounds.Size);
                Assert.True(folderBounds.Left >= 0 && folderBounds.Right <= window.ClientSize.Width,
                    $"The folder link spans x {folderBounds.Left:0.#}-{folderBounds.Right:0.#} " +
                    $"outside the {window.ClientSize.Width:0.#} px window.");
                Save(window, Path.Combine(folder, "recent-menu-thin-150-after.png"));
            }
            finally
            {
                window.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(1));
    }

    [ScreenshotFact]
    public void CaptureOverviewEvidenceStatesAtBothWidthsAndThemes()
    {
        var folder = Environment.GetEnvironmentVariable(ScreenshotFactAttribute.FolderVariable)!;
        Directory.CreateDirectory(folder);
        var states = new List<string>();

        foreach (var state in new[] { "populated", "empty", "stale" })
        {
            AvaloniaHeadlessFixture.RunUntilComplete(async () =>
            {
                using var culture = new CultureScope(CultureInfo.GetCultureInfo("en-US"));
                var (workspace, window) = await OpenOverSampleData(parse: state != "empty", configure: (fake, _) =>
                {
                    var overview = state == "empty" ? EmptyOverview() : OverviewFor(state);
                    OverviewPageWordsTests.AssertCaptureStopCounts(overview, $"overview {state}");
                    fake.OverviewCompletesWith(overview);
                    if (state == "stale")
                    {
                        var saved = CaptureStartAt;
                        fake.CurrentBaselineCompletesWith(new CurrentBaselineResponse(Token(), BaselineSaveAt, false)
                        {
                            ProjectLastWriteUtc = saved,
                        });
                    }
                });
                try
                {
                    workspace.CurrentPage = WorkspacePage.Overview;
                    foreach (var (theme, variant) in new[] { ("light", ThemeVariant.Light), ("dark", ThemeVariant.Dark) })
                    {
                        Application.Current!.RequestedThemeVariant = variant;
                        foreach (var width in new[] { 1040, 1240 })
                        {
                            window.Width = width;
                            window.Height = 780;
                            AssertSceneHasExpectedErrorState(window, workspace, $"overview {state}");
                            if (state == "empty")
                            {
                                AssertParsePromptUsesPageAction(window);
                                var handoff = Assert.Single(window.GetVisualDescendants().OfType<Border>(),
                                    border => border.Classes.Contains("overviewHandoff"));
                                Assert.False(handoff.IsEffectivelyVisible);
                            }
                            if (state == "stale")
                            {
                                Assert.True(workspace.FreshnessIsStale);
                                var card = ProjectCardAccess.Open(window);
                                var freshnessDetail = card.GetVisualDescendants().OfType<CopyableTextBlock>().Single(text =>
                                    AutomationProperties.GetAutomationId(text) == SIL.Motif.App.AutomationIds.FreshnessDetail);
                                Assert.True(freshnessDetail.IsEffectivelyVisible);
                                Assert.Equal(workspace.FreshnessDetail, freshnessDetail.Text);
                                var detail = freshnessDetail.Text!.Replace('\u202f', ' ');
                                var baselineDate = WindowTimeText.Format(DateTimeOffset.Parse(Token().CapturedUtc), workspace.Context.Clock).Replace('\u202f', ' ');
                                var savedDate = WindowTimeText.Format(CaptureStartAt, workspace.Context.Clock).Replace('\u202f', ' ');
                                Assert.Contains(baselineDate, detail, StringComparison.Ordinal);
                                Assert.Contains($"saved {savedDate}", detail, StringComparison.Ordinal);
                                if (width == 1240)
                                    Assert.Contains($"Baseline of {baselineDate}", detail, StringComparison.Ordinal);
                                else
                                    Assert.DoesNotContain("Baseline of", detail, StringComparison.Ordinal);
                                Assert.DoesNotContain("FieldWorks has changed since the Baseline behind these numbers", detail, StringComparison.Ordinal);
                                ProjectCardAccess.Close(window);
                            }
                            var file = $"overview-{state}-{width}-{theme}.png";
                            Save(window, Path.Combine(folder, file));
                            states.Add($"{file}\tOverview {state}, {width} px, {theme} theme.");
                        }
                    }
                }
                finally
                {
                    Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
                    window.Close();
                }
            }, TimeSpan.FromMinutes(3));
        }

        File.WriteAllLines(Path.Combine(folder, "overview-states.txt"), states);
    }

    [ScreenshotFact]
    public void CaptureEmptyAnalyzeTextsAtBothWidthsAndThemes()
    {
        var folder = Environment.GetEnvironmentVariable(ScreenshotFactAttribute.FolderVariable)!;
        Directory.CreateDirectory(folder);

        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await OpenOverSampleData(parse: false);
            try
            {
                workspace.PageModel<TextsPageModel>().Tab = TextsTab.AnalyzeTexts;
                workspace.CurrentPage = WorkspacePage.Texts;
                foreach (var (theme, variant) in new[] { ("light", ThemeVariant.Light), ("dark", ThemeVariant.Dark) })
                {
                        Application.Current!.RequestedThemeVariant = variant;
                        foreach (var width in new[] { 1040, 1240 })
                        {
                            window.Width = width;
                            window.Height = 780;
                            Settle(window);
                            AssertParsePromptUsesPageAction(window);
                            Save(window, Path.Combine(folder, $"09-empty-parse-analyze-{width}-{theme}.png"));
                        }
                }
            }
            finally
            {
                Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
                window.Close();
            }
        }, TimeSpan.FromMinutes(3));
    }

    private static void AssertParsePromptUsesPageAction(MainWindow window)
    {
        var visibleButtons = window.GetVisualDescendants().OfType<Button>()
            .Where(button => button.IsEffectivelyVisible).ToArray();
        Assert.Single(visibleButtons, button => AutomationProperties.GetName(button) == "Choose what to parse");
        Assert.Single(visibleButtons, button => AutomationProperties.GetAutomationId(button) == "motif-refresh-project");
    }

    private static OverviewResponse OverviewFor(string state) => state == "stale"
        ? SampleEvidence.Overview(Assessment()) with
        {
            IsStale = true,
            LastFieldWorksSaveUtc = CaptureStartAt,
        }
        : SampleEvidence.Overview(Assessment());

    private static OverviewResponse EmptyOverview()
    {
        return SampleEvidence.Overview(Assessment()) with
        {
            AssessmentId = null, AssessedUtc = null, AssessmentElapsedSeconds = null,
            TextCoverage = new OverviewTextCoverage(0, 0, 0, 0, 9, 0),
            Accuracy = new OverviewAccuracy(0, 0, 0, 0, 0, 0, 0, 0),
            Timing = new OverviewTiming(null, null, [], 0), LookFirst = OverviewLookFirst.Empty,
        };
    }

    /// <summary>Every page, and every tab of the Texts page, with the file name each is saved under.</summary>
    internal static IEnumerable<(string Name, WorkspacePage Page, TextsTab Tab)> Views() =>
    [
        ("1-overview", WorkspacePage.Overview, TextsTab.Matrix),
        ("2a-texts-matrix", WorkspacePage.Texts, TextsTab.Matrix),
        ("2b-texts-analyze", WorkspacePage.Texts, TextsTab.AnalyzeTexts),
        ("2c-texts-lists", WorkspacePage.Texts, TextsTab.Lists),
        ("2d-texts-what-changed", WorkspacePage.Texts, TextsTab.WhatChanged),
        ("3-try-a-word", WorkspacePage.TryAWord, TextsTab.Matrix),
        ("4-timing", WorkspacePage.Timing, TextsTab.Matrix),
        ("5-warnings", WorkspacePage.Warnings, TextsTab.Matrix),
        ("6-review", WorkspacePage.Review, TextsTab.Matrix),
        ("7-ai-handoff", WorkspacePage.AiHandoff, TextsTab.Matrix),
        ("8-ai-assistants", WorkspacePage.AiAssistants, TextsTab.Matrix),
        ("9-proposals", WorkspacePage.Proposals, TextsTab.Matrix),
        ("10-settings", WorkspacePage.Settings, TextsTab.Matrix),
    ];

    [Fact]
    public void ScreenshotTraceFixtureIsFieldWorksSnapshotForAnUnparsedWord()
    {
        using var document = JsonDocument.Parse(TraceFixture());
        var root = document.RootElement;
        var grammar = root.GetProperty("provenance").GetProperty("grammar");
        Assert.Equal("pangloss.trace-details.v4", root.GetProperty("schemaVersion").GetString());
        Assert.Equal("synthetic", root.GetProperty("fixture").GetProperty("kind").GetString());
        var failed = Descendants(root.GetProperty("trace"))
            .Single(node => node.TryGetProperty("type", out var type) && type.GetString() == "Failed");
        var morphs = failed.GetProperty("attemptedMorphs").EnumerateArray().ToArray();
        var morph = morphs.Single(item => item.GetProperty("form").GetProperty("text").GetString() == "ja-");
        var identity = morph.GetProperty("identity");
        var trace = WordTraceQuery.LoadDiagnostic(TraceFixture()).Value!;
        var stopped = Assert.Single(trace.Reading.StopGroups);
        var ja = trace.Reading.Refs.Single(reference => reference.Label == "ja-");

        Assert.Equal("hawajafika", root.GetProperty("word").GetString());
        Assert.Equal("snapshot", grammar.GetProperty("sourceKind").GetString());
        Assert.False(grammar.TryGetProperty("grammarHash", out _));
        Assert.Empty(root.GetProperty("result").GetProperty("analyses").EnumerateArray());
        Assert.Equal(["ha-", "wa-", "ja-"], morphs.Select(item => item.GetProperty("form").GetProperty("text").GetString()));
        Assert.Equal("RequiredSyntacticFeatureStruct", failed.GetProperty("failureReason").GetString());
        Assert.Null(stopped.Rule);
        Assert.Equal("RequiredSyntacticFeatureStruct", stopped.ReasonCode);
        Assert.Equal(1, stopped.Count);
        Assert.Equal("NEG.PERF", ja.Gloss);
        Assert.Equal("authored", ja.IdentityQuality);
        Assert.Equal("ja-", morph.GetProperty("form").GetProperty("text").GetString());
        Assert.Equal("NEG.PERF", morph.GetProperty("gloss").GetProperty("text").GetString());
        Assert.Equal("authored", identity.GetProperty("quality").GetString());
        Assert.True(Guid.TryParse(identity.GetProperty("formId").GetString(), out _));
        Assert.True(Guid.TryParse(identity.GetProperty("msaId").GetString(), out _));
    }

    private static IEnumerable<JsonElement> Descendants(JsonElement element)
    {
        yield return element;
        if (element.ValueKind != JsonValueKind.Object) yield break;
        foreach (var property in element.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.Object)
            {
                foreach (var descendant in Descendants(property.Value)) yield return descendant;
            }
            else if (property.Value.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in property.Value.EnumerateArray())
                    foreach (var descendant in Descendants(item)) yield return descendant;
            }
        }
    }

    [Fact]
    public void ScreenshotAssessmentHasProjectStandingsAndTextOccurrences()
    {
        var assessment = Assessment();

        Assert.All(assessment.Words, word =>
        {
            Assert.False(string.IsNullOrWhiteSpace(word.ProjectStanding));
            Assert.True(word.OccurrenceCount > 0);
        });
    }

    [Fact]
    public void ScreenshotMatrixKeepsItsCellsAndItsApprovedWordsShareTheirAffixes()
    {
        var table = new AssessWordsViewModel();
        table.Load(Assessment().Words);
        var compare = new CompareViewModel();
        compare.Load(table.AllRows);
        var builtElse = compare.Cells.Single(cell => cell.Row == WordProjectStatus.Approved && cell.Column == CompareColumnKind.NoMatch);

        Assert.Equal(5, builtElse.Count);
        compare.Toggle(builtElse, additive: false);

        Assert.Equal(["a- 3SG in 2", "-a FV in 2"], compare.Shared.Select(item => $"{item.Form} {item.Gloss} {item.CountText}"));
        Assert.Equal([3, 4], compare.Words.Single(word => word.Word == "alikula").WordRow.Row.DifferingPositions);
        Assert.All(compare.Words.Where(word => word.Word != "alikula"), word => Assert.Single(word.WordRow.Row.DifferingPositions));
    }

    [Fact]
    public void ScreenshotTextWordsCountTheirOccurrences()
    {
        var words = TextWords();

        Assert.True(words.OccurrenceCount > 0);
        Assert.Equal(words.Words.Sum(word => word.Occurrences.Count), words.OccurrenceCount);
    }

    [Fact]
    public void DefaultTimingSceneShowsTheSamplesRecordedWorkSteps()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await OpenOverSampleData(seedReaderToken: false);
            try
            {
                foreach (var (_, page, tab) in Views())
                {
                    workspace.PageModel<TextsPageModel>().Tab = tab;
                    if (page == WorkspacePage.TryAWord) await TryTheSampleWord(workspace);
                    workspace.CurrentPage = page;
                    if (page == WorkspacePage.Timing) break;
                }
                Assert.Equal(WorkspacePage.Timing, workspace.CurrentPage);
                Settle(window);
                var timing = workspace.PageModel<TimingPageModel>();
                Assert.False(timing.ShowNoTimingRecorded);
                Assert.Equal("all", timing.WordSet);
                Assert.NotNull(timing.KindTiming);
                Assert.Equal(9, timing.KindTiming.WordCount);
                Assert.Equal(Assessment().Words.Select(word => word.Word).Order(),
                    timing.KindTiming.Words.Select(word => word.Word).Order());
                Assert.Equal("3,581 work steps", timing.HeadlineTotal);
            }
            finally { window.Close(); }
        }, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task SampleOverviewAndTimingDescribeTheSameAssessment()
    {
        var fake = new FakeCommandClient();
        var assessment = Assessment();
        OverviewTimingScreenshots.ReadOverviewAndTiming(fake, assessment);
        var overview = (await fake.OverviewAsync(new SIL.Motif.Contract.Requests.OverviewRequest(ProjectPath), default)).Value!;
        var timing = (await fake.TimingAsync(new SIL.Motif.Contract.Requests.TimingRequest(ProjectPath, WordSet: "all", By: "kind"), default)).Value!;
        Assert.Equal(assessment.Words.Count, overview.SelectionWordCount);
        Assert.Equal(assessment.Words.Sum(word => word.ElapsedMs), overview.AssessmentElapsedSeconds * 1000);
        Assert.Equal(assessment.Words.Select(word => word.Word).Order(), timing.Words.Select(word => word.Word).Order());
        Assert.Equal(overview.Timing.StepLimitedWordCount, overview.LookFirst.StepLimitedWords.Count);
        Assert.Equal(overview.Accuracy.ApprovedWordsNoParse, overview.LookFirst.ApprovedLostWords.Count);
        Assert.Equal(overview.Timing.Kinds.Select(row => row.SelfMs), timing.Aggregates.Select(row => row.SelfMs));
        Assert.Equal(overview.Timing.Work, timing.Work);
        Assert.Equal(overview.Timing.WorkStepAggregates.Select(row => (row.Key, row.WorkSteps)),
            timing.WorkStepAggregates.Select(row => (row.Key, row.WorkSteps)));
    }

    [Fact]
    public void SampleTraceShowsTheSameWordsEarlierAssessmentTiming()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await OpenOverSampleData(seedReaderToken: false);
            try
            {
                await TryTheSampleWord(workspace);
                var page = workspace.PageModel<TryWordPageModel>();
                Assert.True(page.HasEarlierTiming);
                Assert.Contains("hawajafika took 447 work steps", page.EarlierTimingSource);
                Assert.Equal("Share of 447 work steps", page.EarlierShareHeader);
            }
            finally { window.Close(); }
        }, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void SampleTraceMorphemesShareTheAssessmentsStoredIdentities()
    {
        var trace = WordTraceQuery.LoadDiagnostic(TraceFixture()).Value!;
        foreach (var morph in Assert.Single(trace.Reading.Attempts).Morphs)
        {
            var subject = InspectorSubject.Morpheme(morph.AllomorphId, morph.GrammaticalInfoId, morph.Form, morph.Gloss)!;
            var inspection = SampleInspection(subject);
            Assert.Contains(inspection.Uses.Value!.Words, word => word.Row.Word == "hawajafika");
        }
    }

    [Fact]
    public void SampleOpinionsAndComparisonsAgreeInEveryTextsView()
    {
        var assessment = Assessment();
        var texts = TextWords();
        foreach (var word in texts.Words)
        {
            var result = assessment.Words.Single(row => row.Word == word.Form);
            Assert.Equal(result.ProjectStanding, WordProjectStatuses.StandingOf(word));
            foreach (var token in texts.Texts.SelectMany(text => text.Lines).SelectMany(line => line.Tokens)
                         .Where(token => token.Form == word.Form))
            {
                var inText = new ResultsTokenViewModel("Sample", 1, token, result);
                var row = WordRowProjection.Of(result);
                Assert.Equal(row.Comparison!.MeaningCode, inText.Comparison.MeaningCode);
                Assert.Equal(row.Outcome, inText.Comparison.Outcome);
                Assert.Equal(row.Opinion, WordProjectStatuses.StandingOf(word));
                Assert.Equal(row.FieldWorksMorphemes.Select(morph => morph.Form),
                    inText.PrimaryFieldWorksMorphs.Select(morph => morph.Form));
            }
        }
    }

    [Fact]
    public void SampleStatisticsUseTheAssessmentsRecordedTimes()
    {
        foreach (var result in Assessment().Words)
        {
            var recorded = StatisticsRows().Single(row => row.GetProperty("form").GetString() == result.Word);
            Assert.Equal(result.ElapsedMs * 1_000_000L, recorded.GetProperty("elapsed_ns").GetInt64());
        }
    }

    [Fact]
    public void SampleStatisticsCountOnlyRecordedReadings()
    {
        foreach (var word in Assessment().Words)
        {
            var recorded = StatisticsRows().Single(row => row.GetProperty("form").GetString() == word.Word);
            Assert.Equal(word.Readings!.Count, recorded.GetProperty("passes").GetInt32());
            Assert.Equal(word.Morphology!.Analyses.Count, recorded.GetProperty("passes").GetInt32());
        }
        Assert.All(StatisticsRows().Where(row => row.GetProperty("form").GetString() is "hawajafika" or "mwalimu"),
            row => Assert.Equal(0, row.GetProperty("passes").GetInt32()));
    }

    [Fact]
    public async Task SampleStatisticsHeadlineCountsOneWordWithSeveralAnalyses()
    {
        var fake = new FakeCommandClient();
        fake.StatsCompletesWith(new StatsCommandResponse("assessment/one", ProjectPath, "cache", null, StatisticsRows()));
        var statistics = new StatisticsViewModel(fake) { ProjectPath = ProjectPath, AssessmentId = "assessment/one" };
        await statistics.LoadCommand.ExecuteAsync(null);
        Assert.Equal(1, statistics.SeveralAnalysesCount);
        Assert.Equal("1 word has more than one analysis", statistics.AnalysesHeadline);
    }

    [Fact]
    public void SampleInspectorReadsEachRulesOwnRecordedTimers()
    {
        var timing = SampleEvidence.Timing(Assessment(), "rule");
        foreach (var rule in timing.Aggregates.Take(3))
        {
            var key = new TraceTimingKey(rule.Kind, rule.Key) { IdentityQuality = rule.IdentityQuality, Scope = rule.Scope };
            var inspection = SampleInspection(InspectorSubject.Rule(key, rule.Name, rule.IdentityQuality));
            var ran = inspection.RanIn.Value!;
            Assert.Equal(rule.WordsTouched, ran.Words.Count);
            Assert.Equal(rule.SelfMs, ran.Words.Sum(word => word.ElapsedNs) / 1_000_000d);
            Assert.Equal(rule.Calls, ran.Words.Sum(word => word.Calls));
        }
    }

    [Fact]
    public void SampleBeforeParsingKeepsItsProjectCountsAndWarnings()
    {
        var empty = EmptyOverview();
        var parsed = SampleEvidence.Overview(Assessment());
        Assert.Null(empty.AssessmentId);
        Assert.Null(empty.AssessmentElapsedSeconds);
        Assert.Equal((parsed.WordformCount, parsed.RuleCount, parsed.LexemeCount),
            (empty.WordformCount, empty.RuleCount, empty.LexemeCount));
        Assert.Equal(GrammarFindings().Count, empty.Warnings!.Count);
        Assert.Equal(TextWords().Words.Count, empty.SelectionWordCount);
    }

    [Fact]
    public void SampleInspectorHasNoTimeForAnUnusedAllomorph()
    {
        var subject = InspectorSubject.Morpheme(Id("w-|allomorph", 9), SampleMorph("wa-").GrammaticalInfoId, "w-", "3PL")!;
        var inspection = SampleInspection(subject);
        Assert.Empty(inspection.Uses.Value!.Words);
        Assert.Empty(inspection.RanIn.Value!.Words);
    }

    [Fact]
    public void SampleTryWordUsesTheSharedApprovedAnalysisAndNamesItsGrammar()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await OpenOverSampleData(seedReaderToken: false);
            try
            {
                await TryTheSampleWord(workspace);
                var page = workspace.PageModel<TryWordPageModel>();
                Assert.Equal("Approved", Assert.Single(page.FieldWorksAnalyses).OpinionLabel);
                Assert.Contains("ha- + wa- + ja- + fik + -a", page.FieldWorksAnalyses[0].Text);
                Assert.Equal(workspace.Context.Evidence.Assessment!.Assessment.Baseline.Token,
                    page.Trace.Result!.HostCapture!.Baseline!.Token);
                Assert.Null(page.Trace.Result.ParserElapsedMs);
            }
            finally { window.Close(); }
        }, TimeSpan.FromSeconds(15));
    }

    internal static async Task TryTheSampleWord(WorkspaceShellViewModel workspace)
    {
        workspace.Context.TryWord("hawajafika");
        await workspace.Assess.Trace.TryCommand.ExecutionTask!;
    }

    internal static void AssertSceneHasExpectedErrorState(MainWindow window, WorkspaceShellViewModel workspace,
        string scene, string? expectedCode = null)
    {
        Settle(window);
        var codes = window.GetVisualDescendants().OfType<RefusalBlock>()
            .Where(block => block.IsEffectivelyVisible)
            .Select(block => (block.DataContext as WindowRefusal)?.Code ?? "unbound refusal")
            .ToList();
        if (workspace.CurrentPage == WorkspacePage.Overview &&
            workspace.PageModel<OverviewPageModel>().OverviewRefusal is { } overviewRefusal)
            codes.Add(overviewRefusal.Code);

        if (expectedCode is null)
            Assert.True(codes.Count == 0, $"Scene '{scene}' displays error state(s): {string.Join(", ", codes)}.");
        else
            Assert.Equal([expectedCode], codes);
    }

    internal static void Save(MainWindow window, string path)
    {
        Settle(window);
        LayoutAssertions.BeforeCapture(window);
        SaveRenderedFrame(window, path);
    }

    /// <summary>Checks the explicit page route immediately before saving a standard page capture.</summary>
    internal static void SavePageFrame(MainWindow window, WorkspaceShellViewModel workspace,
        WorkspacePage expectedPage, TextsTab? expectedTab, string path)
    {
        Settle(window);
        LayoutAssertions.BeforeCapture(window);
        MatrixCaptureState.Assert(window, workspace, expectedPage, expectedTab);
        SaveRenderedFrame(window, path);
    }

    /// <summary>Checks one unchanged scene across all layout cells, then captures both widths and themes.</summary>
    internal static void SaveAll(MainWindow window, string folder, string scene)
    {
        Settle(window);
        LayoutAssertions.BeforeCapture(window);
        foreach (var (theme, variant) in new[] { ("light", ThemeVariant.Light), ("dark", ThemeVariant.Dark) })
        {
            Application.Current!.RequestedThemeVariant = variant;
            foreach (var width in new[] { 1040, 1240 })
            {
                window.Width = width;
                Settle(window);
                LayoutAssertions.AssertCurrent(window);
                SaveRenderedFrame(window, Path.Combine(folder, $"{scene}-{width}-{theme}.png"));
            }
        }
    }

    private static void SaveRenderedFrame(MainWindow window, string path)
    {
        using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException($"No frame rendered for {path}.");
        frame.Save(path, PngBitmapEncoderOptions.Default);
    }

    /// <summary>Lets bindings, layout and a render pass catch up with the last change to the window.</summary>
    internal static void Settle(Avalonia.Controls.Window window)
    {
        for (var pass = 0; pass < 3; pass++)
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
    }

    /// <summary>
    /// Opens the window over the sample project; without <paramref name="parse"/> it stops before the first parse,
    /// and with <paramref name="leaveSetupOpen"/> it stops with first-run setup still showing.
    /// Component fixtures can omit shell composition with <paramref name="composeWindow"/> and build their own view.
    /// A test that never reads the selected reader token can skip seeding it with <paramref name="seedReaderToken"/>.
    /// A composed window is attached after the sample is populated (the live window rebinding on every state change
    /// costs several times the populate); a test that checks what the window shows while the data arrives passes
    /// <paramref name="windowFirst"/> to attach it first.
    /// </summary>
    internal static async Task<(WorkspaceShellViewModel Workspace, MainWindow Window)> OpenOverSampleData(
        bool parse = true, Action<FakeCommandClient, AssessCommandResponse>? configure = null, bool leaveSetupOpen = false,
        string? settingsFilePath = null, TextWordsResponse? capturedWords = null,
        AssessCommandResponse? capturedAssessment = null, bool advancedAiModeEnabled = false, IUserPreferencesStore? preferencesStore = null,
        SIL.Motif.Commands.Assistants.IAssistantConnectionService? assistantConnections = null,
        bool renderForCapture = true, Action<string, double>? recordSetup = null, bool composeWindow = true,
        bool activateTextsPage = true, Action<MainWindow>? configureWindow = null, bool seedReaderToken = true,
        bool windowFirst = false)
    {
        MainWindow? window = null;
        SampleWorkspace? sample = null;
        if (composeWindow && !windowFirst)
        {
            var populated = await OpenSampleCore(parse, configure, leaveSetupOpen, capturedWords, capturedAssessment,
                advancedAiModeEnabled, assistantConnections, recordSetup, composeWindow: false, activateTextsPage,
                seedReaderToken, attachShell: null, settle: null);
            IUserPreferencesStore store = preferencesStore ?? (settingsFilePath is null
                ? new MemoryUserPreferencesStore()
                : new FileUserPreferencesStore(settingsFilePath));
            var late = new MainWindow(rememberBounds: false, uriLauncher: null, preferencesStore: store);
            configureWindow?.Invoke(late);
            late.Compose(populated.Workspace);
            late.Closed += async (_, _) => await populated.DisposeAsync();
            late.Show();
            if (renderForCapture) Settle(late); else LayoutInvariantEngineTests.Settle(late);
            return (populated.Workspace, late);
        }
        sample = await OpenSampleCore(parse, configure, leaveSetupOpen, capturedWords, capturedAssessment,
            advancedAiModeEnabled, assistantConnections, recordSetup, composeWindow, activateTextsPage, seedReaderToken,
            attachShell: workspace =>
            {
                IUserPreferencesStore preferences = preferencesStore ?? (settingsFilePath is null
                    ? new MemoryUserPreferencesStore()
                    : new FileUserPreferencesStore(settingsFilePath));
                window = new MainWindow(rememberBounds: false, uriLauncher: null, preferencesStore: preferences);
                configureWindow?.Invoke(window);
                if (composeWindow) window.Compose(workspace);
                else workspace.UpdateWindowWidth(window.Width);
                window.Closed += async (_, _) => await sample!.DisposeAsync();
                window.Show();
            },
            settle: () =>
            {
                if (renderForCapture) Settle(window!);
                else LayoutInvariantEngineTests.Settle(window!);
            });
        return (sample.Workspace, window!);
    }

    /// <summary>
    /// The sample project over the fakes and reader fixture with no window: <paramref name="attachShell"/> is called
    /// with the shell before the project opens (a windowed caller composes its window there) and
    /// <paramref name="settle"/> lets bindings and layout catch up (default: run queued dispatcher jobs).
    /// </summary>
    internal static async Task<SampleWorkspace> OpenSampleCore(
        bool parse, Action<FakeCommandClient, AssessCommandResponse>? configure, bool leaveSetupOpen,
        TextWordsResponse? capturedWords, AssessCommandResponse? capturedAssessment, bool advancedAiModeEnabled,
        SIL.Motif.Commands.Assistants.IAssistantConnectionService? assistantConnections,
        Action<string, double>? recordSetup, bool composeWindow, bool activateTextsPage, bool seedReaderToken,
        Action<WorkspaceShellViewModel>? attachShell, Action? settle)
    {
        var stage = System.Diagnostics.Stopwatch.StartNew();
        void Record(string name) { recordSetup?.Invoke(name, stage.Elapsed.TotalMilliseconds); stage.Restart(); }
        var fake = new FakeCommandClient();
        var clock = NewCaptureClock();
        if (!activateTextsPage)
            fake.DefaultSelectionCompletesWith(new NamedSelectionProjection(
                "Default", [], [], string.Empty, string.Empty, 1000,
                SIL.Motif.Contract.Assess.StepCap.Default));
        fake.KnownProjectsListIs([new KnownProjectSummary(ProjectPath, CaptureStartAt)]);
        fake.CurrentBaselineCompletesWith(new CurrentBaselineResponse(Token(), BaselineSaveAt, false)
        {
            ProjectLastWriteUtc = BaselineSaveAt,
        });
        fake.ProjectHistoryIs(new ProjectHistoryResponse(
        [
            new ProjectHistoryEntry(CaptureStartAt.AddMinutes(-20), ProjectHistoryKind.Assessment, "9 words · 7 parsed · 5 built something else"),
            new ProjectHistoryEntry(DateTimeOffset.Parse(Token().CapturedUtc), ProjectHistoryKind.Baseline, "Baseline captured · 862 lexemes · 2 texts"),
        ]));
        fake.StoredGrammarCheckIs(new GrammarCheckResponse(GrammarFindings(), HasBaseline: true));
        fake.CheckGrammarCompletesWith(new GrammarCheckResponse(GrammarFindings(), HasBaseline: true));
        fake.AssessCompletesWith(capturedAssessment ?? Assessment());
        fake.StatsCompletesWith(new StatsCommandResponse("assessment/one", ProjectPath, "cache", null, StatisticsRows(capturedAssessment)));
        fake.HandoffCompletesWith(new HandoffCommandResponse(@"C:\Users\linguist\Documents\Motif Handoffs\Sample 2 Oct 6:47 PM",
            Capture(), new SelectionProjection([], []),
            HandoffFileExpectations.Assessed.Select(file => file.RelativePath).ToArray(), ["assessment/one"])
        {
            InvocationId = "assessment/one",
            HandoffMarkdown = SampleHandoffMarkdown,
        });

        // Try a Word traces through the page's own path: a result set directly is wiped when a word is chosen.
        fake.TraceWordCompletesWith(WordTraceQuery.LoadDiagnostic(TraceFixture()).Value! with
        {
            ParserElapsedMs = null,
            HostCapture = new TraceHostCapture(Token().ProjectIdentity, null, null, Token().BundleDigest, null, null, [])
            {
                Baseline = new TraceBaselineSource(Token(), BaselineSaveAt,
                    DateTimeOffset.Parse(Token().CapturedUtc), "Sample grammar capture"),
            },
        });
        fake.WordContextHandler = (request, _) =>
        {
            var word = Assessment().Words.FirstOrDefault(word => word.Word == request.Word);
            return Task.FromResult(CommandOutcome<WordContextResponse>.Success(new(request.Word, true)
            {
                IsInFieldWorks = word is not null,
                Baseline = Token(), SourceLastWriteUtc = BaselineSaveAt,
                Analyses = word?.StoredAnalyses ?? [],
                ExpectedAnalysis = word?.StoredAnalyses.FirstOrDefault(analysis => analysis.StoredAnalysisOpinion == ReadingGrade.Approved),
            }));
        };
        fake.OnInspect((request, _) => Task.FromResult(CommandOutcome<InspectResponse>.Success(SampleInspection(request.Subject))));
        OverviewTimingScreenshots.ReadOverviewAndTiming(fake, capturedAssessment ?? Assessment());
        if (!parse) fake.OverviewCompletesWith(EmptyOverview());
        var readerFixture = capturedWords is null ? SampleReaderFixture(parse) :
            StoredSelectionFixture.FromDisplayRecords(capturedWords, Token());
        var inventory = TextInventoryQuery.Query(new(readerFixture.ProjectPath));
        if (!inventory.Succeeded || inventory.Value is null) readerFixture.Dispose();
        Assert.True(inventory.Succeeded, inventory.Refusal?.Message);
        Assert.NotNull(inventory.Value);
        fake.ListTextsCompletesWith(inventory.Value);
        configure?.Invoke(fake, capturedAssessment ?? Assessment());
        Record("configure-fakes");
        if (parse && capturedWords is not null) readerFixture.RecordAssessment(capturedAssessment ?? Assessment());
        fake.SelectionReaderHandler = readerFixture.OpenAsync;
        fake.OnReadWordState((request, cancellation) => Task.Run(() =>
            SIL.Motif.Commands.ReadStateCommands.Execute(request with { ProjectPath = readerFixture.ProjectPath },
                cancellation), cancellation));
        var check = await fake.ReadStoredGrammarCheckAsync(new GrammarCheckRequest(ProjectPath), CancellationToken.None);
        if (check.Value?.Check is { } warnings) readerFixture.RecordGrammarCheck(warnings);

        Record("publish-store");
        var selection = new SelectionViewModel(fake);
        var workspace = new WorkspaceShellViewModel(
            new ProjectViewModel(fake, new Picker()), new BaselineViewModel(fake),
            selection, new AssessViewModel(fake, selection),
            new Folder(), new Drag(),
            fake, clock, techDemoNotice: FirstRunNotice(), advancedAiModeEnabled: advancedAiModeEnabled,
            assistantConnections: assistantConnections);
        var sample = new SampleWorkspace(workspace, fake, readerFixture);
        attachShell?.Invoke(workspace);

        Record("compose-and-show");
        await workspace.SetProjectAsync(readerFixture.ProjectPath);
        Record("open-project");
        if (leaveSetupOpen) return sample;
        workspace.Context.Setup?.SkipCommand.Execute(null);
        foreach (var text in selection.Texts) text.IsChecked = true;
        await Task.Yield();
        await workspace.Context.EvidencePublication;
        if (!parse)
        {
            await workspace.Context.EvidencePublication;
            return sample;
        }
        Record("select-texts");
        if (activateTextsPage)
            await workspace.Assess.RunCommand.ExecuteAsync(null);
        else
        {
            Assert.True(workspace.ParseAllWordsCommand.CanExecute(null));
            await workspace.ParseAllWordsCommand.ExecuteAsync(null);
        }
        await workspace.Context.EvidencePublication;
        Record("assess");
        workspace.PageModel<TimingPageModel>().Statistics.AssessmentId = "assessment/one";
        await workspace.PageModel<TimingPageModel>().Statistics.LoadCommand.ExecuteAsync(null);
        Record("statistics");
        workspace.Assess.Words.SelectedRow = workspace.Assess.Words.Rows.FirstOrDefault(row => row.Word == "hawajafika");
        var textsPage = workspace.PageModel<TextsPageModel>();
        if (activateTextsPage)
        {
            workspace.CurrentPage = WorkspacePage.Texts;
            textsPage.Tab = TextsTab.AnalyzeTexts;
            textsPage.ShowAnalyzeViewCommand.Execute(AnalyzeTextsView.TextReader);
            (settle ?? (() => Dispatcher.UIThread.RunJobs()))();
        }
        var reader = textsPage.ResultsInText;
        await reader.SelectionRefresh;
        if (!composeWindow) reader.ShowReader();
        if (activateTextsPage && seedReaderToken)
        {
            await SeedSampleReaderTokenAsync(workspace);
            Record("reader-and-token");
        }
        await workspace.PageModel<AiHandoffPageModel>().Handoff.RunCommand.ExecuteAsync(null);
        Record("handoff");
        clock.Advance(TimeSpan.FromDays(1).Add(TimeSpan.FromMinutes(35)));
        workspace.PageModel<AiHandoffPageModel>().Handoff.LatestAssessmentAt = clock.GetLocalNow();
        return sample;
    }

    /// <summary>
    /// The sample project's view models without a composed shell, a parse, an Assess run or a seeded reader token,
    /// for tests that host one page or control themselves, or check only view-model state.
    /// </summary>
    internal static Task<(WorkspaceShellViewModel Workspace, MainWindow Window)> OpenSampleModelsOnly(
        Action<FakeCommandClient, AssessCommandResponse>? configure = null, TextWordsResponse? capturedWords = null) =>
        OpenOverSampleData(parse: false, configure: configure, capturedWords: capturedWords,
            composeWindow: false, activateTextsPage: false);

    internal static async Task SeedSampleReaderTokenAsync(WorkspaceShellViewModel workspace)
    {
        var reader = workspace.PageModel<TextsPageModel>().ResultsInText;
        await reader.SelectionRefresh;
        Assert.Null(workspace.Context.SelectionReads.Refusal);
        var positions = workspace.Context.SelectionReads.Summary!.SourcePositions;
        var first = positions.FirstOrDefault(position => position.Word.Form == "alikula") ?? positions.FirstOrDefault();
        Assert.NotNull(first);
        var token = await reader.ReadOccurrenceAsync(first.Location.Anchor);
        Assert.NotNull(token);
        reader.SelectToken(token);
    }

    private const string SampleHandoffMarkdown =
        "# AI Handoff: Sample\n\n" +
        "These files describe how the grammar of **Sample** parsed 9 words from 2 texts.\n\n" +
        "- `parse-results.json`: every word, whether it parsed, and how long it took.\n" +
        "- `texts.json`: the chosen texts with the analyses the project stores.\n" +
        "- `grammar.json`: the grammar the parser used.\n\n" +
        "Start with *hawajafika*, approved in FieldWorks as ha-wa-ja-fik-a but not parsed.\n";

    private static IReadOnlyList<GrammarWarning> GrammarFindings() => SeededGrammarFindings.All();

    private static readonly IReadOnlyDictionary<(string Form, string Gloss), ParserReadingMorph> TraceMorphology =
        WordTraceQuery.LoadDiagnostic(TraceFixture()).Value!.Reading.Attempts.Single().Morphs
            .ToDictionary(morph => (morph.Form, morph.Gloss));

    private static readonly (string Word, string[] Forms, string[] Glosses)[] Vocabulary =
    [
        ("Sungura", ["sungura"], ["hare"]),
        ("alikula", ["a-", "li-", "kul", "-a"], ["3SG", "PST", "eat", "FV"]),
        ("chakula", ["ch-", "akula"], ["7", "food"]),
        ("hawajafika", ["ha-", "wa-", "ja-", "fik", "-a"], ["NEG", "3PL", "NEG.PERF", "arrive", "FV"]),
        ("watoto", ["wa-", "toto"], ["2", "child"]),
        ("walikula", ["wa-", "li-", "kul", "-a"], ["3PL", "PST", "eat", "FV"]),
        ("mwalimu", ["m-", "walimu"], ["1", "teacher"]),
        ("anapenda", ["a-", "na-", "pend", "-a"], ["3SG", "PRS", "love", "FV"]),
        ("kitabu", ["ki-", "tabu"], ["7", "book"]),
    ];

    private static ParseAnalysis Reading(string word) => RawReading(Resolved(word));

    private static ParseAnalysis RawReading(ParserReading reading) =>
        new([.. reading.Morphs.Select(morph => new ParseMorph(morph.AllomorphId!, morph.GrammaticalInfoId!, null, null))]);

    private static string Id(string word, int index) =>
        new Guid(System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(word + index))).ToString("D");

    private static ParserReading Resolved(string word)
    {
        var item = Vocabulary.Single(entry => entry.Word == word);
        return new ParserReading([.. item.Forms.Select((form, index) =>
        {
            TraceMorphology.TryGetValue((form, item.Glosses[index]), out var traceMorph);
            return new ParserReadingMorph(form, item.Glosses[index], index == item.Forms.Length - 1 ? "v" : "", null, false,
                "silfw://localhost/link?tool=lexiconEdit")
            {
                AllomorphId = traceMorph?.AllomorphId ?? Id("allomorph " + form + item.Glosses[index], 0),
                GrammaticalInfoId = traceMorph?.GrammaticalInfoId ?? Id("grammatical info " + item.Glosses[index], 0),
            };
        })]);
    }

    /// <summary>The first sample morpheme spelled <paramref name="form"/>, with the ids every word that uses it shares.</summary>
    internal static ParserReadingMorph SampleMorph(string form) =>
        Vocabulary.Select(item => item.Word).SelectMany(word => Resolved(word).Morphs).First(morph => morph.Form == form);

    /// <summary>
    /// What the inspector reads for the sample: the words whose analyses use a morpheme, by identity, with the
    /// sample's own FieldWorks facts; or, for a rule, its words from the sample timings.
    /// </summary>
    internal static InspectResponse SampleInspection(InspectorSubject subject)
    {
        var asked = new ObjectUseRef
        {
            AllomorphId = subject.AllomorphId, GrammaticalInfoId = subject.GrammaticalInfoId,
            TimingKind = subject.TimingKey?.Kind, TimingKey = subject.TimingKey?.Key, Label = subject.Label, Gloss = subject.Gloss,
            TimingIdentityQuality = subject.TimingKey?.IdentityQuality ?? "authored", TimingScope = subject.TimingKey?.Scope,
        };
        var assessment = Assessment();
        var words = assessment.Words;
        var facts = SampleFacts(asked);
        asked = ObjectUsesQuery.WithTimingKey(asked, facts)!;
        var timings = SampleEvidence.ObjectTimings;
        var morpheme = subject.Kind == InspectorSubjectKind.Morpheme;
        return new InspectResponse(subject, InspectorResolution.Resolved)
        {
            AssessmentId = "assessment/one",
            TimingKey = asked.TimingKind is null ? null : new TraceTimingKey(asked.TimingKind, asked.TimingKey!),
            Facts = InspectorSection<ObjectFacts>.Of(facts),
            Uses = morpheme ? InspectorSection<ObjectUseWords>.Of(ObjectUsesQuery.UsesOf(words, asked))
                : InspectorSection<ObjectUseWords>.Not(InspectorSectionStatus.Unsupported, "Only a morpheme is used by words."),
            RanIn = InspectorSection<ObjectUseWords>.Of(ObjectUsesQuery.RanIn(words, timings, asked)),
            Warnings = InspectorSection<IReadOnlyList<GrammarWarning>>.Of(
                InspectQuery.WarningsNaming(GrammarFindings(), subject, asked.TimingKind is null ? null
                    : new TraceTimingKey(asked.TimingKind, asked.TimingKey!))),
        };
    }

    private static ObjectFacts SampleFacts(ObjectUseRef asked)
    {
        TraceFieldWorksTarget Tool(string tool, string name, string id) =>
            new(tool, name, id, $"silfw://localhost/link?tool={tool}&guid={id}");
        if (asked.AllomorphId is null)
            return new ObjectFacts
            {
                Rule = new ObjectFactsRule(asked.TimingKey!, asked.TimingKind == "phon_rule" ? "phonologicalRule" : "affixRule",
                    asked.Label ?? asked.TimingKey!)
                {
                    FieldWorks = asked.TimingKind == "phon_rule"
                        ? Tool("PhonologicalRuleEdit", "Phonological Rules", asked.TimingKey!)
                        : Tool("lexiconEdit", "Lexicon Edit", asked.TimingKey!),
                },
            };
        var form = asked.Label ?? "?";
        var affix = form.StartsWith('-') || form.EndsWith('-');
        var headword = form == "w-" ? "wa-" : form;
        var entry = Id(headword + "|entry", 3);
        return new ObjectFacts
        {
            Entry = new ObjectFactsEntry(entry, headword)
            {
                MorphType = form.EndsWith('-') ? "prefix" : form.StartsWith('-') ? "suffix" : "root",
                FieldWorks = Tool("lexiconEdit", "Lexicon Edit", entry),
            },
            Senses = [new ObjectFactsSense(Id(form + "|sense", 4), "1") { Gloss = asked.Gloss, FieldWorks = Tool("lexiconEdit", "Lexicon Edit", entry) }],
            GrammaticalInfo = new ObjectFactsGrammaticalInfo(asked.GrammaticalInfoId ?? entry, affix ? "inflectionalAffix" : "stem")
            {
                Category = new ObjectFactsNamed(Id("verb", 5), affix || form == "kul" || form == "fik" ? "Verb" : "Noun")
                {
                    FieldWorks = Tool("posEdit", "Category Edit", Id("verb", 5)),
                },
                Slots = affix && form.EndsWith('-')
                    ? [new ObjectFactsSlot(Id("slot", 6), "Subject") { Templates = [new ObjectFactsNamed(Id("template", 7), "Finite verb")],
                        FieldWorks = Tool("posEdit", "Category Edit", Id("verb", 5)) }]
                    : [],
            },
            // Swahili's wa- loses its vowel before a vowel, which gives the breadcrumb a second allomorph to step to.
            TimingKey = affix ? new TraceTimingKey("morph_rule", asked.GrammaticalInfoId ?? entry) : new TraceTimingKey("lex_entry", entry),
            Allomorphs = form is "wa-" or "w-"
                ?
                [
                    new ObjectFactsAllomorph(SampleMorph("wa-").AllomorphId!, "wa-") { IsAsked = form == "wa-" },
                    new ObjectFactsAllomorph(Id("w-|allomorph", 9), "w-")
                    {
                        IsAsked = form == "w-",
                        Environments = [new ObjectFactsEnvironment(Id("env-v", 9), "/ _ [V]")
                            { FieldWorks = Tool("EnvironmentEdit", "Environments", Id("env-v", 9)) }],
                    },
                ]
                : [new ObjectFactsAllomorph(asked.AllomorphId, form) { IsAsked = true }],
        };
    }

    private static readonly (string Form, string Gloss)[] TraceMorphemes = [("tin", "see"), ("ma-", "PST"), ("-lu", "3SG")];

    /// <summary>The Inspector test trace with authored identities added to its recorded morphemes.</summary>
    internal static WordTraceResponse TraceWithIdentities()
    {
        var document = System.Text.Json.Nodes.JsonNode.Parse(SIL.Motif.Tests.TestFixtures.TestTraceFixture.Read(Path.Combine(
            AppContext.BaseDirectory, "TestFixtures", "trace-details-v3-matinlu.json")))!;
        void Walk(System.Text.Json.Nodes.JsonNode? node)
        {
            if (node is System.Text.Json.Nodes.JsonArray array)
                foreach (var item in array) Walk(item);
            if (node is not System.Text.Json.Nodes.JsonObject element) return;
            if (element["identity"] is System.Text.Json.Nodes.JsonObject identity && identity.ContainsKey("formId"))
            {
                var morpheme = int.Parse(identity["morphemeId"]?.ToString() ?? "0", System.Globalization.CultureInfo.InvariantCulture);
                identity["formId"] = Id("trace-allomorph", 10 + morpheme);
                identity["msaId"] = Id("trace-msa", 10 + morpheme);
                identity["quality"] = "authored";
                var (form, gloss) = TraceMorphemes[morpheme % TraceMorphemes.Length];
                element["form"] ??= form;
                element["gloss"] ??= gloss;
            }
            if (element["type"]?.ToString() is { } type && type.StartsWith("MorphologicalRule", StringComparison.Ordinal) &&
                element["source"]?.ToString() is { } source)
                element["sourceIdentity"] = new System.Text.Json.Nodes.JsonObject
                {
                    ["kind"] = "morphRule", ["id"] = Id(source, 8), ["quality"] = "authored",
                };
            foreach (var (_, child) in element.ToArray()) Walk(child);
        }
        Walk(document);
        return WordTraceQuery.LoadDiagnostic(document.ToJsonString()).Value!;
    }

    // Morphemes named by identity, so the Matrix can say what words share; another stem keeps each word's cell.
    private static ParserReading ApprovedInAssessment(string word)
    {
        return new ParserReading([.. Resolved(word).Morphs.Select(morph => morph with
        {
            AllomorphId = morph.Form.Contains('-') ? morph.AllomorphId : Id("stem of " + word + " " + morph.Form + morph.Gloss, 0),
        })])
        {
            StoredAnalysisId = Id(word, 97),
            StoredAnalysisOpinion = ReadingGrade.Approved,
            Identity = StoredIdentity(word, 7),
        };
    }

    private static ApprovedMorphology StoredIdentity(string word, int variant) =>
        new([.. Resolved(word).Morphs.Select(morph => new ApprovedMorph(
            variant == 0 || morph.Form.Contains('-') ? morph.AllomorphId! :
                Id("stem of " + word + " " + morph.Form + morph.Gloss, 0),
            morph.GrammaticalInfoId!, null, [morph.Form]))]);

    // FieldWorks' kul against PanGloss's ku- + l: the sample's one word whose two analyses part inside a morpheme.
    private static ParserReading AlikulaApproved() =>
        new([Piece("a-", "3SG"), Piece("li-", "PST"), Piece("kul", "eat"), Piece("-a", "FV")])
        {
            StoredAnalysisId = Id("alikula", 90),
            StoredAnalysisOpinion = ReadingGrade.Approved,
        };

    // The sample's identities, so alikula's affixes are the ones the other approved words use.
    private static ParserReadingMorph Piece(string form, string gloss) =>
        new(form, gloss, "", null, false, "silfw://localhost/link?tool=lexiconEdit")
        {
            AllomorphId = Id((form.Contains('-') ? "allomorph " : "stem of alikula ") + form + gloss, 0),
            GrammaticalInfoId = Id("grammatical info " + gloss, 0),
        };

    private static ProjectAnalysis Stored(ParserReading reading) => new(
        reading.StoredAnalysisId!, reading.Morphs)
    {
        StoredAnalysisId = reading.StoredAnalysisId,
        StoredAnalysisOpinion = reading.StoredAnalysisOpinion,
        Identity = reading.Identity,
    };

    [Fact]
    public async Task ScreenshotSelectionOpensAndReadsItsCurrentFormatCapturedLines()
    {
        using var fixture = SampleReaderFixture(true);
        var opened = await fixture.OpenAsync(new OpenSelectionReaderRequest(fixture.ProjectPath, [Story, Letter], []));
        Assert.True(opened.Succeeded, opened.Refusal?.Message);
        await using var reader = opened.Value!;
        var presentation = await reader.ReadPresentationAsync();
        Assert.True(presentation.Succeeded, presentation.Refusal?.Message);
        using var state = presentation.Value!;
        var outcome = await reader.ReadLinePagesAsync(Story,
            [new TextLinePage(1, 0, 4), new(2, 0, 4)]);
        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        using var lines = outcome.Value!;
        Assert.Equal(["Sungura alikula chakula.", "watoto hawajafika, mwalimu"],
            lines.Value.Lines.Select(line => line.Sentence));
        var anchor = new OccurrenceAnchor(Story, Guid.Parse(Id("paragraph " + Story, 1)),
            Guid.Parse(Id("segment " + Story, 1)), 1);
        var marked = SIL.Motif.Commands.ReadStateCommands.Execute(new WordReadStateRequest(fixture.ProjectPath,
            Story, [anchor], true) { ExpectedContext = reader.Context.ExpectedWriteContext() });
        Assert.True(marked.Succeeded, marked.Refusal?.Message);
        Assert.Contains(anchor, marked.Value!.ReadOccurrences);
        Assert.Empty(marked.Value.SkippedOccurrences);
    }

    private static StoredSelectionFixture SampleReaderFixture(bool parse)
    {
        var assessment = Assessment();
        var forms = assessment.Words.ToDictionary(word => word.Word, word =>
        {
            var analyses = word.StoredAnalyses.Select(reading => new TextWordsProjectedAnalysis(reading.StoredAnalysisId!,
                reading.Morphs.Select(morph => new TextWordsProjectedMorph(morph.Form, morph.Gloss, morph.Category,
                    null, morph.Guessed, null)
                { FormWritingSystem = morph.FormWritingSystem, GlossWritingSystem = morph.GlossWritingSystem,
                    CategoryWritingSystem = morph.CategoryWritingSystem }).ToArray())
            { AnalysisId = Guid.Parse(reading.StoredAnalysisId!),
                Opinion = reading.StoredAnalysisOpinion == ReadingGrade.Candidate ? "unknown" : reading.StoredAnalysisOpinion!,
                Identity = reading.Identity! with
                { SourceAnalysisId = CanonicalId.FromGuid(Guid.Parse(reading.StoredAnalysisId!)).Value } }).ToArray();
            return new TextWordsProjectedWordform(Guid.Parse(Id("wordform " + word.Word, 0)),
                analyses.Where(analysis => analysis.Opinion == ReadingGrade.Approved).ToArray(),
                analyses.Where(analysis => analysis.Opinion == ReadingGrade.Disapproved).ToArray(),
                analyses.Count(analysis => analysis.Opinion == "unknown"), false, analyses);
        });
        TextWordsProjectedText Text(Guid id, string title, params string[][] lines) => new(id, title,
            lines.Select((items, index) => new TextWordsProjectedLine(index + 1,
                string.Join(" ", items).Replace(" .", ".").Replace(" ,", ","),
                items.Select((form, offset) =>
                {
                    var word = forms.GetValueOrDefault(form);
                    var analysis = word?.Analyses.FirstOrDefault();
                    return new TextWordsProjectedToken(form, word is null ? [] : [new WritingSystemText(form, "sw")],
                        word?.WordformId, word is null ? null : analysis?.Opinion == ReadingGrade.Approved ? "approved" :
                            analysis?.Opinion == ReadingGrade.Disapproved ? "disapproved" : analysis is null ? "unanalysed" : "unapproved",
                        analysis?.Key, null, null, word is null ? null : new FieldWorksLinkTarget("analyses", word.WordformId),
                        offset, analysis?.AnalysisId) { TextWritingSystem = word is null ? null : "sw" };
                }).ToArray(), Guid.Parse(Id("paragraph " + id, index + 1)),
                Guid.Parse(Id("segment " + id, index + 1)), true)).ToArray(),
            lines.SelectMany(items => items).Distinct().Where(forms.ContainsKey)
                .SelectMany(form => forms[form].Analyses).ToArray());
        var projection = new TextWordsProjection(
        [
            Text(Story, "Hadithi ya sungura", ["Sungura", "alikula", "chakula", "."],
                ["watoto", "hawajafika", ",", "mwalimu"]),
            Text(Letter, "Barua kwa mwalimu", ["walikula", "anapenda", "kitabu", "."]),
        ], forms.Values.ToArray());
        var fixture = new StoredSelectionFixture(projection, Token());
        if (parse) fixture.RecordAssessment(new NewAssessmentRecord(
            AssessmentId: "assessment/one",
            ProposalId: null,
            ProposalIntentDigest: null,
            Assessor: "pangloss",
            Kind: AssessmentKinds.ParseTime,
            ScopeJson: "{}",
            ScopeDigest: "sha256:sample-scope",
            Subject: new AssessmentSubject.Words(Selection.Create("Sample", assessment.Words.Select(word => word.Word)), "whitespace", "1"),
            BaselineToken: JsonSerializer.Serialize(Token(), SIL.Motif.Contract.MotifJson.CreateOptions()),
            OutcomeDigest: "sha256:sample-outcome",
            SemanticDigest: "sha256:sample-semantic",
            GrammarSourceSha256: "sha256:sample-grammar",
            ModelFingerprint: "sample",
            Pipeline: "sample",
            DiagnosticCount: 0,
            Material: new AssessmentRecordMaterial.Words(assessment.Words.Select(word => new AssessedWord(
                word.Word, word.Outcome, [], word.ElapsedMs)
            { Morphology = word.Morphology, IsIncomplete = word.IsIncomplete, ProjectStanding = word.ProjectStanding,
                ReadingGrades = word.ReadingGrades, OccurrenceCount = word.OccurrenceCount,
                AnalysisComparison = word.AnalysisComparison, MissedApproved = word.MissedApproved }).ToArray(), null, null, []),
            SavedUtc: CaptureStartAt.ToString("O")
        ));
        return fixture;
    }

    internal static TextWordsResponse TextWords()
    {
        var assessment = Assessment();
        TextToken Token(string form)
        {
            var word = assessment.Words.Single(word => word.Word == form);
            var stored = word.StoredAnalyses.Select(Stored).ToArray();
            return new TextToken(form, form, null, word.ProjectStanding == ProjectStanding.Approved ? "approved" :
                word.ProjectStanding == ProjectStanding.Rejected ? "disapproved" :
                stored.Length > 0 ? "unapproved" : "unanalysed")
            {
                Analysis = stored.FirstOrDefault(), StoredAnalyses = stored,
                WordformId = Guid.Parse(Id("wordform " + form, 0)),
                StoredAnalysisId = stored.FirstOrDefault()?.StoredAnalysisId,
                WordLink = "silfw://localhost/link?tool=Analyses",
            };
        }
        var lines = new[]
        {
            new TextLines(Story, "Hadithi ya sungura",
            [
                new TextLine(1, [Token("Sungura"), Token("alikula"), Token("chakula"), new TextToken(".", null, null, null)]),
                new TextLine(2, [Token("watoto"), Token("hawajafika"), new TextToken(",", null, null, null), Token("mwalimu")]),
            ]),
            new TextLines(Letter, "Barua kwa mwalimu",
            [new TextLine(1, [Token("walikula"), Token("anapenda"), Token("kitabu"), new TextToken(".", null, null, null)])]),
        };
        var words = assessment.Words.Select(word =>
        {
            var stored = word.StoredAnalyses.Select(Stored).ToArray();
            var occurrences = lines.SelectMany(text => text.Lines.SelectMany(line => line.Tokens
                .Where(token => token.Form == word.Word).Select(token => new WordOccurrence(text.TextId, text.Title,
                    line.Number, string.Join(" ", line.Tokens.Select(token => token.Text)), token.Status!, token.Analysis))))
                .ToArray();
            return new TextWord(word.Word, Id("wordform " + word.Word, 0), occurrences,
                stored.Where(analysis => analysis.StoredAnalysisOpinion == ReadingGrade.Approved).ToArray(),
                stored.Where(analysis => analysis.StoredAnalysisOpinion == ReadingGrade.Disapproved).ToArray(),
                stored.Count(analysis => analysis.StoredAnalysisOpinion == ReadingGrade.Candidate)) { Analyses = stored };
        }).ToArray();
        return new TextWordsResponse(words, lines, HasBaseline: true, words.Sum(word => word.Occurrences.Count));
    }

    internal static AssessCommandResponse Assessment()
    {
        AssessmentWordResult Word(string word, string outcome, string[] grades, int elapsed, params ParseAnalysis[] analyses) =>
            new(word, outcome, outcome is "capped", outcome == "capped" ? "Stopped at a search limit" : "Search completed", elapsed, null)
            {
                Morphology = new ParseWordEvidence("v1", 0, word, elapsed, outcome == "capped", false, false, analyses, []),
                Readings = [.. analyses.Select(_ => Resolved(word))],
                ReadingGrades = grades,
                Attempts = elapsed * 7,
                Passes = analyses.Length,
                ProjectStanding = grades.Contains("approved") ? "approved" :
                    grades.Contains("disapproved") ? "rejected" :
                    outcome == "no-analysis" ? "not-present" : "candidate",
                OccurrenceCount = 1,
                TryWordLink = "silfw://localhost/link?tool=Analyses",
                StoredAnalyses = grades.Contains("approved") ? [ApprovedInAssessment(word)] : [],
            };
        var response = new AssessCommandResponse(Capture(), new SelectionProjection([], []), ["assessment/one"], "## 9 words\n\n7 parsed, 1 built nothing, 1 stopped.")
        {
            InvocationId = "assessment/one",
            CompletionSummary = "9 words · 7 parsed · run 12:15",
            Words =
            [
                Word("Sungura", "analysed", ["approved"], 3, Reading("Sungura")),
                Word("alikula", "analysed", ["approved", "no-opinion"], 11, RawReading(new ParserReading([Piece("a-", "3SG"), Piece("li-", "PST"), Piece("ku-", "INF"), Piece("l", "eat"), Piece("-a", "FV")])), Reading("alikula")) with
                {
                    ExpectedAnalysis = AlikulaApproved(),
                    StoredAnalyses = [AlikulaApproved() with { Identity = new ApprovedMorphology([.. AlikulaApproved().Morphs.Select(morph =>
                        new ApprovedMorph(morph.AllomorphId!, morph.GrammaticalInfoId!, null, [morph.Form]))]) }],
                    Readings = [new ParserReading([Piece("a-", "3SG"), Piece("li-", "PST"), Piece("ku-", "INF"), Piece("l", "eat"),
                        Piece("-a", "FV")]), Resolved("alikula")],
                },
                Word("chakula", "analysed", ["no-opinion"], 6, Reading("chakula")) with { ProjectStanding = ProjectStanding.NotPresent },
                Word("hawajafika", "no-analysis", [], 48) with
                { ProjectStanding = ProjectStanding.Approved, StoredAnalyses = [ApprovedInAssessment("hawajafika")] },
                Word("watoto", "analysed", ["approved"], 4, Reading("watoto")),
                Word("walikula", "analysed", ["disapproved"], 12, Reading("walikula")) with
                { StoredAnalyses = [Resolved("walikula") with
                    { StoredAnalysisId = Id("walikula", 97), StoredAnalysisOpinion = ReadingGrade.Disapproved, Identity = StoredIdentity("walikula", 0) }] },
                Word("mwalimu", "capped", [], 700) with
                { StoredAnalyses = [ApprovedInAssessment("mwalimu") with { StoredAnalysisOpinion = ReadingGrade.Candidate }] },
                Word("anapenda", "analysed", ["approved"], 9, Reading("anapenda")),
                Word("kitabu", "analysed", ["approved"], 2, Reading("kitabu")),
            ],
        };
        return response with
        {
            Measurements = [new ProducedAssessmentReference("assessment/one", AssessmentKinds.ParseTime, "assessment/one")],
            Words = response.Words.Select(word => word with
            {
                Origin = new WordMeasurementOrigin("assessment/one", null, CaptureStartAt),
                StoredAnalysesAvailable = true,
                ReadingGrades = (word.Morphology?.Analyses ?? []).Select(reading =>
                    word.StoredAnalyses.FirstOrDefault(stored => stored.Identity is not null &&
                        SIL.Motif.Host.Analysis.AnalysisMorphologyMatcher.Matches(reading, stored.Identity))
                        ?.StoredAnalysisOpinion ?? ReadingGrade.NoOpinion).ToArray(),
            }).ToArray(),
        };
    }

    private static IReadOnlyList<JsonElement> StatisticsRows(AssessCommandResponse? assessment = null) =>
        (assessment ?? Assessment()).Words.Select(word => JsonSerializer.SerializeToElement(new
        {
            kind = "word", form = word.Word, attempts = word.Attempts, passes = word.Passes,
            elapsed_ns = word.ElapsedMs * 1_000_000L, capped = word.Outcome == "capped", timed_out = false,
        })).ToArray();

    /// <summary>The sample trace's document, as PanGloss wrote it.</summary>
    internal static string SampleTrace() => TraceFixture();

    private static string TraceFixture() =>
        SIL.Motif.Tests.TestFixtures.TestTraceFixture.Read(Path.Combine(AppContext.BaseDirectory, "TestFixtures", "trace-details-v3-hawajafika.json"));

    internal static BaselineToken Token() =>
        new("11111111-1111-1111-1111-111111111111", "sha256:" + new string('a', 64), "1", "2026-09-22T10:00:00Z", "sha256:" + new string('b', 64));

    private static BaselineCaptureResponse Capture() =>
        new(Token(), ProjectPath, BaselineSaveAt, FieldWorksHeldProject: false, ReusedExistingBytes: true);

    private sealed class Picker : IProjectPicker
    {
        public Task<string?> PickProjectFileAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(ProjectPath);
    }

    private sealed class Folder : IHandoffFolderPicker
    {
        public Task<string?> PickFolderAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(@"C:\Users\linguist\Documents\Motif Handoffs");
    }

    /// <summary>The tech demo notice as a first run shows it, live, so a capture never draws it disabled.</summary>
    internal static TechDemoNoticeViewModel FirstRunNotice() => new(new NoticeNotYetSeen());

    private sealed class NoticeNotYetSeen : ITechDemoNoticePreferences
    {
        public bool HasSeenTechDemoNotice => false;

        public void MarkTechDemoNoticeSeen() { }
    }

    private sealed class Launcher : IUriLauncher
    {
        public Uri? LastUri { get; private set; }

        public Task<bool> LaunchAsync(Uri uri, CancellationToken cancellationToken = default)
        {
            LastUri = uri;
            return Task.FromResult(true);
        }
    }

    private sealed class Drag : IFileDragSource
    {
        public Task<DragDropEffects> StartDragAsync(
            PointerPressedEventArgs trigger, IReadOnlyList<string> filePaths, DragDropEffects allowedEffects) =>
            Task.FromResult(allowedEffects);
    }
}
