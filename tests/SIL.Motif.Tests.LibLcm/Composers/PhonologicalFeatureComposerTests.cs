using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Composers;

[Collection(LcmCacheParallelCollections.Group1)]
public sealed class PhonologicalFeatureComposerTests(PristineProjectFixture pristine)
{
    private static PhonologicalFeatureValue[] DefineBinary(LcmCache cache, string name, out CanonicalId feature)
    {
        var operations = AuthorPhonologicalFeatureComposer.Build(cache, new(name, [new("plus", "+"), new("minus", "-")]));
        SoundSystemComposerTests.Execute(cache, operations);
        feature = operations[0].EntityId!.Value;
        var created = feature;
        return operations.Where(o => o.Kind == FsClosedFeatureValuesOperationKinds.Create)
            .Select(o => new PhonologicalFeatureValue(created, o.EntityId!.Value)).ToArray();
    }

    [Fact]
    public void AuthoredFeatureAndValuesAreReadBackFromThePhonologicalFeatureSystem()
    {
        using var cache = pristine.NewScratch();
        var values = DefineBinary(cache, "voice", out var feature);
        var system = cache.LangProject.PhFeatureSystemOA;
        var stored = Assert.IsAssignableFrom<IFsClosedFeature>(
            system.FeaturesOC.Single(f => CanonicalId.FromGuid(f.Guid) == feature));
        Assert.Equal("voice", stored.Name.get_String(cache.DefaultAnalWs).Text);
        Assert.Equal(["plus", "minus"], stored.ValuesOC.Select(v => v.Name.get_String(cache.DefaultAnalWs).Text));
        Assert.Equal(values.Select(v => v.Value), stored.ValuesOC.Select(v => CanonicalId.FromGuid(v.Guid)));
        Assert.DoesNotContain(cache.LangProject.MsFeatureSystemOA.FeaturesOC, f => f.Guid == stored.Guid);
    }

    [Fact]
    public void ADuplicateFeatureNameOrValueNameOrAbbreviationIsRefused()
    {
        using var cache = pristine.NewScratch();
        DefineBinary(cache, "voice", out _);
        Assert.Throws<InvalidOperationException>(() => AuthorPhonologicalFeatureComposer.Build(cache,
            new("voice", [new("on")])));
        Assert.Throws<InvalidOperationException>(() => AuthorPhonologicalFeatureComposer.Build(cache,
            new("nasal", [new("plus"), new("plus")])));
        Assert.Throws<InvalidOperationException>(() => AuthorPhonologicalFeatureComposer.Build(cache,
            new("nasal", [new("plus", "+"), new("minus", "+")])));
        Assert.Throws<InvalidOperationException>(() => AuthorPhonologicalFeatureComposer.Build(cache,
            new("nasal", [])));
    }

    [Fact]
    public void AddingAValueRefusesADuplicateAndAForeignFeature()
    {
        using var cache = pristine.NewScratch();
        DefineBinary(cache, "voice", out var feature);
        var add = AddPhonologicalFeatureValueComposer.Build(cache, new(feature, new("both", "+-")));
        SoundSystemComposerTests.Execute(cache, add);
        Assert.Equal(3, ((IFsClosedFeature)cache.LangProject.PhFeatureSystemOA.FeaturesOC.Single()).ValuesOC.Count);
        Assert.Throws<InvalidOperationException>(() =>
            AddPhonologicalFeatureValueComposer.Build(cache, new(feature, new("plus"))));
        var inflection = cache.LangProject.MsFeatureSystemOA.FeaturesOC.OfType<IFsClosedFeature>().FirstOrDefault();
        if (inflection is not null)
            Assert.Throws<InvalidOperationException>(() => AddPhonologicalFeatureValueComposer.Build(cache,
                new(CanonicalId.FromGuid(inflection.Guid), new("x"))));
    }

    [Fact]
    public void SettingChangingAndClearingPhonemeFeaturesIsReadBack()
    {
        using var cache = pristine.NewScratch();
        var voice = DefineBinary(cache, "voice", out _);
        var nasal = DefineBinary(cache, "nasal", out _);
        var all = new List<OperationEnvelope>();
        var phoneme = SoundSystemComposerTests.Compose(cache, all,
            () => AuthorPhonemeComposer.Build(cache, new("b", ["b"])));
        var stored = (IPhPhoneme)cache.ServiceLocator.GetInstance<ICmObjectRepository>().GetObject(phoneme.ToGuid());
        Assert.Null(stored.FeaturesOA);
        SoundSystemComposerTests.Execute(cache, EditPhonemeFeaturesComposer.Build(cache,
            new(phoneme, [voice[0], nasal[1]], [])));
        Assert.Equal(2, stored.FeaturesOA!.FeatureSpecsOC.Count);
        SoundSystemComposerTests.Execute(cache, EditPhonemeFeaturesComposer.Build(cache,
            new(phoneme, [voice[1]], [nasal[1].Feature])));
        var spec = Assert.IsAssignableFrom<IFsClosedValue>(Assert.Single(stored.FeaturesOA!.FeatureSpecsOC));
        Assert.Equal(voice[1].Value.ToGuid(), spec.ValueRA.Guid);
        SoundSystemComposerTests.Execute(cache, EditPhonemeFeaturesComposer.Build(cache,
            new(phoneme, [], [voice[1].Feature])));
        Assert.Empty(stored.FeaturesOA!.FeatureSpecsOC);
    }

