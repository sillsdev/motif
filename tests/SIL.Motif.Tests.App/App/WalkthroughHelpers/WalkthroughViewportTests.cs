using Avalonia;
using SIL.Motif.Tests.App.Walkthrough;
using Xunit;

namespace SIL.Motif.Tests.App.WalkthroughHelpers;

public sealed class WalkthroughViewportTests
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

}
