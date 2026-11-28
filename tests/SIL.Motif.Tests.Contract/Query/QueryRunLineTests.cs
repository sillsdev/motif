using SIL.Motif.Contract.Query;
using Xunit;

namespace SIL.Motif.Tests.Contract.Query;

public sealed class QueryRunLineTests
{
    [Fact]
    public void TheAdrExampleParsesToTheSameRecordTheCliBuilds()
    {
        Assert.True(QueryRunLine.TryParse("motif query forms --where kind=alternate --group letters.dropped", out var record, out _));
        Assert.Equal("forms", record.From);
        Assert.Equal([new QueryCondition("kind", "is", "alternate")], record.Where);
        Assert.Equal(["letters.dropped"], record.Group);
        Assert.Equal((1, 5, false), (record.MinCount, record.Examples, record.Ids));
    }

    [Fact]
    public void QuotesKeepASelectorTogetherAndRepeatedWheresAllApply()
    {
        Assert.True(QueryRunLine.TryParse("motif query forms --where \"form ~ 'a b'\" --where 'kind in lexeme,variant' --min-count 2 --ids", out var record, out _));
        Assert.Equal(2, record.Where.Count);
        Assert.Equal("'a b'", record.Where[0].Value);
        Assert.Equal(new QueryCondition("kind", "in", "lexeme,variant"), record.Where[1]);
        Assert.Equal((2, true), (record.MinCount, record.Ids));
    }

    [Fact]
    public void AnEscapedQuoteInsideDoubleQuotesIsKept()
    {
        Assert.True(QueryRunLine.TryTokenize("motif query forms --where \"form ~ \\\"x\\\"\"", out var tokens, out _));
        Assert.Equal("form ~ \"x\"", tokens[^1]);
    }

    [Fact]
    public void SavedQueriesAndEvidenceParse()
    {
        Assert.True(QueryRunLine.TryParse("motif query saved P-msa-no-category --examples 2", out var saved, out _));
        Assert.Equal(2, saved.Examples);
        Assert.True(QueryRunLine.TryParse("motif query evidence forms --where kind=alternate", out var evidence, out _));
        Assert.True(evidence.Evidence);
        Assert.False(QueryRunLine.TryParse("motif query saved P-nope", out _, out var refusal));
        Assert.Equal("guide-query.run-invalid", refusal.Code);
    }

    [Fact]
    public void AsOfIsReadFromQueryAndGroupLines()
    {
        Assert.True(QueryRunLine.TryParseRun("motif query forms --as-of 42", out var query, out _));
        Assert.Equal("42", query.AsOf);
        Assert.True(QueryRunLine.TryParseRun("motif group suffix --ending an --as-of baseline --parse-state fitting --limit 5 --cursor cDI", out var group, out _));
        Assert.Equal(("suffix", "an", "fitting", 5, "cDI", "baseline"),
            (group.Group!.Kind, group.Group.Ending, group.Group.ParseState, group.Group.Limit, group.Group.Cursor, group.AsOf));
    }

    [Fact]
    public void AsOfOnAQueryIsAFlagOnlyWhereTheCallerAllowsIt()
    {
        Assert.False(QueryRunLine.TryParseQueryArguments(["forms", "--project", "p", "--as-of", "1"], false, out _, out _));
        Assert.True(QueryRunLine.TryParseQueryArguments(["forms", "--project", "p", "--as-of", "1"], true, out var parsed, out _));
        Assert.Equal("p", parsed.Project);
    }

    [Fact]
    public void AGuideRunLineParsesSidesAndSortIntoTheRecord()
    {
        Assert.True(QueryRunLine.TryParse("motif query forms --as-of 42 --sides both --sort proposal.form", out var record, out var refusal), refusal?.Message);
        Assert.Equal(("42", "both", "proposal.form"), (record.AsOf, record.Sides, record.Sort));
    }

