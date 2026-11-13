using SIL.Motif.Scripture;
using SIL.Scripture;
using Xunit;

namespace SIL.Motif.Tests.Scripture;

public sealed class ScriptureContextTests
{
    private static readonly ScrVers English = ScrVers.English;

    [Fact]
    public void BookRangeWithChapterPartKeepsTheLaterPartForThatBook()
    {
        var context = ScriptureContext.Parse("GEN-ROM; 1CO 13-15", English);

        Assert.Equal([13, 14, 15], context.ChaptersOf("1CO"));
        Assert.True(context.Contains("MAT", 28));
        Assert.False(context.Contains("1CO", 12));
        Assert.False(context.Contains("REV", 1));
    }

    [Fact]
    public void GroupNamesSelectWholeTestaments()
    {
        var context = ScriptureContext.Parse("NT", English);

        Assert.True(context.Contains("MAT", 1));
        Assert.True(context.Contains("REV", 22));
        Assert.False(context.Contains("GEN", 1));
        Assert.Equal(27, context.Books.Count);
    }

    [Fact]
    public void ChapterCanBeRemovedFromAWholeGroup()
    {
        var context = ScriptureContext.Parse("OT; -GEN 1", English);

        Assert.False(context.Contains("GEN", 1));
        Assert.True(context.Contains("GEN", 2));
        Assert.True(context.Contains("MAL", 1));
    }

    [Theory]
    [InlineData("1COR")]
    [InlineData("XXA")]
    [InlineData("gen")]
    [InlineData("nt")]
    [InlineData("GEN-ROM; 1Cor 13")]
    [InlineData("Matt 5")]
    public void LowerCaseAndAliasedBookIdsAreRefusedWithTheStandardIdNamed(string text)
    {
        var ok = ScriptureContext.TryParse(text, English, out var context, out var error);

        Assert.False(ok);
        Assert.Null(context);
        Assert.Contains("standard book ID", error);
        Assert.Contains("1CO", error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("-GEN 1")]
    [InlineData("GEN 99")]
    [InlineData("NT; -GEN")]
    public void EmptyOrImpossibleContextsAreRefusedWithAReason(string text)
    {
        var ok = ScriptureContext.TryParse(text, English, out _, out var error);

        Assert.False(ok);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void CommaBetweenBooksIsNotASeparator()
    {
        Assert.False(ScriptureContext.TryParse("GEN-ROM, 1CO 13-15", English, out _, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void ParseThrowsFormatExceptionWithTheSameMessage()
    {
        var ex = Assert.Throws<FormatException>(() => ScriptureContext.Parse("1COR", English));

        Assert.Contains("1CO", ex.Message);
    }

    [Fact]
    public void WholeBookCoversEveryChapterOfTheVersification()
    {
        var context = ScriptureContext.Parse("JOL", English);

        Assert.Equal([1, 2, 3], context.ChaptersOf("JOL"));
    }

    [Fact]
    public void LastChapterFollowsTheVersification()
    {
        var english = ScriptureContext.Parse("JOL", ScrVers.English);
        var original = ScriptureContext.Parse("JOL", ScrVers.Original);

        Assert.False(english.Contains("JOL", 4));
        Assert.True(original.Contains("JOL", 4));
    }

    [Fact]
    public void NormalizeWritesWholeBooksAlonePartialBooksAsRunsAndSameChaptersAlike()
    {
        var a = ScriptureContext.Parse("GEN-EXO; 1CO 13-15", English);
        var b = ScriptureContext.Parse("EXO; GEN; 1CO 15; 1CO 13-14", English);

        Assert.Equal("GEN; EXO; 1CO 13-15", a.Normalize());
        Assert.Equal(a.Normalize(), b.Normalize());
        Assert.Equal("GEN; EXO; 1CO 13-15", ScriptureContext.Parse("GEN-EXO; 1CO 13,14,15", English).Normalize());
    }

    [Fact]
    public void TwoChapterPartsForOneBookAddTogether()
    {
        var context = ScriptureContext.Parse("1CO 1-3; 1CO 13-15", English);

        Assert.Equal([1, 2, 3, 13, 14, 15], context.ChaptersOf("1CO"));
    }

    [Fact]
    public void ContainsAnUnknownBookIdIsFalse()
    {
        var context = ScriptureContext.Parse("GEN", English);

        Assert.False(context.Contains("XXX", 1));
    }
}
