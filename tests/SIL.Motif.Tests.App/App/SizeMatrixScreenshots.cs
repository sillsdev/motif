using System.Globalization;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using SIL.Motif.App;
using SIL.Motif.App.Controls;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.App.Walkthrough;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
[Trait("MotifTestLevel", "System")]
public sealed class SizeMatrixScreenshots
{
    private static readonly string[] AdditionalSceneNames =
    [
        "settings-open", "settings-parsing", "open-recent-projects", "try-a-word-traced-expert",
        "ai-assistants-advanced", "parsimony-enabled", "first-run-setup-first-step",
        "refused-store-delete-action", "review-pending-changes",
    ];

    /// <summary>Stable screenshot scene names accepted by the capture matrix selector.</summary>
    public static IReadOnlyList<string> SceneNames { get; } = Array.AsReadOnly(
        PageScreenshots.Views().Select(view => view.Name).Concat(AdditionalSceneNames).ToArray());

    [ScreenshotFact]
    public void WriteCaptureMatrixPlan()
    {
        var root = Environment.GetEnvironmentVariable(ScreenshotFactAttribute.FolderVariable)!;
        AvaloniaHeadlessPlatform.Invoke(() =>
        {
            var selection = MatrixCaptureSelection.Read(SceneNames);
            var sizes = MatrixSizes.Read();
            var plan = new
            {
                Scenes = SceneNames,
                Sizes = sizes,
                Points = selection.Points,
            };
            MatrixRun.WriteArtifact(Path.Combine(root, "matrix", "capture-plan.json"),
                JsonSerializer.Serialize(plan, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                }));
        });
    }

    [ScreenshotFact]
    public void CaptureScenesAtEveryMatrixSizeAndWriteAllFindings()
    {
        var root = Environment.GetEnvironmentVariable(ScreenshotFactAttribute.FolderVariable)!;
        var matrixFolder = Path.Combine(root, "matrix");
        Directory.CreateDirectory(matrixFolder);
        var selection = MatrixCaptureSelection.Read(SceneNames);
        var run = new MatrixRun(root);

        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var sizes = MatrixSizes.Read();
            var (workspace, window) = await PageScreenshots.OpenOverSampleData(
                configure: (fake, _) => fake.ProposalStandingsCompleteWith(ProposalsPageScreenshots.Standings()));
            try
            {
                foreach (var (scene, page, tab) in PageScreenshots.Views())
                {
                    if (!selection.Scenes.Contains(scene, StringComparer.Ordinal)) continue;
                    workspace.PageModel<TextsPageModel>().Tab = tab;
                    if (page == WorkspacePage.TryAWord) await PageScreenshots.TryTheSampleWord(workspace);
                    workspace.CurrentPage = page;
                    if (page == WorkspacePage.Texts && tab == TextsTab.AnalyzeTexts)
                        await AnalyzeTextsLayoutTests.SettleReaderAsync(workspace, window);
                    await CaptureScene(run, window, workspace, scene,
                        expectedPage: page, expectedTab: page == WorkspacePage.Texts ? tab : null,
                        sizes: sizes, selection: selection);
                }

                if (selection.ContainsAny("settings-open", "settings-parsing"))
                {
                    workspace.CurrentPage = WorkspacePage.Overview;
                    var settings = OpenSettings(window);
                    if (selection.Contains("settings-open"))
                        await CaptureScene(run, window, workspace, "settings-open",
                            expectedPage: WorkspacePage.Overview, expectedTab: null, sizes: sizes, selection: selection);
                    if (selection.Contains("settings-parsing"))
                    {
                        var model = Assert.IsType<SettingsViewModel>(settings.DataContext);
                        model.SelectGroup(SettingsGroup.Parsing);
                        PageScreenshots.Settle(window);
                        await CaptureScene(run, window, workspace, "settings-parsing",
                            expectedPage: WorkspacePage.Overview, expectedTab: null, sizes: sizes, selection: selection);
                    }
                    HeadlessClick.Click(window, settings.FindControl<Button>("SettingsClose")!, "Leave Settings");
                }

                if (selection.Contains("open-recent-projects"))
                {
                    workspace.CurrentPage = WorkspacePage.Overview;
                    OpenRecentProjects(window);
                    await CaptureScene(run, window, workspace, "open-recent-projects",
                        expectedPage: WorkspacePage.Overview, expectedTab: null, sizes: sizes, selection: selection);
                    CloseRecentProjects(window);
                }

                if (selection.Contains("try-a-word-traced-expert"))
                {
                    workspace.CurrentPage = WorkspacePage.TryAWord;
                    await PageScreenshots.TryTheSampleWord(workspace);
                    workspace.Assess.Trace.IsExpert = true;
                    await CaptureScene(run, window, workspace, "try-a-word-traced-expert",
                        expectedPage: WorkspacePage.TryAWord, expectedTab: null, sizes: sizes, selection: selection);
                    workspace.Assess.Trace.IsExpert = false;
                }
            }
            finally
            {
                Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
                WindowZoomPolicy.Apply(Application.Current!, ZoomPolicy.DefaultPercent);
                window.Close();
            }

            if (selection.ContainsAny("ai-assistants-advanced", "parsimony-enabled"))
            {
                var (advancedWorkspace, advancedWindow) = await PageScreenshots.OpenOverSampleData(
                    advancedAiModeEnabled: true,
                    configure: (fake, _) => ParsimonyStateScreenshots.Configure(fake, "listed-evidence"));
                try
                {
                    if (selection.Contains("ai-assistants-advanced"))
                    {
                        advancedWorkspace.CurrentPage = WorkspacePage.AiAssistants;
                        PageScreenshots.Settle(advancedWindow);
                        var advancedMode = Assert.Single(advancedWindow.GetVisualDescendants().OfType<CheckBox>(),
                            checkBox => Equals(checkBox.Content, "Turn on Advanced AI mode"));
                        advancedMode.IsChecked = true;
                        PageScreenshots.Settle(advancedWindow);
                        Assert.True(advancedWorkspace.PageModel<AssistantConnectionsPageModel>().IsEnabled);
                        Assert.True(advancedMode.IsChecked);
                        await CaptureScene(run, advancedWindow, advancedWorkspace, "ai-assistants-advanced",
                            expectedPage: WorkspacePage.AiAssistants, expectedTab: null, sizes: sizes, selection: selection);
                    }
                    if (selection.Contains("parsimony-enabled"))
                    {
                        advancedWorkspace.CurrentPage = WorkspacePage.Parsimony;
                        await ParsimonyStateScreenshots.DriveAsync("listed-evidence",
                            advancedWorkspace.PageModel<ParsimonyPageModel>());
                        await CaptureScene(run, advancedWindow, advancedWorkspace, "parsimony-enabled",
                            expectedPage: WorkspacePage.Parsimony, expectedTab: null, sizes: sizes, selection: selection);
                    }
                }
                finally
                {
                    Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
                    WindowZoomPolicy.Apply(Application.Current!, ZoomPolicy.DefaultPercent);
                    advancedWindow.Close();
                }
            }

            if (selection.Contains("first-run-setup-first-step"))
            {
                var (setupWorkspace, setupWindow) = await PageScreenshots.OpenOverSampleData(
                    parse: false, leaveSetupOpen: true);
                try
                {
                    setupWorkspace.CurrentPage = WorkspacePage.Overview;
                    Assert.NotNull(setupWorkspace.Context.Setup);
                    await CaptureScene(run, setupWindow, setupWorkspace, "first-run-setup-first-step",
                        expectedPage: WorkspacePage.Overview, expectedTab: null, sizes: sizes, selection: selection);
                }
                finally { setupWindow.Close(); }
            }

            if (selection.Contains("refused-store-delete-action"))
            {
                var (refusedWorkspace, refusedWindow) = await PageScreenshots.OpenOverSampleData(
                    parse: false, leaveSetupOpen: true,
                    configure: (fake, _) => fake.CurrentBaselineRefusesWith(SampleStoreRefusal()));
                try
                {
                    refusedWorkspace.CurrentPage = WorkspacePage.Overview;
                    Assert.True(refusedWorkspace.HasRefreshRefusal);
                    var refusal = Assert.Single(refusedWindow.GetLogicalDescendants().OfType<RefusalBlock>(),
                        block => AutomationProperties.GetName(block) == "Refresh refusal");
                    Assert.NotNull(refusal.StoreDeletionCommand);
                    var delete = FindStoreDeletionButton(refusal.GetLogicalDescendants().OfType<Button>());
                    Assert.True(delete.IsEffectivelyVisible);
                    Assert.True(delete.IsEffectivelyEnabled);
                    await CaptureScene(run, refusedWindow, refusedWorkspace, "refused-store-delete-action",
                        expectedPage: WorkspacePage.Overview, expectedTab: null, sizes: sizes, selection: selection);
                }
                finally { refusedWindow.Close(); }
            }

            if (selection.Contains("review-pending-changes"))
            {
                var reviewFake = new FakeCommandClient();
                reviewFake.PendingChangesIs(ReviewScreenshots.Blocked());
                var (reviewWorkspace, reviewWindow) = await ReviewScreenshots.OpenAsync(reviewFake);
                try
                {
                    reviewWorkspace.CurrentPage = WorkspacePage.Review;
                    PageScreenshots.Settle(reviewWindow);
                    Assert.NotEmpty(reviewWorkspace.PageModel<ReviewPageModel>().ReviewEntries);
                    await CaptureScene(run, reviewWindow, reviewWorkspace, "review-pending-changes",
                        expectedPage: WorkspacePage.Review, expectedTab: null, sizes: sizes, selection: selection);
                }
                finally { reviewWindow.Close(); }
            }
        }, TimeSpan.FromMinutes(30));

        run.WriteArtifacts();
        run.AssertComplete(selection);
        ExpectedMatrixFindings.AssertNoUnexpected(run.Findings, requireAllExpected: true, selection);
    }

    /// <summary>Saves one selected scene across its matrix points after checking its explicit page and tab route.</summary>
    internal static async Task CaptureScene(MatrixRun run, MainWindow window, WorkspaceShellViewModel workspace,
        string scene, WorkspacePage expectedPage, TextsTab? expectedTab,
        IReadOnlyList<MatrixSize> sizes, MatrixCaptureSelection selection)
    {
        foreach (var point in selection.Points)
        {
            var size = sizes.Single(item => item.Name == point.Size);
            Application.Current!.RequestedThemeVariant = point.Theme == "dark" ? ThemeVariant.Dark : ThemeVariant.Light;
            ApplySize(window, size, point.Zoom);
            PageScreenshots.Settle(window);

            var context = new LayoutFindingContext(scene, size.Name, point.Zoom, point.Theme);
            var findings = ReachabilityAssertions.Inspect(window, context);
            var file = $"{scene}-{size.Name}-{point.Zoom}-{point.Theme}.png";
            MatrixCaptureState.SaveFrame(window, workspace, expectedPage, expectedTab,
                Path.Combine(run.MatrixFolder, file));
            run.AddCapture(file, scene, size, point.Zoom, point.Theme, findings);
            run.WriteArtifacts();
        }

        await Task.Yield();
    }

    private static SettingsPage OpenSettings(MainWindow window) => SettingsTestAccess.Open(window);

    private static void OpenRecentProjects(MainWindow window)
    {
        var projectMenu = Assert.IsType<Button>(window.FindControl<Button>("ProjectMenuButton"));
        var flyout = Assert.IsType<Flyout>(projectMenu.Flyout);
        flyout.ShowAt(projectMenu);
        PageScreenshots.Settle(window);
        var content = Assert.IsAssignableFrom<Panel>(flyout.Content);
        var openRecent = Assert.Single(content.Children.OfType<Button>(), button =>
            AutomationProperties.GetName(button) == "Open a recent project");
        Assert.NotNull(openRecent.Flyout);
        openRecent.Flyout.ShowAt(openRecent);
        PageScreenshots.Settle(window);
        Assert.NotEmpty(window.RecentProjectItems);
    }

    private static void CloseRecentProjects(MainWindow window)
    {
        var projectMenu = window.FindControl<Button>("ProjectMenuButton")!;
        var flyout = (Flyout)projectMenu.Flyout!;
        var content = Assert.IsAssignableFrom<Panel>(flyout.Content);
        var openRecent = content.Children.OfType<Button>().Single(button =>
            AutomationProperties.GetName(button) == "Open a recent project");
        openRecent.Flyout?.Hide();
        flyout.Hide();
        PageScreenshots.Settle(window);
    }

    private static void CloseFlyout(Button button)
    {
        button.Flyout?.Hide();
        if (button.ContextFlyout is { IsOpen: true } contextFlyout) contextFlyout.Hide();
    }

    private static Refusal SampleStoreRefusal() => new(RefusalCodes.StoreOtherVersion, FailureReason.Refused,
        "The Motif store was created by another version.", new Dictionary<string, string>
        {
            ["fwDataPath"] = PageScreenshots.SampleProjectPath,
            [RefusalFactNames.StorePath] = Path.ChangeExtension(PageScreenshots.SampleProjectPath, ".motif.db"),
        });

    /// <summary>Finds the refused-store action by its stable control name.</summary>
    internal static Button FindStoreDeletionButton(IEnumerable<Button> buttons) =>
        Assert.Single(buttons, button => button.Name == "DeleteStoreButton");

    internal static void ApplySize(MainWindow window, MatrixSize size, int zoom)
    {
        SettingsTestAccess.Model(window).ZoomPercent = zoom;
        window.Width = size.Width;
        window.Height = size.Height;
    }

}

