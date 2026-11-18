using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace SIL.Motif.RecordStore;

internal sealed class ContentRow
{
    public int Id { get; set; }
    public byte[] Data { get; set; } = [];
}

internal sealed class RecordContent
{
    private readonly Dictionary<string, int> identities = new(StringComparer.Ordinal);
    private readonly Dictionary<int, JsonElement> values = new();
    private readonly List<ContentRow> pending = [];
    private int next;
    private readonly SortedDictionary<int, byte[]> hashes = new();
    public IReadOnlyList<ContentRow> Pending => pending;
    public void Saved() => pending.Clear();

    public void Load(IEnumerable<ContentRow> rows)
    {
        foreach (var row in rows)
        {
            if (row.Id <= 0) throw new InvalidDataException("Invalid immutable content identity.");
            var hash = SHA256.HashData(row.Data);
            var value = Decode(row.Data);
            identities.Add(Convert.ToHexString(SHA256.HashData(Canonical(value))), row.Id);
            hashes.Add(row.Id, hash);
            values.Add(row.Id, value);
            next = Math.Max(next, row.Id);
        }
    }

    public int Put(JsonElement value)
    {
        var raw = Canonical(value);
        var key = Convert.ToHexString(SHA256.HashData(raw));
        if (identities.TryGetValue(key, out var prior)) return prior;
        var bytes = Encode(raw);
        var hash = SHA256.HashData(bytes);
        var id = checked(++next);
        identities.Add(key, id);
        values.Add(id, value.Clone());
        hashes.Add(id, hash);
        pending.Add(new ContentRow { Id = id, Data = bytes });
        return id;
    }

    public string Digest()
    {
        using var output = new BinaryOutput();
        foreach (var (id, hash) in hashes) { output.Unsigned((ulong)id); output.Bytes(hash); }
        return Convert.ToHexStringLower(SHA256.HashData(output.Finish()));
    }

    public JsonElement Get(int id) => values.TryGetValue(id, out var value) ? value
        : throw new InvalidDataException("Missing immutable record content.");

    internal static byte[] Canonical(JsonElement value)
    {
        using var writer = new BinaryOutput();
        writer.Byte(2);
        Write(writer, value, 0);
        var raw = writer.Finish();
        if (raw.Length > 64 * 1024 * 1024) throw new InvalidDataException("Record content is too large.");
        return raw;
    }

    private static byte[] Encode(byte[] raw)
    {
        if (raw.Length < 256) return [0, ..raw];
        using var output = new MemoryStream();
        output.WriteByte(1);
        using (var compression = new BrotliStream(output, CompressionLevel.Optimal, true)) compression.Write(raw);
        return output.Length < raw.Length + 1 ? output.ToArray() : [0, ..raw];
    }

    private static JsonElement Decode(byte[] bytes)
    {
        if (bytes.Length < 2 || bytes[0] > 1) throw new InvalidDataException("Unknown content envelope.");
        byte[] raw;
        if (bytes[0] == 0) raw = bytes[1..];
        else
        {
            using var input = new MemoryStream(bytes, 1, bytes.Length - 1);
            using var compression = new BrotliStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            var buffer = new byte[8192];
            int read;
            while ((read = compression.Read(buffer)) != 0)
            {
                if (output.Length + read > 64 * 1024 * 1024) throw new InvalidDataException("Record content is too large.");
                output.Write(buffer, 0, read);
            }
            raw = output.ToArray();
        }
        var reader = new BinaryInput(raw);
        if (reader.Byte() != 2) throw new InvalidDataException("Unknown content version.");
        using var json = new MemoryStream();
        using (var writer = new Utf8JsonWriter(json)) Read(reader, writer, 0);
        reader.End();
        using var document = JsonDocument.Parse(json.ToArray());
        return document.RootElement.Clone();
    }

