using SIL.Motif.App.GuideQueries;
using Xunit;
using static SIL.Motif.Tests.App.GuideQueryTestSupport;

namespace SIL.Motif.Tests.App;

public sealed class GuideQueryDocumentTests
{
    private static GuideQueryDocument Section() => GuideQueryDocument.From(SectionLines);

    [Fact]
    public void ParagraphsPerLineAreParsedByTheContractsParserIntoOneQueryAndItsShow()
    {
        var blocks = Section().Blocks;

        var query = Assert.Single(blocks.Queries);
        Assert.Equal("alternates", query.Id);
        Assert.Equal("motif query forms --where kind=alternate", query.Run);
        Assert.Equal("alternates", Assert.Single(blocks.Shows).Query);
        Assert.Empty(blocks.Diagnostics);
    }

    [Fact]
    public void ABodyReplacedLeavesEveryOtherCharacterByteIdentical()
    {
        var document = Section();
        var query = document.Blocks.Queries[0];

        var changed = document.Replaced(query.Span, "id: alternates\ntitle: Alternates\nrun: motif query forms");

        Assert.Equal(document.Text.Replace("title: Alternate forms", "title: Alternates")
            .Replace("run: motif query forms --where kind=alternate", "run: motif query forms"), changed);
        Assert.StartsWith(document.Text[..query.Span.Start], changed);
        Assert.EndsWith(document.Text[query.Span.End..], changed);
    }

    [Fact]
    public void AQueryBlockCoversWholeParagraphsUnlessItSharesOneWithOtherText()
    {
        var document = Section();
        Assert.True(document.TryParagraphsOf(document.Blocks.Queries[0].Span, out var first, out var last));
        Assert.Equal((1, 5), (first, last));

        var shared = GuideQueryDocument.From(["intro ```motif-query", "id: a", "run: motif query forms", "```"]);
        Assert.Empty(shared.Blocks.Queries);
    }

    [Fact]
    public void ABlockInOneParagraphWithLineBreaksIsOneParagraph()
    {
        var document = GuideQueryDocument.From(["before", "```motif-query\nid: a\nrun: motif query forms\n```", "after"]);

        var query = Assert.Single(document.Blocks.Queries);
        Assert.True(document.TryParagraphsOf(query.Span, out var first, out var last));
        Assert.Equal((1, 1), (first, last));
        Assert.Equal("id: a\nrun: motif query forms", document.BodyOf(query.Span));
    }

    [Fact]
    public void TextWithoutBlocksHasNone()
    {
        Assert.False(GuideQueryDocument.From(["Just prose.", "More prose."]).HasBlocks);
        Assert.False(GuideQueryDocument.From([]).HasBlocks);
    }
}
