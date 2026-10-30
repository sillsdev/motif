using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Model.Snapshot;
using SIL.Motif.Runner.Apply;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Composers;

[Collection(LcmCacheParallelCollections.Group3)]
public sealed class DeleteSenseTests : IDisposable
{
    private readonly LcmCache _cache;
    private readonly SeededProject _seed;

    public DeleteSenseTests(PristineProjectFixture pristine)
    {
        _cache = pristine.NewScratch();
        _seed = pristine.Seed;
    }

    public void Dispose()
    {
        if (!_cache.IsDisposed) _cache.Dispose();
    }

    [Fact]
    public void ASenseIsDeletedWithItsSubsensesAndExamplesAndItsOrphanedAnalysisIsRemoved()
    {
        var root = Root("bisagra", sharedAnalysis: false, "hinge", "duplicate");
        ILexSense sub = null!;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            var sense = Sense(root.Senses[1]);
            sense.Definition.set_String(_cache.DefaultAnalWs, "a duplicate");
            sense.ExamplesOS.Add(_cache.ServiceLocator.GetInstance<ILexExampleSentenceFactory>().Create());
            sub = _cache.ServiceLocator.GetInstance<ILexSenseFactory>().Create();
            sense.SensesOS.Add(sub);
            sub.MorphoSyntaxAnalysisRA = sense.MorphoSyntaxAnalysisRA;
        });
        var subId = sub.Guid;

        var operations = Compose(root.Senses[1], root.Entry);
        var operation = Assert.Single(operations);
        var payload = LexSenseRemovePayload.Parse(operation.After!.Value);
        Assert.True(payload.TopLevel);
        Assert.Equal(root.OtherMsa!.Value, Assert.Single(payload.Msas).ToGuid());
        ApplyAll(operations);

        Assert.Equal([root.Senses[0]], Entry(root.Entry).SensesOS.Select(sense => sense.Guid));
        Assert.False(Repository.IsValidObjectId(root.Senses[1]));
        Assert.False(Repository.IsValidObjectId(subId));
        Assert.False(Repository.IsValidObjectId(root.OtherMsa!.Value));
        Assert.Equal(root.Msa, Assert.Single(Entry(root.Entry).MorphoSyntaxAnalysesOC).Guid);
    }

    [Fact]
    public void ASharedAnalysisIsKeptBecauseARemainingSenseUsesIt()
    {
        var root = Root("ayu", sharedAnalysis: true, "tree", "kaayu");
        var operations = Compose(root.Senses[1], root.Entry);
        Assert.Empty(LexSenseRemovePayload.Parse(operations[0].After!.Value).Msas);
        ApplyAll(operations);

        Assert.Equal(root.Msa, Assert.Single(Entry(root.Entry).MorphoSyntaxAnalysesOC).Guid);
        Assert.Equal(root.Msa, Entry(root.Entry).SensesOS.Single().MorphoSyntaxAnalysisRA.Guid);
    }

    [Fact]
    public void AnOrphanedAnalysisThatAMorphBundleStillRefersToBlocksTheDeletion()
    {
        var root = Root("bisagra", sharedAnalysis: false, "hinge", "duplicate");
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            var wordform = _cache.ServiceLocator.GetInstance<IWfiWordformFactory>().Create();
            var analysis = _cache.ServiceLocator.GetInstance<IWfiAnalysisFactory>().Create();
            wordform.AnalysesOC.Add(analysis);
            var bundle = _cache.ServiceLocator.GetInstance<IWfiMorphBundleFactory>().Create();
            analysis.MorphBundlesOS.Add(bundle);
            bundle.MsaRA = (IMoMorphSynAnalysis)Repository.GetObject(root.OtherMsa!.Value);
        });
        var refused = Assert.Throws<InvalidOperationException>(() => Compose(root.Senses[1], root.Entry));
        Assert.Contains("WfiMorphBundle", refused.Message, StringComparison.Ordinal);
        Assert.Contains("analysis", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheOnlySenseOfAnEntryIsRefusedAtComposeAndAtApply()
    {
        var solo = Root("solo", sharedAnalysis: true, "alone");
        Assert.Contains("only sense", Assert.Throws<InvalidOperationException>(() =>
            Compose(solo.Senses[0], solo.Entry)).Message, StringComparison.Ordinal);

        var duo = Root("duo", sharedAnalysis: true, "one", "two");
        var operations = Compose(duo.Senses[1], duo.Entry);
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () => Sense(duo.Senses[0]).Delete());
        Assert.Contains("only sense", Assert.ThrowsAny<Exception>(() => ScratchDryRun.Of(_cache, Proposal(operations))).ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void ASenseThatARelationAMorphBundleOrAComponentReferencesIsRefusedNamingTheReferrer()
    {
        var root = Root("tuig", sharedAnalysis: true, "year", "annual", "yearly", "spare");
        var other = Root("other", sharedAnalysis: true, "x", "y");
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            var wordform = _cache.ServiceLocator.GetInstance<IWfiWordformFactory>().Create();
            var analysis = _cache.ServiceLocator.GetInstance<IWfiAnalysisFactory>().Create();
            wordform.AnalysesOC.Add(analysis);
            var bundle = _cache.ServiceLocator.GetInstance<IWfiMorphBundleFactory>().Create();
            analysis.MorphBundlesOS.Add(bundle);
            bundle.SenseRA = Sense(root.Senses[1]);

            var type = _cache.ServiceLocator.GetInstance<ILexRefTypeFactory>().Create();
            _cache.LangProject.LexDbOA.ReferencesOA ??= _cache.ServiceLocator.GetInstance<ICmPossibilityListFactory>().Create();
            _cache.LangProject.LexDbOA.ReferencesOA.PossibilitiesOS.Add(type);
            type.Name.set_String(_cache.DefaultAnalWs, "Synonyms");
            var relation = _cache.ServiceLocator.GetInstance<ILexReferenceFactory>().Create();
            type.MembersOC.Add(relation);
            relation.TargetsRS.Add(Sense(root.Senses[2]));
            relation.TargetsRS.Add(Sense(other.Senses[0]));

            var entryRef = _cache.ServiceLocator.GetInstance<ILexEntryRefFactory>().Create();
            Entry(other.Entry).EntryRefsOS.Add(entryRef);
            entryRef.ComponentLexemesRS.Add(Sense(root.Senses[3]));
        });

        var bundleRefusal = Assert.Throws<InvalidOperationException>(() => Compose(root.Senses[1], root.Entry));
        Assert.Contains("WfiMorphBundle", bundleRefusal.Message, StringComparison.Ordinal);
        var relationRefusal = Assert.Throws<InvalidOperationException>(() => Compose(root.Senses[2], root.Entry));
        Assert.Contains("Synonyms", relationRefusal.Message, StringComparison.Ordinal);
        var componentRefusal = Assert.Throws<InvalidOperationException>(() => Compose(root.Senses[3], root.Entry));
        Assert.Contains("LexEntryRef", componentRefusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ASubsenseIsDeletedAndTheParentKeepsItsOtherSubsensesAndItsAnalysis()
    {
        var root = Root("bisagra", sharedAnalysis: true, "hinge", "joint");
        var subs = new List<Guid>();
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            var parent = Sense(root.Senses[0]);
            foreach (var gloss in new[] { "even, including", "even so" })
            {
                var sub = _cache.ServiceLocator.GetInstance<ILexSenseFactory>().Create();
                parent.SensesOS.Add(sub);
                sub.Gloss.set_String(_cache.DefaultAnalWs, gloss);
                sub.MorphoSyntaxAnalysisRA = parent.MorphoSyntaxAnalysisRA;
                subs.Add(sub.Guid);
            }
        });
        var operations = Compose(subs[0], root.Senses[0]);
        var payload = LexSenseRemovePayload.Parse(Assert.Single(operations).After!.Value);
        Assert.False(payload.TopLevel);
        Assert.Empty(payload.Msas);
        var proposal = Proposal(operations);
        var dryRun = ScratchDryRun.Of(_cache, proposal);
        var effect = Assert.Single(dryRun.ExpectedEffects, e => e.Field == SnapshotFields.LexSenseSubsenses);
        Assert.Equal(2, effect.Before.Count);
        Assert.Equal(subs[1], CanonicalId.Parse(Assert.Single(effect.After).Value).ToGuid());
        Assert.Contains("even, including", effect.Preview!.Value.GetRawText(), StringComparison.Ordinal);
        ApplyAll(operations);

        Assert.Equal([subs[1]], Sense(root.Senses[0]).SensesOS.Select(sub => sub.Guid));
        Assert.Equal(2, Entry(root.Entry).SensesOS.Count);
        Assert.Equal(root.Msa, Assert.Single(Entry(root.Entry).MorphoSyntaxAnalysesOC).Guid);
    }

    [Fact]
    public void AWrongOwnerOrAStaleOwnerIsRefused()
    {
        var root = Root("ayu", sharedAnalysis: true, "tree", "kaayu", "maayu");
        var other = Root("tuig", sharedAnalysis: true, "year", "annual");
        Assert.Contains("different owner", Assert.Throws<InvalidOperationException>(() =>
            Compose(root.Senses[1], other.Entry)).Message, StringComparison.Ordinal);
        Assert.Contains("must be a sense", Assert.Throws<InvalidOperationException>(() =>
            Compose(root.Entry, root.Entry)).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DryRunEffectsListEveryDeletionAndTheFootprintChangesNothing()
    {
        var root = Root("bisagra", sharedAnalysis: false, "hinge", "duplicate");
        var operations = Compose(root.Senses[1], root.Entry);
        var proposal = Proposal(operations);
        var digestBefore = FootprintProbe.ComputeCurrentFootprintDigest(_cache, proposal);

        var dryRun = ScratchDryRun.Of(_cache, proposal);
        var senses = Assert.Single(dryRun.ExpectedEffects, e => e.Field == SnapshotFields.LexEntrySenses &&
                                                                e.CanonicalId == Id(root.Entry));
        Assert.Equal(2, senses.Before.Count);
        Assert.Single(senses.After);
        var preview = senses.Preview!.Value.GetRawText();
        Assert.Contains("duplicate", preview, StringComparison.Ordinal);
        Assert.Contains("deletedAnalyses", preview, StringComparison.Ordinal);
        Assert.Contains(Id(root.OtherMsa!.Value).Value, preview, StringComparison.Ordinal);
        var analyses = Assert.Single(dryRun.ExpectedEffects, e => e.Field == SnapshotFields.LexEntryMorphoSyntaxAnalyses &&
                                                                  e.CanonicalId == Id(root.Entry));
        Assert.Equal(2, analyses.Before.Count);
        Assert.Single(analyses.After);

        Assert.Equal(2, Entry(root.Entry).SensesOS.Count);
        Assert.Equal(digestBefore, FootprintProbe.ComputeCurrentFootprintDigest(_cache, proposal));
    }

    [Fact]
    public void ThePayloadIsClosedAndNamesEveryIdentityItNeeds()
    {
        using var unknown = JsonDocument.Parse(
            $"{{\"owner\":\"{Id(Guid.NewGuid()).Value}\",\"topLevel\":true,\"entry\":\"{Id(Guid.NewGuid()).Value}\",\"msas\":[],\"force\":true}}");
        using var missing = JsonDocument.Parse("{}");
        Assert.ThrowsAny<Exception>(() => LexSenseRemovePayload.Parse(unknown.RootElement));
        Assert.ThrowsAny<Exception>(() => LexSenseRemovePayload.Parse(missing.RootElement));
    }

    private void ApplyAll(IReadOnlyList<OperationEnvelope> operations)
    {
        var proposal = Proposal(operations);
        ProposalApplier.Apply(_cache, proposal, ScratchDryRun.Of(_cache, proposal).Anchor, "delete-sense-tests");
    }

    private IReadOnlyList<OperationEnvelope> Compose(Guid target, Guid owner) =>
        DeleteSenseComposer.Build(_cache, new DeleteSenseIntent(Id(target), Id(owner)));

    private sealed record RootSeed(Guid Entry, Guid Msa, Guid? OtherMsa, Guid[] Senses);

    /// <summary>A root entry; its last sense has its own analysis unless the senses share one.</summary>
    private RootSeed Root(string form, bool sharedAnalysis, params string[] glosses)
    {
        var services = _cache.ServiceLocator;
        var senses = new List<Guid>();
        Guid entryId = default, msaId = default;
        Guid? otherId = null;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            var entry = services.GetInstance<ILexEntryFactory>().Create();
            entryId = entry.Guid;
            var lexemeForm = services.GetInstance<IMoStemAllomorphFactory>().Create();
            entry.LexemeFormOA = lexemeForm;
            lexemeForm.MorphTypeRA = services.GetInstance<IMoMorphTypeRepository>().GetObject(MoMorphTypeTags.kguidMorphStem);
            lexemeForm.Form.set_String(_cache.DefaultVernWs, form);
            var pos = services.GetInstance<IPartOfSpeechRepository>().GetObject(_seed.PartOfSpeechId);
            var msa = StemMsa(entry, pos);
            msaId = msa.Guid;
            for (var i = 0; i < glosses.Length; i++)
            {
                var sense = services.GetInstance<ILexSenseFactory>().Create();
                entry.SensesOS.Add(sense);
                sense.Gloss.set_String(_cache.DefaultAnalWs, glosses[i]);
                var last = i == glosses.Length - 1 && i > 0;
                if (last && !sharedAnalysis)
                {
                    var other = StemMsa(entry, pos);
                    otherId = other.Guid;
                    sense.MorphoSyntaxAnalysisRA = other;
                }
                else sense.MorphoSyntaxAnalysisRA = msa;
                senses.Add(sense.Guid);
            }
        });
        return new RootSeed(entryId, msaId, otherId, senses.ToArray());
    }

    private IMoStemMsa StemMsa(ILexEntry entry, IPartOfSpeech pos)
    {
        var msa = _cache.ServiceLocator.GetInstance<IMoStemMsaFactory>().Create();
        entry.MorphoSyntaxAnalysesOC.Add(msa);
        msa.PartOfSpeechRA = pos;
        return msa;
    }

    private ILexSense Sense(Guid id) => _cache.ServiceLocator.GetInstance<ILexSenseRepository>().GetObject(id);

    private ILexEntry Entry(Guid id) => _cache.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(id);

    private ICmObjectRepository Repository => _cache.ServiceLocator.GetInstance<ICmObjectRepository>();

    private static CanonicalId Id(Guid guid) => CanonicalId.FromGuid(guid);

    private static Proposal Proposal(IReadOnlyList<OperationEnvelope> operations) => new(
        new Dictionary<string, string> { ["lexical"] = "1.0", ["grammar"] = "1.0" }, CanonicalId.Mint(), null,
        operations);
}
