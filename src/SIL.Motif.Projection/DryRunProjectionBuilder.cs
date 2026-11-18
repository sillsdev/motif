using DryRunResult = SIL.Motif.Model.DryRun.DryRun;
using SIL.Motif.Contract.Ids;
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
        var effects = proposal is null ? EffectProjectionBuilder.Build(dryRun.ExpectedEffects) :
            EffectProjectionBuilder.Build(dryRun.ExpectedEffects, proposal);
        var projection = new DryRunProjection(proposalId, dryRun.IntentDigest, dryRun.BaselineNote,
            effects, dryRun.EffectDigest, dryRun.Anchor.FootprintDigest)
        {
            Warnings = ExtractWarnings(effects),
        };
        return proposal is null ? projection : projection with { Operations = OrderOperations(proposal) };
    }

    internal static IReadOnlyList<string> ExtractWarnings(IReadOnlyList<EffectView> effects) => effects
        .Where(effect => effect.Field == "dryRunWarning")
        .Select(effect => effect.Preview is { ValueKind: System.Text.Json.JsonValueKind.Object } preview &&
            preview.TryGetProperty("warning", out var warning) && warning.ValueKind == System.Text.Json.JsonValueKind.String
                ? warning.GetString() : null)
        .OfType<string>()
        .Distinct(System.StringComparer.Ordinal)
        .ToArray();

    private static IReadOnlyList<ProposalOperationView> OrderOperations(Proposal proposal)
    {
        // Keyed by identity, so a dependency spelled with another prefix still names its operation (ADR 0012).
        var operations = proposal.Operations.ToDictionary(item => item.OperationId);
        if (operations.Values.SelectMany(operation => operation.DependsOn)
            .Any(dependency => !operations.ContainsKey(dependency.OperationId)))
            throw new System.IO.InvalidDataException("The Proposal dependency graph names an absent operation.");
        var remaining = operations.Keys.ToHashSet();
        var emitted = new List<ProposalOperationView>(operations.Count);
        while (remaining.Count > 0)
        {
            var ready = remaining.Select(id => operations[id])
                .Where(operation => operation.DependsOn.All(dependency =>
                    !remaining.Contains(dependency.OperationId)))
                .OrderBy(operation => operation.OperationId, CanonicalId.SpellingOrder).ToArray();
            if (ready.Length == 0) throw new System.IO.InvalidDataException("The Proposal dependency graph has a cycle.");
            foreach (var operation in ready)
            {
                remaining.Remove(operation.OperationId);
                emitted.Add(new ProposalOperationView(operation.OperationId.Value, operation.Kind,
                    operation.Target?.Value, operation.EntityId?.Value,
                    operation.DependsOn.Select(dependency => dependency.OperationId.Value)
                        .OrderBy(id => id, System.StringComparer.Ordinal).ToArray(),
                    operation.After?.GetRawText(), CitationView.Of(operation)));
            }
        }
        return emitted;
    }
}
