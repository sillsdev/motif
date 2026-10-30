using System.Runtime.CompilerServices;
using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Model.Effects;
using SIL.Motif.Model.Snapshot;
using SIL.Motif.Runner.Snapshotting;

namespace SIL.Motif.Runner.Operations;

/// <summary>The operation kind that moves one top-level sense, with its sub-senses, into another entry.</summary>
public static class LexSenseMoveOperationKinds
{
    /// <summary>Moves the targeted <c>LexSense</c> to the end of a new entry's senses and points it at that entry's analysis.</summary>
    public const string MoveToEntry = "lexical/lexSense/moveToEntry";

#pragma warning disable CA2255 // Load-time registration makes this kind available before parsing and dispatch.
    [ModuleInitializer]
    internal static void Register()
    {
        OperationKindRegistry.Register(MoveToEntry);
        OperationHandlerRegistry.Register(MoveToEntry, LexSenseMoveHandler.Instance);
        OperationSlotRegistry.RegisterMany(MoveToEntry, operation =>
        {
            var sense = OperationSlotRegistry.Target(operation);
            var payload = LexSenseMovePayload.Parse(OperationSlotRegistry.After(operation));
            var member = OperationSlotRegistry.MemberKey(sense);
            var slots = new List<OperationSlot>
            {
                new(sense, SnapshotFields.LexSenseMorphoSyntaxAnalysis),
                new(payload.FromEntry, SnapshotFields.LexEntrySenses, member),
                new(payload.Entry, SnapshotFields.LexEntrySenses, member),
            };
            if (payload.DeleteMsa is { } msa)
            {
                slots.Add(new OperationSlot(msa, null));
                slots.Add(new OperationSlot(payload.FromEntry, SnapshotFields.LexEntryMorphoSyntaxAnalyses,
                    OperationSlotRegistry.MemberKey(msa)));
            }
            return slots;
        });
    }
#pragma warning restore CA2255
}

/// <summary>
/// The closed payload of a sense move. The new entry and its analysis are usually created by earlier operations of
/// the same Draft, so they are named here and resolved only when the move is applied.
/// </summary>
/// <param name="Entry">The new entry, which must own no sense when the move is applied.</param>
/// <param name="Msa">The analysis, owned by <paramref name="Entry"/>, that the moved senses point at afterwards.</param>
/// <param name="FromEntry">The entry the sense leaves; a different owner makes the Draft stale.</param>
/// <param name="DeleteMsa">The analysis of <paramref name="FromEntry"/> that no sense uses once the sense has gone, or null.</param>
public sealed record LexSenseMovePayload(CanonicalId Entry, CanonicalId Msa, CanonicalId FromEntry, CanonicalId? DeleteMsa)
{
    /// <summary>Parses the closed payload, refusing any other property and a missing <c>deleteMsa</c>.</summary>
    public static LexSenseMovePayload Parse(JsonElement after)
    {
        const string kind = LexSenseMoveOperationKinds.MoveToEntry;
        ClosedPayloadParsing.RequireObject(after, kind);
        ClosedPayloadParsing.RejectUnknownProperties(after, ["entry", "msa", "fromEntry", "deleteMsa"], kind);
        if (!after.TryGetProperty("deleteMsa", out var delete))
            throw new ContractParseException($"'{kind}' operation 'after.deleteMsa' is required: an id, or null for none.");
        return new LexSenseMovePayload(
            CanonicalId.FromGuid(ClosedPayloadParsing.GetRequiredCanonicalId(after, "entry", kind).ToGuid()),
            CanonicalId.FromGuid(ClosedPayloadParsing.GetRequiredCanonicalId(after, "msa", kind).ToGuid()),
            CanonicalId.FromGuid(ClosedPayloadParsing.GetRequiredCanonicalId(after, "fromEntry", kind).ToGuid()),
            delete.ValueKind == JsonValueKind.Null ? null :
                CanonicalId.FromGuid(ClosedPayloadParsing.GetRequiredCanonicalId(after, "deleteMsa", kind).ToGuid()));
    }
}

/// <summary>
/// Moves a sense the way FieldWorks' "move sense to a copy of its entry" does: the sense keeps its identity, text,
/// examples and sub-senses; it points at an analysis of the new entry; and the old entry's analysis is removed when
/// nothing uses it any more. The new entry is built by ordinary creates in the same Draft.
/// </summary>
internal sealed class LexSenseMoveHandler : ICompoundOperationHandler
{
    internal static readonly LexSenseMoveHandler Instance = new();
    private const string Kind = LexSenseMoveOperationKinds.MoveToEntry;

    private LexSenseMoveHandler()
    {
    }

    public ExpectedEffect ApplyAndCaptureEffect(
        LcmCache cache, OperationEnvelope operation, List<CanonicalId> touchedTargets) =>
        ApplyAndCaptureEffects(cache, operation, touchedTargets)[0];

