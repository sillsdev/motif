using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.VisualTree;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.App.Views.Shell;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class WorkspacePaneHostTests(AvaloniaHeadlessFixture avalonia)
{
    [Fact]
    public void ForPageAttachesThePageAndItsExistingPanesWithTheirDataContext()
    {
        avalonia.Invoke(() =>
        {
            var page = new PanePage();

            var host = WorkspaceLayoutHost.ForPage(page);

            Assert.Same(page, host.Page);
            Assert.Same(page.Side, host.FindControl<ContentControl>("SidePanelContent")!.Content);
            Assert.Same(page.Inspector, host.FindControl<ContentControl>("PageInspectorHost")!.Content);
            Assert.Same(page.Footer, host.FindControl<ContentControl>("InspectorFooterHost")!.Content);
            Assert.Same(page.DataContext, page.Side.DataContext);
            Assert.Same(page.DataContext, page.Inspector.DataContext);
            Assert.Same(page.DataContext, page.Footer.DataContext);
        });
    }

    [Fact]
    public void ForPageTracksLogicalWidthAcrossResizesAtBothZoomLevelsAndMovesOneFooter()
    {
        avalonia.Invoke(() =>
        {
            var page = new PanePage();
            var host = WorkspaceLayoutHost.ForPage(page);
            var transform = new LayoutTransformControl
            {
                LayoutTransform = new ScaleTransform(1, 1),
                Child = host,
            };
            var window = new MotifTestWindow { Width = 1300, Height = 800, Content = transform };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);

                AssertLogicalWidth(host, 1300);
                AssertPaneVisibility(host, sidePanel: true, inspector: true);
                AssertFooterParent(host, page.Footer, "InspectorFooterHost");

                window.Width = 1100;
                PageScreenshots.Settle(window);
                AssertLogicalWidth(host, 1100);
                AssertPaneVisibility(host, sidePanel: false, inspector: true);
                AssertFooterParent(host, page.Footer, "InspectorFooterHost");

                transform.LayoutTransform = new ScaleTransform(1.5, 1.5);
                PageScreenshots.Settle(window);
                AssertLogicalWidth(host, window.ClientSize.Width / 1.5);
                AssertPaneVisibility(host, sidePanel: false, inspector: false);
                AssertFooterParent(host, page.Footer, "PageActionFooterHost");

                window.Width = 1500;
                PageScreenshots.Settle(window);
                AssertLogicalWidth(host, 1000);
                AssertPaneVisibility(host, sidePanel: false, inspector: true);
                AssertFooterParent(host, page.Footer, "InspectorFooterHost");

                window.Width = 1800;
                PageScreenshots.Settle(window);
                AssertLogicalWidth(host, 1200);
                AssertPaneVisibility(host, sidePanel: true, inspector: true);
                AssertFooterParent(host, page.Footer, "InspectorFooterHost");
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void NavigationReleasesPreviousPanesAndReusesThemWhenReturning()
    {
        avalonia.Invoke(() =>
        {
            var first = new PanePage();
            var second = new PanePage();
            var host = WorkspaceLayoutHost.ForPage(first);
            var window = new MotifTestWindow { Width = 1600, Height = 800, Content = host };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);
                AssertPageAttached(host, first);

                host.Page = second;
                host.SetPagePanels(second);
                PageScreenshots.Settle(window);

                AssertPanesDetached(first);
                AssertPageAttached(host, second);

                host.Page = first;
                host.SetPagePanels(first);
                PageScreenshots.Settle(window);

                AssertPanesDetached(second);
                AssertPageAttached(host, first);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void DetachingAndReattachingTracksTheNewWidthWithoutReparentingGetterResults()
    {
        avalonia.Invoke(() =>
        {
            var page = new PanePage();
            var host = WorkspaceLayoutHost.ForPage(page);
            var window = new MotifTestWindow { Width = 1600, Height = 800, Content = host };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);
                AssertPageAttached(host, page);

                var sideParent = page.Side.Parent;
                var inspectorParent = page.Inspector.Parent;
                var footerParent = page.Footer.Parent;
                Assert.Same(page.Side, page.SidePanel);
                Assert.Same(page.Inspector, page.InspectorContent);
                Assert.Same(page.Footer, page.InspectorFooter);
                Assert.Same(sideParent, page.Side.Parent);
                Assert.Same(inspectorParent, page.Inspector.Parent);
                Assert.Same(footerParent, page.Footer.Parent);

                window.Content = new Border();
                window.Width = 800;
                PageScreenshots.Settle(window);
                window.Content = host;
                PageScreenshots.Settle(window);

                AssertLogicalWidth(host, 800);
                AssertPaneVisibility(host, sidePanel: false, inspector: false);
                AssertFooterParent(host, page.Footer, "PageActionFooterHost");
                Assert.Same(host.FindControl<ContentControl>("SidePanelContent"), page.Side.Parent);
                Assert.Same(host.FindControl<ContentControl>("PageInspectorHost"), page.Inspector.Parent);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void TextsSelectorIsUnparentedBeforeItsGetterAndAttachesInStandaloneAndMainWindowHosts()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await PageScreenshots.OpenOverSampleData(parse: false);
            try
            {
                var texts = Assert.Single(window.GetVisualDescendants().OfType<TextsPage>());
                var selection = texts.FindControl<ContentControl>("SelectionHost")!;
                Assert.Null(selection.Parent);
                Assert.Same(texts.DataContext, selection.DataContext);

                _ = texts.SidePanel;
                var parentBeforeRepeatedGetter = selection.Parent;
                Assert.Same(selection, texts.SidePanel);
                Assert.Same(parentBeforeRepeatedGetter, selection.Parent);
                Assert.Null(selection.Parent);

                var standalonePage = new TextsPage(workspace.PageModel<TextsPageModel>());
                var standaloneSelection = standalonePage.FindControl<ContentControl>("SelectionHost")!;
                Assert.Null(standaloneSelection.Parent);
                Assert.Same(standalonePage.DataContext, standaloneSelection.DataContext);
                var standaloneHost = WorkspaceLayoutHost.ForPage(standalonePage);
                Assert.Same(standalonePage, standaloneHost.Page);
                Assert.Same(standaloneSelection,
                    standaloneHost.FindControl<ContentControl>("SidePanelContent")!.Content);
                Assert.Same(standalonePage.DataContext, standaloneSelection.DataContext);
                Assert.Same(standaloneSelection, standalonePage.SidePanel);
                var standaloneParent = standaloneSelection.Parent;
                Assert.Same(standaloneSelection, standalonePage.SidePanel);
                Assert.Same(standaloneParent, standaloneSelection.Parent);

                workspace.CurrentPage = WorkspacePage.Texts;
                PageScreenshots.Settle(window);
                var mainHost = window.FindControl<WorkspaceLayoutHost>("WorkspaceLayout")!;
                Assert.Same(selection, mainHost.FindControl<ContentControl>("SidePanelContent")!.Content);
                Assert.Same(texts.DataContext, selection.DataContext);
                var mainWindowParent = selection.Parent;
                Assert.Same(selection, texts.SidePanel);
                Assert.Same(mainWindowParent, selection.Parent);
            }
            finally
            {
                window.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void ForPageRejectsANullPage()
    {
        avalonia.Invoke(() => Assert.Throws<ArgumentNullException>(() => WorkspaceLayoutHost.ForPage(null!)));
    }

    private static void AssertPageAttached(WorkspaceLayoutHost host, PanePage page)
    {
        Assert.Same(page.Side, host.FindControl<ContentControl>("SidePanelContent")!.Content);
        Assert.Same(page.Inspector, host.FindControl<ContentControl>("PageInspectorHost")!.Content);
        AssertFooterParent(host, page.Footer, "InspectorFooterHost");
        Assert.Same(host.FindControl<ContentControl>("SidePanelContent"), page.Side.Parent);
        Assert.Same(host.FindControl<ContentControl>("PageInspectorHost"), page.Inspector.Parent);
    }

    private static void AssertPanesDetached(PanePage page)
    {
        Assert.Null(page.Side.Parent);
        Assert.Null(page.Inspector.Parent);
        Assert.Null(page.Footer.Parent);
    }

    private static void AssertPaneVisibility(WorkspaceLayoutHost host, bool sidePanel, bool inspector)
    {
        Assert.Equal(sidePanel, host.FindControl<Border>("SidePanelBorder")!.IsVisible);
        Assert.Equal(inspector, host.FindControl<Border>("InspectorPane")!.IsVisible);
    }

    private static void AssertFooterParent(WorkspaceLayoutHost host, Control footer, string parentName)
    {
        var parent = host.FindControl<ContentControl>(parentName)!;
        var otherName = parentName == "InspectorFooterHost" ? "PageActionFooterHost" : "InspectorFooterHost";
        Assert.Same(footer, parent.Content);
        Assert.Null(host.FindControl<ContentControl>(otherName)!.Content);
        Assert.Same(parent, footer.Parent);
    }

    private static void AssertLogicalWidth(WorkspaceLayoutHost host, double width) =>
        Assert.InRange(Math.Abs(host.Bounds.Width - width), 0, 1);

    private sealed class PanePage : UserControl, IWorkspacePaneProvider
    {
        public PanePage()
        {
            DataContext = new object();
            Side = Pane(DataContext);
            Inspector = Pane(DataContext);
            Footer = Pane(DataContext);
        }

        public Control Side { get; }
        public Control Inspector { get; }
        public Control Footer { get; }

        public Control? SidePanel => Side;
        public Control? InspectorContent => Inspector;
        public Control? InspectorFooter => Footer;

        private static Control Pane(object? dataContext) => new Border { DataContext = dataContext };
    }
}