internal sealed record MatrixSize(string Name, double Width, double Height);

internal sealed record MatrixSelectionPoint(string Size, int Zoom, string Theme)
{
    /// <summary>150% app zoom stands in for display scaling in a headless capture.</summary>
    internal bool UsesAppZoomAsDisplayScale => Zoom == 150;
}

internal static class MatrixSizes
{
    internal static IReadOnlyList<string> Names { get; } = ["thin", "half", "full", "short"];

    internal static IReadOnlyList<MatrixSize> Read()
    {
        var application = Application.Current ?? throw new InvalidOperationException("The App is not initialized.");
        var minimumWidth = ResourceSize(application, "Component.Shell.MinWidth");
        var minimumHeight = ResourceSize(application, "Component.Shell.MinHeight");
        return
        [
            new("thin", minimumWidth, 900),
            new("half", 960, 1040),
            new("full", 1920, 1080),
            new("short", 1600, Math.Max(minimumHeight, 600)),
        ];
    }

    private static double ResourceSize(Application application, string key) =>
        Convert.ToDouble(application.FindResource(key), CultureInfo.InvariantCulture);
}

internal sealed record MatrixCaptureSelection(IReadOnlySet<string> Scenes,
    IReadOnlyList<MatrixSelectionPoint> Points, IReadOnlySet<string>? Checks = null)
{
    internal bool Contains(string scene) => Scenes.Contains(scene);
    internal bool ContainsAny(params string[] scenes) => scenes.Any(Scenes.Contains);
    internal bool Selects(ExpectedLayoutFinding entry, ExpectedFindingLocation location) =>
        (Checks is null || Checks.Contains(entry.Check)) && Scenes.Contains(entry.Scene) && Points.Contains(new MatrixSelectionPoint(location.Size, location.Zoom, location.Theme));

    internal static MatrixCaptureSelection Read(IReadOnlyList<string> availableScenes)
    {
        var matrixMode = Environment.GetEnvironmentVariable("MOTIF_CAPTURE_MATRIX");
        var sceneValue = Environment.GetEnvironmentVariable("MOTIF_CAPTURE_SCENES");
        var sizeValue = Environment.GetEnvironmentVariable("MOTIF_CAPTURE_SIZES");
        var zoomValue = Environment.GetEnvironmentVariable("MOTIF_CAPTURE_ZOOMS");
        var themeValue = Environment.GetEnvironmentVariable("MOTIF_CAPTURE_THEMES");
        var isAll = string.Equals(matrixMode?.Trim(), "all", StringComparison.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(matrixMode) && !isAll)
            throw new InvalidOperationException("MOTIF_CAPTURE_MATRIX must be unset or 'all'.");

        if (isAll)
            return new MatrixCaptureSelection(availableScenes.ToHashSet(StringComparer.Ordinal),
                Cartesian(MatrixSizes.Names, [100, 150], ["light", "dark"]));

        var scenes = ParseList("MOTIF_CAPTURE_SCENES", sceneValue, availableScenes);
        var hasCustomSelection = new[] { sceneValue, sizeValue, zoomValue, themeValue }
            .Any(value => !string.IsNullOrWhiteSpace(value));
        if (!hasCustomSelection)
            return new MatrixCaptureSelection(scenes.ToHashSet(StringComparer.Ordinal),
            [
                new("thin", 100, "light"), new("half", 100, "light"),
                new("full", 100, "light"), new("short", 100, "light"),
                new("half", 150, "light"), new("thin", 100, "dark"), new("full", 100, "dark"),
            ]);

        var sizes = ParseList("MOTIF_CAPTURE_SIZES", sizeValue, MatrixSizes.Names);
        var zooms = ParseList("MOTIF_CAPTURE_ZOOMS", zoomValue, ["100", "150"], ["100"])
            .Select(value => int.Parse(value, CultureInfo.InvariantCulture)).ToArray();
        var themes = ParseList("MOTIF_CAPTURE_THEMES", themeValue, ["light", "dark"], ["light"]);
        return new MatrixCaptureSelection(scenes.ToHashSet(StringComparer.Ordinal),
            Cartesian(sizes, zooms, themes));
    }

    private static IReadOnlyList<string> ParseList(string variable, string? value,
        IReadOnlyList<string> allowed, IReadOnlyList<string>? defaults = null)
    {
        if (string.IsNullOrWhiteSpace(value)) return defaults ?? allowed;
        var selected = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (selected.Length == 0 || selected.Distinct(StringComparer.Ordinal).Count() != selected.Length)
            throw new InvalidOperationException($"{variable} must be a comma-separated list without duplicates.");
        var unknown = selected.Except(allowed, StringComparer.Ordinal).ToArray();
        if (unknown.Length > 0)
            throw new InvalidOperationException(
                $"{variable} contains unknown value(s): {string.Join(", ", unknown)}. " +
                $"Allowed values: {string.Join(", ", allowed)}.");
        return selected;
    }

    private static IReadOnlyList<MatrixSelectionPoint> Cartesian(
        IReadOnlyList<string> sizes, IReadOnlyList<int> zooms, IReadOnlyList<string> themes) =>
        sizes.SelectMany(size => zooms.SelectMany(zoom => themes.Select(theme =>
            new MatrixSelectionPoint(size, zoom, theme)))).ToArray();
}

