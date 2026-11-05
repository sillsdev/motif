using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using System.Text.Json;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Model.Snapshot;
using SIL.Motif.Projection;
using SIL.Motif.Runner.Apply;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Execution;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Composers;

[Collection(LcmCacheParallelCollections.Group1)]
public sealed class ProhibitionMsaCleanupTests(PristineProjectFixture pristine) : IDisposable
{
    private readonly LcmCache _cache = pristine.NewScratch();
    public void Dispose() => _cache.Dispose();

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void NativeCleanupIsReviewedBeforeApplyAndSurvivesReopen(bool delete, bool rest, bool withBundle)
    {
        var (rule, old, replacement) = Seed(rest);
        var oldId = Id(old);
        var ruleId = Id(rule);
        var bundle = withBundle ? Bundle(old) : null;
        var bundleId = bundle is null ? (CanonicalId?)null : Id(bundle);
        var proposal = Proposal(rule, replacement, delete, rest);
        var dry = ScratchDryRun.Of(_cache, proposal);
        Assert.True(old.IsValidObject);
        if (bundle is not null) Assert.Equal(old, bundle.MsaRA);
        var deleted = Assert.Single(dry.ExpectedEffects, effect => effect.CanonicalId == oldId);
        Assert.NotEmpty(deleted.Before);
        Assert.Empty(deleted.After);
        Assert.Contains("Delete grammatical analysis", deleted.Preview!.Value.GetProperty("summary").GetString());
        if (bundleId is { } id)
        {
            var cleared = Assert.Single(dry.ExpectedEffects, effect => effect.CanonicalId == id && effect.Field == SnapshotFields.WfiMorphBundleMsa);
            Assert.Contains(oldId.Value, cleared.Before.Values);
            Assert.Empty(cleared.After);
            Assert.Contains("Delete word-analysis reference", cleared.Preview!.Value.GetProperty("summary").GetString());
        }
        var projected = EffectProjectionBuilder.Build(dry.ExpectedEffects, proposal);
        Assert.All(projected, effect => Assert.Contains(proposal.Operations[0].OperationId.Value, effect.OperationIds));
        var receipt = ProposalApplier.Apply(_cache, proposal, dry.Anchor, "cleanup-tests");
        Assert.Equal(dry.EffectDigest, receipt.EffectDigest);
        Assert.False(Exists(_cache, oldId));
        Assert.Equal(!delete, Exists(_cache, ruleId));
        if (bundle is not null) Assert.Null(bundle.MsaRA);
        new FwDataProjectLoader().Save(_cache);
        using var reopened = new FwDataProjectLoader().LoadScratchCache(_cache.ProjectId.Path);
        Assert.False(Exists(reopened, oldId));
        Assert.Equal(!delete, Exists(reopened, ruleId));
        if (bundleId is { } saved) Assert.Null(((IWfiMorphBundle)reopened.ServiceLocator.ObjectRepository.GetObject(saved.ToGuid())).MsaRA);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void NewBundleOrSenseAfterDryRunRefusesWithoutCleanup(bool delete, bool sense)
    {
        var (rule, old, replacement) = Seed(false);
        var proposal = Proposal(rule, replacement, delete, false);
        var dry = ScratchDryRun.Of(_cache, proposal);
        if (sense)
            NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () => ((ILexEntry)old.Owner).SensesOS[0].MorphoSyntaxAnalysisRA = old);
        else Bundle(old);
        Assert.Throws<PreconditionRefusal>(() => ProposalApplier.Apply(_cache, proposal, dry.Anchor, "cleanup-tests"));
        Assert.True(old.IsValidObject);
        Assert.Equal(old, rule.FirstMorphemeRA);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DifferentEffectsRefuseAndRollBackCleanup(bool delete)
    {
        var (rule, old, replacement) = Seed(false);
        var bundle = Bundle(old);
        var proposal = Proposal(rule, replacement, delete, false);
        var dry = ScratchDryRun.Of(_cache, proposal);
        Assert.Throws<PreconditionRefusal>(() => ProposalApplier.Apply(_cache, proposal,
            dry.Anchor with { EffectDigest = "sha256:" + new string('0', 64) }, "cleanup-tests"));
        Assert.True(old.IsValidObject);
        Assert.True(rule.IsValidObject);
        Assert.Equal(old, bundle.MsaRA);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void SenseOrAnotherProhibitionKeepsTheAnalysisAndItsBundle(bool delete, bool sense)
    {
        var (rule, old, replacement) = Seed(false);
        var bundle = Bundle(old);
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            if (sense) ((ILexEntry)old.Owner).SensesOS[0].MorphoSyntaxAnalysisRA = old;
            else
            {
                var keeper = _cache.ServiceLocator.GetInstance<IMoMorphAdhocProhibFactory>().Create();
                _cache.LangProject.MorphologicalDataOA.AdhocCoProhibitionsOC.Add(keeper);
                keeper.FirstMorphemeRA = old;
                keeper.RestOfMorphsRS.Add(rule.RestOfMorphsRS[0]);
            }
        });
        var proposal = Proposal(rule, replacement, delete, false);
        var dry = ScratchDryRun.Of(_cache, proposal);
        Assert.DoesNotContain(dry.ExpectedEffects, effect => effect.CanonicalId == Id(old) || effect.CanonicalId == Id(bundle));
        var receipt = ProposalApplier.Apply(_cache, proposal, dry.Anchor, "cleanup-tests");
        Assert.Equal(dry.EffectDigest, receipt.EffectDigest);
        Assert.True(old.IsValidObject);
        Assert.Equal(old, bundle.MsaRA);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EveryClearedBundleIsListedAndRemovingOneAfterDryRunIsStale(bool delete)
    {
        var (rule, old, replacement) = Seed(false);
        var bundles = Enumerable.Range(0, 12).Select(_ => Bundle(old)).ToArray();
        var proposal = Proposal(rule, replacement, delete, false);
        var dry = ScratchDryRun.Of(_cache, proposal);
        foreach (var bundle in bundles)
            Assert.Contains(dry.ExpectedEffects, effect => effect.CanonicalId == Id(bundle) && effect.After.Count == 0);
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () => bundles[0].MsaRA = replacement);
        Assert.Throws<PreconditionRefusal>(() => ProposalApplier.Apply(_cache, proposal, dry.Anchor, "cleanup-tests"));
        Assert.True(old.IsValidObject);
        Assert.True(rule.IsValidObject);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ComponentAnalysisCleanupIsAlsoReviewedAndSaved(bool delete)
    {
        var (rule, old, replacement) = Seed(false);
        var component = _cache.ServiceLocator.GetInstance<IMoUnclassifiedAffixMsaFactory>().Create();
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            ((ILexEntry)old.Owner).MorphoSyntaxAnalysesOC.Add(component);
            old.ComponentsRS.Add(component);
        });
        var componentId = Id(component);
        var bundle = Bundle(component);
        var bundleId = Id(bundle);
        var proposal = Proposal(rule, replacement, delete, false);
        var dry = ScratchDryRun.Of(_cache, proposal);
        Assert.Contains(dry.ExpectedEffects, effect => effect.CanonicalId == componentId && effect.After.Count == 0);
        Assert.Contains(dry.ExpectedEffects, effect => effect.CanonicalId == bundleId && effect.After.Count == 0);
        var receipt = ProposalApplier.Apply(_cache, proposal, dry.Anchor, "cleanup-tests");
        Assert.Equal(dry.EffectDigest, receipt.EffectDigest);
        new FwDataProjectLoader().Save(_cache);
        using var reopened = new FwDataProjectLoader().LoadScratchCache(_cache.ProjectId.Path);
        Assert.False(Exists(reopened, componentId));
        Assert.Null(((IWfiMorphBundle)reopened.ServiceLocator.ObjectRepository.GetObject(bundleId.ToGuid())).MsaRA);
    }

    [Fact]
    public void NativeReferenceDeletionCannotShareASlotWithAnAuthoredBundleWrite()
    {
        var (rule, old, replacement) = Seed(false);
        var bundle = Bundle(replacement);
        var write = new OperationEnvelope(CanonicalId.Mint(), MsaReferenceRetargetOperationKinds.SetBundleMsa,
            target: Id(bundle), after: JsonSerializer.SerializeToElement(new { from = Id(replacement).Value, to = Id(old).Value }));
        var deletion = Assert.Single(DeleteAdhocProhibitionComposer.Build(_cache, new(Id(rule), AdhocReferenceState.Read(rule))));
        deletion = new(deletion.OperationId, deletion.Kind, target: deletion.Target, after: deletion.After,
            dependsOn: [new OperationDependency(write.OperationId)]);
        var proposal = new Proposal(new Dictionary<string, string> { ["grammar"] = "1.0" }, CanonicalId.Mint(), null, [write, deletion]);
        var error = Assert.Throws<ContractParseException>(() => ScratchDryRun.Of(_cache, proposal));
        Assert.Contains("same target or field slot", error.Message);
        Assert.True(old.IsValidObject);
        Assert.Equal(replacement, bundle.MsaRA);
    }

    private (IMoMorphAdhocProhib Rule, IMoMorphSynAnalysis Old, IMoMorphSynAnalysis Replacement) Seed(bool rest)
    {
        var entries = _cache.LangProject.LexDbOA.Entries.Take(2).ToArray();
        var old = _cache.ServiceLocator.GetInstance<IMoUnclassifiedAffixMsaFactory>().Create();
        var replacement = _cache.ServiceLocator.GetInstance<IMoUnclassifiedAffixMsaFactory>().Create();
        var rule = _cache.ServiceLocator.GetInstance<IMoMorphAdhocProhibFactory>().Create();
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            entries[0].MorphoSyntaxAnalysesOC.Add(old);
            entries[0].MorphoSyntaxAnalysesOC.Add(replacement);
            _cache.LangProject.MorphologicalDataOA.AdhocCoProhibitionsOC.Add(rule);
            rule.FirstMorphemeRA = rest ? entries[1].MorphoSyntaxAnalysesOC.First() : old;
            rule.RestOfMorphsRS.Add(rest ? old : entries[1].MorphoSyntaxAnalysesOC.First());
        });
        return (rule, old, replacement);
    }

    private IWfiMorphBundle Bundle(IMoMorphSynAnalysis msa)
    {
        IWfiMorphBundle bundle = null!;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            var word = _cache.ServiceLocator.GetInstance<IWfiWordformFactory>().Create(TsStringUtils.MakeString("reviewword", _cache.DefaultVernWs));
            var analysis = _cache.ServiceLocator.GetInstance<IWfiAnalysisFactory>().Create();
            word.AnalysesOC.Add(analysis);
            bundle = _cache.ServiceLocator.GetInstance<IWfiMorphBundleFactory>().Create();
            analysis.MorphBundlesOS.Add(bundle);
            bundle.MsaRA = msa;
            bundle.MorphRA = ((ILexEntry)msa.Owner).LexemeFormOA;
        });
        return bundle;
    }

    private Proposal Proposal(IMoMorphAdhocProhib rule, IMoMorphSynAnalysis replacement, bool delete, bool rest)
    {
        var before = AdhocReferenceState.Read(rule);
        var operations = delete ? DeleteAdhocProhibitionComposer.Build(_cache, new(Id(rule), before)) :
            EditAdhocProhibitionReferencesComposer.Build(_cache, new(Id(rule), before,
                rest ? before with { Rest = [Id(replacement)] } : before with { First = Id(replacement) }));
        return new(new Dictionary<string, string> { ["grammar"] = "1.0" }, CanonicalId.Mint(), null, operations);
    }

    private static CanonicalId Id(ICmObject item) => CanonicalId.FromGuid(item.Guid);
    private static bool Exists(LcmCache cache, CanonicalId id) => cache.ServiceLocator.ObjectRepository.IsValidObjectId(id.ToGuid());
}