    [Fact]
    public void APhonemeFeatureEditRefusesNoOpsContradictionsAndMissingFeatures()
    {
        using var cache = pristine.NewScratch();
        var voice = DefineBinary(cache, "voice", out _);
        var nasal = DefineBinary(cache, "nasal", out _);
        var all = new List<OperationEnvelope>();
        var phoneme = SoundSystemComposerTests.Compose(cache, all,
            () => AuthorPhonemeComposer.Build(cache, new("b", ["b"], [voice[0]])));
        Assert.Throws<InvalidOperationException>(() => EditPhonemeFeaturesComposer.Build(cache, new(phoneme, [], [])));
        Assert.Throws<InvalidOperationException>(() => EditPhonemeFeaturesComposer.Build(cache, new(phoneme, [voice[0]], [])));
        Assert.Throws<InvalidOperationException>(() => EditPhonemeFeaturesComposer.Build(cache,
            new(phoneme, [voice[1]], [voice[1].Feature])));
        Assert.Throws<InvalidOperationException>(() => EditPhonemeFeaturesComposer.Build(cache,
            new(phoneme, [], [nasal[0].Feature])));
        // A value of another feature cannot stand for this feature.
        Assert.Throws<InvalidOperationException>(() => EditPhonemeFeaturesComposer.Build(cache,
            new(phoneme, [new(voice[0].Feature, nasal[0].Value)], [])));
    }

    [Fact]
    public void ANaturalClassFeatureEditChangesConstraintsAndRefusesEmptyingOrSegmentClasses()
    {
        using var cache = pristine.NewScratch();
        var voice = DefineBinary(cache, "voice", out _);
        var nasal = DefineBinary(cache, "nasal", out _);
        var all = new List<OperationEnvelope>();
        var phoneme = SoundSystemComposerTests.Compose(cache, all,
            () => AuthorPhonemeComposer.Build(cache, new("b", ["b"])));
        var featureClass = SoundSystemComposerTests.Compose(cache, all,
            () => AuthorNaturalClassComposer.Build(cache, new("voiced", "VCD", Features: [voice[0]])));
        var segmentClass = SoundSystemComposerTests.Compose(cache, all,
            () => AuthorNaturalClassComposer.Build(cache, new("stops", "STP", Members: [phoneme])));
        var stored = (IPhNCFeatures)cache.ServiceLocator.GetInstance<ICmObjectRepository>().GetObject(featureClass.ToGuid());
        SoundSystemComposerTests.Execute(cache, EditNaturalClassFeaturesComposer.Build(cache,
            new(featureClass, [nasal[0], voice[1]], [])));
        Assert.Equal(2, stored.FeaturesOA!.FeatureSpecsOC.Count);
        SoundSystemComposerTests.Execute(cache, EditNaturalClassFeaturesComposer.Build(cache,
            new(featureClass, [], [voice[1].Feature])));
        Assert.Single(stored.FeaturesOA!.FeatureSpecsOC);
        Assert.Throws<InvalidOperationException>(() => EditNaturalClassFeaturesComposer.Build(cache,
            new(featureClass, [], [nasal[0].Feature])));
        Assert.Throws<InvalidOperationException>(() => EditNaturalClassFeaturesComposer.Build(cache,
            new(segmentClass, [voice[0]], [])));
    }

    [Fact]
    public void TheIntentParsersAcceptClosedShapesOnly()
    {
        using var extra = JsonDocument.Parse("""{"name":"voice","values":[{"name":"plus"}],"colour":"red"}""");
        Assert.Throws<ContractParseException>(() => AuthorPhonologicalFeatureIntentParser.Parse(extra.RootElement));
        using var noValues = JsonDocument.Parse("""{"name":"voice"}""");
        Assert.Throws<ContractParseException>(() => AuthorPhonologicalFeatureIntentParser.Parse(noValues.RootElement));
    }
}
