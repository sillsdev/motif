using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Composers;

[Collection(LcmCacheParallelCollections.Group2)]
public sealed class FeatureRuleComposerTests(PristineProjectFixture pristine)
{
    [Fact]
    public void FeatureChangeSetsOnlyTheNamedFeatureAndReadsBackAsAnEditableIntent()
    {
        using var cache = pristine.NewScratch();
        var f = new FeaturedSegments(cache);
        var intent = AuthorPhonologicalRuleIntentParser.Parse(JsonSerializer.SerializeToElement(new
        {
            name = "labialize", direction = "simultaneous",
            input = new[] { new { naturalClass = f.Nasals.Value } },
            output = new[] { new { change = new[] { new { feature = f.Place.Value, value = f.Labial.Value } } } },
        }));
        var operations = AuthorPhonologicalRuleComposer.Build(cache, intent);
        SoundSystemComposerTests.Execute(cache, operations);
        var rule = Assert.Single(cache.LangProject.PhonologicalDataOA.PhonRulesOS);
        var rhs = Assert.Single(((IPhRegularRule)rule).RightHandSidesOS);
        var output = Assert.IsAssignableFrom<IPhSimpleContextNC>(Assert.Single(rhs.StrucChangeOS));
        var nc = Assert.IsAssignableFrom<IPhNCFeatures>(output.FeatureStructureRA);
        var spec = Assert.IsAssignableFrom<IFsClosedValue>(Assert.Single(nc.FeaturesOA.FeatureSpecsOC));
        Assert.Equal(f.Place.ToGuid(), spec.FeatureRA.Guid);
        Assert.Equal(f.Labial.ToGuid(), spec.ValueRA.Guid);
        var wire = PhonologicalRuleReader.ToJson(PhonologicalRuleReader.Read(rule)!);
        Assert.Equal(CanonicalId.FromGuid(nc.Guid).Value, wire["output"]![0]!["naturalClass"]!.GetValue<string>());
        var edit = EditPhonologicalRuleIntentParser.Parse(JsonSerializer.SerializeToElement(new
        {
            rule = CanonicalId.FromGuid(rule.Guid).Value, name = "renamed", expected = wire,
        }));
        SoundSystemComposerTests.Execute(cache, EditPhonologicalRuleComposer.Build(cache, edit));
        Assert.Equal("renamed", rule.Name.BestAnalysisAlternative.Text);
    }
    [Theory]
    [InlineData("+")]
    [InlineData("-")]
    public void AlphaConstraintsPersistAndPartialEditsReuseTheSameVariable(string polarity)
    {
        using var cache = pristine.NewScratch();
        var f = new FeaturedSegments(cache);
        var intent = AuthorPhonologicalRuleIntentParser.Parse(f.Assimilation(polarity));
        SoundSystemComposerTests.Execute(cache, AuthorPhonologicalRuleComposer.Build(cache, intent));
        var loader = new FwDataProjectLoader();
        loader.Save(cache);
        using var reopened = loader.LoadScratchCache(cache.ProjectId.Path);
        var data = reopened.LangProject.PhonologicalDataOA;
        var rule = Assert.IsAssignableFrom<IPhRegularRule>(Assert.Single(data.PhonRulesOS));
        var constraint = Assert.Single(data.FeatConstraintsOS);
        Assert.Equal(f.Place.ToGuid(), constraint.FeatureRA.Guid);
        var rhs = Assert.Single(rule.RightHandSidesOS);
        var output = Assert.IsAssignableFrom<IPhSimpleContextNC>(Assert.Single(rhs.StrucChangeOS));
        var right = Assert.IsAssignableFrom<IPhSequenceContext>(rhs.RightContextOA);
        var binder = Assert.IsAssignableFrom<IPhSimpleContextNC>(right.MembersRS[1]);
        Assert.Equal(constraint.Guid, Assert.Single(binder.PlusConstrRS).Guid);
        Assert.Equal(constraint.Guid, Assert.Single(polarity == "+" ? output.PlusConstrRS : output.MinusConstrRS).Guid);
        var read = PhonologicalRuleReader.Read(rule)!;
        var wire = PhonologicalRuleReader.ToJson(read);
        Assert.Equal(polarity, wire["output"]![0]!["variables"]![0]!["polarity"]!.GetValue<string>());
        Assert.Equal(CanonicalId.FromGuid(constraint.Guid).Value, Assert.Single(read.Output[0].Variables!).Variable);
        var edit = EditPhonologicalRuleIntentParser.Parse(JsonSerializer.SerializeToElement(new
        {
            rule = CanonicalId.FromGuid(rule.Guid).Value, output = wire["output"], expected = wire,
        }));
        SoundSystemComposerTests.Execute(reopened, EditPhonologicalRuleComposer.Build(reopened, edit));
        Assert.Equal(constraint.Guid, Assert.Single(data.FeatConstraintsOS).Guid);
        var after = PhonologicalRuleReader.ToJson(PhonologicalRuleReader.Read(rule)!);
        Assert.Equal(wire.ToJsonString(), after.ToJsonString());
        Assert.Equal(constraint.Guid, Assert.Single(binder.PlusConstrRS).Guid);
    }

