using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using SIL.Motif.App.Controls;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.App.Views.Shell;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.App.Walkthrough;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class PageInspectorArbitrationTests(AvaloniaHeadlessFixture avalonia)
{
    [Fact]
    public void AnOpenObjectInspectorTakesPrecedenceOverPageDetails()
    {
        avalonia.Invoke(() =>
        {
            var page = new InspectorPage();
            var host = WorkspaceLayoutHost.ForPage(page);
            var objectInspector = new Border { DataContext = new object() };
            host.ObjectInspector = objectInspector;
            var window = new MotifTestWindow { Width = 1300, Height = 800, Content = host };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);
                Assert.True(host.FindControl<ContentControl>("PageInspectorHost")!.IsVisible);
                Assert.False(host.FindControl<ContentControl>("ObjectInspectorHost")!.IsVisible);
                AssertFooterParent(host, page.Footer, "InspectorFooterHost");

                host.SetObjectInspectorOpen(true);
                PageScreenshots.Settle(window);
                CaptureCurrentState(window, "object-priority-page-object-fixture.png");

                Assert.False(host.FindControl<ContentControl>("PageInspectorHost")!.IsVisible);
                Assert.True(host.FindControl<ContentControl>("ObjectInspectorHost")!.IsVisible);
                Assert.False(host.FindControl<TextBlock>("InspectorEmpty")!.IsVisible);
                Assert.Same(objectInspector, host.FindControl<ContentControl>("ObjectInspectorHost")!.Content);
                AssertFooterParent(host, page.Footer, "InspectorFooterHost");

                host.SetObjectInspectorOpen(false);
                PageScreenshots.Settle(window);

                Assert.True(host.FindControl<ContentControl>("PageInspectorHost")!.IsVisible);
                Assert.False(host.FindControl<ContentControl>("ObjectInspectorHost")!.IsVisible);
                Assert.False(host.FindControl<TextBlock>("InspectorEmpty")!.IsVisible);
                AssertFooterParent(host, page.Footer, "InspectorFooterHost");
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void StandaloneHostReopensAt640LogicalWidthAndKeepsTheCurrentPageFooter()
    {
        avalonia.Invoke(() =>
        {
            const double scale = 1.5;
            var first = new InspectorPage();
            var second = new InspectorPage();
            var host = WorkspaceLayoutHost.ForPage(first);
            host.ObjectInspector = new Border { DataContext = new object() };
            var transform = new LayoutTransformControl
            {
                LayoutTransform = new ScaleTransform(scale, scale),
                Child = host,
            };
            var window = new MotifTestWindow { Width = 640 * scale, Height = 600, Content = transform };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);
                Assert.InRange(Math.Abs(host.Bounds.Width - 640), 0, 1);
                Assert.False(host.FindControl<Border>("InspectorPane")!.IsVisible);

                host.SetObjectInspectorOpen(true);
                host.ShowInspector();
                PageScreenshots.Settle(window);
                Assert.True(host.FindControl<Border>("InspectorPane")!.IsVisible);
                Assert.False(host.FindControl<ContentControl>("PageInspectorHost")!.IsVisible);
                Assert.True(host.FindControl<ContentControl>("ObjectInspectorHost")!.IsVisible);
                AssertFooterParent(host, first.Footer, "InspectorFooterHost");

                HeadlessClick.Click(window, host.FindControl<Button>("InspectorHideButton")!, "Hide Inspector");
                PageScreenshots.Settle(window);
                Assert.False(host.FindControl<Border>("InspectorPane")!.IsVisible);
                AssertFooterParent(host, first.Footer, "PageActionFooterHost");

                HeadlessClick.Click(window, host.FindControl<Button>("InspectorStrip")!, "Show Inspector");
                PageScreenshots.Settle(window);
                Assert.True(host.FindControl<Border>("InspectorPane")!.IsVisible);
                Assert.True(host.FindControl<ContentControl>("ObjectInspectorHost")!.IsVisible);
                AssertFooterParent(host, first.Footer, "InspectorFooterHost");

                host.Page = second;
                host.SetPagePanels(second);
                PageScreenshots.Settle(window);
                Assert.Null(first.Details.Parent);
                Assert.Null(first.Footer.Parent);
                Assert.False(host.FindControl<ContentControl>("PageInspectorHost")!.IsVisible);
                Assert.True(host.FindControl<ContentControl>("ObjectInspectorHost")!.IsVisible);
                AssertFooterParent(host, second.Footer, "InspectorFooterHost");

                host.SetObjectInspectorOpen(false);
                PageScreenshots.Settle(window);
                Assert.True(host.FindControl<ContentControl>("PageInspectorHost")!.IsVisible);
                Assert.False(host.FindControl<ContentControl>("ObjectInspectorHost")!.IsVisible);
                Assert.False(host.FindControl<Border>("InspectorPane")!.IsVisible);
                Assert.True(host.FindControl<Grid>("WorkingPageRegion")!.IsEffectivelyVisible);
                AssertFooterParent(host, second.Footer, "PageActionFooterHost");
                Assert.Same(second.Context, second.Footer.DataContext);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void StandalonePageInspectorRequestClearsTheObjectAndBubblesUnhandled()
    {
        avalonia.Invoke(() =>
        {
            var page = new InspectorPage();
            var host = WorkspaceLayoutHost.ForPage(page);
            host.ObjectInspector = new Border();
            var window = new MotifTestWindow { Width = 1300, Height = 800, Content = host };
            RoutedEventArgs? requested = null;
            host.PageInspectorRequested += (_, args) => requested = args;
            try
            {
                window.Show();
                PageScreenshots.Settle(window);
                host.SetObjectInspectorOpen(true);
                PageScreenshots.Settle(window);

                WorkspaceLayoutHost.RequestPageInspector(page.RequestOrigin);

                Assert.NotNull(requested);
                Assert.False(requested.Handled);
                Assert.True(host.FindControl<Border>("InspectorPane")!.IsVisible);
                Assert.True(host.FindControl<ContentControl>("PageInspectorHost")!.IsVisible);
                Assert.False(host.FindControl<ContentControl>("ObjectInspectorHost")!.IsVisible);
                Assert.False(host.FindControl<TextBlock>("InspectorEmpty")!.IsVisible);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void MainWindowInspectLinkReopensTheHiddenPaneForAnAlreadyOpenSampleObject()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = SampleInspectorClient();
            var (workspace, window) = FakeComposedWindow.Create(fake);
            var host = window.FindControl<WorkspaceLayoutHost>("WorkspaceLayout")!;
            try
            {
                workspace.CurrentPage = WorkspacePage.Texts;
                window.Width = 1300;
                window.Height = 800;
                window.Show();
                PageScreenshots.Settle(window);
                var page = InstallPageInput(window, SampleSubject());
                host.SetPagePanels(new InspectorPage());
                PageScreenshots.Settle(window);

                workspace.Context.OpenInspector(SampleSubject());
                await workspace.Inspector.Loading;
                PageScreenshots.Settle(window);
                Assert.True(workspace.Inspector.IsOpen);
                Assert.True(host.FindControl<Border>("InspectorPane")!.IsVisible);
                Assert.False(host.FindControl<ContentControl>("PageInspectorHost")!.IsVisible);
                Assert.True(host.FindControl<ContentControl>("ObjectInspectorHost")!.IsVisible);

                HeadlessClick.Click(window, host.FindControl<Button>("InspectorHideButton")!, "Hide Inspector");
                PageScreenshots.Settle(window);
                Assert.True(workspace.Inspector.IsOpen);
                Assert.False(host.FindControl<Border>("InspectorPane")!.IsVisible);

                HeadlessClick.Click(window, page.Link, "Inspect the sample morpheme");
                await workspace.Inspector.Loading;
                PageScreenshots.Settle(window);

                Assert.True(workspace.Inspector.IsOpen);
                Assert.True(host.FindControl<Border>("InspectorPane")!.IsVisible);
                Assert.False(host.FindControl<ContentControl>("PageInspectorHost")!.IsVisible);
                Assert.True(host.FindControl<ContentControl>("ObjectInspectorHost")!.IsVisible);
            }
            finally
            {
                window.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void MainWindowPageDetailsRequestClosesTheObjectWithoutTakingFocusFromTheAction()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = SampleInspectorClient();
            var (workspace, window) = FakeComposedWindow.Create(fake);
            var host = window.FindControl<WorkspaceLayoutHost>("WorkspaceLayout")!;
            try
            {
                workspace.CurrentPage = WorkspacePage.Texts;
                window.Width = 1300;
                window.Height = 800;
                window.Show();
                PageScreenshots.Settle(window);
                var page = InstallPageInput(window, SampleSubject());
                host.SetPagePanels(new InspectorPage());
                PageScreenshots.Settle(window);
                var handledAtWindow = false;
                window.AddHandler(WorkspaceLayoutHost.PageInspectorRequestedEvent,
                    (_, args) => handledAtWindow = args.Handled, RoutingStrategies.Bubble, handledEventsToo: true);

                HeadlessClick.Click(window, page.Link, "Inspect the sample morpheme");
                await workspace.Inspector.Loading;
                PageScreenshots.Settle(window);
                Assert.True(workspace.Inspector.IsOpen);
                Assert.Same(page.Link, window.FocusManager!.GetFocusedElement());

                HeadlessClick.Click(window, page.DetailsAction, "Show page details");
                PageScreenshots.Settle(window);

                Assert.True(handledAtWindow);
                Assert.False(workspace.Inspector.IsOpen);
                Assert.Null(workspace.Context.Inspector);
                Assert.True(page.DetailsAction.IsFocused);
                Assert.False(page.Link.IsFocused);
                Assert.True(host.FindControl<Border>("InspectorPane")!.IsVisible);
                Assert.True(host.FindControl<ContentControl>("PageInspectorHost")!.IsVisible);
                Assert.False(host.FindControl<ContentControl>("ObjectInspectorHost")!.IsVisible);
                Assert.False(host.FindControl<TextBlock>("InspectorEmpty")!.IsVisible);
            }
            finally
            {
                window.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(1));
    }

    [Theory]
    [InlineData(100)]
    [InlineData(150)]
    public void ClosingAnObjectOpenedFromANarrowPageRestoresItsVisibleFocusOrigin(int zoom)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = FakeComposedWindow.Create(SampleInspectorClient());
            try
            {
                workspace.CurrentPage = WorkspacePage.Texts;
                window.Width = 640;
                window.Height = 600;
                workspace.PageModel<SettingsPageModel>().Settings!.ZoomPercent = zoom;
                window.Show();
                PageScreenshots.Settle(window);
                var input = InstallPageInput(window, SampleSubject());
                var provider = new InspectorPage();
                var host = window.FindControl<WorkspaceLayoutHost>("WorkspaceLayout")!;
                host.SetPagePanels(provider);
                PageScreenshots.Settle(window);
                Assert.False(host.FindControl<Border>("InspectorPane")!.IsVisible);
                HeadlessClick.Click(window, input.Link, "Inspect the sample morpheme");
                await workspace.Inspector.Loading;
                PageScreenshots.Settle(window);
                Assert.True(workspace.Inspector.IsOpen);
                Assert.False(host.FindControl<Grid>("WorkingPageRegion")!.IsVisible);
                AssertFooterParent(host, provider.Footer, "InspectorFooterHost");
                var close = Assert.Single(host.FindControl<ContentControl>("ObjectInspectorHost")!
                    .GetVisualDescendants().OfType<Button>(), button =>
                    Avalonia.Automation.AutomationProperties.GetName(button) == "Close the inspector");
                HeadlessClick.Click(window, close, "Close the inspector");
                PageScreenshots.Settle(window);
                Assert.False(workspace.Inspector.IsOpen);
                Assert.False(host.FindControl<Border>("InspectorPane")!.IsVisible);
                Assert.True(host.FindControl<Grid>("WorkingPageRegion")!.IsEffectivelyVisible);
                Assert.True(input.Link.IsEffectivelyVisible && input.Link.IsFocused);
                AssertFooterParent(host, provider.Footer, "PageActionFooterHost");
            }
            finally
            {
                window.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void PageInspectorRequestRejectsANullOrigin()
    {
        avalonia.Invoke(() => Assert.Throws<ArgumentNullException>(
            () => WorkspaceLayoutHost.RequestPageInspector(null!)));
    }

    [Fact]
    public void EmptyInspectorAppearsOnlyWhenNeitherPageDetailsNorObjectInspectionExists()
    {
        avalonia.Invoke(() =>
        {
            var host = WorkspaceLayoutHost.ForPage(new Border());
            host.ObjectInspector = new Border();
            var window = new MotifTestWindow { Width = 1300, Height = 800, Content = host };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);
                Assert.True(host.FindControl<TextBlock>("InspectorEmpty")!.IsVisible);

                host.SetObjectInspectorOpen(true);

                Assert.False(host.FindControl<TextBlock>("InspectorEmpty")!.IsVisible);
                Assert.True(host.FindControl<ContentControl>("ObjectInspectorHost")!.IsVisible);
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static void CaptureCurrentState(Window window, string name)
    {
        LayoutAssertions.AssertCurrent(window);
        using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No frame rendered.");
        var folder = Environment.GetEnvironmentVariable(ScreenshotFactAttribute.FolderVariable);
        if (string.IsNullOrWhiteSpace(folder)) folder = Path.Combine(AppContext.BaseDirectory, "test-results");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, name);
        frame.Save(path, PngBitmapEncoderOptions.Default);
    }

    private static void AssertFooterParent(WorkspaceLayoutHost host, Control footer, string parentName)
    {
        var parent = host.FindControl<ContentControl>(parentName)!;
        var otherName = parentName == "InspectorFooterHost" ? "PageActionFooterHost" : "InspectorFooterHost";
        Assert.Same(footer, parent.Content);
        Assert.Null(host.FindControl<ContentControl>(otherName)!.Content);
        Assert.Same(parent, footer.Parent);
    }

    private static PageInput InstallPageInput(MainWindow window, InspectorSubject subject)
    {
        var texts = Assert.Single(window.GetVisualDescendants().OfType<TextsPage>());
        var root = Assert.IsType<Grid>(texts.Content);
        var input = new PageInput(subject);
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
        };
        actions.Children.Add(input.Link);
        actions.Children.Add(input.DetailsAction);
        input.DetailsAction.Click += (_, _) => WorkspaceLayoutHost.RequestPageInspector(input.DetailsAction);
        Grid.SetRow(actions, 1);
        root.Children.Add(actions);
        return input;
    }

    private static InspectorSubject SampleSubject()
    {
        var morph = PageScreenshots.SampleMorph("wa-");
        return InspectorSubject.Morpheme(morph.AllomorphId, morph.GrammaticalInfoId, morph.Form, "3PL")!;
    }

    private static FakeCommandClient SampleInspectorClient()
    {
        var fake = new FakeCommandClient();
        fake.OnInspect((request, _) => Task.FromResult(CommandOutcome<InspectResponse>.Success(
            PageScreenshots.SampleInspection(request.Subject))));
        return fake;
    }

    private sealed class PageInput(InspectorSubject subject)
    {
        public InspectLink Link { get; } = new() { Content = subject.Label, Subject = subject };
        public Button DetailsAction { get; } = new() { Content = "Page details" };
    }

    private sealed class InspectorPage : UserControl, IWorkspacePaneProvider
    {
        public object Context { get; } = new();
        public Border Details { get; } = new();
        public Border Footer { get; } = new();
        public Button RequestOrigin { get; } = new() { Content = "Details" };

        public InspectorPage()
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
