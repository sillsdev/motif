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

[Collection(LcmCacheParallelCollections.Group3)]
public sealed class ReviewMergeGapTests : IDisposable
{
    private readonly LcmCache _cache;
    private readonly CanonicalId _category;

    public ReviewMergeGapTests(PristineProjectFixture pristine)
    {
        _cache = pristine.NewScratch();
        _category = CanonicalId.FromGuid(pristine.Seed.PartOfSpeechId);
    }

    public void Dispose() { if (!_cache.IsDisposed) _cache.Dispose(); }

    private IReadOnlyList<OperationEnvelope> MergeThen(CanonicalId survivor, CanonicalId merged,
        Func<LcmCache, IReadOnlyList<OperationEnvelope>> next)
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
            return [.. merge, .. next(copy)];
        }
        finally
        {
            copy.Dispose();
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public void MovedSenseNoteChangedAfterDryRunIsStillDrift()
    {
        var survivor = AddStem(_cache, "mag", _category);
        var merged = AddStem(_cache, "mag-", _category);
        var sense = Get<ILexEntry>(_cache, merged.Entry).SensesOS.Single().Guid;
        var ops = MergeThen(survivor.Entry, merged.Entry, _ =>
            [new OperationEnvelope(CanonicalId.Mint(), "lexical/lexSense/setGeneralNote",
                target: CanonicalId.FromGuid(sense),
                after: System.Text.Json.JsonSerializer.SerializeToElement(new { ws = "en", text = "planned note" }),
                rationale: "r")]);
        var proposal = Proposal(ops);
        var dryRun = ScratchDryRun.Of(_cache, proposal);
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
            _cache.ServiceLocator.GetInstance<ILexSenseRepository>().GetObject(sense)
                .GeneralNote.set_String(_cache.DefaultAnalWs, "someone else wrote this"));
        var ex = Record.Exception(() => ProposalApplier.Apply(_cache, proposal, dryRun.Anchor, "gap"));
        Assert.True(ex is PreconditionRefusal, ex?.ToString() ?? "applied with no refusal");
        Assert.Equal(PreconditionKind.FootprintDrift, ((PreconditionRefusal)ex!).Kind);
    }

    [Fact]
    public void MovedSenseGrammarNoteChangedAfterDryRunIsStillDrift()
    {
        var survivor = AddStem(_cache, "bukid", _category);
        var merged = AddStem(_cache, "bukid-", _category);
        var sense = Get<ILexEntry>(_cache, merged.Entry).SensesOS.Single().Guid;
        var ops = MergeThen(survivor.Entry, merged.Entry, _ =>
            [new OperationEnvelope(CanonicalId.Mint(), "lexical/lexSense/setGrammarNote",
                target: CanonicalId.FromGuid(sense),
                after: System.Text.Json.JsonSerializer.SerializeToElement(new { ws = "en", text = "planned" }),
                rationale: "r")]);
        var proposal = Proposal(ops);
        var dryRun = ScratchDryRun.Of(_cache, proposal);
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
            _cache.ServiceLocator.GetInstance<ILexSenseRepository>().GetObject(sense)
                .GrammarNote.set_String(_cache.DefaultAnalWs, "someone else wrote this"));
        var refusal = Assert.Throws<PreconditionRefusal>(() => ProposalApplier.Apply(_cache, proposal, dryRun.Anchor, "gap"));
        Assert.Equal(PreconditionKind.FootprintDrift, refusal.Kind);
    }
}
