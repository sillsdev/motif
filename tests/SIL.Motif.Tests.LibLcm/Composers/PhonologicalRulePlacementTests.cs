using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using static SIL.Motif.Tests.Composers.AffixTestSupport;

namespace SIL.Motif.Tests.Composers;

/// <summary>One anchor places a rule anywhere in a long order, and a refusal prints the order it checked.</summary>
[Collection(LcmCacheParallelCollections.Group2)]
public sealed class PhonologicalRulePlacementTests(PristineProjectFixture pristine)
{
    private const int RuleCount = 10;

    private LcmCache Project(out CanonicalId[] ids)
    {
        var cache = pristine.NewScratch();
        RealParserProject.PrepareForParsing(cache, "d", "h", "m", "a");
        var rules = new List<CanonicalId>();
        for (var i = 1; i <= RuleCount; i++)
            rules.Add(Author(cache, $"R{i}", rules.Count == 0 ? null : new Placement(rules[^1], null)));
        ids = rules.ToArray();
        return cache;
    }

    private static CanonicalId Author(LcmCache cache, string name, Placement? placement)
    {
        var phonemes = RealParserReadings.Phonemes(cache);
        var intent = new AuthorPhonologicalRuleIntent(name, PhonologicalRuleDirection.Simultaneous,
            [new(Phoneme: phonemes["h"])], [new(Phoneme: phonemes["m"])], [], [], placement);
        return Created(ApplyOps(cache, AuthorPhonologicalRuleComposer.Build(cache, intent)),
            PhPhonDataPhonRulesOperationKinds.Create);
    }

    private static string[] Names(LcmCache cache) => cache.LangProject.PhonologicalDataOA.PhonRulesOS
        .Select(rule => ((IPhSegmentRule)rule).Name.BestAnalysisAlternative.Text).ToArray();

    private static string[] Expected(int slot)
    {
        var names = Enumerable.Range(1, RuleCount).Select(i => $"R{i}").ToList();
        names.Insert(slot, "new");
        return names.ToArray();
    }

    [Theory]
    [InlineData("before", 9, 9)]
    [InlineData("after", 8, 9)]
    [InlineData("before", 0, 0)]
    [InlineData("after", 9, 10)]
    [InlineData("before", 4, 4)]
    public void OneAnchorPlacesTheRuleInTheRightSlotOfTenRules(string side, int anchorIndex, int slot)
    {
        using var cache = Project(out var ids);
        var anchor = ids[anchorIndex];

        Author(cache, "new", side == "before" ? new Placement(null, anchor) : new Placement(anchor, null));

        Assert.Equal(Expected(slot), Names(cache));
    }

    [Fact]
    public void TheStoredPlacementCarriesBothDerivedNeighbours()
    {
        using var cache = Project(out var ids);
        var phonemes = RealParserReadings.Phonemes(cache);
        var intent = new AuthorPhonologicalRuleIntent("new", PhonologicalRuleDirection.Simultaneous,
            [new(Phoneme: phonemes["h"])], [new(Phoneme: phonemes["m"])], [], [], new Placement(null, ids[9]));

        var create = AuthorPhonologicalRuleComposer.Build(cache, intent)
            .Single(operation => operation.Kind == PhPhonDataPhonRulesOperationKinds.Create);

        Assert.Equal(ids[8], create.Placement!.After);
        Assert.Equal(ids[9], create.Placement.Before);
    }

    [Fact]
    public void AStaleAnchorIsRefusedWithTheLiveOrderAndTheAnchorsGiven()
    {
        using var cache = Project(out var ids);
        var stale = CanonicalId.Mint();

        var refusal = Assert.Throws<InvalidOperationException>(() => Author(cache, "new", new Placement(null, stale)));

        Assert.Contains("anchors are stale or are not adjacent", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("not in the live order", refusal.Message, StringComparison.Ordinal);
        Assert.Contains(stale.Value, refusal.Message, StringComparison.Ordinal);
        Assert.Contains($"1. {ids[0].Value} 'R1'", refusal.Message, StringComparison.Ordinal);
        Assert.Contains($"10. {ids[9].Value} 'R10'", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TwoNonAdjacentAnchorsAreRefusedWithTheLiveOrder()
    {
        using var cache = Project(out var ids);

        var refusal = Assert.Throws<InvalidOperationException>(() =>
            Author(cache, "new", new Placement(ids[1], ids[5])));

        Assert.Contains("the two anchors are not adjacent", refusal.Message, StringComparison.Ordinal);
        Assert.Contains($"6. {ids[5].Value} 'R6'", refusal.Message, StringComparison.Ordinal);
        Assert.Equal(RuleCount, cache.LangProject.PhonologicalDataOA.PhonRulesOS.Count);
    }

    [Fact]
    public void AMetathesisRuleTakesOneAnchorInTheMiddleToo()
    {
        using var cache = Project(out var ids);
        var phonemes = RealParserReadings.Phonemes(cache);

        var ops = AuthorMetathesisRuleComposer.Build(cache, new("swap", PhonologicalRuleDirection.Simultaneous, [],
            new(Phoneme: phonemes["h"]), null, new(Phoneme: phonemes["m"]), [], Placement: new Placement(ids[3], null)));

        var create = ops.Single(operation => operation.Kind == PhPhonDataPhonRulesOperationKinds.Create);
        Assert.Equal(ids[3], create.Placement!.After);
        Assert.Equal(ids[4], create.Placement.Before);
    }
}
