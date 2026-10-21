using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using SIL.Motif.App.Controls.WordPresentation;
using SIL.Motif.App.Views;
using SIL.Motif.Tests.App.Walkthrough;
using Xunit;

namespace SIL.Motif.Tests.App.WalkthroughHelpers;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class WalkthroughViewportTests(AvaloniaHeadlessFixture avalonia)
{
    [Theory]
    [InlineData(-30, 10, 20, 20, false)]
    [InlineData(-5, 10, 20, 20, false)]
    [InlineData(780, 10, 20, 20, true)]
    [InlineData(790, 10, 20, 20, false)]
    [InlineData(810, 10, 20, 20, false)]
    [InlineData(10, -5, 20, 20, false)]
    [InlineData(10, 280, 20, 20, true)]
    [InlineData(10, 290, 20, 20, false)]
    public void BoundsMustFitInsideTheScrollViewerViewport(
        double x, double y, double width, double height, bool expected)
    {
        Assert.Equal(expected, WalkthroughWindow.FitsViewport(
            new Rect(x, y, width, height), new Size(800, 300)));
    }

    [Fact]
    public void ATargetPastAWordListSlotViewportIsReportedAsClipped()
    {
        avalonia.Invoke(() =>
        {
            var inside = new Border { Width = 20, Height = 20, HorizontalAlignment = HorizontalAlignment.Left };
            var outside = new Border
            {
                Width = 20, Height = 20, HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(3000, 0, 0, 0),
            };
            var rows = new ListBox();
            Grid.SetRow(rows, 1);
            var content = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,*"),
                Children = { new WordRowHeader(), rows, inside, outside },
            };
            var slot = new WordListSlot { Content = content };
            var window = new MotifTestWindow { Content = new Grid { Width = 600, Height = 300, Children = { slot } },
                Width = 640, Height = 360 };
            window.Show();
            try
            {
                for (var pass = 0; pass < 3; pass++)
                {
                    Dispatcher.UIThread.RunJobs();
                    window.UpdateLayout();
                }
                Assert.Null(WalkthroughWindow.ClippingViewer(inside));
                Assert.Same(slot, WalkthroughWindow.ClippingViewer(outside));
            }
            finally { window.Close(); }
        });
    }
}
