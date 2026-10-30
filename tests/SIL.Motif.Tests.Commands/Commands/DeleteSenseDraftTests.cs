using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Runner.Apply;
using SIL.Motif.Runner.DryRun;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[Collection(LcmCacheParallelCollections.Group2)]
public sealed class DeleteSenseDraftTests(PristineProjectFixture pristine)
{
    private const string Version = "0.1.0";

    [Fact]
    public void TheDeletionsAreCliFirstAndHaveNoApplyPathThroughAgentTools()
    {
        foreach (var verb in new[] { "compose-delete-sense", "compose-remove-alternate-form" })
        {
            var descriptor = Assert.Single(ProposalCommands.ComposerDescriptors, composer => composer.Verb == verb);
            Assert.False(descriptor.IsAgentTool);
            Assert.Contains(verb, ProposalCommands.DraftComposers.Keys);
        }
    }

    [Fact]
    public void AMoveADeletionAndAnAlternateFormRemovalFormOneDraftComposedAgainstEachOther()
    {
        using var cache = pristine.NewScratch();
        var path = cache.ProjectId.Path;
        var vernacular = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultVernWs);
        Guid entryId = default, formId = default;
        var senses = new List<Guid>();
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var services = cache.ServiceLocator;
            var entry = services.GetInstance<ILexEntryFactory>().Create();
            entryId = entry.Guid;
            var stem = services.GetInstance<IMoMorphTypeRepository>().GetObject(MoMorphTypeTags.kguidMorphStem);
            var form = services.GetInstance<IMoStemAllomorphFactory>().Create();
            entry.LexemeFormOA = form;
            form.MorphTypeRA = stem;
            form.Form.set_String(cache.DefaultVernWs, "tuig");
            var alternate = services.GetInstance<IMoStemAllomorphFactory>().Create();
            entry.AlternateFormsOS.Add(alternate);
            alternate.MorphTypeRA = stem;
            alternate.Form.set_String(cache.DefaultVernWs, "tinuig");
            formId = alternate.Guid;
            var msa = services.GetInstance<IMoStemMsaFactory>().Create();
            entry.MorphoSyntaxAnalysesOC.Add(msa);
            msa.PartOfSpeechRA = services.GetInstance<IPartOfSpeechRepository>().GetObject(pristine.Seed.PartOfSpeechId);
            foreach (var gloss in new[] { "year", "annual", "yearly" })
            {
                var sense = services.GetInstance<ILexSenseFactory>().Create();
                entry.SensesOS.Add(sense);
                sense.Gloss.set_String(cache.DefaultAnalWs, gloss);
                sense.MorphoSyntaxAnalysisRA = msa;
                senses.Add(sense.Guid);
            }
        });
        new FwDataProjectLoader().Save(cache);
        const string draft = "clean-tuig";
        Assert.True(ProposalCommands.New(new(path, Version, draft, "Clean tuig")).Succeeded);

        var moved = ProposalCommands.ComposeIntent(new(path, Version, draft, "compose-move-sense-to-entry", Json(new
        {
            sense = Id(senses[1]), form = "tinuig", formWs = vernacular,
            morphType = CanonicalId.FromGuid(MoMorphTypeTags.kguidMorphStem).Value,
        })));
        Assert.True(moved.Succeeded, moved.Refusal?.Message);
        var deleted = ProposalCommands.ComposeIntent(new(path, Version, draft, "compose-delete-sense",
            Json(new { target = Id(senses[2]), owner = Id(entryId) })));
        Assert.True(deleted.Succeeded, deleted.Refusal?.Message);
        var onlySense = ProposalCommands.ComposeIntent(new(path, Version, draft, "compose-delete-sense",
            Json(new { target = Id(senses[0]), owner = Id(entryId) })));
        Assert.False(onlySense.Succeeded);
        Assert.Contains("only sense", onlySense.Refusal?.Message ?? "", StringComparison.Ordinal);
        var removed = ProposalCommands.ComposeIntent(new(path, Version, draft, "compose-remove-alternate-form",
            Json(new { entry = Id(entryId), form = Id(formId) })));
        Assert.True(removed.Succeeded, removed.Refusal?.Message);
        var extra = ProposalCommands.ComposeIntent(new(path, Version, draft, "compose-delete-sense",
            Json(new { target = Id(senses[2]), owner = Id(entryId), force = true })));
        Assert.False(extra.Succeeded);

        Assert.True(DraftRationale.Write(path, Version, draft,
            "tinuig has its own entry; the other sense and the alternate form are duplicates.").Succeeded);
        var finalized = ProposalCommands.Finalize(new(path, Version, draft));
        Assert.True(finalized.Succeeded, finalized.Refusal?.Message);
        var stored = ProjectStoreCommand.Run<Proposal>(path, Version, (database, _) =>
            CommandOutcome<Proposal>.Success(ProposalJsonParser.Parse(new ProposalRepository(database)
                .Get(CanonicalId.Parse(finalized.Value!.ProposalId)).ProposalJson!)));
        Assert.True(stored.Succeeded, stored.Refusal?.Message);
        var proposal = stored.Value!;
        Assert.Single(proposal.Operations, op => op.Kind == LexSenseRemoveOperationKinds.Remove);
        Assert.Single(proposal.Operations, op => op.Kind == AlternateFormRemoveOperationKinds.Remove);

        using var scratch = DryRunScratch.Adopt(new FwDataProjectLoader().LoadScratchCache(path), "delete senses");
        var dry = ProposalDryRunner.Run(scratch, proposal);
        ProposalApplier.Apply(cache, proposal, dry.Anchor, "tester");

        var root = cache.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(entryId);
        Assert.Equal([senses[0]], root.SensesOS.Select(sense => sense.Guid));
        Assert.Empty(root.AlternateFormsOS);
        Assert.Equal(senses[1], cache.ServiceLocator.GetInstance<ILexSenseRepository>().GetObject(senses[1]).Guid);
        Assert.False(cache.ServiceLocator.GetInstance<ICmObjectRepository>().IsValidObjectId(senses[2]));
    }

    private static string Id(Guid guid) => CanonicalId.FromGuid(guid).Value;

    private static string Json(object value) => JsonSerializer.Serialize(value);
}
