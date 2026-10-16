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
public sealed class EditCompoundRuleComposerTests : IDisposable
{
    private readonly LcmCache _cache;
    private readonly SeededProject _seed;

    public EditCompoundRuleComposerTests(PristineProjectFixture pristine)
    {
        _cache = pristine.NewScratch();
        _seed = pristine.Seed;
    }

    public void Dispose()
    {
        if (!_cache.IsDisposed) _cache.Dispose();
    }

    [Fact]
    public void BuildAuthorsOneExplicitDisabledFieldWriteForEachCompoundRuleKind()
    {
        var endocentric = CreateEndocentricRule(disabled: false);
        var exocentric = CreateExocentricRule(disabled: true);

        var endoWrite = Assert.Single(EditCompoundRuleComposer.Build(_cache,
            new EditCompoundRuleIntent(Id(endocentric), false, true)));
        var exoWrite = Assert.Single(EditCompoundRuleComposer.Build(_cache,
            new EditCompoundRuleIntent(Id(exocentric), true, false)));

        AssertDisabledWrite(endoWrite, endocentric.Guid, true);
        AssertDisabledWrite(exoWrite, exocentric.Guid, false);
        Assert.False(endocentric.Disabled);
        Assert.True(exocentric.Disabled);
    }

    [Fact]
    public void BuildRejectsAnAdHocProhibitionAndAnUnrelatedObjectAsTargets()
    {
        var prohibition = _cache.ServiceLocator.GetInstance<IMoAlloAdhocProhibFactory>().Create();
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
            _cache.LangProject.MorphologicalDataOA.AdhocCoProhibitionsOC.Add(prohibition));
        var prohibitionError = Assert.Throws<InvalidOperationException>(() => EditCompoundRuleComposer.Build(_cache,
            new EditCompoundRuleIntent(Id(prohibition), false, true)));
        Assert.Contains("is not a compound rule", prohibitionError.Message, StringComparison.Ordinal);

        var entryError = Assert.Throws<InvalidOperationException>(() => EditCompoundRuleComposer.Build(_cache,
            new EditCompoundRuleIntent(CanonicalId.FromGuid(_seed.FirstEntryId), false, true)));
        Assert.Contains("is not a compound rule", entryError.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildRefusesAStalePreconditionWithoutChangingTheProject()
    {
        var rule = CreateEndocentricRule(disabled: false);

        var error = Assert.Throws<InvalidOperationException>(() => EditCompoundRuleComposer.Build(_cache,
            new EditCompoundRuleIntent(Id(rule), ExpectedDisabled: true, Disabled: true)));

        Assert.Contains("the intent expected Disabled=True", error.Message, StringComparison.Ordinal);
        Assert.False(rule.Disabled);
    }

    [Fact]
    public void DryRunKeepsTheSavedProjectUnchangedAndApplyReadsBackTheAuthoredValue()
    {
        var rule = CreateEndocentricRule(disabled: false);
        var proposal = Proposal(new EditCompoundRuleIntent(Id(rule), false, true));

        var dryRun = ScratchDryRun.Of(_cache, proposal);

        Assert.False(rule.Disabled);
        var effect = Assert.Single(dryRun.ExpectedEffects);
        Assert.Equal(SnapshotFields.MoCompoundRuleDisabled, effect.Field);
        Assert.Equal("false", effect.Before[BooleanFieldAlternatives.Key]);

        var receipt = ProposalApplier.Apply(_cache, proposal, dryRun.Anchor, "motif-tests");

        Assert.False(receipt.AlreadyApplied);
        Assert.True(rule.Disabled);
        Assert.Equal("true", Assert.Single(receipt.ActualEffects).After[BooleanFieldAlternatives.Key]);
    }

    [Fact]
    public void ApplyFailureAfterTheDisabledWriteRollsBackTheWholeProposal()
    {
        var rule = CreateEndocentricRule(disabled: false);
        var proposal = Proposal(new EditCompoundRuleIntent(Id(rule), false, true));
        var anchor = new BoundDryRunAnchor(IntentDigest.Compute(proposal),
            FootprintProbe.ComputeCurrentFootprintDigest(_cache, proposal),
            "sha256:" + new string('0', 64), "test", "test", SnapshotFields.ProjectionVersion, "20260101T000000Z");

        Assert.Throws<InvalidOperationException>(() => ProposalApplier.Apply(_cache, proposal, anchor,
            "motif-tests", "", (_, _) => throw new InvalidOperationException("stop after the field write")));

        Assert.False(rule.Disabled);
        Assert.Empty(ProjectAppliedLog.ReadAll(_cache));
    }

    private IMoEndoCompound CreateEndocentricRule(bool disabled)
    {
        var rule = _cache.ServiceLocator.GetInstance<IMoEndoCompoundFactory>().Create();
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            _cache.LangProject.MorphologicalDataOA.CompoundRulesOS.Add(rule);
            rule.Disabled = disabled;
        });
        return rule;
    }

    private IMoExoCompound CreateExocentricRule(bool disabled)
    {
        var rule = _cache.ServiceLocator.GetInstance<IMoExoCompoundFactory>().Create();
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            _cache.LangProject.MorphologicalDataOA.CompoundRulesOS.Add(rule);
            rule.Disabled = disabled;
        });
        return rule;
    }

    private Proposal Proposal(EditCompoundRuleIntent intent)
    {
        var operations = EditCompoundRuleComposer.Build(_cache, intent);
        return new Proposal(new Dictionary<string, string> { ["grammar"] = "1.0" }, CanonicalId.Mint(), null,
            operations);
    }

    private static void AssertDisabledWrite(OperationEnvelope operation, Guid target, bool disabled)
    {
        Assert.Equal(MoCompoundRuleDisabledOperationKinds.SetDisabled, operation.Kind);
        Assert.Equal(CanonicalId.FromGuid(target), operation.Target);
        Assert.Empty(operation.DependsOn);
        Assert.Equal(disabled, MoCompoundRuleDisabledSetPayload.Parse(operation.After!.Value));
    }

    private static CanonicalId Id(ICmObject value) => CanonicalId.FromGuid(value.Guid);
}
