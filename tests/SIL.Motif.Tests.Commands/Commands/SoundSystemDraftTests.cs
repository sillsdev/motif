using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Runner.DryRun;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Worker.Store;
using SIL.Motif.Runner.Composers;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[Collection(LcmCacheParallelCollections.Group2)]
public sealed class SoundSystemDraftTests(PristineProjectFixture pristine)
{
    [Fact]
    public void FeatureValueSelectsAValueOnAStructureCreatedInTheSameDraft()
    {
        using var cache = pristine.NewScratch();
        var path = cache.ProjectId.Path;
        SIL.LCModel.IFsClosedFeature feature = null!;
        SIL.LCModel.IFsSymFeatVal value = null!;
        SIL.LCModel.Infrastructure.NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            feature = cache.ServiceLocator.GetInstance<SIL.LCModel.IFsClosedFeatureFactory>().Create();
            cache.LangProject.MsFeatureSystemOA.FeaturesOC.Add(feature);
            value = cache.ServiceLocator.GetInstance<SIL.LCModel.IFsSymFeatValFactory>().Create();
            feature.ValuesOC.Add(value);
        });
        var featureId = CanonicalId.FromGuid(feature.Guid).Value;
        var valueId = CanonicalId.FromGuid(value.Guid).Value;
        var msaId = CanonicalId.FromGuid(cache.ServiceLocator.GetInstance<SIL.LCModel.ILexEntryRepository>()
            .GetObject(pristine.Seed.FirstEntryId).MorphoSyntaxAnalysesOC.First().Guid).Value;
        new SIL.Motif.Host.LcmUtils.FwDataProjectLoader().Save(cache);
        cache.Dispose();
        const string draft = "chosen-value";
        const string version = "0.1.0";
        Assert.True(ProposalCommands.New(new(path, version, draft, "Choose a feature value")).Succeeded);
        var structure = ProposalCommands.ComposeAuthorFeatureStructure(new(path, version, draft,
            JsonSerializer.Serialize(new { msa = msaId })));
        Assert.True(structure.Succeeded, structure.Refusal?.Message);
        AssertCreatedObjectsMatch(structure.Value!);
        var created = structure.Value!.Operations.Single(o => o.EntityId is not null);
        var selected = ProposalCommands.ComposeIntent(new(path, version, draft, "compose-author-feature-value",
            JsonSerializer.Serialize(new { featStruc = created.EntityId, feature = featureId, value = valueId })));
        Assert.True(selected.Succeeded, selected.Refusal?.Message);
        AssertCreatedObjectsMatch(selected.Value!);
        Assert.Contains(selected.Value!.Operations, o => o.Kind.EndsWith("/setValue", StringComparison.Ordinal));
        var duplicate = ProposalCommands.ComposeIntent(new(path, version, draft, "compose-author-feature-value",
            JsonSerializer.Serialize(new { featStruc = created.EntityId, feature = featureId, value = valueId })));
        Assert.False(duplicate.Succeeded);
        Assert.Contains("already has a feature specification", duplicate.Refusal!.Message);
    }

    [Fact]
    public void ACompleteSoundSystemDraftReferencesItsOwnObjectsWithoutWritingTheLiveProject()
    {
        using var cache = pristine.NewScratch();
        var path = cache.ProjectId.Path;
        cache.Dispose();
        const string version = "0.1.0";
        const string draft = "sound-system";
        Assert.True(ProposalCommands.New(new(path, version, draft, "Author sound system")).Succeeded);
        Assert.True(DraftRationale.Write(path, version, draft, "Synthetic vowel context.").Succeeded);
        var phoneme = ProposalCommands.ComposeAuthorPhoneme(new(path, version, draft,
            JsonSerializer.Serialize(new { name = "a", representations = new[] { "a" } })));
        Assert.True(phoneme.Succeeded, phoneme.Refusal?.Message);
        AssertCreatedObjectsMatch(phoneme.Value!);
        var createdPhoneme = phoneme.Value!.Operations.Single(o => o.Kind.EndsWith("/createPhonemes", StringComparison.Ordinal));
        var second = ProposalCommands.ComposeAuthorPhoneme(new(path, version, draft,
            JsonSerializer.Serialize(new { name = "i", representations = new[] { "i" } })));
        Assert.True(second.Succeeded, second.Refusal?.Message);
        AssertCreatedObjectsMatch(second.Value!);
        var naturalClass = ProposalCommands.ComposeAuthorNaturalClass(new(path, version, draft,
            JsonSerializer.Serialize(new { name = "vowels", abbreviation = "V", members = new[] { createdPhoneme.EntityId } })));
        Assert.True(naturalClass.Succeeded, naturalClass.Refusal?.Message);
        AssertCreatedObjectsMatch(naturalClass.Value!);
        var createdClass = naturalClass.Value!.Operations.Single(o => o.Kind.EndsWith("/createNaturalClasses", StringComparison.Ordinal));
        var environment = ProposalCommands.ComposeAuthorEnvironment(new(path, version, draft,
            JsonSerializer.Serialize(new { name = "after a vowel", left = new[] { new { naturalClass = createdClass.EntityId } }, right = Array.Empty<object>() })));
        Assert.True(environment.Succeeded, environment.Refusal?.Message);
        AssertCreatedObjectsMatch(environment.Value!);
        var bad = ProposalCommands.ComposeAuthorNaturalClass(new(path, version, draft,
            JsonSerializer.Serialize(new { name = "duplicate", abbreviation = "V", members = new[] { createdPhoneme.EntityId } })));
        Assert.False(bad.Succeeded);
        Assert.Contains("already exists", bad.Refusal!.Message);
        var finalized = ProposalCommands.Finalize(new(path, version, draft));
        Assert.True(finalized.Succeeded, finalized.Refusal?.Message);
        var loaded = ProjectStoreCommand.Run<Proposal>(path, version, (database, _) =>
            CommandOutcome<Proposal>.Success(ProposalJsonParser.Parse(new ProposalRepository(database)
                .Get(CanonicalId.Parse(finalized.Value!.ProposalId)).ProposalJson!)));
        Assert.True(loaded.Succeeded, loaded.Refusal?.Message);
        var proposal = loaded.Value!;
        var member = proposal.Operations.Single(o => o.Kind.EndsWith("/addRefSegments", StringComparison.Ordinal));
        Assert.Contains(member.DependsOn, d => d.OperationId.Value == createdPhoneme.OperationId);
        var setString = proposal.Operations.Single(o => o.Kind.EndsWith("/setStringRepresentation", StringComparison.Ordinal));
        Assert.Contains(setString.DependsOn, d => d.OperationId.Value == createdClass.OperationId);
        var secondCode = proposal.Operations.Last(o => o.Kind.EndsWith("/createCodes", StringComparison.Ordinal));
        Assert.NotNull(secondCode.EntityId);
        using var live = new SIL.Motif.Host.LcmUtils.FwDataProjectLoader().LoadScratchCache(path);
        Assert.Empty(live.LangProject.PhonologicalDataOA.PhonemeSetsOS);
        using var scratch = DryRunScratch.Adopt(new SIL.Motif.Host.LcmUtils.FwDataProjectLoader().LoadScratchCache(path), "Draft replay");
        Assert.NotEmpty(ProposalDryRunner.Run(scratch, proposal).ExpectedEffects);
    }

    [Fact]
    public void AuthorEnvironmentResolvesNaturalClassNameCreatedEarlierInTheDraft()
    {
        using var cache = pristine.NewScratch();
        var path = cache.ProjectId.Path;
        cache.Dispose();

        const string version = "0.1.0";
        const string draft = "named-environment-class";
        Assert.True(ProposalCommands.New(new(path, version, draft, "Use a named natural class")).Succeeded);
        var phoneme = ProposalCommands.ComposeAuthorPhoneme(new(path, version, draft,
            JsonSerializer.Serialize(new { name = "a", representations = new[] { "a" } })));
        Assert.True(phoneme.Succeeded, phoneme.Refusal?.Message);
        var phonemeCreate = Assert.Single(phoneme.Value!.Operations,
            operation => operation.Kind == PhPhonemeSetPhonemesOperationKinds.Create);
        var naturalClass = ProposalCommands.ComposeAuthorNaturalClass(new(path, version, draft,
            JsonSerializer.Serialize(new
            {
                name = "vowels",
                abbreviation = "V",
                members = new[] { phonemeCreate.EntityId },
            })));
        Assert.True(naturalClass.Succeeded, naturalClass.Refusal?.Message);
        var classCreate = Assert.Single(naturalClass.Value!.Operations,
            operation => operation.Kind == PhPhonDataNaturalClassesOperationKinds.Create);

        var environment = ProposalCommands.ComposeAuthorEnvironment(new(path, version, draft,
            JsonSerializer.Serialize(new
            {
                name = "after a vowel",
                left = new[] { new { naturalClass = "vowels" } },
                right = Array.Empty<object>(),
            })));

        Assert.True(environment.Succeeded, environment.Refusal?.Message);
        var representation = Assert.Single(ReadDraftProposal(path, draft).Operations,
            operation => operation.Kind == PhEnvironmentStringRepresentationOperationKinds.Set);
        using var payload = JsonDocument.Parse(representation.After!.Value.GetRawText());
        Assert.Equal(classCreate.EntityId,
            payload.RootElement.GetProperty("left")[0].GetProperty("naturalClass").GetString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MembershipEditOfAClassCreatedEarlierInTheDraftPreservesInitialization(bool canonicalTarget)
    {
        using var cache = pristine.NewScratch();
        var path = cache.ProjectId.Path;
        var aOperations = AuthorPhonemeComposer.Build(cache, new("a", ["a"]));
        ExecuteAuthored(cache, aOperations);
        var a = Assert.Single(aOperations,
            operation => operation.Kind == PhPhonemeSetPhonemesOperationKinds.Create).EntityId!.Value;
        var bOperations = AuthorPhonemeComposer.Build(cache, new("b", ["b"]));
        ExecuteAuthored(cache, bOperations);
        var b = Assert.Single(bOperations,
            operation => operation.Kind == PhPhonemeSetPhonemesOperationKinds.Create).EntityId!.Value;
        new SIL.Motif.Host.LcmUtils.FwDataProjectLoader().Save(cache);
        cache.Dispose();
        const string draft = "edit-created-class";
        const string version = "0.1.0";
        Assert.True(ProposalCommands.New(new(path, version, draft, "Correct a staged class")).Succeeded);
        var creation = ProposalCommands.ComposeAuthorNaturalClass(new(path, version, draft,
            JsonSerializer.Serialize(new { name = "vowels", abbreviation = "V", members = new[] { a.Value, b.Value } })));
        Assert.True(creation.Succeeded, creation.Refusal?.Message);
        var classCreate = Assert.Single(creation.Value!.Operations,
            operation => operation.Kind == PhPhonDataNaturalClassesOperationKinds.Create);
        var before = ReadDraftProposal(path, draft);
        var target = canonicalTarget ? classCreate.EntityId! : "vowels";
        CommandOutcome<ComposedOperationsResponse> Edit(string[] expectedMembers, string[] members) =>
            ProposalCommands.ComposeEditNaturalClass(new(path, version, draft,
                JsonSerializer.Serialize(new { target, expectedMembers, members })));
        void AssertReadBack(params CanonicalId[] expected)
        {
            var current = ReadDraftProposal(path, draft);
            ProposalOperationSlotValidator.Validate(current);
            using var scratch = DryRunScratch.Adopt(new SIL.Motif.Host.LcmUtils.FwDataProjectLoader().LoadScratchCache(path),
                "Created natural-class edit read-back");
            Assert.NotEmpty(ProposalDryRunner.Run(scratch, current).ExpectedEffects);
            var edited = scratch.PeekCache().ServiceLocator.GetInstance<IPhNCSegmentsRepository>()
                .GetObject(CanonicalId.Parse(classCreate.EntityId!).ToGuid());
            Assert.Equal(expected.Select(member => member.ToGuid()).Order(), edited.SegmentsRC.Select(member => member.Guid).Order());
        }
        var edit = Edit([a.Value, b.Value], [a.Value]);
        Assert.True(edit.Succeeded, edit.Refusal?.Message);
        var proposal = ReadDraftProposal(path, draft);
        Assert.Contains(proposal.Operations, operation => operation.OperationId.Value == classCreate.OperationId);
        var unchangedMember = Assert.Single(before.Operations, operation =>
            operation.Kind == PhNCSegmentsSegmentsOperationKinds.AddRefSegments &&
            operation.After!.Value.GetProperty("member").GetString() == a.Value);
        Assert.Contains(proposal.Operations, operation => operation.OperationId == unchangedMember.OperationId);
        var originalCreation = Assert.Single(before.Extensions!.Value.GetProperty("composers").EnumerateArray(),
            item => item.GetProperty("composer").GetString() == "AuthorNaturalClass");
        var recordedCreation = Assert.Single(proposal.Extensions!.Value.GetProperty("composers").EnumerateArray(),
            item => item.GetProperty("composer").GetString() == "AuthorNaturalClass");
        Assert.Equal(originalCreation.GetProperty("input").GetRawText(), recordedCreation.GetProperty("input").GetRawText());
        var recordedIds = recordedCreation.GetProperty("operationIds").EnumerateArray().Select(id => id.GetString()).ToArray();
        Assert.Contains(classCreate.OperationId, recordedIds);
        Assert.Contains(unchangedMember.OperationId.Value, recordedIds);
        Assert.All(recordedIds, id => Assert.Contains(proposal.Operations, operation => operation.OperationId.Value == id));
        AssertReadBack(a);

        var beforeStale = JsonSerializer.Serialize(proposal);
        var stale = Edit([a.Value, b.Value], [b.Value]);
        Assert.Equal("draft.invalid", stale.Refusal?.Code);
        Assert.Contains("expected member list", stale.Refusal!.Message);
        Assert.Equal(beforeStale, JsonSerializer.Serialize(ReadDraftProposal(path, draft)));
        var restore = Edit([a.Value], [a.Value, b.Value]);
        Assert.True(restore.Succeeded, restore.Refusal?.Message);
        AssertReadBack(a, b);
        var beforeOverlap = JsonSerializer.Serialize(ReadDraftProposal(path, draft));
        var overlap = ProposalCommands.ComposeBatch(new(path, version, draft, JsonSerializer.Serialize(new { intents = new[]
        {
            new { verb = "compose-edit-natural-class", intent = new
                { target, expectedMembers = new[] { a.Value, b.Value }, members = new[] { a.Value } } },
            new { verb = "compose-edit-natural-class", intent = new
                { target, expectedMembers = new[] { a.Value }, members = new[] { b.Value } } },
        } })));
        Assert.Equal("draft.invalid", overlap.Refusal?.Code);
        Assert.Contains("already edits the same target", overlap.Refusal!.Message);
        Assert.Equal(beforeOverlap, JsonSerializer.Serialize(ReadDraftProposal(path, draft)));
        var repeated = Edit([a.Value, b.Value], [b.Value]);
        Assert.True(repeated.Succeeded, repeated.Refusal?.Message);
        AssertReadBack(b);
        var replaceAgain = Edit([b.Value], [a.Value]);
        Assert.True(replaceAgain.Succeeded, replaceAgain.Refusal?.Message);
        AssertReadBack(a);
        var environment = ProposalCommands.ComposeAuthorEnvironment(new(path, version, draft, JsonSerializer.Serialize(new
        {
            name = "after vowels", left = new[] { new { naturalClass = "vowels" } }, right = Array.Empty<object>(),
        })));
        Assert.True(environment.Succeeded, environment.Refusal?.Message);
        var beforeDependant = JsonSerializer.Serialize(ReadDraftProposal(path, draft));
        var dependent = Edit([a.Value], [b.Value]);
        Assert.Equal("draft.invalid", dependent.Refusal?.Code);
        Assert.Contains("depends on an edit", dependent.Refusal!.Message);
        Assert.Equal(beforeDependant, JsonSerializer.Serialize(ReadDraftProposal(path, draft)));
        AssertReadBack(a);
        var final = ReadDraftProposal(path, draft);
        Assert.All(final.Extensions!.Value.GetProperty("composers").EnumerateArray(), provenance =>
            Assert.All(provenance.GetProperty("operationIds").EnumerateArray(), id =>
                Assert.Contains(final.Operations, operation => operation.OperationId.Value == id.GetString())));
        using var saved = new SIL.Motif.Host.LcmUtils.FwDataProjectLoader().LoadScratchCache(path);
        Assert.DoesNotContain(saved.LangProject.PhonologicalDataOA.NaturalClassesOS,
            naturalClass => naturalClass.Guid == CanonicalId.Parse(classCreate.EntityId!).ToGuid());
    }

    [Fact]
    public void NaturalClassComposersResolveClassAndEnvironmentNames()
    {
        using var cache = pristine.NewScratch();
        var path = cache.ProjectId.Path;
        var aOperations = AuthorPhonemeComposer.Build(cache, new("a", ["a"]));
        ExecuteAuthored(cache, aOperations);
        var aId = aOperations.Single(operation => operation.Kind == PhPhonemeSetPhonemesOperationKinds.Create)
            .EntityId!.Value;
        var bOperations = AuthorPhonemeComposer.Build(cache, new("b", ["b"]));
        ExecuteAuthored(cache, bOperations);
        var bId = bOperations.Single(operation => operation.Kind == PhPhonemeSetPhonemesOperationKinds.Create)
            .EntityId!.Value;
        var sourceOperations = AuthorNaturalClassComposer.Build(cache, new("vowels", "V", [aId, bId]));
        ExecuteAuthored(cache, sourceOperations);
        var sourceId = sourceOperations.Single(operation =>
            operation.Kind == PhPhonDataNaturalClassesOperationKinds.Create).EntityId!.Value;
        var environmentOperations = AuthorEnvironmentComposer.Build(cache,
            new("after a vówel", [new(NaturalClass: sourceId)], []));
        ExecuteAuthored(cache, environmentOperations);
        new SIL.Motif.Host.LcmUtils.FwDataProjectLoader().Save(cache);
        cache.Dispose();

        const string version = "0.1.0";
        const string draft = "named-natural-class-edit";
        Assert.True(ProposalCommands.New(new(path, version, draft, "Use named sound objects")).Succeeded);
        var edit = ProposalCommands.ComposeEditNaturalClass(new(path, version, draft,
            JsonSerializer.Serialize(new
            {
                target = "vowels",
                expectedMembers = new[] { aId.Value, bId.Value },
                members = new[] { aId.Value },
            })));
        Assert.True(edit.Succeeded, edit.Refusal?.Message);

        var repeated = ProposalCommands.ComposeEditNaturalClass(new(path, version, draft,
            JsonSerializer.Serialize(new
            {
                target = "v", expectedMembers = new[] { aId.Value, bId.Value }, members = new[] { bId.Value },
            })));
        Assert.True(repeated.Succeeded, repeated.Refusal?.Message);
        Assert.Single(ReadDraftProposal(path, draft).Operations,
            operation => operation.Kind == PhNCSegmentsSegmentsOperationKinds.RemoveRefSegments);
        var beforeRefusal = JsonSerializer.Serialize(ReadDraftProposal(path, draft));
        var stale = ProposalCommands.ComposeEditNaturalClass(new(path, version, draft,
            JsonSerializer.Serialize(new { target = "vowels", expectedMembers = new[] { aId.Value }, members = new[] { bId.Value } })));
        Assert.Equal("draft.invalid", stale.Refusal?.Code);
        Assert.Contains("expected member list", stale.Refusal!.Message);
        Assert.Equal(beforeRefusal, JsonSerializer.Serialize(ReadDraftProposal(path, draft)));
        var duplicate = ProposalCommands.ComposeBatch(new(path, version, draft, JsonSerializer.Serialize(new { intents = new[]
        {
            new { verb = "compose-edit-natural-class", intent = new
                { target = "vowels", expectedMembers = new[] { aId.Value, bId.Value }, members = new[] { aId.Value } } },
            new { verb = "compose-edit-natural-class", intent = new
                { target = "V", expectedMembers = new[] { aId.Value, bId.Value }, members = new[] { bId.Value } } },
        } })));
        Assert.Equal("draft.invalid", duplicate.Refusal?.Code);
        Assert.Contains("already edits the same target", duplicate.Refusal!.Message);
        Assert.Equal(beforeRefusal, JsonSerializer.Serialize(ReadDraftProposal(path, draft)));

        var replacement = ProposalCommands.ComposeAuthorNaturalClass(new(path, version, draft,
            JsonSerializer.Serialize(new
            {
                name = "front vowels",
                abbreviation = "F",
                members = new[] { aId.Value },
            })));
        Assert.True(replacement.Succeeded, replacement.Refusal?.Message);
        var replacementCreate = Assert.Single(replacement.Value!.Operations,
            operation => operation.Kind == PhPhonDataNaturalClassesOperationKinds.Create);
        var relink = ProposalCommands.ComposeRelinkNaturalClass(new(path, version, draft,
            JsonSerializer.Serialize(new
            {
                source = "vowels",
                replacement = "front vowels",
                replacementCreationOperation = replacementCreate.OperationId,
                environments = new[]
                {
                    new
                    {
                        target = "AFTER A VO\u0301WEL",
                        expectedLeft = new[] { new { naturalClass = "vowels" } },
                        expectedRight = Array.Empty<object>(),
                    },
                },
                ruleContexts = Array.Empty<string>(),
            })));

        Assert.True(relink.Succeeded, relink.Refusal?.Message);
        var relinkOperationId = Assert.Single(relink.Value!.Operations,
            operation => operation.Kind == PhEnvironmentStringRepresentationOperationKinds.Set).OperationId;
        var relinked = Assert.Single(ReadDraftProposal(path, draft).Operations,
            operation => operation.OperationId.Value == relinkOperationId);
        Assert.Equal(PhEnvironmentStringRepresentationOperationKinds.Set, relinked.Kind);

        using var relinkPayload = JsonDocument.Parse(relinked.After!.Value.GetRawText());
        Assert.Equal("/ [F] _", relinkPayload.RootElement.GetProperty("text").GetString());
        Assert.Equal(environmentOperations.Single(operation =>
            operation.Kind == PhPhonDataEnvironmentsOperationKinds.Create).EntityId!.Value.Value,
            relinked.Target!.Value.Value);

        var beforeDependentRefusal = JsonSerializer.Serialize(ReadDraftProposal(path, draft));
        var dependent = ProposalCommands.ComposeEditNaturalClass(new(path, version, draft,
            JsonSerializer.Serialize(new
            {
                target = "vowels", expectedMembers = new[] { aId.Value, bId.Value }, members = new[] { aId.Value },
            })));
        Assert.Equal("draft.invalid", dependent.Refusal?.Code);
        Assert.Contains("depends on an edit", dependent.Refusal!.Message);
        Assert.Equal(beforeDependentRefusal, JsonSerializer.Serialize(ReadDraftProposal(path, draft)));
        using var scratch = DryRunScratch.Adopt(new SIL.Motif.Host.LcmUtils.FwDataProjectLoader().LoadScratchCache(path),
            "Named relink replay");
        Assert.NotEmpty(ProposalDryRunner.Run(scratch, ReadDraftProposal(path, draft)).ExpectedEffects);
    }

    [Fact]
    public void SoundObjectNameRefusalsListCandidateIdentities()
    {
        using var cache = pristine.NewScratch();
        var path = cache.ProjectId.Path;
        var phonemeOperations = AuthorPhonemeComposer.Build(cache, new("a", ["a"]));
        ExecuteAuthored(cache, phonemeOperations);
        var phonemeId = phonemeOperations.Single(operation =>
            operation.Kind == PhPhonemeSetPhonemesOperationKinds.Create).EntityId!.Value;
        var firstClass = AuthorNaturalClassComposer.Build(cache, new("vowels", "V1", [phonemeId]));
        ExecuteAuthored(cache, firstClass);
        var firstId = firstClass.Single(operation =>
            operation.Kind == PhPhonDataNaturalClassesOperationKinds.Create).EntityId!.Value;
        var secondClass = AuthorNaturalClassComposer.Build(cache, new("vowels", "V2", [phonemeId]));
        ExecuteAuthored(cache, secondClass);
        var secondId = secondClass.Single(operation =>
            operation.Kind == PhPhonDataNaturalClassesOperationKinds.Create).EntityId!.Value;
        var firstEnvironment = AuthorEnvironmentComposer.Build(cache,
            new("after a vowel", [new(NaturalClass: firstId)], []));
        ExecuteAuthored(cache, firstEnvironment);
        var firstEnvironmentId = firstEnvironment.Single(operation =>
            operation.Kind == PhPhonDataEnvironmentsOperationKinds.Create).EntityId!.Value;
        var secondEnvironment = AuthorEnvironmentComposer.Build(cache,
            new("after a vowel", [new(NaturalClass: firstId)], []));
        ExecuteAuthored(cache, secondEnvironment);
        var secondEnvironmentId = secondEnvironment.Single(operation =>
            operation.Kind == PhPhonDataEnvironmentsOperationKinds.Create).EntityId!.Value;
        new SIL.Motif.Host.LcmUtils.FwDataProjectLoader().Save(cache);
        cache.Dispose();

        const string version = "0.1.0";
        const string draft = "ambiguous-natural-class-name";
        Assert.True(ProposalCommands.New(new(path, version, draft, "Refuse uncertain names")).Succeeded);
        CommandOutcome<ComposedOperationsResponse> Compose(string naturalClass) =>
            ProposalCommands.ComposeAuthorEnvironment(new(path, version, draft,
                JsonSerializer.Serialize(new
                {
                    name = "after a vowel",
                    left = new[] { new { naturalClass } },
                    right = Array.Empty<object>(),
                })));

        var ambiguous = Compose("vowels");
        Assert.False(ambiguous.Succeeded);
        Assert.Equal("draft.invalid", ambiguous.Refusal!.Code);
        Assert.Equal(FailureReason.Refused, ambiguous.Refusal.Reason);
        Assert.Contains(firstId.Value, ambiguous.Refusal.Message);
        Assert.Contains(secondId.Value, ambiguous.Refusal.Message);

        var substring = Compose("vow");
        Assert.Equal("draft.invalid", substring.Refusal?.Code);
        Assert.Contains("was not found", substring.Refusal!.Message);
        var duplicateProperty = ProposalCommands.ComposeAuthorEnvironment(new(path, version, draft,
            """{"name":"one","name":"two","left":[{"naturalClass":"V1"}],"right":[]}"""));
        Assert.Equal("draft.invalid", duplicateProperty.Refusal?.Code);
        Assert.Contains("duplicate property 'name'", duplicateProperty.Refusal!.Message);
        var unknown = ProposalCommands.ComposeAuthorEnvironment(new(path, version, draft,
            JsonSerializer.Serialize(new { name = "closed", left = new[] { new { naturalClass = "V1", extra = true } },
                right = Array.Empty<object>() })));
        Assert.Equal("draft.invalid", unknown.Refusal?.Code);

        var missing = Compose("consonants");
        Assert.False(missing.Succeeded);
        Assert.Equal("draft.invalid", missing.Refusal!.Code);
        Assert.Equal(FailureReason.Refused, missing.Refusal.Reason);
        Assert.Contains("vowels", missing.Refusal.Message);
        Assert.Contains(firstId.Value, missing.Refusal.Message);

        var replacement = ProposalCommands.ComposeAuthorNaturalClass(new(path, version, draft,
            JsonSerializer.Serialize(new
            {
                name = "replacement vowels",
                abbreviation = "R",
                members = new[] { phonemeId.Value },
            })));
        Assert.True(replacement.Succeeded, replacement.Refusal?.Message);
        var replacementCreate = Assert.Single(replacement.Value!.Operations,
            operation => operation.Kind == PhPhonDataNaturalClassesOperationKinds.Create);
        CommandOutcome<ComposedOperationsResponse> Relink(string environment) =>
            ProposalCommands.ComposeRelinkNaturalClass(new(path, version, draft,
                JsonSerializer.Serialize(new
                {
                    source = firstId.Value,
                    replacement = replacementCreate.EntityId!,
                    replacementCreationOperation = replacementCreate.OperationId,
                    environments = new[]
                    {
                        new
                        {
                            target = environment,
                            expectedLeft = new[] { new { naturalClass = firstId.Value } },
                            expectedRight = Array.Empty<object>(),
                        },
                    },
                    ruleContexts = Array.Empty<string>(),
                })));

        var ambiguousEnvironment = Relink("after a vowel");
        Assert.False(ambiguousEnvironment.Succeeded);
        Assert.Equal("draft.invalid", ambiguousEnvironment.Refusal!.Code);
        Assert.Equal(FailureReason.Refused, ambiguousEnvironment.Refusal.Reason);
        Assert.Contains(firstEnvironmentId.Value, ambiguousEnvironment.Refusal.Message);
        Assert.Contains(secondEnvironmentId.Value, ambiguousEnvironment.Refusal.Message);

        var missingEnvironment = Relink("after a consonant");
        Assert.False(missingEnvironment.Succeeded);
        Assert.Equal("draft.invalid", missingEnvironment.Refusal!.Code);
        Assert.Equal(FailureReason.Refused, missingEnvironment.Refusal.Reason);
        Assert.Contains("after a vowel", missingEnvironment.Refusal.Message);
        Assert.Contains(firstEnvironmentId.Value, missingEnvironment.Refusal.Message);
    }

    [Theory]
    [InlineData("generic")]
    [InlineData("batch")]
    [InlineData("compose")]
    public void GenericAndBatchCompositionResolveEarlierClassNamesWithoutChangingTheSavedProject(string mode)
    {
        using var cache = pristine.NewScratch();
        var path = cache.ProjectId.Path;
        cache.Dispose();
        const string draft = "named-shared-compose";
        const string version = "0.1.0";
        Assert.True(ProposalCommands.New(new(path, version, draft, "Build named contexts")).Succeeded);
        var phoneme = ProposalCommands.ComposeAuthorPhoneme(new(path, version, draft,
            JsonSerializer.Serialize(new { name = "a", representations = new[] { "a" } })));
        Assert.True(phoneme.Succeeded, phoneme.Refusal?.Message);
        var phonemeId = Assert.Single(phoneme.Value!.Operations,
            operation => operation.Kind == PhPhonemeSetPhonemesOperationKinds.Create).EntityId!;
        (string Verb, object Intent)[] entries =
        [
            ("compose-author-natural-class", new { name = "vówels", abbreviation = "V", members = new[] { phonemeId } }),
            ("compose-author-environment", new
            {
                name = "after a vowel", left = new[] { new { naturalClass = "VO\u0301WELS" } }, right = Array.Empty<object>(),
            }),
            ("compose-author-phonological-rule", new
            {
                name = "insert a", direction = "left-to-right", input = Array.Empty<object>(),
                output = new[] { new { phoneme = phonemeId } },
                left = new[] { new { naturalClass = "v" } }, right = new[] { new { naturalClass = "vówels" } },
            }),
        ];
        if (mode == "compose")
        {
            var composed = ProposalCommands.Compose(new(path, version, draft,
                entries.Select(entry => new ComposeStepRequest(entry.Verb,
                    JsonSerializer.SerializeToElement(entry.Intent))).ToArray()));
            Assert.True(composed.Succeeded, composed.Refusal?.Message);
        }
        else if (mode == "batch")
        {
            var composed = ProposalCommands.ComposeBatch(new(path, version, draft,
                JsonSerializer.Serialize(new { intents = entries.Select(entry => new { verb = entry.Verb, intent = entry.Intent }) })));
            Assert.True(composed.Succeeded, composed.Refusal?.Message);
        }
        else
        {
            foreach (var entry in entries)
            {
                var composed = ProposalCommands.ComposeIntent(new(path, version, draft, entry.Verb,
                    JsonSerializer.Serialize(entry.Intent)));
                Assert.True(composed.Succeeded, composed.Refusal?.Message);
            }
        }
        var proposal = ReadDraftProposal(path, draft);
        var classId = Assert.Single(proposal.Operations,
            operation => operation.Kind == PhPhonDataNaturalClassesOperationKinds.Create).EntityId!.Value;
        var environment = Assert.Single(proposal.Operations,
            operation => operation.Kind == PhEnvironmentStringRepresentationOperationKinds.Set);
        Assert.Equal(classId.Value, environment.After!.Value.GetProperty("left")[0].GetProperty("naturalClass").GetString());
        Assert.Contains(proposal.Operations, operation => operation.Kind == PhSimpleContextNCFeatureStructureOperationKinds.SetFeatureStructure);
        using var saved = new SIL.Motif.Host.LcmUtils.FwDataProjectLoader().LoadScratchCache(path);
        Assert.Empty(saved.LangProject.PhonologicalDataOA.NaturalClassesOS);
        using var scratch = DryRunScratch.Adopt(new SIL.Motif.Host.LcmUtils.FwDataProjectLoader().LoadScratchCache(path),
            "Named Draft replay");
        Assert.NotEmpty(ProposalDryRunner.Run(scratch, proposal).ExpectedEffects);
    }

    [Theory]
    [InlineData("")]
    [InlineData("environment/")]
    public void EditAllomorphConditionDependsOnAnEnvironmentCreatedEarlierInTheDraft(string prefix)
    {
        // The prefix is optional (design rule 8): a prefixed spelling of the created id must earn the same edge.
        using var cache = pristine.NewScratch();
        var path = cache.ProjectId.Path;
        var entry = cache.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(pristine.Seed.FirstEntryId);
        var target = CanonicalId.FromGuid(entry.LexemeFormOA!.Guid).Value;
        var vernacularWs = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultVernWs);
        var analysisWs = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultAnalWs);
        cache.Dispose();

        const string version = "0.1.0";
        var draft = "condition-new-environment" + (prefix.Length == 0 ? "" : "-prefixed");
        Assert.True(ProposalCommands.New(new(path, version, draft, "Restrict one stem allomorph")).Succeeded);
        Assert.True(DraftRationale.Write(path, version, draft,
            "The new typed environment narrows this allomorph without changing its shared users.").Succeeded);
        var phoneme = ProposalCommands.ComposeAuthorPhoneme(new(path, version, draft,
            JsonSerializer.Serialize(new { name = "a\u0301", representations = new[] { "á" } })));
        Assert.True(phoneme.Succeeded, phoneme.Refusal?.Message);
        AssertCreatedObjectsMatch(phoneme.Value!);
        var phonemeCreate = Assert.Single(phoneme.Value!.Operations,
            operation => operation.Kind.EndsWith("/createPhonemes", StringComparison.Ordinal));

        var environment = ProposalCommands.ComposeAuthorEnvironment(new(path, version, draft,
            JsonSerializer.Serialize(new
            {
                name = "after a\u0301",
                left = new[] { new { phoneme = phonemeCreate.EntityId! } },
                right = Array.Empty<object>(),
            })));
        Assert.True(environment.Succeeded, environment.Refusal?.Message);
        AssertCreatedObjectsMatch(environment.Value!);
        var environmentCreate = Assert.Single(environment.Value!.Operations,
            operation => operation.Kind.EndsWith("/createEnvironments", StringComparison.Ordinal));

        var condition = ProposalCommands.ComposeEditAllomorphCondition(new(path, version, draft,
            JsonSerializer.Serialize(new
            {
                target,
                field = "phoneEnv",
                expectedEnvironments = Array.Empty<string>(),
                environments = new[] { prefix + environmentCreate.EntityId! },
            })));
        Assert.True(condition.Succeeded, condition.Refusal?.Message);
        AssertCreatedObjectsMatch(condition.Value!);
        Assert.Empty(condition.Value!.CreatedObjects);
        var attachment = Assert.Single(condition.Value!.Operations,
            operation => operation.Kind == MoStemAllomorphPhoneEnvOperationKinds.AddRefPhoneEnv);

        var finalized = ProposalCommands.Finalize(new(path, version, draft));
        Assert.True(finalized.Succeeded, finalized.Refusal?.Message);
        var saved = ProjectStoreCommand.Run<Proposal>(path, version, (database, _) =>
            CommandOutcome<Proposal>.Success(ProposalJsonParser.Parse(new ProposalRepository(database)
                .Get(CanonicalId.Parse(finalized.Value!.ProposalId)).ProposalJson!)));
        Assert.True(saved.Succeeded, saved.Refusal?.Message);
        var proposal = saved.Value!;
        var environmentCreateOperation = proposal.Operations.Single(operation =>
            operation.OperationId.Value == environmentCreate.OperationId);
        var attachmentOperation = proposal.Operations.Single(operation =>
            operation.OperationId.Value == attachment.OperationId);
        Assert.Contains(environmentCreateOperation.OperationId.Value,
            attachmentOperation.DependsOn.Select(dependency => dependency.OperationId.Value));
        var nameWrite = Assert.Single(proposal.Operations,
            operation => operation.Kind == PhEnvironmentNameOperationKinds.SetName);
        using (var namePayload = JsonDocument.Parse(nameWrite.After!.Value.GetRawText()))
        {
            Assert.Equal(analysisWs, namePayload.RootElement.GetProperty("ws").GetString());
            Assert.Equal("after a\u0301", namePayload.RootElement.GetProperty("text").GetString());
        }
        var stringWrite = Assert.Single(proposal.Operations,
            operation => operation.Kind == PhEnvironmentStringRepresentationOperationKinds.Set);
        using (var stringPayload = JsonDocument.Parse(stringWrite.After!.Value.GetRawText()))
        {
            Assert.Equal(vernacularWs, stringPayload.RootElement.GetProperty("ws").GetString());
            Assert.Equal("/ a\u0301 _", stringPayload.RootElement.GetProperty("text").GetString());
        }
        var reversed = new Proposal(proposal.ContractVersions, proposal.ProposalId, proposal.Requires,
            proposal.Operations.Reverse().ToArray(), proposal.Extensions);
        using var scratch = DryRunScratch.Adopt(
            new SIL.Motif.Host.LcmUtils.FwDataProjectLoader().LoadScratchCache(path), "reversed environment dependency");
        Assert.NotEmpty(ProposalDryRunner.Run(scratch, reversed).ExpectedEffects);
    }

    [Fact]
    public void EditNaturalClassComposesIdentityChangesAndListsItsUsers()
    {
        string path;
        CanonicalId aId;
        CanonicalId bId;
        CanonicalId classId;
        CanonicalId environmentId;
        using (var cache = pristine.NewScratch())
        {
            path = cache.ProjectId.Path;
            var aOperations = AuthorPhonemeComposer.Build(cache, new("a", ["a"]));
            ExecuteAuthored(cache, aOperations);
            aId = aOperations.Single(operation => operation.Kind == PhPhonemeSetPhonemesOperationKinds.Create)
                .EntityId!.Value;
            var bOperations = AuthorPhonemeComposer.Build(cache, new("b", ["b"]));
            ExecuteAuthored(cache, bOperations);
            bId = bOperations.Single(operation => operation.Kind == PhPhonemeSetPhonemesOperationKinds.Create)
                .EntityId!.Value;
            var classOperations = AuthorNaturalClassComposer.Build(cache, new("vowels", "V", [aId, bId]));
            ExecuteAuthored(cache, classOperations);
            classId = classOperations.Single(operation =>
                operation.Kind == PhPhonDataNaturalClassesOperationKinds.Create).EntityId!.Value;
            var environmentOperations = AuthorEnvironmentComposer.Build(cache,
                new("after a vowel", [new(NaturalClass: classId)], []));
            ExecuteAuthored(cache, environmentOperations);
            environmentId = environmentOperations.Single(operation =>
                operation.Kind == PhPhonDataEnvironmentsOperationKinds.Create).EntityId!.Value;
            new SIL.Motif.Host.LcmUtils.FwDataProjectLoader().Save(cache);
        }

        const string version = "0.1.0";
        const string draft = "edit-natural-class";
        Assert.True(ProposalCommands.New(new(path, version, draft, "Correct a segment class")).Succeeded);
        Assert.True(ProposalCommands.Label(new(path, version, draft, "Remove a leaked class member")).Succeeded);
        Assert.True(DraftRationale.Write(path, version, draft,
            "Keep the remaining environment meaning and verify the membership edit in Dry Run.").Succeeded);

        var edit = ProposalCommands.ComposeEditNaturalClass(new(path, version, draft,
            JsonSerializer.Serialize(new
            {
                target = classId.Value,
                expectedMembers = new[] { aId.Value, bId.Value },
                members = new[] { aId.Value },
            })));

        Assert.True(edit.Succeeded, edit.Refusal?.Message);
        AssertCreatedObjectsMatch(edit.Value!);
        Assert.Empty(edit.Value!.CreatedObjects);
        Assert.Contains(edit.Value!.Operations, operation => operation.Kind == PhNCSegmentsSegmentsOperationKinds.RemoveRefSegments);
        Assert.Contains(edit.Value.RelatedObjects!, user => user.Kind == "environment" && user.Id == environmentId.Value);
        Assert.Contains(edit.Value.RelatedObjects!, user => user.Name == "after a vowel");

        var finalized = ProposalCommands.Finalize(new(path, version, draft));
        Assert.True(finalized.Succeeded, finalized.Refusal?.Message);
        var saved = ProjectStoreCommand.Run<Proposal>(path, version, (database, _) =>
            CommandOutcome<Proposal>.Success(ProposalJsonParser.Parse(new ProposalRepository(database)
                .Get(CanonicalId.Parse(finalized.Value!.ProposalId)).ProposalJson!)));
        Assert.True(saved.Succeeded, saved.Refusal?.Message);
        var editOperation = Assert.Single(saved.Value!.Operations,
            operation => operation.Kind == PhNCSegmentsSegmentsOperationKinds.RemoveRefSegments);
        Assert.Empty(editOperation.DependsOn);
    }

    [Fact]
    public void RelinkNaturalClassRequiresAnEarlierCreatorAndSurvivesReversedOperationOrder()
    {
        string path;
        CanonicalId aId;
        CanonicalId bId;
        CanonicalId sourceId;
        CanonicalId environmentId;
        CanonicalId ruleContextId;
        using (var cache = pristine.NewScratch())
        {
            path = cache.ProjectId.Path;
            var aOperations = AuthorPhonemeComposer.Build(cache, new("a", ["a"]));
            ExecuteAuthored(cache, aOperations);
            aId = aOperations.Single(operation => operation.Kind == PhPhonemeSetPhonemesOperationKinds.Create)
                .EntityId!.Value;
            var bOperations = AuthorPhonemeComposer.Build(cache, new("b", ["b"]));
            ExecuteAuthored(cache, bOperations);
            bId = bOperations.Single(operation => operation.Kind == PhPhonemeSetPhonemesOperationKinds.Create)
                .EntityId!.Value;
            var sourceOperations = AuthorNaturalClassComposer.Build(cache,
                new("old class", "OLD", [aId, bId]));
            ExecuteAuthored(cache, sourceOperations);
            sourceId = sourceOperations.Single(operation =>
                operation.Kind == PhPhonDataNaturalClassesOperationKinds.Create).EntityId!.Value;
            var environmentOperations = AuthorEnvironmentComposer.Build(cache,
                new("selected environment", [new(NaturalClass: sourceId)], []));
            ExecuteAuthored(cache, environmentOperations);
            environmentId = environmentOperations.Single(operation =>
                operation.Kind == PhPhonDataEnvironmentsOperationKinds.Create).EntityId!.Value;
            var ruleOperations = AuthorPhonologicalRuleComposer.Build(cache,
                new("class context", PhonologicalRuleDirection.Simultaneous,
                    [new(Phoneme: aId)], [new(Phoneme: bId)],
                    [new(NaturalClass: sourceId)], [new(Phoneme: bId)]));
            ExecuteAuthored(cache, ruleOperations);
            ruleContextId = ruleOperations.Single(operation =>
                operation.Kind == PhSegRuleRHSLeftContextOperationKinds.Create).EntityId!.Value;
            new SIL.Motif.Host.LcmUtils.FwDataProjectLoader().Save(cache);
        }

        const string version = "0.1.0";
        const string draft = "relink-natural-class";
        Assert.True(ProposalCommands.New(new(path, version, draft, "Relink selected class users")).Succeeded);
        Assert.True(ProposalCommands.Label(new(path, version, draft, "Keep shared meanings distinct")).Succeeded);
        Assert.True(DraftRationale.Write(path, version, draft,
            "Move only the selected rule context and environment to the corrected class.").Succeeded);
        var replacement = ProposalCommands.ComposeAuthorNaturalClass(new(path, version, draft,
            JsonSerializer.Serialize(new { name = "corrected class", abbreviation = "NEW", members = new[] { aId.Value } })));
        Assert.True(replacement.Succeeded, replacement.Refusal?.Message);
        AssertCreatedObjectsMatch(replacement.Value!);
        var replacementCreate = replacement.Value!.Operations.Single(operation =>
            operation.Kind == PhPhonDataNaturalClassesOperationKinds.Create);

        var relink = ProposalCommands.ComposeRelinkNaturalClass(new(path, version, draft,
            JsonSerializer.Serialize(new
            {
                source = sourceId.Value,
                replacement = replacementCreate.EntityId!,
                replacementCreationOperation = replacementCreate.OperationId,
                environments = new[]
                {
                    new
                    {
                        target = environmentId.Value,
                        expectedLeft = new[] { new { naturalClass = sourceId.Value } },
                        expectedRight = Array.Empty<object>(),
                    },
                },
                ruleContexts = new[] { ruleContextId.Value },
            })));

        Assert.True(relink.Succeeded, relink.Refusal?.Message);
        AssertCreatedObjectsMatch(relink.Value!);
        Assert.Empty(relink.Value!.CreatedObjects);
        Assert.Equal(2, relink.Value!.Operations.Count);
        Assert.Contains(relink.Value.RelatedObjects!, user => user.Kind == "environment");
        Assert.Contains(relink.Value.RelatedObjects!, user => user.Kind == "phonological-rule");
        var finalized = ProposalCommands.Finalize(new(path, version, draft));
        Assert.True(finalized.Succeeded, finalized.Refusal?.Message);
        var saved = ProjectStoreCommand.Run<Proposal>(path, version, (database, _) =>
            CommandOutcome<Proposal>.Success(ProposalJsonParser.Parse(new ProposalRepository(database)
                .Get(CanonicalId.Parse(finalized.Value!.ProposalId)).ProposalJson!)));
        Assert.True(saved.Succeeded, saved.Refusal?.Message);
        var relinkIds = relink.Value.Operations.Select(operation => operation.OperationId)
            .ToHashSet(StringComparer.Ordinal);
        var relinkOperations = saved.Value!.Operations.Where(operation => relinkIds.Contains(operation.OperationId.Value)).ToArray();
        Assert.Equal(2, relinkOperations.Length);
        Assert.All(relinkOperations,
            operation => Assert.Contains(operation.DependsOn,
                dependency => dependency.OperationId.Value == replacementCreate.OperationId));

        var reversed = new Proposal(saved.Value.ContractVersions, saved.Value.ProposalId,
            saved.Value.Requires, saved.Value.Operations.Reverse().ToArray(), saved.Value.Extensions);
        using var scratch = DryRunScratch.Adopt(
            new SIL.Motif.Host.LcmUtils.FwDataProjectLoader().LoadScratchCache(path), "reversed natural-class relink");
        Assert.NotEmpty(ProposalDryRunner.Run(scratch, reversed).ExpectedEffects);
    }

    [Fact]
    public void AuthorAffixSlotCommandStagesCategoryOwnedCreation()
    {
        using var cache = pristine.NewScratch();
        var path = cache.ProjectId.Path;
        var categoryId = CanonicalId.FromGuid(pristine.Seed.PartOfSpeechId).Value;
        cache.Dispose();

        const string version = "0.1.0";
        const string draft = "new-affix-slot";
        Assert.True(ProposalCommands.New(new(path, version, draft, "Add a new inflectional slot")).Succeeded);

        var intent = JsonSerializer.Serialize(new
        {
            category = categoryId,
            name = "person number",
            ws = "en",
            optional = false,
        });
        var composed = ProposalCommands.ComposeAuthorAffixSlot(
            new ComposeAuthorAffixSlotRequest(path, version, draft, intent));

        Assert.True(composed.Succeeded, composed.Refusal?.Message);
        AssertCreatedObjectsMatch(composed.Value!);
        Assert.Contains(composed.Value!.Operations,
            operation => operation.Kind.EndsWith("/createAffixSlots", StringComparison.Ordinal));
    }

    [Fact]
    public void EditAffixTemplateAddsASlotMintedEarlierInTheSameDraftWithDependencies()
    {
        using var cache = pristine.NewScratch();
        var path = cache.ProjectId.Path;
        var categoryId = CanonicalId.FromGuid(pristine.Seed.PartOfSpeechId).Value;
        cache.Dispose();

        const string version = "0.1.0";
        const string draft = "edit-affix-template";
        Assert.True(ProposalCommands.New(new(path, version, draft, "Extend a template")).Succeeded);
        string Slot(string name)
        {
            var result = ProposalCommands.ComposeAuthorAffixSlot(new(path, version, draft,
                JsonSerializer.Serialize(new { category = categoryId, name, ws = "en", optional = false })));
            Assert.True(result.Succeeded, result.Refusal?.Message);
            return result.Value!.Operations.Single(op => op.Kind.EndsWith("/createAffixSlots", StringComparison.Ordinal))
                .EntityId!;
        }
        var template = ProposalCommands.ComposeAuthorAffixTemplate(new(path, version, draft,
            JsonSerializer.Serialize(new
            {
                category = categoryId, name = "indicative", ws = "en", prefixSlots = Array.Empty<string>(),
                suffixSlots = Array.Empty<string>(), final = false,
            })));
        Assert.True(template.Succeeded, template.Refusal?.Message);
        var templateId = template.Value!.Operations
            .Single(op => op.Kind.EndsWith("/createAffixTemplates", StringComparison.Ordinal)).EntityId!;
        var second = Slot("POST");

        var edit = ProposalCommands.ComposeEditAffixTemplate(new(path, version, draft,
            JsonSerializer.Serialize(new
            {
                target = templateId, expectedPrefixSlots = Array.Empty<string>(), prefixSlots = Array.Empty<string>(),
                expectedSuffixSlots = Array.Empty<string>(), suffixSlots = new[] { second },
            })));
        Assert.True(edit.Succeeded, edit.Refusal?.Message);
        var add = Assert.Single(edit.Value!.Operations);
        Assert.EndsWith("/addRefSuffixSlots", add.Kind, StringComparison.Ordinal);
    }

    [Fact]
    public void AuthorAffixTemplateReferencesASlotCreatedEarlierInTheSameDraft()
    {
        using var cache = pristine.NewScratch();
        var path = cache.ProjectId.Path;
        var categoryId = CanonicalId.FromGuid(pristine.Seed.PartOfSpeechId).Value;
        cache.Dispose();

        const string version = "0.1.0";
        const string draft = "new-affix-template";
        Assert.True(ProposalCommands.New(new(path, version, draft, "Add an alternative affix template")).Succeeded);
        var slot = ProposalCommands.ComposeAuthorAffixSlot(new(path, version, draft,
            JsonSerializer.Serialize(new
            {
                category = categoryId,
                name = "plural suffix",
                ws = "en",
                optional = false,
            })));
        Assert.True(slot.Succeeded, slot.Refusal?.Message);
        AssertCreatedObjectsMatch(slot.Value!);
        var slotCreate = slot.Value!.Operations.Single(operation =>
            operation.Kind.EndsWith("/createAffixSlots", StringComparison.Ordinal));

        var template = ProposalCommands.ComposeAuthorAffixTemplate(new(path, version, draft,
            JsonSerializer.Serialize(new
            {
                category = categoryId,
                name = "plural branch",
                ws = "en",
                prefixSlots = Array.Empty<string>(),
                suffixSlots = new[] { slotCreate.EntityId },
                final = false,
            })));
        Assert.True(template.Succeeded, template.Refusal?.Message);
        AssertCreatedObjectsMatch(template.Value!);

        Assert.True(ProposalCommands.Label(new(path, version, draft, "Add alternative template")).Succeeded);
        Assert.True(DraftRationale.Write(path, version, draft,
            "The plural branch requires the plural suffix slot.").Succeeded);
        var finalized = ProposalCommands.Finalize(new(path, version, draft));
        Assert.True(finalized.Succeeded, finalized.Refusal?.Message);
        var loaded = ProjectStoreCommand.Run<Proposal>(path, version, (database, _) =>
            CommandOutcome<Proposal>.Success(ProposalJsonParser.Parse(new ProposalRepository(database)
                .Get(CanonicalId.Parse(finalized.Value!.ProposalId)).ProposalJson!)));
        Assert.True(loaded.Succeeded, loaded.Refusal?.Message);
        var proposal = loaded.Value!;
        var templateCreate = Assert.Single(proposal.Operations,
            operation => operation.Kind.EndsWith("/createAffixTemplates", StringComparison.Ordinal));
        var membership = Assert.Single(proposal.Operations,
            operation => operation.Kind.EndsWith("/addRefSuffixSlots", StringComparison.Ordinal));
        Assert.Contains(templateCreate.OperationId, membership.DependsOn.Select(dependency => dependency.OperationId));
        Assert.Contains(CanonicalId.Parse(slotCreate.OperationId),
            membership.DependsOn.Select(dependency => dependency.OperationId));
        var final = Assert.Single(proposal.Operations,
            operation => operation.Kind == "grammar/moInflAffixTemplate/setFinal");
        Assert.Contains(templateCreate.OperationId, final.DependsOn.Select(dependency => dependency.OperationId));

        using var live = new SIL.Motif.Host.LcmUtils.FwDataProjectLoader().LoadScratchCache(path);
        var category = live.ServiceLocator.GetInstance<ICmObjectRepository>().GetObject(
            CanonicalId.Parse(categoryId).ToGuid()) as IPartOfSpeech;
        Assert.NotNull(category);
        Assert.DoesNotContain(category!.AffixSlotsOC, existing => existing.Guid == CanonicalId.Parse(slotCreate.EntityId!).ToGuid());
        Assert.DoesNotContain(category.AffixTemplatesOS,
            existing => existing.Guid == templateCreate.EntityId!.Value.ToGuid());

        using var scratch = DryRunScratch.Adopt(
            new SIL.Motif.Host.LcmUtils.FwDataProjectLoader().LoadScratchCache(path), "Template authoring draft replay");
        Assert.NotEmpty(ProposalDryRunner.Run(scratch, proposal).ExpectedEffects);
    }

    private static Proposal ReadDraftProposal(string path, string draft)
    {
        var loaded = ProjectStoreCommand.Run(path, "0.1.0", (database, _) =>
            CommandOutcome<Proposal>.Success(ProposalJsonParser.Parse(ProposalCommands.BuildProposalJson(
                ProposalCommands.DeserializeDraft(new ProposalRepository(database).GetDraft(draft).ProposalJson!)))));
        Assert.True(loaded.Succeeded, loaded.Refusal?.Message);
        return loaded.Value!;
    }

    private static void ExecuteAuthored(LcmCache cache, IReadOnlyList<OperationEnvelope> operations) =>
        SIL.LCModel.Infrastructure.NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            foreach (var operation in operations)
                OperationHandlerRegistry.Resolve(operation.Kind, "sound-system test setup")
                    .ApplyAndCaptureEffect(cache, operation, []);
        });

    private static void AssertCreatedObjectsMatch(ComposedOperationsResponse response)
    {
        var expected = response.Operations.Where(operation => operation.EntityId is not null)
            .Select(operation => new CreatedObject(operation.EntityId!, operation.Kind)).ToArray();
        Assert.Equal(expected, response.CreatedObjects);
    }
}
