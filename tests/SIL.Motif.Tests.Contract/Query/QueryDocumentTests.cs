using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Query;
using Xunit;

namespace SIL.Motif.Tests.Contract.Query;

public sealed class QueryDocumentTests
{
    private static readonly QueryForm[] Rows =
    [
        new("a", "sarang", "sadang", "alternate", "e1", "stem", "", ["/_V"], ["V"], 10),
        new("b", "sarang", "sadang", "alternate", "e2", "stem", "", ["/#_C"], ["V"], 30),
        new("c", "sang", "sadang", "variant", "e3", "stem", "", [], ["N"], 20),
    ];

    private static QueryDocument Parse(string json)
    {
        Assert.True(QueryDocument.TryParse(json, out var document, out var refusal), refusal?.Message);
        return document;
    }

    private static string[] Ids(string where)
    {
        var document = Parse("{\"version\":\"motif.query.v2\",\"from\":\"forms\",\"where\":" + where + "}");
        return [.. FormQuery.Run(document.ToRecord(), Rows, ["ng"]).Rows.Select(row => row.Id).Order(StringComparer.Ordinal)];
    }

    private static string Field(string field, string op, string value) =>
        "{\"field\":\"" + field + "\",\"op\":\"" + op + "\",\"value\":" + value + "}";

    [Theory]
    [InlineData("kind", "eq", "\"variant\"", "c")]
    [InlineData("kind", "ne", "\"variant\"", "a,b")]
    [InlineData("kind", "in", "[\"variant\",\"lexeme\"]", "c")]
    [InlineData("kind", "nin", "[\"variant\"]", "a,b")]
    [InlineData("environments", "has", "[\"/_V\"]", "a")]
    [InlineData("tokens", "gt", "15", "b,c")]
    [InlineData("tokens", "lt", "15", "a")]
    [InlineData("form", "regex", "\"sar.*\"", "a,b")]
    [InlineData("form", "regex", "\"sar\"", "")]
    [InlineData("form", "contains", "\"ar\"", "a,b")]
    [InlineData("environments", "exists", "true", "a,b")]
    [InlineData("environments", "exists", "false", "c")]
    public void EveryFieldOpFiltersRows(string field, string op, string value, string expected) =>
        Assert.Equal(expected.Length == 0 ? [] : expected.Split(','), Ids(Field(field, op, value)));

    [Fact]
    public void RegexIsAnchoredAndContainsIsNot()
    {
        Assert.Empty(Ids(Field("form", "regex", "\"ar\"")));
        Assert.Equal(["a", "b"], Ids(Field("form", "contains", "\"ar\"")));
        Assert.Equal(["c"], Ids(Field("form", "regex", "\"sa(ng|n)\"")));
    }

    [Theory]
    [InlineData("x)|(y")]
    [InlineData("sar)|(a")]
    public void AnUnbalancedRegexCannotEscapeTheAnchor(string pattern)
    {
        var document = Parse("{\"version\":\"motif.query.v2\",\"from\":\"forms\",\"where\":" +
            Field("form", "regex", System.Text.Json.JsonSerializer.Serialize(pattern)) + "}");
        Assert.ThrowsAny<ArgumentException>(() => FormQuery.Run(document.ToRecord(), Rows, []));
    }

    [Fact]
    public void BooleanOperatorsCombine()
    {
        var variant = Field("kind", "eq", "\"variant\"");
        var heavy = Field("tokens", "gt", "25");
        Assert.Equal(["b", "c"], Ids("{\"or\":[" + variant + "," + heavy + "]}"));
        Assert.Equal(["b"], Ids("{\"and\":[" + heavy + ",{\"not\":" + variant + "}]}"));
        Assert.Equal(["a", "b"], Ids("{\"not\":" + variant + "}"));
    }

    [Theory]
    [InlineData("tokens", "regex", "\"1\"", "numeric")]
    [InlineData("tokens", "contains", "\"1\"", "numeric")]
    [InlineData("form", "lt", "3", "text")]
    [InlineData("kind", "gt", "3", "text")]
    [InlineData("kind", "has", "[\"x\"]", "text")]
    public void AnOpThatDoesNotFitTheColumnIsATypedRefusal(string field, string op, string value, string type)
    {
        var document = Parse("{\"version\":\"motif.query.v2\",\"from\":\"forms\",\"where\":" + Field(field, op, value) + "}");
        var error = Assert.Throws<QueryRefusalException>(() => FormQuery.Run(document.ToRecord(), Rows, []));
        Assert.Equal(RefusalCodes.QueryOpTypeMismatch, error.Refusal.Code);
        Assert.Equal(type, error.Refusal.Facts["columnType"]);
    }

