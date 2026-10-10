using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Model.Snapshot;
using SIL.Motif.Runner.Apply;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.DryRun;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Runner.Snapshotting;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Composers;

[Collection(LcmCacheParallelCollections.Group1)]
public sealed class SoundSystemComposerTests(PristineProjectFixture pristine)
{
    [Fact]
    public void SoundSystem_DryRunApplySaveReopen_UsesAuthoredIdsAndLeavesNoPlaceholder()
    {
        using var authoring = pristine.NewScratch();
        var operations = new List<OperationEnvelope>();
        var a = Compose(authoring, operations, () => AuthorPhonemeComposer.Build(authoring, new("a", ["a", "á"])));
        var i = Compose(authoring, operations, () => AuthorPhonemeComposer.Build(authoring, new("i", ["i"])));
        var naturalClass = Compose(authoring, operations, () => AuthorNaturalClassComposer.Build(authoring,
            new("vowels", "V", [a, i])));
        var environment = Compose(authoring, operations, () => AuthorEnvironmentComposer.Build(authoring,
            new("after a vowel", [new(NaturalClass: naturalClass)], [new(Boundary: "word")])));
        using var live = pristine.NewScratch();
        var before = SoundSystemSnapshots.Read(live);
        using var dryScratch = DryRunScratch.Adopt(pristine.NewScratch(), "pristine sound-system copy");
        var proposal = Proposal(operations);
        var dryRun = ProposalDryRunner.Run(dryScratch, proposal);
        Assert.NotEmpty(dryRun.ExpectedEffects);
        var wordBoundary = CanonicalId.FromGuid(LangProjectTags.kguidPhRuleWordBdry);
        Assert.Contains(dryRun.ExpectedEffects, effect =>
            effect.Field == SnapshotFields.PhPhonemeSetBoundaryMarkers &&
            effect.After.Values.Contains(wordBoundary.Value));
        Assert.Empty(SoundSystemSnapshots.Compare(before, SoundSystemSnapshots.Read(live)));
        var receipt = ProposalApplier.Apply(live, proposal, dryRun.Anchor, "sound-system tests", "Create sound system");
        Assert.False(receipt.AlreadyApplied);
        var after = SoundSystemSnapshots.Read(live);
        var differences = SoundSystemSnapshots.Compare(before, after);
        Assert.Contains(differences, e => e.CanonicalId == a && e.Field == "class" && e.Before.Count == 0);
        Assert.Contains(differences, e => e.CanonicalId == naturalClass);
        Assert.Contains(differences, e => e.CanonicalId == environment);
        Assert.Equal(SoundSystemSnapshots.Compare(before, after).Count, differences.Count);
        var loader = new FwDataProjectLoader();
        loader.Save(live);
        using var reopened = loader.LoadScratchCache(live.ProjectId.Path);
        Assert.Empty(SoundSystemSnapshots.Compare(after, SoundSystemSnapshots.Read(reopened)));
        var repository = reopened.ServiceLocator.GetInstance<ICmObjectRepository>();
        var phoneme = (IPhPhoneme)repository.GetObject(a.ToGuid());
        Assert.Equal(2, phoneme.CodesOS.Count);
        Assert.DoesNotContain(phoneme.CodesOS, c => c.Representation.get_String(reopened.DefaultVernWs)?.Text == "***");
        Assert.Equal("/ [V] _ #", ((IPhEnvironment)repository.GetObject(environment.ToGuid())).StringRepresentation.Text);
        Assert.Equal(new[] { "a", "a\u0301" }, phoneme.CodesOS.Select(c => c.Representation.get_String(reopened.DefaultVernWs).Text));
        var phonemeSet = Assert.Single(reopened.LangProject.PhonologicalDataOA.PhonemeSetsOS);
        foreach (var (id, codeText) in new[]
                 {
                     (LangProjectTags.kguidPhRuleWordBdry, "#"),
                     (LangProjectTags.kguidPhRuleMorphBdry, "+"),
                 })
        {
            var marker = Assert.Single(phonemeSet.BoundaryMarkersOC, item => item.Guid == id);
            Assert.Equal(phonemeSet.Guid, marker.Owner?.Guid);
            Assert.Equal(codeText, marker.Name.get_String(reopened.DefaultVernWs)?.Text);
            Assert.Equal(codeText, Assert.Single(marker.CodesOS).Representation
                .get_String(reopened.DefaultVernWs)?.Text);
        }
    }

