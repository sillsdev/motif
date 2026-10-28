using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using SIL.Motif.App;
using SIL.Motif.App.Controls;
using SIL.Motif.App.Controls.WordPresentation;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.App.ControlContracts;
using SIL.Motif.Tests.App.Walkthrough;
using Xunit;
using WordRow = SIL.Motif.App.Controls.WordPresentation.WordRow;
using WordRowHeader = SIL.Motif.App.Controls.WordPresentation.WordRowHeader;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
[Trait("MotifTestLevel", "System")]
public sealed class SharedWordListResponsiveTests
{
    private static readonly ResponsiveScene[] Scenes =
    [
        new("2a-texts-matrix", TooltipScene.Matrix, WordListSchemas.All, true, true),
        new("2b-texts-analyze", TooltipScene.AnalyzeWordList, WordListSchemas.WordList, true, false),
        new("2c-texts-lists", TooltipScene.Lists, WordListSchemas.All, true, true),
        new("4-timing", TooltipScene.Timing, WordListSchemas.Timing, false, false),
    ];

    private static readonly ResponsiveScene DifferenceScene =
        new("2d-texts-what-changed", TooltipScene.WhatChanged, WordListSchemas.WhatChanged, true, true);

    private static readonly ResponsiveSize[] Sizes =
    [
        // Low resolution (640x600), zoomed in (640x600 at 150%) and the taller half-screen window (640x900).
        new(600, 100), new(600, 150), new(900, 100),
    ];

