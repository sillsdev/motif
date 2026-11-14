using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using static SIL.Motif.Tests.Composers.AffixTestSupport;

namespace SIL.Motif.Tests.Composers;

/// <summary>Checks that a query tags the stems it selects, with the polarity that tags fewest where FieldWorks allows both.</summary>
[Collection(LcmCacheParallelCollections.Group1)]
public sealed class TagStemsByQueryComposerTests(PristineProjectFixture pristine)
{
    private static CanonicalId NewFeature(LcmCache cache, string name) =>
        Created(ApplyOps(cache, AuthorExceptionFeatureComposer.Build(cache, new(name, Anal(cache)))),
            CmPossibilityListPossibilitiesOperationKinds.Create);

    private static (LcmCache Cache, CanonicalId Verb, CanonicalId Feature, CanonicalId[] DFinal, CanonicalId[] Others) Project(
        PristineProjectFixture pristine, int dFinal, int others)
    {
        var cache = pristine.NewScratch();
        var verb = AddCategory(cache, "Verb");
        var d = Enumerable.Range(0, dFinal).Select(i => AddStem(cache, $"kaud{i}d", verb).Msa).ToArray();
        var rest = Enumerable.Range(0, others).Select(i => AddStem(cache, $"inum{i}", verb).Msa).ToArray();
        return (cache, verb, NewFeature(cache, "Keeps d"), d, rest);
    }

    private static TagStemsByQueryIntent Intent(CanonicalId verb, CanonicalId feature, string? gate = null, string? action = null) =>
        new(new StemQuery(Category: verb, FinalLetters: "d"), feature, gate, action);

    [Fact]
    public void AnAffixGateTagsTheMatchingStemsAndSaysWhenTheOthersWereFewer()
    {
        var (cache, verb, feature, matching, others) = Project(pristine, dFinal: 3, others: 1);
        using var _ = cache;

        var plan = TagStemsByQueryComposer.Evaluate(cache, Intent(verb, feature, "affix"));

        Assert.Equal(3, plan.Targets.Count);
        Assert.Equal("matching", plan.Polarity);
        Assert.Contains("only require", plan.Limitation);
        ApplyOps(cache, TagStemsByQueryComposer.Build(cache, Intent(verb, feature, "affix")));
        Assert.All(matching, id => Assert.Contains(feature.ToGuid(),
            Get<IMoStemMsa>(cache, id).ProdRestrictRC.Select(item => item.Guid)));
        Assert.All(others, id => Assert.Empty(Get<IMoStemMsa>(cache, id).ProdRestrictRC));
    }

    [Fact]
    public void ARuleGateTagsTheOtherStemsWhenTheyAreFewerAndTheSummaryNamesExclusion()
    {
        var (cache, verb, feature, matching, others) = Project(pristine, dFinal: 4, others: 1);
        using var _ = cache;

        ApplyOps(cache, TagStemsByQueryComposer.Build(cache, Intent(verb, feature, "rule")));

        Assert.All(matching, id => Assert.Empty(Get<IMoStemMsa>(cache, id).ProdRestrictRC));
        Assert.All(others, id => Assert.Contains(feature.ToGuid(),
            Get<IMoStemMsa>(cache, id).ProdRestrictRC.Select(item => item.Guid)));
        var summary = Assert.Single(TagStemsByQueryComposer.Summarize(cache, Intent(verb, feature, "rule")));
        Assert.StartsWith("4 of 5 stems match; tag 1 (others);", summary.Name);
        Assert.Contains("EXCLUDE", summary.Name);
    }

    [Fact]
    public void ARuleGateKeepsTheMatchingStemsWhenTheyAreFewerOrTied()
    {
        var (cache, verb, feature, matching, _) = Project(pristine, dFinal: 1, others: 1);
        using var _c = cache;

        var plan = TagStemsByQueryComposer.Evaluate(cache, Intent(verb, feature, "rule"));

        Assert.Equal("matching", plan.Polarity);
        Assert.Equal(matching.Select(id => id.ToGuid()), plan.Targets.Select(msa => msa.Guid));
        Assert.Contains("REQUIRE", Assert.Single(TagStemsByQueryComposer.Summarize(cache, Intent(verb, feature, "rule"))).Name);
    }

    [Fact]
    public void TaggingTwiceAddsNothingAndUntagRemovesTheFeatureFromMatchingStems()
    {
        var (cache, verb, feature, matching, _) = Project(pristine, dFinal: 2, others: 2);
        using var _c = cache;
        ApplyOps(cache, TagStemsByQueryComposer.Build(cache, Intent(verb, feature)));

        Assert.Empty(TagStemsByQueryComposer.Build(cache, Intent(verb, feature)));
        var removal = TagStemsByQueryComposer.Build(cache, Intent(verb, feature, action: "untag"));
        Assert.Equal(2, removal.Count);
        Assert.All(removal, item => Assert.Equal(MoStemMsaProdRestrictOperationKinds.RemoveRefProdRestrict, item.Kind));
        ApplyOps(cache, removal);
        Assert.All(matching, id => Assert.Empty(Get<IMoStemMsa>(cache, id).ProdRestrictRC));
    }

