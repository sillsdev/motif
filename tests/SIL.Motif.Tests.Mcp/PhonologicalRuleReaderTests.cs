using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Mcp;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Mcp;

[Collection(LcmCacheParallelCollections.Group3)]
public sealed class PhonologicalRuleReaderTests(PristineProjectFixture pristine)
{
    [Fact]
    public void DetailedRuleReadSuppliesTheWholeIntentForAnEditGuard()
    {
        using var cache = pristine.NewScratch();
        var rule = Rule(cache);
        var summary = GrammarReader.Read(cache, "phonological_rules", "Sequence", detailed: false)["items"]![0]!;
        Assert.Null(summary["intent"]);
        var detail = GrammarReader.Read(cache, "phonological_rules", "Sequence", detailed: true)["items"]![0]!;
        var intent = Assert.IsType<System.Text.Json.Nodes.JsonObject>(detail["intent"]);
        Assert.Equal(2, intent["input"]!.AsArray().Count);
        Assert.Equal(2, intent["output"]!.AsArray().Count);
        Assert.Equal("simultaneous", intent["direction"]!.GetValue<string>());
        var parsed = AuthorPhonologicalRuleIntentParser.Parse(JsonSerializer.SerializeToElement(intent));
        Assert.Equal(rule.StrucDescOS.Select(context => ((IPhSimpleContextSeg)context).FeatureStructureRA.Guid),
            parsed.Input.Select(item => item.Phoneme!.Value.ToGuid()));
        Assert.Equal(rule.RightHandSidesOS[0].StrucChangeOS.Select(context => ((IPhSimpleContextSeg)context).FeatureStructureRA.Guid),
            parsed.Output.Select(item => item.Phoneme!.Value.ToGuid()));

        var edit = EditPhonologicalRuleIntentParser.Parse(JsonSerializer.SerializeToElement(new
        {
            rule = detail["id"]!.GetValue<string>(), name = "Renamed", expected = intent,
        }));
        Assert.NotEmpty(EditPhonologicalRuleComposer.Build(cache, edit));
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var first = (IPhSimpleContextSeg)rule.StrucDescOS[0];
            var second = (IPhSimpleContextSeg)rule.StrucDescOS[1];
            (first.FeatureStructureRA, second.FeatureStructureRA) = (second.FeatureStructureRA, first.FeatureStructureRA);
        });
        var stale = Assert.Throws<InvalidOperationException>(() => EditPhonologicalRuleComposer.Build(cache, edit));
        Assert.Contains("expectation for input", stale.Message);
    }

    [Fact]
    public void MultipleRightHandSidesAreIdentifiedWithoutAnEditableIntent()
    {
        using var cache = pristine.NewScratch();
        var rule = Rule(cache);
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            rule.RightHandSidesOS.Add(cache.ServiceLocator.GetInstance<IPhSegRuleRHSFactory>().Create()));
        var detail = GrammarReader.Read(cache, "phonological_rules", "Sequence", detailed: true)["items"]![0]!.AsObject();
        Assert.True(detail.ContainsKey("intent"));
        Assert.Null(detail["intent"]);
        Assert.Equal(2, detail["rightHandSideCount"]!.GetValue<int>());
        Assert.Throws<InvalidOperationException>(() => EditPhonologicalRuleComposer.Build(cache,
            new(CanonicalId.FromGuid(rule.Guid), new(Name: "Renamed"))));
    }

    private static IPhRegularRule Rule(LcmCache cache)
    {
        IPhRegularRule rule = null!;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var services = cache.ServiceLocator;
            var data = cache.LangProject.PhonologicalDataOA;
            var set = services.GetInstance<IPhPhonemeSetFactory>().Create();
            data.PhonemeSetsOS.Add(set);
            rule = services.GetInstance<IPhRegularRuleFactory>().Create();
            data.PhonRulesOS.Add(rule);
            rule.Name.set_String(cache.DefaultAnalWs, "Sequence");
            rule.Direction = 2;
            var rhs = rule.RightHandSidesOS.Single();
            foreach (var letter in new[] { "n", "t", "m", "d" })
            {
                var phoneme = services.GetInstance<IPhPhonemeFactory>().Create();
                set.PhonemesOC.Add(phoneme);
                phoneme.Name.set_String(cache.DefaultVernWs, letter);
                var context = services.GetInstance<IPhSimpleContextSegFactory>().Create();
                if (letter is "n" or "t") rule.StrucDescOS.Add(context);
                else rhs.StrucChangeOS.Add(context);
                context.FeatureStructureRA = phoneme;
            }
        });
        return rule;
    }
}
