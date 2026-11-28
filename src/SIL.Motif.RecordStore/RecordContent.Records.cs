using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Text.Json;

namespace SIL.Motif.RecordStore;

/// <summary>
/// Writes the canonical bytes of a record straight from its typed members. The result is identical to canonicalizing
/// the record's JSON form, without building and reparsing that JSON for every record.
/// </summary>
internal sealed partial class RecordContent
{
    // Keyed by document identity: a fresh parse never hits, so the cache is bounded.
    private const int LayoutCacheLimit = 1024;
    private static readonly ConcurrentDictionary<JsonElement, byte[]> LayoutBytes = new();

    /// <summary>Entries in the layout cache; it never exceeds its limit by more than the writers racing at once.</summary>
    internal static int LayoutCacheCount => Volatile.Read(ref layoutCount);

    // Counted here because ConcurrentDictionary.Count takes every lock.
    private static int layoutCount;

    // Property names in canonical (ordinal) order, each with its precomputed token or text prefix.
    private static readonly string[] ItemNames = ["ClassId", "DeletedAt", "Id", "IdentityFields", "Kind", "Layout", "OwnOrdinal",
        "Owner", "OwningField", "Provenance", "Values"];
    private static readonly string[] SortedItemNames = ItemNames.Order(StringComparer.Ordinal).ToArray();
    // Lazy: Prefix needs Properties from the other file of this partial type; static init order there is undefined.
    private static readonly Lazy<byte[][]> ItemPrefixes = new(() => SortedItemNames.Select(Prefix).ToArray());

    private static byte[] Prefix(string name)
    {
        using var output = new BinaryOutput();
        Property(output, name);
        return output.Finish();
    }

    /// <summary>Whether <see cref="WriteRecordItem"/> covers this record; any other shape uses the JSON form.</summary>
    internal static bool HasDirectItem(GenericRecord record) =>
        record.DeletedAt is null && !record.Lists.Any(p => p.Value.Ops.Count > 0) && !TextShaped(record.Values);

    // A two-key object could be mistaken for a rich-text shape by the canonical writer; those keep the JSON form.
    private static bool TextShaped(Dictionary<string, JsonElement> values) => values.Count == 2 &&
        (values.ContainsKey("text") || values.ContainsKey("ints"));

    /// <summary>The digest item of an unedited, undeleted record: its canonical JSON form (see RecordStore.SnapshotItem).</summary>
    internal static void WriteRecordItem(BinaryOutput output, GenericRecord record)
    {
        output.Byte(2);
        output.Byte(7); output.Unsigned((ulong)ItemNames.Length);
        var order = 0;
        foreach (var name in SortedItemNames)
        {
            output.Append(ItemPrefixes.Value[order++]);
            switch (name)
            {
                case "ClassId": output.Byte(3); output.Integer(record.ClassId); break;
                case "DeletedAt": output.Byte(0); break;
                case "Id": output.Byte(6); output.Guid(record.Id); break;
                case "IdentityFields": output.Byte(3); output.Integer(record.IdentityFields); break;
                case "Kind": WriteString(output, record.Kind); break;
                case "Layout": output.Append(LayoutValue(record.Layout)); break;
                case "OwnOrdinal": output.Byte(3); output.Integer(record.OwnOrdinal); break;
                case "Owner":
                    if (record.Owner is { } owner) { output.Byte(6); output.Guid(owner); } else output.Byte(0);
                    break;
                case "OwningField": output.Byte(3); output.Integer(record.OwningField); break;
                case "Provenance":
                    if (record.Provenance is null) output.Byte(0); else WriteProvenance(output, record.Provenance);
                    break;
                default: WriteValues(output, record.Values); break;
            }
        }
    }

    private static byte[] LayoutValue(JsonElement layout)
    {
        if (LayoutBytes.TryGetValue(layout, out var known)) return known;
        using var output = new BinaryOutput();
        Write(output, layout, 1);
        var bytes = output.Finish();
        if (Volatile.Read(ref layoutCount) >= LayoutCacheLimit)
        {
            LayoutBytes.Clear();
            Volatile.Write(ref layoutCount, 0);
        }
        if (LayoutBytes.TryAdd(layout, bytes)) Interlocked.Increment(ref layoutCount);
        return bytes;
    }

    private static void WriteValues(BinaryOutput output, Dictionary<string, JsonElement> values)
    {
        output.Byte(7); output.Unsigned((ulong)values.Count);
        var keys = values.Keys.ToArray();
        Array.Sort(keys, StringComparer.Ordinal);
        foreach (var key in keys) { Property(output, key); Write(output, values[key], 2); }
    }

    /// <summary>Canonical bytes of the provenance object, as <c>JsonSerializer</c> would shape it.</summary>
    internal static void WriteProvenance(BinaryOutput output, NativeProvenance provenance)
    {
        output.Byte(7); output.Unsigned(4);
        Property(output, "EncounterOrdinal"); output.Byte(3); output.Integer(provenance.EncounterOrdinal);
        Property(output, "RepositoryOrdinals"); WriteOrdinals(output, provenance.RepositoryOrdinals);
        Property(output, "TraversalVersion"); WriteString(output, provenance.TraversalVersion);
        Property(output, "Vectors"); WriteVectors(output, provenance.Vectors);
    }

    internal static void WriteVectors(BinaryOutput output, ImmutableDictionary<int, ImmutableArray<Guid>> vectors)
    {
        output.Byte(7); output.Unsigned((ulong)vectors.Count);
        foreach (var (key, items) in vectors
            .Select(p => (Key: p.Key.ToString(System.Globalization.CultureInfo.InvariantCulture), Items: p.Value))
            .OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            Property(output, key);
            output.Byte(8); output.Unsigned((ulong)items.Length);
            foreach (var item in items) { output.Byte(6); output.Guid(item); }
        }
    }

    internal static void WriteOrdinals(BinaryOutput output, ImmutableDictionary<string, long> ordinals)
    {
        output.Byte(7); output.Unsigned((ulong)ordinals.Count);
        foreach (var (key, value) in ordinals.OrderBy(p => p.Key, StringComparer.Ordinal))
        { Property(output, key); output.Byte(3); output.Integer(value); }
    }
}
