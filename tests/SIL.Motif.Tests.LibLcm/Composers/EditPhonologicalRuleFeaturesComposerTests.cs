using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
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

    private static CanonicalId NewCategory(LcmCache cache, string name) =>
        Created(ApplyOps(cache, AuthorPartOfSpeechComposer.Build(cache,
            new AuthorPartOfSpeechIntent(name, Anal(cache), name[..1]))), CmPossibilityListPossibilitiesOperationKinds.Create);

    [Fact]
    public void ACategoryLimitsTheRuleAndAnEmptyListClearsIt()
    {
        using var cache = pristine.NewScratch();
        RealParserProject.PrepareForParsing(cache, "u", "i", "a", "n");
        var rule = NewRule(cache);
        var verb = NewCategory(cache, "Verb");
        var noun = NewCategory(cache, "Noun");

        ApplyOps(cache, EditPhonologicalRuleFeaturesComposer.Build(cache, new(rule, InputCategories: [verb])));
        var rhs = ((IPhRegularRule)Get<IPhSegmentRule>(cache, rule)).RightHandSidesOS.Single();
        Assert.Equal([verb.ToGuid()], rhs.InputPOSesRC.Select(p => p.Guid));

        Assert.Empty(EditPhonologicalRuleFeaturesComposer.Build(cache, new(rule, InputCategories: [verb])));

        var swap = EditPhonologicalRuleFeaturesComposer.Build(cache, new(rule, InputCategories: [noun]));
        Assert.Equal([PhSegRuleRHSInputPOSesOperationKinds.RemoveRefInputPOSes, PhSegRuleRHSInputPOSesOperationKinds.AddRefInputPOSes],
            swap.Select(op => op.Kind));
        ApplyOps(cache, swap);
        Assert.Equal([noun.ToGuid()], rhs.InputPOSesRC.Select(p => p.Guid));

        ApplyOps(cache, EditPhonologicalRuleFeaturesComposer.Build(cache, new(rule, InputCategories: [])));
        Assert.Empty(rhs.InputPOSesRC);

        Assert.Throws<InvalidOperationException>(() => EditPhonologicalRuleFeaturesComposer.Build(cache,
            new(rule, InputCategories: [Phoneme(cache, "u")])));
    }

    [Fact]
    public void TheSingleOutcomeShorthandCanCarryACategory()
    {
        using var cache = pristine.NewScratch();
        RealParserProject.PrepareForParsing(cache, "u", "i", "a", "n");
        var verb = NewCategory(cache, "Verb");
        var rule = Created(ApplyOps(cache, AuthorPhonologicalRuleComposer.Build(cache, new("u becomes i on verbs",
            PhonologicalRuleDirection.Simultaneous, [new(Phoneme: Phoneme(cache, "u"))],
            [new(Phoneme: Phoneme(cache, "i"))], [new(Phoneme: Phoneme(cache, "a"))], [new(Phoneme: Phoneme(cache, "n"))],
            InputCategories: [verb]))), PhPhonDataPhonRulesOperationKinds.Create);
        var rhs = ((IPhRegularRule)Get<IPhSegmentRule>(cache, rule)).RightHandSidesOS.Single();
        Assert.Equal([verb.ToGuid()], rhs.InputPOSesRC.Select(p => p.Guid));

        var parsed = AuthorPhonologicalRuleIntentParser.Parse(JsonDocument.Parse(
            $"{{\"name\":\"r\",\"direction\":\"simultaneous\",\"input\":[],\"output\":[],\"inputCategories\":[\"{verb.Value}\"]}}").RootElement);
        Assert.Equal([verb], parsed.Outcomes.Single().InputCategories!);
        Assert.Throws<ContractParseException>(() => AuthorPhonologicalRuleIntentParser.Parse(JsonDocument.Parse(
            $"{{\"name\":\"r\",\"direction\":\"simultaneous\",\"input\":[],\"rightHandSides\":[{{\"output\":[]}}],\"inputCategories\":[\"{verb.Value}\"]}}").RootElement));
    }

    [Fact]
    public void TheShorthandAndTheFullRightHandSideFormStoreTheSameCategoryAndReadBackTheSame()
    {
        using var cache = pristine.NewScratch();
        RealParserProject.PrepareForParsing(cache, "u", "i", "a", "n");
        var verb = NewCategory(cache, "Verb");
        var (u, i, a, n) = (Phoneme(cache, "u"), Phoneme(cache, "i"), Phoneme(cache, "a"), Phoneme(cache, "n"));
        string Item(CanonicalId id) => $"{{\"phoneme\":\"{id.Value}\"}}";
        string Common(string name) =>
            $"\"name\":\"{name}\",\"direction\":\"simultaneous\",\"input\":[{Item(u)}]";
        string Parts() => $"\"output\":[{Item(i)}],\"left\":[{Item(a)}],\"right\":[{Item(n)}]";
        var shorthand = $"{{{Common("same")},{Parts()},\"inputCategories\":[\"{verb.Value}\"]}}";
        var full = $"{{{Common("same")},\"rightHandSides\":[{{{Parts()},\"inputCategories\":[\"{verb.Value}\"]}}]}}";
        var made = new List<CanonicalId>();
        foreach (var json in new[] { shorthand, full })
        {
            var placed = made.Count == 0 ? json : json[..^1] + $",\"placement\":{{\"after\":\"{made[0].Value}\"}}}}";
            made.Add(Created(ApplyOps(cache, AuthorPhonologicalRuleComposer.Build(cache,
                AuthorPhonologicalRuleIntentParser.Parse(JsonDocument.Parse(placed).RootElement))), PhPhonDataPhonRulesOperationKinds.Create));
        }

        // FieldWorks data: the right-hand side's InputPOSes is what the HC loader reads for the rule's category.
        var categories = made.Select(rule => ((IPhRegularRule)Get<IPhSegmentRule>(cache, rule)).RightHandSidesOS.Single()
            .InputPOSesRC.Select(p => p.Guid).ToArray()).ToArray();
        Assert.Equal([verb.ToGuid()], categories[0]);
        Assert.Equal(categories[0], categories[1]);
        // Everything Motif reads back about the single outcome (output, contexts, features, categories) is identical.
        var read = made.Select(rule => JsonSerializer.Serialize(
            PhonologicalRuleReader.Read(Get<IPhSegmentRule>(cache, rule))!)).ToArray();
        Assert.Equal(read[0], read[1]);
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
