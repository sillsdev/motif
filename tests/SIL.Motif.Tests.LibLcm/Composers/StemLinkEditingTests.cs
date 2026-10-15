using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.LiveHost.Baselines;
using SIL.Motif.Runner.Apply;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Model.Snapshot;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using static SIL.Motif.Tests.Composers.AffixTestSupport;

namespace SIL.Motif.Tests.Composers;

[Collection(LcmCacheParallelCollections.Group3)]
public sealed class StemLinkEditingTests(PristineProjectFixture pristine)
{
    [Fact]
    public void StemNameSetClearAndNoopUseExplicitState()
    {
        using var cache = pristine.NewScratch();
        var category = CanonicalId.FromGuid(pristine.Seed.PartOfSpeechId);
        var child = AddChildCategory(cache, category);
        var name = AddStemName(cache, category, "plural");
        var (_, _, stem) = AddStem(cache, "root", child);
        var set = EditStemAllomorphStemNameComposer.Build(cache, new(Alias(stem), null, Alias(name)));
        var operation = Assert.Single(set);
        Assert.Equal(MoStemAllomorphStemNameOperationKinds.SetStemName, operation.Kind);
        Assert.Equal(name.Value, operation.After!.Value.GetProperty("ref").GetString());
        ApplyOps(cache, set);
        Assert.Equal(name.ToGuid(), Get<IMoStemAllomorph>(cache, stem).StemNameRA.Guid);
        Assert.Empty(EditStemAllomorphStemNameComposer.Build(cache, new(stem, Alias(name), name)));
        var clear = EditStemAllomorphStemNameComposer.Build(cache, new(stem, name, null));
        Assert.Equal(MoStemAllomorphStemNameOperationKinds.ClearStemName, Assert.Single(clear).Kind);
        Assert.Empty(clear[0].After!.Value.EnumerateObject());
        ApplyOps(cache, clear);
        Assert.Null(Get<IMoStemAllomorph>(cache, stem).StemNameRA);
        Assert.Empty(EditStemAllomorphStemNameComposer.Build(cache, new(stem, null, null)));
    }

    [Fact]
    public void StemNameRefusalsLeaveLiveStateAlone()
    {
        using var cache = pristine.NewScratch();
        var category = CanonicalId.FromGuid(pristine.Seed.PartOfSpeechId);
        var child = AddChildCategory(cache, category);
        var own = AddStemName(cache, child, "own");
        var foreign = AddStemName(cache, AddCategory(cache, "foreign"), "foreign");
        var (_, msa, stem) = AddStem(cache, "root", category);
        var (_, _, childStem) = AddStem(cache, "kid", child);
        var (_, _, affix) = AddInflectionalPrefix(cache, "n", category);
        var before = BaselineSemanticDigest.Compute(cache);
        Assert.Throws<InvalidOperationException>(() => EditStemAllomorphStemNameComposer.Build(cache, new(stem, null, own)));
        Assert.Throws<InvalidOperationException>(() => EditStemAllomorphStemNameComposer.Build(cache, new(childStem, null, foreign)));
        Assert.Throws<InvalidOperationException>(() => EditStemAllomorphStemNameComposer.Build(cache, new(childStem, own, null)));
        Assert.Throws<InvalidOperationException>(() => EditStemAllomorphStemNameComposer.Build(cache, new(affix, null, null)));
        Assert.Throws<InvalidOperationException>(() => EditStemAllomorphStemNameComposer.Build(cache, new(msa, null, null)));
        Assert.Throws<InvalidOperationException>(() => EditStemAllomorphStemNameComposer.Build(cache, new(childStem, null, category)));
        Assert.ThrowsAny<Exception>(() => EditStemAllomorphStemNameComposer.Build(cache, new(childStem, null, CanonicalId.Mint())));
        Assert.Equal(before, BaselineSemanticDigest.Compute(cache));
    }

