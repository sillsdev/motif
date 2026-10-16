using Avalonia;
using SIL.Motif.Tests.App.Walkthrough;
using Xunit;

namespace SIL.Motif.Tests.App.WalkthroughHelpers;

public sealed class StillAimableTests
{
    [Fact]
    public void ARightAlignedControlThatOnlyChangedWidthIsStillAimable() =>
        Assert.True(WalkthroughWindow.StillAimable(new Rect(1001, 757, 231, 22), new Rect(1003, 757, 229, 22)));

    [Fact]
    public void ALeftAlignedControlThatOnlyChangedWidthIsStillAimable() =>
        Assert.True(WalkthroughWindow.StillAimable(new Rect(1001, 757, 231, 22), new Rect(1001, 757, 229, 22)));

    [Fact]
    public void AControlSlidingInOnBothEdgesIsNotYetAimable() =>
        Assert.False(WalkthroughWindow.StillAimable(new Rect(1001, 757, 231, 22), new Rect(1021, 757, 231, 22)));

    [Fact]
    public void AControlMovingVerticallyIsNotYetAimable() =>
        Assert.False(WalkthroughWindow.StillAimable(new Rect(1001, 757, 231, 22), new Rect(1001, 760, 231, 22)));
}
