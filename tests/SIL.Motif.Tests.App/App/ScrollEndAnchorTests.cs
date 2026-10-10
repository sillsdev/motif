using Avalonia.Controls;
using Avalonia.Threading;
using SIL.Motif.App.Views;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>Pins that a viewer left at its end follows content that grows later, and that scrolling up lets it go.</summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class ScrollEndAnchorTests
{
    [Fact]
    public void AViewerLeftAtItsEndStaysThereWhenTheContentGrows()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            var (window, viewer, content) = Open();
            viewer.ScrollToEnd();
            Settle(window);

            content.Height = 1300;
            Settle(window);

            Assert.Equal(viewer.Extent.Height - viewer.Viewport.Height, viewer.Offset.Y, 3);
            Assert.True(viewer.Offset.Y > 1000);
            window.Close();
            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void AViewerSentToItsEndReachesTheEndThatMovedInTheSameLayoutPass()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            var (window, viewer, content) = Open();

            viewer.ScrollToEnd();
            content.Height = 1300;
            Settle(window);

            Assert.Equal(viewer.Extent.Height - viewer.Viewport.Height, viewer.Offset.Y, 3);
            Assert.True(viewer.Offset.Y > 1000);
            window.Close();
            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void AViewerScrolledUpFromItsEndKeepsItsPlaceWhenTheContentGrows()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            var (window, viewer, content) = Open();
            viewer.ScrollToEnd();
            Settle(window);
            viewer.Offset = viewer.Offset.WithY(viewer.Offset.Y - 300);
            Settle(window);
            var place = viewer.Offset.Y;

            content.Height = 1300;
            Settle(window);

            Assert.Equal(place, viewer.Offset.Y, 3);
            window.Close();
            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(60));
    }

    private static (Window Window, ScrollViewer Viewer, Border Content) Open()
    {
        var content = new Border { Height = 1000 };
        var viewer = new ScrollViewer { Content = content };
        ScrollEndAnchor.Attach(viewer);
        var window = new Window { Width = 300, Height = 200, Content = viewer };
        window.Show();
        Settle(window);
        return (window, viewer, content);
    }

    private static void Settle(Window window)
    {
        for (var pass = 0; pass < 3; pass++)
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
        }
    }
}
