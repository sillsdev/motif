using Avalonia;
using Avalonia.Controls;
using Avalonia.Automation;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
[Trait("MotifTestLevel", "Integration")]
public sealed class LayoutInvariantEngineTests(AvaloniaHeadlessFixture avalonia)
{
    [Fact]
    public void CachedFullGridMeasurementsFollowTextAndFontChanges()
    {
        avalonia.Invoke(() =>
        {
            var text = new TextBlock
            {
                Width = 40, Height = 80, TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.None,
            };
            var window = new MotifTestWindow { Width = 320, Height = 160, Content = text };
            var measurements = new LayoutTextMeasurements();
            var context = new LayoutFindingContext("measurement", "cell", 100, "light");
            try
            {
                window.Show();
                foreach (var (value, size, weight) in new[]
                {
                    ("i", 12d, FontWeight.Normal), ("MMMMMMMM", 12d, FontWeight.Normal),
                    ("MMMMMMMM", 4d, FontWeight.Normal), ("MMMMMMMM", 24d, FontWeight.Bold),
                    ("MMMMMMMM", 24d, FontWeight.Normal), ("i", 12d, FontWeight.Normal),
                })
                {
                    text.Text = value;
                    text.FontSize = size;
                    text.FontWeight = weight;
                    LayoutInvariantEngineTests.Settle(window);
                    var fresh = ReachabilityAssertions.TextBoxOverflowFindings(text, context, window);
                    var cached = ReachabilityAssertions.TextBoxOverflowFindings(text, context, window, measurements);
                    Assert.Equal(fresh, cached);
                    if (value == "i" || size == 4) Assert.Empty(cached);
                    else Assert.Single(cached);
                }
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void AVisibleUncoveredActionIsReachableAfterDrawingDataSettles()
    {
        avalonia.Invoke(() =>
        {
            var action = new Button { Content = "Go", Width = 80, Height = 30 };
            WithWindow(action, window => Assert.True(ReachabilityAssertions.IsReachable(action, window)));
        });
    }

    [Theory]
    [InlineData("opacity")]
    [InlineData("ancestor-opacity")]
    [InlineData("hit-testing")]
    [InlineData("overlay")]
    [InlineData("zero-height")]
    public void AnEnabledActionNeedsAVisibleHitTarget(string defect)
    {
        avalonia.Invoke(() =>
        {
            var action = new Button { Name = "Action", Content = "Go", Width = 80, Height = 30 };
            var host = new Grid { Children = { action } };
            if (defect == "opacity") action.Opacity = 0;
            if (defect == "ancestor-opacity") host.Opacity = 0;
            if (defect == "hit-testing") host.IsHitTestVisible = false;
            if (defect == "overlay") host.Children.Add(new Border { Background = Brushes.Transparent });
            if (defect == "zero-height") { host.MaxHeight = 0; host.ClipToBounds = true; }
            WithWindow(host, window => Assert.False(ReachabilityAssertions.IsReachable(action, window)));
        });
    }

    [Fact]
    public void ScrollingOutsideAnInnerDisabledViewportCannotRevealItsClippedAction()
    {
        avalonia.Invoke(() =>
        {
            var action = new Button { Content = "Go", Width = 80, Height = 30 };
            var content = new Canvas { Height = 300, Children = { action } };
            Canvas.SetTop(action, 220);
            var inner = new ScrollViewer
            {
                Height = 80, Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            };
            var outer = new ScrollViewer
            {
                Content = new StackPanel { Children = { new Border { Height = 180 }, inner } },
            };
            WithWindow(outer, window => Assert.False(ReachabilityAssertions.IsReachable(action, window)));
        });
    }

    [Fact]
    public void AnActionInsideItsOwnScrollableExtentHasARevealRoute()
    {
        avalonia.Invoke(() =>
        {
            var action = new Button { Content = "Go", Width = 80, Height = 30 };
            var content = new Canvas { Height = 300, Children = { action } };
            Canvas.SetTop(action, 220);
            var scroll = new ScrollViewer { Content = content };
            WithWindow(scroll, window => Assert.True(ReachabilityAssertions.IsReachable(action, window)));
        });
    }

    [Fact]
    public void OverlapBehindAClipIsNotReportedAsPaintedText()
    {
        avalonia.Invoke(() =>
        {
            var first = new TextBlock { Text = "A long piece of left text", Width = 250, Height = 30 };
            var clipped = new Border { Width = 80, Height = 30, ClipToBounds = true, Child = first };
            var second = new TextBlock { Text = "Right text", Width = 100, Height = 30 };
            var canvas = new Canvas { Children = { clipped, second } };
            Canvas.SetLeft(second, 120);
            WithWindow(canvas, window => Assert.DoesNotContain(
                ReachabilityAssertions.Inspect(window, new("clip", "small", 100, "light")),
                finding => finding.Check == LayoutCheckNames.OverlappingText));
        });
    }

    [Fact]
    public void AScrollRouteMustProduceARealHitAndRestoreItsOffset()
    {
        avalonia.Invoke(() =>
        {
            var action = new Button { Content = "Go", Width = 80, Height = 30 };
            var content = new Canvas { Height = 500, Children = { action } };
            Canvas.SetTop(action, 400);
            var scroll = new ScrollViewer { Content = content };
            WithWindow(scroll, window =>
            {
                Assert.True(ReachabilityAssertions.IsReachable(action, window));
                Assert.Equal(default, scroll.Offset);
                content.Children.Add(new Border { Width = 100, Height = 500, Background = Brushes.Transparent });
                Settle(window);
                Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Assert.False(ReachabilityAssertions.IsReachable(action, window));
                Assert.Equal(default, scroll.Offset);
            });
        });
    }

    [Fact]
    public void ScrollingCannotRepairAViewportOutsideTheWindow()
    {
        avalonia.Invoke(() =>
        {
            var action = new Button { Content = "Go", Width = 80, Height = 30 };
            var scroll = new ScrollViewer { Width = 100, Height = 100,
                Content = new StackPanel { Children = { new Border { Height = 200 }, action } } };
            Canvas.SetLeft(scroll, 400);
            WithWindow(new Canvas { Children = { scroll } }, window =>
                Assert.False(ReachabilityAssertions.IsReachable(action, window)));
        });
    }

    [Fact]
    public void RuntimeNamesUsePeersAndDuplicateIdsIdentifyBothActions()
    {
        avalonia.Invoke(() =>
        {
            var label = new Button { Content = "Save" };
            var unnamed = new Button { Content = new Border() };
            AutomationProperties.SetAutomationId(label, "shared");
            AutomationProperties.SetAutomationId(unnamed, "shared");
            WithWindow(new StackPanel { Children = { label, unnamed } }, window =>
            {
                var findings = ReachabilityAssertions.AutomationFindings([label, unnamed],
                    new("names", "small", 100, "light"), window);
                Assert.Single(findings, finding => finding.Check == LayoutCheckNames.MissingAutomationName);
                Assert.Single(findings, finding => finding.Check == LayoutCheckNames.DuplicateAutomationId);
                AutomationProperties.SetName(unnamed, "Open inspector");
                AutomationProperties.SetAutomationId(unnamed, "inspector");
                Assert.Empty(ReachabilityAssertions.AutomationFindings([label, unnamed],
                    new("names", "small", 100, "light"), window));
            });
        });
    }

    [Fact]
    public void BlankAllocatedTextBoxesDoNotCountAsPaintedOverlap()
    {
        avalonia.Invoke(() =>
        {
            var real = new TextBlock { Text = "A label", Width = 100, Height = 30 };
            var blank = new TextBlock { Width = 100, Height = 30 };
            WithWindow(new Grid { Children = { real, blank } }, window => Assert.DoesNotContain(
                ReachabilityAssertions.InspectQuick(window, new("blank", "small", 100, "light")),
                finding => finding.Check == LayoutCheckNames.OverlappingText));
        });
    }

    [Fact]
    public void ABackRouteCanRealizeAndHitAnOffscreenNavigationItem()
    {
        avalonia.Invoke(() =>
        {
            var list = new ListBox { ItemsSource = Enumerable.Range(0, 100).Select(index => $"Page {index}").ToArray() };
            WithWindow(list, window =>
            {
                list.ScrollIntoView(99);
                Settle(window);
                var scroll = Assert.Single(list.GetVisualDescendants().OfType<ScrollViewer>());
                var offset = scroll.Offset;
                Assert.True(ReachabilityAssertions.CanReachItem(list, 0, window));
                Assert.Equal(offset, scroll.Offset);
                Assert.False(ReachabilityAssertions.CanReachItem(list, 100, window));
            });
        });
    }

    [Fact]
    public void AutomationIdsCanRepeatInDistinctRowsButNotWithinOneIdentifiedContainer()
    {
        avalonia.Invoke(() =>
        {
            var first = new Button { Content = "Go" };
            var second = new Button { Content = "Go" };
            var rowOne = new StackPanel { Children = { first } };
            var rowTwo = new StackPanel { Children = { second } };
            AutomationProperties.SetAutomationId(rowOne, "row-one");
            AutomationProperties.SetAutomationId(rowTwo, "row-two");
            AutomationProperties.SetAutomationId(first, "action");
            AutomationProperties.SetAutomationId(second, "action");
            WithWindow(new StackPanel { Children = { rowOne, rowTwo } }, window =>
            {
                var context = new LayoutFindingContext("ids", "small", 100, "light");
                Assert.Empty(ReachabilityAssertions.AutomationFindings([rowOne, rowTwo, first, second], context, window));
                AutomationProperties.SetAutomationId(rowTwo, "row-one");
                Assert.Single(ReachabilityAssertions.AutomationFindings([rowOne, rowTwo, first, second], context, window));
            });
        });
    }

    [Fact]
    public void TextDetachedDuringAScrollProbeCannotOverlapTheLivePage()
    {
        avalonia.Invoke(() =>
        {
            var live = new TextBlock { Text = "Current", Width = 80, Height = 30 };
            var old = new TextBlock { Text = "Old", Width = 80, Height = 30 };
            var oldHost = new Border { Width = 80, Height = 30, Child = old };
            Canvas.SetLeft(oldHost, 120);
            var action = new Button { Content = "Go", Width = 60, Height = 30 };
            Canvas.SetTop(action, 400);
            var scroll = new ScrollViewer { Width = 90, Height = 120,
                Content = new Canvas { Height = 500, Children = { action } } };
            Canvas.SetLeft(scroll, 220);
            scroll.ScrollChanged += (_, _) => { if (scroll.Offset.Y > 0) oldHost.Child = null; };
            WithWindow(new Canvas { Children = { live, oldHost, scroll } }, window =>
            {
                var findings = ReachabilityAssertions.InspectQuick(window, new("recycled", "small", 100, "light"));
                Assert.Null(oldHost.Child);
                Assert.DoesNotContain(findings, finding => finding.Check == LayoutCheckNames.OverlappingText);
            });
        });
    }

    [Fact]
    public void ScrollableListContentsInsideTheirViewportDoNotOverflowThePage()
    {
        avalonia.Invoke(() =>
        {
            var list = new ListBox { Width = 120, Height = 120,
                ItemsSource = new[] { new Button { Content = "Wide row", Width = 500 } } };
            ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Auto);
            var host = new Panel { Name = "PageHost", Children = { new UserControl { Content = list } } };
            WithWindow(host, window =>
            {
                var scroll = Assert.Single(list.GetVisualDescendants().OfType<ScrollViewer>());
                Assert.True(scroll.Extent.Width > scroll.Viewport.Width);
                Assert.Empty(ReachabilityAssertions.PageHorizontalScrollFindings(window,
                    new("list", "small", 100, "light"), window));
            });
        });
    }

    [Fact]
    public void DecorativePartIdsDoNotDefineInteractiveIdentity()
    {
        avalonia.Invoke(() =>
        {
            var first = new TextBlock { Text = "One" };
            var second = new TextBlock { Text = "Two" };
            AutomationProperties.SetAutomationId(first, "glyph-part");
            AutomationProperties.SetAutomationId(second, "glyph-part");
            WithWindow(new StackPanel { Children = { first, second } }, window => Assert.Empty(
                ReachabilityAssertions.AutomationFindings([first, second], new("decorative", "small", 100, "light"), window)));
        });
    }

    internal static void Settle(Window window)
    {
        for (var pass = 0; pass < 3; pass++)
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
        }
    }

    private static void WithWindow(Control content, Action<Window> assertion)
    {
        var window = new MotifTestWindow { Content = content, Width = 320, Height = 160 };
        try
        {
            window.Show();
            Settle(window);
            Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            assertion(window);
        }
        finally { window.Close(); }
    }
}
