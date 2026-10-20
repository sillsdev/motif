using System.Globalization;
using SIL.Motif.App;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>Pins the ICU-style plural helper that formats count sentences stored as .resx values.</summary>
public sealed class PluralTextTests
{
    private const string Changes = "{0, plural, one {# change} other {# changes}}";

    [Theory]
    [InlineData(1, "1 change")]
    [InlineData(0, "0 changes")]
    [InlineData(2, "2 changes")]
    [InlineData(1234, "1,234 changes")]
    public void ChoosesTheEnglishCategory(int count, string expected) =>
        Assert.Equal(expected, PluralText.Format(Changes, count, CultureInfo.InvariantCulture));

    [Fact]
    public void AnExactBranchBeatsTheCategory() =>
        Assert.Equal("No changes", PluralText.Format("{0, plural, =0 {No changes} one {# change} other {# changes}}", 0));

    [Fact]
    public void ThePatternNeedsAnOtherBranch() =>
        Assert.Throws<FormatException>(() => PluralText.Format("{0, plural, one {# change}}", 2));

    [Fact]
    public void AValueWithoutAPatternIsNotOneAndStaysAPlainFormat()
    {
        Assert.False(PluralText.IsPattern("Undo change to {0}"));
        Assert.Throws<FormatException>(() => PluralText.Format("Undo change to {0}", 1));
        Assert.Equal("Undo change to Gloss", string.Format(CultureInfo.InvariantCulture, "Undo change to {0}", "Gloss"));
    }
}