    private static void Write(BinaryOutput writer, JsonElement value, int depth)
    {
        if (depth > 64) throw new InvalidDataException("Record content nesting is too deep.");
        switch (value.ValueKind)
        {
            case JsonValueKind.Null: writer.Byte(0); break;
            case JsonValueKind.False: writer.Byte(1); break;
            case JsonValueKind.True: writer.Byte(2); break;
            case JsonValueKind.Number:
                if (value.TryGetInt64(out var number)) { writer.Byte(3); writer.Integer(number); }
                else { writer.Byte(4); writer.Text(value.GetRawText()); }
                break;
            case JsonValueKind.String:
                var text = value.GetString()!;
                if (System.Guid.TryParseExact(text, "D", out var guid) && text == guid.ToString("D"))
                { writer.Byte(6); writer.Guid(guid); }
                else { writer.Byte(5); writer.Text(text); }
                break;
            case JsonValueKind.Array:
                writer.Byte(8);
                writer.Unsigned((ulong)value.GetArrayLength());
                foreach (var item in value.EnumerateArray()) Write(writer, item, depth + 1);
                break;
            case JsonValueKind.Object:
                if (TryText(writer, value, depth)) break;
                writer.Byte(7);
                var properties = value.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal).ToArray();
                writer.Unsigned((ulong)properties.Length);
                foreach (var property in properties) { Property(writer, property.Name); Write(writer, property.Value, depth + 1); }
                break;
            default: throw new InvalidDataException("Undefined record value.");
        }
    }

    private static void Read(BinaryInput reader, Utf8JsonWriter writer, int depth)
    {
        if (depth > 64) throw new InvalidDataException("Record content nesting is too deep.");
        switch (reader.Byte())
        {
            case 0: writer.WriteNullValue(); break;
            case 1: writer.WriteBooleanValue(false); break;
            case 2: writer.WriteBooleanValue(true); break;
            case 3: writer.WriteNumberValue(reader.Integer()); break;
            case 4: writer.WriteRawValue(reader.Text(), skipInputValidation: false); break;
            case 5: writer.WriteStringValue(reader.Text()); break;
            case 6: writer.WriteStringValue(reader.Guid().ToString("D")); break;
            case 7:
                writer.WriteStartObject();
                for (var n = reader.Count(); n > 0; n--) { writer.WritePropertyName(Property(reader)); Read(reader, writer, depth + 1); }
                writer.WriteEndObject();
                break;
            case 8:
                writer.WriteStartArray();
                for (var n = reader.Count(); n > 0; n--) Read(reader, writer, depth + 1);
                writer.WriteEndArray();
                break;
            case 9: ReadRaw(reader).WriteTo(writer); break;
            case 10:
                var raw = Element(reader, depth + 1);
                var props = Element(reader, depth + 1);
                var bytes = raw.GetProperty("utf16le");
                var length = bytes.ValueKind == JsonValueKind.Null ? 0 : Convert.FromBase64String(bytes.GetString()!).Length / 2;
                JsonSerializer.Serialize(writer, new { text = raw, runs = new[] { new { start = 0, end = length, text = raw, props } } });
                break;
            case 11:
                writer.WriteStartObject();
                writer.WriteStartArray("ints");
                for (var count = reader.Count(); count > 0; count--)
                {
                    writer.WriteStartObject(); writer.WriteNumber("type", reader.Integer());
                    writer.WriteNumber("variation", reader.Integer()); writer.WritePropertyName("value");
                    Read(reader, writer, depth + 1); writer.WriteEndObject();
                }
                writer.WriteEndArray(); writer.WriteStartArray("strings");
                for (var count = reader.Count(); count > 0; count--)
                {
                    writer.WriteStartObject(); writer.WriteNumber("type", reader.Integer()); writer.WritePropertyName("value");
                    Read(reader, writer, depth + 1); writer.WriteEndObject();
                }
                writer.WriteEndArray(); writer.WriteEndObject();
                break;
            default: throw new InvalidDataException("Unknown binary value kind.");
        }
    }
    private static readonly string[] Properties = ["text", "utf16le", "runs", "start", "end", "props", "ints", "strings",
        "type", "variation", "value", "ticks", "kind", "base64", "writingSystems", "offset", "tag"];
    private static void Property(BinaryOutput output, string property)
    {
        var token = Array.IndexOf(Properties, property) + 1;
        output.Unsigned((ulong)token);
        if (token == 0) output.Text(property);
    }
    private static string Property(BinaryInput input)
    {
        var token = input.Unsigned();
        return token == 0 ? input.Text() : token <= (ulong)Properties.Length ? Properties[token - 1]
            : throw new InvalidDataException("Unknown property token.");
    }
    private static bool Shape(JsonElement value, params string[] names) => value.ValueKind == JsonValueKind.Object &&
        value.EnumerateObject().Count() == names.Length && names.All(name => value.TryGetProperty(name, out _));

    private static bool Raw(JsonElement value, out byte[]? bytes)
    {
        bytes = null;
        if (!Shape(value, "text", "utf16le")) return false;
        var text = value.GetProperty("text");
        var raw = value.GetProperty("utf16le");
        if (raw.ValueKind == JsonValueKind.Null) return text.ValueKind == JsonValueKind.Null;
        if (raw.ValueKind != JsonValueKind.String || text.ValueKind != JsonValueKind.String) return false;
        try { bytes = Convert.FromBase64String(raw.GetString()!); }
        catch (FormatException) { return false; }
        if (bytes.Length % 2 != 0) return false;
        var restored = Utf16(bytes);
        return Convert.ToBase64String(bytes) == raw.GetString() && JsonSerializer.SerializeToElement(restored).GetString() == text.GetString();
    }
    private static string Utf16(byte[] bytes) => new(Enumerable.Range(0, bytes.Length / 2)
        .Select(i => (char)(bytes[2 * i] | bytes[2 * i + 1] << 8)).ToArray());

    private static bool TryText(BinaryOutput output, JsonElement value, int depth)
    {
        if (Raw(value, out var bytes))
        {
            output.Byte(9); output.Byte(bytes is null ? (byte)0 : (byte)1);
            if (bytes is not null) output.Bytes(bytes);
            return true;
        }
        if (Shape(value, "text", "runs") && Raw(value.GetProperty("text"), out var raw) &&
            value.GetProperty("runs") is var runs && runs.ValueKind == JsonValueKind.Array && runs.GetArrayLength() == 1 &&
            runs[0] is var run && Shape(run, "start", "end", "text", "props") &&
            run.GetProperty("start").ValueKind == JsonValueKind.Number && run.GetProperty("start").TryGetInt32(out var start) && start == 0 &&
            run.GetProperty("end").ValueKind == JsonValueKind.Number && run.GetProperty("end").TryGetInt32(out var end) && end == (raw?.Length ?? 0) / 2 &&
            JsonElement.DeepEquals(run.GetProperty("text"), value.GetProperty("text")))
        {
            output.Byte(10); Write(output, value.GetProperty("text"), depth + 1); Write(output, run.GetProperty("props"), depth + 1);
            return true;
        }
        if (!Shape(value, "ints", "strings")) return false;
        var integers = value.GetProperty("ints");
        var strings = value.GetProperty("strings");
        if (integers.ValueKind != JsonValueKind.Array || strings.ValueKind != JsonValueKind.Array ||
            !integers.EnumerateArray().All(p => Shape(p, "type", "variation", "value") &&
                p.GetProperty("type").ValueKind == JsonValueKind.Number && p.GetProperty("type").TryGetInt64(out _) &&
                p.GetProperty("variation").ValueKind == JsonValueKind.Number && p.GetProperty("variation").TryGetInt64(out _)) ||
            !strings.EnumerateArray().All(p => Shape(p, "type", "value") && p.GetProperty("type").ValueKind == JsonValueKind.Number &&
                p.GetProperty("type").TryGetInt64(out _))) return false;
        output.Byte(11); output.Unsigned((ulong)integers.GetArrayLength());
        foreach (var integer in integers.EnumerateArray())
        {
            output.Integer(integer.GetProperty("type").GetInt64()); output.Integer(integer.GetProperty("variation").GetInt64());
            Write(output, integer.GetProperty("value"), depth + 1);
        }
        output.Unsigned((ulong)strings.GetArrayLength());
        foreach (var text in strings.EnumerateArray())
        { output.Integer(text.GetProperty("type").GetInt64()); Write(output, text.GetProperty("value"), depth + 1); }
        return true;
    }

    private static JsonElement ReadRaw(BinaryInput input)
    {
        var marker = input.Byte();
        if (marker > 1) throw new InvalidDataException("Invalid raw text marker.");
        var bytes = marker == 0 ? null : input.Bytes();
        if (bytes is not null && bytes.Length % 2 != 0) throw new InvalidDataException("Invalid raw text length.");
        return JsonSerializer.SerializeToElement(new { text = bytes is null ? null : Utf16(bytes),
            utf16le = bytes is null ? null : Convert.ToBase64String(bytes) });
    }
    private static JsonElement Element(BinaryInput reader, int depth)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) Read(reader, writer, depth);
        using var document = JsonDocument.Parse(stream.ToArray());
        return document.RootElement.Clone();
    }

}
