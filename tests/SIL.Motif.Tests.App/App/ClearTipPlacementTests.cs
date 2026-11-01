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

    [Fact]
    public void ATipMayTouchAControlWithoutCoveringIt()
    {
        Rect[] below = [new(350, 400, 200, 40)];

        Assert.Equal((PopupAnchor.Top, PopupGravity.Top), ClearTipPlacement.Choose(Owner, Tip, Window, below));
    }

    [Fact]
    public void ATipWiderThanItsLineHangsAboveTheWholeLineWhenEverySideBesideTheOwnerIsTaken()
    {
        var line = new Rect(300, 380, 300, 100);
        var wide = new Size(400, 40);
        Rect[] neighbours = [new(0, 380, 300, 100), new(600, 380, 300, 100)];

        var plan = ClearTipPlacement.Plan(Owner, line, wide, Window, neighbours);

        Assert.Equal((PopupAnchor.Top, PopupGravity.Top), (plan.Side, plan.Gravity));
        Assert.Equal(new Rect(400, 380, 100, 100), plan.Anchor);
        var area = ClearTipPlacement.Area(plan.Anchor, wide, plan.Side, plan.Gravity);
        Assert.All(neighbours, neighbour => Assert.False(area.Intersects(neighbour)));
        Assert.True(Window.Contains(area));
    }

    [Fact]
    public void ATipKeepsItsOwnersSideWhenThatSideIsClearEvenInsideALine()
    {
        var plan = ClearTipPlacement.Plan(Owner, new Rect(300, 380, 300, 100), Tip, Window, []);

        Assert.Equal(Owner, plan.Anchor);
        Assert.Equal((PopupAnchor.Top, PopupGravity.Top), (plan.Side, plan.Gravity));
    }

    [Fact]
    public void ATipWithoutALineChoosesExactlyAsItDidBefore()
    {
        Rect[] crowded = [new(0, 0, 1000, 395), new(0, 0, 1000, 380), new(0, 435, 1000, 365), new(0, 395, 395, 40), new(505, 395, 495, 40)];

        var plan = ClearTipPlacement.Plan(Owner, null, Tip, Window, crowded);

        Assert.Equal((Owner, PopupAnchor.Left, PopupGravity.Left), plan);
    }

    private static readonly Size WideTip = new(400, 40);

    [Fact]
    public void ATipNearTheRightEdgeHangsAboveTheLineGrowingLeft()
    {
        var owner = new Rect(880, 400, 100, 30);
        var line = new Rect(800, 320, 200, 150);
        Rect[] beside = [new(580, 360, 300, 80)];

        var plan = ClearTipPlacement.Plan(owner, line, WideTip, Window, beside);

        Assert.Equal((new Rect(880, 320, 100, 150), PopupAnchor.TopRight, PopupGravity.TopLeft), plan);
        var area = ClearTipPlacement.Area(plan.Anchor, WideTip, plan.Side, plan.Gravity);
        Assert.True(Window.Contains(area));
        Assert.All(beside, obstacle => Assert.False(area.Intersects(obstacle)));
    }

    [Fact]
    public void ATipNearTheLeftEdgeHangsAboveTheLineGrowingRight()
    {
        var owner = new Rect(20, 400, 100, 30);
        var line = new Rect(0, 320, 200, 150);
        Rect[] beside = [new(120, 360, 300, 80)];

        var plan = ClearTipPlacement.Plan(owner, line, WideTip, Window, beside);

        Assert.Equal((new Rect(20, 320, 100, 150), PopupAnchor.TopLeft, PopupGravity.TopRight), plan);
        Assert.True(Window.Contains(ClearTipPlacement.Area(plan.Anchor, WideTip, plan.Side, plan.Gravity)));
    }

    [Fact]
    public void ATipKeepsItsOldChoiceWhenTheLineIsATallContainer()
    {
        var owner = new Rect(880, 400, 100, 30);
        Rect[] beside = [new(580, 360, 300, 80)];
        var old = ClearTipPlacement.Choose(owner, WideTip, Window, beside);

        var plan = ClearTipPlacement.Plan(owner, new Rect(800, 100, 200, 5 * ClearTipPlacement.MaxLineTips * WideTip.Height), WideTip, Window, beside);

        Assert.Equal((owner, old.Anchor, old.Gravity), plan);
    }

    [Fact]
    public void ATipKeepsItsOldChoiceWhenTheLineSideCoversAControlToo()
    {
        var owner = new Rect(880, 400, 100, 30);
        Rect[] crowded = [new(580, 360, 300, 80), new(580, 270, 400, 50), new(580, 480, 400, 50)];
        var old = ClearTipPlacement.Choose(owner, WideTip, Window, crowded);

        var plan = ClearTipPlacement.Plan(owner, new Rect(800, 320, 200, 150), WideTip, Window, crowded);

        Assert.Equal((owner, old.Anchor, old.Gravity), plan);
    }

    [Fact]
    public void ATipHangingAboveTheLineKeepsTheOwnersClearance()
    {
        var owner = new Rect(870, 390, 120, 50);
        var line = new Rect(800, 320, 200, 150);
        Rect[] beside = [new(580, 350, 300, 100)];

        var plan = ClearTipPlacement.Plan(owner, line, WideTip, Window, beside, clearance: 10);

        Assert.Equal(new Rect(870, 310, 120, 170), plan.Anchor);
        Assert.Equal(PopupAnchor.TopRight, plan.Side);
    }

    [Fact]
    public void ObstacleBoundsIncludeTheFullScaleAndTranslation()
    {
        var transformed = ClearTipPlacement.TransformBounds(new Rect(0, 0, 100, 20),
            new Matrix(1.5, 0, 0, 1.5, 10, 20));

        Assert.Equal(new Rect(10, 20, 150, 30), transformed);
    }
}