    [Theory]
    [InlineData("{\"morph\":\"CAUS\"}", "morph(CAUS)")]
    [InlineData("{\"seq\":{\"items\":[\"root:fanya\",\"CAUS\"]}}", "seq(root:fanya, CAUS)")]
    [InlineData("{\"seq\":{\"items\":[\"a\",\"b\",\"c\"],\"adjacent\":true}}", "seq(a, b, c, adjacent=true)")]
    public void MorphAndSeqDocumentsRoundTripAndPrintAsTextAndAreRefusedOnASourceWithNoParse(string body, string text)
    {
        var json = "{\"version\":\"motif.query.v2\",\"from\":\"forms\",\"where\":{\"and\":[" + Field("kind", "eq", "\"variant\"") + "," + body + "]}}";
        var document = Parse(json);
        Assert.Equal(json, document.ToJson());
        Assert.Equal("kind is variant and " + text, QueryExprText.Print(document.ToRecord().Filter!));
        var error = Assert.Throws<QueryRefusalException>(() => FormQuery.Run(document.ToRecord(), Rows, []));
        Assert.Equal(RefusalCodes.QueryNotYetAvailable, error.Refusal.Code);
    }

    [Theory]
    [InlineData("{\"morph\":\"\"}")]
    [InlineData("{\"morph\":3}")]
    [InlineData("{\"seq\":{\"items\":[\"a\"]}}")]
    [InlineData("{\"seq\":{\"items\":[\"a\",\"\"]}}")]
    [InlineData("{\"seq\":{\"items\":[\"a\",\"b\"],\"adjacent\":\"yes\"}}")]
    [InlineData("{\"seq\":{\"items\":[\"a\",\"b\"],\"extra\":1}}")]
    [InlineData("{\"seq\":[\"a\",\"b\"]}")]
    public void MalformedMorphAndSeqDocumentsAreInvalid(string body)
    {
        var json = "{\"version\":\"motif.query.v2\",\"from\":\"forms\",\"where\":" + body + "}";
        Assert.False(QueryDocument.TryParse(json, out _, out var refusal));
        Assert.Equal(RefusalCodes.QueryDocumentInvalid, refusal!.Code);
    }

    [Fact]
    public void RealizesDocumentsRoundTripAndPrintAsText()
    {
        var json = "{\"version\":\"motif.query.v2\",\"from\":\"forms\",\"where\":{\"and\":[" + Field("kind", "eq", "\"variant\"") +
                   ",{\"realizes\":{\"morpheme\":\"sfx:-an\",\"tier\":\"attested\"}}]}}";
        var document = Parse(json);
        Assert.Equal(json, document.ToJson());
        Assert.Equal("kind is variant and realizes(sfx:-an, attested)", QueryExprText.Print(document.Where!));
    }

    [Theory]
    [InlineData("{\"morpheme\":\"sfx:-an\"}")]
    [InlineData("{\"morpheme\":\"sfx:-an\",\"tier\":\"sometimes\"}")]
    [InlineData("{\"morpheme\":\" \",\"tier\":\"attested\"}")]
    [InlineData("{\"morpheme\":\"sfx:-an\",\"tier\":\"attested\",\"more\":1}")]
    public void MalformedRealizesIsRefused(string body)
    {
        var json = "{\"version\":\"motif.query.v2\",\"from\":\"forms\",\"where\":{\"realizes\":" + body + "}}";
        Assert.False(QueryDocument.TryParse(json, out _, out var refusal));
        Assert.Equal(RefusalCodes.QueryDocumentInvalid, refusal!.Code);
    }

    [Fact]
    public void RealizesIsNotRunnableOverAFormsSourceAndSaysSo()
    {
        var document = Parse("{\"version\":\"motif.query.v2\",\"from\":\"forms\",\"where\":{\"realizes\":{\"morpheme\":\"sfx:-an\",\"tier\":\"attested\"}}}");
        var error = Assert.Throws<QueryRefusalException>(() => FormQuery.Run(document.ToRecord(), Rows, []));
        Assert.Equal(RefusalCodes.QueryNotYetAvailable, error.Refusal.Code);
    }

