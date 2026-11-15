using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using SIL.Motif.App.Views.Shell;
using SIL.Motif.Tests.App.Walkthrough;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class PageInspectorArbitrationTests(AvaloniaHeadlessFixture avalonia)
{
    [Fact]
    public void PageDetailsReopenAtNarrowWidthAndMoveWithTheirOwner()
    {
        avalonia.Invoke(() =>
        {
            var first = new DetailsPage();
            var second = new DetailsPage();
            var host = WorkspaceLayoutHost.ForPage(first);
            var transform = new LayoutTransformControl
            {
                LayoutTransform = new ScaleTransform(1.5, 1.5),
                Child = host,
            };
            var window = new MotifTestWindow { Width = 960, Height = 600, Content = transform };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);
                Assert.InRange(Math.Abs(host.Bounds.Width - 640), 0, 1);
                Assert.False(host.FindControl<Border>("InspectorPane")!.IsVisible);
                Assert.Null(host.FindControl<ContentControl>("ObjectInspectorHost"));

                WorkspaceLayoutHost.RequestPageInspector(first.RequestOrigin);
                PageScreenshots.Settle(window);
                Assert.True(host.FindControl<Border>("InspectorPane")!.IsVisible);
                Assert.Same(first.Details, host.FindControl<ContentControl>("PageInspectorHost")!.Content);
                AssertFooterParent(host, first.Footer, "InspectorFooterHost");

                HeadlessClick.Click(window, host.FindControl<Button>("InspectorHideButton")!, "Back to Page");
                PageScreenshots.Settle(window);
                AssertFooterParent(host, first.Footer, "PageActionFooterHost");
                host.Page = second;
                host.SetPagePanels(second);
                PageScreenshots.Settle(window);
                Assert.Null(first.Details.Parent);
                Assert.Null(first.Footer.Parent);
                AssertFooterParent(host, second.Footer, "PageActionFooterHost");
                Assert.Same(second.Context, second.Footer.DataContext);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void DetailsRequestBubblesAndRejectsNullOrigin()
    {
        avalonia.Invoke(() =>
        {
            var page = new DetailsPage();
            var host = WorkspaceLayoutHost.ForPage(page);
            var window = new MotifTestWindow { Width = 1300, Height = 800, Content = host };
            RoutedEventArgs? requested = null;
            host.PageInspectorRequested += (_, args) => requested = args;
            try
            {
                window.Show();
                PageScreenshots.Settle(window);
                WorkspaceLayoutHost.RequestPageInspector(page.RequestOrigin);
                Assert.NotNull(requested);
                Assert.False(requested.Handled);
                Assert.Same(page.Details, host.FindControl<ContentControl>("PageInspectorHost")!.Content);
                Assert.Throws<ArgumentNullException>(() => WorkspaceLayoutHost.RequestPageInspector(null!));
            }
            finally { window.Close(); }
        });
    }

    private static void AssertFooterParent(WorkspaceLayoutHost host, Control footer, string parentName)
    {
        var parent = host.FindControl<ContentControl>(parentName)!;
        var otherName = parentName == "InspectorFooterHost" ? "PageActionFooterHost" : "InspectorFooterHost";
        Assert.Same(footer, parent.Content);
        Assert.Null(host.FindControl<ContentControl>(otherName)!.Content);
        Assert.Same(parent, footer.Parent);
    }

    private sealed class DetailsPage : UserControl, IWorkspacePaneProvider
    {
        public object Context { get; } = new();
        public Border Details { get; } = new();
        public Border Footer { get; } = new();
        public Button RequestOrigin { get; } = new() { Content = "Details" };

        public DetailsPage()
        {
            DataContext = Context;
            Details.DataContext = Context;
            Footer.DataContext = Context;
            Content = new Grid { Children = { RequestOrigin } };
        }

        public Control? InspectorContent => Details;
        public Control? InspectorFooter => Footer;
    }
}
