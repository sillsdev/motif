using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Runner.DryRun;
using SIL.Motif.Runner.Apply;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[Collection(LcmCacheParallelCollections.Group2)]
public sealed class SenseDeletionAndStemPartOfSpeechDraftTests(PristineProjectFixture pristine)
{
    private const string Version = "0.1.0";
    private const string DeleteVerb = "compose-delete-empty-sense";
    private const string PosVerb = "compose-set-stem-part-of-speech";

    [Fact]
    public void DeletionIsOfferedToAnAgentAndPartOfSpeechRemainsCliFirst()
    {
        foreach (var verb in new[] { DeleteVerb, PosVerb })
        {
            var descriptor = Assert.Single(ProposalCommands.ComposerDescriptors, composer => composer.Verb == verb);
            Assert.Equal(verb == DeleteVerb, descriptor.IsAgentTool);
            Assert.Contains(verb, ProposalCommands.DraftComposers.Keys);
        }
    }

    [Fact]
    public void OneBatchOfManyEntriesBecomesOneDraftOfManyOperationsThatFinalizesWithARationale()
    {
        var (path, seeds, withPos) = Open(variants: 5, stems: 3);
        const string draft = "ceb-d001";
        Assert.True(ProposalCommands.New(new(path, Version, draft, "Apply D-CEB-001")).Succeeded);
        var posId = CanonicalId.FromGuid(pristine.Seed.PartOfSpeechId).Value;
        var intents = seeds.Select(seed => (object)new
            {
                verb = DeleteVerb,
                intent = new { target = Id(seed.SenseId), entry = Id(seed.EntryId) },
                citation = new { source = "D-CEB-001", locator = "classification.csv" },
            })
            .Concat(withPos.Select(seed => new
            {
                verb = PosVerb,
                intent = new { target = Id(seed.MsaId), expectedPartOfSpeech = (string?)null, partOfSpeech = posId },
            }))
            .ToArray();

        var batch = ProposalCommands.ComposeBatch(new(path, Version, draft, Json(new { intents })));

        Assert.True(batch.Succeeded, batch.Refusal?.Message);
        Assert.Equal(8, batch.Value!.Intents.Count);
        Assert.Equal(8, batch.Value.OperationCount);
        var operations = ReadDraft(path, draft).Operations;
        Assert.Equal(5, operations.Count(op => op.Kind == LexSenseDeleteOperationKinds.DeleteSense));
        Assert.Equal(3, operations.Count(op => op.Kind == MoStemMsaPartOfSpeechOperationKinds.SetPartOfSpeech));
        var deletion = operations.First(op => op.Kind == LexSenseDeleteOperationKinds.DeleteSense);
        Assert.Equal(Id(seeds[0].MsaId), deletion.After!.Value.GetProperty("msa").GetString());
        Assert.Equal("D-CEB-001", OperationCitation.FromProvenance(deletion.Provenance)!.Source);

        var unfinished = ProposalCommands.Finalize(new(path, Version, draft));
        Assert.False(unfinished.Succeeded);
        Assert.Equal("proposal.rationale-empty", unfinished.Refusal!.Code);
        Assert.True(DraftRationale.Write(path, Version, draft,
            "Remove each variant's empty sense and its POS-less stem analysis so it takes its main entry's path.").Succeeded);
        var finalized = ProposalCommands.Finalize(new(path, Version, draft));
        Assert.True(finalized.Succeeded, finalized.Refusal?.Message);

        var loaded = ProjectStoreCommand.Run(path, Version, (database, _) =>
            CommandOutcome<Proposal>.Success(ProposalJsonParser.Parse(new ProposalRepository(database)
                .Get(CanonicalId.Parse(finalized.Value!.ProposalId)).ProposalJson!)));
        Assert.True(loaded.Succeeded, loaded.Refusal?.Message);
        using var scratch = DryRunScratch.Adopt(new FwDataProjectLoader().LoadScratchCache(path), "ceb preview");
        var dryRun = ProposalDryRunner.Run(scratch, loaded.Value!);
        Assert.Equal(5 * 2 + 3, dryRun.ExpectedEffects.Count);
    }

