using System.Runtime.CompilerServices;
using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Model.Effects;
using SIL.Motif.Runner.Composers;

namespace SIL.Motif.Runner.Operations;

public static class AdhocReferenceOperationKinds
{
    public const string Edit = "grammar/moAdhocProhib/retargetReferences";
    public const string Delete = "grammar/moAdhocProhib/delete";
    public const string StateField = "grammar/moAdhocProhib/references";

#pragma warning disable CA2255
    [ModuleInitializer]
    internal static void Register()
    {
        OperationKindRegistry.Register(Edit);
        OperationKindRegistry.Register(Delete);
        OperationHandlerRegistry.Register(Edit, new AdhocReferenceHandler(false));
        OperationHandlerRegistry.Register(Delete, new AdhocReferenceHandler(true));
        OperationSlotRegistry.RegisterMany(Edit, operation =>
        {
            var state = AdhocReferenceState.Parse(OperationSlotRegistry.After(operation).GetProperty("expected"));
            var fields = state.Kind == "allomorph"
                ? new[] { "grammar/moAlloAdhocProhib/firstAllomorph", "grammar/moAlloAdhocProhib/restOfAllos", "grammar/moAlloAdhocProhib/allomorphs" }
                : new[] { "grammar/moMorphAdhocProhib/firstMorpheme", "grammar/moMorphAdhocProhib/restOfMorphs" };
            return fields.Select(field => new OperationSlot(OperationSlotRegistry.Target(operation), field, WholeField: true)).ToArray();
        });
        OperationSlotRegistry.RegisterEntityDeletion(Delete);
    }
#pragma warning restore CA2255
}

internal static class AdhocReferenceEditing
{
    internal static void RequireExpected(IMoAdhocProhib rule, AdhocReferenceState expected)
    {
        if (!AdhocReferenceState.Read(rule).Same(expected))
            throw new InvalidOperationException($"Prohibition '{CanonicalId.FromGuid(rule.Guid)}' no longer matches expected-current state; reread it.");
    }

    internal static void Validate(LcmCache cache, AdhocReferenceState before, AdhocReferenceState after)
    {
        if (before.Kind != after.Kind || before.Adjacency != after.Adjacency || before.Disabled != after.Disabled ||
            before.Rest.Count != after.Rest.Count || before.Allomorphs.Count != after.Allomorphs.Count ||
            (before.First is null) != (after.First is null))
            throw new InvalidOperationException("A reference edit preserves kind, adjacency, Disabled and occurrence counts; use compose-delete-adhoc-prohibition to remove the prohibition.");
        if (after.Kind == "morpheme" && after.Allomorphs.Count != 0)
            throw new InvalidOperationException("Morpheme prohibitions have no legacy allomorph occurrences.");
        if (before.First is { } first && after.First is { } replacement) RequirePair(first, replacement);
        foreach (var (left, right) in before.Rest.Zip(after.Rest).Concat(before.Allomorphs.Zip(after.Allomorphs))) RequirePair(left, right);

        void RequirePair(CanonicalId sourceId, CanonicalId destinationId)
        {
            ICmObject Resolve(CanonicalId id) => before.Kind == "allomorph"
                ? ReferenceFieldLowering.Resolve<IMoForm>(cache, id, AdhocReferenceOperationKinds.Edit)
                : ReferenceFieldLowering.Resolve<IMoMorphSynAnalysis>(cache, id, AdhocReferenceOperationKinds.Edit);
            var source = Resolve(sourceId);
            var destination = Resolve(destinationId);
            if (source.Owner is not ILexEntry sourceEntry || destination.Owner is not ILexEntry destinationEntry || sourceEntry.Guid != destinationEntry.Guid)
                throw new InvalidOperationException($"Reference '{sourceId}' can only move to another {before.Kind} of the same entry; '{destinationId}' is inconsistent.");
        }
    }

    internal static void Write(LcmCache cache, IMoAdhocProhib rule, AdhocReferenceState state)
    {
        if (rule is IMoAlloAdhocProhib allo)
        {
            IMoForm Resolve(CanonicalId id) => ReferenceFieldLowering.Resolve<IMoForm>(cache, id, AdhocReferenceOperationKinds.Edit);
            allo.FirstAllomorphRA = state.First is { } first ? Resolve(first) : null;
            allo.RestOfAllosRS.Replace(0, allo.RestOfAllosRS.Count, state.Rest.Select(Resolve).ToArray());
            allo.AllomorphsRS.Replace(0, allo.AllomorphsRS.Count, state.Allomorphs.Select(Resolve).ToArray());
        }
        else
        {
            var morph = (IMoMorphAdhocProhib)rule;
            IMoMorphSynAnalysis Resolve(CanonicalId id) => ReferenceFieldLowering.Resolve<IMoMorphSynAnalysis>(cache, id, AdhocReferenceOperationKinds.Edit);
            morph.FirstMorphemeRA = state.First is { } first ? Resolve(first) : null;
            morph.RestOfMorphsRS.Replace(0, morph.RestOfMorphsRS.Count, state.Rest.Select(Resolve).ToArray());
        }
    }

