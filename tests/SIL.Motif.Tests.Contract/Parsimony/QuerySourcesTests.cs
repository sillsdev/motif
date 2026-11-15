using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Query;
using Xunit;

namespace SIL.Motif.Tests.Contract.Parsimony;

public sealed class QuerySourcesTests
{
    private static QueryRow Stem(string id, string form, string[] category, string[] features, string gloss = "go", int msas = 1, int alternates = 0) =>
        new(id, [[form], ["e-" + id], category, features, [gloss], [msas.ToString()], [alternates.ToString()]]);

    private static readonly QueryRow[] Stems =
    [
        Stem("s1", "kaldad", ["V"], ["CLF"]),
        Stem("s2", "Kaldad", ["V"], [], msas: 2),
        Stem("s3", "nga", ["none"], ["CLF", "X"], gloss: "none", alternates: 3),
        Stem("s4", "ngang", ["N"], []),
    ];

    private static QueryResult Run(QueryRecord query, params string[] graphemes) => FormQuery.RunRows(query, Stems, graphemes);

    [Theory]
    [InlineData("shape", "is", "CVCCVC", "kaldad")]
    [InlineData("syllables", "<", "2", "nga")]
    [InlineData("final:2", "~", "^[aiu]d$", "kaldad")]
    [InlineData("initial:1", "is", "ng", "nga")]
    [InlineData("category", "is", "none", "nga")]
    [InlineData("features", "has", "X", "nga")]
    [InlineData("alternates", ">", "0", "nga")]
    [InlineData("msa-count", ">", "1", "Kaldad")]
    public void ColumnsSelectStems(string col, string op, string value, string expected)
    {
        var items = Run(new QueryRecord("stems", [new(col, op, value)], []), "ng").Items!;
        Assert.Contains(items, item => item.Values["form"][0] == expected);
    }

    [Fact]
    public void AGraphemeTableMakesNgOneConsonantSegment()
    {
        var withTable = Run(new QueryRecord("stems", [new("form", "is", "nga")], [], Examples: 0), "ng").Items!.Single();
        var shape = Run(new QueryRecord("stems", [], ["shape"]), "ng").Groups.ToDictionary(g => g.Key[0], g => g.Count);
        Assert.Equal("nga", withTable.Values["form"][0]);
        Assert.Equal(2, shape["CVCCVC"]);
        Assert.Equal(1, shape["CV"]);
        Assert.Equal(1, Run(new QueryRecord("stems", [], ["shape"]), "ng").Groups.Single(g => g.Key[0] == "CVC").Count);
        Assert.Equal(1, Run(new QueryRecord("stems", [], ["shape"], RawLetters: true)).Groups.Single(g => g.Key[0] == "CCV").Count);
    }

    [Fact]
    public void FoldGroupsSpellingsThatDifferOnlyInTheNamedSet()
    {
        var groups = Run(new QueryRecord("stems", [], ["fold"], MinCount: 2, Distinct: "form")).Groups;
        Assert.Equal("kaldad", Assert.Single(groups).Key[0]);
        Assert.Equal(2, groups[0].Count);
        Assert.Empty(Run(new QueryRecord("stems", [], ["fold"], MinCount: 2, Distinct: "form", Fold: "nfc")).Groups);
    }

    [Theory]
    [InlineData("iy~y", "diyos", "dyos")]
    [InlineData("o~u", "ola", "ula")]
    [InlineData("e~i", "ela", "ila")]
    [InlineData("c~k", "cala", "kala")]
    [InlineData("a>o,nfc", "ala", "olo")]
    public void NamedFoldSetsRewriteTheKey(string spec, string form, string folded) =>
        Assert.Equal(folded, QueryFold.Compile(spec)(form));

    [Fact]
    public void AnUnknownFoldSetOrColumnIsRefused()
    {
        Assert.Throws<ArgumentException>(() => QueryFold.Compile("zz"));
        Assert.Throws<ArgumentException>(() => Run(new QueryRecord("stems", [new("final:9", "is", "x")], [])));
        Assert.Throws<ArgumentException>(() => Run(new QueryRecord("stems", [new("tokens", ">", "1")], [])));
        Assert.Throws<ArgumentException>(() => Run(new QueryRecord("stems", [new("category", "~", "(")], [])));
    }

    [Fact]
    public void TheProjectFoldTokenIsReplacedByTheProjectRules() =>
        Assert.Equal("nfc,case,iy>y", QueryFold.ResolveProject("nfc,case,project", [new FoldRule("iy", "y")]));

    [Fact]
    public void ObjectsGroupAndCountWithoutAJoin()
    {
        QueryRow[] objects =
        [
            new("e1", [["environment"], ["/_V"], ["0"], ["0"], [], [], ["a‖b"]]),
            new("e2", [["environment"], ["/_C"], ["2"], ["0"], [], [], ["a‖b"]]),
            new("e3", [["environment"], ["/_N"], ["1"], ["1"], [], [], ["c‖d"]]),
            new("f1", [["exception-feature"], ["CLF"], ["0"], ["0"], ["2"], ["1"], []]),
        ];
        var duplicates = FormQuery.RunRows(new QueryRecord("objects", [new("class", "is", "environment")], ["key"], MinCount: 2), objects, []);
        Assert.Equal(["e2", "e1"], Assert.Single(duplicates.Groups).Samples!.Select(s => s.Id));
        var unused = FormQuery.RunRows(new QueryRecord("objects", [new("applied", "is", "0")], [], Ids: true), objects, []);
        Assert.Equal(["e1", "e2", "f1"], unused.Ids);
        var features = FormQuery.RunRows(new QueryRecord("objects", [new("users", ">", "0"), new("carriers", "<", "4")], []), objects, []);
        Assert.Equal("f1", Assert.Single(features.Items!).Id);
    }

    [Fact]
    public void FormsGainTheTextColumns()
    {
        QueryForm[] rows = [new("a", "sarang", "sadang", "alternate", "e1", "stem", "", [], [], 1)];
        var shape = Assert.Single(FormQuery.Run(new QueryRecord("forms", [], ["shape", "final:2"]), rows, ["ng"]).Groups);
        Assert.Equal(["CVCVC", "ang"], shape.Key);
    }

    [Fact]
    public void EverySavedQueryIsAClosedValidRecord()
    {
        Assert.Equal(["P-env-duplicate", "P-feature-few-carriers", "P-lexeme-near-duplicate", "P-msa-no-category", "P-statement-unused"], SavedQueries.Ids);
        foreach (var id in SavedQueries.Ids)
        {
            Assert.True(SavedQueries.TryGet(id, out var query));
            if (query.From == "forms") FormQuery.Run(query with { Fold = QueryFold.ResolveProject(query.Fold, []) }, [], []);
            else FormQuery.RunRows(query, [], []);
        }
    }

    [Fact]
    public void TheParserKeepsAColonInAColumnName()
    {
        var condition = QueryConditionParser.Parse("final:2~[aiu]d");
        Assert.Equal(new QueryCondition("final:2", "~", "[aiu]d"), condition);
    }
}
