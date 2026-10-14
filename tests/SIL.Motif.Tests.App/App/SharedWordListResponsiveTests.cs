using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using SIL.Motif.App;
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

    private static readonly ResponsiveSize[] Sizes =
    [
        new(600, 100), new(600, 150), new(900, 100), new(900, 150),
    ];

    [Fact]
    public void SharedWordListsKeepTheirTracksInsideTheActualViewport()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var failures = new List<string>();
            var cacheWasChecked = false;
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
                        if (!cacheWasChecked)
                        {
                            InspectScrollCache(slots[index], state, window, failures);
                            cacheWasChecked = true;
                        }
                    }
                }
            });

            var fake = new FakeCommandClient();
            fake.PendingChangesIs(ReviewScreenshots.Blocked());
            var (reviewWorkspace, reviewWindow) = await ReviewScreenshots.OpenAsync(fake);
            try
            {
                reviewWorkspace.CurrentPage = WorkspacePage.Review;
                PageScreenshots.Settle(reviewWindow);
                foreach (var theme in Themes)
                foreach (var size in Sizes)
                {
                    ApplySize(reviewWindow, size, theme);
                    var slots = reviewWindow.GetVisualDescendants().OfType<WordListSlot>()
                        .Where(slot => slot.IsEffectivelyVisible && slot.CurrentLayout is not null).ToArray();
                    var state = $"6-review, 640x{size.Height} at {size.Zoom}% {theme.Name}";
                    Record(slots.Length > 0, state,
                        "no visible production WordListSlot was measured", failures);

                    for (var index = 0; index < slots.Length; index++)
                        InspectSlotLayout(slots[index], new ResponsiveScene("6-review", TooltipScene.ReviewStaged,
                            WordListSchemas.Review, true, true), index, size, theme.Name, reviewWindow, failures);
                }
            }
            finally
            {
                Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
                WindowZoomPolicy.Apply(Application.Current!, ZoomPolicy.DefaultPercent);
                reviewWindow.Close();
            }

            Record(cacheWasChecked, "shared word-list cache", "no measured slot was available for a scroll check", failures);
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
                await CaptureScene("6-review", window, root, states);
            }
            finally
            {
                Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
                WindowZoomPolicy.Apply(Application.Current!, ZoomPolicy.DefaultPercent);
                window.Close();
            }
        }, TimeSpan.FromMinutes(5));

        File.WriteAllLines(Path.Combine(root, "shared-word-list-states.txt"), states);
        Assert.Equal(40, states.Count);
    }

    private static async Task VisitWordListScenes(
        Func<ResponsiveScene, TooltipScenes, WorkspaceShellViewModel, MainWindow, Task> visit,
        IEnumerable<ResponsiveScene>? selectedScenes = null, bool leaveAfterVisit = true)
    {
        var fixture = await TooltipScenes.Open();
        fixture.Width = 640;
        var workspace = fixture.Workspace;
        var window = fixture.Window;
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
            foreach (var theme in Themes)
            foreach (var size in Sizes)
            {
                await VisitWordListScenes(async (scene, _, workspace, window) =>
                {
                    var state = StateName(scene, size, theme);
                    window.MouseMove(new Point(8, 8));
                    PageScreenshots.Settle(window);
                    await SelectTimingRule(scene, workspace, window, state, failures);
                    ApplySize(window, size, theme);
                    var slot = window.GetVisualDescendants().OfType<WordListSlot>()
                        .FirstOrDefault(item => item.IsEffectivelyVisible && item.CurrentLayout is not null);
                    Record(slot is not null, state,
                        "no visible production WordListSlot was measured", failures);
                    if (slot is not null)
                        InspectRightmostAction(slot, scene, size, theme, window, workspace, failures);
                }, [selectedScene], leaveAfterVisit: false);

                await VisitWordListScenes(async (scene, _, workspace, window) =>
                {
                    var state = StateName(scene, size, theme);
                    window.MouseMove(new Point(8, 8));
                    PageScreenshots.Settle(window);
                    await SelectTimingRule(scene, workspace, window, state, failures);
                    ApplySize(window, size, theme);
                    var slot = window.GetVisualDescendants().OfType<WordListSlot>()
                        .FirstOrDefault(item => item.IsEffectivelyVisible && item.CurrentLayout is not null);
                    Record(slot is not null, state,
                        "no visible production WordListSlot was measured for keyboard input", failures);
                    if (slot is null) return;
                    var target = PrepareRightmostAction(slot, scene, size, theme, window, failures);
                    if (target is null) return;
                    var (row, action) = target.Value;
                    var outsideFocus = window.FindControl<Button>("ProjectMenuButton");
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
                }, [selectedScene]);
            }
            Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
        }, TimeSpan.FromMinutes(5));
    }

    private static void InspectSlotLayout(WordListSlot slot, ResponsiveScene scene, int index, ResponsiveSize size,
        string theme, MainWindow window, ICollection<string> failures)
    {
        var state = $"{scene.Name}, slot {index + 1}, 640x{size.Height} at {size.Zoom}% {theme}";
        var layout = slot.CurrentLayout!;
        var viewportBounds = BoundsInWindow(slot, window, slot.Viewport);
        var pageHost = window.FindControl<Panel>("PageHost");
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
        Record(layout.UsesHorizontalViewport, state,
            $"the {layout.TrackAreaWidth:0.###} DIP tracks do not use the slot's horizontal viewport", failures);
        Record(slot.Extent.Width > slot.Viewport.Width + 1, state,
            $"horizontal extent {slot.Extent.Width:0.###} does not exceed viewport {slot.Viewport.Width:0.###}", failures);
        Record(slot.HorizontalScrollBarVisibility == ScrollBarVisibility.Auto, state,
            "the slot does not expose its horizontal overflow", failures);

        var expectedColumns = layout.Schema.OrderedColumns(layout.ShowsMeaning);
        Record(layout.Schema == scene.Schema, state,
            "the slot changed the scene's semantic word-list schema", failures);
        Record(layout.Schema.HasFieldWorksMorphology == scene.HasFieldWorksMorphology &&
               layout.Schema.HasPanGlossMorphology == scene.HasPanGlossMorphology,
            state, "the slot changed which producer columns carry morphology", failures);
        Record(expectedColumns.SequenceEqual(layout.Tracks.Select(track => track.Column)), state,
            "the resolved layout changed the schema's semantic columns", failures);

        var header = (slot.Content as Control)?.GetVisualDescendants().OfType<WordRowHeader>().FirstOrDefault();
        var row = slot.GetVisualDescendants().OfType<WordRow>().FirstOrDefault(item => item.IsEffectivelyVisible);
        Record(header is not null, state, "the shared header is absent", failures);
        Record(row is not null, state, "no realized word row is visible", failures);
        if (header is null || row is null) return;

        Record(ReferenceEquals(layout, WordListLayoutScope.GetLayout(header)), state,
            "the header did not receive the slot's resolved layout", failures);
        Record(ReferenceEquals(layout, WordListLayoutScope.GetLayout(row)), state,
            "the realized row did not receive the slot's resolved layout", failures);

        var headerLine = header.GetVisualDescendants().OfType<Grid>()
            .FirstOrDefault(grid => grid.Classes.Contains("wordPresentationHeaderLine"));
        var rowLine = row.GetVisualDescendants().OfType<Grid>()
            .FirstOrDefault(grid => grid.Classes.Contains("wordPresentationShell"));
        Record(headerLine is not null && rowLine is not null, state,
            "the shared header or row track grid is absent", failures);
        if (headerLine is null || rowLine is null) return;

        Record(TracksMatch(headerLine, layout) && TracksMatch(rowLine, layout), state,
            "the header or row tracks differ from the slot's resolved layout", failures);
    }

    private static void InspectScrollCache(WordListSlot slot, string state, MainWindow window,
        ICollection<string> failures)
    {
        var layout = slot.CurrentLayout;
        var resolutions = slot.LayoutResolutionCount;
        var publications = slot.LayoutPublicationCount;
        var headingMeasurements = slot.HeadingMeasurementCount;
        slot.Offset = new Vector(Math.Max(0, slot.Extent.Width - slot.Viewport.Width), slot.Offset.Y);
        PageScreenshots.Settle(window);
        Record(slot.Offset.X > 0, state, "the word list did not scroll to its rightmost tracks", failures);
        Record(ReferenceEquals(layout, slot.CurrentLayout) && resolutions == slot.LayoutResolutionCount &&
               publications == slot.LayoutPublicationCount && headingMeasurements == slot.HeadingMeasurementCount,
            state, "scrolling changed the cached sizing or published tracks", failures);
        slot.Offset = new Vector(0, slot.Offset.Y);
        PageScreenshots.Settle(window);
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
        var scrollBar = slot.GetVisualDescendants().OfType<ScrollBar>().FirstOrDefault(bar =>
            bar.Orientation == Orientation.Horizontal && bar.IsEffectivelyVisible &&
            bar.Bounds.Width > 0 && bar.Bounds.Height > 0);
        Record(scrollBar is not null, state,
            "the shared word-list horizontal scrollbar is not visible for pointer input", failures);
        if (scrollBar is not null)
        {
            scrollBar.BringIntoView();
            PageScreenshots.Settle(window);
            var thumb = FindVisibleThumb(scrollBar);
            Record(thumb is not null, state,
                "the shared word-list horizontal scrollbar has no visible thumb to drag", failures);
            var scrollRoot = thumb is null ? null : TopLevel.GetTopLevel(thumb);
            Record(scrollRoot is not null, state,
                "the shared word-list horizontal scrollbar has no pointer input root", failures);
            if (thumb is not null && scrollRoot is not null)
            {
                var track = thumb.FindAncestorOfType<Track>();
                Record(track is not null, state,
                    "the shared word-list scrollbar thumb has no track", failures);
                if (track is not null)
                {
                    var scrollStart = slot.Offset.X;
                    var pressedTargets = new List<string>();
                    var attemptedPoints = new List<string>();
                    void RecordScrollbarPress(object? _, PointerPressedEventArgs args)
                    {
                        var source = args.Source as Visual;
                        var currentBar = FindHorizontalScrollBar(slot);
                        var currentThumb = currentBar is null ? null : FindVisibleThumb(currentBar);
                        var currentTrack = currentThumb?.FindAncestorOfType<Track>();
                        var sourcePath = source is null
                            ? "no visual source"
                            : string.Join(" <- ", source.GetVisualAncestors().Prepend(source).Take(7)
                                .Select(visual =>
                                    $"{visual.GetType().Name} '{(visual as Control)?.Name}' " +
                                    $"{BoundsInWindow(visual, scrollRoot)}"));
                        pressedTargets.Add(
                            $"position={args.GetPosition(scrollRoot)}, source={sourcePath}, " +
                            $"inputRoot={scrollRoot.GetType().Name} client={scrollRoot.ClientSize}, " +
                            $"bar={DescribeBounds(currentBar, scrollRoot)}, " +
                            $"visibleBar={DescribeClippedBounds(currentBar, scrollRoot)}, " +
                            $"thumb={DescribeBounds(currentThumb, scrollRoot)}, " +
                            $"track={DescribeBounds(currentTrack, scrollRoot)}, " +
                            $"visibleTrack={DescribeClippedBounds(currentTrack, scrollRoot)}, " +
                            $"slotViewport={BoundsInWindow(slot, scrollRoot, slot.Viewport)}, " +
                            $"offset={slot.Offset.X:0.###}");
                    }

                    scrollRoot.AddHandler(InputElement.PointerPressedEvent, RecordScrollbarPress,
                        RoutingStrategies.Tunnel, handledEventsToo: true);
                    if (!ReferenceEquals(scrollRoot, window))
                        window.AddHandler(InputElement.PointerPressedEvent, RecordScrollbarPress,
                            RoutingStrategies.Tunnel, handledEventsToo: true);
                    try
                    {
                        attemptedPoints.Add($"initial thumb hover={BoundsInWindow(thumb, scrollRoot).Center}");
                        scrollRoot.MouseMove(BoundsInWindow(thumb, scrollRoot).Center);
                        PageScreenshots.Settle(window);
                        scrollBar = FindHorizontalScrollBar(slot);
                        thumb = scrollBar is null ? null : FindVisibleThumb(scrollBar);
                        track = thumb?.FindAncestorOfType<Track>();
                        Record(thumb is not null && track is not null, state,
                            "the horizontal scrollbar thumb or track disappeared after hover", failures);
                        if (thumb is not null && track is not null)
                        {
                            scrollRoot.MouseMove(BoundsInWindow(thumb, scrollRoot).Center);
                            PageScreenshots.Settle(window);
                            scrollBar = FindHorizontalScrollBar(slot);
                            thumb = scrollBar is null ? null : FindVisibleThumb(scrollBar);
                            track = thumb?.FindAncestorOfType<Track>();
                        }

                        Record(thumb is not null && track is not null, state,
                            "the horizontal scrollbar thumb or track disappeared after refreshed hover", failures);
                        if (thumb is not null && track is not null)
                        {
                            var thumbBounds = BoundsInWindow(thumb, scrollRoot);
                            var trackBounds = BoundsInWindow(track, scrollRoot);
                            var visibleTrackBounds = ClippedBoundsInWindow(track, scrollRoot);
                            var start = thumbBounds.Center;
                            var end = new Point(
                                Math.Min(trackBounds.Right - thumbBounds.Width / 2,
                                    visibleTrackBounds.Right - thumbBounds.Width / 2), start.Y);
                            attemptedPoints.Add(
                                $"thumb press={start}, drag end={end}, " +
                                $"root={scrollRoot.GetType().Name}/{scrollRoot.ClientSize}, " +
                                $"thumb={thumbBounds}, track={trackBounds}, visible track={visibleTrackBounds}");
                            scrollRoot.MouseDown(start, MouseButton.Left);
                            scrollRoot.MouseMove(end, RawInputModifiers.LeftMouseButton);
                            scrollRoot.MouseUp(end, MouseButton.Left);
                            PageScreenshots.Settle(window);
                        }

                        if (slot.Offset.X <= scrollStart)
                        {
                            scrollBar = FindHorizontalScrollBar(slot);
                            thumb = scrollBar is null ? null : FindVisibleThumb(scrollBar);
                            track = thumb?.FindAncestorOfType<Track>();
                            Record(track is not null, state,
                                "the shared horizontal scrollbar has no track for pointer fallback", failures);
                            if (track is not null)
                            {
                                var initialTrackPoint = VisibleTrackEnd(track, scrollRoot);
                                attemptedPoints.Add($"track fallback hover={initialTrackPoint}");
                                scrollRoot.MouseMove(initialTrackPoint);
                                PageScreenshots.Settle(window);
                                scrollBar = FindHorizontalScrollBar(slot);
                                thumb = scrollBar is null ? null : FindVisibleThumb(scrollBar);
                                track = thumb?.FindAncestorOfType<Track>();
                                Record(track is not null, state,
                                    "the shared horizontal scrollbar track disappeared after fallback hover", failures);
                                if (track is not null)
                                {
                                    var trackPoint = VisibleTrackEnd(track, scrollRoot);
                                    attemptedPoints.Add(
                                        $"track fallback press={trackPoint}, " +
                                        $"track={BoundsInWindow(track, scrollRoot)}, " +
                                        $"visible track={VisibleBoundsInRoot(track, scrollRoot)}");
                                    scrollRoot.MouseMove(trackPoint);
                                    PageScreenshots.Settle(window);
                                    scrollBar = FindHorizontalScrollBar(slot);
                                    thumb = scrollBar is null ? null : FindVisibleThumb(scrollBar);
                                    track = thumb?.FindAncestorOfType<Track>();
                                    if (track is not null)
                                    {
                                        trackPoint = VisibleTrackEnd(track, scrollRoot);
                                        scrollRoot.MouseDown(trackPoint, MouseButton.Left);
                                        scrollRoot.MouseUp(trackPoint, MouseButton.Left);
                                        PageScreenshots.Settle(window);
                                    }
                                }
                            }
                        }
                    }
                    finally
                    {
                        scrollRoot.RemoveHandler(InputElement.PointerPressedEvent, RecordScrollbarPress);
                        if (!ReferenceEquals(scrollRoot, window))
                            window.RemoveHandler(InputElement.PointerPressedEvent, RecordScrollbarPress);
                    }

                    var pressDetails = pressedTargets.Count == 0
                        ? "no PointerPressed source was observed; " + string.Join("; ", attemptedPoints)
                        : string.Join("; ", pressedTargets);
                    var parentPageScroll = slot.GetVisualAncestors().OfType<ScrollViewer>()
                        .FirstOrDefault(viewer => viewer.VerticalScrollBarVisibility != ScrollBarVisibility.Disabled);
                    if (slot.Offset.X <= scrollStart)
                        failures.Add($"{state}: real pointer input on the shared horizontal scrollbar did not " +
                            $"move its viewport (offset {scrollStart:0.###} to {slot.Offset.X:0.###}); " +
                            $"input root={scrollRoot.GetType().Name} client={scrollRoot.ClientSize}; " +
                            $"parent page scroll={parentPageScroll?.Offset}/{parentPageScroll?.Viewport}/" +
                            $"{parentPageScroll?.Extent}; " +
                            $"slot viewport={BoundsInWindow(slot, scrollRoot, slot.Viewport)}; " +
                            $"scrollbar={DescribeBounds(FindHorizontalScrollBar(slot), scrollRoot)}; " +
                            $"visible scrollbar={DescribeClippedBounds(FindHorizontalScrollBar(slot), scrollRoot)}; " +
                            $"scrollbar presses: {pressDetails}");
                }
            }
        }

        var rows = (slot.Content as Grid)?.Children.OfType<ItemsControl>().FirstOrDefault();
        if (rows is { ItemCount: > 0 })
        {
            rows.ScrollIntoView(0);
            PageScreenshots.Settle(window);
        }
        row = slot.GetVisualDescendants().OfType<WordRow>()
            .FirstOrDefault(item => item.IsEffectivelyVisible);
        Record(row is not null, state, "no realized word row is visible after scrollbar input", failures);
        if (row is null) return null;
        row.BringIntoView();
        PageScreenshots.Settle(window);
        var rightmostOffset = Math.Max(0, slot.Extent.Width - slot.Viewport.Width);
        slot.Offset = new Vector(rightmostOffset, slot.Offset.Y);
        PageScreenshots.Settle(window);
        Record(rightmostOffset > 0 && Math.Abs(slot.Offset.X - rightmostOffset) <= 1, state,
            $"the word list did not remain at its rightmost tracks after scrolling ({slot.Offset.X:0.###}/" +
            $"{rightmostOffset:0.###} DIP)", failures);
        row = slot.GetVisualDescendants().OfType<WordRow>()
            .FirstOrDefault(item => item.IsEffectivelyVisible);
        Record(row is not null, state, "no realized word row is visible at the rightmost tracks", failures);
        if (row is null) return null;
        var actions = row.GetVisualDescendants().OfType<Button>().Where(button =>
            AutomationProperties.GetAutomationId(button) is AutomationIds.WordOpenInText or AutomationIds.WordTryAWord)
            .ToArray();
        Record(actions.Length > 0, state, "the row's next-step actions are absent after horizontal scrolling", failures);
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

    private static ScrollBar? FindHorizontalScrollBar(WordListSlot slot) =>
        slot.GetVisualDescendants().OfType<ScrollBar>().FirstOrDefault(bar =>
            bar.Orientation == Orientation.Horizontal && bar.IsEffectivelyVisible &&
            bar.Bounds.Width > 0 && bar.Bounds.Height > 0);

    private static Thumb? FindVisibleThumb(ScrollBar scrollBar) =>
        scrollBar.GetVisualDescendants().OfType<Thumb>().FirstOrDefault(control =>
            control.IsEffectivelyVisible && control.Bounds.Width > 0 && control.Bounds.Height > 0);

    private static Point VisibleTrackEnd(Track track, TopLevel root)
    {
        var visible = VisibleBoundsInRoot(track, root);
        return new Point(visible.Right - 1, visible.Center.Y);
    }

    private static Rect VisibleBoundsInRoot(Visual visual, TopLevel root)
    {
        var clipped = ClippedBoundsInWindow(visual, root);
        var rootBounds = new Rect(default, root.ClientSize);
        var left = Math.Max(clipped.Left, rootBounds.Left);
        var top = Math.Max(clipped.Top, rootBounds.Top);
        var right = Math.Min(clipped.Right, rootBounds.Right);
        var bottom = Math.Min(clipped.Bottom, rootBounds.Bottom);
        return right > left && bottom > top ? new Rect(left, top, right - left, bottom - top) : default;
    }

    private static string DescribeBounds(Visual? visual, Visual root) => visual is null
        ? "none"
        : BoundsInWindow(visual, root).ToString();

    private static string DescribeClippedBounds(Visual? visual, Visual root) => visual is null
        ? "none"
        : root is TopLevel topLevel
            ? VisibleBoundsInRoot(visual, topLevel).ToString()
            : ClippedBoundsInWindow(visual, root).ToString();

    private static void ResetTimingPageScroll(MainWindow window)
    {
        var timingPage = window.GetVisualDescendants().OfType<TimingPage>().FirstOrDefault();
        var pageScroll = timingPage?.GetVisualDescendants().OfType<ScrollViewer>()
            .FirstOrDefault(scroll => scroll.Content is StackPanel);
        if (pageScroll is not null) pageScroll.Offset = new Vector(pageScroll.Offset.X, 0);
        PageScreenshots.Settle(window);
    }

    private static bool TracksMatch(Grid grid, WordListLayout layout) =>
        grid.ColumnDefinitions.Count == layout.Tracks.Length && layout.Tracks.Select((track, index) =>
            Math.Abs(grid.ColumnDefinitions[index].Width.Value - track.Width) <= 1).All(matches => matches);

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

    private static async Task CaptureScene(string scene, MainWindow window, string root, ICollection<string> states)
    {
        foreach (var theme in Themes)
        foreach (var size in Sizes)
        {
            ApplySize(window, size, theme);
            var slots = window.GetVisualDescendants().OfType<WordListSlot>()
                .Where(slot => slot.IsEffectivelyVisible && slot.CurrentLayout is not null).ToArray();
            if (slots.Length == 0)
                throw new InvalidOperationException($"{scene}, 640x{size.Height} at {size.Zoom}% {theme.Name}: no visible word list slot.");

            var fileName = $"{scene}-640x{size.Height}-{size.Zoom}-{theme.Name}.png";
            SaveFrame(window, Path.Combine(root, fileName));
            states.Add($"{fileName}\t{scene}, 640x{size.Height}, {size.Zoom}%, {theme.Name}.");
            await Task.Yield();
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
        new("dark", ThemeVariant.Dark),
    ];

    private sealed record ResponsiveScene(string Name, TooltipScene Scene, WordListSchema Schema,
        bool HasFieldWorksMorphology, bool HasPanGlossMorphology);
    private sealed record ResponsiveSize(int Height, int Zoom);
    private sealed record ThemeChoice(string Name, ThemeVariant Variant);
}
