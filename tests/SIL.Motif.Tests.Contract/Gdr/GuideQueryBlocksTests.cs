using SIL.Motif.Contract.Gdr;
using Xunit;

namespace SIL.Motif.Tests.Contract.Gdr;

public sealed class GuideQueryBlocksTests
{
    private const string Query =
        "```motif-query\nid: dropped-letters\ntitle: Alternate forms that lose a letter, grouped by the letter lost\n" +
        "run: motif query forms --where kind=alternate --group letters.dropped\ncolumns: letters.dropped, count\nsort: count desc\n```";

    private const string Show = "```motif-show\nquery: dropped-letters\nshow: counts\nlimit: 10\n```";

    [Fact]
    public void TheWorkedExamplesParseWithEveryField()
    {
        var blocks = GuideQueryBlocks.Parse("# Heading\n\n" + Query + "\n\nText\n\n" + Show + "\n");
        Assert.Empty(blocks.Diagnostics);
        var query = Assert.Single(blocks.Queries);
        Assert.Equal("dropped-letters", query.Id);
        Assert.Equal("Alternate forms that lose a letter, grouped by the letter lost", query.Title);
        Assert.Equal("motif query forms --where kind=alternate --group letters.dropped", query.Run);
        Assert.Equal(["letters.dropped", "count"], query.Columns);
        Assert.Equal(new GuideSort("count", true), query.Sort);
        var show = Assert.Single(blocks.Shows);
        Assert.Equal(("dropped-letters", GuideShowKind.Counts, 10, null), (show.Query, show.Show, show.Limit, show.Columns));
    }

    [Fact]
    public void ASpanCoversTheFencesAndNamesItsLines()
    {
        var text = "intro\n\n" + Query + "\nafter";
        var span = Assert.Single(GuideQueryBlocks.Parse(text).Queries).Span;
        Assert.Equal(Query, text[span.Start..span.End]);
        Assert.Equal((3, 9), (span.StartLine, span.EndLine));
        Assert.Equal(text.IndexOf("id:", StringComparison.Ordinal), span.BodyStart);
        Assert.Equal("sort: count desc", text[(span.BodyEnd - 16)..span.BodyEnd]);
    }

    [Fact]
    public void ShowDefaultsToATableWithNoLimit()
    {
        var blocks = GuideQueryBlocks.Parse(Query + "\n```motif-show\nquery: dropped-letters\ncolumns: count\n```");
        var show = Assert.Single(blocks.Shows);
        Assert.Equal((GuideShowKind.Table, (int?)null), (show.Show, show.Limit));
        Assert.Equal(["count"], show.Columns);
    }

    [Fact]
    public void ACrlfDocumentParsesAndKeepsItsLineBreaks()
    {
        var text = ("a\n" + Query + "\n" + Show + "\nz").Replace("\n", "\r\n");
        var blocks = GuideQueryBlocks.Parse(text);
        Assert.Empty(blocks.Diagnostics);
        Assert.Equal("count desc", blocks.Queries[0].Sort is { } sort ? sort.Column + " " + (sort.Descending ? "desc" : "asc") : null);
        Assert.Equal(Query.Replace("\n", "\r\n"), text[blocks.Queries[0].Span.Start..blocks.Queries[0].Span.End]);
        Assert.Equal(9, blocks.Shows[0].Span.StartLine);
    }

    [Fact]
    public void ABlockShownInsideAnotherFenceIsNotABlock()
    {
        var text = "````text\n" + Query + "\n````\n\n" + Show;
        var blocks = GuideQueryBlocks.Parse(text);
        Assert.Empty(blocks.Queries);
        Assert.Single(blocks.Shows);
        Assert.Equal(GuideBlockDiagnosticKind.UnknownQuery, Assert.Single(blocks.Diagnostics).Kind);
    }

    [Fact]
    public void ATildeFenceAndALongerFenceAreRecognised()
    {
        var text = "~~~motif-query\nid: a\ntitle: t\nrun: motif query forms\n~~~\n\n````motif-show\nquery: a\n````";
        var blocks = GuideQueryBlocks.Parse(text);
        Assert.Empty(blocks.Diagnostics);
        Assert.Single(blocks.Queries);
        Assert.Single(blocks.Shows);
    }

