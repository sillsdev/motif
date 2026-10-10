using System.Text.Json;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Model.DryRun;
using SIL.Motif.Model.Effects;
using SIL.Motif.Projection;
using SIL.Motif.Runner.Operations;
using Xunit;

namespace SIL.Motif.Tests.Retirement;

public sealed class DryRunOperationLinkTests
{
    [Fact]
    public void DryRunEffectsLinkToTheirAuthoredOperationWithoutReorderingOperations()
    {
        var entry = Id(1);
        var bundle = Id(2);
        var retarget = Id(3);
        var delete = Id(4);
        var proposal = new Proposal(
            new Dictionary<string, string> { ["analysis"] = "1.0", ["lexical"] = "1.0" },
            Id(5), null,
            [
                new OperationEnvelope(delete, AlternateFormRetirementOperationKinds.DeleteAlternateForm,
                    target: entry, after: JsonSerializer.SerializeToElement(new { forms = new[] { Id(6).Value } }),
                    dependsOn: [new OperationDependency(retarget)]),
                new OperationEnvelope(retarget, AllomorphRetargetOperationKinds.SetBundleMorph,
                    target: bundle),
            ]);
        var dryRun = new DryRun("intent", "baseline",
            [
                Effect(bundle, "analysis/wfiMorphBundle/morph"),
                Effect(bundle, "analysis/wfiMorphBundle/form"),
                Effect(entry, "lexical/lexEntry/alternateForms"),
                Effect(Id(7), "unclassified/field"),
            ], "effects", Anchor());

        var projection = DryRunProjectionBuilder.Build(proposal.ProposalId.Value, dryRun, proposal);

        Assert.Equal(new[] { retarget.Value }, Assert.Single(projection.Effects,
            item => item.Field == "analysis/wfiMorphBundle/morph").OperationIds);
        Assert.Equal(new[] { retarget.Value }, Assert.Single(projection.Effects,
            item => item.Field == "analysis/wfiMorphBundle/form").OperationIds);
        Assert.Equal(new[] { delete.Value }, Assert.Single(projection.Effects,
            item => item.Field == "lexical/lexEntry/alternateForms").OperationIds);
        Assert.Equal(new[] { retarget.Value, delete.Value },
            projection.Operations.Select(operation => operation.OperationId));
        Assert.Empty(Assert.Single(projection.Effects, item => item.Field == "unclassified/field").OperationIds);
    }

    private static CanonicalId Id(int value) => CanonicalId.FromGuid(Guid.Parse(
        $"00000000-0000-0000-0000-{value:D12}"));

    private static ExpectedEffect Effect(CanonicalId id, string field) => new(id, field,
        new Dictionary<string, string> { ["qaa"] = "before" },
        new Dictionary<string, string> { ["qaa"] = "after" });

    private static BoundDryRunAnchor Anchor() => new("intent", "footprint", "effects", "runner", "lcm",
        "projection", "2026-10-09T00:00:00Z");
}