    [Theory]
    [InlineData("input")]
    [InlineData("left")]
    [InlineData("right")]
    [InlineData("left-sequence")]
    [InlineData("right-sequence")]
    [InlineData("left-iteration")]
    [InlineData("right-iteration")]
    [InlineData("right-sequence-iteration")]
    [InlineData("negative-context")]
    public void AlphaBindingsInEveryRepresentablePositionSurviveAWholeRuleRoundTrip(string location)
    {
        using var cache = pristine.NewScratch();
        var f = new FeaturedSegments(cache);
        var authored = AuthorPhonologicalRuleIntentParser.Parse(f.Assimilation()) with { Right = [] };
        if (location == "input") authored = authored with
        {
            Input = [new(NaturalClass: f.Nasals, Variables: [new("alpha", f.Place)])],
        };
        else
        {
            var binder = new EnvironmentContext(NaturalClass: f.Stops, Variables: [new("alpha", f.Place, location == "negative-context" ? "-" : "+")],
                Minimum: location.Contains("iteration") ? 1 : null, Maximum: location.Contains("iteration") ? -1 : null);
            EnvironmentContext[] contexts = location.Contains("sequence") ? [new(Phoneme: f.Phones["a"]), binder] : [binder];
            authored = location.StartsWith("left") ? authored with { Left = contexts } : authored with { Right = contexts };
        }
        SoundSystemComposerTests.Execute(cache, AuthorPhonologicalRuleComposer.Build(cache, authored));
        var rule = Assert.Single(cache.LangProject.PhonologicalDataOA.PhonRulesOS);
        var constraint = Assert.Single(cache.LangProject.PhonologicalDataOA.FeatConstraintsOS);
        var wire = PhonologicalRuleReader.ToJson(PhonologicalRuleReader.Read(rule)!);
        var classIds = cache.LangProject.PhonologicalDataOA.NaturalClassesOS.Select(c => c.Guid).ToArray();
        var edit = EditPhonologicalRuleIntentParser.Parse(JsonSerializer.SerializeToElement(new
        {
            rule = CanonicalId.FromGuid(rule.Guid).Value,
            input = wire["input"], output = wire["output"], left = wire["left"], right = wire["right"], expected = wire,
        }));
        AffixTestSupport.ApplyOps(cache, EditPhonologicalRuleComposer.Build(cache, edit).Reverse().ToArray());
        Assert.Equal(wire.ToJsonString(), PhonologicalRuleReader.ToJson(PhonologicalRuleReader.Read(rule)!).ToJsonString());
        Assert.Equal(classIds, cache.LangProject.PhonologicalDataOA.NaturalClassesOS.Select(c => c.Guid));
        Assert.Equal(constraint.Guid, Assert.Single(cache.LangProject.PhonologicalDataOA.FeatConstraintsOS).Guid);
    }

    [Theory]
    [InlineData("contradiction", "alpha")]
    [InlineData("different-features", "alpha")]
    [InlineData("input-change", "Output")]
    [InlineData("unequal", "aligned")]
    public void InvalidFeatureRulesRefuseBeforeMintingAnyIdentity(string scenario, string message)
    {
        using var cache = pristine.NewScratch();
        var f = new FeaturedSegments(cache);
        var intent = AuthorPhonologicalRuleIntentParser.Parse(f.Assimilation());
        if (scenario == "contradiction") intent = intent with
        {
            Output = [new(Change: [], Variables: [new("alpha", f.Place), new("alpha", f.Place, "-")])],
        };
        if (scenario == "different-features") intent = intent with
        {
            Output = [new(Change: [], Variables: [new("alpha", f.Labial)])],
        };
        if (scenario == "input-change") intent = intent with { Input = [new(Change: [new(f.Place, f.Labial)])] };
        if (scenario == "unequal") intent = intent with { Input = [] };
        var minted = 0;
        var error = Assert.Throws<InvalidOperationException>(() => AuthorPhonologicalRuleComposer.Build(cache, intent,
            () => { minted++; return CanonicalId.Mint(); }));
        Assert.Contains(message, error.Message);
        Assert.Equal(0, minted);
        Assert.Empty(cache.LangProject.PhonologicalDataOA.PhonRulesOS);
        Assert.Empty(cache.LangProject.PhonologicalDataOA.FeatConstraintsOS);
    }

