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
        var created = structure.Value!.Operations.Single(o => o.EntityId is not null);
        var selected = ProposalCommands.ComposeAuthorFeatureValue(new(path, version, draft,
            JsonSerializer.Serialize(new { featStruc = created.EntityId, feature = featureId, value = valueId })));
        Assert.True(selected.Succeeded, selected.Refusal?.Message);
        Assert.Contains(selected.Value!.Operations, o => o.Kind.EndsWith("/setValue", StringComparison.Ordinal));
        var duplicate = ProposalCommands.ComposeAuthorFeatureValue(new(path, version, draft,
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
        Assert.True(ProposalCommands.Comment(new(path, version, draft, "Synthetic vowel context.")).Succeeded);
        var phoneme = ProposalCommands.ComposeAuthorPhoneme(new(path, version, draft,
            JsonSerializer.Serialize(new { name = "a", representations = new[] { "a" } })));
        Assert.True(phoneme.Succeeded, phoneme.Refusal?.Message);
        var createdPhoneme = phoneme.Value!.Operations.Single(o => o.Kind.EndsWith("/createPhonemes", StringComparison.Ordinal));
        var second = ProposalCommands.ComposeAuthorPhoneme(new(path, version, draft,
            JsonSerializer.Serialize(new { name = "i", representations = new[] { "i" } })));
        Assert.True(second.Succeeded, second.Refusal?.Message);
        var naturalClass = ProposalCommands.ComposeAuthorNaturalClass(new(path, version, draft,
            JsonSerializer.Serialize(new { name = "vowels", abbreviation = "V", members = new[] { createdPhoneme.EntityId } })));
        Assert.True(naturalClass.Succeeded, naturalClass.Refusal?.Message);
        var createdClass = naturalClass.Value!.Operations.Single(o => o.Kind.EndsWith("/createNaturalClasses", StringComparison.Ordinal));
        var environment = ProposalCommands.ComposeAuthorEnvironment(new(path, version, draft,
            JsonSerializer.Serialize(new { name = "after a vowel", left = new[] { new { naturalClass = createdClass.EntityId } }, right = Array.Empty<object>() })));
        Assert.True(environment.Succeeded, environment.Refusal?.Message);
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
    public void EditAllomorphConditionDependsOnAnEnvironmentCreatedEarlierInTheDraft()
    {
        using var cache = pristine.NewScratch();
        var path = cache.ProjectId.Path;
        var entry = cache.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(pristine.Seed.FirstEntryId);
        var target = CanonicalId.FromGuid(entry.LexemeFormOA!.Guid).Value;
        var vernacularWs = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultVernWs);
        var analysisWs = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultAnalWs);
        cache.Dispose();

        const string version = "0.1.0";
        const string draft = "condition-new-environment";
        Assert.True(ProposalCommands.New(new(path, version, draft, "Restrict one stem allomorph")).Succeeded);
        Assert.True(ProposalCommands.Comment(new(path, version, draft,
            "The new typed environment narrows this allomorph without changing its shared users.")).Succeeded);
        var phoneme = ProposalCommands.ComposeAuthorPhoneme(new(path, version, draft,
            JsonSerializer.Serialize(new { name = "a\u0301", representations = new[] { "á" } })));
        Assert.True(phoneme.Succeeded, phoneme.Refusal?.Message);
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
        var environmentCreate = Assert.Single(environment.Value!.Operations,
            operation => operation.Kind.EndsWith("/createEnvironments", StringComparison.Ordinal));

        var condition = ProposalCommands.ComposeEditAllomorphCondition(new(path, version, draft,
            JsonSerializer.Serialize(new
            {
                target,
                field = "phoneEnv",
                expectedEnvironments = Array.Empty<string>(),
                environments = new[] { environmentCreate.EntityId! },
            })));
        Assert.True(condition.Succeeded, condition.Refusal?.Message);
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
        Assert.True(ProposalCommands.Comment(new(path, version, draft,
            "Keep the remaining environment meaning and verify the membership edit in Dry Run.")).Succeeded);

        var edit = ProposalCommands.ComposeEditNaturalClass(new(path, version, draft,
            JsonSerializer.Serialize(new
            {
                target = classId.Value,
                expectedMembers = new[] { aId.Value, bId.Value },
                members = new[] { aId.Value },
            })));

        Assert.True(edit.Succeeded, edit.Refusal?.Message);
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
        Assert.True(ProposalCommands.Comment(new(path, version, draft,
            "Move only the selected rule context and environment to the corrected class.")).Succeeded);
        var replacement = ProposalCommands.ComposeAuthorNaturalClass(new(path, version, draft,
            JsonSerializer.Serialize(new { name = "corrected class", abbreviation = "NEW", members = new[] { aId.Value } })));
        Assert.True(replacement.Succeeded, replacement.Refusal?.Message);
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
        Assert.Contains(composed.Value!.Operations,
            operation => operation.Kind.EndsWith("/createAffixSlots", StringComparison.Ordinal));
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

        Assert.True(ProposalCommands.Label(new(path, version, draft, "Add alternative template")).Succeeded);
        Assert.True(ProposalCommands.Comment(new(path, version, draft,
            "The plural branch requires the plural suffix slot.")).Succeeded);
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

    private static void ExecuteAuthored(LcmCache cache, IReadOnlyList<OperationEnvelope> operations) =>
        SIL.LCModel.Infrastructure.NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            foreach (var operation in operations)
                OperationHandlerRegistry.Resolve(operation.Kind, "sound-system test setup")
                    .ApplyAndCaptureEffect(cache, operation, []);
        });
}
