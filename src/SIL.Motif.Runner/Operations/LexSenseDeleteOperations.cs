using System.Runtime.CompilerServices;
using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.Cellar;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Model.Effects;
using SIL.Motif.Model.Snapshot;

namespace SIL.Motif.Runner.Operations;

/// <summary>The operation kind that deletes one wholly empty lexical sense and, optionally, its orphaned analysis.</summary>
public static class LexSenseDeleteOperationKinds
{
    /// <summary>Deletes the targeted <c>LexSense</c> and the <c>MoMorphSynAnalysis</c> its payload names, if any.</summary>
    public const string DeleteSense = "lexical/lexSense/delete";

#pragma warning disable CA2255 // Load-time registration makes this kind available before parsing and dispatch.
    [ModuleInitializer]
    internal static void Register()
    {
        OperationKindRegistry.Register(DeleteSense);
        OperationHandlerRegistry.Register(DeleteSense, LexSenseDeleteHandler.Instance);
        OperationSlotRegistry.RegisterMany(DeleteSense, operation =>
        {
            var slots = new List<OperationSlot> { new(OperationSlotRegistry.Target(operation), null) };
            if (LexSenseDeletePayload.Parse(OperationSlotRegistry.After(operation)).Msa is { } msa)
                slots.Add(new OperationSlot(msa, null));
            return slots;
        });
    }
#pragma warning restore CA2255
}

/// <summary>
/// The closed payload of a sense deletion: <c>{ "msa": "&lt;id&gt;" | null }</c>. The id names the analysis the sense
/// points at when nothing else uses it, so a reader of the Proposal sees both deletions; null leaves the analysis alone.
/// </summary>
/// <param name="Msa">The analysis deleted with the sense, or null when none is.</param>
public sealed record LexSenseDeletePayload(CanonicalId? Msa)
{
    /// <summary>Parses the closed payload, refusing any other property and a missing <c>msa</c>.</summary>
    public static LexSenseDeletePayload Parse(JsonElement after)
    {
        const string kind = LexSenseDeleteOperationKinds.DeleteSense;
        ClosedPayloadParsing.RequireObject(after, kind);
        ClosedPayloadParsing.RejectUnknownProperties(after, ["msa"], kind);
        if (!after.TryGetProperty("msa", out var msa))
            throw new ContractParseException($"'{kind}' operation 'after.msa' is required: an id, or null for none.");
        return msa.ValueKind == JsonValueKind.Null
            ? new LexSenseDeletePayload((CanonicalId?)null)
            : new LexSenseDeletePayload(
                CanonicalId.FromGuid(ClosedPayloadParsing.GetRequiredCanonicalId(after, "msa", kind).ToGuid()));
    }
}

/// <summary>
/// Decides whether a sense carries anything a person wrote, by reading every field of the class except the
/// analysis link, so a field added to LibLCM later is counted without a change here.
/// </summary>
public static class LexSenseEmptiness
{
    /// <summary>The name of the first populated field, or null when the sense is wholly empty.</summary>
    public static string? FirstPopulatedField(LcmCache cache, ILexSense sense)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(sense);
        var metadata = (IFwMetaDataCacheManaged)cache.MetaDataCacheAccessor;
        var data = cache.DomainDataByFlid;
        var writingSystems = cache.ServiceLocator.WritingSystems.AllWritingSystems.Select(ws => ws.Handle).ToArray();
        foreach (var flid in metadata.GetFieldIds().Where(id => IsSameOrSubclass(metadata, sense.ClassID, metadata.GetOwnClsId(id))))
        {
            var name = metadata.GetFieldName(flid);
            if (name == "MorphoSyntaxAnalysis" || ((IFwMetaDataCache)metadata).get_IsVirtual(flid))
                continue;
            var populated = (CellarPropertyType)metadata.GetFieldType(flid) switch
            {
                CellarPropertyType.String =>
                    data.get_StringProp(sense.Hvo, flid)?.Length > 0,
                CellarPropertyType.Unicode =>
                    !string.IsNullOrEmpty(data.get_UnicodeProp(sense.Hvo, flid)),
                CellarPropertyType.MultiString or CellarPropertyType.MultiUnicode =>
                    writingSystems.Any(ws => data.get_MultiStringAlt(sense.Hvo, flid, ws)?.Length > 0),
                CellarPropertyType.OwningAtomic or CellarPropertyType.ReferenceAtomic =>
                    data.get_ObjectProp(sense.Hvo, flid) != 0,
                CellarPropertyType.OwningCollection or CellarPropertyType.OwningSequence or
                    CellarPropertyType.ReferenceCollection or CellarPropertyType.ReferenceSequence =>
                    data.get_VecSize(sense.Hvo, flid) > 0,
                _ => false,
            };
            if (populated)
                return name;
        }
        return null;
    }

    private static bool IsSameOrSubclass(IFwMetaDataCacheManaged metadata, int classId, int ancestor)
    {
        for (var current = classId; current != 0; current = metadata.GetBaseClsId(current))
            if (current == ancestor)
                return true;
        return false;
    }
}

