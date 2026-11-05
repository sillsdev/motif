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

    [Theory]
    [InlineData("input", true)]
    [InlineData("input", false)]
    [InlineData("output", true)]
    [InlineData("output", false)]
    [InlineData("left", true)]
    [InlineData("left", false)]
    [InlineData("right", true)]
    [InlineData("right", false)]
    [InlineData("left-sequence", true)]
    [InlineData("left-sequence", false)]
    [InlineData("right-sequence", true)]
    [InlineData("right-sequence", false)]
    [InlineData("left-iteration", true)]
    [InlineData("left-iteration", false)]
    [InlineData("right-iteration", true)]
    [InlineData("right-iteration", false)]
    [InlineData("left-sequence-iteration", true)]
    [InlineData("left-sequence-iteration", false)]
    [InlineData("right-sequence-iteration", true)]
    [InlineData("right-sequence-iteration", false)]
    public void AlphaConstraintsReadFaithfullyAndAStaleGuardCannotRemoveTheirReferences(
        string location, bool positive)
    {
        using var cache = pristine.NewScratch();
        var rule = Rule(cache);
        var context = AddNaturalClassContext(cache, rule, location);
        var before = GrammarReader.Read(cache, "phonological_rules", "Sequence", detailed: true)["items"]![0]!;
        var wire = Assert.IsType<System.Text.Json.Nodes.JsonObject>(before["intent"]);
        var edit = EditPhonologicalRuleIntentParser.Parse(JsonSerializer.SerializeToElement(new
        {
            rule = CanonicalId.FromGuid(rule.Guid).Value,
            input = wire["input"], output = wire["output"], left = wire["left"], right = wire["right"], expected = wire,
        }));
        IPhFeatureConstraint constraint = null!;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var services = cache.ServiceLocator;
            var feature = services.GetInstance<IFsClosedFeatureFactory>().Create();
            cache.LangProject.PhFeatureSystemOA.FeaturesOC.Add(feature);
            constraint = services.GetInstance<IPhFeatureConstraintFactory>().Create();
            cache.LangProject.PhonologicalDataOA.FeatConstraintsOS.Add(constraint);
            constraint.FeatureRA = feature;
            if (positive) context.PlusConstrRS.Add(constraint);
            else context.MinusConstrRS.Add(constraint);
        });
        var contextId = context.Guid;
        var plus = context.PlusConstrRS.Select(item => item.Guid).ToArray();
        var minus = context.MinusConstrRS.Select(item => item.Guid).ToArray();

        var detail = GrammarReader.Read(cache, "phonological_rules", "Sequence", detailed: true)["items"]![0]!.AsObject();
        Assert.Equal(1, detail["rightHandSideCount"]!.GetValue<int>());
        Assert.True(detail.ContainsKey("intent"));
        var read = Assert.IsType<System.Text.Json.Nodes.JsonObject>(detail["intent"]);
        var side = location.Split('-')[0];
        var referenced = Assert.Single(read[side]!.AsArray(), item => item?["naturalClass"] is not null);
        var alpha = Assert.Single(referenced!["variables"]!.AsArray());
        Assert.Equal(CanonicalId.FromGuid(constraint.Guid).Value, alpha!["variable"]!.GetValue<string>());
        Assert.Equal(positive ? "+" : "-", alpha["polarity"]!.GetValue<string>());
        var minted = 0;
        var refused = Assert.Throws<InvalidOperationException>(() => EditPhonologicalRuleComposer.Build(cache, edit,
            mintId: () => { minted++; return CanonicalId.Mint(); }));
        Assert.Contains("expectation", refused.Message);
        Assert.Equal(0, minted);
        Assert.True(context.IsValidObject);
        Assert.Equal(contextId, context.Guid);
        Assert.True(constraint.IsValidObject);
        Assert.Equal(plus, context.PlusConstrRS.Select(item => item.Guid));
        Assert.Equal(minus, context.MinusConstrRS.Select(item => item.Guid));
        Assert.Equal(constraint.Guid, Assert.Single(cache.LangProject.PhonologicalDataOA.FeatConstraintsOS).Guid);
    }

    [Theory]
    [InlineData("contradictory-polarity")]
    [InlineData("missing-feature")]
    [InlineData("nested-iteration")]
    public void AnUnrepresentableAlphaOrContextStillRefusesAnEditableIntent(string scenario)
    {
        using var cache = pristine.NewScratch();
        var rule = Rule(cache);
        var context = AddNaturalClassContext(cache, rule, "right-iteration");
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var services = cache.ServiceLocator;
            var data = cache.LangProject.PhonologicalDataOA;
            if (scenario == "nested-iteration")
            {
                var outer = (IPhIterationContext)rule.RightHandSidesOS[0].RightContextOA;
                var inner = services.GetInstance<IPhIterationContextFactory>().Create();
                data.ContextsOS.Add(inner);
                inner.Minimum = 0;
                inner.Maximum = 1;
                inner.MemberRA = context;
                outer.MemberRA = inner;
                return;
            }
            var constraint = services.GetInstance<IPhFeatureConstraintFactory>().Create();
            data.FeatConstraintsOS.Add(constraint);
            context.PlusConstrRS.Add(constraint);
            if (scenario == "contradictory-polarity")
            {
                var feature = services.GetInstance<IFsClosedFeatureFactory>().Create();
                cache.LangProject.PhFeatureSystemOA.FeaturesOC.Add(feature);
                constraint.FeatureRA = feature;
                context.MinusConstrRS.Add(constraint);
            }
        });
        Assert.Null(PhonologicalRuleReader.Read(rule));
        var minted = 0;
        Assert.Throws<InvalidOperationException>(() => EditPhonologicalRuleComposer.Build(cache,
            new(CanonicalId.FromGuid(rule.Guid), new(Name: "renamed")), () => { minted++; return CanonicalId.Mint(); }));
        Assert.Equal(0, minted);
    }

    private static IPhSimpleContextNC AddNaturalClassContext(LcmCache cache, IPhRegularRule rule, string location)
    {
        IPhSimpleContextNC context = null!;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var services = cache.ServiceLocator;
            var data = cache.LangProject.PhonologicalDataOA;
            var naturalClass = services.GetInstance<IPhNCSegmentsFactory>().Create();
            data.NaturalClassesOS.Add(naturalClass);
            naturalClass.SegmentsRC.Add(data.PhonemeSetsOS[0].PhonemesOC.First());
            context = services.GetInstance<IPhSimpleContextNCFactory>().Create();
            var rhs = rule.RightHandSidesOS.Single();
            if (location == "input") rule.StrucDescOS.Add(context);
            else if (location == "output") rhs.StrucChangeOS.Add(context);
            else
            {
                IPhSequenceContext? sequence = null;
                if (location.Contains("sequence", StringComparison.Ordinal))
                {
                    sequence = services.GetInstance<IPhSequenceContextFactory>().Create();
                    OwnEnvironment(sequence);
                    var preceding = services.GetInstance<IPhSimpleContextSegFactory>().Create();
                    data.ContextsOS.Add(preceding);
                    preceding.FeatureStructureRA = data.PhonemeSetsOS[0].PhonemesOC.First();
                    sequence.MembersRS.Add(preceding);
                }
                if (location.Contains("iteration", StringComparison.Ordinal))
                {
                    var iteration = services.GetInstance<IPhIterationContextFactory>().Create();
                    if (sequence is null) OwnEnvironment(iteration);
                    else data.ContextsOS.Add(iteration);
                    data.ContextsOS.Add(context);
                    iteration.Minimum = 0;
                    iteration.Maximum = 1;
                    iteration.MemberRA = context;
                    sequence?.MembersRS.Add(iteration);
                }
                else if (sequence is null) OwnEnvironment(context);
                else
                {
                    data.ContextsOS.Add(context);
                    sequence.MembersRS.Add(context);
                }

                void OwnEnvironment(IPhPhonContext root)
                {
                    if (location.StartsWith("left", StringComparison.Ordinal)) rhs.LeftContextOA = root;
                    else rhs.RightContextOA = root;
                }
            }
            context.FeatureStructureRA = naturalClass;
        });
        return context;
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
