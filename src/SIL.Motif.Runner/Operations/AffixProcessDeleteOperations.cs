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

public static class AffixProcessDeleteOperationKinds
{
    public const string Delete = "lexical/moAffixProcess/deleteGraph";
    public const string GraphField = "lexical/moAffixProcess/ownedGraph";

#pragma warning disable CA2255
    [ModuleInitializer]
    internal static void Register()
    {
        OperationKindRegistry.Register(Delete);
        OperationHandlerRegistry.Register(Delete, new AffixProcessDeleteHandler());
        OperationSlotRegistry.RegisterMany(Delete, operation =>
        {
            var payload = AffixProcessDeletePayload.Parse(OperationSlotRegistry.After(operation));
            var slots = payload.Members.Select(id => new OperationSlot(id, null)).ToList();
            if (payload.Lexeme) slots.Add(new(payload.Entry, SnapshotFields.LexEntryLexemeForm));
            else slots.Add(new(payload.Entry, SnapshotFields.LexEntryAlternateForms,
                OperationSlotRegistry.MemberKey(OperationSlotRegistry.Target(operation))));
            if (payload.Lexeme && payload.Replacement is { } replacement)
                slots.Add(new(replacement, "$owner"));
            return slots;
        });
    }
#pragma warning restore CA2255
}

internal sealed record AffixProcessDeletePayload(CanonicalId Entry, bool Lexeme, CanonicalId? Replacement, string GraphDigest, IReadOnlyList<CanonicalId> Members)
{
    internal static AffixProcessDeletePayload Parse(JsonElement value)
    {
        const string kind = AffixProcessDeleteOperationKinds.Delete;
        ClosedPayloadParsing.RequireObject(value, kind);
        ClosedPayloadParsing.RejectUnknownProperties(value, ["entry", "lexeme", "replacement", "graphDigest", "members"], kind);
        var replacement = value.GetProperty("replacement");
        return new(ClosedPayloadParsing.GetRequiredCanonicalId(value, "entry", kind), value.GetProperty("lexeme").GetBoolean(),
            replacement.ValueKind == JsonValueKind.Null ? null : ClosedPayloadParsing.GetRequiredCanonicalId(value, "replacement", kind),
            ClosedPayloadParsing.GetRequiredString(value, "graphDigest", kind), RetargetPayloadParsing.Ids(value, "members", kind));
    }
}

internal static class AffixProcessDeletion
{
    internal static IMoAffixAllomorph? Validate(LcmCache cache, ILexEntry entry, IMoAffixProcess process, CanonicalId? replacementId)
    {
        if (process.Owner?.Guid != entry.Guid || entry.LexemeFormOA?.Guid != process.Guid && !entry.AlternateFormsOS.Contains(process))
            throw new InvalidOperationException("The process must be a lexeme or alternate form of the named entry.");
        IMoAffixAllomorph? replacement = null;
        if (replacementId is { } id)
        {
            replacement = ReferenceFieldLowering.Resolve<IMoAffixAllomorph>(cache, id, AffixProcessDeleteOperationKinds.Delete);
            if (replacement.Owner?.Guid != entry.Guid || !entry.AlternateFormsOS.Contains(replacement) && entry.LexemeFormOA?.Guid != replacement.Guid)
                throw new InvalidOperationException("The replacement must be a plain allomorph of the same entry.");
            if (replacement.MorphTypeRA?.Guid != process.MorphTypeRA?.Guid)
                throw new InvalidOperationException("The replacement must keep the process's morph type.");
        }
        OwnedGraphGuard.RequireNoExternalReferences(cache, process);
        return replacement;
    }
}

internal sealed class AffixProcessDeleteHandler : ICompoundOperationHandler
{
    public ExpectedEffect ApplyAndCaptureEffect(LcmCache cache, OperationEnvelope operation, List<CanonicalId> touchedTargets) =>
        ApplyAndCaptureEffects(cache, operation, touchedTargets)[0];