internal sealed class LexSenseDeleteHandler : ICompoundOperationHandler
{
    internal static readonly LexSenseDeleteHandler Instance = new();
    private const string Kind = LexSenseDeleteOperationKinds.DeleteSense;

    private LexSenseDeleteHandler()
    {
    }

    public ExpectedEffect ApplyAndCaptureEffect(
        LcmCache cache, OperationEnvelope operation, List<CanonicalId> touchedTargets) =>
        ApplyAndCaptureEffects(cache, operation, touchedTargets)[0];

    public IReadOnlyList<ExpectedEffect> ApplyAndCaptureEffects(
        LcmCache cache, OperationEnvelope operation, List<CanonicalId> touchedTargets)
    {
        var (senseId, sense, entry, msa) = Resolve(cache, operation);
        touchedTargets.Add(senseId);
        var entryId = CanonicalId.FromGuid(entry.Guid);
        var senseBefore = ReadSenses(entry);
        var msaBefore = ReadAnalyses(entry);
        if (msa is not null)
            touchedTargets.Add(CanonicalId.FromGuid(msa.Guid));
        sense.Delete();
        if (msa is { IsValidObject: true })
            msa.Delete();
        var effects = new List<ExpectedEffect>
        {
            new(entryId, SnapshotFields.LexEntrySenses, senseBefore, ReadSenses(entry)),
        };
        if (msa is not null)
            effects.Add(new(entryId, SnapshotFields.LexEntryMorphoSyntaxAnalyses, msaBefore, ReadAnalyses(entry)));
        return effects;
    }

    public ExpectedEffect ReadCurrentFootprint(LcmCache cache, OperationEnvelope operation) =>
        ReadCurrentFootprintEffects(cache, operation)[0];

    public IReadOnlyList<ExpectedEffect> ReadCurrentFootprintEffects(LcmCache cache, OperationEnvelope operation)
    {
        var (_, _, entry, msa) = Resolve(cache, operation);
        var entryId = CanonicalId.FromGuid(entry.Guid);
        var senses = ReadSenses(entry);
        var effects = new List<ExpectedEffect> { new(entryId, SnapshotFields.LexEntrySenses, senses, senses) };
        if (msa is not null)
        {
            var analyses = ReadAnalyses(entry);
            effects.Add(new(entryId, SnapshotFields.LexEntryMorphoSyntaxAnalyses, analyses, analyses));
        }
        return effects;
    }

    private static (CanonicalId Id, ILexSense Sense, ILexEntry Entry, IMoMorphSynAnalysis? Msa) Resolve(
        LcmCache cache, OperationEnvelope operation)
    {
        var payload = LexSenseDeletePayload.Parse(operation.After ??
            throw new ContractParseException($"'{Kind}' operation requires 'after'."));
        var (id, sense) = TargetResolution.Resolve<ILexSense>(cache, operation, Kind);
        if (sense.Owner is not ILexEntry entry)
            throw new InvalidOperationException($"'{Kind}' deletes only a top-level sense of an entry.");
        if (LexSenseEmptiness.FirstPopulatedField(cache, sense) is { } field)
            throw new InvalidOperationException(
                $"'{Kind}' deletes only a wholly empty sense; the sense has content in '{field}'.");
        if (sense.ReferringObjects.Any())
            throw new InvalidOperationException($"'{Kind}' refuses a sense that other objects still refer to.");
        var current = sense.MorphoSyntaxAnalysisRA;
        if (payload.Msa is null)
            return (id, sense, entry, null);
        if (current is null || current.Guid != payload.Msa.Value.ToGuid())
            throw new InvalidOperationException(
                $"'{Kind}' names an analysis the sense no longer points at; recompute the Draft.");
        if (current.Owner?.Guid != entry.Guid)
            throw new InvalidOperationException($"'{Kind}' deletes only an analysis the sense's own entry owns.");
        if (entry.SensesOS.Any(other => other.Guid != sense.Guid && other.MorphoSyntaxAnalysisRA?.Guid == current.Guid))
            throw new InvalidOperationException($"'{Kind}' refuses an analysis another sense of the entry uses.");
        if (current.ReferringObjects.Any(other => other.Guid != sense.Guid))
            throw new InvalidOperationException(
                $"'{Kind}' refuses an analysis that other objects, such as parser analyses, still refer to.");
        return (id, sense, entry, current);
    }

    private static Dictionary<string, string> ReadSenses(ILexEntry entry)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < entry.SensesOS.Count; i++)
            result[i.ToString(System.Globalization.CultureInfo.InvariantCulture)] =
                CanonicalId.FromGuid(entry.SensesOS[i].Guid).Value;
        return result;
    }

    private static Dictionary<string, string> ReadAnalyses(ILexEntry entry)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var ids = entry.MorphoSyntaxAnalysesOC.Select(msa => CanonicalId.FromGuid(msa.Guid).Value)
            .Order(StringComparer.Ordinal).ToArray();
        for (var i = 0; i < ids.Length; i++)
            result[i.ToString(System.Globalization.CultureInfo.InvariantCulture)] = ids[i];
        return result;
    }
}
