using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using SIL.Motif.LiveHost.Baselines;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Model.Snapshot;
using SIL.Motif.Runner.Apply;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Snapshotting;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Composers;

[Collection(LcmCacheParallelCollections.Group2)]
public sealed class LexiconOwningSnapshotTests(PristineProjectFixture pristine)
{
    [Fact]
    public void EntryCreateMembershipAndOrderedSensesAppearInMechanicalDiff()
    {
        using var cache = pristine.NewScratch();
        var lexicon = cache.LangProject.LexDbOA;
        var before = LexiconOwningSnapshots.Snapshot(cache, lexicon);
        var operations = AuthorLexicalEntryComposer.Build(cache,
            new("synthetic", cache.WritingSystemFactory.GetStrFromWs(cache.DefaultVernWs),
                CanonicalId.FromGuid(MoMorphTypeTags.kguidMorphStem), "SYNTHETIC",
                cache.WritingSystemFactory.GetStrFromWs(cache.DefaultAnalWs),
                new(LexicalEntryMsaKind.Stem, CanonicalId.FromGuid(pristine.Seed.PartOfSpeechId))));
        var proposal = new Proposal(new Dictionary<string, string> { ["lexical"] = "1.0", ["grammar"] = "1.0" },
            CanonicalId.Mint(), null, operations);
        ProposalApplier.Apply(cache, proposal, ScratchDryRun.Of(cache, proposal).Anchor, "lexicon-snapshot-tests");
        var created = operations.Single(op => op.Kind == "lexical/lexDb/createEntries").EntityId!.Value;
        var entry = (ILexEntry)cache.ServiceLocator.GetInstance<ICmObjectRepository>().GetObject(created.ToGuid());
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            entry.SensesOS.Add(cache.ServiceLocator.GetInstance<ILexSenseFactory>().Create()));
        var entrySnapshot = LexiconOwningSnapshots.Snapshot(cache, entry);
        Assert.Equal(entry.SensesOS.Select(sense => CanonicalId.FromGuid(sense.Guid).Value),
            entrySnapshot.AlternativesFields[SnapshotFields.LexEntrySenses].Values);
        var after = LexiconOwningSnapshots.Snapshot(cache, lexicon);
        var diff = SoundSystemSnapshots.Compare(new Dictionary<CanonicalId, ObjectSnapshot> { [before.CanonicalId] = before },
            new Dictionary<CanonicalId, ObjectSnapshot> { [after.CanonicalId] = after, [created] = entrySnapshot });
        foreach (var field in new[] { SnapshotFields.LexDbEntries, SnapshotFields.LexEntrySenses, SnapshotFields.LexEntryMorphoSyntaxAnalyses })
            Assert.Contains(diff, effect => effect.Field == field);
        Assert.Contains(created.Value, after.AlternativesFields[SnapshotFields.LexDbEntries].Values);
    }

    [Fact]
    public void MovingASenseBetweenEntriesChangesTheBaselineDigest()
    {
        using var cache = pristine.NewScratch();
        var entries = cache.ServiceLocator.GetInstance<ILexEntryRepository>().AllInstances().Take(2).ToArray();
        Assert.Equal(2, entries.Length);
        var sense = entries[0].SensesOS.First();
        var before = BaselineSemanticDigest.Compute(cache);
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () => entries[1].SensesOS.Add(sense));
        Assert.NotEqual(before, BaselineSemanticDigest.Compute(cache));
        Assert.Equal(entries[1].Guid, sense.Owner?.Guid);
    }
}