    [Fact]
    public void AFeatureCannotHaveTwoFixedValuesAndAlphaCannotDisappearIntoAnAllomorphEnvironment()
    {
        using var cache = pristine.NewScratch();
        var f = new FeaturedSegments(cache);
        var authored = new AuthorPhonologicalRuleIntent("contradiction", PhonologicalRuleDirection.Simultaneous,
            [new(NaturalClass: f.Nasals)], [new(Change: [new(f.Place, f.Labial), new(f.Place, f.Coronal)])], [], []);
        var duplicate = Assert.Throws<InvalidOperationException>(() => AuthorPhonologicalRuleComposer.Build(cache, authored));
        Assert.Contains(f.Place.Value, duplicate.Message);
        var environment = Assert.Throws<InvalidOperationException>(() => AuthorEnvironmentComposer.Build(cache,
            new("alpha environment", [], [new(NaturalClass: f.Stops, Variables: [new("alpha", f.Place)])])));
        Assert.Contains("alpha", environment.Message);
    }

    [Theory]
    [InlineData("left", 0, 1)]
    [InlineData("right", 1, -1)]
    [InlineData("both", 1, -1)]
    public void AlphaOnlyInsideRepeatedSequenceItemsIsRefusedBeforeMinting(string side, int minimum, int maximum)
    {
        using var cache = pristine.NewScratch();
        var f = new FeaturedSegments(cache);
        EnvironmentContext[] contexts = [new(Phoneme: f.Phones["a"]),
            new(NaturalClass: f.Stops, Minimum: minimum, Maximum: maximum, Variables: [new("alpha", f.Place)])];
        var intent = new AuthorPhonologicalRuleIntent("hidden variable", PhonologicalRuleDirection.Simultaneous,
            [new(NaturalClass: f.Nasals)], [new(NaturalClass: f.Stops)],
            side is "left" or "both" ? contexts : [], side is "right" or "both" ? contexts : []);
        var minted = 0;
        var error = Assert.Throws<InvalidOperationException>(() => AuthorPhonologicalRuleComposer.Build(cache, intent,
            () => { minted++; return CanonicalId.Mint(); }));
        Assert.Contains("alpha", error.Message);
        Assert.Contains("HCLoader", error.Message);
        Assert.Contains("rule.FeatureConstraints", error.Message);
        Assert.Equal(0, minted);
        Assert.Empty(cache.LangProject.PhonologicalDataOA.PhonRulesOS);
        Assert.Empty(cache.LangProject.PhonologicalDataOA.FeatConstraintsOS);
    }