    [Fact]
    public void CategoryAndExistingFeatureConditionsNarrowTheSelection()
    {
        using var cache = pristine.NewScratch();
        var verb = AddCategory(cache, "Verb");
        var noun = AddCategory(cache, "Noun");
        var kin = NewFeature(cache, "Kin");
        var feature = NewFeature(cache, "Keeps d");
        var verbStem = AddStem(cache, "kaud", verb).Msa;
        var nounStem = AddStem(cache, "tud", noun).Msa;
        ApplyOps(cache, EditExceptionFeaturesComposer.Build(cache, new(verbStem, [kin])));

        Assert.Equal([verbStem.ToGuid()], TagStemsByQueryComposer.Evaluate(cache,
            new(new StemQuery(Category: verb, FinalLetters: "d"), feature)).Matching.Select(msa => msa.Guid));
        Assert.Equal([nounStem.ToGuid()], TagStemsByQueryComposer.Evaluate(cache,
            new(new StemQuery(LacksFeature: kin, FinalLetters: "d"), feature)).Matching.Select(msa => msa.Guid));
        Assert.Equal([verbStem.ToGuid()], TagStemsByQueryComposer.Evaluate(cache,
            new(new StemQuery(HasFeature: kin), feature)).Matching.Select(msa => msa.Guid));
    }

    [Fact]
    public void AQueryWithNoConditionIsRefusedAndTheParserIsClosed()
    {
        using var cache = pristine.NewScratch();
        var feature = NewFeature(cache, "Keeps d");
        Assert.Throws<InvalidOperationException>(() => TagStemsByQueryComposer.Evaluate(cache, new(new StemQuery(), feature)));
        var json = JsonDocument.Parse($$"""{"query":{"finalLetters":"d","bogus":1},"exceptionFeature":"{{feature.Value}}"}""");
        Assert.ThrowsAny<Exception>(() => TagStemsByQueryIntentParser.Parse(json.RootElement));
    }

    [Fact]
    public void StemsAlreadyTaggedOnTheWrongSideLoseTheTagSoTheTaggedSetEqualsTheChosenSide()
    {
        var (cache, verb, feature, matching, others) = Project(pristine, dFinal: 4, others: 2);
        using var _c = cache;
        ApplyOps(cache, EditExceptionFeaturesComposer.Build(cache, new(matching[0], [feature])));
        ApplyOps(cache, EditExceptionFeaturesComposer.Build(cache, new(others[0], [feature])));

        var change = TagStemsByQueryComposer.Build(cache, Intent(verb, feature, "rule"));

        Assert.Equal(1, change.Count(item => item.Kind == MoStemMsaProdRestrictOperationKinds.AddRefProdRestrict));
        Assert.Equal(1, change.Count(item => item.Kind == MoStemMsaProdRestrictOperationKinds.RemoveRefProdRestrict));
        Assert.Contains("1 tags added, 1 removed", Assert.Single(TagStemsByQueryComposer.Summarize(cache, Intent(verb, feature, "rule"))).Name);
        ApplyOps(cache, change);
        var tagged = matching.Concat(others).Where(id =>
            Get<IMoStemMsa>(cache, id).ProdRestrictRC.Any(item => item.Guid == feature.ToGuid())).ToArray();
        Assert.Equal(others.Select(id => id.ToGuid()).Order(), tagged.Select(id => id.ToGuid()).Order());

        var affix = TagStemsByQueryComposer.Build(cache, Intent(verb, feature, "affix"));
        ApplyOps(cache, affix);
        Assert.All(matching, id => Assert.Contains(feature.ToGuid(), Get<IMoStemMsa>(cache, id).ProdRestrictRC.Select(i => i.Guid)));
        Assert.All(others, id => Assert.Empty(Get<IMoStemMsa>(cache, id).ProdRestrictRC));
    }

    [Fact]
    public void ARuleGateThatWouldTagTheOthersWithoutACategoryIsRefused()
    {
        var (cache, verb, feature, _, _) = Project(pristine, dFinal: 4, others: 1);
        using var _c = cache;
        var noCategory = new TagStemsByQueryIntent(new StemQuery(FinalLetters: "d"), feature, "rule");

        var refused = Assert.Throws<InvalidOperationException>(() => TagStemsByQueryComposer.Evaluate(cache, noCategory));
        Assert.Contains("Give 'category'", refused.Message);
        Assert.Equal("matching", TagStemsByQueryComposer.Evaluate(cache, noCategory with { Gate = "affix" }).Polarity);
    }
}
