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
public sealed class MoveSenseToEntryDraftTests(PristineProjectFixture pristine)
{
    private const string Version = "0.1.0";
    private const string Verb = "compose-move-sense-to-entry";

    [Fact]
    public void TheMoveIsCliFirstAndHasNoApplyPathThroughAgentTools()
    {
        var descriptor = Assert.Single(ProposalCommands.ComposerDescriptors, composer => composer.Verb == Verb);
        Assert.False(descriptor.IsAgentTool);
        Assert.Contains(Verb, ProposalCommands.DraftComposers.Keys);
    }

    [Fact]
    public void TwoMovesFromOneRootBecomeOneDraftThatFinalizesDryRunsAndApplies()
    {
        using var cache = pristine.NewScratch();
        var path = cache.ProjectId.Path;
        var vernacular = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultVernWs);
        Guid entryId = default;
        var senses = new List<Guid>();
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var services = cache.ServiceLocator;
            var entry = services.GetInstance<ILexEntryFactory>().Create();
            entryId = entry.Guid;
            var form = services.GetInstance<IMoStemAllomorphFactory>().Create();
            entry.LexemeFormOA = form;
            form.MorphTypeRA = services.GetInstance<IMoMorphTypeRepository>().GetObject(MoMorphTypeTags.kguidMorphStem);
            form.Form.set_String(cache.DefaultVernWs, "ayu");
            var msa = services.GetInstance<IMoStemMsaFactory>().Create();
            entry.MorphoSyntaxAnalysesOC.Add(msa);
            msa.PartOfSpeechRA = services.GetInstance<IPartOfSpeechRepository>().GetObject(pristine.Seed.PartOfSpeechId);
            foreach (var gloss in new[] { "tree", "kaayu", "maayu" })
            {
                var sense = services.GetInstance<ILexSenseFactory>().Create();
                entry.SensesOS.Add(sense);
                sense.Gloss.set_String(cache.DefaultAnalWs, gloss);
                sense.MorphoSyntaxAnalysisRA = msa;
                senses.Add(sense.Guid);
            }
        });
        new FwDataProjectLoader().Save(cache);
        const string draft = "split-ayu";
        Assert.True(ProposalCommands.New(new(path, Version, draft, "Split ayu")).Succeeded);
        var morphType = CanonicalId.FromGuid(MoMorphTypeTags.kguidMorphStem).Value;

        var refusedExtra = ProposalCommands.ComposeIntent(new(path, Version, draft, Verb, Json(new
        {
            sense = Id(senses[1]), form = "kaayu", formWs = vernacular, morphType, apply = true,
        })));
        Assert.False(refusedExtra.Succeeded);
        foreach (var (sense, form) in new[] { (senses[1], "kaayu"), (senses[2], "maayu") })
        {
            var composed = ProposalCommands.ComposeIntent(new(path, Version, draft, Verb, Json(new
            {
                sense = Id(sense), form, formWs = vernacular, morphType,
            })));
            Assert.True(composed.Succeeded, composed.Refusal?.Message);
        }
        Assert.True(DraftRationale.Write(path, Version, draft,
            "kaayu and maayu are derived words, not senses of ayu.").Succeeded);
        var finalized = ProposalCommands.Finalize(new(path, Version, draft));
        Assert.True(finalized.Succeeded, finalized.Refusal?.Message);
        var stored = ProjectStoreCommand.Run<Proposal>(path, Version, (database, _) =>
            CommandOutcome<Proposal>.Success(ProposalJsonParser.Parse(new ProposalRepository(database)
                .Get(CanonicalId.Parse(finalized.Value!.ProposalId)).ProposalJson!)));
        Assert.True(stored.Succeeded, stored.Refusal?.Message);
        var proposal = stored.Value!;
        var moves = proposal.Operations.Where(op => op.Kind == LexSenseMoveOperationKinds.MoveToEntry).ToArray();
        Assert.Equal(2, moves.Length);
        Assert.All(moves, move => Assert.Equal(2, move.DependsOn.Count));

        using var scratch = DryRunScratch.Adopt(new FwDataProjectLoader().LoadScratchCache(path), "move senses");
        var dry = ProposalDryRunner.Run(scratch, proposal);
        ProposalApplier.Apply(cache, proposal, dry.Anchor, "tester");

        var root = cache.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(entryId);
        Assert.Equal([senses[0]], root.SensesOS.Select(sense => sense.Guid));
        Assert.Equal(root.MorphoSyntaxAnalysesOC.Single().Guid, root.SensesOS[0].MorphoSyntaxAnalysisRA.Guid);
        var owners = senses.Skip(1).Select(id =>
            cache.ServiceLocator.GetInstance<ILexSenseRepository>().GetObject(id).Owner.Guid).ToArray();
        Assert.Equal(2, owners.Distinct().Count());
        Assert.DoesNotContain(entryId, owners);
    }

    private static string Id(Guid guid) => CanonicalId.FromGuid(guid).Value;

    private static string Json(object value) => JsonSerializer.Serialize(value);
}