    public IReadOnlyList<ExpectedEffect> ApplyAndCaptureEffects(LcmCache cache, OperationEnvelope operation, List<CanonicalId> touchedTargets)
    {
        var payload = AffixProcessDeletePayload.Parse(OperationSlotRegistry.After(operation));
        var (id, process) = TargetResolution.Resolve<IMoAffixProcess>(cache, operation, AffixProcessDeleteOperationKinds.Delete);
        var entry = ReferenceFieldLowering.Resolve<ILexEntry>(cache, payload.Entry, AffixProcessDeleteOperationKinds.Delete);
        var replacement = AffixProcessDeletion.Validate(cache, entry, process, payload.Replacement);
        if (OwnedGraphGuard.Digest(cache, process) != payload.GraphDigest ||
            !OwnedGraphGuard.Members(process).Select(item => CanonicalId.FromGuid(item.Guid)).SequenceEqual(payload.Members))
            throw new InvalidOperationException("The whole process graph changed; reread and recompose its deletion.");
        var lexeme = entry.LexemeFormOA?.Guid == process.Guid;
        if (lexeme != payload.Lexeme)
            throw new InvalidOperationException("The process moved between lexeme and alternate ownership; recompose its deletion.");
        var beforeLexeme = ReferenceFieldSnapshotting.ReadAlternatives(entry.LexemeFormOA);
        var beforeAlternates = ReferenceSequenceFieldSnapshotting.ReadAlternatives(entry.AlternateFormsOS);
        var replacementForm = RemovalPreview.AllWritingSystems(cache, replacement?.Form);
        var preview = JsonSerializer.SerializeToElement(new
        {
            summary = $"Delete affix process {id.Value} and its entire owned graph; " +
                (replacement is null ? "select no replacement. " :
                    $"keep plain replacement {payload.Replacement!.Value.Value} ({string.Join(", ", replacementForm.Select(pair => $"{pair.Key}: {pair.Value}"))}). ") +
                "Keep the entry, senses, grammatical analyses, other forms and retargeted prohibitions.",
            deletedProcess = id.Value, deletedGraphMembers = payload.Members.Select(member => member.Value).ToArray(),
            replacement = payload.Replacement?.Value, replacementForm,
            kept = "The entry, its senses, grammatical analyses, other allomorphs and explicitly retargeted prohibitions are kept.",
            action = replacement is null ? "Delete the entire affix process graph." : lexeme
                ? "Delete the entire affix process graph and promote the plain allomorph to lexeme form."
                : "Delete the entire affix process graph and keep the plain replacement allomorph.",
        });
        touchedTargets.AddRange(payload.Members);
        touchedTargets.Add(payload.Entry);
        process.Delete();
        if (lexeme && replacement is not null) entry.LexemeFormOA = replacement;
        if (payload.Members.Any(member => cache.ServiceLocator.ObjectRepository.TryGetObject(member.ToGuid(), out _)))
            throw new InvalidOperationException("LibLCM left a member of the deleted process graph.");
        return [new(id, AffixProcessDeleteOperationKinds.GraphField, new Dictionary<string, string> { ["digest"] = payload.GraphDigest },
                new Dictionary<string, string>(), preview),
            new(payload.Entry, SnapshotFields.LexEntryLexemeForm, beforeLexeme, ReferenceFieldSnapshotting.ReadAlternatives(entry.LexemeFormOA)),
            new(payload.Entry, SnapshotFields.LexEntryAlternateForms, beforeAlternates, ReferenceSequenceFieldSnapshotting.ReadAlternatives(entry.AlternateFormsOS))];
    }

    public ExpectedEffect ReadCurrentFootprint(LcmCache cache, OperationEnvelope operation) => ReadCurrentFootprintEffects(cache, operation)[0];

    public IReadOnlyList<ExpectedEffect> ReadCurrentFootprintEffects(LcmCache cache, OperationEnvelope operation)
    {
        var payload = AffixProcessDeletePayload.Parse(OperationSlotRegistry.After(operation));
        var (id, process) = TargetResolution.Resolve<IMoAffixProcess>(cache, operation, AffixProcessDeleteOperationKinds.Delete);
        var entry = ReferenceFieldLowering.Resolve<ILexEntry>(cache, payload.Entry, AffixProcessDeleteOperationKinds.Delete);
        var graph = new Dictionary<string, string> { ["digest"] = OwnedGraphGuard.Digest(cache, process),
            ["references"] = OwnedGraphGuard.ReferenceDigest(cache, process) };
        if (payload.Replacement is { } replacement)
            graph["replacementDigest"] = cache.ServiceLocator.ObjectRepository.TryGetObject(replacement.ToGuid(), out var survivor)
                ? OwnedGraphGuard.Digest(cache, survivor) : "created by this Proposal";
        var lexeme = ReferenceFieldSnapshotting.ReadAlternatives(entry.LexemeFormOA);
        var alternates = ReferenceSequenceFieldSnapshotting.ReadAlternatives(entry.AlternateFormsOS);
        return [new(id, AffixProcessDeleteOperationKinds.GraphField, graph, graph),
            new(payload.Entry, SnapshotFields.LexEntryLexemeForm, lexeme, lexeme),
            new(payload.Entry, SnapshotFields.LexEntryAlternateForms, alternates, alternates)];
    }
}