internal sealed record ExpectedFindingLocation(string Size, int Zoom, string Theme);

/// <summary>
/// One real product defect the layout checks still report. The list is a work list, not a baseline: every entry
/// names the defect and the lead who owns it, every run must see every selected entry recur, and an entry is
/// deleted when its defect is fixed.
/// </summary>
/// <param name="Item">The work item that owns the fix. The id is transient: it goes when the entry does.</param>
/// <param name="Defect">The plain name of the defect, as a screenshot shows it.</param>
/// <param name="Owner">The lead who owns the fix: "UF" or "LD".</param>
internal sealed record ExpectedLayoutFinding(
    string Scene,
    string Check,
    ControlMatch Control,
    ControlMatch? RelatedControl,
    IReadOnlyList<ExpectedFindingLocation> Locations,
    string Reason,
    string ScreenshotPath,
    string Item,
    string Defect,
    string Owner)
{
    internal bool Matches(LayoutFinding finding) =>
        finding.Scene == Scene && finding.Check == Check &&
        Control.Matches(finding.Control) && ControlMatch.Matches(RelatedControl, finding.RelatedControl) &&
        Locations.Contains(new ExpectedFindingLocation(finding.Size, finding.Zoom, finding.Theme));

    internal bool Matches(LayoutFinding finding, ExpectedFindingLocation location) =>
        Locations.Contains(location) && finding.Size == location.Size && finding.Zoom == location.Zoom &&
        finding.Theme == location.Theme && Matches(finding);

    internal string Describe() => RelatedControl is null ? Control.ToString() : $"{Control} with {RelatedControl}";
}

