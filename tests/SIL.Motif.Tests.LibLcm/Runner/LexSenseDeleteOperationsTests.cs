using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Model.Snapshot;
using SIL.Motif.Runner.Apply;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Runner;

[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group1)]
public sealed class LexSenseDeleteOperationsTests : IDisposable
{
    private readonly LcmCache _cache;

    public LexSenseDeleteOperationsTests(PristineProjectFixture pristine) => _cache = pristine.NewScratch();

    public void Dispose()
    {
        if (!_cache.IsDisposed) _cache.Dispose();
    }

    [Fact]
    public void AnEmptySenseAndItsOrphanedAnalysisAreDeletedThroughDryRunAndApply()
    {
        var seed = EmptySenseSeed.Add(_cache, "varia");
        var proposal = Proposal(seed.SenseId, new { msa = Id(seed.MsaId) });

        var dryRun = ScratchDryRun.Of(_cache, proposal);

        Assert.Equal([SnapshotFields.LexEntrySenses, SnapshotFields.LexEntryMorphoSyntaxAnalyses],
            dryRun.ExpectedEffects.Select(effect => effect.Field));
        Assert.All(dryRun.ExpectedEffects, effect => Assert.Single(effect.Before));
        Assert.All(dryRun.ExpectedEffects, effect => Assert.Empty(effect.After));
        Assert.True(_cache.ServiceLocator.ObjectRepository.IsValidObjectId(seed.SenseId));

        ProposalApplier.Apply(_cache, proposal, dryRun.Anchor, "tester");

        var entry = _cache.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(seed.EntryId);
        Assert.Empty(entry.SensesOS);
        Assert.Empty(entry.MorphoSyntaxAnalysesOC);
        Assert.False(_cache.ServiceLocator.ObjectRepository.IsValidObjectId(seed.MsaId));
    }

    [Fact]
    public void ASenseWithAnyContentIsRefusedAndNothingChanges()
    {
        var seed = EmptySenseSeed.Add(_cache, "glossed", gloss: "a gloss");
        var proposal = Proposal(seed.SenseId, new { msa = Id(seed.MsaId) });

        var failure = Assert.ThrowsAny<Exception>(() => ScratchDryRun.Of(_cache, proposal));

        Assert.Contains("wholly empty", failure.ToString(), StringComparison.Ordinal);
        Assert.True(_cache.ServiceLocator.ObjectRepository.IsValidObjectId(seed.SenseId));
    }

    [Fact]
    public void AnAnalysisAnotherSenseStillUsesIsNotDeleted()
    {
        var seed = EmptySenseSeed.Add(_cache, "shared", secondSenseSharesMsa: true);
        var refused = Assert.ThrowsAny<Exception>(() =>
            ScratchDryRun.Of(_cache, Proposal(seed.SenseId, new { msa = Id(seed.MsaId) })));
        Assert.Contains("another sense", refused.ToString(), StringComparison.Ordinal);

        var proposal = Proposal(seed.SenseId, new { msa = (string?)null });
        var dryRun = ScratchDryRun.Of(_cache, proposal);
        ProposalApplier.Apply(_cache, proposal, dryRun.Anchor, "tester");

        var entry = _cache.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(seed.EntryId);
        Assert.Single(entry.SensesOS);
        Assert.Single(entry.MorphoSyntaxAnalysesOC);
    }

    [Fact]
    public void ThePayloadIsClosedAndRequiresTheAnalysisField()
    {
        using var unknown = JsonDocument.Parse("{\"msa\":null,\"force\":true}");
        using var missing = JsonDocument.Parse("{}");
        Assert.ThrowsAny<Exception>(() => LexSenseDeletePayload.Parse(unknown.RootElement));
        Assert.ThrowsAny<Exception>(() => LexSenseDeletePayload.Parse(missing.RootElement));
    }

    private static string Id(Guid guid) => CanonicalId.FromGuid(guid).Value;

    private static Proposal Proposal(Guid sense, object after)
    {
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(after));
        return new Proposal(new Dictionary<string, string> { ["lexical"] = "1.0" }, CanonicalId.Mint(), null,
            [new OperationEnvelope(CanonicalId.Mint(), LexSenseDeleteOperationKinds.DeleteSense, null,
                CanonicalId.FromGuid(sense), json.RootElement.Clone())]);
    }
}
