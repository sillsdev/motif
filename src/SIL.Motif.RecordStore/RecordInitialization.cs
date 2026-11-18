using System.Globalization;
using System.Text.Json;

namespace SIL.Motif.RecordStore;

internal static class RecordInitialization
{
    public static readonly JsonElement Empty = JsonSerializer.SerializeToElement(new Dictionary<string, object>());

    public static readonly JsonElement EmptyLayout = Layout([]);

    public static JsonElement Layout(IEnumerable<RecordImport> records)
    {
        var defaults = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var types = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var import in records)
        foreach (var (field, value) in import.Record.Values)
        {
            var type = int.TryParse(field, out var id) ? import.FieldTypes.GetValueOrDefault(id) : 0;
            if (types.TryGetValue(field, out var prior) && prior != type) throw new InvalidDataException("Conflicting field types.");
            types[field] = type;
            if (type is 14 or 16) defaults[field] = Empty;
            else if (type >= 25) defaults[field] = JsonSerializer.SerializeToElement(Array.Empty<Guid>());
            else if (Neutral(value)) defaults.TryAdd(field, value);
        }
        return JsonSerializer.SerializeToElement(new { version = 1, types, defaults });
    }

    private static bool Neutral(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.False => true,
        JsonValueKind.Number => value.TryGetInt64(out var number) && number == 0,
        JsonValueKind.String => value.GetString() is "" or "00000000-0000-0000-0000-000000000000",
        JsonValueKind.Array => value.GetArrayLength() == 0,
        JsonValueKind.Object => value.EnumerateObject().All(p => Neutral(p.Value)),
        _ => false
    };

    public static GenericRecord Create(Guid id, string kind, int classId, Guid? owner, int field,
        int ordinal, byte identityFields, JsonElement layout, NativeProvenance? provenance)
    {
        if (layout.GetProperty("version").GetInt32() != 1) throw new InvalidDataException("Unknown initialization layout.");
        var record = new GenericRecord
        {
            Id = id, Kind = kind, ClassId = classId, Owner = owner, OwningField = field,
            OwnOrdinal = ordinal, IdentityFields = identityFields, Layout = layout, Provenance = provenance,
            Values = layout.GetProperty("defaults").EnumerateObject().ToDictionary(p => p.Name, p => p.Value, StringComparer.Ordinal)
        };
        foreach (var identity in Identities(record)) record.Values[identity.Key] = identity.Value;
        return record;
    }

    public static IEnumerable<KeyValuePair<string, JsonElement>> Identities(GenericRecord record)
    {
        if ((record.IdentityFields & 1) != 0) yield return new("101", JsonSerializer.SerializeToElement(record.Id));
        if ((record.IdentityFields & 2) != 0) yield return new("102", JsonSerializer.SerializeToElement(record.ClassId));
        if ((record.IdentityFields & 4) != 0) yield return new("104", JsonSerializer.SerializeToElement(record.OwningField));
        if ((record.IdentityFields & 8) != 0) yield return new("105", JsonSerializer.SerializeToElement(record.OwnOrdinal));
    }

    public static int Type(GenericRecord record, string field) => record.Layout.GetProperty("types").TryGetProperty(field, out var value)
        ? value.GetInt32() : 0;

    public static bool IsInitial(GenericRecord record, string field, JsonElement value) =>
        record.Layout.GetProperty("defaults").TryGetProperty(field, out var initial) && JsonElement.DeepEquals(initial, value) ||
        Identities(record).Any(pair => pair.Key == field && JsonElement.DeepEquals(pair.Value, value));

    public static GenericRecord Prepare(GenericRecord source, JsonElement layout)
    {
        var ordinal = source.Values.TryGetValue("105", out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : 0;
        byte flags = 0;
        var identities = new (string Field, JsonElement Value)[]
        {
            ("101", JsonSerializer.SerializeToElement(source.Id)), ("102", JsonSerializer.SerializeToElement(source.ClassId)),
            ("104", JsonSerializer.SerializeToElement(source.OwningField)), ("105", JsonSerializer.SerializeToElement(ordinal))
        };
        for (var index = 0; index < identities.Length; index++)
            if (source.Kind == "lcm" && source.Values.TryGetValue(identities[index].Field, out var actual) &&
                JsonElement.DeepEquals(actual, identities[index].Value)) flags |= (byte)(1 << index);
        var record = Create(source.Id, source.Kind, source.ClassId, source.Owner, source.OwningField, ordinal, flags, layout, source.Provenance);
        record.Values = new(source.Values, StringComparer.Ordinal);
        return record;
    }
}