    [Fact]
    public void ANonEmptySenseAWrongEntryAndAStalePartOfSpeechAreRefusedWithTheDraftUnchanged()
    {
        var (path, seeds, withPos) = Open(variants: 1, stems: 1);
        string glossedSense, glossedEntry;
        using (var cache = new FwDataProjectLoader().LoadScratchCache(path))
        {
            var glossed = EmptySenseSeed.Add(cache, "glossed", gloss: "has content");
            new FwDataProjectLoader().Save(cache);
            glossedSense = Id(glossed.SenseId);
            glossedEntry = Id(glossed.EntryId);
        }
        const string draft = "refusals";
        Assert.True(ProposalCommands.New(new(path, Version, draft, "Refusals")).Succeeded);
        var before = ReadDraft(path, draft).Operations.Count;
        var posId = CanonicalId.FromGuid(pristine.Seed.PartOfSpeechId).Value;

        var nonEmpty = ProposalCommands.ComposeIntent(new(path, Version, draft, DeleteVerb,
            Json(new { target = glossedSense, entry = glossedEntry })));
        Assert.False(nonEmpty.Succeeded);
        Assert.Contains("not empty", nonEmpty.Refusal!.Message, StringComparison.Ordinal);

        var wrongEntry = ProposalCommands.ComposeIntent(new(path, Version, draft, DeleteVerb,
            Json(new { target = Id(seeds[0].SenseId), entry = Id(pristine.Seed.FirstEntryId) })));
        Assert.False(wrongEntry.Succeeded);
        Assert.Contains("different entry", wrongEntry.Refusal!.Message, StringComparison.Ordinal);

        var extra = ProposalCommands.ComposeIntent(new(path, Version, draft, DeleteVerb,
            Json(new { target = Id(seeds[0].SenseId), entry = Id(seeds[0].EntryId), force = true })));
        Assert.False(extra.Succeeded);

        var stale = ProposalCommands.ComposeIntent(new(path, Version, draft, PosVerb,
            Json(new { target = Id(withPos[0].MsaId), expectedPartOfSpeech = posId, partOfSpeech = posId })));
        Assert.False(stale.Succeeded);
        Assert.Contains("differs from the expected", stale.Refusal!.Message, StringComparison.Ordinal);

        Assert.Equal(before, ReadDraft(path, draft).Operations.Count);
    }