    [Fact]
    public void StemNameNeedsAStemAnalysisWithACategory()
    {
        using var cache = pristine.NewScratch();
        var category = CanonicalId.FromGuid(pristine.Seed.PartOfSpeechId);
        var name = AddStemName(cache, category, "plural");
        var (_, msa, stem) = AddStem(cache, "root", category);
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            Get<IMoStemMsa>(cache, msa).PartOfSpeechRA = null);
        Assert.Throws<InvalidOperationException>(() => EditStemAllomorphStemNameComposer.Build(cache, new(stem, null, name)));
    }

    [Fact]
    public void ClassSetClearAndNoopUseExplicitState()
    {
        using var cache = pristine.NewScratch();
        var parent = CanonicalId.FromGuid(pristine.Seed.PartOfSpeechId);
        var child = AddChildCategory(cache, parent);
        var concord = AddInflectionClass(cache, parent, "concord");
        var (_, msa, _) = AddStem(cache, "root", child);
        var set = EditStemInflectionClassComposer.Build(cache, new(Alias(msa), null, Alias(concord)));
        var operation = Assert.Single(set);
        Assert.Equal(MoStemMsaInflectionClassOperationKinds.SetInflectionClass, operation.Kind);
        Assert.Equal(concord.Value, operation.After!.Value.GetProperty("ref").GetString());
        ApplyOps(cache, set);
        Assert.Equal(concord.ToGuid(), Get<IMoStemMsa>(cache, msa).InflectionClassRA.Guid);
        Assert.Empty(EditStemInflectionClassComposer.Build(cache, new(msa, Alias(concord), concord)));
        var clear = EditStemInflectionClassComposer.Build(cache, new(msa, concord, null));
        Assert.Equal(MoStemMsaInflectionClassOperationKinds.ClearInflectionClass, Assert.Single(clear).Kind);
        Assert.Empty(clear[0].After!.Value.EnumerateObject());
        ApplyOps(cache, clear);
        Assert.Null(Get<IMoStemMsa>(cache, msa).InflectionClassRA);
        Assert.Empty(EditStemInflectionClassComposer.Build(cache, new(msa, null, null)));
    }

    [Fact]
    public void ClassRefusalsLeaveLiveStateAlone()
    {
        using var cache = pristine.NewScratch();
        var parent = CanonicalId.FromGuid(pristine.Seed.PartOfSpeechId);
        var child = AddChildCategory(cache, parent);
        var childClass = AddInflectionClass(cache, child, "child class");
        var foreign = AddInflectionClass(cache, AddCategory(cache, "foreign"), "foreign");
        var (_, parentMsa, stem) = AddStem(cache, "root", parent);
        var (_, _, prefixMsaForm) = AddInflectionalPrefix(cache, "n", parent);
        var (_, bareMsa, _) = AddStem(cache, "bare", parent);
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            Get<IMoStemMsa>(cache, bareMsa).PartOfSpeechRA = null);
        var before = BaselineSemanticDigest.Compute(cache);
        Assert.Throws<InvalidOperationException>(() => EditStemInflectionClassComposer.Build(cache, new(parentMsa, null, childClass)));
        Assert.Throws<InvalidOperationException>(() => EditStemInflectionClassComposer.Build(cache, new(parentMsa, null, foreign)));
        Assert.Throws<InvalidOperationException>(() => EditStemInflectionClassComposer.Build(cache, new(parentMsa, childClass, null)));
        Assert.Throws<InvalidOperationException>(() => EditStemInflectionClassComposer.Build(cache, new(stem, null, null)));
        Assert.Throws<InvalidOperationException>(() => EditStemInflectionClassComposer.Build(cache, new(prefixMsaForm, null, null)));
        Assert.Throws<InvalidOperationException>(() => EditStemInflectionClassComposer.Build(cache, new(bareMsa, null, null)));
        Assert.ThrowsAny<Exception>(() => EditStemInflectionClassComposer.Build(cache, new(parentMsa, null, CanonicalId.Mint())));
        Assert.Equal(before, BaselineSemanticDigest.Compute(cache));
    }

    [Fact]
    public void ChildStemMayUseAnAncestorsClass()
    {
        using var cache = pristine.NewScratch();
        var parent = CanonicalId.FromGuid(pristine.Seed.PartOfSpeechId);
        var child = AddChildCategory(cache, parent);
        var concord = AddInflectionClass(cache, parent, "concord");
        var (_, msa, _) = AddStem(cache, "root", child);
        Assert.Single(EditStemInflectionClassComposer.Build(cache, new(msa, null, concord)));
    }

    [Fact]
    public void PreviewApplyAndSavedReadbackShowBothLinks()
    {
        using var cache = pristine.NewScratch();
        var category = CanonicalId.FromGuid(pristine.Seed.PartOfSpeechId);
        var cls = AddInflectionClass(cache, category, "concord");
        var name = AddStemName(cache, category, "plural");
        var (_, msa, stem) = AddStem(cache, "root", category);
        var proposal = Proposal(EditStemInflectionClassComposer.Build(cache, new(msa, null, cls))
            .Concat(EditStemAllomorphStemNameComposer.Build(cache, new(stem, null, name))).Reverse().ToArray());
        var baseline = BaselineSemanticDigest.Compute(cache);
        var dryRun = ScratchDryRun.Of(cache, proposal);
        Assert.Equal(baseline, BaselineSemanticDigest.Compute(cache));
        Assert.Contains(dryRun.ExpectedEffects, effect =>
            effect.Field == SnapshotFields.MoStemMsaInflectionClass && effect.After.Values.Contains(cls.Value));
        Assert.Contains(dryRun.ExpectedEffects, effect =>
            effect.Field == SnapshotFields.MoStemAllomorphStemName && effect.After.Values.Contains(name.Value));
        ProposalApplier.Apply(cache, proposal, dryRun.Anchor, "stem-link-tests");
        var digest = BaselineSemanticDigest.Compute(cache);
        var loader = new FwDataProjectLoader();
        loader.Save(cache);
        using var reopened = loader.LoadScratchCache(cache.ProjectId.Path);
        Assert.Equal(cls.ToGuid(), Get<IMoStemMsa>(reopened, msa).InflectionClassRA.Guid);
        Assert.Equal(name.ToGuid(), Get<IMoStemAllomorph>(reopened, stem).StemNameRA.Guid);
        Assert.Equal(digest, BaselineSemanticDigest.Compute(reopened));
    }

    [Fact]
    public void FailureAfterBothWritesRollsBackTheWholeProposal()
    {
        using var cache = pristine.NewScratch();
        var category = CanonicalId.FromGuid(pristine.Seed.PartOfSpeechId);
        var cls = AddInflectionClass(cache, category, "concord");
        var name = AddStemName(cache, category, "plural");
        var (_, msa, stem) = AddStem(cache, "root", category);
        var valid = Proposal(EditStemInflectionClassComposer.Build(cache, new(msa, null, cls))
            .Concat(EditStemAllomorphStemNameComposer.Build(cache, new(stem, null, name))).ToArray());
        var dryRun = ScratchDryRun.Of(cache, valid);
        var before = BaselineSemanticDigest.Compute(cache);
        var reached = false;
        Assert.Throws<InvalidOperationException>(() => ProposalApplier.Apply(cache, valid, dryRun.Anchor,
            "stem-link-tests", "", (index, _) =>
            {
                if (index != 2) return;
                Assert.Equal(cls.ToGuid(), Get<IMoStemMsa>(cache, msa).InflectionClassRA.Guid);
                Assert.Equal(name.ToGuid(), Get<IMoStemAllomorph>(cache, stem).StemNameRA.Guid);
                reached = true;
                throw new InvalidOperationException("Injected failure after both stem link edits.");
            }));
        Assert.True(reached);
        Assert.Null(Get<IMoStemMsa>(cache, msa).InflectionClassRA);
        Assert.Null(Get<IMoStemAllomorph>(cache, stem).StemNameRA);
        Assert.Equal(before, BaselineSemanticDigest.Compute(cache));
    }

    [Fact]
    public void TwoStemNameEditsOfOneAllomorphAreRefusedBeforeAnyUnitOfWork()
    {
        using var cache = pristine.NewScratch();
        var category = CanonicalId.FromGuid(pristine.Seed.PartOfSpeechId);
        var first = AddStemName(cache, category, "first");
        var second = AddStemName(cache, category, "second");
        var (_, _, stem) = AddStem(cache, "root", category);
        var setFirst = EditStemAllomorphStemNameComposer.Build(cache, new(stem, null, first));
        var setSecond = EditStemAllomorphStemNameComposer.Build(cache, new(stem, null, second));
        var both = setFirst.Concat(setSecond).ToArray();
        Assert.NotNull(ProposalOperationSlotValidator.FindConflict(both));
        var anchor = ScratchDryRun.Of(cache, Proposal(setFirst)).Anchor;
        var before = BaselineSemanticDigest.Compute(cache);
        var refusal = Assert.Throws<ContractParseException>(() =>
            ProposalApplier.Apply(cache, Proposal(both), anchor, "stem-link-tests"));
        Assert.Contains("address the same target, field, and member slot", refusal.Message);
        Assert.Contains(setFirst[0].OperationId.Value, refusal.Message);
        Assert.Contains(setSecond[0].OperationId.Value, refusal.Message);
        Assert.Throws<ContractParseException>(() => ScratchDryRun.Of(cache, Proposal(both)));
        Assert.Null(Get<IMoStemAllomorph>(cache, stem).StemNameRA);
        Assert.Equal(before, BaselineSemanticDigest.Compute(cache));
    }

    [Fact]
    public void TwoInflectionClassEditsOfOneStemAnalysisAreRefusedBeforeAnyUnitOfWork()
    {
        using var cache = pristine.NewScratch();
        var category = CanonicalId.FromGuid(pristine.Seed.PartOfSpeechId);
        var first = AddInflectionClass(cache, category, "first");
        var second = AddInflectionClass(cache, category, "second");
        var (_, msa, _) = AddStem(cache, "root", category);
        var setFirst = EditStemInflectionClassComposer.Build(cache, new(msa, null, first));
        var setSecond = EditStemInflectionClassComposer.Build(cache, new(msa, null, second));
        var both = setFirst.Concat(setSecond).ToArray();
        Assert.NotNull(ProposalOperationSlotValidator.FindConflict(both));
        var anchor = ScratchDryRun.Of(cache, Proposal(setFirst)).Anchor;
        var before = BaselineSemanticDigest.Compute(cache);
        var refusal = Assert.Throws<ContractParseException>(() =>
            ProposalApplier.Apply(cache, Proposal(both), anchor, "stem-link-tests"));
        Assert.Contains("address the same target, field, and member slot", refusal.Message);
        Assert.Throws<ContractParseException>(() => ScratchDryRun.Of(cache, Proposal(both)));
        Assert.Null(Get<IMoStemMsa>(cache, msa).InflectionClassRA);
        Assert.Equal(before, BaselineSemanticDigest.Compute(cache));
    }

    private static CanonicalId Alias(CanonicalId id) => CanonicalId.Parse("link:" + id.Value);

    private static CanonicalId AddChildCategory(LcmCache cache, CanonicalId parent)
    {
        IPartOfSpeech child = null!;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            child = cache.ServiceLocator.GetInstance<IPartOfSpeechFactory>().Create();
            Get<IPartOfSpeech>(cache, parent).SubPossibilitiesOS.Add(child);
            child.Name.set_String(cache.DefaultAnalWs, "child");
        });
        return Id(child);
    }

    private static CanonicalId AddStemName(LcmCache cache, CanonicalId category, string name)
    {
        IMoStemName created = null!;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            created = cache.ServiceLocator.GetInstance<IMoStemNameFactory>().Create();
            Get<IPartOfSpeech>(cache, category).StemNamesOC.Add(created);
            created.Name.set_String(cache.DefaultAnalWs, name);
        });
        return Id(created);
    }
}