    [Fact]
    public void LibLcmReadBackExposesTheLoaderOmissionAndAnEditCanMakeTheVariableDiscoverable()
    {
        using var cache = pristine.NewScratch();
        var f = new FeaturedSegments(cache);
        EnvironmentContext[] contexts = [new(Phoneme: f.Phones["a"]),
            new(NaturalClass: f.Stops, Minimum: 1, Maximum: -1, Variables: [new("alpha", f.Place)])];
        AffixTestSupport.ApplyOps(cache, AuthorPhonologicalRuleComposer.Build(cache,
            new("hidden variable", PhonologicalRuleDirection.Simultaneous, [new(NaturalClass: f.Nasals)],
                [new(NaturalClass: f.Stops, Variables: [new("alpha", f.Place)])], contexts, contexts)));
        var authored = Assert.IsAssignableFrom<IPhRegularRule>(Assert.Single(cache.LangProject.PhonologicalDataOA.PhonRulesOS));
        SIL.LCModel.Infrastructure.NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            ((IPhSimpleContextNC)authored.RightHandSidesOS[0].StrucChangeOS[0]).PlusConstrRS.Clear());
        var loader = new FwDataProjectLoader();
        loader.Save(cache);
        using var reopened = loader.LoadScratchCache(cache.ProjectId.Path);
        var data = reopened.LangProject.PhonologicalDataOA;
        var rule = Assert.IsAssignableFrom<IPhRegularRule>(Assert.Single(data.PhonRulesOS));
        var constraint = Assert.Single(data.FeatConstraintsOS);
        Assert.Empty(rule.FeatureConstraints);
        var read = PhonologicalRuleReader.Read(rule)!;
        Assert.Equal(CanonicalId.FromGuid(constraint.Guid).Value, Assert.Single(read.Left[1].Variables!).Variable);
        Assert.Equal(CanonicalId.FromGuid(constraint.Guid).Value, Assert.Single(read.Right[1].Variables!).Variable);
        var minted = 0;
        var error = Assert.Throws<InvalidOperationException>(() => EditPhonologicalRuleComposer.Build(reopened,
            new(CanonicalId.FromGuid(rule.Guid), new(Name: "still hidden")), () => { minted++; return CanonicalId.Mint(); }));
        Assert.Contains("HCLoader", error.Message);
        Assert.Equal(0, minted);
        AffixTestSupport.ApplyOps(reopened, EditPhonologicalRuleComposer.Build(reopened,
            new(CanonicalId.FromGuid(rule.Guid), new(Left: [read.Left[1]]))));
        Assert.Equal(constraint.Guid, Assert.Single(rule.FeatureConstraints).Guid);
        Assert.NotNull(PhonologicalRuleReader.Read(rule));
    }

    [Theory]
    [InlineData("+", 1)]
    [InlineData("-", 1)]
    [InlineData("+", 2)]
    [InlineData("-", 2)]
    public void ReplacingAllInputReferencesPreservesAlphaSharing(string polarity, int count)
    {
        using var cache = pristine.NewScratch();
        var f = new FeaturedSegments(cache);
        var input = Enumerable.Range(0, count).Select(_ =>
            new PhonologicalRuleItem(NaturalClass: f.Nasals, Variables: [new("alpha", f.Place, polarity)])).ToArray();
        AffixTestSupport.ApplyOps(cache, AuthorPhonologicalRuleComposer.Build(cache,
            new("Input agreement", PhonologicalRuleDirection.Simultaneous, input, [new(NaturalClass: f.Stops)], [], [])));
        var data = cache.LangProject.PhonologicalDataOA;
        var rule = Assert.IsAssignableFrom<IPhRegularRule>(Assert.Single(data.PhonRulesOS));
        var oldConstraint = Assert.Single(rule.FeatureConstraints);
        var wire = PhonologicalRuleReader.ToJson(PhonologicalRuleReader.Read(rule)!);
        var edit = EditPhonologicalRuleIntentParser.Parse(JsonSerializer.SerializeToElement(new
        {
            rule = CanonicalId.FromGuid(rule.Guid).Value, input = wire["input"], expected = wire,
        }));
        AffixTestSupport.ApplyOps(cache, EditPhonologicalRuleComposer.Build(cache, edit).Reverse().ToArray());
        var replacement = Assert.Single(data.FeatConstraintsOS);
        Assert.False(oldConstraint.IsValidObject);
        Assert.NotEqual(oldConstraint.Guid, replacement.Guid);
        Assert.Equal(f.Place.ToGuid(), replacement.FeatureRA.Guid);
        Assert.Equal(replacement.Guid, Assert.Single(rule.FeatureConstraints).Guid);
        Assert.Equal(count, rule.StrucDescOS.Count);
        foreach (var item in rule.StrucDescOS.Cast<IPhSimpleContextNC>())
            Assert.Equal(replacement.Guid, Assert.Single(polarity == "+" ? item.PlusConstrRS : item.MinusConstrRS).Guid);
        var after = PhonologicalRuleReader.ToJson(PhonologicalRuleReader.Read(rule)!);
        Assert.Equal(wire.ToJsonString().Replace(CanonicalId.FromGuid(oldConstraint.Guid).Value,
            CanonicalId.FromGuid(replacement.Guid).Value), after.ToJsonString());
        var loader = new FwDataProjectLoader();
        loader.Save(cache);
        using var reopened = loader.LoadScratchCache(cache.ProjectId.Path);
        Assert.Equal(after.ToJsonString(), PhonologicalRuleReader.ToJson(
            PhonologicalRuleReader.Read(Assert.Single(reopened.LangProject.PhonologicalDataOA.PhonRulesOS))!).ToJsonString());
    }

    [Theory]
    [InlineData("input", "+")]
    [InlineData("input", "-")]
    [InlineData("left", "+")]
    [InlineData("left", "-")]
    [InlineData("right", "+")]
    [InlineData("right", "-")]
    [InlineData("output", "+")]
    [InlineData("output", "-")]
    [InlineData("both-contexts", "-")]
    public void SingleReferencesAndAllNegativeVariablesReadBackAndRemainEditable(string location, string polarity)
    {
        using var cache = pristine.NewScratch();
        var f = new FeaturedSegments(cache);
        PhonologicalRuleVariable[] variables = [new("alpha", f.Surface.Feature, polarity)];
        var intent = new AuthorPhonologicalRuleIntent("agreement", PhonologicalRuleDirection.Simultaneous,
            [new(NaturalClass: f.Nasals, Variables: location == "input" ? variables : null)],
            [new(NaturalClass: f.Stops, Variables: location == "output" ? variables : null)],
            location is "left" or "both-contexts" ? [new(NaturalClass: f.Stops, Variables: variables)] : [],
            location is "right" or "both-contexts" ? [new(NaturalClass: f.Stops, Variables: variables)] : []);
        AffixTestSupport.ApplyOps(cache, AuthorPhonologicalRuleComposer.Build(cache, intent));
        var rule = Assert.IsAssignableFrom<IPhRegularRule>(Assert.Single(cache.LangProject.PhonologicalDataOA.PhonRulesOS));
        var constraint = Assert.Single(rule.FeatureConstraints);
        var read = PhonologicalRuleReader.Read(rule)!;
        var uses = read.Input.Concat(read.Output).SelectMany(i => i.Variables ?? [])
            .Concat(read.Left.Concat(read.Right).SelectMany(i => i.Variables ?? [])).ToArray();
        Assert.Equal(location == "both-contexts" ? 2 : 1, uses.Length);
        Assert.All(uses, v =>
        {
            Assert.Equal(CanonicalId.FromGuid(constraint.Guid).Value, v.Variable);
            Assert.Equal(polarity, v.Polarity);
        });
        AffixTestSupport.ApplyOps(cache, EditPhonologicalRuleComposer.Build(cache,
            new(CanonicalId.FromGuid(rule.Guid), new(Name: "renamed"))));
        Assert.Equal("renamed", rule.Name.BestAnalysisAlternative.Text);
        Assert.Equal(constraint.Guid, Assert.Single(rule.FeatureConstraints).Guid);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ADeclaredFeatureMayBeUsedBeforeAnyPhonemeHasItsValues(bool alpha)
    {
        using var cache = pristine.NewScratch();
        var f = new FeaturedSegments(cache);
        var ops = AuthorPhonologicalFeatureComposer.Build(cache, new("unused", [new("yes"), new("no")]));
        SoundSystemComposerTests.Execute(cache, ops);
        var feature = ops[0].EntityId!.Value;
        var value = ops.First(o => o.Kind == FsClosedFeatureValuesOperationKinds.Create).EntityId!.Value;
        var intent = new AuthorPhonologicalRuleIntent("declared feature", PhonologicalRuleDirection.Simultaneous,
            [new(NaturalClass: f.Nasals, Variables: alpha ? [new("alpha", feature)] : null)],
            [new(Change: alpha ? [] : [new(feature, value)], Variables: alpha ? [new("alpha", feature)] : null)], [], []);
        AffixTestSupport.ApplyOps(cache, AuthorPhonologicalRuleComposer.Build(cache, intent));
        var rule = Assert.Single(cache.LangProject.PhonologicalDataOA.PhonRulesOS);
        Assert.NotNull(PhonologicalRuleReader.Read(rule));
        AffixTestSupport.ApplyOps(cache, EditPhonologicalRuleComposer.Build(cache,
            new(CanonicalId.FromGuid(rule.Guid), new(Name: "still editable"))));
        Assert.Equal("still editable", rule.Name.BestAnalysisAlternative.Text);
    }

    [Fact]
    public void ClearingTheLastPhonemeUseOfAFeatureDoesNotBlockANameEdit()
    {
        using var cache = pristine.NewScratch();
        var f = new FeaturedSegments(cache);
        AffixTestSupport.ApplyOps(cache, AuthorPhonologicalRuleComposer.Build(cache,
            AuthorPhonologicalRuleIntentParser.Parse(f.Assimilation())));
        SIL.LCModel.Infrastructure.NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            foreach (var phone in cache.LangProject.PhonologicalDataOA.PhonemeSetsOS[0].PhonemesOC)
                foreach (var spec in phone.FeaturesOA.FeatureSpecsOC.Where(s => s.FeatureRA.Guid == f.Place.ToGuid()).ToArray())
                    phone.FeaturesOA.FeatureSpecsOC.Remove(spec);
        });
        var rule = Assert.Single(cache.LangProject.PhonologicalDataOA.PhonRulesOS);
        AffixTestSupport.ApplyOps(cache, EditPhonologicalRuleComposer.Build(cache,
            new(CanonicalId.FromGuid(rule.Guid), new(Name: "still editable"))));
        Assert.Equal("still editable", rule.Name.BestAnalysisAlternative.Text);
    }

}

