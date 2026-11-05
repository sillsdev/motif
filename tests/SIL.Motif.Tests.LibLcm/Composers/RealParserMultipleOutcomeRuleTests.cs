using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.Parser;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using Xunit.Abstractions;
using static SIL.Motif.Tests.Composers.AffixTestSupport;

namespace SIL.Motif.Tests.Composers;

[Collection(LcmCacheParallelCollections.Group3)]
public sealed class RealParserMultipleOutcomeRuleTests(PristineProjectFixture pristine, ITestOutputHelper output)
{
    [RealParserFact]
    public async Task AnOutputClassAllowsBothLexicalAlternativesAndEditingItRemovesOne()
    {
        await ProveOutputClass(retainOriginal: false);
        await ProveOutputClass(retainOriginal: true);
    }

    private async Task ProveOutputClass(bool retainOriginal)
    {
        using var cache = pristine.NewScratch();
        RealParserProject.PrepareForParsing(cache, "d", "r", "l", "a", "u");
        var category = AddCategory(cache, "Word");
        var tagged = AddStem(cache, "ada", category);
        var control = AddStem(cache, "udu", category);
        var feature = Created(ApplyOps(cache, AuthorExceptionFeatureComposer.Build(cache, new("liquid alternation", Anal(cache), null))),
            CmPossibilityListPossibilitiesOperationKinds.Create);
        ApplyOps(cache, EditExceptionFeaturesComposer.Build(cache, new(tagged.Msa, [feature])));
        var phones = RealParserReadings.Phonemes(cache);
        var liquids = RealParserReadings.ClassOf(cache, "selected outcomes", "OUT", phones["r"], phones[retainOriginal ? "d" : "l"]);
        var ruleId = Created(ApplyOps(cache, AuthorPhonologicalRuleComposer.Build(cache,
            new("lexical alternatives", PhonologicalRuleDirection.Simultaneous, [new(Phoneme: phones["d"])], [], [], [],
                RightHandSides: [new([new(NaturalClass: liquids)], [], [], [feature])]))), PhPhonDataPhonRulesOperationKinds.Create);
        var loader = new FwDataProjectLoader();
        loader.Save(cache);
        using var invoker = new PanGlossInvoker();
        var tracer = new PanGlossTracer(invoker);
        Assert.Equal([tagged.Form], await Forms("ara"));
        Assert.Equal([tagged.Form], await Forms(retainOriginal ? "ada" : "ala"));
        Assert.Equal([control.Form], await Forms("udu"));
        Assert.Empty(await Forms("uru"));
        Assert.Empty(await Forms("ulu"));
        var expected = PhonologicalRuleReader.ToJson(PhonologicalRuleReader.Read(Get<IPhSegmentRule>(cache, ruleId))!);
        var edit = EditPhonologicalRuleIntentParser.Parse(JsonSerializer.SerializeToElement(new
        {
            rule = ruleId.Value,
            rightHandSides = new[] { new { output = new[] { new { phoneme = phones["r"].Value } }, requiredFeatures = new[] { feature.Value } } },
            expected,
        }));
        ApplyOps(cache, EditPhonologicalRuleComposer.Build(cache, edit));
        loader.Save(cache);
        Assert.Equal([tagged.Form], await Forms("ara"));
        Assert.Empty(await Forms(retainOriginal ? "ada" : "ala"));

        async Task<CanonicalId[]> Forms(string word)
        {
            var result = Assert.IsType<PanGlossTraceOutcome.Completed>(await tracer.TraceAsync(
                cache.ProjectId.Path, word, CancellationToken.None, Wait.Scaled(TimeSpan.FromSeconds(120))));
            Assert.True(result.Details!.SearchCompleted, word);
            Assert.False(result.Details.Capped || result.Details.TimedOut || result.Details.InvalidShape, word);
            var forms = result.Document!.Analyses.SelectMany(a => a.Morphs)
                .Select(m => CanonicalId.FromGuid(Guid.Parse(m.FormId!))).Distinct().ToArray();
            output.WriteLine($"{word}: {forms.Length} underlying forms");
            return forms;
        }
    }

