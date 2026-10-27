using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
[Trait("MotifTestLevel", "System")]
public sealed class TimingResponsiveTests
{
    [ScreenshotFact]
    public void SelectedRuleAndPrimaryActionsRemainReachableAtCompactSizes()
    {
        var folder = Environment.GetEnvironmentVariable(ScreenshotFactAttribute.FolderVariable)!;
        Directory.CreateDirectory(folder);

        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await PageScreenshots.OpenOverSampleData(
                configure: OverviewTimingScreenshots.ReadOverviewAndTiming);
            var settings = SettingsTestAccess.Model(window);
            workspace.CurrentPage = WorkspacePage.Timing;
            PageScreenshots.Settle(window);
            var page = window.PageView<TimingPage>();
            var headline = Assert.Single(page.GetLogicalDescendants().OfType<Panel>(),
                panel => panel.Classes.Contains("timingHeadline"));
            var pageBorder = Assert.Single(page.GetLogicalDescendants().OfType<Border>(),
                border => border.Classes.Contains("timingPage"));
            var shareLayout = page.FindControl<Grid>("TimingShareLayout")!;
            var sidePanel = page.FindControl<StackPanel>("TimingSidePanel")!;
            var ruleRows = page.FindControl<ItemsControl>("TimingPageRuleRowsItems")!;
            var primaryActions = page.GetLogicalDescendants().OfType<Button>()
                .Where(button => AutomationProperties.GetName(button) is
                    "All timed words" or "Words from a Matrix cell" or "Words from a list" or
                    "AI Handoff for these words" or "More ways to choose words")
                .ToArray();
            var callsButton = page.GetLogicalDescendants().OfType<Button>()
                .Single(button => AutomationProperties.GetName(button) == "Show calls per kind");
            var ruleHandoff = page.GetLogicalDescendants().OfType<Button>()
                .Single(button => button.Content?.ToString() == MotifAppStrings.AIHandoffForThisRule);
            var pageScroll = Assert.IsType<ScrollViewer>(pageBorder.FindAncestorOfType<ScrollViewer>());

            Assert.Equal(5, primaryActions.Length);
            Assert.True(page.Page.HasRule, "The sample Timing page must show a selected rule.");
            Assert.True(page.Page.HasRuleDetail, "The selected rule must have its detail loaded.");

            try
            {
                foreach (var (theme, variant) in new[]
                         {
                             ("light", ThemeVariant.Light),
                             ("dark", ThemeVariant.Dark),
                         })
                {
                    Application.Current!.RequestedThemeVariant = variant;
                    foreach (var height in new[] { 600, 900 })
                    foreach (var zoom in new[] { 100, 150 })
                    {
                        window.Width = 640;
                        window.Height = height;
                        settings.ZoomPercent = zoom;
                        pageScroll.Offset = new Vector(0, 0);
                        PageScreenshots.Settle(window);

                        Assert.Equal(zoom / 100d, settings.ZoomScale);
                        Assert.Equal(640, Math.Round(window.ClientSize.Width));
                        Assert.Equal(height, Math.Round(window.ClientSize.Height));
                        Assert.Equal(height / settings.ZoomScale < 600,
                            window.FindControl<ListBox>("PageList")!.Classes.Contains("shortRail"));
                        Assert.True(page.Page.HasRule);
                        Assert.True(page.Page.HasRuleDetail);

                        var content = ContentBounds(pageBorder, window);
                        foreach (var child in headline.Children.OfType<Control>().Where(child => child.IsEffectivelyVisible))
                            AssertInsideHorizontally(child, content, window, $"{height}/{zoom}/{theme} headline");
                        foreach (var action in primaryActions)
                        {
                            AssertInsideHorizontally(action, content, window, $"{height}/{zoom}/{theme} action");
                            AssertHitTarget(window, action, $"{height}/{zoom}/{theme} action");
                        }

                        var ruleTableBounds = WindowBounds(ruleRows, window);
                        Assert.True(ruleTableBounds.Left >= content.Left - 0.5 &&
                                    ruleTableBounds.Right <= content.Right + 0.5,
                            $"{height}/{zoom}/{theme} rule table {ruleTableBounds} exceeds Timing content {content}.");
                        Assert.True(shareLayout.Bounds.Width < 900,
                            $"{height}/{zoom}/{theme} Timing share layout unexpectedly measures {shareLayout.Bounds.Width:F1} px.");
                        Assert.Equal(1, Grid.GetRow(sidePanel));
                        Assert.True(WindowBounds(sidePanel, window).Top >= ruleTableBounds.Bottom - 0.5,
                            $"{height}/{zoom}/{theme} selected-rule details are not below the rule grid.");

                        using (var frame = window.CaptureRenderedFrame()
                            ?? throw new InvalidOperationException("No rendered Timing frame was available."))
                            frame.Save(Path.Combine(folder, $"timing-640-{height}-{zoom}-{theme}.png"),
                                PngBitmapEncoderOptions.Default);

                        BringIntoViewAndAssertHit(window, callsButton, pageScroll,
                            $"{height}/{zoom}/{theme} kind calls action");
                        window.MouseDown(WindowBounds(callsButton, window).Center, MouseButton.Left);
                        window.MouseUp(WindowBounds(callsButton, window).Center, MouseButton.Left);
                        PageScreenshots.Settle(window);
                        Assert.True(page.Page.ShowKindCalls,
                            $"{height}/{zoom}/{theme} kind calls action did not open its detail.");
                        Assert.True(page.FindControl<ItemsControl>("TimingPageKindCallSharesItems")!.IsVisible);
                        window.MouseDown(WindowBounds(callsButton, window).Center, MouseButton.Left);
                        window.MouseUp(WindowBounds(callsButton, window).Center, MouseButton.Left);
                        PageScreenshots.Settle(window);
                        Assert.False(page.Page.ShowKindCalls,
                            $"{height}/{zoom}/{theme} kind calls action did not close its detail.");

                        BringIntoViewAndAssertHit(window, ruleHandoff, pageScroll,
                            $"{height}/{zoom}/{theme} selected-rule action");
                        using (var frame = window.CaptureRenderedFrame()
                            ?? throw new InvalidOperationException("No rendered selected-rule frame was available."))
                            frame.Save(Path.Combine(folder, $"timing-640-{height}-{zoom}-{theme}-reflow.png"),
                                PngBitmapEncoderOptions.Default);
                    }
                }
            }
            finally
            {
                Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
                settings.ZoomPercent = ZoomPolicy.DefaultPercent;
                WindowZoomPolicy.Apply(Application.Current!, ZoomPolicy.DefaultPercent);
                window.Close();
            }
        }, TimeSpan.FromMinutes(4));
    }

    private static Rect ContentBounds(Border border, Window window)
    {
        var first = border.TranslatePoint(new Point(border.Padding.Left, border.Padding.Top), window)!.Value;
        var last = border.TranslatePoint(new Point(border.Bounds.Width - border.Padding.Right,
            border.Bounds.Height - border.Padding.Bottom), window)!.Value;
        return new Rect(first, last);
    }

    private static Rect WindowBounds(Control control, Window window)
    {
        var points = new[]
        {
            new Point(0, 0),
            new Point(control.Bounds.Width, 0),
            new Point(0, control.Bounds.Height),
            new Point(control.Bounds.Width, control.Bounds.Height),
        }.Select(point => control.TranslatePoint(point, window)!.Value).ToArray();
        var left = points.Min(point => point.X);
        var top = points.Min(point => point.Y);
        var right = points.Max(point => point.X);
        var bottom = points.Max(point => point.Y);
        return new Rect(left, top, right - left, bottom - top);
    }

    private static void AssertInsideHorizontally(Control control, Rect content, Window window, string state)
    {
        var bounds = WindowBounds(control, window);
        Assert.True(bounds.Left >= content.Left - 0.5 && bounds.Right <= content.Right + 0.5,
            $"{state}: {control.GetType().Name} {bounds} exceeds Timing content {content}.");
    }

    private static void BringIntoViewAndAssertHit(Window window, Control control, ScrollViewer viewport, string state)
    {
        control.BringIntoView();
        PageScreenshots.Settle(window);
        Assert.True(control.IsEffectivelyVisible && control.IsHitTestVisible,
            $"{state}: the control is hidden or cannot receive input.");
        var bounds = WindowBounds(control, window);
        var visible = WindowBounds(viewport, window);
        Assert.True(bounds.Left >= visible.Left - 0.5 && bounds.Right <= visible.Right + 0.5 &&
                    bounds.Top >= visible.Top - 0.5 && bounds.Bottom <= visible.Bottom + 0.5,
            $"{state}: control {bounds} is outside the page viewport {visible}.");
        AssertHitTarget(window, control, state);
    }

    private static void AssertHitTarget(Window window, Control control, string state)
    {
        var bounds = WindowBounds(control, window);
        var center = bounds.Center;
        Assert.True(center.X >= 0 && center.Y >= 0 && center.X < window.ClientSize.Width &&
                    center.Y < window.ClientSize.Height,
            $"{state}: input point {center} is outside the client area {window.ClientSize}.");
        var hit = window.InputHitTest(center) as Visual;
        Assert.True(hit?.GetSelfAndVisualAncestors().Contains(control) == true,
            $"{state}: input at {center} does not reach {control.GetType().Name}.");
    }
}
