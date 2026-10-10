using System;
using System.Collections.Generic;
using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Canonicalization;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Model.DryRun;
using SIL.Motif.Model.Snapshot;
using SIL.Motif.Runner.AppliedLog;
using SIL.Motif.Runner.Apply;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.DryRun;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Composers;

[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group0)]
public sealed class EditAdhocProhibitionComposerTests : IDisposable
{
    private readonly LcmCache _cache;
    private readonly SeededProject _seed;

    public EditAdhocProhibitionComposerTests(PristineProjectFixture pristine)
    {
        _cache = pristine.NewScratch();
        _seed = pristine.Seed;
    }

    public void Dispose()
    {
        if (!_cache.IsDisposed) _cache.Dispose();
    }

    [Fact]
    public void BuildAuthorsOneExplicitDisabledFieldWriteForEachFlatProhibitionKind()
    {
        var allomorphRule = CreateAllomorphRule(disabled: false);
        var morphemeRule = CreateMorphemeRule(disabled: true);

        var allomorphWrite = Assert.Single(EditAdhocProhibitionComposer.Build(_cache,
            new EditAdhocProhibitionIntent(Id(allomorphRule), false, true)));
        var morphemeWrite = Assert.Single(EditAdhocProhibitionComposer.Build(_cache,
            new EditAdhocProhibitionIntent(Id(morphemeRule), true, false)));

        AssertDisabledWrite(allomorphWrite, allomorphRule.Guid, true);
        AssertDisabledWrite(morphemeWrite, morphemeRule.Guid, false);
        Assert.False(allomorphRule.Disabled);
        Assert.True(morphemeRule.Disabled);
    }

    [Fact]
    public void BuildRejectsAGroupAndAnUnrelatedObjectAsTargets()
    {
        var group = _cache.ServiceLocator.GetInstance<IMoAdhocProhibGrFactory>().Create();
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
            _cache.LangProject.MorphologicalDataOA.AdhocCoProhibitionsOC.Add(group));
        var groupError = Assert.Throws<InvalidOperationException>(() => EditAdhocProhibitionComposer.Build(_cache,
            new EditAdhocProhibitionIntent(Id(group), false, true)));
        Assert.Contains("Only flat allomorph and morpheme prohibitions can be edited.",
            groupError.Message, StringComparison.Ordinal);

        var entryError = Assert.Throws<InvalidOperationException>(() => EditAdhocProhibitionComposer.Build(_cache,
            new EditAdhocProhibitionIntent(CanonicalId.FromGuid(_seed.FirstEntryId), false, true)));
        Assert.Contains("not an ad hoc prohibition", entryError.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildRefusesAStalePreconditionWithoutChangingTheProject()
    {
        var rule = CreateAllomorphRule(disabled: false);

        var error = Assert.Throws<InvalidOperationException>(() => EditAdhocProhibitionComposer.Build(_cache,
            new EditAdhocProhibitionIntent(Id(rule), ExpectedDisabled: true, Disabled: true)));

        Assert.Contains("the intent expected Disabled=True", error.Message, StringComparison.Ordinal);
        Assert.False(rule.Disabled);
    }

    [Fact]
    public void DryRunKeepsTheSavedProjectUnchangedAndApplyReadsBackTheAuthoredValue()
    {
        var rule = CreateAllomorphRule(disabled: false);
        var proposal = Proposal(new EditAdhocProhibitionIntent(Id(rule), false, true));

        var dryRun = ScratchDryRun.Of(_cache, proposal);

        Assert.False(rule.Disabled);
        var effect = Assert.Single(dryRun.ExpectedEffects);
        Assert.Equal(SnapshotFields.MoAdhocProhibDisabled, effect.Field);
        Assert.Equal("false", effect.Before[BooleanFieldAlternatives.Key]);

        var receipt = ProposalApplier.Apply(_cache, proposal, dryRun.Anchor, "motif-tests");

        Assert.False(receipt.AlreadyApplied);
        Assert.True(rule.Disabled);
        Assert.Equal("true", Assert.Single(receipt.ActualEffects).After[BooleanFieldAlternatives.Key]);
    }

    [Fact]
    public void ApplyFailureAfterTheDisabledWriteRollsBackTheWholeProposal()
    {
        var rule = CreateAllomorphRule(disabled: false);
        var proposal = Proposal(new EditAdhocProhibitionIntent(Id(rule), false, true));
        var anchor = new BoundDryRunAnchor(IntentDigest.Compute(proposal),
            FootprintProbe.ComputeCurrentFootprintDigest(_cache, proposal),
            "sha256:" + new string('0', 64), "test", "test", "1", "20260101T000000Z");

        Assert.Throws<InvalidOperationException>(() => ProposalApplier.Apply(_cache, proposal, anchor,
            "motif-tests", "", (_, _) => throw new InvalidOperationException("stop after the field write")));

        Assert.False(rule.Disabled);
        Assert.Empty(ProjectAppliedLog.ReadAll(_cache));
    }

    private IMoAlloAdhocProhib CreateAllomorphRule(bool disabled)
    {
        var rule = _cache.ServiceLocator.GetInstance<IMoAlloAdhocProhibFactory>().Create();
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            _cache.LangProject.MorphologicalDataOA.AdhocCoProhibitionsOC.Add(rule);
            rule.Disabled = disabled;
        });
        return rule;
    }

    private IMoMorphAdhocProhib CreateMorphemeRule(bool disabled)
    {
        var rule = _cache.ServiceLocator.GetInstance<IMoMorphAdhocProhibFactory>().Create();
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            _cache.LangProject.MorphologicalDataOA.AdhocCoProhibitionsOC.Add(rule);
            rule.Disabled = disabled;
        });
        return rule;
    }

    private Proposal Proposal(EditAdhocProhibitionIntent intent)
    {
        var operations = EditAdhocProhibitionComposer.Build(_cache, intent);
        return new Proposal(new Dictionary<string, string> { ["grammar"] = "1.0" }, CanonicalId.Mint(), null,
            operations);
    }

    private static void AssertDisabledWrite(OperationEnvelope operation, Guid target, bool disabled)
    {
        Assert.Equal(MoAdhocProhibDisabledOperationKinds.SetDisabled, operation.Kind);
        Assert.Equal(CanonicalId.FromGuid(target), operation.Target);
        Assert.Empty(operation.DependsOn);
        Assert.Equal(disabled, MoAdhocProhibDisabledSetPayload.Parse(operation.After!.Value));
    }

    private static CanonicalId Id(ICmObject value) => CanonicalId.FromGuid(value.Guid);
}
