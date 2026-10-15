using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using SIL.Motif.App;
using SIL.Motif.App.Views.Shell;
using SIL.Motif.Tests.App.Walkthrough;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class FooterHeightBudgetTests(AvaloniaHeadlessFixture avalonia)
{
    private const double FooterHeightCap = 240;

    public static TheoryData<int, int, string> MinimumWindowCases => new()
    {
        { 600, 100, "light" },
        { 600, 100, "dark" },
        { 600, 150, "light" },
        { 600, 150, "dark" },
        { 480, 100, "light" },
        { 480, 100, "dark" },
        { 480, 150, "light" },
        { 480, 150, "dark" },
    };

    [Theory]
    [MemberData(nameof(MinimumWindowCases))]
    public void LongFooterLeavesThePageUsableAndScrollsToDisabledApply(int height, int zoom, string theme)
    {
        avalonia.Invoke(() =>
        {
            var application = Application.Current!;
            var originalTheme = application.RequestedThemeVariant;
            Window? window = null;
            try
            {
                application.RequestedThemeVariant = theme == "dark" ? ThemeVariant.Dark : ThemeVariant.Light;
                var scale = zoom / 100d;
                var host = new WorkspaceLayoutHost { Page = new Border { Child = new TextBlock { Text = "Working page" } } };
                window = Show(host, 640, height, scale);
                host.UpdateViewport(window.ClientSize.Width / scale);
                var footer = CreateFooter(false, out var apply, out var reason, out var context);
                host.SetPagePanels(new PaneProvider(footer));
                PageScreenshots.Settle(window);

                var caseName = $"640-{height}-{zoom}-{theme}";
                SaveCaptureIfRequested(window, $"footer-at-top-{caseName}.png");
                var availableHeight = window.ClientSize.Height / scale;
                var expectedBudget = Math.Min(FooterHeightCap, Math.Max(0, (availableHeight - (double)host.FindResource("Component.Shell.WorkingPageMinHeight")!) / 2));
                var pageViewport = host.FindControl<ContentControl>("WorkingPageHost")!;
                Assert.True(pageViewport.Bounds.Height >= expectedBudget - 2,
                    $"The working page has {pageViewport.Bounds.Height:0.#} DIP beside a {footer.Bounds.Height:0.#} DIP footer at " +
                    $"{window.ClientSize.Width}×{window.ClientSize.Height}, {zoom}%.");

                var pageFooterViewport = Assert.IsType<ScrollViewer>(host.FindControl<ScrollViewer>("PageActionFooterViewport"));
                var inspectorFooterViewport = Assert.IsType<ScrollViewer>(host.FindControl<ScrollViewer>("InspectorFooterViewport"));
                Assert.Equal(expectedBudget, pageFooterViewport.MaxHeight);
                Assert.InRange(pageFooterViewport.Bounds.Height, expectedBudget - 2, expectedBudget + 2);
                Assert.True(pageFooterViewport.IsEffectivelyVisible);
                Assert.False(inspectorFooterViewport.IsEffectivelyVisible);
                Assert.Same(footer, host.FindControl<ContentControl>("PageActionFooterHost")!.Content);
                Assert.Null(host.FindControl<ContentControl>("InspectorFooterHost")!.Content);
                Assert.Same(context, apply.DataContext);
                Assert.Same(context, reason.DataContext);
                Assert.False(apply.IsEffectivelyEnabled);
                Assert.Equal(ScrollBarVisibility.Disabled,
                    ScrollViewer.GetHorizontalScrollBarVisibility(pageFooterViewport));
                Assert.Equal(ScrollBarVisibility.Auto,
                    ScrollViewer.GetVerticalScrollBarVisibility(pageFooterViewport));
                Assert.True(pageFooterViewport.Extent.Height > pageFooterViewport.Viewport.Height);

                pageFooterViewport.Offset = new Vector(pageFooterViewport.Offset.X, pageFooterViewport.Extent.Height);
                PageScreenshots.Settle(window);
                SaveCaptureIfRequested(window, $"footer-apply-reason-{caseName}.png");
                Assert.True(pageFooterViewport.Offset.Y > 0);
                Assert.True(apply.IsEffectivelyVisible);
                Assert.True(reason.IsEffectivelyVisible);
                AssertInside(pageFooterViewport, apply, window);
                AssertInside(pageFooterViewport, reason, window);
                LayoutAssertions.AssertCurrent(window);

                HeadlessClick.Click(window, host.FindControl<Button>("InspectorStrip")!, "Show Inspector");
                PageScreenshots.Settle(window);
                AssertFooterInOneHost(host, footer, inspectorVisible: true);
                Assert.True(inspectorFooterViewport.IsEffectivelyVisible);
                Assert.False(pageFooterViewport.IsEffectivelyVisible);
                Assert.Equal(expectedBudget, inspectorFooterViewport.MaxHeight);
                Assert.InRange(inspectorFooterViewport.Bounds.Height, expectedBudget - 2, expectedBudget + 2);
                Assert.Equal(ScrollBarVisibility.Auto,
                    ScrollViewer.GetVerticalScrollBarVisibility(inspectorFooterViewport));
                var inspectorPane = host.FindControl<Border>("InspectorPane")!;
                var header = Assert.Single(inspectorPane.GetVisualDescendants().OfType<Border>(),
                    border => border.Classes.Contains("workspacePaneHeader"));
                Assert.True(inspectorPane.Bounds.Height - header.Bounds.Height - inspectorFooterViewport.Bounds.Height >= 80,
                    "The Inspector footer leaves no useful body below its header.");

                HeadlessClick.Click(window, host.FindControl<Button>("InspectorHideButton")!, "Hide Inspector");
                PageScreenshots.Settle(window);
                AssertFooterInOneHost(host, footer, inspectorVisible: false);
                Assert.Same(context, apply.DataContext);
            }
            finally
            {
                window?.Close();
                application.RequestedThemeVariant = originalTheme;
            }
        });
    }

    [Theory]
    [InlineData(100)]
    [InlineData(150)]
    public void EnabledApplyReceivesInputAfterScrollingToTheEnd(int zoom)
    {
        avalonia.Invoke(() =>
        {
            var host = new WorkspaceLayoutHost { Page = new Border() };
            var window = Show(host, 640, 600, zoom / 100d);
            try
            {
                host.UpdateViewport(window.ClientSize.Width / (zoom / 100d));
                var footer = CreateFooter(true, out var apply, out _, out _);
                var applied = false;
                apply.Click += (_, _) => applied = true;
                host.SetPagePanels(new PaneProvider(footer));
                PageScreenshots.Settle(window);

                var viewport = Assert.IsType<ScrollViewer>(host.FindControl<ScrollViewer>("PageActionFooterViewport"));
                viewport.Offset = new Vector(viewport.Offset.X, viewport.Extent.Height);
                PageScreenshots.Settle(window);
                AssertInside(viewport, apply, window);
                HeadlessClick.Click(window, apply, "Apply after scrolling");
                Assert.True(applied);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void WideInspectorFooterStopsAtItsTokenCap()
    {
        avalonia.Invoke(() =>
        {
            var host = new WorkspaceLayoutHost { Page = new Border() };
            var window = Show(host, 1920, 1080, 1);
            try
            {
                host.UpdateViewport(window.ClientSize.Width);
                var footer = CreateFooter(false, out var apply, out var reason, out _);
                host.SetPagePanels(new PaneProvider(footer));
                PageScreenshots.Settle(window);

                var viewport = Assert.IsType<ScrollViewer>(host.FindControl<ScrollViewer>("InspectorFooterViewport"));
                Assert.True(viewport.IsEffectivelyVisible);
                Assert.Equal(240d, viewport.MaxHeight);
                Assert.Equal(240d, viewport.Bounds.Height);
                viewport.Offset = new Vector(viewport.Offset.X, viewport.Extent.Height);
                PageScreenshots.Settle(window);
                SaveCaptureIfRequested(window, "footer-wide-1920-1080.png");
                AssertInside(viewport, apply, window);
                AssertInside(viewport, reason, window);
                LayoutAssertions.AssertCurrent(window);
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static Window Show(WorkspaceLayoutHost host, int width, int height, double scale)
    {
        var window = new Window
        {
            Width = width,
            Height = height,
            Content = new LayoutTransformControl
            {
                LayoutTransform = new ScaleTransform(scale, scale),
                Child = host,
            },
        };
        window.Show();
        PageScreenshots.Settle(window);
        Assert.Equal(width, window.ClientSize.Width);
        Assert.Equal(height, window.ClientSize.Height);
        return window;
    }

    private static StackPanel CreateFooter(bool enableApply, out Button apply, out TextBlock reason, out object context)
    {
        context = new object();
        var footer = new StackPanel { DataContext = context };
        const string refusal = "The project has changed since these checks were prepared. Review the explanation and " +
            "confirm that the selected grammar and current project still describe the same work. ";
        for (var index = 0; index < 14; index++)
            footer.Children.Add(new TextBlock
            {
                Text = string.Concat(Enumerable.Repeat(refusal, 3)),
                TextWrapping = TextWrapping.Wrap,
            });
        apply = new Button { Name = "FooterApply", Content = "Apply", IsEnabled = enableApply };
        reason = new TextBlock
        {
            Name = "FooterApplyReason",
            Text = "Apply is disabled until every explanation has been reviewed and the project is checked again.",
            TextWrapping = TextWrapping.Wrap,
        };
        footer.Children.Add(apply);
        footer.Children.Add(reason);
        return footer;
    }

    private static void AssertFooterInOneHost(WorkspaceLayoutHost host, Control footer, bool inspectorVisible)
    {
        var pageContent = host.FindControl<ContentControl>("PageActionFooterHost")!.Content;
        var inspectorContent = host.FindControl<ContentControl>("InspectorFooterHost")!.Content;
        Assert.Equal(inspectorVisible, ReferenceEquals(footer, inspectorContent));
        Assert.Equal(!inspectorVisible, ReferenceEquals(footer, pageContent));
        Assert.NotSame(pageContent, inspectorContent);
    }

    private static void AssertInside(ScrollViewer viewport, Control control, Window window)
    {
        Assert.True(control.IsEffectivelyVisible, $"{control.Name ?? control.GetType().Name} is not visible.");
        var presenter = Assert.Single(viewport.GetVisualDescendants().OfType<ScrollContentPresenter>());
        AssertRectContains(BoundsInWindow(presenter, window), BoundsInWindow(control, window), control.Name);
        var bounds = BoundsInWindow(control, window);
        Assert.True(bounds.Left >= -1 && bounds.Top >= -1 && bounds.Right <= window.ClientSize.Width + 1 &&
            bounds.Bottom <= window.ClientSize.Height + 1,
            $"{control.Name ?? control.GetType().Name} is outside the window client area: {bounds}.");
    }

    private static Rect BoundsInWindow(Control control, Window window)
    {
        var points = new[]
        {
            new Point(0, 0),
            new Point(control.Bounds.Width, 0),
            new Point(0, control.Bounds.Height),
            new Point(control.Bounds.Width, control.Bounds.Height),
        }.Select(point => control.TranslatePoint(point, window)
            ?? throw new InvalidOperationException($"{control.Name ?? control.GetType().Name} is detached."))
            .ToArray();
        var left = points.Min(point => point.X);
        var top = points.Min(point => point.Y);
        var right = points.Max(point => point.X);
        var bottom = points.Max(point => point.Y);
        return new Rect(left, top, right - left, bottom - top);
    }

    private static void AssertRectContains(Rect container, Rect content, string? name)
    {
        Assert.True(container.Left <= content.Left + 1 && container.Top <= content.Top + 1 &&
            container.Right >= content.Right - 1 && container.Bottom >= content.Bottom - 1,
            $"{name ?? "Control"} is outside the clipped footer viewport: {content}; viewport: {container}.");
    }

    private static void SaveCaptureIfRequested(Window window, string fileName)
    {
        var folder = Environment.GetEnvironmentVariable(ScreenshotFactAttribute.FolderVariable);
        if (string.IsNullOrWhiteSpace(folder)) return;
        Directory.CreateDirectory(folder);
        using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException($"No frame rendered for {fileName}.");
        frame.Save(Path.Combine(folder, fileName), PngBitmapEncoderOptions.Default);
    }

    private sealed class PaneProvider(Control footer) : IWorkspacePaneProvider
    {
        public string SidePanelTitle => "Details";
        public Control? SidePanel => null;
        public Control InspectorFooter => footer;
    }
}