    [Fact]
    public void FeatureClassAndPhoneme_ChooseValuesFromThePhonologicalSystem()
    {
        using var cache = pristine.NewScratch();
        var pair = Feature(cache);
        var operations = new List<OperationEnvelope>();
        var phonemeId = Compose(cache, operations, () => AuthorPhonemeComposer.Build(cache, new("a", ["a"], [pair])));
        var classId = Compose(cache, operations, () => AuthorNaturalClassComposer.Build(cache,
            new("voiced", "Voiced", Features: [pair])));
        var repository = cache.ServiceLocator.GetInstance<ICmObjectRepository>();
        var phoneme = (IPhPhoneme)repository.GetObject(phonemeId.ToGuid());
        var naturalClass = (IPhNCFeatures)repository.GetObject(classId.ToGuid());
        foreach (var structure in new[] { phoneme.FeaturesOA, naturalClass.FeaturesOA })
        {
            var spec = Assert.IsAssignableFrom<IFsClosedValue>(Assert.Single(structure.FeatureSpecsOC));
            Assert.Equal(pair.Feature.ToGuid(), spec.FeatureRA.Guid);
            Assert.Equal(pair.Value.ToGuid(), spec.ValueRA.Guid);
        }
        var description = AuthorEnvironmentComposer.Build(cache, new("voiced context", [new(NaturalClass: classId)], []));
        Execute(cache, description);
        var snapshot = SoundSystemSnapshots.Read(cache);
        new FwDataProjectLoader().Save(cache);
        using var reopened = new FwDataProjectLoader().LoadScratchCache(cache.ProjectId.Path);
        Assert.Empty(SoundSystemSnapshots.Compare(snapshot, SoundSystemSnapshots.Read(reopened)));
    }

    [Fact]
    public void InvalidInputsRefuseBeforeMutatingTheProject()
    {
        using var cache = pristine.NewScratch();
        var before = SoundSystemSnapshots.Read(cache);
        Assert.Throws<InvalidOperationException>(() => AuthorPhonemeComposer.Build(cache, new("bad", [])));
        Assert.Throws<InvalidOperationException>(() => AuthorPhonemeComposer.Build(cache, new("bad", ["***"])));
        Assert.Throws<InvalidOperationException>(() => AuthorPhonemeComposer.Build(cache, new("bad", ["á", "a\u0301"])));
        Assert.Throws<InvalidOperationException>(() => AuthorNaturalClassComposer.Build(cache, new("bad", "V", [], [])));
        Assert.Throws<InvalidOperationException>(() => AuthorNaturalClassComposer.Build(cache, new("bad", "V")));
        Assert.Throws<InvalidOperationException>(() => AuthorNaturalClassComposer.Build(cache, new("bad", "[V]", [])));
        Assert.Throws<InvalidOperationException>(() => AuthorEnvironmentComposer.Build(cache, new("bad", [], [])));
        Assert.Throws<InvalidOperationException>(() => AuthorEnvironmentComposer.Build(cache,
            new("bad", [new(Boundary: "invalid")], [])));
        Assert.Throws<InvalidOperationException>(() => AuthorEnvironmentComposer.Build(cache,
            new("bad", [new(Boundary: "word"), new(Boundary: "word")], [])));
        Assert.Empty(SoundSystemSnapshots.Compare(before, SoundSystemSnapshots.Read(cache)));
    }

    [Fact]
    public void EnvironmentBoundaryUsesReservedMorphemeIdentityAndKeepsExplicitMarkersLiteral()
    {
        using var cache = pristine.NewScratch();
        var phoneme = AuthorPhonemeComposer.Build(cache, new("a", ["a"]));
        Execute(cache, phoneme);
        var set = cache.LangProject.PhonologicalDataOA.PhonemeSetsOS.Single();
        var existingMorpheme = set.BoundaryMarkersOC.SingleOrDefault(marker =>
            marker.Guid == LangProjectTags.kguidPhRuleMorphBdry);
        if (existingMorpheme is not null)
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
                set.BoundaryMarkersOC.Remove(existingMorpheme));
        _ = AddBoundaryMarker(cache, Guid.NewGuid(), "+");
        var beforeRefusal = SoundSystemSnapshots.Read(cache);
        var missingReserved = Assert.Throws<InvalidOperationException>(() => AuthorEnvironmentComposer.Build(cache,
            new("morpheme edge", [new(Boundary: "morpheme")], [])));
        Assert.Contains("morpheme boundary", missingReserved.Message, StringComparison.Ordinal);
        Assert.Empty(SoundSystemSnapshots.Compare(beforeRefusal, SoundSystemSnapshots.Read(cache)));

