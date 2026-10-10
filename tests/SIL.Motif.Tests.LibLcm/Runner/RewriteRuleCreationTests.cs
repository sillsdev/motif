using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Model.Snapshot;
using SIL.Motif.Runner.Apply;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Runner.Snapshotting;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Runner;

[Collection(LcmCacheParallelCollections.Group2)]
public sealed class RewriteRuleCreationTests(PristineProjectFixture pristine)
{
    [Fact]
    public void RegularRuleAndRhs_DryRunApplyAndReopen_PreserveIdentityOwnerDisabledStateAndPosition()
    {
        using var cache = pristine.NewScratch();
        var data = cache.LangProject.PhonologicalDataOA;
        var existing = AddExistingRule(cache, "Existing rule");
        var before = SoundSystemSnapshots.Read(cache);
        var ids = BuildIds();
        var proposal = BuildProposal(cache, data, existing, ids);

        var dryRun = ScratchDryRun.Of(cache, proposal);
        var sequenceEffect = Assert.Single(dryRun.ExpectedEffects, effect =>
            effect.Field == SnapshotFields.PhPhonDataPhonRules);
        Assert.Equal(new[] { CanonicalId.FromGuid(existing.Guid).Value, ids.Rule.Value },
            sequenceEffect.After.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Value));
        Assert.Empty(SoundSystemSnapshots.Compare(before, SoundSystemSnapshots.Read(cache)));

        var receipt = ProposalApplier.Apply(cache, proposal, dryRun.Anchor, "rewrite-rule-tests");
        Assert.False(receipt.AlreadyApplied);
        var rule = Assert.IsAssignableFrom<IPhRegularRule>(cache.ServiceLocator
            .GetInstance<ICmObjectRepository>().GetObject(ids.Rule.ToGuid()));
        var rhs = Assert.Single(rule.RightHandSidesOS);
        Assert.Equal(data.Guid, rule.Owner?.Guid);
        Assert.Equal(rule.Guid, rhs.Owner?.Guid);
        Assert.True(rule.Disabled);
        Assert.Equal("O7 test rule", rule.Name.get_String(cache.DefaultAnalWs)?.Text);
        Assert.Equal(2, rule.Direction);

        var after = SoundSystemSnapshots.Read(cache);
        var differences = SoundSystemSnapshots.Compare(before, after);
        Assert.Contains(differences, effect => effect.CanonicalId == CanonicalId.FromGuid(data.Guid) &&
            effect.Field == SnapshotFields.PhPhonDataPhonRules);
        Assert.Contains(differences, effect => effect.CanonicalId == ids.Rule &&
            effect.Field == SnapshotFields.PhSegmentRuleName);
        Assert.Contains(differences, effect => effect.CanonicalId == ids.Rule &&
            effect.Field == SnapshotFields.PhSegmentRuleDirection);
        Assert.Contains(differences, effect => effect.CanonicalId == ids.Rule &&
            effect.Field == SnapshotFields.PhSegmentRuleDisabled);
        Assert.Contains(differences, effect => effect.CanonicalId == ids.Rule &&
            effect.Field == SnapshotFields.PhRegularRuleRightHandSides);

