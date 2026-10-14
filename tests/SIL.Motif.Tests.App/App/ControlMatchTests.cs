using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class ControlMatchTests
{
    private static ControlDescription Control(string type, string? name = null, string? id = null,
        string classes = "", string? ancestor = null) => new(type, name, id, null, classes, ancestor);

    [Fact]
    public void ANameMatchesWhateverClassesTheControlWears()
    {
        var expected = new ControlMatch("TextBlock", Name: "MatrixCellCount", Classes: "matrixCellCount");

        Assert.True(expected.Matches(Control("TextBlock", name: "MatrixCellCount", classes: "somethingElse")));
        Assert.True(expected.Matches(Control("TextBlock", name: "MatrixCellCount")));
    }

    [Fact]
    public void ANameThatDiffersFailsEvenWhenTheClassesAreIdentical()
    {
        var expected = new ControlMatch("TextBlock", Name: "MatrixCellCount", Classes: "matrixCellCount");

        Assert.False(expected.Matches(Control("TextBlock", name: "MatrixCellLabel", classes: "matrixCellCount")));
        Assert.False(expected.Matches(Control("TextBlock", classes: "matrixCellCount")));
    }

    [Fact]
    public void AnAutomationIdMatchesTheSameWayAsAName()
    {
        var expected = new ControlMatch("Button", Name: "motif-word-open-in-text");

        Assert.True(expected.Matches(Control("Button", id: "motif-word-open-in-text", classes: "reviewAction")));
    }

    [Fact]
    public void TheTypeMustAlwaysMatch()
    {
        Assert.False(new ControlMatch("Button", Name: "x").Matches(Control("TextBlock", name: "x")));
        Assert.False(new ControlMatch("TextBlock", Ancestor: "x").Matches(Control("Button", ancestor: "x")));
        Assert.False(new ControlMatch("Grid", Classes: "").Matches(Control("Border")));
    }

    [Fact]
    public void WithoutANameTheNearestNamedAncestorDecidesAndClassesAreIgnored()
    {
        var expected = new ControlMatch("AccessText", Ancestor: "motif-reader-read-menu", Classes: "ignored");

        Assert.True(expected.Matches(Control("AccessText", classes: "other", ancestor: "motif-reader-read-menu")));
        Assert.False(expected.Matches(Control("AccessText", classes: "ignored", ancestor: "motif-matrix-parse-again")));
        Assert.False(expected.Matches(Control("AccessText", classes: "ignored")));
    }

    [Fact]
    public void ANameBeatsAnAncestorToo()
    {
        var expected = new ControlMatch("TextBlock", Name: "a", Ancestor: "wrong");

        Assert.True(expected.Matches(Control("TextBlock", name: "a", ancestor: "right")));
    }

    [Fact]
    public void ClassesAreTheLastResortAndIgnoreOrderAndPseudoClasses()
    {
        var expected = new ControlMatch("AccessText", Classes: "b.a");

        Assert.True(expected.Matches(Control("AccessText", classes: "a.b :disabled")));
        Assert.False(expected.Matches(Control("AccessText", classes: "a")));
    }

    [Fact]
    public void AMissingRelatedControlMatchesOnlyAMissingRelatedControl()
    {
        Assert.True(ControlMatch.Matches(null, null));
        Assert.False(ControlMatch.Matches(null, Control("TextBlock")));
        Assert.False(ControlMatch.Matches(new ControlMatch("TextBlock", Name: "x"), null));
    }

    [Fact]
    public void AnExpectedFindingMatchesOnIdsWhenTheClassesOfTheFindingChange()
    {
        var entry = new ExpectedLayoutFinding("scene", LayoutCheckNames.OverlappingText,
            new ControlMatch("TextBlock", Name: "first"), new ControlMatch("AccessText", Ancestor: "button"),
            [new ExpectedFindingLocation("thin", 100, "light")], "reason", "matrix/x.png", "item", "defect", "UF");
        var finding = new LayoutFinding("scene", "thin", 100, "light", LayoutCheckNames.OverlappingText,
            Control("TextBlock", id: "first", classes: "restyled"), new LayoutBounds(0, 0, 1, 1), "identity", "detail",
            Control("AccessText", ancestor: "button", classes: "alsoRestyled"));

        Assert.True(entry.Matches(finding));
        Assert.False(entry.Matches(finding with { Zoom = 150 }));
        Assert.False(entry.Matches(finding with { Control = Control("TextBlock", id: "second") }));
    }
}
