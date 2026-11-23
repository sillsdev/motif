using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Query;
using Xunit;

namespace SIL.Motif.Tests.Contract.Query;

public sealed class QueryTextRoundTripTests
{
    public static TheoryData<string> Expressions() =>
    [
        "kind is variant",
        "kind not variant",
        "kind in lexeme,variant",
        "kind not in lexeme,variant",
        "environments has /_V",
        "tokens > 15",
        "tokens < 15",
        "form ~ ar",
        "form matches \"sar.*\"",
        "form matches \"a b\"",
        "gloss exists",
        "gloss missing",
        "kind is variant and tokens > 15",
        "kind is variant or kind is alternate",
        "not kind is variant",
        "not (kind is variant and tokens > 15)",
        "not (kind is variant or kind is alternate)",
        "kind is variant or tokens > 15 and form ~ sa",
        "(kind is variant or tokens > 15) and form ~ sa",
        "kind is variant and (tokens > 15 or tokens < 5)",
        "(kind is a and form ~ b) or (kind is c and not form ~ d)",
        "kind is a and (form ~ b and tokens > 1)",
        "kind is a or (form ~ b or tokens > 1)",
        "not not kind is a",
        "not (not kind is a and kind is b)",
        "form is \"with space\"",
        "form is \"say \\\"x\\\"\"",
        "form is \"\"",
        "form is \"a(b)\"",
        "form in \"a b\",c,\"d,e\"",
        "form matches \"(a|b)+\" and not form ~ \"x.y\"",
        "shape is CVCV and syllables > 1 and initial:2 in ma,ka",
        "final:1 not in a,e,i,o,u",
        "((kind is a))",
        "kind=alternate or kind=variant",
    ];

    private static QueryDocument Wrap(QueryExpr expr) => new() { From = "forms", Where = expr };

    [Theory]
    [MemberData(nameof(Expressions))]
    public void TextToAstToTextIsStableAndAstToTextToAstIsIdentity(string text)
    {
        var ast = QueryExprText.Parse(text);
        var canonical = QueryExprText.Print(ast);
        var again = QueryExprText.Parse(canonical);
        Assert.Equal(Wrap(ast).ToJson(), Wrap(again).ToJson());
        Assert.Equal(canonical, QueryExprText.Print(again));
    }

    [Fact]
    public void EveryExpressionShapeRoundTripsFromTheAst()
    {
        QueryField A(string op, object value) => new("kind", op, value);
        QueryExpr[] trees =
        [
            new QueryAnd([A("eq", "x"), new QueryAnd([A("eq", "y"), A("eq", "z")])]),
            new QueryOr([new QueryOr([A("eq", "x"), A("eq", "y")]), A("eq", "z")]),
            new QueryNot(new QueryNot(A("ne", "x"))),
            new QueryAnd([new QueryOr([A("in", new[] { "a", "b" }), A("nin", new[] { "c" })]), new QueryNot(new QueryAnd([A("eq", "q"), A("exists", false)]))]),
            new QueryOr([new QueryAnd([A("regex", "a|b"), A("contains", "c d")]), new QueryField("tokens", "gt", 3L)]),
        ];
        foreach (var tree in trees)
        {
            var text = QueryExprText.Print(tree);
            Assert.Equal(Wrap(tree).ToJson(), Wrap(QueryExprText.Parse(text)).ToJson());
        }
    }

    [Theory]
    [InlineData("kind is")]
    [InlineData("kind is a and")]
    [InlineData("(kind is a")]
    [InlineData("kind is a)")]
    [InlineData("tokens > many")]
    [InlineData("and is a")]
    [InlineData("kind banana a")]
    [InlineData("form is \"open")]
    public void MalformedTextIsRefusedWithAPosition(string text)
    {
        Assert.False(QueryExprText.TryParse(text, out _, out var refusal));
        Assert.Equal(RefusalCodes.QueryDocumentInvalid, refusal.Code);
        Assert.Contains("character", refusal.Message);
    }

    [Theory]
    [InlineData("morph(sfx:-an)")]
    [InlineData("kind is a and phon(C V C V)")]
    [InlineData("realizes(root:kaon, generated)")]
    public void ReservedKindsInTextAreRefusedAsNotYetAvailable(string text)
    {
        Assert.False(QueryExprText.TryParse(text, out _, out var refusal));
        Assert.Equal(RefusalCodes.QueryNotYetAvailable, refusal.Code);
    }

