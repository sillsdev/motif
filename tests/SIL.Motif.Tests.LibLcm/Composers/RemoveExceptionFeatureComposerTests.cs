using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.Parser;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using static SIL.Motif.Tests.Composers.AffixTestSupport;

namespace SIL.Motif.Tests.Composers;

/// <summary>Checks that one intent takes an exception feature off every rule branch and stem that carries it, and nothing else.</summary>
[Collection(LcmCacheParallelCollections.Group1)]
public sealed class RemoveExceptionFeatureComposerTests(PristineProjectFixture pristine)
{
    private static CanonicalId Phoneme(LcmCache cache, string text) =>
        CanonicalId.FromGuid(cache.LangProject.PhonologicalDataOA.PhonemeSetsOS[0].PhonemesOC
            .SelectMany(phoneme => phoneme.CodesOS.Select(code => (Code: code, Phoneme: phoneme)))
            .Single(pair => pair.Code.Representation.get_String(cache.DefaultVernWs)?.Text == text).Phoneme.Guid);

    private static CanonicalId NewRule(LcmCache cache, string name) =>
        Created(ApplyOps(cache, AuthorPhonologicalRuleComposer.Build(cache, new(name,
            PhonologicalRuleDirection.Simultaneous, [new(Phoneme: Phoneme(cache, "u"))],
            [new(Phoneme: Phoneme(cache, "i"))], [new(Phoneme: Phoneme(cache, "a"))],
            [new(Phoneme: Phoneme(cache, "n"))],
            cache.LangProject.PhonologicalDataOA.PhonRulesOS is { Count: > 0 } rules
                ? new Placement(CanonicalId.FromGuid(rules[^1].Guid), null) : null))), PhPhonDataPhonRulesOperationKinds.Create);

    private static CanonicalId NewFeature(LcmCache cache, string name) =>
        Created(ApplyOps(cache, AuthorExceptionFeatureComposer.Build(cache, new(name, Anal(cache), null))),
            CmPossibilityListPossibilitiesOperationKinds.Create);

    private static bool Carries(LcmCache cache, CanonicalId msa, CanonicalId feature) =>
        Get<IMoStemMsa>(cache, msa).ProdRestrictRC.Any(item => item.Guid == feature.ToGuid());

    private static IPhSegRuleRHS Rhs(LcmCache cache, CanonicalId rule) =>
        ((IPhRegularRule)Get<IPhSegmentRule>(cache, rule)).RightHandSidesOS.Single();

    private (LcmCache Cache, CanonicalId Dead, CanonicalId Live, CanonicalId ExcludingRule, CanonicalId RequiringRule,
        CanonicalId UntouchedRule, CanonicalId[] Stems, CanonicalId Clean) Project()
    {
        var cache = pristine.NewScratch();
        RealParserProject.PrepareForParsing(cache, "u", "i", "a", "n");
        var verb = AddCategory(cache, "Verb");
        var dead = NewFeature(cache, "Retains");
        var live = NewFeature(cache, "Other");
        var excluding = NewRule(cache, "excludes dead");
        var requiring = NewRule(cache, "requires dead and live");
        var untouched = NewRule(cache, "only live");
        ApplyOps(cache, EditPhonologicalRuleFeaturesComposer.Build(cache, new(excluding, ExcludedFeatures: [dead])));
        ApplyOps(cache, EditPhonologicalRuleFeaturesComposer.Build(cache, new(requiring, RequiredFeatures: [dead, live])));
        ApplyOps(cache, EditPhonologicalRuleFeaturesComposer.Build(cache, new(untouched, RequiredFeatures: [live])));
        var stems = Enumerable.Range(0, 3).Select(i => AddStem(cache, $"kaun{new string('n', i)}", verb).Msa).ToArray();
        var clean = AddStem(cache, "tulog", verb).Msa;
        ApplyOps(cache, TagStemsByQueryComposer.Build(cache, new(new StemQuery(FinalLetters: "n"), dead, "affix")));
        ApplyOps(cache, TagStemsByQueryComposer.Build(cache, new(new StemQuery(FinalLetters: "g"), live, "affix")));
        return (cache, dead, live, excluding, requiring, untouched, stems, clean);
    }