    [Fact]
    public void AnUnknownFieldIsReportedAndTheBlockIsKept()
    {
        var blocks = GuideQueryBlocks.Parse("```motif-query\nid: a\ntitle: t\nrun: motif query forms\ncolour: red\n```");
        Assert.Single(blocks.Queries);
        var diagnostic = Assert.Single(blocks.Diagnostics);
        Assert.Equal((GuideBlockDiagnosticKind.UnknownField, "colour", 5), (diagnostic.Kind, diagnostic.Field, diagnostic.Line));
    }

    [Fact]
    public void ADuplicateIdIsReportedOnTheLaterBlock()
    {
        var one = "```motif-query\nid: a\ntitle: t\nrun: motif query forms\n```";
        var blocks = GuideQueryBlocks.Parse(one + "\n" + one);
        Assert.Equal(2, blocks.Queries.Count);
        var diagnostic = Assert.Single(blocks.Diagnostics);
        Assert.Equal((GuideBlockDiagnosticKind.DuplicateId, 7), (diagnostic.Kind, diagnostic.Line));
    }

    [Fact]
    public void AShowNamingAnUnknownIdIsReportedWhateverTheOrder()
    {
        var blocks = GuideQueryBlocks.Parse("```motif-show\nquery: later\n```\n\n```motif-query\nid: later\ntitle: t\nrun: motif query forms\n```\n" +
            "```motif-show\nquery: nowhere\n```");
        var diagnostic = Assert.Single(blocks.Diagnostics);
        Assert.Equal((GuideBlockDiagnosticKind.UnknownQuery, "query"), (diagnostic.Kind, diagnostic.Field));
        Assert.Equal(10, diagnostic.Line);
    }

    [Theory]
    [InlineData("show: grid", GuideBlockDiagnosticKind.MalformedField, "show")]
    [InlineData("limit: 0", GuideBlockDiagnosticKind.MalformedField, "limit")]
    [InlineData("limit: ten", GuideBlockDiagnosticKind.MalformedField, "limit")]
    [InlineData("limit: 1\nlimit: 2", GuideBlockDiagnosticKind.DuplicateField, "limit")]
    [InlineData("no colon here", GuideBlockDiagnosticKind.MalformedLine, null)]
    public void MalformedShowFieldsAreTypedDiagnostics(string extra, GuideBlockDiagnosticKind kind, string? field)
    {
        var blocks = GuideQueryBlocks.Parse("```motif-query\nid: a\ntitle: t\nrun: motif query forms\n```\n```motif-show\nquery: a\n" + extra + "\n```");
        var diagnostic = Assert.Single(blocks.Diagnostics);
        Assert.Equal((kind, field), (diagnostic.Kind, diagnostic.Field));
    }

    [Theory]
    [InlineData("Bad_Id", "motif query forms", "", GuideBlockDiagnosticKind.InvalidId)]
    [InlineData("a", "motif query forms", "\nsort: count sideways", GuideBlockDiagnosticKind.MalformedField)]
    [InlineData("a", "motif trial", "", GuideBlockDiagnosticKind.InvalidRun)]
    public void MalformedQueryFieldsAreTypedDiagnostics(string id, string run, string extra, GuideBlockDiagnosticKind kind)
    {
        var text = "```motif-query\nid: " + id + "\ntitle: t\nrun: " + run + extra + "\n```";
        Assert.Equal(kind, Assert.Single(GuideQueryBlocks.Parse(text).Diagnostics).Kind);
    }

    [Fact]
    public void AnUnreadOnlyRunCarriesTheRefusalCode()
    {
        var diagnostic = Assert.Single(GuideQueryBlocks.Parse("```motif-query\nid: a\ntitle: t\nrun: motif apply --project x\n```").Diagnostics);
        Assert.Equal("guide-query.run-not-read-only", diagnostic.RefusalCode);
    }

    [Fact]
    public void AnUnclosedBlockRunsToTheEndAndIsReported()
    {
        var text = "x\n```motif-query\nid: a\ntitle: t\nrun: motif query forms";
        var blocks = GuideQueryBlocks.Parse(text);
        Assert.Equal(GuideBlockDiagnosticKind.UnclosedBlock, Assert.Single(blocks.Diagnostics).Kind);
        Assert.Equal(text.Length, blocks.Queries[0].Span.End);
    }