    [RealParserFact]
    public async Task OverlappingOutcomesUseTheFirstApplicableRhsAndRemovingItEnablesTheSecond()
    {
        using var cache = pristine.NewScratch();
        RealParserProject.PrepareForParsing(cache, "d", "r", "l", "a", "u");
        var category = AddCategory(cache, "Word");
        var tagged = AddStem(cache, "ada", category);
        var control = AddStem(cache, "udu", category);
        var feature = Created(ApplyOps(cache, AuthorExceptionFeatureComposer.Build(cache, new("liquid outcomes", Anal(cache), null))),
            CmPossibilityListPossibilitiesOperationKinds.Create);
        ApplyOps(cache, EditExceptionFeaturesComposer.Build(cache, new(tagged.Msa, [feature])));
        var phonemes = RealParserReadings.Phonemes(cache);
        object Outcome(string segment) => new
        {
            output = new[] { new { phoneme = phonemes[segment].Value } },
            requiredFeatures = new[] { feature.Value },
        };
        var intent = AuthorPhonologicalRuleIntentParser.Parse(JsonSerializer.SerializeToElement(new
        {
            name = "two liquid outcomes", direction = "simultaneous",
            input = new[] { new { phoneme = phonemes["d"].Value } },
            rightHandSides = new[] { Outcome("r"), Outcome("l") },
        }));
        var ruleId = Created(ApplyOps(cache, AuthorPhonologicalRuleComposer.Build(cache, intent)), PhPhonDataPhonRulesOperationKinds.Create);
        Assert.Equal(2, ((IPhRegularRule)Get<IPhSegmentRule>(cache, ruleId)).RightHandSidesOS.Count);
        var names = new Dictionary<CanonicalId, string>(CanonicalId.ByGuid) { [tagged.Form] = "ada", [control.Form] = "udu" };
        var loader = new FwDataProjectLoader();
        loader.Save(cache);
        using var invoker = new PanGlossInvoker();
        var tracer = new PanGlossTracer(invoker);
        Assert.Equal(["ada"], await Read("ara"));
        Assert.Empty(await Read("ala"));
        Assert.Equal(["udu"], await Read("udu"));
        Assert.Empty(await Read("uru"));
        Assert.Empty(await Read("ulu"));
        var expected = PhonologicalRuleReader.ToJson(PhonologicalRuleReader.Read(Get<IPhSegmentRule>(cache, ruleId))!);
        var edit = EditPhonologicalRuleIntentParser.Parse(JsonSerializer.SerializeToElement(new
        {
            rule = ruleId.Value, rightHandSides = new[] { Outcome("l") }, expected,
        }));
        ApplyOps(cache, EditPhonologicalRuleComposer.Build(cache, edit));
        loader.Save(cache);
        Assert.Empty(await Read("ara"));
        Assert.Equal(["ada"], await Read("ala"));
        Assert.Empty(await Read("uru"));
        Assert.Empty(await Read("ulu"));

        async Task<string[]> Read(string word)
        {
            var result = Assert.IsType<PanGlossTraceOutcome.Completed>(await tracer.TraceAsync(
                cache.ProjectId.Path, word, CancellationToken.None, Wait.Scaled(TimeSpan.FromSeconds(120))));
            Assert.True(result.Details!.SearchCompleted, word);
            Assert.False(result.Details.Capped || result.Details.TimedOut || result.Details.InvalidShape, word);
            var readings = result.Document!.Analyses.SelectMany(a => a.Morphs)
                .Select(m => names[CanonicalId.FromGuid(Guid.Parse(m.FormId!))]).Distinct().Order().ToArray();
            output.WriteLine($"{word}: {string.Join(", ", readings)}");
            return readings;
        }
    }
}