    [Theory]
    [InlineData("motif apply --project x")]
    [InlineData("motif trial")]
    [InlineData("motif wordform dispose --from-file f")]
    [InlineData("motif proposal save")]
    [InlineData("motif current set a b")]
    public void AVerbThatIsNotQueryOrGroupIsRefusedAsNotReadOnly(string run)
    {
        Assert.False(QueryRunLine.TryParse(run, out _, out var refusal));
        Assert.Equal("guide-query.run-not-read-only", refusal.Code);
        Assert.Equal(run.Split(' ')[1], refusal.Facts["verb"]);
    }

    [Theory]
    [InlineData("motif query forms ; motif apply")]
    [InlineData("motif query forms | tee x")]
    [InlineData("motif query forms > out.txt")]
    [InlineData("motif query forms && motif trial")]
    [InlineData("motif query forms $(whoami)")]
    public void ChainingAndRedirectionAreRefused(string run)
    {
        Assert.False(QueryRunLine.TryParse(run, out _, out var refusal));
        Assert.Equal("guide-query.run-not-read-only", refusal.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("motif")]
    [InlineData("query forms")]
    [InlineData("motif query")]
    [InlineData("motif query nothing")]
    [InlineData("motif query forms --project x")]
    [InlineData("motif query forms --members ids.txt")]
    [InlineData("motif query forms --where")]
    [InlineData("motif query forms --where nonsense")]
    [InlineData("motif query forms --bogus 1")]
    [InlineData("motif query forms --min-count 0")]
    [InlineData("motif query forms --at final")]
    [InlineData("motif query forms --where 'kind=alternate")]
    public void AMalformedLineIsAnInvalidRunRefusal(string run)
    {
        Assert.False(QueryRunLine.TryParse(run, out _, out var refusal));
        Assert.Equal("guide-query.run-invalid", refusal.Code);
    }

    [Fact]
    public void AGroupLineIsNotAQueryRecordButParsesAsARun()
    {
        Assert.False(QueryRunLine.TryParse("motif group prefix --start ka", out _, out var refusal));
        Assert.Equal("guide-query.run-invalid", refusal.Code);
        Assert.True(QueryRunLine.TryParseRun("motif group prefix --start ka", out var line, out _));
        Assert.Equal("ka", line.Group!.Start);
    }

    [Theory]
    [InlineData("motif group middle --ending an")]
    [InlineData("motif group suffix --project x")]
    [InlineData("motif group suffix --autonomous")]
    [InlineData("motif group suffix --limit 0")]
    [InlineData("motif group")]
    public void AMalformedGroupLineIsRefused(string run)
    {
        Assert.False(QueryRunLine.TryParseRun(run, out _, out var refusal));
        Assert.Equal("guide-query.run-invalid", refusal.Code);
    }

    [Theory]
    [InlineData("motif query forms\nmotif apply")]
    [InlineData("motif query forms\rmotif apply")]
    [InlineData("motif query forms\r\n")]
    public void ALineBreakIsRefusedAsNotReadOnly(string run)
    {
        Assert.False(QueryRunLine.TryParse(run, out _, out var refusal));
        Assert.Equal("guide-query.run-not-read-only", refusal.Code);
    }

    [Fact]
    public void AQuotedAngleBracketIsData()
    {
        Assert.True(QueryRunLine.TryParse("motif query stems --where \"msa-count>1\"", out var record, out _));
        Assert.Equal("msa-count", record.Where[0].Col);
    }

    [Fact]
    public void TryBuildQueryKeepsTheCallersFlagsAndPositionalsAsTheCliSplitThem()
    {
        var flags = new Dictionary<string, string> { ["project"] = "p", ["ids"] = "true" };
        Assert.True(QueryRunLine.TryBuildQuery(flags, ["forms"], ["forms", "--project", "p", "--ids", "--", "x"], false, out var parsed, out _));
        Assert.True(parsed.Record.Ids);
        Assert.False(QueryRunLine.TryBuildQuery(new Dictionary<string, string>(), [], [], false, out _, out _));
    }
}