    public static TheoryData<string> V1Lines() =>
    [
        "motif query forms",
        "motif query evidence forms --where kind=alternate",
        "motif query forms --as-of 42",
        "motif query forms --as-of 42 --sides both --sort proposal.form",
        "motif query forms --as-of baseline",
        "motif query forms --where kind=alternate",
        "motif query forms --where kind=alternate --as-of baseline",
        "motif query forms --where kind=alternate --group letters.dropped",
        "motif query forms --where kind=alternate --sort form --as-of baseline",
        "motif query forms --where \"form ~ 'a b'\" --where 'kind in lexeme,variant' --min-count 2 --ids",
        "motif query forms --where kind=alternate --where 'change=d>r'",
        "motif query forms --where proposal.parse-state=parsed --as-of 42 --sides both --sort proposal.form:desc",
        "motif query objects --where class=environment --where applied=0",
        "motif query stems --where 'final:2~[aiu]d' --group shape",
        "motif query stems --where \"final:2=ad\" --change 'd>r' --at final --affixes an,un --wordset union --score reliability,tolerance",
        "motif query evidence stems --where \"final:2=ad\" --change 'd>r' --at final --affixes an,un --wordset union",
        "motif query forms --where kind=alternate --group letters.dropped --min-count 3 --examples 2 --raw-letters --fold nfc,case --distinct entry",
        "motif query forms --where 'tokens>15' --where 'environments has /_V'",
    ];

    [Theory]
    [MemberData(nameof(V1Lines))]
    public void EveryV1LineRoundTripsThroughTheCanonicalText(string line)
    {
        Assert.True(QueryDocumentText.TryParse(line, out var document, out var refusal), refusal?.Message);
        var text = QueryDocumentText.Print(document);
        Assert.True(QueryDocumentText.TryParse(text, out var again, out refusal), refusal?.Message);
        Assert.Equal(document.ToJson(), again.ToJson());
        Assert.Equal(text, QueryDocumentText.Print(again));
        Assert.True(QueryDocument.TryParse(document.ToJson(), out var viaJson, out _));
        Assert.Equal(text, QueryDocumentText.Print(viaJson));
    }

    public static TheoryData<string> V2Lines() =>
    [
        "motif query forms --where \"kind is a or kind is b\" --limit 10",
        "motif query forms --where \"not (kind is a and tokens > 5)\" --group kind",
        "motif query forms --where \"form matches \\\"sar.*\\\"\"",
        "motif query forms --where \"(form is \\\"a b\\\")\"",
        "motif query forms --where \"(kind not in a,b)\"",
        "motif query stems --where \"category has N or gloss exists\" --limit 3",
        "motif query uses --where \"target-kind is natural-class or target-kind is environment\" --group user-kind",
    ];

    [Theory]
    [MemberData(nameof(V2Lines))]
    public void EveryV2LineRoundTripsThroughTheCanonicalText(string line) => EveryV1LineRoundTripsThroughTheCanonicalText(line);

    [Fact]
    public void AV1SelectorKeepsItsMeaningWhenItHoldsTheWordsAndOrOr()
    {
        Assert.True(QueryRunLine.TryParse("motif query forms --where \"gloss ~ salt or pepper\"", out var record, out _));
        Assert.Equal([new QueryCondition("gloss", "~", "salt or pepper")], record.Where);
        Assert.Null(record.Filter);
    }

    [Fact]
    public void AnExpressionSelectorBecomesTheRecordsFilterAndAV1OneStaysASelector()
    {
        Assert.True(QueryRunLine.TryParse("motif query forms --where kind=a --where \"kind is b or tokens > 5\" --limit 4", out var record, out _));
        Assert.Equal([new QueryCondition("kind", "is", "a")], record.Where);
        Assert.Equal("kind is b or tokens > 5", QueryExprText.Print(record.Filter!));
        Assert.Equal(4, record.Limit);
    }

    [Fact]
    public void AnAsOfLineKeepsAConjunctionAsSelectorsAndLeavesTheRestAsAFilter()
    {
        Assert.True(QueryRunLine.TryParse("motif query forms --as-of baseline --where \"kind is a and tokens > 5\"", out var conjunction, out _));
        Assert.Equal(2, conjunction.Where.Count);
        Assert.Null(conjunction.Filter);
        Assert.True(QueryRunLine.TryParse("motif query forms --as-of baseline --where \"kind is a or tokens > 5\"", out var either, out _));
        Assert.Empty(either.Where);
        Assert.NotNull(either.Filter);
    }

    [Fact]
    public void AReservedKindInASelectorIsRefusedAsNotYetAvailable()
    {
        Assert.False(QueryRunLine.TryParse("motif query forms --where \"phon(C V)\"", out _, out var refusal));
        Assert.Equal(RefusalCodes.QueryNotYetAvailable, refusal.Code);
    }
}