/// <summary>
/// The control one side of an expected finding names. A <paramref name="Name"/> is the control's Name or
/// AutomationId and decides the match alone: classes are ignored when one is given. A control that cannot carry an
/// id, such as the AccessText a Button makes for its label, is named by its nearest named
/// <paramref name="Ancestor"/>. <paramref name="Classes"/> is the last resort for a control with neither.
/// </summary>
internal sealed record ControlMatch(string Type, string? Name = null, string? Ancestor = null, string? Classes = null)
{
    internal bool Matches(ControlDescription? actual)
    {
        if (actual is null || actual.Type != Type) return false;
        if (Name is not null) return actual.Name == Name || actual.AutomationId == Name;
        if (Ancestor is not null) return actual.NamedAncestor == Ancestor;
        return ClassSet(actual.Classes) == ClassSet(Classes);
    }

    internal static bool Matches(ControlMatch? expected, ControlDescription? actual) =>
        expected is null ? actual is null : expected.Matches(actual);

    public override string ToString() => Name is not null ? $"{Type} '{Name}'"
        : Ancestor is not null ? $"{Type} inside '{Ancestor}'" : $"{Type}.{Classes}";

    /// <summary>Style classes minus pseudo-classes such as :disabled, so state never changes a match.</summary>
    private static string ClassSet(string? classes) => string.Join('.',
        (classes ?? "").Split(['.', ' '], StringSplitOptions.RemoveEmptyEntries)
            .Where(token => !token.StartsWith(':')).Order(StringComparer.Ordinal));
}

