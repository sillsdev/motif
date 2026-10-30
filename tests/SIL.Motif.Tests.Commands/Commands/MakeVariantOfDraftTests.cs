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
public sealed class MakeVariantOfDraftTests(PristineProjectFixture pristine)
{
    private const string Version = "0.1.0";
    private const string Verb = "compose-make-variant-of";

    [Fact]
    public void ABatchOfTwoNewEntriesAndTheirVariantLinkDependsOnBothAndAppliesAfterADryRun()
    {
        using var cache = pristine.NewScratch();
        var path = cache.ProjectId.Path;
        var vernacular = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultVernWs);
        var analysis = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultAnalWs);
        var typeId = SeedVariantType(cache);
        new FwDataProjectLoader().Save(cache);
        const string draft = "both-new";
        Assert.True(ProposalCommands.New(new(path, Version, draft, "Make a variant")).Succeeded);
        var main = Author(path, draft, "mainform", vernacular, analysis);
        var variant = Author(path, draft, "mainform-", vernacular, analysis);
        var linked = ProposalCommands.ComposeIntent(new(path, Version, draft, Verb,
            Json(new { entry = variant.EntityId, component = main.EntityId, variantType = Id(typeId) })));
        Assert.True(linked.Succeeded, linked.Refusal?.Message);
        Assert.True(DraftRationale.Write(path, Version, draft, "Mark the hyphenated form as a spelling variant.").Succeeded);
        var proposal = ReadFinal(path, draft);
        var link = Assert.Single(proposal.Operations, operation => operation.Kind == VariantRefCreateOperationKinds.Create);
        Assert.Contains(link.DependsOn, dependency => dependency.OperationId == CanonicalId.Parse(main.OperationId));
        Assert.Contains(link.DependsOn, dependency => dependency.OperationId == CanonicalId.Parse(variant.OperationId));
        using var scratch = DryRunScratch.Adopt(new FwDataProjectLoader().LoadScratchCache(path), "both new");
        var dry = ProposalDryRunner.Run(scratch, proposal);
        Assert.Contains(dry.ExpectedEffects, effect => effect.Field == "lexical/lexEntry/entryRefs" &&
            effect.CanonicalId == CanonicalId.Parse(variant.EntityId!));
        ProposalApplier.Apply(cache, proposal, dry.Anchor, "tester");
        var entry = cache.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(CanonicalId.Parse(variant.EntityId!).ToGuid());
        var reference = Assert.Single(entry.EntryRefsOS);
        Assert.Equal(LexEntryRefTags.krtVariant, reference.RefType);
        Assert.Equal(CanonicalId.Parse(main.EntityId!).ToGuid(), Assert.Single(reference.ComponentLexemesRS).Guid);
        Assert.Equal(typeId, Assert.Single(reference.VariantEntryTypesRS).Guid);
    }

    [Fact]
    public void ADraftRefusesALinkBackToTheVariantAsACycle()
    {
        using var cache = pristine.NewScratch();
        var path = cache.ProjectId.Path;
        var vernacular = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultVernWs);
        var analysis = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultAnalWs);
        var typeId = SeedVariantType(cache);
        new FwDataProjectLoader().Save(cache);
        Assert.True(ProposalCommands.New(new(path, Version, "cycle", "Cycle")).Succeeded);
        var main = Author(path, "cycle", "a", vernacular, analysis);
        var variant = Author(path, "cycle", "b", vernacular, analysis);
        Assert.True(ProposalCommands.ComposeIntent(new(path, Version, "cycle", Verb,
            Json(new { entry = variant.EntityId, component = main.EntityId, variantType = Id(typeId) }))).Succeeded);
        var back = ProposalCommands.ComposeIntent(new(path, Version, "cycle", Verb,
            Json(new { entry = main.EntityId, component = variant.EntityId, variantType = Id(typeId) })));
        Assert.False(back.Succeeded);
        Assert.Equal(RefusalCodes.VariantCycle, back.Refusal!.Code);
        var duplicate = ProposalCommands.ComposeIntent(new(path, Version, "cycle", Verb,
            Json(new { entry = variant.EntityId, component = main.EntityId, variantType = Id(typeId) })));
        Assert.Equal(RefusalCodes.VariantAlreadyLinked, duplicate.Refusal!.Code);
    }

    [Fact]
    public void TheVerbIsOfferedToAnAgentAsADraftComposer()
    {
        var descriptor = Assert.Single(ProposalCommands.ComposerDescriptors, composer => composer.Verb == Verb);
        Assert.True(descriptor.IsAgentTool);
        Assert.Contains(Verb, ProposalCommands.DraftComposers.Keys);
    }

    private OperationSummary Author(string path, string draft, string form, string vernacular, string analysis)
    {
        var authored = ProposalCommands.ComposeAuthorLexicalEntry(new(path, Version, draft, Json(new
        {
            form, formWs = vernacular, morphType = Id(MoMorphTypeTags.kguidMorphStem),
            gloss = "meaning " + form, analysisWs = analysis,
            msa = new { kind = "stem", category = Id(pristine.Seed.PartOfSpeechId) },
        })));
        Assert.True(authored.Succeeded, authored.Refusal?.Message);
        return Assert.Single(authored.Value!.Operations, operation => operation.Kind == "lexical/lexDb/createEntries");
    }

    private static Guid SeedVariantType(LcmCache cache)
    {
        Guid typeId = default;
        SIL.LCModel.Infrastructure.NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var services = cache.ServiceLocator;
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
        return typeId;
    }

    private static Proposal ReadFinal(string path, string draft)
    {
        var finalized = ProposalCommands.Finalize(new(path, Version, draft));
        Assert.True(finalized.Succeeded, finalized.Refusal?.Message);
        var stored = ProjectStoreCommand.Run<Proposal>(path, Version, (database, _) =>
            CommandOutcome<Proposal>.Success(ProposalJsonParser.Parse(new ProposalRepository(database)
                .Get(CanonicalId.Parse(finalized.Value!.ProposalId)).ProposalJson!)));
        Assert.True(stored.Succeeded, stored.Refusal?.Message);
        return stored.Value!;
    }

    private static string Id(Guid guid) => CanonicalId.FromGuid(guid).Value;
    private static string Json(object value) => JsonSerializer.Serialize(value);
}
