using System.Runtime.CompilerServices;
using System.Text;
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

/// <summary>The operation kind that merges one dictionary entry into another, as FieldWorks' Merge Entry does.</summary>
public static class LexEntryMergeOperationKinds
{
    /// <summary>Merges the targeted entry into the entry named by <c>into</c>; the targeted entry ceases to exist.</summary>
    public const string Merge = "lexical/lexEntry/merge";

#pragma warning disable CA2255 // Load-time registration makes this kind available before parsing and dispatch.
    [ModuleInitializer]
    internal static void Register()
    {
        OperationKindRegistry.Register(Merge);
        OperationHandlerRegistry.Register(Merge, LexEntryMergeHandler.Instance);
        OperationSlotRegistry.RegisterMany(Merge, operation =>
        {
            var entry = OperationSlotRegistry.Target(operation);
            var payload = LexEntryMergePayload.Parse(OperationSlotRegistry.After(operation));
            return
            [
                new OperationSlot(entry, null),
                new OperationSlot(payload.Into, "$mergedEntries", OperationSlotRegistry.MemberKey(entry)),
                new OperationSlot(payload.Lexicon, SnapshotFields.LexDbEntries, OperationSlotRegistry.MemberKey(entry)),
            ];
        });
    }
#pragma warning restore CA2255
}

/// <summary>The closed payload of an entry merge.</summary>
/// <param name="Lexicon">The project's lexicon, which lists the entry; a different one makes the Draft stale.</param>
/// <param name="Into">The entry that survives and takes the merged entry's forms, senses, analyses and references.</param>
public sealed record LexEntryMergePayload(CanonicalId Lexicon, CanonicalId Into)
{
    /// <summary>Parses the closed payload, refusing any other property.</summary>
    public static LexEntryMergePayload Parse(JsonElement after)
    {
        const string kind = LexEntryMergeOperationKinds.Merge;
        ClosedPayloadParsing.RequireObject(after, kind);
        ClosedPayloadParsing.RejectUnknownProperties(after, ["lexicon", "into"], kind);
        return new LexEntryMergePayload(
            CanonicalId.FromGuid(ClosedPayloadParsing.GetRequiredCanonicalId(after, "lexicon", kind).ToGuid()),
            CanonicalId.FromGuid(ClosedPayloadParsing.GetRequiredCanonicalId(after, "into", kind).ToGuid()));
    }
}

/// <summary>Entry identity checks and the before-state bound to an entry merge.</summary>
internal static class LexEntryMerging
{
    private const string Kind = LexEntryMergeOperationKinds.Merge;

    internal static void Inspect(ILexEntry merged, ILexEntry survivor)
    {
        if (merged.Guid == survivor.Guid)
            throw new InvalidOperationException(
                $"'{Kind}' refuses {Name(merged)}: an entry cannot be merged into itself.");
    }

    internal static string Name(ILexEntry entry) =>
        $"the entry '{entry.HeadWord?.Text}' ({CanonicalId.FromGuid(entry.Guid).Value})";

    /// <summary>The recorded before-state: what moves onto the survivor and which entry it joins.</summary>
    internal static JsonElement Preview(ILexEntry merged, ILexEntry survivor)
    {
        var preview = LexEntryRemoval.Preview(merged).GetProperty("deletedEntry");
        return JsonSerializer.SerializeToElement(new
        {
            mergedEntry = preview,
            mergedText = TextFields(merged),
            intoEntry = new
            {
                id = CanonicalId.FromGuid(survivor.Guid).Value,
                headword = survivor.HeadWord?.Text,
                senses = survivor.AllSenses.Count(),
                text = TextFields(survivor),
            },
            outcome = "one entry: the merged entry's senses, analyses and alternate forms are added to the surviving entry, " +
                "its lexeme form joins the surviving one or becomes an alternate form when the text differs, every reference to " +
                "the merged entry points at the surviving entry, and equal alternate forms and analyses become one",
            reversal = "createEntries with the merged entry's id, then recreate its forms, senses and analyses from the " +
                "ids and content listed here and repoint the references",
        });
    }