        new FwDataProjectLoader().Save(cache);
        using var reopened = new FwDataProjectLoader().LoadScratchCache(cache.ProjectId.Path);
        var reopenedData = reopened.LangProject.PhonologicalDataOA;
        Assert.Equal(new[] { existing.Guid, ids.Rule.ToGuid() }, reopenedData.PhonRulesOS.Select(item => item.Guid));
        var reopenedRule = Assert.IsAssignableFrom<IPhRegularRule>(reopened.ServiceLocator
            .GetInstance<ICmObjectRepository>().GetObject(ids.Rule.ToGuid()));
        Assert.True(reopenedRule.Disabled);
        Assert.Equal("O7 test rule", reopenedRule.Name.get_String(reopened.DefaultAnalWs)?.Text);
        Assert.Equal(2, reopenedRule.Direction);
        Assert.Single(reopenedRule.RightHandSidesOS);
        Assert.Empty(SoundSystemSnapshots.Compare(after, SoundSystemSnapshots.Read(reopened)));
    }

    [Fact]
    public void IncompleteRegularRuleCannotBeEnabled()
    {
        using var cache = pristine.NewScratch();
        var data = cache.LangProject.PhonologicalDataOA;
        var existing = AddExistingRule(cache, "Existing rule");
        var ids = BuildIds();
        var proposal = BuildProposal(cache, data, existing, ids);
        var activation = proposal.Operations.Single(operation =>
            operation.Kind == PhSegmentRuleDisabledOperationKinds.SetDisabled);
        var preceding = proposal.Operations.Where(operation =>
            operation.OperationId != activation.OperationId).ToArray();
        var enabledProposal = new Proposal(
            proposal.ContractVersions,
            proposal.ProposalId,
            proposal.Requires,
            [.. preceding,
                new OperationEnvelope(activation.OperationId, activation.Kind, target: ids.Rule,
                    after: JsonSerializer.SerializeToElement(new { value = false }),
                    dependsOn: preceding.Select(operation => new OperationDependency(operation.OperationId)).ToArray())]);

        var exception = Assert.Throws<InvalidOperationException>(() => ScratchDryRun.Of(cache, enabledProposal));
        Assert.Contains("left and right contexts", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RuleCreation_RejectsSameTypeAndWrongTypeIdentityCollisions()
    {
        using var cache = pristine.NewScratch();
        var data = cache.LangProject.PhonologicalDataOA;
        var existing = AddExistingRule(cache, "Existing rule");
        var sameType = RuleCreateProposal(data, CanonicalId.FromGuid(existing.Guid),
            new Placement(CanonicalId.FromGuid(existing.Guid), null));

        var reuse = Assert.Throws<InvalidOperationException>(() => ScratchDryRun.Of(cache, sameType));
        Assert.Contains("overwrite/reuse", reuse.Message, StringComparison.Ordinal);

        var wrongTypeId = CanonicalId.FromGuid(data.Guid);
        var wrongType = RuleCreateProposal(data, wrongTypeId, new Placement(CanonicalId.FromGuid(existing.Guid), null));
        var conflict = Assert.Throws<InvalidOperationException>(() => ScratchDryRun.Of(cache, wrongType));
        Assert.Contains("semantic conflict", conflict.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RuleCreation_RejectsMetathesisConcreteClass()
    {
        using var cache = pristine.NewScratch();
        var data = cache.LangProject.PhonologicalDataOA;
        var proposal = new Proposal(
            new Dictionary<string, string> { ["grammar"] = "1.0" }, CanonicalId.Mint(), null,
            [new OperationEnvelope(CanonicalId.Mint(), PhPhonDataPhonRulesOperationKinds.Create,
                entityId: CanonicalId.Mint(), target: CanonicalId.FromGuid(data.Guid),
                after: JsonSerializer.SerializeToElement(new { @class = "PhMetathesisRule" }))]);

        var exception = Assert.Throws<InvalidOperationException>(() => ScratchDryRun.Of(cache, proposal));

        Assert.Contains("class must be PhRegularRule", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RuleCreation_FootprintDriftRequiresANewDryRun()
    {
        using var cache = pristine.NewScratch();
        var data = cache.LangProject.PhonologicalDataOA;
        var existing = AddExistingRule(cache, "Existing rule");
        var proposal = RuleCreateProposal(data, CanonicalId.Mint(),
            new Placement(CanonicalId.FromGuid(existing.Guid), null));
        var dryRun = ScratchDryRun.Of(cache, proposal);
        AddExistingRule(cache, "Concurrent rule");

        var exception = Assert.Throws<ApplyPreconditionException>(() => ProposalApplier.Apply(
            cache, proposal, dryRun.Anchor, "rewrite-rule-tests"));

        Assert.Contains("Footprint drift detected", exception.Message, StringComparison.Ordinal);
        Assert.Equal(2, data.PhonRulesOS.Count);
    }

    [Fact]
    public void RuleAndRightHandSideCreation_RollsBackAsOneProposal()
    {
        using var cache = pristine.NewScratch();
        var data = cache.LangProject.PhonologicalDataOA;
        var existing = AddExistingRule(cache, "Existing rule");
        var before = SoundSystemSnapshots.Read(cache);
        var ids = BuildIds();
        var proposal = BuildProposal(cache, data, existing, ids);
        var dryRun = ScratchDryRun.Of(cache, proposal);

        var exception = Assert.Throws<InvalidOperationException>(() => ProposalApplier.Apply(
            cache, proposal, dryRun.Anchor, "rewrite-rule-tests", string.Empty,
            afterOperation: (_, operation) =>
            {
                if (operation.Kind == PhRegularRuleRightHandSidesOperationKinds.Create)
                    throw new InvalidOperationException("injected after right-hand-side creation");
            }));

        Assert.Equal("injected after right-hand-side creation", exception.Message);
        var repository = cache.ServiceLocator.GetInstance<ICmObjectRepository>();
        Assert.False(repository.IsValidObjectId(ids.Rule.ToGuid()));
        Assert.False(repository.IsValidObjectId(ids.RightHandSide.ToGuid()));
        Assert.Equal(new[] { existing.Guid }, data.PhonRulesOS.Select(rule => rule.Guid));
        Assert.Empty(SoundSystemSnapshots.Compare(before, SoundSystemSnapshots.Read(cache)));
    }

    private static IPhRegularRule AddExistingRule(LcmCache cache, string name)
    {
        IPhRegularRule result = null!;
        UndoableUnitOfWorkHelper.Do("test setup", "test setup",
            cache.ServiceLocator.GetInstance<IActionHandler>(), () =>
            {
                result = cache.ServiceLocator.GetInstance<IPhRegularRuleFactory>().Create();
                cache.LangProject.PhonologicalDataOA.PhonRulesOS.Add(result);
                result.Name.set_String(cache.DefaultAnalWs, name);
                result.Disabled = true;
            });
        return result;
    }

    private static (CanonicalId Rule, CanonicalId RightHandSide) BuildIds() =>
        (CanonicalId.Mint(), CanonicalId.Mint());

    private static Proposal RuleCreateProposal(IPhPhonData data, CanonicalId entityId, Placement placement) =>
        new(new Dictionary<string, string> { ["grammar"] = "1.0" }, CanonicalId.Mint(), null,
            [new OperationEnvelope(CanonicalId.Mint(), PhPhonDataPhonRulesOperationKinds.Create,
                entityId: entityId, target: CanonicalId.FromGuid(data.Guid),
                after: JsonSerializer.SerializeToElement(new { @class = "PhRegularRule" }),
                placement: placement)]);

    private static Proposal BuildProposal(
        LcmCache cache,
        IPhPhonData data,
        IPhRegularRule existing,
        (CanonicalId Rule, CanonicalId RightHandSide) ids)
    {
        var createRuleOperationId = CanonicalId.Mint();
        var dependsOnRule = new[] { new OperationDependency(createRuleOperationId) };
        var writingSystem = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultAnalWs);
        return new Proposal(
            new Dictionary<string, string> { ["grammar"] = "1.0" },
            CanonicalId.Mint(),
            null,
            [
                new OperationEnvelope(createRuleOperationId, PhPhonDataPhonRulesOperationKinds.Create,
                    entityId: ids.Rule, target: CanonicalId.FromGuid(data.Guid),
                    after: JsonSerializer.SerializeToElement(new { @class = "PhRegularRule" }),
                    placement: new Placement(CanonicalId.FromGuid(existing.Guid), null)),
                new OperationEnvelope(CanonicalId.Mint(), PhSegmentRuleNameOperationKinds.SetName,
                    target: ids.Rule,
                    after: JsonSerializer.SerializeToElement(new { ws = writingSystem, text = "O7 test rule" }),
                    dependsOn: dependsOnRule),
                new OperationEnvelope(CanonicalId.Mint(), PhSegmentRuleDirectionOperationKinds.SetDirection,
                    target: ids.Rule, after: JsonSerializer.SerializeToElement(new { value = 2 }),
                    dependsOn: dependsOnRule),
                new OperationEnvelope(CanonicalId.Mint(), PhSegmentRuleDisabledOperationKinds.SetDisabled,
                    target: ids.Rule, after: JsonSerializer.SerializeToElement(new { value = true }),
                    dependsOn: dependsOnRule),
                new OperationEnvelope(CanonicalId.Mint(), PhRegularRuleRightHandSidesOperationKinds.Create,
                    entityId: ids.RightHandSide, target: ids.Rule,
                    after: JsonSerializer.SerializeToElement(new { }), dependsOn: dependsOnRule),
            ]);
    }
}