    public IReadOnlyList<ExpectedEffect> ApplyAndCaptureEffects(
        LcmCache cache, OperationEnvelope operation, List<CanonicalId> touchedTargets)
    {
        var (senseId, sense, from, payload) = ResolveSource(cache, operation);
        var repository = cache.ServiceLocator.ObjectRepository;
        if (!repository.TryGetObject(payload.Entry.ToGuid(), out var found) || found is not ILexEntry target)
            throw new InvalidOperationException($"'{Kind}' needs the new entry '{payload.Entry.Value}' to exist; create it first.");
        if (target.Guid == from.Guid)
            throw new InvalidOperationException($"'{Kind}' moves a sense to a different entry.");
        if (target.SensesOS.Count != 0)
            throw new InvalidOperationException($"'{Kind}' moves a sense only into a new entry that has no sense yet.");
        if (!repository.TryGetObject(payload.Msa.ToGuid(), out var foundMsa) || foundMsa is not IMoMorphSynAnalysis msa ||
            msa.Owner?.Guid != target.Guid)
            throw new InvalidOperationException($"'{Kind}' needs the analysis '{payload.Msa.Value}' owned by the new entry.");
        var moved = sense.AllSenses.ToArray();
        var oldMsa = RequireMovable(sense, moved, from);
        var deleted = RequireDeletable(from, moved, oldMsa, payload);

        var fromBefore = LexSenseDeleteHandler.ReadSenses(from);
        var targetBefore = LexSenseDeleteHandler.ReadSenses(target);
        var msasBefore = LexSenseDeleteHandler.ReadAnalyses(from);
        var analysisBefore = moved.ToDictionary(item => item,
            item => ReferenceFieldSnapshotting.ReadAlternatives(item.MorphoSyntaxAnalysisRA));
        var preview = Preview(from, sense, oldMsa, deleted, payload);

        foreach (var item in moved)
            if (item.MorphoSyntaxAnalysisRA is not null || item.Guid == sense.Guid)
                item.MorphoSyntaxAnalysisRA = msa;
        target.SensesOS.Add(sense);
        if (deleted is { IsValidObject: true })
            from.MorphoSyntaxAnalysesOC.Remove(deleted);
        touchedTargets.Add(senseId);
        touchedTargets.Add(payload.Entry);

        var effects = new List<ExpectedEffect>
        {
            new(CanonicalId.FromGuid(from.Guid), SnapshotFields.LexEntrySenses, fromBefore,
                LexSenseDeleteHandler.ReadSenses(from), preview),
            new(payload.Entry, SnapshotFields.LexEntrySenses, targetBefore, LexSenseDeleteHandler.ReadSenses(target)),
        };
        effects.AddRange(moved.Select(item => new ExpectedEffect(CanonicalId.FromGuid(item.Guid),
            SnapshotFields.LexSenseMorphoSyntaxAnalysis, analysisBefore[item],
            ReferenceFieldSnapshotting.ReadAlternatives(item.MorphoSyntaxAnalysisRA))));
        if (deleted is not null)
            effects.Add(new(CanonicalId.FromGuid(from.Guid), SnapshotFields.LexEntryMorphoSyntaxAnalyses, msasBefore,
                LexSenseDeleteHandler.ReadAnalyses(from)));
        return effects;
    }

    public ExpectedEffect ReadCurrentFootprint(LcmCache cache, OperationEnvelope operation) =>
        ReadCurrentFootprintEffects(cache, operation)[0];

    /// <summary>
    /// Reads only what exists now. The new entry and its analysis may be created by an earlier operation of the
    /// Draft, so they are read when present and otherwise count as empty; they are validated when applied.
    /// </summary>
    public IReadOnlyList<ExpectedEffect> ReadCurrentFootprintEffects(LcmCache cache, OperationEnvelope operation)
    {
        var (_, sense, from, payload) = ResolveSource(cache, operation);
        var fromSenses = LexSenseDeleteHandler.ReadSenses(from);
        var targetSenses = cache.ServiceLocator.ObjectRepository.TryGetObject(payload.Entry.ToGuid(), out var found) &&
                           found is ILexEntry target
            ? LexSenseDeleteHandler.ReadSenses(target) : new Dictionary<string, string>();
        var moved = sense.AllSenses.ToArray();
        var oldMsa = sense.MorphoSyntaxAnalysisRA;
        var deleted = payload.DeleteMsa is null ? null : oldMsa;
        var effects = new List<ExpectedEffect>
        {
            new(CanonicalId.FromGuid(from.Guid), SnapshotFields.LexEntrySenses, fromSenses, fromSenses,
                Preview(from, sense, oldMsa, deleted, payload)),
            new(payload.Entry, SnapshotFields.LexEntrySenses, targetSenses, targetSenses),
        };
        foreach (var item in moved)
        {
            var current = ReferenceFieldSnapshotting.ReadAlternatives(item.MorphoSyntaxAnalysisRA);
            effects.Add(new(CanonicalId.FromGuid(item.Guid), SnapshotFields.LexSenseMorphoSyntaxAnalysis, current, current));
        }
        if (payload.DeleteMsa is not null)
        {
            var analyses = LexSenseDeleteHandler.ReadAnalyses(from);
            effects.Add(new(CanonicalId.FromGuid(from.Guid), SnapshotFields.LexEntryMorphoSyntaxAnalyses, analyses, analyses));
        }
        return effects;
    }