    [Fact]
    public void LinkedVariantWithPartOfSpeechCanBeComposedAndUnlinkedVariantIsRefused()
    {
        using var cache = pristine.NewScratch();
        var path = cache.ProjectId.Path;
        var linked = EmptySenseSeed.Add(cache, "linked");
        var unlinked = EmptySenseSeed.Add(cache, "unlinked");
        var extra = EmptySenseSeed.Add(cache, "extra");
        SIL.LCModel.Infrastructure.NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var entries = cache.ServiceLocator.GetInstance<ILexEntryRepository>();
            var main = entries.GetObject(pristine.Seed.FirstEntryId);
            foreach (var seed in new[] { linked, unlinked, extra })
            {
                var entry = entries.GetObject(seed.EntryId);
                ((IMoStemMsa)entry.MorphoSyntaxAnalysesOC.Single()).PartOfSpeechRA =
                    cache.ServiceLocator.GetInstance<IPartOfSpeechRepository>().GetObject(pristine.Seed.PartOfSpeechId);
                var reference = cache.ServiceLocator.GetInstance<ILexEntryRefFactory>().Create();
                entry.EntryRefsOS.Add(reference);
                reference.RefType = LexEntryRefTags.krtVariant;
                if (seed != unlinked) reference.ComponentLexemesRS.Add(main);
                if (seed == extra)
                {
                    var category = ((IMoStemMsa)entry.MorphoSyntaxAnalysesOC.Single()).PartOfSpeechRA;
                    var cls = cache.ServiceLocator.GetInstance<IMoInflClassFactory>().Create();
                    category.InflectionClassesOC.Add(cls);
                    ((IMoStemMsa)entry.MorphoSyntaxAnalysesOC.Single()).InflectionClassRA = cls;
                }
            }
        });
        new FwDataProjectLoader().Save(cache);
        Assert.True(ProposalCommands.New(new(path, Version, "variants", "Variants")).Succeeded);
        var refused = ProposalCommands.ComposeIntent(new(path, Version, "variants", DeleteVerb,
            Json(new { target = Id(unlinked.SenseId), entry = Id(unlinked.EntryId) })));
        Assert.False(refused.Succeeded);
        Assert.Equal(RefusalCodes.VariantComponentRequired, refused.Refusal!.Code);
        Assert.Contains("component target", refused.Refusal.Message, StringComparison.Ordinal);
        var content = ProposalCommands.ComposeIntent(new(path, Version, "variants", DeleteVerb,
            Json(new { target = Id(extra.SenseId), entry = Id(extra.EntryId) })));
        Assert.False(content.Succeeded);
        Assert.Equal(RefusalCodes.SenseAnalysisContent, content.Refusal!.Code);
        Assert.Equal("InflectionClass", content.Refusal.Facts["field"]);
        var accepted = ProposalCommands.ComposeIntent(new(path, Version, "variants", DeleteVerb,
            Json(new { target = Id(linked.SenseId), entry = Id(linked.EntryId) })));
        Assert.True(accepted.Succeeded, accepted.Refusal?.Message);
    }

    [Fact]
    public void LinkingToAnEntryCreatedEarlierInTheDraftDryRunsAndApplies()
    {
        using var cache = pristine.NewScratch();
        var path = cache.ProjectId.Path;
        var vernacular = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultVernWs);
        var analysis = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultAnalWs);
        var seed = EmptySenseSeed.Add(cache, "variant-link");
        Guid referenceId = default, typeId = default;
        SIL.LCModel.Infrastructure.NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var services = cache.ServiceLocator;
            var entry = services.GetInstance<ILexEntryRepository>().GetObject(seed.EntryId);
            var reference = services.GetInstance<ILexEntryRefFactory>().Create();
            entry.EntryRefsOS.Add(reference);
            reference.RefType = LexEntryRefTags.krtVariant;
            referenceId = reference.Guid;
            var list = cache.LangProject.LexDbOA.VariantEntryTypesOA;
            if (list is null)
            {
                list = services.GetInstance<ICmPossibilityListFactory>().Create();
                cache.LangProject.LexDbOA.VariantEntryTypesOA = list;
            }
            var type = services.GetInstance<ILexEntryTypeFactory>().Create();
            list.PossibilitiesOS.Add(type);
            typeId = type.Guid;
        });
        new FwDataProjectLoader().Save(cache);
        const string draft = "entry-and-variant";
        Assert.True(ProposalCommands.New(new(path, Version, draft, "Add and link")).Succeeded);
        var authored = ProposalCommands.ComposeAuthorLexicalEntry(new(path, Version, draft, Json(new
        {
            form = "mainform", formWs = vernacular, morphType = Id(MoMorphTypeTags.kguidMorphStem),
            gloss = "meaning", analysisWs = analysis,
            msa = new { kind = "stem", category = Id(pristine.Seed.PartOfSpeechId) },
        })));
        Assert.True(authored.Succeeded, authored.Refusal?.Message);
        var created = Assert.Single(authored.Value!.Operations, operation => operation.Kind == "lexical/lexDb/createEntries");
        var linked = ProposalCommands.ComposeIntent(new(path, Version, draft, "compose-link-variant",
            Json(new { target = Id(referenceId), component = created.EntityId, variantType = Id(typeId) })));
        Assert.True(linked.Succeeded, linked.Refusal?.Message);
        Assert.True(DraftRationale.Write(path, Version, draft, "Create an entry and link its variant.").Succeeded);
        var finalized = ProposalCommands.Finalize(new(path, Version, draft));
        Assert.True(finalized.Succeeded, finalized.Refusal?.Message);
        var stored = ProjectStoreCommand.Run<Proposal>(path, Version, (database, _) =>
            CommandOutcome<Proposal>.Success(ProposalJsonParser.Parse(new ProposalRepository(database)
                .Get(CanonicalId.Parse(finalized.Value!.ProposalId)).ProposalJson!)));
        Assert.True(stored.Succeeded, stored.Refusal?.Message);
        var proposal = stored.Value!;
        var link = Assert.Single(proposal.Operations, operation => operation.Kind == VariantLinkOperationKinds.Set);
        Assert.Contains(link.DependsOn, dependency => dependency.OperationId == CanonicalId.Parse(created.OperationId));
        using var scratch = DryRunScratch.Adopt(new FwDataProjectLoader().LoadScratchCache(path), "created entry variant");
        var dry = ProposalDryRunner.Run(scratch, proposal);
        ProposalApplier.Apply(cache, proposal, dry.Anchor, "tester");
        var referenceAfter = cache.ServiceLocator.GetInstance<ILexEntryRefRepository>().GetObject(referenceId);
        Assert.Equal(CanonicalId.Parse(created.EntityId!).ToGuid(), Assert.Single(referenceAfter.ComponentLexemesRS).Guid);
        Assert.Equal(typeId, Assert.Single(referenceAfter.VariantEntryTypesRS).Guid);
    }

    [Fact]
    public void ExistingAnalysisCanUseAPartOfSpeechCreatedEarlierInTheDraft()
    {
        using var cache = pristine.NewScratch();
        var path = cache.ProjectId.Path;
        var analysisWs = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultAnalWs);
        var seed = EmptySenseSeed.Add(cache, "existing-analysis");
        new FwDataProjectLoader().Save(cache);
        const string draft = "created-category";
        Assert.True(ProposalCommands.New(new(path, Version, draft, "Create and use a category")).Succeeded);
        var category = ProposalCommands.ComposeAuthorPartOfSpeech(new(path, Version, draft,
            Json(new { name = "New category", ws = analysisWs })));
        Assert.True(category.Succeeded, category.Refusal?.Message);
        var created = Assert.Single(category.Value!.Operations, operation => operation.EntityId is not null);
        var set = ProposalCommands.ComposeIntent(new(path, Version, draft, PosVerb,
            Json(new { target = Id(seed.MsaId), expectedPartOfSpeech = (string?)null, partOfSpeech = created.EntityId })));
        Assert.True(set.Succeeded, set.Refusal?.Message);
        Assert.True(DraftRationale.Write(path, Version, draft, "Create a category and assign it.").Succeeded);
        var finalized = ProposalCommands.Finalize(new(path, Version, draft));
        Assert.True(finalized.Succeeded, finalized.Refusal?.Message);
        var stored = ProjectStoreCommand.Run<Proposal>(path, Version, (database, _) =>
            CommandOutcome<Proposal>.Success(ProposalJsonParser.Parse(new ProposalRepository(database)
                .Get(CanonicalId.Parse(finalized.Value!.ProposalId)).ProposalJson!)));
        Assert.True(stored.Succeeded, stored.Refusal?.Message);
        var proposal = stored.Value!;
        var operation = Assert.Single(proposal.Operations, op => op.Kind == MoStemMsaPartOfSpeechOperationKinds.SetPartOfSpeech);
        Assert.Contains(operation.DependsOn, dependency => dependency.OperationId == CanonicalId.Parse(created.OperationId));
        using var scratch = DryRunScratch.Adopt(new FwDataProjectLoader().LoadScratchCache(path), "created category");
        var dry = ProposalDryRunner.Run(scratch, proposal);
        ProposalApplier.Apply(cache, proposal, dry.Anchor, "tester");
        var msa = cache.ServiceLocator.GetInstance<IMoStemMsaRepository>().GetObject(seed.MsaId);
        Assert.Equal(CanonicalId.Parse(created.EntityId!).ToGuid(), msa.PartOfSpeechRA.Guid);
    }

    [Fact]
    public void LinkingThenDeletingInOneBatchDeclaresTheDependencyAndDryRuns()
    {
        using var cache = pristine.NewScratch();
        var path = cache.ProjectId.Path;
        var seed = EmptySenseSeed.Add(cache, "variant");
        Guid referenceId = default, typeId = default;
        SIL.LCModel.Infrastructure.NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var services = cache.ServiceLocator;
            var entry = services.GetInstance<ILexEntryRepository>().GetObject(seed.EntryId);
            ((IMoStemMsa)entry.MorphoSyntaxAnalysesOC.Single()).PartOfSpeechRA =
                services.GetInstance<IPartOfSpeechRepository>().GetObject(pristine.Seed.PartOfSpeechId);
            var reference = services.GetInstance<ILexEntryRefFactory>().Create();
            entry.EntryRefsOS.Add(reference);
            reference.RefType = LexEntryRefTags.krtVariant;
            referenceId = reference.Guid;
            var list = cache.LangProject.LexDbOA.VariantEntryTypesOA;
            if (list is null)
            {
                list = services.GetInstance<ICmPossibilityListFactory>().Create();
                cache.LangProject.LexDbOA.VariantEntryTypesOA = list;
            }
            var type = services.GetInstance<ILexEntryTypeFactory>().Create();
            list.PossibilitiesOS.Add(type);
            typeId = type.Guid;
        });
        new FwDataProjectLoader().Save(cache);
        Assert.True(ProposalCommands.New(new(path, Version, "link", "Link variant")).Succeeded);
        var self = ProposalCommands.ComposeIntent(new(path, Version, "link", "compose-link-variant",
            Json(new { target = Id(referenceId), component = Id(seed.EntryId), variantType = Id(typeId) })));
        Assert.False(self.Succeeded);
        Assert.Equal(RefusalCodes.VariantSelfLink, self.Refusal!.Code);
        var batch = ProposalCommands.ComposeBatch(new(path, Version, "link", Json(new
        {
            intents = new object[]
            {
                new { verb = "compose-link-variant", intent = new { target = Id(referenceId),
                    component = Id(pristine.Seed.FirstEntryId), variantType = Id(typeId) } },
                new { verb = DeleteVerb, intent = new { target = Id(seed.SenseId), entry = Id(seed.EntryId) } },
            },
        })));
        Assert.True(batch.Succeeded, batch.Refusal?.Message);
        var proposal = ReadDraft(path, "link");
        var link = Assert.Single(proposal.Operations, operation => operation.Kind == VariantLinkOperationKinds.Set);
        var deletion = Assert.Single(proposal.Operations, operation => operation.Kind == LexSenseDeleteOperationKinds.DeleteSense);
        Assert.Contains(deletion.DependsOn, dependency => dependency.OperationId == link.OperationId);
        using var scratch = DryRunScratch.Adopt(new FwDataProjectLoader().LoadScratchCache(path), "variant preview");
        var dryRun = ProposalDryRunner.Run(scratch, proposal);
        Assert.Equal(4, dryRun.ExpectedEffects.Count);
        ProposalApplier.Apply(cache, proposal, dryRun.Anchor, "tester");
        Assert.False(cache.ServiceLocator.ObjectRepository.IsValidObjectId(seed.SenseId));
        Assert.False(cache.ServiceLocator.ObjectRepository.IsValidObjectId(seed.MsaId));
    }

    private (string Path, EmptySenseSeed[] Variants, EmptySenseSeed[] Stems) Open(int variants, int stems)
    {
        using var cache = pristine.NewScratch();
        var path = cache.ProjectId.Path;
        var variantSeeds = Enumerable.Range(0, variants).Select(i => EmptySenseSeed.Add(cache, "var" + i)).ToArray();
        var stemSeeds = Enumerable.Range(0, stems).Select(i => EmptySenseSeed.Add(cache, "stem" + i)).ToArray();
        new FwDataProjectLoader().Save(cache);
        return (path, variantSeeds, stemSeeds);
    }

    private static string Id(Guid guid) => CanonicalId.FromGuid(guid).Value;

    private static string Json(object value) => JsonSerializer.Serialize(value);

    private static Proposal ReadDraft(string path, string draftName)
    {
        var result = ProjectStoreCommand.Run(path, Version, (database, _) =>
        {
            var document = ProposalCommands.DeserializeDraft(new ProposalRepository(database)
                .GetDraft(draftName).ProposalJson!);
            return CommandOutcome<Proposal>.Success(ProposalJsonParser.Parse(ProposalCommands.BuildProposalJson(document)));
        });
        Assert.True(result.Succeeded, result.Refusal?.Message);
        return result.Value!;
    }
}