    [Theory]
    [InlineData("phon", "\"Stop V Stop V\"")]
    [InlineData("phon", "\"/p/ ( V )* #\"")]
    [InlineData("env", "\"[Voiced] / # _ V\"")]
    [InlineData("env", "\"V / _\"")]
    public void PhonAndEnvDocumentsRoundTripAndTextMatchesJson(string kind, string body)
    {
        var json = "{\"version\":\"motif.query.v2\",\"from\":\"forms\",\"where\":{\"and\":[" + Field("kind", "eq", "\"variant\"") +
                   ",{\"" + kind + "\":" + body + "}]}}";
        var document = Parse(json);
        Assert.Equal(json, document.ToJson());
        var text = QueryExprText.Print(document.Where!);
        Assert.True(QueryExprText.TryParse(text, out var again, out _));
        Assert.Equal(text, QueryExprText.Print(again!));
    }

    [Fact]
    public void PhonTakesNoScopeFieldAndNoScopePrefix()
    {
        const string json = "{\"version\":\"motif.query.v2\",\"from\":\"forms\",\"where\":{\"phon\":\"V\",\"where\":\"stem\"}}";
        Assert.False(QueryDocument.TryParse(json, out _, out var refusal));
        Assert.Equal(RefusalCodes.QueryDocumentInvalid, refusal.Code);
        Assert.True(QueryExprText.TryParse("phon(stem: V)", out var expr, out _));
        Assert.Equal("stem: V", Assert.IsType<QueryPhon>(expr).Pattern);
    }

    [Theory]
    [InlineData("phon", "\"\"")]
    [InlineData("phon", "\"( V\"")]
    [InlineData("phon", "\"V )\"")]
    [InlineData("env", "\"V\"")]
    [InlineData("env", "\" / _ \"")]
    public void MalformedPatternsAreRefusedWithAPosition(string kind, string body)
    {
        var json = "{\"version\":\"motif.query.v2\",\"from\":\"forms\",\"where\":{\"" + kind + "\":" + body + "}}";
        Assert.False(QueryDocument.TryParse(json, out _, out var refusal));
        Assert.Equal(RefusalCodes.QueryDocumentInvalid, refusal.Code);
    }

    [Theory]
    [InlineData("{\"version\":\"motif.query.v1\",\"from\":\"forms\"}")]
    [InlineData("{\"version\":\"motif.query.v2\",\"from\":\"nope\"}")]
    [InlineData("{\"version\":\"motif.query.v2\",\"from\":\"forms\",\"surprise\":1}")]
    [InlineData("{\"version\":\"motif.query.v2\",\"from\":\"forms\",\"where\":{\"and\":[]}}")]
    [InlineData("{\"version\":\"motif.query.v2\",\"from\":\"forms\",\"where\":{\"field\":\"kind\",\"op\":\"in\",\"value\":\"x\"}}")]
    [InlineData("{\"version\":\"motif.query.v2\",\"from\":\"forms\",\"where\":{\"field\":\"kind\",\"op\":\"like\",\"value\":\"x\"}}")]
    [InlineData("{\"version\":\"motif.query.v2\",\"from\":\"forms\",\"where\":{\"bogus\":1}}")]
    [InlineData("[]")]
    [InlineData("{")]
    public void AMalformedDocumentIsRefused(string json)
    {
        Assert.False(QueryDocument.TryParse(json, out _, out var refusal));
        Assert.Equal(RefusalCodes.QueryDocumentInvalid, refusal.Code);
    }

    [Fact]
    public void LimitSortAndOptionsLowerToTheRecord()
    {
        var document = Parse("{\"version\":\"motif.query.v2\",\"from\":\"forms\",\"side\":\"baseline\",\"sides\":null,\"limit\":7," +
                             "\"sort\":[{\"field\":\"tokens\",\"dir\":\"desc\"}],\"group\":[\"kind\"],\"minCount\":2,\"examples\":3," +
                             "\"rawLetters\":true,\"where\":" + Field("kind", "eq", "\"variant\"") + "}");
        var record = document.ToRecord();
        Assert.Equal("baseline", record.AsOf);
        Assert.Equal("tokens:desc", record.Sort);
        Assert.Equal(7, record.Limit);
        Assert.Equal((2, 3, true), (record.MinCount, record.Examples, record.RawLetters));
        Assert.Equal([new QueryCondition("kind", "is", "variant")], record.Where);
        Assert.Null(record.Filter);
    }

    [Fact]
    public void ADocumentRoundTripsThroughItsCanonicalJson()
    {
        var json = Parse("{\"version\":\"motif.query.v2\",\"from\":\"stems\",\"limit\":3,\"where\":{\"or\":[" +
                         Field("category", "has", "[\"N\"]") + ",{\"not\":" + Field("gloss", "exists", "true") + "}]},\"group\":[\"category\"]}").ToJson();
        Assert.Equal(json, Parse(json).ToJson());
    }
}
