using System.Globalization;
using System.Text.Json;

namespace SIL.Motif.RecordStore;

internal static class RecordInitialization
{
    public static readonly JsonElement Empty = JsonSerializer.SerializeToElement(new Dictionary<string, object>());
    private static readonly JsonElement EmptyVector = JsonSerializer.SerializeToElement(Array.Empty<Guid>());

    public static readonly JsonElement EmptyLayout = Layout([]);

    public static JsonElement Layout(IEnumerable<RecordImport> records, bool requireDefaults = false)
    {
        var defaults = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var types = new Dictionary<string, int>(StringComparer.Ordinal);
        var declared = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var import in records)
        foreach (var (field, value) in import.Record.Values)
        {
            var type = int.TryParse(field, out var id) ? import.FieldTypes.GetValueOrDefault(id) : 0;
            if (!KnownType(type)) throw new InvalidDataException("Unknown field type.");
            if (declared.TryGetValue(field, out var prior) && prior != type) throw new InvalidDataException("Conflicting field types.");
            declared[field] = type;
            if (!Tabled(import.Record.Kind, field, type)) types[field] = type;
            if (type is 14 or 16) defaults[field] = Empty;
            else if (type >= 25) defaults[field] = EmptyVector;
            else if (Neutral(value)) defaults.TryAdd(field, value);
        }
        if (requireDefaults && records.Any(import => import.Record.Values.Count < declared.Count &&
            defaults.Keys.Any(field => !import.Record.Values.ContainsKey(field))))
            throw new InvalidDataException("A packed record is missing a field with a shared initialization default.");
        return JsonSerializer.SerializeToElement(new { version = 2, table = BuiltInLayouts.Version, types, defaults });
    }

    private static bool KnownType(int type) => type == 0 || BuiltInLayouts.KnownTypes.Contains(type);

    // The pinned table answers for a built-in native field; only fields it cannot answer for are embedded.
    private static bool Tabled(string kind, string field, int type) =>
        kind == "lcm" && int.TryParse(field, out var id) && BuiltInLayouts.TypeOf(id) == type;

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
        if (layout.GetProperty("version").GetInt32() != 2) throw new InvalidDataException("Unknown initialization layout.");
        if (!layout.TryGetProperty("table", out var table) || table.GetString() != BuiltInLayouts.Version)
            throw new InvalidDataException("Unknown built-in layout table; reimport the store after an LCM upgrade.");
        foreach (var declared in layout.GetProperty("types").EnumerateObject())
            if (!declared.Value.TryGetInt32(out var type) || !KnownType(type)) throw new InvalidDataException("Unknown field type.");
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
        ? value.GetInt32() : record.Kind == "lcm" && int.TryParse(field, out var id) ? BuiltInLayouts.TypeOf(id) ?? 0 : 0;

    public static bool IsInitial(GenericRecord record, string field, JsonElement value) =>
        record.Layout.GetProperty("defaults").TryGetProperty(field, out var initial) && JsonElement.DeepEquals(initial, value) ||
        (field switch
        {
            "101" when (record.IdentityFields & 1) != 0 => JsonElement.DeepEquals(JsonSerializer.SerializeToElement(record.Id), value),
            "102" when (record.IdentityFields & 2) != 0 => JsonElement.DeepEquals(JsonSerializer.SerializeToElement(record.ClassId), value),
            "104" when (record.IdentityFields & 4) != 0 => JsonElement.DeepEquals(JsonSerializer.SerializeToElement(record.OwningField), value),
            "105" when (record.IdentityFields & 8) != 0 => JsonElement.DeepEquals(JsonSerializer.SerializeToElement(record.OwnOrdinal), value),
            _ => false
        });

    /// <summary>A layout's field types and initial values, parsed once for every record that shares the layout.</summary>
    internal sealed class LayoutView
    {
        private readonly Dictionary<string, int> types = new(StringComparer.Ordinal);
        private readonly Dictionary<string, JsonElement> defaults = new(StringComparer.Ordinal);

        public LayoutView(JsonElement layout)
        {
            foreach (var property in layout.GetProperty("types").EnumerateObject()) types[property.Name] = property.Value.GetInt32();
            foreach (var property in layout.GetProperty("defaults").EnumerateObject()) defaults[property.Name] = property.Value;
        }

        public int Type(GenericRecord record, string field) => types.TryGetValue(field, out var value) ? value
            : record.Kind == "lcm" && int.TryParse(field, out var id) ? BuiltInLayouts.TypeOf(id) ?? 0 : 0;

        public bool IsInitial(GenericRecord record, string field, JsonElement value) =>
            defaults.TryGetValue(field, out var initial) && JsonElement.DeepEquals(initial, value) || IdentityMatches(record, field, value);

        private static bool IdentityMatches(GenericRecord record, string field, JsonElement value)
        {
            switch (field)
            {
                case "101" when (record.IdentityFields & 1) != 0:
                    return value.ValueKind == JsonValueKind.String ? value.GetString() == record.Id.ToString("D")
                        : JsonElement.DeepEquals(JsonSerializer.SerializeToElement(record.Id), value);
                case "102" when (record.IdentityFields & 2) != 0: return Number(value, record.ClassId);
                case "104" when (record.IdentityFields & 4) != 0: return Number(value, record.OwningField);
                case "105" when (record.IdentityFields & 8) != 0: return Number(value, record.OwnOrdinal);
                default: return false;
            }
        }

        private static bool Number(JsonElement value, int expected) => value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var actual)
            ? actual == expected : JsonElement.DeepEquals(JsonSerializer.SerializeToElement(expected), value);
    }

    public static GenericRecord Prepare(GenericRecord source, JsonElement layout, bool reuseValues = false)
    {
        var ordinal = source.Values.TryGetValue("105", out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : 0;
        byte flags = 0;
        if (source.Kind == "lcm")
        {
            if (source.Values.TryGetValue("101", out var guid) && guid.ValueKind == JsonValueKind.String &&
                guid.GetString() == source.Id.ToString("D")) flags |= 1;
            if (MatchesNumber(source.Values, "102", source.ClassId)) flags |= 2;
            if (MatchesNumber(source.Values, "104", source.OwningField)) flags |= 4;
            if (MatchesNumber(source.Values, "105", ordinal)) flags |= 8;
        }
        return new GenericRecord
        {
            Id = source.Id, Kind = source.Kind, ClassId = source.ClassId, Owner = source.Owner,
            OwningField = source.OwningField, OwnOrdinal = ordinal, IdentityFields = flags,
            Layout = layout, Provenance = source.Provenance,
            Values = reuseValues ? source.Values : new(source.Values, StringComparer.Ordinal)
        };
    }

    private static bool MatchesNumber(Dictionary<string, JsonElement> values, string field, int expected) =>
        values.TryGetValue(field, out var value) && value.ValueKind == JsonValueKind.Number &&
        (value.TryGetInt32(out var actual) ? actual == expected : JsonElement.DeepEquals(value, JsonSerializer.SerializeToElement(expected)));
}
