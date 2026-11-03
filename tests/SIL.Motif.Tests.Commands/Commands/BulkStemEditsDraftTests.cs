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

/// <summary>One compose-batch file retypes and retires hundreds of stems in a single Draft, so no per-stem command is needed.</summary>
[Collection(LcmCacheParallelCollections.Group2)]
public sealed class BulkStemEditsDraftTests(PristineProjectFixture pristine)
{
    private const string Version = "0.1.0";
    private const int StemCount = 150;

    [Fact]
    public void OneBatchRetypesAndRetiresHundredsOfStemsInOneDraft()
    {
        using var cache = pristine.NewScratch();
        var path = cache.ProjectId.Path;
        var seeds = Enumerable.Range(0, StemCount).Select(i => EmptySenseSeed.Add(cache, "bulk" + i)).ToArray();
        new FwDataProjectLoader().Save(cache);
        cache.Dispose();
        const string draft = "bulk-stems";
        Assert.True(ProposalCommands.New(new(path, Version, draft, "Bulk retype")).Succeeded);
        var pos = CanonicalId.FromGuid(pristine.Seed.PartOfSpeechId).Value;
        var intents = seeds.Select(seed => (object)new
            {
                verb = "compose-set-stem-part-of-speech",
                intent = new { target = Id(seed.MsaId), expectedPartOfSpeech = (string?)null, partOfSpeech = pos },
            })
            .Concat(seeds.Select(seed => new
            {
                verb = "compose-set-entry-do-not-parse",
                intent = new { target = Id(seed.EntryId), expectedDoNotParse = false, doNotParse = true },
            }))
            .ToArray();

        var batch = ProposalCommands.ComposeBatch(new(path, Version, draft, JsonSerializer.Serialize(new { intents })));

        Assert.True(batch.Succeeded, batch.Refusal?.Message);
        Assert.Equal(2 * StemCount, batch.Value!.Intents.Count);
        Assert.Equal(2 * StemCount, batch.Value.OperationCount);
        var result = ProjectStoreCommand.Run(path, Version, (database, _) =>
        {
            var document = ProposalCommands.DeserializeDraft(new ProposalRepository(database).GetDraft(draft).ProposalJson!);
            return CommandOutcome<Proposal>.Success(ProposalJsonParser.Parse(ProposalCommands.BuildProposalJson(document)));
        });
        Assert.True(result.Succeeded, result.Refusal?.Message);
        Assert.Equal(StemCount, result.Value!.Operations.Count(op => op.Kind == MoStemMsaPartOfSpeechOperationKinds.SetPartOfSpeech));
        Assert.Equal(StemCount, result.Value.Operations.Count(
            op => op.Kind == LexEntryDoNotUseForParsingOperationKinds.SetDoNotUseForParsing));
    }

    private static string Id(Guid guid) => CanonicalId.FromGuid(guid).Value;
}
