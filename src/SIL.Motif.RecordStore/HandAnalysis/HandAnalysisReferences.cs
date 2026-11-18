using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;

namespace SIL.Motif.RecordStore.HandAnalysis;

internal static class HandAnalysisReferences
{
    public static ReferenceIndexSnapshot WithAnchors(ReferenceIndexSnapshot index,
        IReadOnlyDictionary<Guid, GenericRecord> records, GenericRecord[] anchors, CancellationToken ct)
    {
        var schema = index.UsageSchema;
        var fields = anchors.Length == 0 ? [] : BundleFields(schema);
        var coverage = anchors.Length == 0 || fields.Length == 4;
        var witnesses = ImmutableArray.CreateBuilder<ReferenceWitness>();
        foreach (var anchor in anchors)
        {
            ct.ThrowIfCancellationRequested();
            if (HandAnalysisKinds.AnchorTarget(anchor) is not { } target || !records.TryGetValue(target, out var native))
            {
                coverage = false;
                continue;
            }
            var location = anchor.Values["anchor"].GetString();
            var targets = new HashSet<Guid> { target };
            if (schema is not null)
            {
                var root = schema.Is(native.ClassId, "WfiGloss") ? native.Owner ?? target : target;
                if (schema.IsAnalysis(native.ClassId) || schema.IsWordform(native.ClassId) ||
                    schema.IsBundle(native.ClassId) || schema.Is(native.ClassId, "WfiGloss"))
                {
                    foreach (var id in index.OwnedClosure([root], ct))
                    {
                        ct.ThrowIfCancellationRequested();
                        var record = records[id];
                        if (!schema.IsBundle(record.ClassId)) continue;
                        foreach (var field in fields)
                            if (record.Values.TryGetValue(field, out var value) && value.ValueKind == JsonValueKind.String &&
                                value.TryGetGuid(out var referent)) targets.Add(referent);
                    }
                }
            }
            foreach (var use in targets)
            {
                ct.ThrowIfCancellationRequested();
                witnesses.Add(new(ReferenceWitnessKind.Anchor, anchor.Id.ToString("D"), index.Revision, use, target)
                { Location = location });
            }
        }
        return index.WithStoredAnchors(witnesses.ToImmutable(), coverage, ct);
    }

    private static string[] BundleFields(NativeUsageSchema? schema)
    {
        if (schema is null) return [];
        try
        {
            var wfi = new WfiSchema(schema);
            return new[] { wfi.Morph, wfi.Sense, wfi.Msa, wfi.InflType }
                .Select(field => field.ToString(CultureInfo.InvariantCulture)).ToArray();
        }
        catch (Exception exception) when (exception is KeyNotFoundException or FormatException or InvalidOperationException)
        {
            return [];
        }
    }
}
