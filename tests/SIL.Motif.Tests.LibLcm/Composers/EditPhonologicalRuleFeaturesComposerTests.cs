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

/// <summary>Checks that a rule's required and excluded features are set exactly through rule feature items.</summary>
[Collection(LcmCacheParallelCollections.Group1)]
public sealed class EditPhonologicalRuleFeaturesComposerTests(PristineProjectFixture pristine)
{
    private static CanonicalId Phoneme(LcmCache cache, string text) =>
        CanonicalId.FromGuid(cache.LangProject.PhonologicalDataOA.PhonemeSetsOS[0].PhonemesOC
            .SelectMany(phoneme => phoneme.CodesOS.Select(code => (Code: code, Phoneme: phoneme)))
            .Single(pair => pair.Code.Representation.get_String(cache.DefaultVernWs)?.Text == text).Phoneme.Guid);

    private static CanonicalId NewRule(LcmCache cache) =>
        Created(ApplyOps(cache, AuthorPhonologicalRuleComposer.Build(cache, new("u becomes i between a and n",
            PhonologicalRuleDirection.Simultaneous, [new(Phoneme: Phoneme(cache, "u"))],
            [new(Phoneme: Phoneme(cache, "i"))], [new(Phoneme: Phoneme(cache, "a"))],
            [new(Phoneme: Phoneme(cache, "n"))]))), PhPhonDataPhonRulesOperationKinds.Create);

    private static CanonicalId NewFeature(LcmCache cache, string name) =>
        Created(ApplyOps(cache, AuthorExceptionFeatureComposer.Build(cache, new(name, Anal(cache), null))),
            CmPossibilityListPossibilitiesOperationKinds.Create);

    [Fact]
    public void ExcludingAFeatureWrapsItOnceAndClearingRemovesTheReference()
    {
        using var cache = pristine.NewScratch();
        RealParserProject.PrepareForParsing(cache, "u", "i", "a", "n");
        var rule = NewRule(cache);
        var keep = NewFeature(cache, "Keep");

        var operations = EditPhonologicalRuleFeaturesComposer.Build(cache, new(rule, ExcludedFeatures: [keep]));
        Assert.Contains(operations, op => op.Kind == PhPhonRuleFeatItemOperationKinds.SetItem);
        Assert.Contains(operations, op => op.Kind == PhSegRuleRHSExclRuleFeatsOperationKinds.AddRefExclRuleFeats);
        ApplyOps(cache, operations);

        var rhs = ((IPhRegularRule)Get<IPhSegmentRule>(cache, rule)).RightHandSidesOS.Single();
        var wrapper = Assert.Single(rhs.ExclRuleFeatsRC);
        Assert.Equal(keep.ToGuid(), wrapper.ItemRA.Guid);
        Assert.Empty(rhs.ReqRuleFeatsRC);

        var again = EditPhonologicalRuleFeaturesComposer.Build(cache, new(rule, ExcludedFeatures: [keep]));
        Assert.Empty(again);

        var cleared = EditPhonologicalRuleFeaturesComposer.Build(cache, new(rule, ExcludedFeatures: []));
        Assert.Equal([PhSegRuleRHSExclRuleFeatsOperationKinds.RemoveRefExclRuleFeats], cleared.Select(op => op.Kind));
        ApplyOps(cache, cleared);
        Assert.Empty(rhs.ExclRuleFeatsRC);

        var required = EditPhonologicalRuleFeaturesComposer.Build(cache, new(rule, RequiredFeatures: [keep]));
        Assert.DoesNotContain(required, op => op.Kind == PhPhonRuleFeatItemOperationKinds.SetItem);
    }

    [Fact]
    public void ARuleCannotBothRequireAndExcludeAFeatureAndAnUnknownItemIsRefused()
    {
        using var cache = pristine.NewScratch();
        RealParserProject.PrepareForParsing(cache, "u", "i", "a", "n");
        var rule = NewRule(cache);
        var keep = NewFeature(cache, "Keep");

        var overlap = Assert.Throws<InvalidOperationException>(() => EditPhonologicalRuleFeaturesComposer.Build(cache,
            new(rule, RequiredFeatures: [keep], ExcludedFeatures: [keep])));
        Assert.Contains("both required and excluded", overlap.Message);

        var unknown = Assert.Throws<InvalidOperationException>(() => EditPhonologicalRuleFeaturesComposer.Build(cache,
            new(rule, RequiredFeatures: [Phoneme(cache, "u")])));
        Assert.Contains("neither an exception feature nor an inflection class", unknown.Message);

        var empty = Assert.Throws<InvalidOperationException>(() => EditPhonologicalRuleFeaturesComposer.Build(cache,
            new(rule)));
        Assert.Contains("requiredFeatures", empty.Message);
    }

    private static readonly string Rule = CanonicalId.FromGuid(Guid.NewGuid()).Value;

    [Fact]
    public void TheClosedIntentNamesTheRuleAndAtLeastOneSet()
    {
        var parsed = EditPhonologicalRuleFeaturesIntentParser.Parse(JsonDocument.Parse(
            $"{{\"rule\":\"{Rule}\",\"requiredFeatures\":[]}}").RootElement);
        Assert.Empty(parsed.RequiredFeatures!);
        Assert.Null(parsed.ExcludedFeatures);
        Assert.ThrowsAny<Exception>(() => EditPhonologicalRuleFeaturesIntentParser.Parse(
            JsonDocument.Parse($"{{\"rule\":\"{Rule}\"}}").RootElement));
        Assert.ThrowsAny<Exception>(() => EditPhonologicalRuleFeaturesIntentParser.Parse(
            JsonDocument.Parse($"{{\"rule\":\"{Rule}\",\"extra\":1}}").RootElement));
    }
}