internal static class ExpectedMatrixFindings
{
    internal static IReadOnlyList<ExpectedLayoutFinding> Entries { get; } =
    [
        new("1-overview", "text-own-box-overflow", new("CopyableTextBlock", Name: "motif-overview-selection-label"), null, [new("half", 150, "light")], "The overview subtitle is wider than its text box at this zoom.", "matrix/1-overview-half-150-light.png",
            "V09", "overview subtitle wider than its text box", "LD"),
        new("2c-texts-lists", "page-horizontal-scroll", new("WordListSlot", Name: "motif-lists-words-slot"), null, [new("thin", 100, "light"), new("thin", 150, "light"), new("half", 150, "light"), new("thin", 100, "dark")], "The word list keeps columns wider than the page at these sizes.", "matrix/2c-texts-lists-thin-100-light.png",
            "V05", "result table wider than the page", "LD"),
        new("open-recent-projects", "popup-outside-window", new("FlyoutPresenter", Name: "motif-project-menu-flyout"), null, [new("thin", 100, "light"), new("half", 100, "light"), new("full", 100, "light"), new("short", 100, "light"), new("half", 150, "light"), new("thin", 100, "dark"), new("full", 100, "dark")], "The recent-project menu overlaps the top bar at these sizes.", "matrix/open-recent-projects-thin-100-light.png",
            "V25", "recent-projects menu overlaps the top bar", "UF"),
        new("open-recent-projects", "text-own-box-overflow", new("CopyableTextBlock", Name: "motif-overview-selection-label"), null, [new("half", 150, "light")], "The overview subtitle is wider than its text box behind the open menu.", "matrix/open-recent-projects-half-150-light.png",
            "V09", "overview subtitle wider than its text box", "LD"),
        new("ai-assistants-advanced", "text-own-box-overflow", new("CopyableTextBlock", Name: "motif-assistants-empty-text"), null, [new("half", 150, "light")], "The assistant setup instructions are wider than their text box at this zoom.", "matrix/ai-assistants-advanced-half-150-light.png",
            "V15", "assistant setup text wider than its text box", "LD"),
        new("refused-store-delete-action", "text-own-box-overflow", new("CopyableTextBlock", Name: "motif-overview-selection-label"), null, [new("half", 150, "light")], "The refusal notice leaves an overview subtitle wider than its text box.", "matrix/refused-store-delete-action-half-150-light.png",
            "V09", "overview subtitle wider than its text box", "LD"),
        new("review-pending-changes", "page-horizontal-scroll", new("WordListSlot", Name: "motif-review-words-slot"), null, [new("thin", 100, "light"), new("half", 100, "light"), new("half", 150, "light"), new("thin", 100, "dark")], "The pending-change list keeps columns wider than the page at these sizes.", "matrix/review-pending-changes-thin-100-light.png",
            "V03", "review panel text and actions overlap", "LD"),
        new("review-pending-changes", "page-horizontal-scroll", new("Grid", Name: "motif-review-words-grid"), null, [new("half", 150, "light")], "The pending-change list extends beyond the page body at this zoom.", "matrix/review-pending-changes-half-150-light.png",
            "V03", "review panel text and actions overlap", "LD"),
        new("review-pending-changes", "unreachable-control", new("Button", Name: "motif-word-open-in-text"), null, [new("thin", 100, "light"), new("half", 100, "light"), new("full", 100, "light"), new("short", 100, "light"), new("half", 150, "light"), new("thin", 100, "dark"), new("full", 100, "dark")], "Pending change actions extend beyond the visible review area.", "matrix/review-pending-changes-thin-100-light.png",
            "V35", "word row Next action off-screen", "UF"),
        new("review-pending-changes", "unreachable-control", new("Button", Name: "motif-word-try-a-word"), null, [new("thin", 100, "light"), new("half", 100, "light"), new("full", 100, "light"), new("short", 100, "light"), new("half", 150, "light"), new("thin", 100, "dark"), new("full", 100, "dark")], "Pending change actions extend beyond the visible review area.", "matrix/review-pending-changes-thin-100-light.png",
            "V35", "word row Next action off-screen", "UF"),
        new("review-pending-changes", "unreachable-control", new("Button", Name: "motif-word-undo-change"), null, [new("thin", 100, "light"), new("half", 100, "light"), new("half", 150, "light"), new("thin", 100, "dark")], "Pending change actions extend beyond the visible review area.", "matrix/review-pending-changes-thin-100-light.png",
            "V03", "review panel text and actions overlap", "LD"),        new("2a-texts-matrix", "unreachable-control", new("WordRow", Name: "motif-word-row-matrix-alikula-row"), null, [new("thin", 100, "light"), new("thin", 150, "light"), new("half", 150, "light")], "Word rows with always-visible actions are wider than the results viewport and its scroll route.", "matrix/2a-texts-matrix-thin-100-light.png",
            "H01", "matrix word row beyond its scroll route", "Analyze texts"),
        new("2a-texts-matrix", "unreachable-control", new("WordRow", Name: "motif-word-row-matrix-anapenda-row"), null, [new("thin", 100, "light")], "Word rows with always-visible actions are wider than the results viewport and its scroll route.", "matrix/2a-texts-matrix-thin-100-light.png",
            "H01", "matrix word row beyond its scroll route", "Analyze texts"),
        new("2a-texts-matrix", "unreachable-control", new("RepeatButton", Name: "PART_PageDownButton"), null, [new("thin", 150, "light"), new("half", 150, "light")], "The results list's page-down region sits outside the page body at this zoom.", "matrix/2a-texts-matrix-thin-150-light.png",
            "H01", "matrix scroll bar outside the page body", "Analyze texts"),
        new("2c-texts-lists", "unreachable-control", new("WordRow", Name: "motif-word-row-lists-hawajafika-row"), null, [new("thin", 100, "light"), new("thin", 150, "light"), new("half", 150, "light")], "Word rows with always-visible actions are wider than the list viewport and its scroll route.", "matrix/2c-texts-lists-thin-100-light.png",
            "H02", "list word row beyond its scroll route", "Analyze texts"),
        new("2c-texts-lists", "unreachable-control", new("Button", Name: "motif-handoff-list"), null, [new("thin", 150, "light")], "The list's AI Handoff action sits beyond the narrow page at this zoom.", "matrix/2c-texts-lists-thin-150-light.png",
            "H02", "list handoff action beyond the page", "Analyze texts"),
        new("3-try-a-word", "unreachable-control", new("ListBoxItem", Classes: ""), null, [new("thin", 100, "light"), new("thin", 150, "light"), new("short", 100, "light"), new("half", 150, "light")], "Recorded attempts sit below the compact result without a scroll route.", "matrix/3-try-a-word-thin-100-light.png",
            "H03", "attempt row without a scroll route", "Try a Word"),
        new("3-try-a-word", "unreachable-control", new("StackPanel", Classes: "morphemePanelPart.morphemePanelPartLast.hoverReveal.morphemePanelInspectable"), null, [new("thin", 100, "light"), new("thin", 150, "light"), new("short", 100, "light"), new("half", 150, "light")], "An attempt's inspectable morpheme sits below the compact result without a scroll route.", "matrix/3-try-a-word-thin-100-light.png",
            "H03", "attempt morpheme without a scroll route", "Try a Word"),
        new("4-timing", "unreachable-control", new("WordRow", Name: "motif-word-row-timing-rule-words-mwalimu-row"), null, [new("thin", 100, "light"), new("thin", 150, "light"), new("half", 150, "light")], "The costliest rule's word rows sit beyond the Timing page's scroll route.", "matrix/4-timing-thin-100-light.png",
            "H04", "timing rule word row beyond its scroll route", "Frame"),
        new("7-ai-handoff", "unreachable-control", new("ToggleButton", Name: "ExpanderHeader"), null, [new("thin", 100, "light")], "The handoff's expander header sits beyond the page's scroll route in a narrow window.", "matrix/7-ai-handoff-thin-100-light.png",
            "H05", "handoff expander beyond its scroll route", "AI pages"),
        new("7-ai-handoff", "unreachable-control", new("Button", Classes: ""), null, [new("thin", 150, "light")], "The handoff file copy actions sit beyond the page's scroll route at this zoom.", "matrix/7-ai-handoff-thin-150-light.png",
            "H05", "handoff copy actions beyond their scroll route", "AI pages"),
        new("10-settings", "unreachable-control", new("RepeatButton", Name: "PART_PageUpButton"), null, [new("thin", 100, "light"), new("thin", 150, "light"), new("short", 100, "light"), new("half", 150, "light")], "The Settings list's page-up region has no height when the list is at its top.", "matrix/10-settings-thin-100-light.png",
            "H06", "settings scroll bar page-up region without height", "Frame / Settings"),

    ];