    [Fact]
    public void MissingFieldsAreReported()
    {
        var kinds = GuideQueryBlocks.Parse("```motif-query\n```").Diagnostics.Select(d => (d.Kind, d.Field)).ToArray();
        Assert.Equal([(GuideBlockDiagnosticKind.MissingField, "id"), (GuideBlockDiagnosticKind.MissingField, "run"),
            (GuideBlockDiagnosticKind.MissingField, "title")], kinds.OrderBy(k => k.Field == "id" ? 0 : k.Field == "run" ? 1 : 2).ToArray());
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void ReplacingOneBlockLeavesEveryOtherByteIdentical(string eol)
    {
        var text = ("before\n" + Query + "\nmiddle\n" + Show + "\nafter\n").Replace("\n", eol);
        var blocks = GuideQueryBlocks.Parse(text);
        var replacement = "```motif-query" + eol + "id: dropped-letters" + eol + "title: T" + eol + "run: motif query forms" + eol + "```";
        var edited = GuideQueryBlocks.Replace(text, blocks.Queries[0].Span, replacement);
        var span = blocks.Queries[0].Span;
        Assert.Equal(text[..span.Start], edited[..span.Start]);
        Assert.Equal(text[span.End..], edited[(span.Start + replacement.Length)..]);
        var again = GuideQueryBlocks.Parse(edited);
        Assert.Equal("T", again.Queries[0].Title);
        Assert.Equal(Show.Replace("\n", eol), edited[again.Shows[0].Span.Start..again.Shows[0].Span.End]);
        Assert.Equal(text, GuideQueryBlocks.Replace(edited, again.Queries[0].Span, text[span.Start..span.End]));
    }

    [Fact]
    public void TextWithoutBlocksParsesToNothingAndNeverThrows()
    {
        Assert.Empty(GuideQueryBlocks.Parse("").Queries);
        Assert.Empty(GuideQueryBlocks.Parse("```\n```motif-query").Diagnostics);
        _ = GuideQueryBlocks.Parse("```motif-show");
        _ = GuideQueryBlocks.Parse("\r");
    }

    [Fact]
    public void DiagnosticsOnOneLineKeepTheOrderTheyWereFound()
    {
        var text = string.Concat(Enumerable.Repeat("```motif-query\n```\n", 12));
        var fields = GuideQueryBlocks.Parse(text).Diagnostics.Where(d => d.Line == 1).Select(d => d.Field!).ToArray();
        Assert.Equal(["id", "run", "title"], fields);
        var lines = GuideQueryBlocks.Parse(text).Diagnostics.Select(d => d.Line).ToArray();
        Assert.Equal(lines.OrderBy(line => line).ToArray(), lines);
    }

    [Fact]
    public void ANullDocumentParsesToNothing()
    {
        var blocks = GuideQueryBlocks.Parse(null);
        Assert.Empty(blocks.Queries);
        Assert.Empty(blocks.Diagnostics);
    }

    [Fact]
    public void CrOnlyLineEndingsGiveTheSameBlock()
    {
        var text = ("x\n" + Query + "\ny").Replace("\n", "\r");
        var span = Assert.Single(GuideQueryBlocks.Parse(text).Queries).Span;
        Assert.Equal(Query.Replace("\n", "\r"), text[span.Start..span.End]);
        Assert.Equal((2, 8), (span.StartLine, span.EndLine));
    }

    [Fact]
    public void AnEmptyBodyHasAnEmptyBodySpan()
    {
        var span = GuideQueryBlocks.Parse("```motif-query\n```").Diagnostics[0].Span;
        Assert.Equal(span.BodyStart, span.BodyEnd);
    }

    [Fact]
    public void ReplacingAnUnclosedBlockAndAnOutOfRangeSpan()
    {
        var text = "a\n```motif-query\nid: x";
        var span = Assert.Single(GuideQueryBlocks.Parse(text).Queries).Span;
        Assert.Equal("a\nB", GuideQueryBlocks.Replace(text, span, "B"));
        Assert.Throws<ArgumentOutOfRangeException>(() => GuideQueryBlocks.Replace("ab", new GuideBlockSpan(0, 9, 1, 1, 0, 0), "x"));
        Assert.Throws<ArgumentNullException>(() => GuideQueryBlocks.Replace("ab", null!, "x"));
    }
}
