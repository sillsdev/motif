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

/// <summary>Whole-entry deletion: what goes, what is kept, and every reference that blocks it.</summary>
[Collection(LcmCacheParallelCollections.Group3)]
public sealed class DeleteLexicalEntryTests : IDisposable
{
    private readonly LcmCache _cache;
    private readonly CanonicalId _category;

    public DeleteLexicalEntryTests(PristineProjectFixture pristine)
    {
        _cache = pristine.NewScratch();
        _category = CanonicalId.FromGuid(pristine.Seed.PartOfSpeechId);
    }

    public void Dispose()
    {
        if (!_cache.IsDisposed) _cache.Dispose();
    }

    [Fact]
    public void AnUnreferencedEntryIsDeletedWithEverythingItOwnsAndOtherEntriesAreUntouched()
    {
        var doomed = AddStem(_cache, "tal", _category);
        var kept = AddStem(_cache, "bukid", _category);
        var owned = Get<ILexEntry>(_cache, doomed.Entry).AllOwnedObjects.Select(item => item.Guid).ToArray();
        Assert.NotEmpty(owned);
        var entries = _cache.LangProject.LexDbOA.Entries.Count();

        var operations = DeleteLexicalEntryComposer.Build(_cache, new DeleteLexicalEntryIntent(doomed.Entry));
        var operation = Assert.Single(operations);
        Assert.Equal(LexEntryRemoveOperationKinds.Remove, operation.Kind);
        var proposal = Proposal(operations);
        var dryRun = ScratchDryRun.Of(_cache, proposal);
        var effect = Assert.Single(dryRun.ExpectedEffects, e => e.Field == SnapshotFields.LexDbEntries);
        Assert.Equal(doomed.Entry.Value, Assert.Single(effect.Before).Value);
        Assert.Empty(effect.After);
        Assert.Contains("tal", effect.Preview!.Value.GetRawText(), StringComparison.Ordinal);
        Assert.Equal(entries, _cache.LangProject.LexDbOA.Entries.Count());

        ProposalApplier.Apply(_cache, proposal, dryRun.Anchor, "delete-entry-tests");

        Assert.Equal(entries - 1, _cache.LangProject.LexDbOA.Entries.Count());
        Assert.False(Exists(_cache, doomed.Entry));
        Assert.All(owned, guid => Assert.False(_cache.ServiceLocator.ObjectRepository.IsValidObjectId(guid)));
        Assert.True(Exists(_cache, kept.Entry));
        Assert.True(Exists(_cache, kept.Form));
    }

    [Fact]
    public void AnEntryThatAMorphBundleOfAParserAnalysisReferencesIsRefusedNamingTheReferrer()
    {
        var doomed = AddStem(_cache, "tal", _category);
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            var wordform = _cache.ServiceLocator.GetInstance<IWfiWordformFactory>().Create();
            var analysis = _cache.ServiceLocator.GetInstance<IWfiAnalysisFactory>().Create();
            wordform.AnalysesOC.Add(analysis);
            var bundle = _cache.ServiceLocator.GetInstance<IWfiMorphBundleFactory>().Create();
            analysis.MorphBundlesOS.Add(bundle);
            bundle.MsaRA = Get<IMoMorphSynAnalysis>(_cache, doomed.Msa);
        });

        var refused = Assert.Throws<InvalidOperationException>(() =>
            DeleteLexicalEntryComposer.Build(_cache, new DeleteLexicalEntryIntent(doomed.Entry)));
        Assert.Contains("WfiMorphBundle", refused.Message, StringComparison.Ordinal);
        Assert.Contains("tal", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEntryThatAnotherEntrysVariantReferenceNamesAsComponentIsRefused()
    {
        var doomed = AddStem(_cache, "tal", _category);
        var variant = AddStem(_cache, "tala", _category);
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            var reference = _cache.ServiceLocator.GetInstance<ILexEntryRefFactory>().Create();
            Get<ILexEntry>(_cache, variant.Entry).EntryRefsOS.Add(reference);
            reference.ComponentLexemesRS.Add(Get<ILexEntry>(_cache, doomed.Entry));
        });

        var refused = Assert.Throws<InvalidOperationException>(() =>
            DeleteLexicalEntryComposer.Build(_cache, new DeleteLexicalEntryIntent(doomed.Entry)));
        Assert.Contains("LexEntryRef", refused.Message, StringComparison.Ordinal);
        Assert.Contains("retarget or remove", refused.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReferencesInsideTheEntryDoNotBlockItAndAReferenceAddedAfterComposeBlocksApply()
    {
        var doomed = AddStem(_cache, "tal", _category);
        var operations = DeleteLexicalEntryComposer.Build(_cache, new DeleteLexicalEntryIntent(doomed.Entry));
        var sense = Get<ILexEntry>(_cache, doomed.Entry).SensesOS.Single();
        Assert.Equal(doomed.Msa.ToGuid(), sense.MorphoSyntaxAnalysisRA.Guid);

        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            var wordform = _cache.ServiceLocator.GetInstance<IWfiWordformFactory>().Create();
            var analysis = _cache.ServiceLocator.GetInstance<IWfiAnalysisFactory>().Create();
            wordform.AnalysesOC.Add(analysis);
            var bundle = _cache.ServiceLocator.GetInstance<IWfiMorphBundleFactory>().Create();
            analysis.MorphBundlesOS.Add(bundle);
            bundle.SenseRA = sense;
        });

        Assert.Contains("WfiMorphBundle", Assert.ThrowsAny<Exception>(() => ScratchDryRun.Of(_cache, Proposal(operations))).ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void ANonEntryOrMissingIdIsRefused()
    {
        var doomed = AddStem(_cache, "tal", _category);
        Assert.Throws<InvalidOperationException>(() =>
            DeleteLexicalEntryComposer.Build(_cache, new DeleteLexicalEntryIntent(doomed.Msa)));
        Assert.Throws<InvalidOperationException>(() =>
            DeleteLexicalEntryComposer.Build(_cache, new DeleteLexicalEntryIntent(CanonicalId.Mint())));
    }

    [Fact]
    public void AnEntryCanBeDeletedAndItsReplacementAuthoredInTheSameProposal()
    {
        var old = AddStem(_cache, "tal", _category);
        var replacement = AuthorLexicalEntryComposer.Build(_cache, new AuthorLexicalEntryIntent("tala", Vern(_cache),
            MorphType(SIL.LCModel.MoMorphTypeTags.kguidMorphStem), "tala", Anal(_cache),
            new LexicalEntryMsa(LexicalEntryMsaKind.Stem, _category)));
        var operations = DeleteLexicalEntryComposer.Build(_cache, new DeleteLexicalEntryIntent(old.Entry)).Concat(replacement).ToArray();
        ApplyOps(_cache, operations);
        Assert.False(Exists(_cache, old.Entry));
        Assert.Contains(_cache.LangProject.LexDbOA.Entries, entry => entry.LexemeFormOA.Form.VernacularDefaultWritingSystem.Text == "tala");
    }
}