internal sealed class FeaturedSegments
{
    internal CanonicalId Place { get; }
    internal CanonicalId Labial { get; }
    internal CanonicalId Coronal { get; }
    internal CanonicalId Dorsal { get; }
    internal CanonicalId Nasals { get; }
    internal CanonicalId Stops { get; }
    internal CanonicalId Underlying { get; }
    internal PhonologicalFeatureValue Surface { get; }
    internal Dictionary<string, CanonicalId> Phones { get; } = new();

    internal FeaturedSegments(LcmCache cache)
    {
        var place = Feature(cache, "place", "labial", "coronal", "dorsal");
        Place = place.Feature;
        (Labial, Coronal, Dorsal) = (place.Values[0], place.Values[1], place.Values[2]);
        var manner = Feature(cache, "manner", "nasal", "stop", "vowel");
        var arch = Feature(cache, "archiphoneme", "surface", "underlying");
        Surface = new(arch.Feature, arch.Values[0]);
        foreach (var (phone, p, m) in new[]
                 { ("N", Labial, 0), ("m", Labial, 0), ("n", Coronal, 0), ("ŋ", Dorsal, 0),
                     ("p", Labial, 1), ("t", Coronal, 1), ("k", Dorsal, 1), ("a", Labial, 2) })
        {
            var features = new List<PhonologicalFeatureValue>
            {
                new(manner.Feature, manner.Values[m]), new(arch.Feature, arch.Values[phone == "N" ? 1 : 0]),
            };
            if (phone is not ("N" or "a")) features.Add(new(Place, p));
            var ops = AuthorPhonemeComposer.Build(cache, new(phone, [phone], features));
            SoundSystemComposerTests.Execute(cache, ops);
            Phones.Add(phone, ops.First(o => o.Kind == PhPhonemeSetPhonemesOperationKinds.Create).EntityId!.Value);
        }
        Underlying = NaturalClass(cache, "archiphonemes", "ARCH", new(arch.Feature, arch.Values[1]));
        Nasals = NaturalClass(cache, "nasals", "NAS", new(manner.Feature, manner.Values[0]));
        Stops = NaturalClass(cache, "stops", "STP", new(manner.Feature, manner.Values[1]));
    }

