using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.DryRun;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Composers;

[Collection(LcmCacheParallelCollections.Group1)]
public sealed class EditNaturalClassComposerTests(PristineProjectFixture pristine)
{
    [Fact]
    public void EditsOnlyTheRequestedSegmentIdentities()
    {
        using var cache = pristine.NewScratch();
        var authored = new List<OperationEnvelope>();
        var a = CreatePhoneme(cache, authored, "a");
        var b = CreatePhoneme(cache, authored, "b");
        var p = CreatePhoneme(cache, authored, "p");
        var naturalClass = CreateSegmentClass(cache, authored, "labials", "LAB", [a, b]);

        var operations = EditNaturalClassComposer.Build(cache,
            new EditNaturalClassIntent(naturalClass, [b, a], [p, a]));

        Assert.Equal(2, operations.Count);
        Assert.Equal(PhNCSegmentsSegmentsOperationKinds.RemoveRefSegments,
            Assert.Single(operations, operation => operation.Kind == PhNCSegmentsSegmentsOperationKinds.RemoveRefSegments).Kind);
        Assert.Equal(PhNCSegmentsSegmentsOperationKinds.AddRefSegments,
            Assert.Single(operations, operation => operation.Kind == PhNCSegmentsSegmentsOperationKinds.AddRefSegments).Kind);
        Assert.All(operations, operation => Assert.Equal(naturalClass, operation.Target));
        SoundSystemComposerTests.Execute(cache, operations);
        var segments = Assert.IsAssignableFrom<IPhNCSegments>(
            cache.ServiceLocator.GetInstance<ICmObjectRepository>().GetObject(naturalClass.ToGuid()));
        Assert.Equal(new[] { a.Value, p.Value }.Order(StringComparer.Ordinal),
            segments.SegmentsRC.Select(segment => CanonicalId.FromGuid(segment.Guid).Value).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void RequiresNonemptyDistinctExpectedAndRequestedMembersAndRejectsStaleState()
    {
        using var cache = pristine.NewScratch();
        var authored = new List<OperationEnvelope>();
        var a = CreatePhoneme(cache, authored, "a");
        var b = CreatePhoneme(cache, authored, "b");
        var naturalClass = CreateSegmentClass(cache, authored, "vowels", "V", [a, b]);

        Assert.Throws<InvalidOperationException>(() => EditNaturalClassComposer.Build(cache,
            new EditNaturalClassIntent(naturalClass, [], [a])));
        Assert.Throws<InvalidOperationException>(() => EditNaturalClassComposer.Build(cache,
            new EditNaturalClassIntent(naturalClass, [a, b], [])));
        Assert.Throws<InvalidOperationException>(() => EditNaturalClassComposer.Build(cache,
            new EditNaturalClassIntent(naturalClass, [a, a], [a])));
        Assert.Throws<InvalidOperationException>(() => EditNaturalClassComposer.Build(cache,
            new EditNaturalClassIntent(naturalClass, [a, b], [a, a])));
        var stale = Assert.Throws<InvalidOperationException>(() => EditNaturalClassComposer.Build(cache,
            new EditNaturalClassIntent(naturalClass, [a], [a])));
        Assert.Contains("expected member list", stale.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RefusesForeignPhonemesAndInPlaceFeatureClassConversion()
    {
        using var cache = pristine.NewScratch();
        var authored = new List<OperationEnvelope>();
        var a = CreatePhoneme(cache, authored, "a");
        var segmentClass = CreateSegmentClass(cache, authored, "vowels", "V", [a]);
        IPhPhoneme foreign = null!;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var set = cache.ServiceLocator.GetInstance<IPhPhonemeSetFactory>().Create();
            cache.LangProject.PhonologicalDataOA.PhonemeSetsOS.Add(set);
            foreign = cache.ServiceLocator.GetInstance<IPhPhonemeFactory>().Create();
            set.PhonemesOC.Add(foreign);
            foreign.Name.set_String(cache.DefaultVernWs, "x");
            var code = cache.ServiceLocator.GetInstance<IPhCodeFactory>().Create();
            foreign.CodesOS.Add(code);
            code.Representation.set_String(cache.DefaultVernWs, "x");
        });
        var pair = AddFeature(cache);
        var featureClassOperations = AuthorNaturalClassComposer.Build(cache,
            new AuthorNaturalClassIntent("voiced", "VOI", Features: [pair]));
        SoundSystemComposerTests.Execute(cache, featureClassOperations);
        var featureClass = featureClassOperations.Single(operation =>
            operation.Kind == PhPhonDataNaturalClassesOperationKinds.Create).EntityId!.Value;

        Assert.Throws<InvalidOperationException>(() => EditNaturalClassComposer.Build(cache,
            new EditNaturalClassIntent(segmentClass, [a], [CanonicalId.FromGuid(foreign.Guid)])));
        Assert.Throws<InvalidOperationException>(() => EditNaturalClassComposer.Build(cache,
            new EditNaturalClassIntent(featureClass, [a], [a])));
    }

    [Fact]
    public void ReportsEveryEnvironmentAndRuleThatUsesTheClass()
    {
        using var cache = pristine.NewScratch();
        var authored = new List<OperationEnvelope>();
        var a = CreatePhoneme(cache, authored, "a");
        var b = CreatePhoneme(cache, authored, "b");
        var naturalClass = CreateSegmentClass(cache, authored, "labials", "LAB", [a, b]);
        var environment = SoundSystemComposerTests.Compose(cache, authored,
            () => AuthorEnvironmentComposer.Build(cache, new("after labial", [new(NaturalClass: naturalClass)], [])));
        SoundSystemComposerTests.Execute(cache, AuthorPhonologicalRuleComposer.Build(cache,
            new AuthorPhonologicalRuleIntent("labial context", PhonologicalRuleDirection.Simultaneous,
                [new(Phoneme: a)], [new(Phoneme: b)], [new(NaturalClass: naturalClass)],
                [new(NaturalClass: naturalClass)])));

        var users = EditNaturalClassComposer.ReadAffectedUsers(cache,
            new EditNaturalClassIntent(naturalClass, [a, b], [a]));

        Assert.Equal(2, users.Count);
        Assert.Contains(users, user => user.Kind == "environment" && user.Id == environment.Value);
        Assert.Contains(users, user => user.Kind == "phonological-rule" && user.Name == "labial context");
    }

    [Fact]
    public void RelinksOnlyDeclaredUsersAndDependsOnReplacementCreation()
    {
        using var cache = pristine.NewScratch();
        var operations = new List<OperationEnvelope>();
        var a = CreatePhoneme(cache, operations, "a");
        var b = CreatePhoneme(cache, operations, "b");
        var source = CreateSegmentClass(cache, operations, "source", "SRC", [a, b]);
        var selectedEnvironment = SoundSystemComposerTests.Compose(cache, operations,
            () => AuthorEnvironmentComposer.Build(cache, new("selected user", [new(NaturalClass: source)], [])));
        _ = SoundSystemComposerTests.Compose(cache, operations,
            () => AuthorEnvironmentComposer.Build(cache, new("retained user", [new(NaturalClass: source)], [])));
        var ruleOperations = AuthorPhonologicalRuleComposer.Build(cache,
            new AuthorPhonologicalRuleIntent("source context", PhonologicalRuleDirection.Simultaneous,
                [new(Phoneme: a)], [new(Phoneme: b)], [new(NaturalClass: source)], [new(Phoneme: b)]));
        SoundSystemComposerTests.Execute(cache, ruleOperations);
        operations.AddRange(ruleOperations);
        var ruleContext = Assert.Single(ruleOperations, operation =>
            operation.Kind == PhSimpleContextNCFeatureStructureOperationKinds.SetFeatureStructure &&
            operation.After!.Value.GetProperty("ref").GetString() == source.Value).Target!.Value;
        var baselinePath = cache.ProjectId.Path;
        new FwDataProjectLoader().Save(cache);
        operations.Clear();
        var replacement = CreateSegmentClass(cache, operations, "replacement", "RPL", [a]);
        var replacementCreation = operations.Single(operation =>
            operation.Kind == PhPhonDataNaturalClassesOperationKinds.Create && operation.EntityId == replacement);
        var intent = new RelinkNaturalClassIntent(source, replacement, replacementCreation.OperationId,
            [new(selectedEnvironment, [new(NaturalClass: source)], [])], [ruleContext]);

        var users = EditNaturalClassComposer.ReadAffectedUsers(cache, source);
        Assert.Equal(3, users.Count);
        var relinks = RelinkNaturalClassComposer.Build(cache, intent);
        Assert.Equal(2, relinks.Count);
        Assert.All(relinks, operation => Assert.Contains(operation.DependsOn,
            dependency => dependency.OperationId == replacementCreation.OperationId));
        RelinkNaturalClassComposer.RequireEarlierCreation(operations, intent, relinks);
        Assert.Contains(relinks, operation => operation.Target == selectedEnvironment &&
            operation.Kind == PhEnvironmentStringRepresentationOperationKinds.Set);
        Assert.Contains(relinks, operation => operation.Target == ruleContext &&
            operation.Kind == PhSimpleContextNCFeatureStructureOperationKinds.SetFeatureStructure);

        operations.AddRange(relinks);
        cache.Dispose();
        using var dryScratch = DryRunScratch.Adopt(new FwDataProjectLoader().LoadScratchCache(baselinePath),
            "natural-class relink copy");
        var proposal = new Proposal(new Dictionary<string, string> { ["grammar"] = "1.0" },
            CanonicalId.Mint(), null, operations.ToArray());
        var dryRun = ProposalDryRunner.Run(dryScratch, proposal);

        Assert.NotEmpty(dryRun.ExpectedEffects);
    }

    [Fact]
    public void ParserRequiresCanonicalDistinctMemberListsAndRejectsUnknownProperties()
    {
        var a = CanonicalId.Mint().Value;
        var b = CanonicalId.Mint().Value;
        using var valid = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            target = CanonicalId.Mint().Value,
            expectedMembers = new[] { b, a },
            members = new[] { a, b },
        }));
        using var duplicate = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            target = CanonicalId.Mint().Value,
            expectedMembers = new[] { a, a },
            members = new[] { b },
        }));
        using var unknown = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            target = CanonicalId.Mint().Value,
            expectedMembers = new[] { a },
            members = new[] { b },
            convertSubtype = true,
        }));

        Assert.Equal(new[] { a, b }.Order(StringComparer.Ordinal), EditNaturalClassIntentParser.Parse(valid.RootElement)
            .ExpectedMembers.Select(member => member.Value).Order(StringComparer.Ordinal));
        Assert.Throws<ContractParseException>(() => EditNaturalClassIntentParser.Parse(duplicate.RootElement));
        Assert.Throws<ContractParseException>(() => EditNaturalClassIntentParser.Parse(unknown.RootElement));
    }

    private static CanonicalId CreatePhoneme(LcmCache cache, List<OperationEnvelope> operations, string value) =>
        SoundSystemComposerTests.Compose(cache, operations,
            () => AuthorPhonemeComposer.Build(cache, new(value, [value])));

    private static CanonicalId CreateSegmentClass(LcmCache cache, List<OperationEnvelope> operations,
        string name, string abbreviation, IReadOnlyList<CanonicalId> members) =>
        SoundSystemComposerTests.Compose(cache, operations,
            () => AuthorNaturalClassComposer.Build(cache, new(name, abbreviation, members)));

    private static PhonologicalFeatureValue AddFeature(LcmCache cache)
    {
        PhonologicalFeatureValue pair = null!;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var feature = cache.ServiceLocator.GetInstance<IFsClosedFeatureFactory>().Create();
            cache.LangProject.PhFeatureSystemOA.FeaturesOC.Add(feature);
            var value = cache.ServiceLocator.GetInstance<IFsSymFeatValFactory>().Create();
            feature.ValuesOC.Add(value);
            pair = new(CanonicalId.FromGuid(feature.Guid), CanonicalId.FromGuid(value.Guid));
        });
        return pair;
    }
}