    private static SortedDictionary<string, SortedDictionary<string, string>> TextFields(ILexEntry entry)
    {
        var cache = entry.Cache;
        var metadata = (IFwMetaDataCacheManaged)cache.MetaDataCacheAccessor;
        var data = cache.DomainDataByFlid;
        var fields = new SortedDictionary<string, SortedDictionary<string, string>>(StringComparer.Ordinal);
        // Custom entry text must drift too, so the fields come from LibLCM's live metadata.
        foreach (var flid in metadata.GetFields(entry.ClassID, true, (int)CellarPropertyTypeFilter.All))
        {
            if (((IFwMetaDataCache)metadata).get_IsVirtual(flid)) continue;
            var values = new SortedDictionary<string, string>(StringComparer.Ordinal);
            var type = (CellarPropertyType)metadata.GetFieldType(flid);
            switch (type)
            {
                case CellarPropertyType.Unicode:
                    if (data.get_UnicodeProp(entry.Hvo, flid) is { Length: > 0 } unicode)
                        values[""] = unicode.Normalize(NormalizationForm.FormD);
                    break;
                case CellarPropertyType.String:
                    if (data.get_StringProp(entry.Hvo, flid)?.get_NormalizedForm(FwNormalizationMode.knmNFSC)?.Text
                        is { Length: > 0 } text)
                        values[""] = text;
                    break;
                case CellarPropertyType.MultiString:
                case CellarPropertyType.MultiUnicode:
                    var alternatives = data.get_MultiStringProp(entry.Hvo, flid);
                    for (var index = 0; index < alternatives.StringCount; index++)
                    {
                        var value = alternatives.GetStringFromIndex(index, out var ws);
                        var normalized = type == CellarPropertyType.MultiUnicode
                            ? value?.Text?.Normalize(NormalizationForm.FormD)
                            : value?.get_NormalizedForm(FwNormalizationMode.knmNFSC)?.Text;
                        if (!string.IsNullOrEmpty(normalized))
                            values[RemovalPreview.WritingSystemName(cache, ws)] = normalized;
                    }
                    break;
            }
            if (values.Count != 0) fields[metadata.GetFieldName(flid)] = values;
        }
        return fields;
    }
}

/// <summary>Merges an entry into another with LibLCM's own entry merge, which FieldWorks' Merge Entry runs.</summary>
internal sealed class LexEntryMergeHandler : IOperationHandler
{
    internal static readonly LexEntryMergeHandler Instance = new();
    private const string Kind = LexEntryMergeOperationKinds.Merge;

    private LexEntryMergeHandler()
    {
    }

    public ExpectedEffect ApplyAndCaptureEffect(
        LcmCache cache, OperationEnvelope operation, List<CanonicalId> touchedTargets)
    {
        var (id, merged, survivor) = Resolve(cache, operation);
        LexEntryMerging.Inspect(merged, survivor);
        var lexicon = LexEntryRemoval.LexiconId(cache);
        var before = Membership(cache, id);
        var preview = LexEntryMerging.Preview(merged, survivor);
        touchedTargets.Add(id);
        touchedTargets.Add(CanonicalId.FromGuid(survivor.Guid));
        touchedTargets.AddRange(merged.AllOwnedObjects.Select(item => CanonicalId.FromGuid(item.Guid)));
        // Keep differing notes together, pinned by `DifferentEntryNotesAreJoinedWithoutLosingEitherText`.
        survivor.MergeObject(merged, true);
        return new ExpectedEffect(lexicon, SnapshotFields.LexDbEntries, before, Membership(cache, id), preview);
    }

    public ExpectedEffect ReadCurrentFootprint(LcmCache cache, OperationEnvelope operation)
    {
        var (id, merged, survivor) = Resolve(cache, operation);
        var current = Membership(cache, id);
        return new ExpectedEffect(LexEntryRemoval.LexiconId(cache), SnapshotFields.LexDbEntries, current, current,
            LexEntryMerging.Preview(merged, survivor));
    }

    private static (CanonicalId Id, ILexEntry Merged, ILexEntry Survivor) Resolve(LcmCache cache, OperationEnvelope operation)
    {
        var payload = LexEntryMergePayload.Parse(
            operation.After ?? throw new ContractParseException($"'{Kind}' operation requires 'after'."));
        if (payload.Lexicon != LexEntryRemoval.LexiconId(cache))
            throw new InvalidOperationException($"'{Kind}' names a lexicon this project does not have; recompute the Draft.");
        var (id, merged) = TargetResolution.Resolve<ILexEntry>(cache, operation, Kind);
        var survivor = cache.ServiceLocator.GetInstance<ICmObjectRepository>().TryGetObject(payload.Into.ToGuid(), out var found)
            ? found as ILexEntry : null;
        return (id, merged, survivor ?? throw new InvalidOperationException(
            $"'{Kind}' names the surviving entry '{payload.Into.Value}', which is not an entry in this project; recompute the Draft."));
    }

    private static Dictionary<string, string> Membership(LcmCache cache, CanonicalId id) =>
        cache.LangProject.LexDbOA.Entries.Any(entry => entry.Guid == id.ToGuid())
            ? new(StringComparer.Ordinal) { ["ref"] = id.Value } : new(StringComparer.Ordinal);
}
