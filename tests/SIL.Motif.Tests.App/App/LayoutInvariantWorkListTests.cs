using Xunit;

namespace SIL.Motif.Tests.App;

[Trait("MotifTestLevel", "Integration")]
public sealed class LayoutInvariantWorkListTests
{
    private static readonly LayoutFindingContext Context = new("page/empty", "640x600", 100, "light");
    private static readonly ControlDescription Action = new("Button", "Action", null, "Go", "");
    private static readonly LayoutFinding Finding = new(Context.Scene, Context.Size, Context.Zoom, Context.Theme,
        LayoutCheckNames.UnreachableControl, Action, new(0, 0, 0, 0), "zero", "Action has no target.");
    private static readonly LayoutException Exception = new(Context, Finding.Check, Action, null,
        "UI owner", "The documented action has a zero-height host.", Finding.Identity);

    [Fact]
    public void AKnownFindingNeedsAnExactOwnedExceptionAndMustRecur()
    {
        LayoutInvariantWorkList.AssertNoUnexpected([Finding], [Exception], new HashSet<LayoutFindingContext> { Context });
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => LayoutInvariantWorkList.AssertNoUnexpected(
            [], [Exception], new HashSet<LayoutFindingContext> { Context }));
    }

    [Fact]
    public void AnExceptionForAnUninspectedStateCannotSilentlySurvive()
    {
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => LayoutInvariantWorkList.AssertNoUnexpected(
            [], [Exception], new HashSet<LayoutFindingContext> { Context with { Scene = "other" } }));
    }

    [Fact]
    public void ActionsWithDifferentAccessibleNamesKeepSeparateFindings()
    {
        var open = Finding with { Control = Action with { Name = null, AutomationName = "Open the word" } };
        var tryWord = Finding with { Control = Action with { Name = null, AutomationName = "Try the word" } };
        Assert.Equal(2, ReachabilityAssertions.CollapseDuplicates([open, tryWord]).Count);
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("reason")]
    [InlineData("duplicate")]
    [InlineData("different-control")]
    [InlineData("different-cell")]
    [InlineData("different-identity")]
    [InlineData("different-count")]
    public void AnExceptionCannotHideAnUnauthoredOrAmbiguousFinding(string defect)
    {
        IReadOnlyList<LayoutException> exceptions = defect switch
        {
            "owner" => [Exception with { Owner = "" }],
            "reason" => [Exception with { Reason = "" }],
            "duplicate" => [Exception, Exception],
            "different-identity" => [Exception with { Identity = "other" }],
            "different-count" => [Exception with { Count = 2 }],
            "different-control" => [Exception with { Control = Action with { Name = "Other" } }],
            _ => [Exception with { Context = Context with { Zoom = 150 } }],
        };
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => LayoutInvariantWorkList.AssertNoUnexpected(
            [Finding], exceptions, new HashSet<LayoutFindingContext> { Context }));
    }
}
