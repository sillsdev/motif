using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Model;
using SIL.Motif.Model.Effects;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Model.Snapshot;

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
        var aimed = new OperationsByObject(proposal);
        return effects.Select(effect => BuildOne(effect) with
        {
            OperationIds = LinkOperations(effect, aimed, shared.Contains((effect.CanonicalId, effect.Field))),
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

    private static IReadOnlyList<string> LinkOperations(ExpectedEffect effect, OperationsByObject aimed, bool shared)
    {
        IEnumerable<OperationEnvelope> candidates = aimed.At(effect.CanonicalId)
            .Concat(aimed.ProhibitionCleanups.Where(IsProhibitionCleanup))
            .Distinct<OperationEnvelope>(ReferenceEqualityComparer.Instance);
        // One Occurrence's approval is made by the operation at its location, not by every approval in the Source.
        if (effect.Field == SnapshotFields.ScriptureOccurrenceApproval &&
            effect.Preview is { ValueKind: JsonValueKind.Object } occurrence &&
            occurrence.TryGetProperty("anchor", out var anchor) && anchor.ValueKind == JsonValueKind.String)
            candidates = aimed.AtOccurrence(effect.CanonicalId, anchor.GetString()!);
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
            effect.Preview is { ValueKind: System.Text.Json.JsonValueKind.Object } preview &&
            preview.TryGetProperty("nativeProhibitionCleanup", out var cleanup) && cleanup.ValueKind == System.Text.Json.JsonValueKind.True &&
            preview.TryGetProperty("operationId", out var origin) && origin.GetString() == operation.OperationId.Value;
    }

    // The Proposal's operations by the object each targets or creates, read once per projection.
    private sealed class OperationsByObject
    {
        private readonly Dictionary<CanonicalId, List<OperationEnvelope>> _byObject = new();
        private readonly Dictionary<(CanonicalId, string), List<OperationEnvelope>> _byOccurrence = new();

        public OperationsByObject(Proposal proposal)
        {
            var cleanups = new List<OperationEnvelope>();
            foreach (var operation in proposal.Operations)
            {
                if (operation.Target is { } target) Add(_byObject, target, operation);
                if (operation.EntityId is { } created && created != operation.Target) Add(_byObject, created, operation);
                if (operation.Kind is "grammar/moAdhocProhib/retargetReferences" or "grammar/moAdhocProhib/delete")
                    cleanups.Add(operation);
                if (operation.Target is { } source && operation.After is { ValueKind: JsonValueKind.Object } after &&
                    after.TryGetProperty("anchor", out var anchor) && anchor.ValueKind == JsonValueKind.String)
                    Add(_byOccurrence, (source, anchor.GetString()!), operation);
            }
            ProhibitionCleanups = cleanups;
        }

        public IReadOnlyList<OperationEnvelope> ProhibitionCleanups { get; }

        public IEnumerable<OperationEnvelope> At(CanonicalId id) =>
            _byObject.TryGetValue(id, out var operations) ? operations : [];

        public IEnumerable<OperationEnvelope> AtOccurrence(CanonicalId id, string anchor) =>
            _byOccurrence.TryGetValue((id, anchor), out var operations) ? operations : [];

        private static void Add<TKey>(Dictionary<TKey, List<OperationEnvelope>> index, TKey key, OperationEnvelope operation)
            where TKey : notnull
        {
            if (!index.TryGetValue(key, out var list)) index[key] = list = [];
            list.Add(operation);
        }
    }
}