    internal static void AssertNoUnexpected(IEnumerable<LayoutFinding> source, bool requireAllExpected,
        MatrixCaptureSelection? selection = null)
    {
        var findings = source.ToArray();
        var unexpected = findings.Where(finding => !Matches(finding)).ToArray();
        var stale = requireAllExpected
            ? Entries.SelectMany(entry => entry.Locations
                .Where(location => selection is null || selection.Selects(entry, location))
                .Where(location => findings.All(finding => !entry.Matches(finding, location)))
                .Select(location => (Entry: entry, Location: location))).ToArray()
            : [];
        if (unexpected.Length == 0 && stale.Length == 0) return;

        var message = new StringBuilder();
        foreach (var finding in unexpected)
            message.AppendLine($"Unexpected {finding.Check} in {finding.Scene}/{finding.Size}/{finding.Zoom}/{finding.Theme}: " +
                $"{finding.Control.Type} name='{finding.Control.Name}' id='{finding.Control.AutomationId}' " +
                $"classes='{finding.Control.Classes}' bounds={finding.Bounds}; {finding.Detail}");
        foreach (var (entry, location) in stale)
            message.AppendLine($"Expected finding did not recur: {entry.Scene}/{location.Size}/" +
                $"{location.Zoom}/{location.Theme} {entry.Check} {entry.Describe()}; " +
                $"{entry.Item} {entry.Defect} (owner {entry.Owner}); {entry.Reason}; capture: {entry.ScreenshotPath}");
        message.AppendLine("Every finding of the run is in matrix/findings.json beside the captures; " +
            "tools/Capture-Screens.ps1 also builds the contact sheet (index.html) from them.");
        Xunit.Assert.Fail(message.ToString());
    }

