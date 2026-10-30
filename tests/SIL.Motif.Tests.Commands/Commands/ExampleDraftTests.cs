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
public sealed class ExampleDraftTests(PristineProjectFixture pristine)
{
    private const string Version = "0.1.0";

    [Fact]
    public void TheExampleComposersAreCliFirstAndHaveNoApplyPathThroughAgentTools()
    {
        foreach (var verb in new[] { "compose-move-example", "compose-add-example" })
        {
            var descriptor = Assert.Single(ProposalCommands.ComposerDescriptors, composer => composer.Verb == verb);
            Assert.False(descriptor.IsAgentTool);
            Assert.Contains(verb, ProposalCommands.DraftComposers.Keys);
        }
    }

    [Fact]
    public void ExamplesCanBeAddedAndMovedToSensesCreatedEarlierInTheSameDraftThenFinalizeDryRunAndApply()
    {
        using var cache = pristine.NewScratch();
        var path = cache.ProjectId.Path;
        var vernacular = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultVernWs);
        var analysis = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultAnalWs);
        Guid exampleId = default;
        var senses = new List<Guid>();
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var services = cache.ServiceLocator;
            var entry = services.GetInstance<ILexEntryFactory>().Create();
            var form = services.GetInstance<IMoStemAllomorphFactory>().Create();
            entry.LexemeFormOA = form;
            form.MorphTypeRA = services.GetInstance<IMoMorphTypeRepository>().GetObject(MoMorphTypeTags.kguidMorphStem);
            form.Form.set_String(cache.DefaultVernWs, "ayu");
            var msa = services.GetInstance<IMoStemMsaFactory>().Create();
            entry.MorphoSyntaxAnalysesOC.Add(msa);
            msa.PartOfSpeechRA = services.GetInstance<IPartOfSpeechRepository>().GetObject(pristine.Seed.PartOfSpeechId);
            foreach (var gloss in new[] { "tree", "kaayu" })
            {
                var sense = services.GetInstance<ILexSenseFactory>().Create();
                entry.SensesOS.Add(sense);
                sense.Gloss.set_String(cache.DefaultAnalWs, gloss);
                sense.MorphoSyntaxAnalysisRA = msa;
                senses.Add(sense.Guid);
            }
            var example = services.GetInstance<ILexExampleSentenceFactory>().Create();
            entry.SensesOS[1].ExamplesOS.Add(example);
            example.Example.set_String(cache.DefaultVernWs, "daghang kaayu");
            exampleId = example.Guid;
        });
        new FwDataProjectLoader().Save(cache);
        const string draft = "examples";
        Assert.True(ProposalCommands.New(new(path, Version, draft, "Examples")).Succeeded);

        var authored = ProposalCommands.ComposeIntent(new(path, Version, draft, "compose-author-lexical-entry", Json(new
        {
            form = "tuig", formWs = vernacular, morphType = CanonicalId.FromGuid(MoMorphTypeTags.kguidMorphStem).Value,
            gloss = "year", analysisWs = analysis,
            msa = new { kind = "stem", category = CanonicalId.FromGuid(pristine.Seed.PartOfSpeechId).Value },
        })));
        Assert.True(authored.Succeeded, authored.Refusal?.Message);
        var newSense = authored.Value!.Operations.Single(op => op.Kind == LexEntrySensesOperationKinds.Create).EntityId!;

        var added = ProposalCommands.ComposeIntent(new(path, Version, draft, "compose-add-example", Json(new
        {
            sense = newSense,
            text = new[] { new { ws = vernacular, text = "usa ka tuig" } },
            translations = new[] { new { type = "free", text = new[] { new { ws = analysis, text = "one year" } } } },
        })));
        Assert.True(added.Succeeded, added.Refusal?.Message);
        var moved = ProposalCommands.ComposeIntent(new(path, Version, draft, "compose-move-example", Json(new
        {
            example = Id(exampleId), toSense = newSense,
        })));
        Assert.True(moved.Succeeded, moved.Refusal?.Message);
        var sameSense = ProposalCommands.ComposeIntent(new(path, Version, draft, "compose-move-example", Json(new
        {
            example = Id(exampleId), toSense = Id(senses[1]),
        })));
        Assert.False(sameSense.Succeeded);
        var unknown = ProposalCommands.ComposeIntent(new(path, Version, draft, "compose-add-example", Json(new
        {
            sense = Id(Guid.NewGuid()), text = new[] { new { ws = vernacular, text = "x" } },
        })));
        Assert.False(unknown.Succeeded);
        var extra = ProposalCommands.ComposeIntent(new(path, Version, draft, "compose-move-example", Json(new
        {
            example = Id(exampleId), toSense = newSense, force = true,
        })));
        Assert.False(extra.Succeeded);

        Assert.True(DraftRationale.Write(path, Version, draft, "tuig gets the examples kept from the duplicate.").Succeeded);
        var finalized = ProposalCommands.Finalize(new(path, Version, draft));
        Assert.True(finalized.Succeeded, finalized.Refusal?.Message);
        var stored = ProjectStoreCommand.Run<Proposal>(path, Version, (database, _) =>
            CommandOutcome<Proposal>.Success(ProposalJsonParser.Parse(new ProposalRepository(database)
                .Get(CanonicalId.Parse(finalized.Value!.ProposalId)).ProposalJson!)));
        Assert.True(stored.Succeeded, stored.Refusal?.Message);
        var proposal = stored.Value!;
        Assert.Single(proposal.Operations, op => op.Kind == LexExampleOperationKinds.Create);
        Assert.Single(proposal.Operations, op => op.Kind == LexExampleOperationKinds.Move);

        using var scratch = DryRunScratch.Adopt(new FwDataProjectLoader().LoadScratchCache(path), "examples");
        var dry = ProposalDryRunner.Run(scratch, proposal);
        ProposalApplier.Apply(cache, proposal, dry.Anchor, "tester");

        var target = cache.ServiceLocator.GetInstance<ILexSenseRepository>().GetObject(CanonicalId.Parse(newSense).ToGuid());
        Assert.Equal(2, target.ExamplesOS.Count);
        Assert.Equal(exampleId, target.ExamplesOS[1].Guid);
        Assert.Equal("usa ka tuig", target.ExamplesOS[0].Example.get_String(cache.DefaultVernWs).Text);
        Assert.Empty(cache.ServiceLocator.GetInstance<ILexSenseRepository>().GetObject(senses[1]).ExamplesOS);
    }

    private static string Id(Guid guid) => CanonicalId.FromGuid(guid).Value;

    private static string Json(object value) => JsonSerializer.Serialize(value);
}
