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

    public static IReadOnlyList<EffectView> Build(IReadOnlyList<ExpectedEffect> effects, Proposal proposal)
    {
        // Effects sharing one object and field link to the operation that made their value, not all aimed at it.
        var shared = effects.GroupBy(effect => (effect.CanonicalId, effect.Field)).Where(group => group.Count() > 1)
            .Select(group => group.Key).ToHashSet();
        return effects.Select(effect => BuildOne(effect) with
        {
            OperationIds = LinkOperations(effect, proposal, shared.Contains((effect.CanonicalId, effect.Field))),
        }).ToList();
    }

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

    private static IReadOnlyList<string> LinkOperations(ExpectedEffect effect, Proposal proposal, bool shared)
    {
        var candidates = proposal.Operations.Where(operation => operation.Target == effect.CanonicalId ||
            operation.EntityId == effect.CanonicalId || IsProhibitionCleanup(operation));
        if (effect.Field is "analysis/wfiMorphBundle/morph" or "analysis/wfiMorphBundle/form")
            candidates = candidates.Where(operation => operation.Kind == "analysis/wfiMorphBundle/setMorph");
        else if (effect.Field == "lexical/lexEntry/alternateForms")
            candidates = candidates.Where(operation => operation.Kind is
                "lexical/lexEntry/deleteAlternateForm" or "lexical/lexEntry/moveAlternateForms" or
                "lexical/lexEntry/createAlternateForms");
        var linked = candidates.ToArray();
        if (shared)
        {
            var values = effect.Before.Values.Concat(effect.After.Values).ToHashSet(System.StringComparer.Ordinal);
            // Drop only creators of other values; the object's creator and edits, moves and deletes stay.
            var own = linked.Where(operation => operation.EntityId is not { } created ||
                created == effect.CanonicalId || values.Contains(created.Value)).ToArray();
            if (own.Length > 0) linked = own;
        }
        return linked.Select(operation => operation.OperationId.Value)
            .OrderBy(id => id, System.StringComparer.Ordinal).ToArray();

        bool IsProhibitionCleanup(OperationEnvelope operation) =>
            operation.Kind is "grammar/moAdhocProhib/retargetReferences" or "grammar/moAdhocProhib/delete" &&
            effect.Preview is { ValueKind: System.Text.Json.JsonValueKind.Object } preview &&
            preview.TryGetProperty("nativeProhibitionCleanup", out var cleanup) && cleanup.ValueKind == System.Text.Json.JsonValueKind.True &&
            preview.TryGetProperty("operationId", out var origin) && origin.GetString() == operation.OperationId.Value;
    }
}
