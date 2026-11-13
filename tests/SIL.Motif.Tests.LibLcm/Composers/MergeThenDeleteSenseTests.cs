using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Runner.Apply;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Execution;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using static SIL.Motif.Tests.Composers.AffixTestSupport;

namespace SIL.Motif.Tests.Composers;

/// <summary>One Proposal whose later intent names an object an earlier intent of the same Proposal moved.</summary>
[Collection(LcmCacheParallelCollections.Group3)]
public sealed class MergeThenDeleteSenseTests : IDisposable
{
    private readonly LcmCache _cache;
    private readonly CanonicalId _category;

    public MergeThenDeleteSenseTests(PristineProjectFixture pristine)
    {
        _cache = pristine.NewScratch();
        _category = CanonicalId.FromGuid(pristine.Seed.PartOfSpeechId);
    }

    public void Dispose()
    {
        if (!_cache.IsDisposed) _cache.Dispose();
    }

    [Fact]
    public void MergingAnEntryThenDeletingTheSenseItBroughtIsOneProposalThatDryRunsAndApplies()
    {
        var (survivor, merged) = (AddStem(_cache, "mag", _category), AddStem(_cache, "mag-", _category));
        var movedSense = Get<ILexEntry>(_cache, merged.Entry).SensesOS.Single().Guid;
        var operations = ComposeMergeThenDelete(survivor.Entry, merged.Entry, movedSense);
        Assert.Equal(2, operations.Count(operation => operation.Kind is LexEntryMergeOperationKinds.Merge
            or LexSenseRemoveOperationKinds.Remove));
        var proposal = Proposal(operations);

        var dryRun = ScratchDryRun.Of(_cache, proposal);
        Assert.NotEmpty(dryRun.ExpectedEffects);
        Assert.True(Exists(_cache, merged.Entry));
        Assert.Single(Get<ILexEntry>(_cache, survivor.Entry).SensesOS);

        ProposalApplier.Apply(_cache, proposal, dryRun.Anchor, "merge-then-delete-tests");

        Assert.False(Exists(_cache, merged.Entry));
        var kept = Assert.Single(Get<ILexEntry>(_cache, survivor.Entry).SensesOS);
        Assert.NotEqual(movedSense, kept.Guid);
        Assert.False(_cache.ServiceLocator.GetInstance<ICmObjectRepository>().IsValidObjectId(movedSense));
    }

    [Fact]
    public void ChangingTheMovedSenseAfterTheDryRunStillStopsTheApplyAsDrift()
    {
        var (survivor, merged) = (AddStem(_cache, "nag", _category), AddStem(_cache, "nag-", _category));
        var movedSense = Get<ILexEntry>(_cache, merged.Entry).SensesOS.Single().Guid;
        var proposal = Proposal(ComposeMergeThenDelete(survivor.Entry, merged.Entry, movedSense));
        var dryRun = ScratchDryRun.Of(_cache, proposal);

        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
            _cache.ServiceLocator.GetInstance<ILexSenseRepository>().GetObject(movedSense)
                .Gloss.set_String(_cache.DefaultAnalWs, "changed since the Dry Run"));

        var refusal = Assert.Throws<PreconditionRefusal>(() =>
            ProposalApplier.Apply(_cache, proposal, dryRun.Anchor, "merge-then-delete-tests"));
        Assert.Equal(PreconditionKind.FootprintDrift, refusal.Kind);
        Assert.True(Exists(_cache, merged.Entry));
    }

    /// <summary>Composes the delete on a file copy the merge was applied to, as compose-batch does.</summary>
    private IReadOnlyList<OperationEnvelope> ComposeMergeThenDelete(CanonicalId survivor, CanonicalId merged, Guid movedSense)
    {
        var merge = MergeLexicalEntriesComposer.Build(_cache, new MergeLexicalEntriesIntent(merged, survivor));
        new FwDataProjectLoader().Save(_cache);
        var root = Path.Combine(Path.GetTempPath(), "SIL.Motif.Tests.Scratch", Guid.NewGuid().ToString("N"));
        using var copy = new ScratchCacheFactory().CreateFromFileCopy(_cache.ProjectId.Path, root);
        try
        {
            NonUndoableUnitOfWorkHelper.Do(copy.ActionHandlerAccessor, () =>
            {
                foreach (var operation in merge)
                    OperationHandlerRegistry.Resolve(operation.Kind, "test").ApplyAndCaptureEffect(copy, operation, []);
            });
            var delete = DeleteSenseComposer.Build(copy,
                new DeleteSenseIntent(CanonicalId.FromGuid(movedSense), survivor));
            return [.. merge, .. delete];
        }
        finally
        {
            copy.Dispose();
            try { Directory.Delete(root, recursive: true); } catch { /* best effort */ }
        }
    }
}
