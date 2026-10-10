using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Model;
using SIL.Motif.Model.Effects;
using System.Collections.Generic;
using System.Linq;

namespace SIL.Motif.Projection;

/// <summary>Shapes a DryRun's or a Receipt's effect set into <see cref="EffectView"/>s for a report.</summary>
public static class EffectProjectionBuilder
{
    public static IReadOnlyList<EffectView> Build(IReadOnlyList<ExpectedEffect> effects) =>
        effects.Select(BuildOne).ToList();

    public static IReadOnlyList<EffectView> Build(IReadOnlyList<ExpectedEffect> effects, Proposal proposal) =>
        effects.Select(effect => BuildOne(effect) with
        {
            OperationIds = LinkOperations(effect, proposal),
        }).ToList();

    private static EffectView BuildOne(ExpectedEffect effect)
    {
        var wsKeys = effect.Before.Keys.Union(effect.After.Keys).OrderBy(k => k, System.StringComparer.Ordinal);

        var changes = new List<EffectChange>();
        foreach (var ws in wsKeys)
        {
            var before = effect.Before.TryGetValue(ws, out var b) ? b : null;
            var after = effect.After.TryGetValue(ws, out var a) ? a : null;
            if (before == after)
                continue;

            changes.Add(new EffectChange(ws, before, after));
        }

        return new EffectView(effect.CanonicalId.Value, effect.Field, changes, effect.Preview);
    }

    private static IReadOnlyList<string> LinkOperations(ExpectedEffect effect, Proposal proposal)
    {
        var candidates = proposal.Operations.Where(operation => operation.Target == effect.CanonicalId ||
            operation.EntityId == effect.CanonicalId);
        if (effect.Field is "analysis/wfiMorphBundle/morph" or "analysis/wfiMorphBundle/form")
            candidates = candidates.Where(operation => operation.Kind == "analysis/wfiMorphBundle/setMorph");
        else if (effect.Field == "lexical/lexEntry/alternateForms")
            candidates = candidates.Where(operation => operation.Kind is
                "lexical/lexEntry/deleteAlternateForm" or "lexical/lexEntry/moveAlternateForms");
        return candidates.Select(operation => operation.OperationId.Value)
            .OrderBy(id => id, System.StringComparer.Ordinal).ToArray();
    }
}
