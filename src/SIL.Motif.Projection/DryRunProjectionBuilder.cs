using DryRunResult = SIL.Motif.Model.DryRun.DryRun;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Responses;
using System.Collections.Generic;
using System.Linq;

namespace SIL.Motif.Projection;

/// <summary>Shapes a Runner <see cref="DryRunResult"/> into a <see cref="DryRunProjection"/>.</summary>
public static class DryRunProjectionBuilder
{
    public static DryRunProjection Build(string proposalId, DryRunResult dryRun, Proposal? proposal = null)
    {
        var projection = new DryRunProjection(proposalId, dryRun.IntentDigest, dryRun.BaselineNote,
            proposal is null ? EffectProjectionBuilder.Build(dryRun.ExpectedEffects) :
                EffectProjectionBuilder.Build(dryRun.ExpectedEffects, proposal), dryRun.EffectDigest,
            dryRun.Anchor.FootprintDigest);
        return proposal is null ? projection : projection with { Operations = OrderOperations(proposal) };
    }

    private static IReadOnlyList<ProposalOperationView> OrderOperations(Proposal proposal)
    {
        var operations = proposal.Operations.ToDictionary(item => item.OperationId.Value, System.StringComparer.Ordinal);
        if (operations.Values.SelectMany(operation => operation.DependsOn)
            .Any(dependency => !operations.ContainsKey(dependency.OperationId.Value)))
            throw new System.IO.InvalidDataException("The Proposal dependency graph names an absent operation.");
        var remaining = operations.Keys.ToHashSet(System.StringComparer.Ordinal);
        var emitted = new List<ProposalOperationView>(operations.Count);
        while (remaining.Count > 0)
        {
            var ready = remaining.Select(id => operations[id])
                .Where(operation => operation.DependsOn.All(dependency =>
                    !remaining.Contains(dependency.OperationId.Value)))
                .OrderBy(operation => operation.OperationId.Value, System.StringComparer.Ordinal).ToArray();
            if (ready.Length == 0) throw new System.IO.InvalidDataException("The Proposal dependency graph has a cycle.");
            foreach (var operation in ready)
            {
                remaining.Remove(operation.OperationId.Value);
                emitted.Add(new ProposalOperationView(operation.OperationId.Value, operation.Kind,
                    operation.Target?.Value, operation.EntityId?.Value,
                    operation.DependsOn.Select(dependency => dependency.OperationId.Value)
                        .OrderBy(id => id, System.StringComparer.Ordinal).ToArray(),
                    operation.After?.GetRawText()));
            }
        }
        return emitted;
    }
}
