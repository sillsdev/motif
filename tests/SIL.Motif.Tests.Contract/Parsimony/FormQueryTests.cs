using SIL.Motif.Contract.Query;
using SIL.Motif.Tests.QueryScale;
using Xunit;

namespace SIL.Motif.Tests.Contract.Parsimony;

public sealed class FormQueryTests
{
    private static readonly QueryForm[] Rows =
    [
        new("a", "sarang", "sadang", "alternate", "e1", "stem", "", ["/_V"], ["V"], 10),
        new("b", "sarang", "sadang", "alternate", "e2", "stem", "", ["/#_C"], ["V"], 30),
        new("c", "sang", "sadang", "variant", "e3", "stem", "", [], ["N"], 20)
    ];

    private static QueryRecord Query(string col, string op, string value) =>
        new("forms", [new(col, op, value)], []);

    [Theory]
    [InlineData("kind", "is", "variant", 1)]
    [InlineData("kind", "not", "variant", 2)]
    [InlineData("kind", "in", "variant, alternate", 3)]
    [InlineData("environments", "has", "/_V", 1)]
    [InlineData("form", "~", "^sara", 2)]
    [InlineData("tokens", ">", "15", 2)]
    [InlineData("tokens", "<", "15", 1)]
    [InlineData("change", "is", "d>r", 2)]
    public void SelectorsFilterRows(string col, string op, string value, int count) =>
        Assert.Equal(count, FormQuery.Run(Query(col, op, value), Rows, ["ng"]).Rows.Count);

    [Fact]
    public void GroupingCountsEachLetterOnceAndOrdersExamplesByForm()
    {
        var query = new QueryRecord("forms", [], ["letters.dropped"], Examples: 2);
        var result = FormQuery.Run(query, Rows, ["ng"]);
        var d = Assert.Single(result.Groups, g => g.Key[0] == "d");
        Assert.Equal(3, d.Count);
        Assert.Equal(new[] { "c", "a" }, d.Examples.Select(x => x.Id));
    }

    [Fact]
    public void UnavailableTokenFrequencyIsNotReportedAsZero()
    {
        QueryForm[] rows = [new("id", "form", "base", "alternate", "entry", "", "", [], [])];
        var group = Assert.Single(FormQuery.Run(new QueryRecord("forms", [], ["kind"]), rows, []).Groups);
        Assert.Null(group.Tokens);
        Assert.Null(Assert.Single(group.Examples).Tokens);
    }

    [Fact]
    public void GroupIdsRespectMinimumCountAndTiesSelectLowestId()
    {
        var rows = Rows.Select(row => row with { Tokens = 10 }).ToArray();
        var query = new QueryRecord("forms", [], ["kind"], MinCount: 2, Examples: 1, Ids: true);
        var result = FormQuery.Run(query, rows, ["ng"]);
        Assert.Equal(new[] { "a", "b" }, result.Ids.Order(StringComparer.Ordinal));
        Assert.Equal("a", Assert.Single(Assert.Single(result.Groups).Examples).Id);
    }

    [Fact]
    public void GraphemesAreSegmentsAndRawLettersUsesCharacters()
    {
        QueryForm[] rows = [new("x", "a", "ang", "alternate", "e", "", "", [], [])];
        var query = new QueryRecord("forms", [], ["letters.dropped"]);
        Assert.Equal("ng", Assert.Single(FormQuery.Run(query, rows, ["ng"]).Groups).Key[0]);
        Assert.Equal(new[] { "g", "n" }, FormQuery.Run(query with { RawLetters = true }, rows, ["ng"])
            .Groups.Select(g => g.Key[0]).Order().ToArray());
    }

    [Fact]
    public void AHighCardinalityRowIsRefusedBeforeItsCombinationsAreAllocated()
    {
        var row = Rows[0] with { Environments = Enumerable.Range(0, 1001).Select(i => i.ToString()).ToArray() };
        var error = Assert.ThrowsAny<ArgumentException>(() => FormQuery.Run(
            new QueryRecord("forms", [], ["environments"]), [row], []));
        Assert.Contains("environments", error.Message);
    }

    [Fact]
    public void GroupingRefusesMoreThanOneHundredThousandDistinctGroups()
    {
        var rows = Enumerable.Range(0, 101).Select(i => Rows[0] with
        {
            Id = i.ToString(),
            Category = [i.ToString()],
            Environments = Enumerable.Range(0, 1000).Select(j => j.ToString()).ToArray()
        }).ToArray();
        Assert.Throws<QueryTooManyGroupsException>(() => FormQuery.Run(
            new QueryRecord("forms", [], ["category", "environments"], Examples: 0), rows, []));
    }

    [Fact]
    public void AbstractChangeUsesTheSamePhonemeDifference()
    {
        var row = Rows[0] with { Base = "ang", Form = "an" };
        var result = FormQuery.Run(new QueryRecord("forms", [], []), [row], ["ng"]);
        var change = Assert.Single(result.Rows).Change!;
        Assert.Equal("ng", change.Dropped);
        Assert.Equal("n", change.Gained);
        Assert.Equal("ng>n@final", change.Abstract);
    }

    [Theory]
    [InlineData("form~^a=b$", "form", "~", "^a=b$")]
    [InlineData("form=word in phrase", "form", "is", "word in phrase")]
    [InlineData("change.gained=", "change.gained", "is", "")]
    public void SelectorValuesAreNotParsedAsOperators(string text, string col, string op, string value) =>
        Assert.Equal(new QueryCondition(col, op, value), QueryConditionParser.Parse(text));

    [Fact]
    public void InvalidRecordsAndUnsafeRegexAreRefused()
    {
        Assert.Throws<ArgumentException>(() => FormQuery.Run(Query("missing", "is", "x"), Rows, []));
        Assert.Throws<ArgumentException>(() => FormQuery.Run(Query("form", "has", "x"), Rows, []));
        Assert.Throws<ArgumentException>(() => FormQuery.Run(Query("tokens", "~", "1"), Rows, []));
        Assert.Throws<ArgumentException>(() => FormQuery.Run(Query("form", "~", new string('a', 257)), Rows, []));
        Assert.Throws<ArgumentException>(() => FormQuery.Run(Query("form", "~", "(a)\\1"), Rows, []));
    }
    [SlowScaleFact]
    [Trait("Category", "SlowScale")]
    public void ThirtyOneThousandFormsGroupWithinOneSecond()
    {
        var rows = Enumerable.Range(0, 31000).Select(i => new QueryForm(i.ToString(), "sarang", "sadang",
            "alternate", i.ToString(), "stem", "", [], [], i)).ToArray();
        var query = new QueryRecord("forms", [new("change", "is", "d>r")], ["change.right"], Examples: 5);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var result = FormQuery.Run(query, rows, ["ng"]);
        watch.Stop();
        Assert.Equal(31000, Assert.Single(result.Groups).Count);
        Assert.True(watch.ElapsedMilliseconds < 1000, $"Query took {watch.ElapsedMilliseconds} ms");
    }

}
