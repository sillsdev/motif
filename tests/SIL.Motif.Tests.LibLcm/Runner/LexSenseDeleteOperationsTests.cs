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

    [Fact]
    public void ADeletionCanBeReversedFromItsRecordedBeforeStateToTheOriginal()
    {
        var seed = EmptySenseSeed.Add(_cache, "round");
        var deletion = Proposal(seed.SenseId, new { msa = Id(seed.MsaId) });
        var dryRun = ScratchDryRun.Of(_cache, deletion);
        var recorded = dryRun.ExpectedEffects.First().Preview!.Value;
        ProposalApplier.Apply(_cache, deletion, dryRun.Anchor, "tester");
        var repository = _cache.ServiceLocator.ObjectRepository;
        Assert.False(repository.IsValidObjectId(seed.SenseId));
        Assert.False(repository.IsValidObjectId(seed.MsaId));

        var sense = recorded.GetProperty("deletedSense");
        var analysis = recorded.GetProperty("deletedAnalysis");
        var entry = CanonicalId.Parse(sense.GetProperty("entry").GetString()!);
        var senseId = CanonicalId.Parse(sense.GetProperty("id").GetString()!);
        var msaId = CanonicalId.Parse(analysis.GetProperty("id").GetString()!);
        Assert.Equal("none", analysis.GetProperty("content").GetString());
        Assert.Equal(JsonValueKind.Null, sense.GetProperty("after").ValueKind);
        using var empty = JsonDocument.Parse("{}");
        using var stem = JsonDocument.Parse(JsonSerializer.Serialize(new { @class = analysis.GetProperty("class").GetString() }));
        using var link = JsonDocument.Parse(JsonSerializer.Serialize(new { @ref = msaId.Value }));
        var reversal = new Proposal(new Dictionary<string, string> { ["lexical"] = "1.0" }, CanonicalId.Mint(), null,
        [
            new OperationEnvelope(CanonicalId.Mint(), "lexical/lexEntry/createSenses", senseId, entry, empty.RootElement.Clone()),
            new OperationEnvelope(CanonicalId.Mint(), "lexical/lexEntry/createMorphoSyntaxAnalyses", msaId, entry,
                stem.RootElement.Clone()),
            new OperationEnvelope(CanonicalId.Mint(), LexSenseMorphoSyntaxAnalysisOperationKinds.SetMorphoSyntaxAnalysis,
                null, senseId, link.RootElement.Clone()),
        ]);
        var reverseRun = ScratchDryRun.Of(_cache, reversal);
        ProposalApplier.Apply(_cache, reversal, reverseRun.Anchor, "tester");

        var restoredEntry = _cache.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(seed.EntryId);
        var restoredSense = Assert.Single(restoredEntry.SensesOS);
        var restoredMsa = Assert.IsAssignableFrom<IMoStemMsa>(Assert.Single(restoredEntry.MorphoSyntaxAnalysesOC));
        Assert.Equal(seed.SenseId, restoredSense.Guid);
        Assert.Equal(seed.MsaId, restoredMsa.Guid);
        Assert.Same(restoredMsa, restoredSense.MorphoSyntaxAnalysisRA);
        Assert.Null(restoredMsa.PartOfSpeechRA);
        Assert.Null(LexSenseEmptiness.FirstPopulatedField(_cache, restoredSense));
        Assert.Null(LexSenseEmptiness.FirstPopulatedField(_cache, restoredMsa));
    }

    [Fact]
    public void ASenseAParserBundleRefersToIsRefused() =>
        AssertRefused("refer to", (seed, entry) => Bundle(seed, sense: true));

    [Fact]
    public void AnAnalysisAParserBundleRefersToIsRefused() =>
        AssertRefused("analysis", (seed, entry) => Bundle(seed, sense: false));

    [Fact]
    public void ASenseAVariantEntryReferenceRefersToIsRefused() =>
        AssertRefused("refer to", (seed, entry) => NonUndo(() =>
        {
            var variant = _cache.ServiceLocator.GetInstance<ILexEntryFactory>().Create();
            var reference = _cache.ServiceLocator.GetInstance<ILexEntryRefFactory>().Create();
            variant.EntryRefsOS.Add(reference);
            reference.RefType = LexEntryRefTags.krtVariant;
            reference.ComponentLexemesRS.Add(entry.SensesOS[0]);
        }));

    [Fact]
    public void ASenseWithASubSenseIsRefused() =>
        AssertRefused("wholly empty", (seed, entry) => NonUndo(() =>
            entry.SensesOS[0].SensesOS.Add(_cache.ServiceLocator.GetInstance<ILexSenseFactory>().Create())));

    [Fact]
    public void AnAnalysisWithAPartOfSpeechIsRefusedBecauseItsContentCouldNotBeRestored() =>
        AssertRefused("content", (seed, entry) => NonUndo(() =>
        {
            var category = _cache.ServiceLocator.GetInstance<IPartOfSpeechRepository>().AllInstances().First();
            ((IMoStemMsa)entry.MorphoSyntaxAnalysesOC.Single()).PartOfSpeechRA = category;
        }));

    [Fact]
    public void AStaleAnalysisIdIsRefused()
    {
        var seed = EmptySenseSeed.Add(_cache, "stale");
        var other = EmptySenseSeed.Add(_cache, "other");
        var failure = Assert.ThrowsAny<Exception>(() =>
            ScratchDryRun.Of(_cache, Proposal(seed.SenseId, new { msa = Id(other.MsaId) })));
        Assert.Contains("no longer points", failure.ToString(), StringComparison.Ordinal);
    }

    private void AssertRefused(string expected, Action<EmptySenseSeed, ILexEntry> arrange)
    {
        var seed = EmptySenseSeed.Add(_cache, "refused");
        arrange(seed, _cache.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(seed.EntryId));
        var failure = Assert.ThrowsAny<Exception>(() =>
            ScratchDryRun.Of(_cache, Proposal(seed.SenseId, new { msa = Id(seed.MsaId) })));
        Assert.Contains(expected, failure.ToString(), StringComparison.Ordinal);
        Assert.True(_cache.ServiceLocator.ObjectRepository.IsValidObjectId(seed.SenseId));
    }

    private void Bundle(EmptySenseSeed seed, bool sense) => NonUndo(() =>
    {
        var repository = _cache.ServiceLocator;
        var wordform = repository.GetInstance<IWfiWordformFactory>()
            .Create(SIL.LCModel.Core.Text.TsStringUtils.MakeString("w", _cache.DefaultVernWs));
        var analysis = repository.GetInstance<IWfiAnalysisFactory>().Create();
        wordform.AnalysesOC.Add(analysis);
        var bundle = repository.GetInstance<IWfiMorphBundleFactory>().Create();
        analysis.MorphBundlesOS.Add(bundle);
        if (sense) bundle.SenseRA = repository.GetInstance<ILexSenseRepository>().GetObject(seed.SenseId);
        else bundle.MsaRA = repository.GetInstance<IMoMorphSynAnalysisRepository>().GetObject(seed.MsaId);
    });

    private void NonUndo(Action action) => NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () => action());

    private static string Id(Guid guid) => CanonicalId.FromGuid(guid).Value;

    private static Proposal Proposal(Guid sense, object after)
    {
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(after));
        return new Proposal(new Dictionary<string, string> { ["lexical"] = "1.0" }, CanonicalId.Mint(), null,
            [new OperationEnvelope(CanonicalId.Mint(), LexSenseDeleteOperationKinds.DeleteSense, null,
                CanonicalId.FromGuid(sense), json.RootElement.Clone())]);
    }
}
