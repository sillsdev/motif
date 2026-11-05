using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Model.Snapshot;
using SIL.Motif.Runner.Apply;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using static SIL.Motif.Tests.Composers.AffixTestSupport;

namespace SIL.Motif.Tests.Composers;

/// <summary>Entry merge: what moves onto the surviving entry, what is repointed, and what is refused.</summary>
[Collection(LcmCacheParallelCollections.Group3)]
public sealed class MergeLexicalEntriesTests : IDisposable
{
    private readonly LcmCache _cache;
    private readonly CanonicalId _category;

    public MergeLexicalEntriesTests(PristineProjectFixture pristine)
    {
        _cache = pristine.NewScratch();
        _category = CanonicalId.FromGuid(pristine.Seed.PartOfSpeechId);
    }

    public void Dispose()
    {
        if (!_cache.IsDisposed) _cache.Dispose();
    }

    [Fact]
    public void TheMergedEntryGoesAndItsSenseFormAndReferencesMoveToTheSurvivor()
    {
        var merged = AddStem(_cache, "tindog", _category);
        var survivor = AddStem(_cache, "tindug", _category);
        var other = AddStem(_cache, "bukid", _category);
        var mergedSense = Get<ILexEntry>(_cache, merged.Entry).SensesOS.Single().Guid;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            var reference = _cache.ServiceLocator.GetInstance<ILexEntryRefFactory>().Create();
            Get<ILexEntry>(_cache, other.Entry).EntryRefsOS.Add(reference);
            reference.ComponentLexemesRS.Add(Get<ILexEntry>(_cache, merged.Entry));
        });
        var entries = _cache.LangProject.LexDbOA.Entries.Count();

        var operations = MergeLexicalEntriesComposer.Build(_cache, new MergeLexicalEntriesIntent(merged.Entry, survivor.Entry));
        var operation = Assert.Single(operations);
        Assert.Equal(LexEntryMergeOperationKinds.Merge, operation.Kind);
        var proposal = Proposal(operations);
        var dryRun = ScratchDryRun.Of(_cache, proposal);
        var effect = Assert.Single(dryRun.ExpectedEffects, e => e.Field == SnapshotFields.LexDbEntries);
        Assert.Equal(merged.Entry.Value, Assert.Single(effect.Before).Value);
        Assert.Empty(effect.After);
        var preview = effect.Preview!.Value.GetRawText();
        Assert.Contains("tindog", preview, StringComparison.Ordinal);
        Assert.Contains("tindug", preview, StringComparison.Ordinal);
        Assert.Equal(entries, _cache.LangProject.LexDbOA.Entries.Count());

        ProposalApplier.Apply(_cache, proposal, dryRun.Anchor, "merge-entry-tests");

        Assert.Equal(entries - 1, _cache.LangProject.LexDbOA.Entries.Count());
        Assert.False(Exists(_cache, merged.Entry));
        var stays = Get<ILexEntry>(_cache, survivor.Entry);
        Assert.Equal(2, stays.SensesOS.Count);
        Assert.Contains(stays.SensesOS, sense => sense.Guid == mergedSense);
        Assert.Equal("tindug", stays.LexemeFormOA.Form.VernacularDefaultWritingSystem.Text);
        var alternate = Assert.Single(stays.AlternateFormsOS);
        Assert.Equal(merged.Form.ToGuid(), alternate.Guid);
        Assert.Equal("tindog", alternate.Form.VernacularDefaultWritingSystem.Text);
        Assert.Equal(survivor.Entry.ToGuid(),
            Assert.Single(Get<ILexEntry>(_cache, other.Entry).EntryRefsOS.Single().ComponentLexemesRS).Guid);
        Assert.True(Exists(_cache, other.Entry));
    }

    [Fact]
    public void AnEntryMergedIntoItselfIsRefusedByName()
    {
        var stem = AddStem(_cache, "tal", _category);
        var refused = Assert.Throws<InvalidOperationException>(() =>
            MergeLexicalEntriesComposer.Build(_cache, new MergeLexicalEntriesIntent(stem.Entry, stem.Entry)));
        Assert.Contains("itself", refused.Message, StringComparison.Ordinal);
        Assert.Contains("tal", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ANonEntryOrMissingIdIsRefused()
    {
        var stem = AddStem(_cache, "tal", _category);
        var other = AddStem(_cache, "bukid", _category);
        Assert.Throws<InvalidOperationException>(() =>
            MergeLexicalEntriesComposer.Build(_cache, new MergeLexicalEntriesIntent(stem.Msa, other.Entry)));
        Assert.Throws<InvalidOperationException>(() =>
            MergeLexicalEntriesComposer.Build(_cache, new MergeLexicalEntriesIntent(stem.Entry, other.Form)));
        Assert.Throws<InvalidOperationException>(() =>
            MergeLexicalEntriesComposer.Build(_cache, new MergeLexicalEntriesIntent(stem.Entry, CanonicalId.Mint())));
    }

    [Fact]
    public void AStemAndAPrefixAreRefusedNamingBothEntries()
    {
        var stem = AddStem(_cache, "tal", _category);
        var prefix = AddInflectionalPrefix(_cache, "tal", _category);
        var refused = Assert.Throws<InvalidOperationException>(() =>
            MergeLexicalEntriesComposer.Build(_cache, new MergeLexicalEntriesIntent(stem.Entry, prefix.Entry)));
        Assert.Contains("lexeme form", refused.Message, StringComparison.Ordinal);
        Assert.Contains(stem.Entry.Value, refused.Message, StringComparison.Ordinal);
        Assert.Contains(prefix.Entry.Value, refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TwoStemsOfDifferentMorphTypesAreRefusedNamingTheTypes()
    {
        var free = AddStem(_cache, "tal", _category);
        var bound = AddStem(_cache, "tala", _category);
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
            Get<ILexEntry>(_cache, bound.Entry).LexemeFormOA.MorphTypeRA = _cache.ServiceLocator
                .GetInstance<IMoMorphTypeRepository>().GetObject(MoMorphTypeTags.kguidMorphBoundStem));
        var refused = Assert.Throws<InvalidOperationException>(() =>
            MergeLexicalEntriesComposer.Build(_cache, new MergeLexicalEntriesIntent(free.Entry, bound.Entry)));
        Assert.Contains("morph type", refused.Message, StringComparison.Ordinal);
        Assert.Contains("tal", refused.Message, StringComparison.Ordinal);
        Assert.Contains("tala", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AMergeStaysRefusedIfTheProjectChangesAfterCompose()
    {
        var merged = AddStem(_cache, "tindog", _category);
        var survivor = AddStem(_cache, "tindug", _category);
        var operations = MergeLexicalEntriesComposer.Build(_cache, new MergeLexicalEntriesIntent(merged.Entry, survivor.Entry));
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () => Get<ILexEntry>(_cache, survivor.Entry).Delete());
        Assert.ThrowsAny<Exception>(() => ScratchDryRun.Of(_cache, Proposal(operations)));
    }
}