    internal static Dictionary<string, string> Value(AdhocReferenceState state) => new() { ["state"] = JsonSerializer.Serialize(state.Wire()) };
}

internal sealed class AdhocReferenceHandler(bool delete) : ICompoundOperationHandler
{
    private string Kind => delete ? AdhocReferenceOperationKinds.Delete : AdhocReferenceOperationKinds.Edit;

    public ExpectedEffect ApplyAndCaptureEffect(LcmCache cache, OperationEnvelope operation, List<CanonicalId> touchedTargets) =>
        ApplyAndCaptureEffects(cache, operation, touchedTargets)[0];

    public IReadOnlyList<ExpectedEffect> ApplyAndCaptureEffects(LcmCache cache, OperationEnvelope operation, List<CanonicalId> touchedTargets)
    {
        var (id, rule, expected, after) = Resolve(cache, operation);
        AdhocReferenceEditing.RequireExpected(rule, expected);
        var cleanup = ProhibitionMsaCleanup.Read(cache, rule);
        var before = AdhocReferenceEditing.Value(AdhocReferenceState.Read(rule));
        JsonElement preview;
        if (delete)
        {
            OwnedGraphGuard.RequireNoExternalReferences(cache, rule);
            if (OwnedGraphGuard.Digest(cache, rule) != after.GetProperty("graphDigest").GetString())
                throw new InvalidOperationException("The prohibition's owned content changed; recompose its deletion.");
            preview = JsonSerializer.SerializeToElement(new { summary = $"Delete ad hoc prohibition {id.Value}; review native grammatical-analysis cleanup in the accompanying deletions.",
                deletedProhibition = id.Value, deleted = expected.Wire(),
                kept = "Referenced entries and allomorphs are kept. Native grammatical-analysis and reference deletions are listed separately." });
            rule.Delete();
        }
        else
        {
            var desired = AdhocReferenceState.Parse(after.GetProperty("references"));
            AdhocReferenceEditing.Validate(cache, expected, desired);
            AdhocReferenceEditing.Write(cache, rule, desired);
            preview = JsonSerializer.SerializeToElement(new { summary = $"Retarget ad hoc prohibition {id.Value}; keep adjacency, Disabled, occurrence order and all other content.",
                retargetedProhibition = id.Value, before = expected.Wire(), after = desired.Wire(),
                kept = "Adjacency, Disabled, order, entry identities and all other prohibition content are kept." });
        }
        touchedTargets.Add(id);
        return new[] { new ExpectedEffect(id, AdhocReferenceOperationKinds.StateField, before,
            delete ? new Dictionary<string, string>() : AdhocReferenceEditing.Value(AdhocReferenceState.Read(rule)), preview) }
            .Concat(cleanup.Changes(cache, operation, touchedTargets)).ToArray();
    }

    public ExpectedEffect ReadCurrentFootprint(LcmCache cache, OperationEnvelope operation) =>
        ReadCurrentFootprintEffects(cache, operation)[0];

    public IReadOnlyList<ExpectedEffect> ReadCurrentFootprintEffects(LcmCache cache, OperationEnvelope operation)
    {
        var (id, rule, _, _) = Resolve(cache, operation);
        var state = AdhocReferenceEditing.Value(AdhocReferenceState.Read(rule));
        state["graphDigest"] = OwnedGraphGuard.Digest(cache, rule);
        state["references"] = OwnedGraphGuard.ReferenceDigest(cache, rule);
        return new[] { new ExpectedEffect(id, AdhocReferenceOperationKinds.StateField, state, state) }
            .Concat(ProhibitionMsaCleanup.Read(cache, rule).Footprint()).ToArray();
    }

    private (CanonicalId, IMoAdhocProhib, AdhocReferenceState, JsonElement) Resolve(LcmCache cache, OperationEnvelope operation)
    {
        var after = OperationSlotRegistry.After(operation);
        ClosedPayloadParsing.RequireObject(after, Kind);
        ClosedPayloadParsing.RejectUnknownProperties(after, delete ? ["expected", "graphDigest"] : ["expected", "references"], Kind);
        var expected = AdhocReferenceState.Parse(after.GetProperty("expected"));
        var (id, rule) = TargetResolution.Resolve<IMoAdhocProhib>(cache, operation, Kind);
        return (id, rule, expected, after);
    }
}
