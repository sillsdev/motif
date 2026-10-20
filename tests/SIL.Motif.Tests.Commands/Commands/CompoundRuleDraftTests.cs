using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[Collection(LcmCacheParallelCollections.Group2)]
public sealed class CompoundRuleDraftTests(PristineProjectFixture pristine)
{
    private const string Version = "0.1.0";
    private const string DraftName = "compound-rule";

    [Fact]
    public void TheComposerStagesOneDisabledWriteInTheDraftAndRefusesAStaleOrUnknownRuleWithoutChangingIt()
    {
        var (path, ruleId) = ProjectWithOneEnabledCompoundRule();
        Assert.True(ProposalCommands.New(new(path, Version, DraftName, "Turn off a compound rule")).Succeeded);

        var stale = Compose(path, new { target = ruleId, expectedDisabled = true, disabled = false });
        Assert.False(stale.Succeeded);
        Assert.Contains("expected Disabled=True", stale.Refusal!.Message, StringComparison.Ordinal);

        var unknownId = CanonicalId.Mint();
        var unknown = Compose(path, new { target = unknownId.Value, expectedDisabled = false, disabled = true });
        Assert.False(unknown.Succeeded);
        Assert.Contains(unknownId.ToGuid().ToString("D"), unknown.Refusal!.Message, StringComparison.Ordinal);
        Assert.Contains("not found", unknown.Refusal.Message, StringComparison.Ordinal);

        var before = DraftBytes(path);
        Assert.DoesNotContain(MoCompoundRuleDisabledOperationKinds.SetDisabled, before, StringComparison.Ordinal);

        var staged = Compose(path, new { target = ruleId, expectedDisabled = false, disabled = true });
        Assert.True(staged.Succeeded, staged.Refusal?.Message);
        var operation = Assert.Single(staged.Value!.Operations);
        Assert.Equal(MoCompoundRuleDisabledOperationKinds.SetDisabled, operation.Kind);
        Assert.Contains(MoCompoundRuleDisabledOperationKinds.SetDisabled, DraftBytes(path), StringComparison.Ordinal);
    }

    private static CommandOutcome<ComposedOperationsResponse> Compose(string path, object intent) =>
        ProposalCommands.ComposeIntent(new(path, Version, DraftName, "compose-edit-compound-rule",
            JsonSerializer.Serialize(intent)));

    private (string Path, string RuleId) ProjectWithOneEnabledCompoundRule()
    {
        using var cache = pristine.NewScratch();
        var rule = cache.ServiceLocator.GetInstance<IMoEndoCompoundFactory>().Create();
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            cache.LangProject.MorphologicalDataOA.CompoundRulesOS.Add(rule);
            rule.Disabled = false;
        });
        new FwDataProjectLoader().Save(cache);
        return (cache.ProjectId.Path, CanonicalId.FromGuid(rule.Guid).Value);
    }

    private static string DraftBytes(string path)
    {
        var result = ProjectStoreCommand.Run(path, Version, (database, _) =>
            CommandOutcome<string>.Success(new ProposalRepository(database).GetDraft(DraftName).ProposalJson!));
        Assert.True(result.Succeeded, result.Refusal?.Message);
        return result.Value!;
    }
}