    private static (CanonicalId Id, ILexSense Sense, ILexEntry From, LexSenseMovePayload Payload) ResolveSource(
        LcmCache cache, OperationEnvelope operation)
    {
        var payload = LexSenseMovePayload.Parse(operation.After ??
            throw new ContractParseException($"'{Kind}' operation requires 'after'."));
        var (id, sense) = TargetResolution.Resolve<ILexSense>(cache, operation, Kind);
        if (sense.Owner is not ILexEntry from)
            throw new InvalidOperationException($"'{Kind}' moves only a top-level sense of an entry.");
        if (from.Guid != payload.FromEntry.ToGuid())
            throw new InvalidOperationException($"'{Kind}' names an entry the sense no longer belongs to; recompute the Draft.");
        if (from.SensesOS.Count < 2)
            throw new InvalidOperationException(
                $"'{Kind}' refuses the only sense of an entry, as FieldWorks does: an entry must keep a sense.");
        return (id, sense, from, payload);
    }

    /// <summary>Throws when a moved sense has something the move cannot carry; returns the sense's current analysis.</summary>
    internal static IMoMorphSynAnalysis? RequireMovable(ILexSense sense, ILexSense[] moved, ILexEntry from)
    {
        var oldMsa = sense.MorphoSyntaxAnalysisRA;
        foreach (var item in moved)
        {
            if (item.MorphoSyntaxAnalysisRA is { } own && own.Guid != oldMsa?.Guid)
                throw new InvalidOperationException(
                    $"'{Kind}' refuses a sub-sense whose analysis differs from the sense's own; retarget it first.");
            if (item.ReferringObjects.OfType<IWfiMorphBundle>().Any())
                throw new InvalidOperationException(
                    $"'{Kind}' refuses a sense that parser analyses (morph bundles) still refer to; retarget them first.");
        }
        _ = from;
        return oldMsa;
    }

    private static IMoMorphSynAnalysis? RequireDeletable(
        ILexEntry from, ILexSense[] moved, IMoMorphSynAnalysis? oldMsa, LexSenseMovePayload payload)
    {
        if (payload.DeleteMsa is not { } named)
            return null;
        if (oldMsa is null || oldMsa.Guid != named.ToGuid())
            throw new InvalidOperationException($"'{Kind}' names an analysis the sense no longer points at; recompute the Draft.");
        if (oldMsa.Owner?.Guid != from.Guid)
            throw new InvalidOperationException($"'{Kind}' removes only an analysis the sense's own entry owns.");
        if (!Unused(from, moved, oldMsa))
            throw new InvalidOperationException($"'{Kind}' refuses an analysis another sense or object still uses; recompute the Draft.");
        return oldMsa;
    }

    /// <summary>True when no sense left in <paramref name="from"/> and no other object uses <paramref name="msa"/>.</summary>
    internal static bool Unused(ILexEntry from, IReadOnlyCollection<ILexSense> moved, IMoMorphSynAnalysis msa) =>
        !from.AllSenses.Any(other => moved.All(item => item.Guid != other.Guid) &&
                                     other.MorphoSyntaxAnalysisRA?.Guid == msa.Guid) &&
        msa.ReferringObjects.All(other => moved.Any(item => item.Guid == other.Guid));

    private static JsonElement Preview(
        ILexEntry from, ILexSense sense, IMoMorphSynAnalysis? oldMsa, IMoMorphSynAnalysis? deleted, LexSenseMovePayload payload)
    {
        var index = from.SensesOS.IndexOf(sense);
        string? Neighbour(int at) => at >= 0 && at < from.SensesOS.Count
            ? CanonicalId.FromGuid(from.SensesOS[at].Guid).Value : null;
        return JsonSerializer.SerializeToElement(new
        {
            movedSense = new
            {
                id = CanonicalId.FromGuid(sense.Guid).Value,
                fromEntry = CanonicalId.FromGuid(from.Guid).Value,
                index,
                after = Neighbour(index - 1),
                before = Neighbour(index + 1),
                subsenses = sense.AllSenses.Count() - 1,
                examples = sense.AllSenses.Sum(item => item.ExamplesOS.Count),
                oldAnalysis = oldMsa is null ? null : CanonicalId.FromGuid(oldMsa.Guid).Value,
            },
            toEntry = payload.Entry.Value,
            toAnalysis = payload.Msa.Value,
            deletedAnalysis = deleted is null ? null : new
            {
                id = CanonicalId.FromGuid(deleted.Guid).Value,
                @class = deleted.ClassName,
            },
            reversal = "move the sense back to the end of the old entry's senses at the recorded index, " +
                "recreate the deleted analysis with the same class and id, and point the sense and its sub-senses at it",
        });
    }
}