    [Fact]
    public void SharedWordListsKeepTheirTracksInsideTheActualViewport()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var failures = new List<string>();
            await VisitWordListScenes(async (scene, _, workspace, window) =>
            {
                foreach (var theme in Themes)
                foreach (var size in Sizes)
                {
                    ApplySize(window, size, theme);
                    var slots = window.GetVisualDescendants().OfType<WordListSlot>()
                        .Where(slot => slot.IsEffectivelyVisible && slot.CurrentLayout is not null).ToArray();
                    var state = $"{scene.Name}, 640x{size.Height} at {size.Zoom}% {theme.Name}";
                    Record(slots.Length > 0, state,
                        "no visible production WordListSlot was measured", failures);

                    for (var index = 0; index < slots.Length; index++)
                    {
                        InspectSlotLayout(slots[index], scene, index, size, theme.Name, window, failures);
                    }
                }
            });

            Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
        }, TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void MatrixActionsStayVisibleAndReceivePointerBeforeFocus() => CheckActions(Scenes[0]);

    [Fact]
    public void AnalyzeWordListActionsStayVisibleAndReceivePointerBeforeFocus() => CheckActions(Scenes[1]);

    [Fact]
    public void ListActionsStayVisibleAndReceivePointerBeforeFocus() => CheckActions(Scenes[2]);

    [Fact]
    public void TimingActionsStayVisibleAndReceivePointerBeforeFocus() => CheckActions(Scenes[3]);

    [Fact]
    public void DifferenceActionsStayVisibleAndReceivePointerBeforeFocus() => CheckActions(DifferenceScene);

    [Fact]
    public void MatrixCellLabelsNeverBreakInsideAWord()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var failures = new List<string>();
            var measured = 0;
            await VisitWordListScenes(async (scene, _, _, window) =>
            {
                await Task.CompletedTask;
                foreach (var size in Sizes)
                {
                    ApplySize(window, size, Themes[0]);
                    var labels = window.GetVisualDescendants().OfType<TextBlock>()
                        .Where(text => text.Classes.Contains("matrixCellLabel") && text.IsEffectivelyVisible
                            && !string.IsNullOrEmpty(text.Text) && text.Bounds.Width > 0).ToArray();
                    measured += labels.Length;
                    foreach (var label in labels)
                    {
                        var broken = MidWordBreaks(label);
                        if (broken is not null)
                            failures.Add($"640x{size.Height} at {size.Zoom}%: '{label.Text}' breaks inside '{broken}' in a {label.Bounds.Width:0.#} px cell label.");
                    }
                }
            }, [Scenes[0]]);

            Assert.True(measured > 0, "No visible Matrix cell label was measured.");
            Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
        }, TimeSpan.FromMinutes(3));
    }

    // A line break with no whitespace at the join splits a word.
    private static string? MidWordBreaks(TextBlock label)
    {
        var text = label.Text!;
        var lines = label.TextLayout.TextLines;
        for (var index = 1; index < lines.Count; index++)
        {
            var start = lines[index].FirstTextSourceIndex;
            if (start <= 0 || start >= text.Length) continue;
            if (!char.IsWhiteSpace(text[start - 1]) && !char.IsWhiteSpace(text[start]))
            {
                var from = start;
                while (from > 0 && !char.IsWhiteSpace(text[from - 1])) from--;
                var to = start;
                while (to < text.Length && !char.IsWhiteSpace(text[to])) to++;
                return text[from..to];
            }
        }
        return null;
    }

    [Fact]
    public void ReviewChangesKeepTheirDenseTableBodyVisibleAcrossZoomAndThemes()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = new FakeCommandClient();
            fake.PendingChangesIs(ReviewScreenshots.Blocked());
            var (workspace, window) = await ReviewScreenshots.OpenAsync(fake);
            try
            {
                workspace.CurrentPage = WorkspacePage.Review;
                foreach (var theme in Themes)
                foreach (var size in Sizes)
                {
                    ApplySize(window, size, theme);
                    AssertReviewTableVisible(window, $"6-review, 640x{size.Height} at {size.Zoom}% {theme.Name}");
                }
            }
            finally
            {
                Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
                WindowZoomPolicy.Apply(Application.Current!, ZoomPolicy.DefaultPercent);
                window.Close();
            }
        }, TimeSpan.FromMinutes(3));
    }

    [ScreenshotFact]
    public void CapturesWordListsAt640By600And900AcrossZoomAndThemes()
    {
        var root = Environment.GetEnvironmentVariable(ScreenshotFactAttribute.FolderVariable)!;
        Directory.CreateDirectory(root);
        var states = new List<string>();

        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            await VisitWordListScenes(async (scene, _, _, window) =>
                await CaptureScene(scene.Name, window, root, states));

            var fake = new FakeCommandClient();
            fake.PendingChangesIs(ReviewScreenshots.Blocked());
            var (workspace, window) = await ReviewScreenshots.OpenAsync(fake);
            try
            {
                workspace.CurrentPage = WorkspacePage.Review;
                PageScreenshots.Settle(window);
                await CaptureScene("6-review", window, root, states, AssertReviewTableVisible);
            }
            finally
            {
                Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
                WindowZoomPolicy.Apply(Application.Current!, ZoomPolicy.DefaultPercent);
                window.Close();
            }
        }, TimeSpan.FromMinutes(5));

        File.WriteAllLines(Path.Combine(root, "shared-word-list-states.txt"), states);
        Assert.Equal((Scenes.Length + 1) * Sizes.Length * Themes.Length, states.Count);
    }

    private static async Task VisitWordListScenes(
        Func<ResponsiveScene, TooltipScenes, WorkspaceShellViewModel, MainWindow, Task> visit,
        IEnumerable<ResponsiveScene>? selectedScenes = null, bool leaveAfterVisit = true)
    {
        var fixture = await TooltipScenes.Open(seedReaderToken: false);
        fixture.Width = 640;
        var workspace = fixture.Workspace;
        var window = fixture.Window;
        // These check returning use; the first-run notice is acknowledged as a returning user has.
        workspace.TechDemoNotice?.GotItCommand.Execute(null);
        try
        {
            foreach (var scene in selectedScenes ?? Scenes)
            {
                await fixture.Reach(scene.Scene);
                PageScreenshots.Settle(window);
                await visit(scene, fixture, workspace, window);
                if (leaveAfterVisit) await fixture.Leave(scene.Scene);
            }
        }
        finally
        {
            Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
            WindowZoomPolicy.Apply(Application.Current!, ZoomPolicy.DefaultPercent);
            fixture.Close();
        }
    }

    private static void CheckActions(ResponsiveScene selectedScene)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var failures = new List<string>();
            // One window serves every size; Reach returns from Try a Word.
            await VisitWordListScenes(async (scene, fixture, workspace, window) =>
            {
                foreach (var theme in Themes)
                foreach (var size in Sizes)
                {
                    var state = StateName(scene, size, theme);
                    await PrepareSize(scene, workspace, window, size, theme, state, failures);
                    CheckKeyboard(scene, window, size, theme, state, failures);

                    await ReturnToScene(scene, fixture, workspace, window);
                    await PrepareSize(scene, workspace, window, size, theme, state, failures);
                    var slot = VisibleSlot(window);
                    Record(slot is not null, state,
                        "no visible production WordListSlot was measured", failures);
                    if (slot is not null)
                        InspectRightmostAction(slot, scene, size, theme, window, workspace, failures);

                    // Leave Try a Word and clear the focus and pointer position the checks left behind.
                    await ReturnToScene(scene, fixture, workspace, window);
                }
            }, [selectedScene], leaveAfterVisit: false);
            Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
        }, TimeSpan.FromMinutes(5));
    }

    private static WordListSlot? VisibleSlot(MainWindow window) =>
        window.GetVisualDescendants().OfType<WordListSlot>()
            .FirstOrDefault(item => item.IsEffectivelyVisible && item.CurrentLayout is not null);

    private static async Task PrepareSize(ResponsiveScene scene, WorkspaceShellViewModel workspace,
        MainWindow window, ResponsiveSize size, ThemeChoice theme, string state, ICollection<string> failures)
    {
        window.MouseMove(new Point(8, 8));
        PageScreenshots.Settle(window);
        await SelectTimingRule(scene, workspace, window, state, failures);
        ApplySize(window, size, theme);
    }

    private static async Task ReturnToScene(ResponsiveScene scene, TooltipScenes fixture,
        WorkspaceShellViewModel workspace, MainWindow window)
    {
        // Escape closes anything the last check left open; Reach shows the scene's page again.
        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null);
        await fixture.Reach(scene.Scene);
        window.FindInWindowOrWorkspace<Button>("ProjectChip")?.Focus();
        PageScreenshots.Settle(window);
    }

    private static void CheckKeyboard(ResponsiveScene scene, MainWindow window, ResponsiveSize size,
        ThemeChoice theme, string state, ICollection<string> failures)
    {
        var slot = VisibleSlot(window);
        Record(slot is not null, state,
            "no visible production WordListSlot was measured for keyboard input", failures);
        if (slot is null) return;
        var target = PrepareRightmostAction(slot, scene, size, theme, window, failures);
        if (target is null) return;
        var (row, action) = target.Value;
        var outsideFocus = window.FindInWindowOrWorkspace<Button>("ProjectChip");
        Record(outsideFocus is not null && outsideFocus.Focus(), state,
            "the test could not move focus outside the word row", failures);
        Record(!row.GetVisualDescendants().OfType<Control>().Any(control => control.IsFocused) &&
               !action.IsFocused, state,
            "the row action was focused before the keyboard navigation check", failures);
        row.FocusWord();
        var startedAtRow = row.IsFocused;
        for (var tab = 0; startedAtRow && tab < 100 && !action.IsFocused; tab++)
            window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.None, null);
        Record(startedAtRow && action.IsFocused, state,
            "the rightmost next-step action cannot receive keyboard focus after scrolling", failures);
    }

    private static void InspectSlotLayout(WordListSlot slot, ResponsiveScene scene, int index, ResponsiveSize size,
        string theme, MainWindow window, ICollection<string> failures)
    {
        var state = $"{scene.Name}, slot {index + 1}, 640x{size.Height} at {size.Zoom}% {theme}";
        var layout = slot.CurrentLayout!;
        var viewportBounds = BoundsInWindow(slot, window, slot.Viewport);
        var pageHost = window.FindInWindowOrWorkspace<Panel>("PageHost");
        var pageBounds = pageHost is null ? default : BoundsInWindow(pageHost, window);
        Record(layout.AvailableWidth <= slot.Bounds.Width + 1,
            state, $"layout width {layout.AvailableWidth:0.###} exceeds viewport bounds {slot.Bounds.Width:0.###}", failures);
        Record(slot.DesiredSize.Width <= slot.Bounds.Width + 1,
            state, $"desired width {slot.DesiredSize.Width:0.###} exceeds viewport bounds {slot.Bounds.Width:0.###}", failures);
        Record(pageHost is not null && viewportBounds.Left >= pageBounds.Left - 1 &&
               viewportBounds.Right <= pageBounds.Right + 1,
            state, $"transformed slot viewport {viewportBounds} exceeds page host {pageBounds}", failures);
        var pageScroll = slot.GetVisualAncestors().OfType<ScrollViewer>().LastOrDefault(viewer =>
            viewer.IsEffectivelyVisible && !viewer.GetVisualAncestors().OfType<ScrollViewer>().Any());
        if (pageScroll is not null)
            Record(pageScroll.Extent.Width <= pageScroll.Viewport.Width + 1, state,
                $"the page scroll viewer expands horizontally to {pageScroll.Extent.Width:0.###} DIP " +
                $"for a {pageScroll.Viewport.Width:0.###} DIP viewport", failures);
        Record(layout.Stacked, state,
            $"a {layout.ContentWidth:0.###} DIP content width did not stack the row", failures);
        Record(slot.Extent.Width <= slot.Viewport.Width + 1, state,
            $"horizontal extent {slot.Extent.Width:0.###} exceeds viewport {slot.Viewport.Width:0.###}", failures);
        Record(slot.HorizontalScrollBarVisibility == ScrollBarVisibility.Disabled, state,
            "the slot still offers a horizontal scrollbar", failures);

        var expectedColumns = layout.Schema.OrderedColumns(layout.ShowsMeaning);
        Record(layout.Schema == scene.Schema, state,
            "the slot changed the scene's semantic word-list schema", failures);
        Record(layout.Schema.HasFieldWorksMorphology == scene.HasFieldWorksMorphology &&
               layout.Schema.HasPanGlossMorphology == scene.HasPanGlossMorphology,
            state, "the slot changed which producer columns carry morphology", failures);
        Record(layout.Tracks.IsEmpty, state, "a stacked layout still published column tracks", failures);

        var header = (slot.Content as Control)?.GetVisualDescendants().OfType<WordRowHeader>().FirstOrDefault();
        var row = slot.GetVisualDescendants().OfType<WordRow>().FirstOrDefault(item => item.IsEffectivelyVisible);
        Record(header is not null, state, "the shared header is absent", failures);
        Record(row is not null, state, "no realized word row is visible", failures);
        if (header is null || row is null) return;

        Record(ReferenceEquals(layout, WordListLayoutScope.GetLayout(header)), state,
            "the header did not receive the slot's resolved layout", failures);
        Record(ReferenceEquals(layout, WordListLayoutScope.GetLayout(row)), state,
            "the realized row did not receive the slot's resolved layout", failures);

        Record(!header.IsVisible || !header.GetVisualDescendants().OfType<Border>().First().IsVisible, state,
            "a stacked list still shows its column headings", failures);
        var rowLine = row.GetVisualDescendants().OfType<Grid>()
            .FirstOrDefault(grid => grid.Classes.Contains("wordPresentationShell"));
        Record(rowLine is not null && rowLine.ColumnDefinitions.Count == 2, state,
            "the stacked row did not use its word column and its wrapped remainder", failures);
        if (rowLine is not null)
            Record(rowLine.Bounds.Width <= slot.Viewport.Width + 1, state,
                $"the stacked row is {rowLine.Bounds.Width:0.###} DIP wide in a {slot.Viewport.Width:0.###} DIP slot", failures);
    }

    private static void InspectRightmostAction(WordListSlot slot, ResponsiveScene selectedScene,
        ResponsiveSize size, ThemeChoice theme, MainWindow window, WorkspaceShellViewModel workspace,
        ICollection<string> failures)
    {
        var state = StateName(selectedScene, size, theme);
        var target = PrepareRightmostAction(slot, selectedScene, size, theme, window, failures);
        if (target is null) return;
        var (row, action) = target.Value;
        Record(!row.GetVisualDescendants().OfType<Control>().Any(control => control.IsFocused) && !action.IsFocused,
            state, "the row action was focused before its pointer check", failures);

        string? pointerTrace = null;
        var pointerMatchedAction = false;
        var pointerMatchedWord = false;
        var actionId = AutomationProperties.GetAutomationId(action);
        var actionName = AutomationProperties.GetName(action) ?? "the word row action";
        var actionBounds = BoundsInWindow(action, window);
        var wordKey = row.Data?.Key;
        var expectedWord = row.Data?.Facts.Word;
        void RecordPointerSource(object? _, PointerPressedEventArgs args)
        {
            if (args.Source is not Visual source)
            {
                pointerTrace = "no visual pointer source";
                return;
            }
            var sourcePath = source.GetVisualAncestors().Prepend(source).ToArray();
            var hitAction = sourcePath.OfType<Button>().FirstOrDefault(button =>
                AutomationProperties.GetAutomationId(button) == actionId &&
                AutomationProperties.GetName(button) == actionName);
            var hitRow = sourcePath.OfType<WordRow>().FirstOrDefault();
            pointerMatchedAction = hitAction is not null;
            pointerMatchedWord = wordKey is not null && hitRow?.Data?.Key == wordKey;
            pointerTrace = string.Join(" <- ", source.GetVisualAncestors().Prepend(source).Take(8).Select(visual =>
            {
                var control = visual as Control;
                var name = control is null ? string.Empty : AutomationProperties.GetName(control) ?? string.Empty;
                return $"{visual.GetType().Name} '{control?.Name}' automation='{name}' " +
                       $"bounds={BoundsInWindow(visual, window)} opacity={EffectiveOpacity(visual):0.###}";
            })) + $"; intended action={pointerMatchedAction}; same word={pointerMatchedWord}; " +
                $"same button instance={ReferenceEquals(hitAction, action)}; expected row key={wordKey}; " +
                $"expected word={expectedWord}; pressed row key={hitRow?.Data?.Key}; " +
                $"pressed word={hitRow?.Data?.Facts.Word}; target action bounds={actionBounds}";
        }

        window.AddHandler(InputElement.PointerPressedEvent, RecordPointerSource, RoutingStrategies.Tunnel,
            handledEventsToo: true);
        string? pointerFailure = null;
        try
        {
            HeadlessClick.Click(window, action, actionName);
        }
        catch (Xunit.Sdk.XunitException exception) { pointerFailure = exception.Message; }
        finally
        {
            window.RemoveHandler(InputElement.PointerPressedEvent, RecordPointerSource);
        }
        PageScreenshots.Settle(window);
        Record(pointerMatchedAction && pointerMatchedWord, state,
            $"the pointer did not press the same word's rightmost action before focus; " +
            $"source ancestry: {pointerTrace ?? "no pointer source"}; click helper: {pointerFailure ?? "no helper failure"}", failures);
        var actionResultArrived = workspace.CurrentPage == WorkspacePage.TryAWord && expectedWord is not null &&
            workspace.PageModel<TryWordPageModel>().Trace.WordToTry == expectedWord;
        Record(actionResultArrived, state,
            $"the pointer did not open Try a Word for {expectedWord ?? "the selected word"} " +
            $"(current page {workspace.CurrentPage}, trace word " +
            $"{workspace.PageModel<TryWordPageModel>().Trace.WordToTry})", failures);
    }

    private static (WordRow Row, Button Action)? PrepareRightmostAction(WordListSlot slot,
        ResponsiveScene selectedScene, ResponsiveSize size, ThemeChoice theme, MainWindow window,
        ICollection<string> failures)
    {
        var state = StateName(selectedScene, size, theme);
        var row = slot.GetVisualDescendants().OfType<WordRow>()
            .FirstOrDefault(item => item.IsEffectivelyVisible);
        Record(row is not null, state, "no realized word row is visible", failures);
        if (row is null) return null;
        row.BringIntoView();
        PageScreenshots.Settle(window);

        if (selectedScene.Scene == TooltipScene.Timing)
        {
            var pageScroll = slot.GetVisualAncestors().OfType<ScrollViewer>()
                .FirstOrDefault(viewer => viewer.VerticalScrollBarVisibility != ScrollBarVisibility.Disabled);
            if (pageScroll is not null)
            {
                pageScroll.Offset = new Vector(pageScroll.Offset.X,
                    Math.Max(0, pageScroll.Extent.Height - pageScroll.Viewport.Height));
                PageScreenshots.Settle(window);
            }
        }

        slot.Offset = new Vector(0, slot.Offset.Y);
        PageScreenshots.Settle(window);
        Record(slot.Extent.Width <= slot.Viewport.Width + 1, state,
            $"the word list scrolls sideways: extent {slot.Extent.Width:0.###} exceeds viewport {slot.Viewport.Width:0.###}",
            failures);
        var rows = (slot.Content as Grid)?.Children.OfType<ItemsControl>().FirstOrDefault();
        if (rows is { ItemCount: > 0 })
        {
            rows.ScrollIntoView(0);
            PageScreenshots.Settle(window);
        }
        row = slot.GetVisualDescendants().OfType<WordRow>()
            .FirstOrDefault(item => item.IsEffectivelyVisible);
        Record(row is not null, state, "no realized word row is visible", failures);
        if (row is null) return null;
        row.BringIntoView();
        PageScreenshots.Settle(window);
        var actions = row.GetVisualDescendants().OfType<Button>().Where(button =>
            AutomationProperties.GetAutomationId(button) is AutomationIds.WordOpenInText or AutomationIds.WordTryAWord)
            .ToArray();
        Record(actions.Length > 0, state, "the row's next-step actions are absent", failures);
        if (actions.Length == 0) return null;

        var action = actions.MaxBy(button => BoundsInWindow(button, window).Right)!;
        action.BringIntoView();
        PageScreenshots.Settle(window);
        var actionBounds = BoundsInWindow(action, window);
        var viewportBounds = BoundsInWindow(slot, window, slot.Viewport);
        var visibleActionBounds = ClippedBoundsInWindow(action, window);
        var actionIsShown = action.IsEffectivelyVisible && EffectiveOpacity(action) > 0 &&
            visibleActionBounds.Width > 0 && visibleActionBounds.Height > 0;
        var actionHasOwnBounds = action.Bounds.Width > 0 && action.Bounds.Height > 0;
        var clippingAncestors = action.GetVisualAncestors().Where(ancestor => ancestor.ClipToBounds)
            .Select(ancestor => $"{ancestor.GetType().Name} '{ancestor.Name}' {BoundsInWindow(ancestor, window)}");
        var clipping = string.Join(", ", clippingAncestors);
        var hiddenReason = !action.IsEffectivelyVisible || EffectiveOpacity(action) <= 0 || !actionHasOwnBounds
            ? $"DV dependency: the rightmost action is hidden or has no own bounds ({action.Bounds}) " +
              $"with effective opacity {EffectiveOpacity(action):0.###} before input"
            : $"the rightmost action is clipped from {actionBounds} to {visibleActionBounds} " +
              $"inside slot viewport {viewportBounds} at offset {slot.Offset.X:0.###} DIP; " +
              $"clipping ancestors: {clipping}";
        Record(actionIsShown, state, hiddenReason, failures);
        if (actionIsShown)
            Record(actionBounds.Left >= viewportBounds.Left - 1 && actionBounds.Top >= viewportBounds.Top - 1 &&
                   actionBounds.Right <= viewportBounds.Right + 1 && actionBounds.Bottom <= viewportBounds.Bottom + 1,
                state, $"the visible rightmost action bounds {actionBounds} exceed slot viewport {viewportBounds}", failures);
        return (row, action);
    }

    private static async Task SelectTimingRule(ResponsiveScene scene, WorkspaceShellViewModel workspace,
        MainWindow window, string state, ICollection<string> failures)
    {
        if (scene.Scene != TooltipScene.Timing) return;
        var timingModel = workspace.PageModel<TimingPageModel>();
        var timingRule = timingModel.RuleRows.FirstOrDefault(row => row.HasGrammarWarning)?.Row;
        Record(timingRule is not null, state,
            "no timing rule named by the stored grammar warning was ready", failures);
        if (timingRule is null) return;
        await timingModel.ChooseRuleCommand.ExecuteAsync(timingRule);
        ResetTimingPageScroll(window);
    }

    private static string StateName(ResponsiveScene scene, ResponsiveSize size, ThemeChoice theme) =>
        $"{scene.Name}, 640x{size.Height} at {size.Zoom}% {theme.Name}";

    private static void ResetTimingPageScroll(MainWindow window)
    {
        var timingPage = window.GetVisualDescendants().OfType<TimingPage>().FirstOrDefault();
        var pageScroll = timingPage?.GetVisualDescendants().OfType<ScrollViewer>()
            .FirstOrDefault(scroll => scroll.Content is StackPanel);
        if (pageScroll is not null) pageScroll.Offset = new Vector(pageScroll.Offset.X, 0);
        PageScreenshots.Settle(window);
    }

    private static Rect BoundsInWindow(Visual control, Visual window, Size? size = null)
    {
        var bounds = size ?? control.Bounds.Size;
        var corners = new[]
        {
            control.TranslatePoint(default, window)!.Value,
            control.TranslatePoint(new Point(bounds.Width, 0), window)!.Value,
            control.TranslatePoint(new Point(0, bounds.Height), window)!.Value,
            control.TranslatePoint(new Point(bounds.Width, bounds.Height), window)!.Value,
        };
        var left = corners.Min(point => point.X);
        var top = corners.Min(point => point.Y);
        var right = corners.Max(point => point.X);
        var bottom = corners.Max(point => point.Y);
        return new Rect(left, top, right - left, bottom - top);
    }

    private static double EffectiveOpacity(Visual visual)
    {
        var opacity = visual.Opacity;
        foreach (var ancestor in visual.GetVisualAncestors()) opacity *= ancestor.Opacity;
        return opacity;
    }

    private static Rect ClippedBoundsInWindow(Visual visual, Visual window)
    {
        var visible = BoundsInWindow(visual, window);
        foreach (var ancestor in visual.GetVisualAncestors().Where(ancestor => ancestor.ClipToBounds))
        {
            var clip = BoundsInWindow(ancestor, window);
            var left = Math.Max(visible.Left, clip.Left);
            var top = Math.Max(visible.Top, clip.Top);
            var right = Math.Min(visible.Right, clip.Right);
            var bottom = Math.Min(visible.Bottom, clip.Bottom);
            visible = right > left && bottom > top ? new Rect(left, top, right - left, bottom - top) : default;
        }
        return visible;
    }

    private static async Task CaptureScene(string scene, MainWindow window, string root, ICollection<string> states,
        Action<MainWindow, string>? assertContent = null)
    {
        foreach (var theme in Themes)
        foreach (var size in Sizes)
        {
            ApplySize(window, size, theme);
            var state = $"{scene}, 640x{size.Height} at {size.Zoom}% {theme.Name}";
            if (assertContent is not null) assertContent(window, state);
            else
            {
                var slots = window.GetVisualDescendants().OfType<WordListSlot>()
                    .Where(slot => slot.IsEffectivelyVisible && slot.CurrentLayout is not null).ToArray();
                if (slots.Length == 0)
                    throw new InvalidOperationException($"{state}: no visible word list slot.");
            }

            var fileName = $"{scene}-640x{size.Height}-{size.Zoom}-{theme.Name}.png";
            SaveFrame(window, Path.Combine(root, fileName));
            states.Add($"{fileName}\t{scene}, 640x{size.Height}, {size.Zoom}%, {theme.Name}.");
            await Task.Yield();
        }
    }

    private static void AssertReviewTableVisible(MainWindow window, string state)
    {
        var panel = Assert.Single(window.GetVisualDescendants().OfType<ReviewPanel>());
        var list = panel.FindControl<ListBox>("ReviewItems")!;
        Assert.Contains("denseTable", list.Classes);
        Assert.Equal(AutomationIds.ReviewWordsSlot, AutomationProperties.GetAutomationId(list));
        var changes = list.ItemsSource!.Cast<object>().OfType<ReviewListEntryViewModel>()
            .Where(entry => entry.Change is not null).ToArray();
        Assert.Equal(new[] { "anapenda", "chakula", "kitabu", "watoto" },
            changes.Select(entry => entry.Change!.Word).OrderBy(word => word, StringComparer.Ordinal));
        Assert.True(list.IsEffectivelyVisible && EffectiveOpacity(list) > 0,
            $"{state}: the populated Review table is hidden.");

        var scroll = Assert.Single(list.GetVisualDescendants().OfType<ScrollViewer>(),
            viewer => viewer.Name == "PART_ScrollViewer");
        var body = Assert.Single(scroll.GetVisualDescendants().OfType<ScrollContentPresenter>());
        var pageHost = window.FindInWindowOrWorkspace<Panel>("PageHost")!;
        var bodyBounds = BoundsInWindow(body, window);
        var pageBounds = BoundsInWindow(pageHost, window);
        var windowBounds = new Rect(window.ClientSize);
        Assert.True(body.IsEffectivelyVisible && EffectiveOpacity(body) > 0 &&
            bodyBounds.Width > 0 && bodyBounds.Height > 0 &&
            pageBounds.Inflate(1).Contains(bodyBounds) && windowBounds.Inflate(1).Contains(bodyBounds),
            $"{state}: Review table body {bodyBounds} exceeds page {pageBounds} or window {windowBounds}.");

        var row = list.GetVisualDescendants().OfType<ListBoxItem>().FirstOrDefault(item =>
            item.IsEffectivelyVisible && item.DataContext is ReviewListEntryViewModel { Change: not null } &&
            bodyBounds.Inflate(1).Contains(BoundsInWindow(item, window)) &&
            ClippedBoundsInWindow(item, window).Height >= BoundsInWindow(item, window).Height - 1);
        Assert.True(row is not null, $"{state}: no populated Review change row is fully visible in the table body.");
        var realizedRow = Assert.IsType<ListBoxItem>(row);
        var entry = Assert.IsType<ReviewListEntryViewModel>(realizedRow.DataContext);
        var frame = Assert.Single(realizedRow.GetVisualDescendants().OfType<Border>(),
            border => border.Classes.Contains("denseTableRow"));
        var tracks = Assert.Single(frame.GetVisualDescendants().OfType<Grid>(),
            grid => DenseTable.GetColumns(grid) is not null);
        var header = panel.FindControl<Grid>("ReviewTableHeader")!;
        Assert.Equal(header.ColumnDefinitions.Select(column => column.Width),
            tracks.ColumnDefinitions.Select(column => column.Width));
        var word = Assert.Single(frame.GetVisualDescendants().OfType<CopyableTextBlock>(),
            text => text.IsEffectivelyVisible && text.Text == entry.Change!.Word);
        foreach (var content in new Control[] { frame, word })
        {
            var bounds = BoundsInWindow(content, window);
            var visible = ClippedBoundsInWindow(content, window);
            Assert.True(content.IsEffectivelyVisible && EffectiveOpacity(content) > 0 &&
                bounds.Width > 0 && bounds.Height > 0 && bodyBounds.Inflate(1).Contains(bounds) &&
                visible.Width >= bounds.Width - 1 && visible.Height >= bounds.Height - 1,
                $"{state}: Review {content.GetType().Name} for {entry.Change!.Word} is hidden or clipped; " +
                $"bounds {bounds}, visible {visible}, table body {bodyBounds}.");
        }
    }

    private static void ApplySize(MainWindow window, ResponsiveSize size, ThemeChoice theme)
    {
        Application.Current!.RequestedThemeVariant = theme.Variant;
        window.Width = 640;
        window.Height = size.Height;
        Assert.IsType<WorkspaceShellViewModel>(window.DataContext).PageModel<SettingsPageModel>().Settings!
            .ZoomPercent = size.Zoom;
        PageScreenshots.Settle(window);
        Assert.InRange(Math.Abs(window.ClientSize.Width - 640), 0, 1);
        Assert.InRange(Math.Abs(window.ClientSize.Height - size.Height), 0, 1);
    }

    private static void SaveFrame(MainWindow window, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var frame = window.CaptureRenderedFrame()
            ?? throw new InvalidOperationException($"No frame rendered for {path}.");
        frame.Save(path, PngBitmapEncoderOptions.Default);
    }

    private static void Record(bool passed, string state, string failure, ICollection<string> failures)
    {
        if (!passed) failures.Add($"{state}: {failure}");
    }

    private static readonly ThemeChoice[] Themes =
    [
        new("light", ThemeVariant.Light),
    ];

    private sealed record ResponsiveScene(string Name, TooltipScene Scene, WordListSchema Schema,
        bool HasFieldWorksMorphology, bool HasPanGlossMorphology);
    private sealed record ResponsiveSize(int Height, int Zoom);
    private sealed record ThemeChoice(string Name, ThemeVariant Variant);
}
