using Avalonia;
using Avalonia.Automation;
using Avalonia.Headless;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.VisualTree;
using SIL.Motif.App;
using SIL.Motif.App.Controls.WordPresentation;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using AssessmentProgress = SIL.Motif.Contract.Responses.AssessmentProgress;
using AssessmentStage = SIL.Motif.Contract.Responses.AssessmentStage;
using SIL.Motif.Tests.App.Walkthrough;
using Xunit;
using Xunit.Abstractions;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
[Trait("MotifTestLevel", "System")]
public sealed class SizeMatrixReachabilityTests(ITestOutputHelper output)
{
    [Fact]
    public void TextsDoesNotBreakWordsAtThinSize()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await PageScreenshots.OpenOverSampleData();
            try
            {
                var findings = new List<LayoutFinding>();
                foreach (var (scene, page, tab) in PageScreenshots.Views().Where(view => view.Page == WorkspacePage.Texts))
                {
                    workspace.PageModel<TextsPageModel>().Tab = tab;
                    workspace.CurrentPage = page;
                    if (tab == TextsTab.AnalyzeTexts) await AnalyzeTextsLayoutTests.SettleReaderAsync(workspace, window);
                    SizeMatrixScreenshots.ApplySize(window, MatrixSizes.Read().Single(size => size.Name == "thin"), 100);
                    PageScreenshots.Settle(window);
                    var context = new LayoutFindingContext(scene, "thin", 100, "light");
                    findings.AddRange(window.GetVisualDescendants().OfType<TextBlock>()
                        .SelectMany(text => ReachabilityAssertions.MidWordWrapFindings(text, context, window)));
                }
                Assert.True(findings.Count == 0, string.Join(Environment.NewLine, findings));
            }
            finally
            {
                WindowZoomPolicy.Apply(Application.Current!, ZoomPolicy.DefaultPercent);
                window.Close();
            }
        }, TimeSpan.FromMinutes(2));
    }

    [Fact]
    public void ListsHandoffIsReachableAtThinScaledSizeInBothThemes()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await PageScreenshots.OpenOverSampleData();
            try
            {
                workspace.PageModel<TextsPageModel>().Tab = TextsTab.Lists;
                workspace.CurrentPage = WorkspacePage.Texts;
                foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
                {
                    Application.Current!.RequestedThemeVariant = theme;
                    SizeMatrixScreenshots.ApplySize(window, MatrixSizes.Read().Single(size => size.Name == "thin"), 150);
                    PageScreenshots.Settle(window);
                    var button = Assert.Single(window.GetVisualDescendants().OfType<Button>(),
                        item => AutomationProperties.GetAutomationId(item) == AutomationIds.HandOffList);
                    Assert.True(button.IsEffectivelyVisible && button.IsEffectivelyEnabled);
                    Assert.True(ReachabilityAssertions.IsReachable(button, window), $"Handoff is unreachable in {theme}.");
                }
            }
            finally
            {
                Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
                WindowZoomPolicy.Apply(Application.Current!, ZoomPolicy.DefaultPercent);
                window.Close();
            }
        }, TimeSpan.FromMinutes(2));
    }

    [Fact]
    public void ListsWordRowsAreReachableInAShortScaledWindow()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await PageScreenshots.OpenOverSampleData();
            try
            {
                workspace.PageModel<TextsPageModel>().Tab = TextsTab.Lists;
                workspace.CurrentPage = WorkspacePage.Texts;
                var size = MatrixSizes.Read().Single(item => item.Name == "short");
                SizeMatrixScreenshots.ApplySize(window, size, 150);
                PageScreenshots.Settle(window);
                Assert.Contains(window.GetVisualDescendants().OfType<WordRow>(), row => row.IsEffectivelyVisible);
                var findings = ReachabilityAssertions.InspectReachabilityAndTitleBar(window,
                    new LayoutFindingContext("2c-texts-lists", "short", 150, "light"));
                Assert.DoesNotContain(findings, finding => finding.Check == LayoutCheckNames.UnreachableControl &&
                    finding.Control.Type == nameof(WordRow));
            }
            finally
            {
                WindowZoomPolicy.Apply(Application.Current!, ZoomPolicy.DefaultPercent);
                window.Close();
            }
        }, TimeSpan.FromMinutes(2));
    }

    [Fact]
    public void LargeListsKeepRowsVirtualizedAndTheLastRowReachable()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await PageScreenshots.OpenOverSampleData();
            try
            {
                workspace.PageModel<TextsPageModel>().Tab = TextsTab.Lists;
                workspace.CurrentPage = WorkspacePage.Texts;
                SizeMatrixScreenshots.ApplySize(window, MatrixSizes.Read().Single(item => item.Name == "short"), 150);
                PageScreenshots.Settle(window);
                var panel = Assert.Single(window.GetVisualDescendants().OfType<TextsListsPanel>());
                var list = panel.FindControl<ListBox>("TextsListsPanelWordsItems")!;
                var sample = Assert.IsType<CompareWordViewModel>(list.Items[0]);
                var lists = workspace.PageModel<TextsPageModel>().TextsLists;
                lists.Compare.LoadSources(Enumerable.Range(0, 200)
                    .Select(index => sample.Source with { Word = $"word-{index:000}" }).ToArray());
                lists.SelectListCommand.Execute(lists.Lists.First(item => item.HasWords));
                PageScreenshots.Settle(window);
                Assert.Equal(200, list.ItemCount);
                var outer = Assert.Single(list.GetVisualAncestors().OfType<ScrollViewer>(),
                    scroll => scroll is not WordListSlot);
                Assert.True(list.Bounds.Height <= outer.Bounds.Height,
                    $"List height {list.Bounds.Height} exceeds viewport {outer.Bounds.Height}; realized {list.GetRealizedContainers().Count()}.");
                Assert.InRange(list.GetRealizedContainers().Count(), 1, 40);
                outer.ScrollToEnd();
                list.ScrollIntoView(list.ItemCount - 1);
                PageScreenshots.Settle(window);
                var last = Assert.IsAssignableFrom<Control>(list.ContainerFromIndex(list.ItemCount - 1));
                var bounds = new Rect(last.Bounds.Size).TransformToAABB(last.TransformToVisual(window)!.Value);
                Assert.True(new Rect(window.ClientSize).Contains(bounds), $"Last row is outside the window: {bounds}.");
                Assert.InRange(list.GetRealizedContainers().Count(), 1, 40);
            }
            finally
            {
                WindowZoomPolicy.Apply(Application.Current!, ZoomPolicy.DefaultPercent);
                window.Close();
            }
        }, TimeSpan.FromMinutes(2));
    }

    // A gated Assessment run keeps Cancel live and whole, inside the notice and window, at 150% zoom.
    [Theory]
    [InlineData("thin", 150)]
    [InlineData("half", 150)]
    public void ParseProgressNoticeKeepsItsTextAndCancelInsideTheWindowAtScaledSizes(string sizeName, int zoom)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            FakeCommandClient? fake = null;
            var (workspace, window) = await PageScreenshots.OpenOverSampleData(configure: (client, _) => fake = client);
            Task? running = null;
            try
            {
                fake!.AssessBlocksUntilCancelled(
                    new SIL.Motif.Contract.Commands.Refusal("assessment.cancelled", SIL.Motif.Contract.Responses.FailureReason.Cancelled, "Cancelled."),
                    new AssessmentProgress(AssessmentStage.Parsing, 1, 3, "Parsing"));
                running = workspace.Assess.RerunAsync(["a-first", "b-second", "c-third"], 1000);
                SizeMatrixScreenshots.ApplySize(window, MatrixSizes.Read().Single(size => size.Name == sizeName), zoom);
                PageScreenshots.Settle(window);
                Assert.True(workspace.ShowsParseAllWordsProgress);
                var status = window.FindInWindowOrWorkspace<Button>("StatusParseProgress")!;
                Assert.True(status.IsEffectivelyVisible, $"The parse progress entry is hidden at {sizeName} {zoom}%.");
                Assert.True(ReachabilityAssertions.IsReachable(status, window), $"The parse progress entry is unreachable at {sizeName} {zoom}%.");
                HeadlessClick.Click(window, status, "parse progress status");
                PageScreenshots.Settle(window);
                var flyout = Assert.IsType<Flyout>(status.Flyout);
                Assert.True(flyout.IsOpen, "The parse progress flyout did not open.");
                var notice = Assert.IsAssignableFrom<Control>(flyout.Content);
                var cancel = Assert.Single(window.GetVisualDescendants().OfType<Button>(),
                    item => AutomationProperties.GetAutomationId(item) == AutomationIds.CancelAssessment);
                Assert.True(cancel.IsEffectivelyVisible, "Cancel parsing is hidden.");
                Assert.True(cancel.IsEffectivelyEnabled, "Cancel parsing is disabled during a running Assessment.");
                Assert.True(cancel.Bounds.Width > 0 && cancel.Bounds.Height > 0, "Cancel parsing has no size.");
                var inNotice = new Rect(cancel.Bounds.Size).TransformToAABB(cancel.TransformToVisual(notice)!.Value);
                Assert.True(new Rect(notice.Bounds.Size).Inflate(0.5).Contains(inNotice),
                    $"Cancel {inNotice} is outside the notice {notice.Bounds.Size} at {sizeName} {zoom}%.");
                var inWindow = new Rect(cancel.Bounds.Size).TransformToAABB(cancel.TransformToVisual(window)!.Value);
                Assert.True(new Rect(window.ClientSize).Inflate(0.5).Contains(inWindow),
                    $"Cancel {inWindow} is outside the window {window.ClientSize} at {sizeName} {zoom}%.");
                Assert.Empty(FixedBoxClips.Find(notice, $"parse-progress-{sizeName}@{zoom}", output));
                flyout.Hide();
            }
            finally
            {
                workspace.Assess.CancelCommand.Execute(null);
                if (running is not null) await running.WaitAsync(TimeSpan.FromSeconds(20));
                WindowZoomPolicy.Apply(Application.Current!, ZoomPolicy.DefaultPercent);
                window.Close();
            }
        }, TimeSpan.FromMinutes(2));
    }

    [Fact]
    public void EveryPageKeepsControlsReachableAtSmallAndScaledSizes()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var sizes = MatrixSizes.Read().ToDictionary(size => size.Name, StringComparer.Ordinal);
            var (workspace, window) = await PageScreenshots.OpenOverSampleData();
            try
            {
                Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
                var sizesToInspect = new[] { ("thin", 100), ("thin", 150), ("half", 150) };
                var selection = new MatrixCaptureSelection(
                    PageScreenshots.Views().Select(view => view.Name).ToHashSet(StringComparer.Ordinal),
                    sizesToInspect.Select(point => new MatrixSelectionPoint(point.Item1, point.Item2, "light")).ToArray(),
                    LayoutCheckNames.ReachabilityAndTitleBar);
                var allFindings = new List<LayoutFinding>();
                foreach (var (scene, page, tab) in PageScreenshots.Views())
                {
                    workspace.PageModel<TextsPageModel>().Tab = tab;
                    if (page == WorkspacePage.TryAWord) await PageScreenshots.TryTheSampleWord(workspace);
                    workspace.CurrentPage = page;
                    if (page == WorkspacePage.Texts && tab == TextsTab.AnalyzeTexts)
                        await AnalyzeTextsLayoutTests.SettleReaderAsync(workspace, window);

                    foreach (var (sizeName, zoom) in sizesToInspect)
                    {
                        var size = sizes[sizeName];
                        SizeMatrixScreenshots.ApplySize(window, size, zoom);
                        PageScreenshots.Settle(window);
                        Assert.True(Math.Abs(window.ClientSize.Width - size.Width) < 1,
                            $"Expected client width {size.Width}, got {window.ClientSize.Width} after resize.");
                        Assert.True(Math.Abs(window.ClientSize.Height - size.Height) < 1,
                            $"Expected client height {size.Height}, got {window.ClientSize.Height} after resize.");
                        var context = new LayoutFindingContext(scene, size.Name, zoom, "light");
                        var findings = ReachabilityAssertions.InspectReachabilityAndTitleBar(window, context);
                        allFindings.AddRange(findings);
                    }
                }

                ExpectedMatrixFindings.AssertNoUnexpected(allFindings, requireAllExpected: true, selection);
            }
            finally
            {
                Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
                WindowZoomPolicy.Apply(Application.Current!, ZoomPolicy.DefaultPercent);
                window.Close();
            }
        }, TimeSpan.FromMinutes(4));
    }
}