        var morpheme = AddBoundaryMarker(cache, LangProjectTags.kguidPhRuleMorphBdry, "+");
        var environment = AuthorEnvironmentComposer.Build(cache,
            new("morpheme edge", [new(Boundary: "morpheme")], []));
        Execute(cache, environment);
        var environmentId = environment.Single(operation =>
            operation.Kind == PhPhonDataEnvironmentsOperationKinds.Create).EntityId!.Value;
        Assert.Equal("/ + _", ((IPhEnvironment)cache.ServiceLocator.GetInstance<ICmObjectRepository>()
            .GetObject(environmentId.ToGuid())).StringRepresentation.Text);
        var beforeExplicitMorpheme = SoundSystemSnapshots.Read(cache);
        var literalMorphemeError = Assert.Throws<InvalidOperationException>(() => AuthorEnvironmentComposer.Build(cache,
            new("literal plus marker", [new(BoundaryMarker: morpheme)], [])));
        Assert.Contains("environment syntax", literalMorphemeError.Message, StringComparison.Ordinal);
        Assert.Empty(SoundSystemSnapshots.Compare(beforeExplicitMorpheme, SoundSystemSnapshots.Read(cache)));
        var literalHash = AddBoundaryMarker(cache, Guid.NewGuid(), "#");
        var beforeLiteralRefusal = SoundSystemSnapshots.Read(cache);
        var error = Assert.Throws<InvalidOperationException>(() => AuthorEnvironmentComposer.Build(cache,
            new("literal hash", [new(BoundaryMarker: literalHash)], [])));
        Assert.Contains("environment syntax", error.Message, StringComparison.Ordinal);
        Assert.Equal(LangProjectTags.kguidPhRuleMorphBdry, morpheme.ToGuid());
        Assert.Empty(SoundSystemSnapshots.Compare(beforeLiteralRefusal, SoundSystemSnapshots.Read(cache)));
    }

    [Theory]
    [InlineData("phoneme", "{\"name\":\"a\",\"representations\":[\"a\"],\"setAnyField\":true}")]
    [InlineData("phoneme", "{\"name\":\"a\",\"name\":\"b\",\"representations\":[\"a\"]}")]
    [InlineData("class", "{\"name\":\"vowels\",\"abbreviation\":\"V\",\"features\":[{\"feature\":\"bad\",\"value\":\"bad\",\"condition\":true}]}")]
    [InlineData("environment", "{\"name\":\"x\",\"left\":[{\"boundary\":\"word\",\"phoneme\":\"bad\"}],\"right\":[]}")]
    [InlineData("environment", "{\"name\":\"x\",\"left\":[],\"right\":[],\"pattern\":\"/ _\"}")]
    public void IntentSchemasAreClosed(string family, string json)
    {
        using var document = JsonDocument.Parse(json);
        Assert.Throws<ContractParseException>(() =>
        {
            if (family == "phoneme") AuthorPhonemeIntentParser.Parse(document.RootElement);
            else if (family == "class") AuthorNaturalClassIntentParser.Parse(document.RootElement);
            else AuthorEnvironmentIntentParser.Parse(document.RootElement);
        });
    }

    [Fact]
    public void DuplicateGraphemesAbbreviationsAndForeignValuesRefuse()
    {
        using var cache = pristine.NewScratch();
        var operations = new List<OperationEnvelope>();
        var a = Compose(cache, operations, () => AuthorPhonemeComposer.Build(cache, new("a", ["a"])));
        Compose(cache, operations, () => AuthorNaturalClassComposer.Build(cache, new("vowels", "V", [a])));
        Assert.Throws<InvalidOperationException>(() => AuthorPhonemeComposer.Build(cache, new("another a", ["a"])));
        Assert.Throws<InvalidOperationException>(() => AuthorNaturalClassComposer.Build(cache, new("another V", "V", [a])));
        Assert.Throws<InvalidOperationException>(() => AuthorNaturalClassComposer.Build(cache, new("double a", "AA", [a, a])));
        var pair = Feature(cache);
        var other = Feature(cache);
        Assert.Throws<InvalidOperationException>(() => AuthorNaturalClassComposer.Build(cache,
            new("wrong value", "Wrong", Features: [new(pair.Feature, other.Value)])));
        Assert.Throws<InvalidOperationException>(() => AuthorPhonemeComposer.Build(cache, new("b", ["b"], [pair, pair])));
        Assert.ThrowsAny<Exception>(() => AuthorNaturalClassComposer.Build(cache, new("wrong type", "Wrong", [pair.Feature])));
    }

    [Fact]
    public void FailedProposalRollsBackEveryCreatedObjectAndMembership()
    {
        using var live = pristine.NewScratch();
        using var authoring = pristine.NewScratch();
        var operations = new List<OperationEnvelope>();
        var a = Compose(authoring, operations, () => AuthorPhonemeComposer.Build(authoring, new("a", ["a"])));
        var naturalClass = Compose(authoring, operations, () => AuthorNaturalClassComposer.Build(authoring, new("vowels", "V", [a])));
        Compose(authoring, operations, () => AuthorEnvironmentComposer.Build(authoring, new("after vowel", [new(NaturalClass: naturalClass)], [])));
        var before = SoundSystemSnapshots.Read(live);
        var failing = new OperationEnvelope(CanonicalId.Mint(), PhCodeRepresentationOperationKinds.SetRepresentation,
            target: CanonicalId.Mint(), after: JsonSerializer.SerializeToElement(new { ws = "missing", text = "x" }));
        operations.Add(failing);
        using var unit = new UndoableUnitOfWorkHelper(live.ActionHandlerAccessor, "atomic", "atomic");
        Assert.ThrowsAny<Exception>(() =>
        {
            foreach (var operation in operations)
                OperationHandlerRegistry.Resolve(operation.Kind, "rollback test").ApplyAndCaptureEffect(live, operation, []);
        });
        unit.Dispose();
        Assert.Empty(SoundSystemSnapshots.Compare(before, SoundSystemSnapshots.Read(live)));
    }

    [Fact]
    public void SequenceDriftAndIdentityCollisionRefuseInsteadOfReinterpretingCreation()
    {
        using var live = pristine.NewScratch();
        var operations = new List<OperationEnvelope>();
        var a = Compose(live, operations, () => AuthorPhonemeComposer.Build(live, new("a", ["a"])));
        var proposal = Proposal(AuthorNaturalClassComposer.Build(live, new("vowels", "V", [a])));
        new FwDataProjectLoader().Save(live);
        using var scratchCache = new FwDataProjectLoader().LoadScratchCache(live.ProjectId.Path);
        using var scratch = DryRunScratch.Adopt(scratchCache, "sequence drift copy");
        var run = ProposalDryRunner.Run(scratch, proposal);
        NonUndoableUnitOfWorkHelper.Do(live.ActionHandlerAccessor, () =>
            live.LangProject.PhonologicalDataOA.NaturalClassesOS.Add(live.ServiceLocator.GetInstance<IPhNCSegmentsFactory>().Create()));
        Assert.ThrowsAny<Exception>(() => ProposalApplier.Apply(live, proposal, run.Anchor, "test", "must refuse"));
        var collision = new OperationEnvelope(CanonicalId.Mint(), PhPhonemeSetPhonemesOperationKinds.Create,
            entityId: a, target: CanonicalId.FromGuid(live.LangProject.PhonologicalDataOA.PhonemeSetsOS[0].Guid),
            after: JsonSerializer.SerializeToElement(new { }));
        NonUndoableUnitOfWorkHelper.Do(live.ActionHandlerAccessor, () =>
        {
            var error = Assert.Throws<InvalidOperationException>(() => OperationHandlerRegistry.Resolve(collision.Kind, "test")
                .ApplyAndCaptureEffect(live, collision, []));
            Assert.Contains("overwrite/reuse", error.Message);
        });
    }

    [Fact]
    public void EnvironmentReferenceRenameRefusesRatherThanChangingItsCondition()
    {
        using var cache = pristine.NewScratch();
        var all = new List<OperationEnvelope>();
        var phoneme = Compose(cache, all, () => AuthorPhonemeComposer.Build(cache, new("a", ["a"])));
        var naturalClass = Compose(cache, all, () => AuthorNaturalClassComposer.Build(cache, new("vowels", "V", [phoneme])));
        var environment = AuthorEnvironmentComposer.Build(cache, new("after vowel", [new(NaturalClass: naturalClass)], []));
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            ((IPhNaturalClass)cache.ServiceLocator.GetInstance<ICmObjectRepository>().GetObject(naturalClass.ToGuid()))
                .Abbreviation.set_String(cache.DefaultAnalWs, "Changed"));
        var error = Assert.Throws<InvalidOperationException>(() => Execute(cache, environment));
        Assert.Contains("no longer match", error.Message);
    }

    [Fact]
    public void LoweredDependenciesAndClosedPayloadsAreConformant()
    {
        using var cache = pristine.NewScratch();
        var operations = AuthorPhonemeComposer.Build(cache, new("a", ["a", "aa"]));
        var creates = operations.Where(o => o.EntityId is not null).ToDictionary(o => o.EntityId!.Value, o => o.OperationId);
        foreach (var operation in operations)
        {
            if (operation.Target is { } target && creates.TryGetValue(target, out var create))
                Assert.Contains(operation.DependsOn, d => d.OperationId == create);
            if (operation.Placement?.After is { } neighbour && creates.TryGetValue(neighbour, out var prior))
                Assert.Contains(operation.DependsOn, d => d.OperationId == prior);
        }
        var first = operations[0];
        var invalid = new OperationEnvelope(first.OperationId, first.Kind, first.EntityId, first.Target,
            JsonSerializer.SerializeToElement(new { condition = true }));
        Assert.Throws<ContractParseException>(() => Execute(cache, new[] { invalid }));
        Assert.Empty(cache.LangProject.PhonologicalDataOA.PhonemeSetsOS);
    }

    private static PhonologicalFeatureValue Feature(LcmCache cache)
    {
        PhonologicalFeatureValue result = null!;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var feature = cache.ServiceLocator.GetInstance<IFsClosedFeatureFactory>().Create();
            cache.LangProject.PhFeatureSystemOA.FeaturesOC.Add(feature);
            var value = cache.ServiceLocator.GetInstance<IFsSymFeatValFactory>().Create();
            feature.ValuesOC.Add(value);
            result = new(CanonicalId.FromGuid(feature.Guid), CanonicalId.FromGuid(value.Guid));
        });
        return result;
    }

    private static CanonicalId AddBoundaryMarker(LcmCache cache, Guid guid, string codeText)
    {
        IPhBdryMarker marker = null!;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var set = cache.LangProject.PhonologicalDataOA.PhonemeSetsOS.Single();
            marker = cache.ServiceLocator.GetInstance<IPhBdryMarkerFactory>().Create(guid);
            set.BoundaryMarkersOC.Add(marker);
            marker.Name.set_String(cache.DefaultVernWs, codeText);
            var code = cache.ServiceLocator.GetInstance<IPhCodeFactory>().Create();
            marker.CodesOS.Add(code);
            code.Representation.set_String(cache.DefaultVernWs, codeText);
        });
        return CanonicalId.FromGuid(marker.Guid);
    }

    internal static CanonicalId Compose(LcmCache cache, List<OperationEnvelope> all, Func<IReadOnlyList<OperationEnvelope>> build)
    {
        var operations = build();
        Execute(cache, operations);
        all.AddRange(operations);
        var created = operations.First(o => o.EntityId is not null && o.Kind is not
            (PhPhonDataPhonemeSetsOperationKinds.Create or PhPhonemeSetBoundaryMarkersOperationKinds.Create or PhTerminalUnitCodesOperationKinds.Create));
        return created.EntityId!.Value;
    }

    internal static void Execute(LcmCache cache, IReadOnlyList<OperationEnvelope> operations) =>
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            foreach (var operation in operations)
                OperationHandlerRegistry.Resolve(operation.Kind, "sound-system fixture").ApplyAndCaptureEffect(cache, operation, []);
        });

    private static Proposal Proposal(IReadOnlyList<OperationEnvelope> operations) =>
        new(new Dictionary<string, string> { ["grammar"] = "1.0" }, CanonicalId.Mint(), null, operations);
}