    internal JsonElement Assimilation(string polarity = "+") => JsonSerializer.SerializeToElement(new
    {
        name = "place assimilation", direction = "simultaneous",
        input = new[] { new { naturalClass = Underlying.Value } },
        output = new[] { new { change = new[] { new { feature = Surface.Feature.Value, value = Surface.Value.Value } }, variables = new[] { new { variable = "alpha", feature = Place.Value, polarity } } } },
        right = new object[] { new { boundary = "morpheme" }, new { naturalClass = Stops.Value,
            variables = new[] { new { variable = "alpha", feature = Place.Value, polarity = "+" } } } },
    });

    private static (CanonicalId Feature, CanonicalId[] Values) Feature(LcmCache cache, string name, params string[] values)
    {
        var ops = AuthorPhonologicalFeatureComposer.Build(cache, new(name, values.Select(v => new PhonologicalFeatureOption(v)).ToArray()));
        SoundSystemComposerTests.Execute(cache, ops);
        return (ops[0].EntityId!.Value, ops.Where(o => o.Kind == FsClosedFeatureValuesOperationKinds.Create)
            .Select(o => o.EntityId!.Value).ToArray());
    }

    private static CanonicalId NaturalClass(LcmCache cache, string name, string abbreviation, PhonologicalFeatureValue value)
    {
        var ops = AuthorNaturalClassComposer.Build(cache, new(name, abbreviation, Features: [value]));
        SoundSystemComposerTests.Execute(cache, ops);
        return ops[0].EntityId!.Value;
    }
}