    internal static bool Matches(LayoutFinding finding) => Entries.Any(entry => entry.Matches(finding));
}

internal sealed class MatrixRun(string root)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
    private readonly List<MatrixIndexEntry> _index = [];
    private readonly List<MatrixFindingEntry> _findingEntries = [];

    internal string Root { get; } = root;
    internal string MatrixFolder { get; } = Path.Combine(root, "matrix");
    internal List<LayoutFinding> Findings { get; } = [];

    internal void AddCapture(string file, string scene, MatrixSize size, int zoom, string theme,
        IReadOnlyList<LayoutFinding> findings)
    {
        Findings.AddRange(findings);
        _index.Add(new MatrixIndexEntry(file, scene, size.Name, size.Width, size.Height, zoom, theme, findings.Count));
        _findingEntries.AddRange(findings.Select(finding => new MatrixFindingEntry(
            file, finding.Scene, finding.Size, finding.Zoom, finding.Theme, finding.Check, finding.Control,
            finding.Bounds, finding.Detail, ExpectedMatrixFindings.Matches(finding), finding.Identity,
            finding.RelatedControl, finding.Count)));
    }

    internal void AssertComplete(MatrixCaptureSelection selection)
    {
        Assert.Equal(selection.Scenes.Count * selection.Points.Count, _index.Count);
        foreach (var scene in selection.Scenes)
            foreach (var point in selection.Points)
                Assert.Single(_index, entry => entry.Scene == scene && entry.Size == point.Size &&
                    entry.Zoom == point.Zoom && entry.Theme == point.Theme);
    }

    internal void WriteArtifacts()
    {
        Directory.CreateDirectory(MatrixFolder);
        WriteArtifact(Path.Combine(MatrixFolder, "findings.json"),
            JsonSerializer.Serialize(_findingEntries, JsonOptions));
        var csv = new StringBuilder("file,scene,size,width,height,zoom,theme,findings\n");
        foreach (var entry in _index)
            csv.Append(Csv(entry.File)).Append(',').Append(Csv(entry.Scene)).Append(',')
                .Append(Csv(entry.Size)).Append(',')
                .Append(entry.Width.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(entry.Height.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(entry.Zoom.ToString(CultureInfo.InvariantCulture)).Append(',').Append(Csv(entry.Theme)).Append(',')
                .Append(entry.Findings.ToString(CultureInfo.InvariantCulture)).Append('\n');
        WriteArtifact(Path.Combine(MatrixFolder, "index.csv"), csv.ToString());
    }

    internal static void WriteArtifact(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, content, new UTF8Encoding(false));
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static string Csv(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";

    private sealed record MatrixIndexEntry(
        string File, string Scene, string Size, double Width, double Height, int Zoom, string Theme, int Findings);

    private sealed record MatrixFindingEntry(
        string File, string Scene, string Size, int Zoom, string Theme, string Check, ControlDescription Control,
        LayoutBounds Bounds, string Detail, bool Expected, string Identity, ControlDescription? RelatedControl,
        int Count);
}