    [Fact]
    public void RemovingAFeatureClearsEveryRuleBranchAndStemThatCarriesItAndNothingElse()
    {
        var (cache, dead, live, excluding, requiring, untouched, stems, clean) = Project();
        using var _ = cache;
        Assert.All(stems, stem => Assert.True(Carries(cache, stem, dead)));

        var intent = new RemoveExceptionFeatureIntent(dead, "Retains");
        var operations = RemoveExceptionFeatureComposer.Build(cache, intent);
        Assert.Equal(3, operations.Count(op => op.Kind == MoStemMsaProdRestrictOperationKinds.RemoveRefProdRestrict));
        Assert.Single(operations, op => op.Kind == PhSegRuleRHSExclRuleFeatsOperationKinds.RemoveRefExclRuleFeats);
        Assert.Single(operations, op => op.Kind == PhSegRuleRHSReqRuleFeatsOperationKinds.RemoveRefReqRuleFeats);
        Assert.Equal(5, operations.Count);
        ApplyOps(cache, operations);

        Assert.All(stems, stem => Assert.False(Carries(cache, stem, dead)));
        Assert.True(Carries(cache, clean, live));
        Assert.Empty(Rhs(cache, excluding).ExclRuleFeatsRC);
        Assert.Equal([live.ToGuid()], Rhs(cache, requiring).ReqRuleFeatsRC.Select(w => w.ItemRA.Guid));
        Assert.Equal([live.ToGuid()], Rhs(cache, untouched).ReqRuleFeatsRC.Select(w => w.ItemRA.Guid));
        Assert.Empty(RemoveExceptionFeatureComposer.Build(cache, intent));
    }

    [Fact]
    public void TheSummaryStatesTheCountsFirstAndSaysTheFeatureItemRemains()
    {
        var (cache, dead, _, _, _, _, _, _) = Project();
        using var _c = cache;

        var summary = Assert.Single(RemoveExceptionFeatureComposer.Summarize(cache, new(dead, "Retains")));

        Assert.Equal(RemoveExceptionFeatureComposer.SummaryKind, summary.Kind);
        Assert.StartsWith("3 stems and 2 rule branches carry this feature (1 excluded, 1 required);", summary.Name);
        Assert.Contains("0 other objects", summary.Name);
        Assert.Contains("feature item itself stays", summary.Name);
    }

    [Fact]
    public void AStaleNameOrANonFeatureIsRefused()
    {
        var (cache, dead, _, _, _, _, _, _) = Project();
        using var _c = cache;

        var stale = Assert.Throws<InvalidOperationException>(() =>
            RemoveExceptionFeatureComposer.Build(cache, new(dead, "Wrong")));
        Assert.Contains("not 'Wrong'", stale.Message);
        var notFeature = Assert.Throws<InvalidOperationException>(() =>
            RemoveExceptionFeatureComposer.Build(cache, new(Phoneme(cache, "u"), "u")));
        Assert.Contains("exception features", notFeature.Message);
    }

    [Fact]
    public void TheClosedIntentNamesTheFeatureAndItsExpectedName()
    {
        var feature = CanonicalId.FromGuid(Guid.NewGuid()).Value;
        var parsed = RemoveExceptionFeatureIntentParser.Parse(JsonDocument.Parse(
            $"{{\"feature\":\"{feature}\",\"expectedName\":\"Retains\"}}").RootElement);
        Assert.Equal("Retains", parsed.ExpectedName);
        Assert.ThrowsAny<Exception>(() => RemoveExceptionFeatureIntentParser.Parse(JsonDocument.Parse(
            $"{{\"feature\":\"{feature}\"}}").RootElement));
        Assert.ThrowsAny<Exception>(() => RemoveExceptionFeatureIntentParser.Parse(JsonDocument.Parse(
            $"{{\"feature\":\"{feature}\",\"expectedName\":\"x\",\"extra\":1}}").RootElement));
    }
}
