using Avalonia;
using Avalonia.Controls.Primitives.PopupPositioning;
using SIL.Motif.App.Views;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins how a tooltip owner's tip picks its side: above when that is clear, then left, right and below in turn, never
/// over another control a person could use or outside the window, and over the fewest controls when every side is taken.
/// </summary>
public sealed class ClearTipPlacementTests
{
    private static readonly Rect Window = new(0, 0, 1000, 800);
    private static readonly Rect Owner = new(400, 400, 100, 30);
    private static readonly Size Tip = new(200, 40);

    [Fact]
    public void ATipOpensAboveItsOwnerWhenNothingIsThere()
    {
        Assert.Equal((PopupAnchor.Top, PopupGravity.Top), ClearTipPlacement.Choose(Owner, Tip, Window, []));
        Assert.Equal(new Rect(350, 360, 200, 40), ClearTipPlacement.Area(Owner, Tip, PopupAnchor.Top, PopupGravity.Top));
    }

    [Fact]
    public void ATipLeavesAboveForTheLeftWhenAControlIsAbove()
    {
        Rect[] above = [new(300, 370, 400, 20)];

        var side = ClearTipPlacement.Choose(Owner, Tip, Window, above);

        Assert.Equal((PopupAnchor.Left, PopupGravity.Left), side);
        Assert.False(ClearTipPlacement.Area(Owner, Tip, side.Anchor, side.Gravity).Intersects(above[0]));
    }

    [Fact]
    public void ATipTakesTheFirstClearSideAndNeverOneOutsideTheWindow()
    {
        var owner = new Rect(0, 0, 50, 40);
        Rect[] beside = [new(50, 0, 300, 40)];

        var side = ClearTipPlacement.Choose(owner, Tip, Window, beside);

        Assert.Equal((PopupAnchor.BottomLeft, PopupGravity.BottomRight), side);
        Assert.True(Window.Contains(ClearTipPlacement.Area(owner, Tip, side.Anchor, side.Gravity)));
    }

    [Fact]
    public void ATipWithNoClearSideCoversAsFewControlsAsItCan()
    {
        Rect[] crowded = [new(0, 0, 1000, 395), new(0, 0, 1000, 380), new(0, 435, 1000, 365), new(0, 395, 395, 40), new(505, 395, 495, 40)];

        Assert.Equal((PopupAnchor.Left, PopupGravity.Left), ClearTipPlacement.Choose(Owner, Tip, Window, crowded));
    }

    [Fact]
    public void ATipThatFitsNowhereInTheWindowOpensAbove()
    {
        Assert.Equal(ClearTipPlacement.Sides[0], ClearTipPlacement.Choose(Owner, new Size(2000, 2000), Window, []));
    }
}
