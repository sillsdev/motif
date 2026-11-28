using System.Text.Json.Nodes;
using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Mcp;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Mcp;

/// <summary>The grammar read fetches one object by id or exact name and says where the next page starts.</summary>
[Collection(LcmCacheParallelCollections.Group3)]
public sealed class GrammarLookupTests(PristineProjectFixture pristine) : IDisposable
{
    private readonly LcmCache _cache = pristine.NewScratch();

    public void Dispose() => _cache.Dispose();

    private Guid[] AddCategories(params string[] names)
    {
        var ids = new List<Guid>();
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            foreach (var name in names)
            {
                var category = _cache.ServiceLocator.GetInstance<IPartOfSpeechFactory>().Create();
                _cache.LangProject.PartsOfSpeechOA.PossibilitiesOS.Add(category);
                category.Name.set_String(_cache.DefaultAnalWs, name);
                category.Abbreviation.set_String(_cache.DefaultAnalWs, name[..1].ToLowerInvariant());
                ids.Add(category.Guid);
            }
        });
        return ids.ToArray();
    }

    private static string[] Names(JsonObject result, bool keepOrder = false)
    {
        var names = result["items"]!.AsArray().Select(item => item!["name"]!.GetValue<string>());
        return (keepOrder ? names : names.Order(StringComparer.Ordinal)).ToArray();
    }

    [Fact]
    public void ExactEqualsTheNameOrAbbreviationWhereAQueryOnlyContainsIt()
    {
        AddCategories("Noun", "Nounish", "Verb");

        Assert.Equal(["Noun", "Nounish"], Names(GrammarReader.Read(_cache, "categories", "noun", false))
            .Where(name => name.StartsWith("Noun", StringComparison.Ordinal)));
        Assert.Equal(["Noun"], Names(GrammarReader.Read(_cache, "categories", null, false, exact: "noun")));
        Assert.Equal(["Verb"], Names(GrammarReader.Read(_cache, "categories", null, false, exact: "v")));
    }

    [Fact]
    public void AnIdFetchesTheOneObjectAndAnUnknownIdFindsNone()
    {
        var ids = AddCategories("Noun", "Verb");
        var id = GrammarReader.Id(_cache.ServiceLocator.GetInstance<IPartOfSpeechRepository>().GetObject(ids[1]));

        var one = GrammarReader.Read(_cache, "categories", null, false, id: id);
        Assert.Equal(1, one["total"]!.GetValue<int>());
        Assert.Equal(["Verb"], Names(one));
        Assert.Equal(0, GrammarReader.Read(_cache, "categories", null, false, id: "AaHnUob1K5UlJwJZtEhh_w")["total"]!.GetValue<int>());
    }

    [Fact]
    public void PagesCarryTheTotalAndANextCursorUntilTheLastPage()
    {
        AddCategories("A1", "B1", "C1");

        var first = GrammarReader.Page(GrammarReader.Read(_cache, "categories", null, false), 0, 2);
        Assert.Equal(2, first["returned"]!.GetValue<int>());
        Assert.Equal(SIL.Motif.Contract.Responses.ResultPaging.Cursor(2), first["nextCursor"]!.GetValue<string>());
        Assert.True(first["total"]!.GetValue<int>() >= 3);
        var last = GrammarReader.Page(GrammarReader.Read(_cache, "categories", null, false), first["total"]!.GetValue<int>() - 1, 2);
        Assert.Equal(1, last["returned"]!.GetValue<int>());
        Assert.False(last.ContainsKey("nextCursor"));
    }

    [Fact]
    public void PagesConcatenatedAreTheUnpagedListInOrderWithNoGapOrRepeat()
    {
        AddCategories("Noun", "Verb", "Adjective", "Adverb", "Particle");
        var expected = Names(GrammarReader.Read(_cache, "categories", null, false), keepOrder: true);

        var paged = new List<string>();
        var offset = 0;
        while (true)
        {
            var page = GrammarReader.Page(GrammarReader.Read(_cache, "categories", null, false), offset, 2);
            paged.AddRange(Names(page, keepOrder: true));
            if (page["nextCursor"] is not { } next) break;
            Assert.True(SIL.Motif.Contract.Responses.ResultPaging.TryOffset(next.GetValue<string>(), out offset));
        }

        Assert.Equal(expected, paged);
    }

    [Fact]
    public void PhonologicalRulesKeepApplicationOrderAndCarryTheirPosition()
    {
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            foreach (var name in new[] { "Zeta", "Alpha" })
            {
                var rule = _cache.ServiceLocator.GetInstance<IPhRegularRuleFactory>().Create();
                _cache.LangProject.PhonologicalDataOA.PhonRulesOS.Add(rule);
                rule.Name.set_String(_cache.DefaultAnalWs, name);
            }
        });

        var items = GrammarReader.Read(_cache, "phonological_rules", null, false)["items"]!.AsArray();
        Assert.Equal(["Zeta", "Alpha"], items.Select(item => item!["name"]!.GetValue<string>()).ToArray());
        Assert.Equal([1, 2], items.Select(item => item!["position"]!.GetValue<int>()).ToArray());
    }
}
