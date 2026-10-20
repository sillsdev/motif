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
    public void BothComposersAreCliFirstAndNeverOfferedToAnAgent()
    {
        foreach (var verb in new[] { DeleteVerb, PosVerb })
        {
            var descriptor = Assert.Single(ProposalCommands.ComposerDescriptors, composer => composer.Verb == verb);
            Assert.False(descriptor.IsAgentTool);
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
