using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace SIL.Motif.RecordStore;

internal sealed class ContentRow
{
    public int Id { get; set; }
    public byte[] Data { get; set; } = [];
    /// <summary>The value's identity while the row waits to be saved; not a column of the content table.</summary>
    public ContentKey Key { get; set; }
}

/// <summary>The SHA-256 of an immutable value's canonical bytes; it is the value's identity.</summary>
internal readonly record struct ContentKey(ulong A, ulong B, ulong C, ulong D)
{
    public static ContentKey Of(ReadOnlySpan<byte> canonical)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(canonical, hash);
        return new(BinaryPrimitives.ReadUInt64LittleEndian(hash), BinaryPrimitives.ReadUInt64LittleEndian(hash[8..]),
            BinaryPrimitives.ReadUInt64LittleEndian(hash[16..]), BinaryPrimitives.ReadUInt64LittleEndian(hash[24..]));
    }

    /// <summary>The 32 hash bytes this key was read from; the stored form of the identity.</summary>
    public byte[] ToBytes()
    {
        var bytes = new byte[32];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, A);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(8), B);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(16), C);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(24), D);
        return bytes;
    }
}

internal sealed partial class RecordContent
{
    // Rows stay encoded until read: opening a store costs one hash per row, not one decode per row.
    private Dictionary<ContentKey, int>? identities;
    private Dictionary<int, byte[]> encoded = new();
    // Concurrent: decode workers share one decoded value per row.
    private ConcurrentDictionary<int, JsonElement> values = new();
    private readonly List<ContentRow> pending = [];
    private int next;
    // Ids only grow past every loaded id, so the list stays ordered once loading has sorted it.
    private List<(int Id, byte[] Hash)> hashes = [];
    private readonly BinaryOutput scratch = new();
    private int decodedCount;
    /// <summary>Number of rows decoded from storage; proves that opening and hashing do not decode.</summary>
    internal int DecodedCount => Volatile.Read(ref decodedCount);
    internal int IdentityBuilds { get; private set; }
    public IReadOnlyList<ContentRow> Pending => pending;

    /// <summary>
    /// Set by a format 6 writer: every new row's identity is also written to <c>RecordContentIdentity</c>, and a store
    /// that already has rows finds a value's identity there instead of hashing every row to build a dictionary.
    /// </summary>
    internal bool PersistIdentities { get; set; }

    /// <summary>Looks an identity up in the store; null when the value is not stored. Set only with <see cref="PersistIdentities"/>.</summary>
    internal Func<ContentKey, int?>? PersistedLookup { get; set; }

    // Values added since the last save: not yet in the persisted identity table.
    private readonly Dictionary<ContentKey, int> recent = new();

    public void Saved()
    {
        pending.Clear();
        recent.Clear();
    }

    /// <summary>Writes the identity of every pending row; the caller's transaction makes it atomic with the rows.</summary>
    internal void WriteIdentities(Microsoft.Data.Sqlite.SqliteConnection connection, Microsoft.Data.Sqlite.SqliteTransaction? transaction)
    {
        if (pending.Count == 0) return;
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO RecordContentIdentity (Identity, Id) VALUES ($identity, $id)";
        var identity = command.Parameters.Add("$identity", Microsoft.Data.Sqlite.SqliteType.Blob);
        var id = command.Parameters.Add("$id", Microsoft.Data.Sqlite.SqliteType.Integer);
        foreach (var row in pending)
        {
            identity.Value = row.Key.ToBytes();
            id.Value = row.Id;
            command.ExecuteNonQuery();
        }
    }

    /// <summary>Every stored value's identity, from the rows themselves; the one-time fill of the persisted table.</summary>
    internal IEnumerable<KeyValuePair<ContentKey, int>> AllIdentities() => Identities();

    internal void Reload(IEnumerable<ContentRow> rows, string expectedDigest, CancellationToken ct)
    {
        var replacement = new RecordContent();
        replacement.Load(rows, ct);
        var verifiedDigest = replacement.Digest(ct);
        if (verifiedDigest != expectedDigest)
            throw new InvalidDataException("The published immutable content could not be verified.");
        ct.ThrowIfCancellationRequested();
        identities = null;
        recent.Clear();
        demanded.Clear();
        encoded = replacement.encoded;
        values = replacement.values;
        hashes = replacement.hashes;
        next = replacement.next;
        digest = verifiedDigest;
    }

    public void Load(IEnumerable<ContentRow> rows, CancellationToken ct = default)
    {
        var loaded = rows as ContentRow[] ?? rows.ToArray();
        foreach (var row in loaded) if (row.Id <= 0) throw new InvalidDataException("Invalid immutable content identity.");
        identities = null;
        recent.Clear();
        var rowHashes = new byte[loaded.Length][];
        try
        {
            ct.ThrowIfCancellationRequested();
            Parallel.For(0, loaded.Length, new ParallelOptions { MaxDegreeOfParallelism = Math.Min(4, Environment.ProcessorCount), CancellationToken = ct }, index =>
            {
                Check(loaded[index].Data);
                rowHashes[index] = SHA256.HashData(loaded[index].Data);
            });
        }
        catch (AggregateException failure) when (failure.InnerExceptions.Count > 0)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure.InnerExceptions[0]).Throw();
        }
        for (var index = 0; index < loaded.Length; index++)
        {
            var row = loaded[index];
            encoded.Add(row.Id, row.Data);
            hashes.Add((row.Id, rowHashes[index]));
            next = Math.Max(next, row.Id);
        }
        hashes.Sort((a, b) => a.Id.CompareTo(b.Id));
        for (var index = 1; index < hashes.Count; index++)
            if (hashes[index].Id == hashes[index - 1].Id) throw new InvalidDataException("Invalid immutable content identity.");
        digest = null;
    }

    /// <summary>Stores a value once. Its bytes are retained; the decoded element only when the caller will read it back.</summary>
    public int Put(JsonElement value, bool retain = true)
    {
        scratch.Reset();
        WriteCanonical(scratch, value);
        return PutCanonical(retain ? value : null);
    }

    public int PutGuid(Guid value)
    {
        scratch.Reset();
        scratch.Byte(2); scratch.Byte(6); scratch.Guid(value);
        return PutCanonical(null);
    }

    public int PutText(string value)
    {
        scratch.Reset();
        scratch.Byte(2); WriteString(scratch, value);
        return PutCanonical(null);
    }

    /// <summary>Stores an immutable value whose canonical bytes (after the version byte) the caller writes.</summary>
    public int PutBuilt(Action<BinaryOutput> write)
    {
        scratch.Reset();
        scratch.Byte(2);
        write(scratch);
        if (scratch.Written.Length > 64 * 1024 * 1024) throw new InvalidDataException("Record content is too large.");
        return PutCanonical(null);
    }

    // The stored hash is the identity: it is fixed by the stored bytes, never by decoding them.
    private int PutCanonical(JsonElement? retained)
    {
        var raw = scratch.Written;
        var key = ContentKey.Of(raw);
        // A store with rows and an identity table asks the table; every other store uses the dictionary.
        var lookup = identities is null && PersistedLookup is not null && encoded.Count > 0;
        var known = lookup ? recent : Identities();
        if (known.TryGetValue(key, out var prior)) return prior;
        if (lookup && PersistedLookup!(key) is int stored) return Verified(stored, key);
        var bytes = Encode(raw);
        var id = checked(++next);
        known.Add(key, id);
        encoded.Add(id, bytes);
        if (retained is { } value) values[id] = value.Clone();
        hashes.Add((id, SHA256.HashData(bytes)));
        pending.Add(new ContentRow { Id = id, Data = bytes, Key = key });
        digest = null;
        return id;
    }

    // A hit is checked against its one row, so a damaged identity table cannot hand back the wrong value.
    private int Verified(int id, ContentKey key)
    {
        if (!encoded.TryGetValue(id, out var bytes) || ContentKey.Of(Raw(bytes)) != key)
            throw new InvalidDataException("The immutable content identity index is corrupt.");
        return id;
    }

    private Dictionary<ContentKey, int> Identities()
    {
        if (identities is not null) return identities;
        IdentityBuilds++;
        var built = new Dictionary<ContentKey, int>(encoded.Count);
        foreach (var (id, bytes) in encoded)
            if (!built.TryAdd(ContentKey.Of(Raw(bytes)), id))
                throw new InvalidDataException("Duplicate immutable record content.");
        return identities = built;
    }

    // Hashing over a million rows is far too slow to repeat per call; rows are immutable, so only Put changes it.
    private string? digest;

    public string Digest(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (digest is not null) return digest;
        using var output = new BinaryOutput();
        foreach (var (id, hash) in hashes)
        {
            ct.ThrowIfCancellationRequested();
            output.Unsigned((ulong)id);
            output.Bytes(hash);
        }
        return digest = Convert.ToHexStringLower(SHA256.HashData(output.Finish()));
    }

    /// <summary>
    /// The decoded value of a stored row. Safe to call from several threads while no thread is calling <c>Put</c>,
    /// <c>Load</c> or <c>Reload</c>: the encoded rows are only read, and the first decoder of a row publishes the one
    /// shared element (a racing decoder drops its copy), so equal ids always yield the same element.
    /// </summary>
    public JsonElement Get(int id)
    {
        if (values.TryGetValue(id, out var value)) return value;
        if (!encoded.TryGetValue(id, out var bytes)) bytes = Demanded(id);
        var decoded = Decode(bytes);
        if (!values.TryAdd(id, decoded)) return values[id];
        Interlocked.Increment(ref decodedCount);
        return decoded;
    }

    /// <summary>
    /// Reads one stored row by id; set on a reader that serves a certified point without loading every row. The row's bytes
    /// are hashed and the hash must be the identity the store's identity table holds for that id.
    /// </summary>
    internal Func<int, byte[]?>? RowReader { get; set; }

    private readonly ConcurrentDictionary<int, byte[]> demanded = new();

    /// <summary>Rows fetched by id instead of loaded; each was checked against <c>RecordContentIdentity</c> when it was fetched.</summary>
    internal int DemandedCount => demanded.Count;

    private byte[] Demanded(int id)
    {
        if (demanded.TryGetValue(id, out var cached)) return cached;
        var read = RowReader ?? throw new InvalidDataException("Missing immutable record content.");
        var bytes = read(id) ?? throw new InvalidDataException("Missing immutable record content.");
        Check(bytes);
        var lookup = PersistedLookup ?? throw new InvalidDataException("The immutable content identity index is unavailable.");
        if (lookup(ContentKey.Of(Raw(bytes))) != id)
            throw new InvalidDataException($"RecordContent: row {id} does not match its stored identity (RecordContent or RecordContentIdentity is damaged).");
        return demanded.GetOrAdd(id, bytes);
    }

    internal static byte[] Canonical(JsonElement value)
    {
        using var writer = new BinaryOutput();
        WriteCanonical(writer, value);
        return writer.Finish();
    }

    internal static void WriteCanonical(BinaryOutput writer, JsonElement value)
    {
        writer.Byte(2);
        Write(writer, value, 0);
        if (writer.Written.Length > 64 * 1024 * 1024) throw new InvalidDataException("Record content is too large.");
    }

    private static byte[] Encode(ReadOnlySpan<byte> raw)
    {
        if (raw.Length >= 256)
        {
            using var output = new MemoryStream();
            output.WriteByte(1);
            using (var compression = new BrotliStream(output, CompressionLevel.Optimal, true)) compression.Write(raw);
            if (output.Length < raw.Length + 1) return output.ToArray();
        }
        var plain = new byte[raw.Length + 1];
        raw.CopyTo(plain.AsSpan(1));
        return plain;
    }

    private static void Check(byte[] bytes)
    {
        if (bytes.Length < 2 || bytes[0] > 1) throw new InvalidDataException("Unknown content envelope.");
        int first;
        if (bytes[0] == 0) first = bytes[1];
        else
        {
            using var input = new MemoryStream(bytes, 1, bytes.Length - 1);
            using var compression = new BrotliStream(input, CompressionMode.Decompress);
            first = compression.ReadByte();
        }
        if (first != 2) throw new InvalidDataException("Unknown content version.");
    }

    private static byte[] Raw(byte[] bytes)
    {
        if (bytes.Length < 2 || bytes[0] > 1) throw new InvalidDataException("Unknown content envelope.");
        if (bytes[0] == 0) return bytes[1..];
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
        return output.ToArray();
    }

    private static JsonElement Decode(byte[] bytes)
    {
        var raw = Raw(bytes);
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
            case JsonValueKind.String: WriteString(writer, value.GetString()!); break;
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

    internal static void WriteString(BinaryOutput writer, string text)
    {
        Span<char> canonical = stackalloc char[36];
        if (text.Length == 36 && Guid.TryParseExact(text, "D", out var guid) && guid.TryFormat(canonical, out _, "D") &&
            text.AsSpan().SequenceEqual(canonical))
        { writer.Byte(6); writer.Guid(guid); }
        else { writer.Byte(5); writer.Text(text); }
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
        var encoded = raw.GetString()!;
        try { bytes = Convert.FromBase64String(encoded); }
        catch (FormatException) { return false; }
        if (bytes.Length % 2 != 0) return false;
        if (Convert.ToBase64String(bytes) != encoded) return false;
        var restored = Utf16(bytes);
        var display = text.GetString();
        return restored == display || JsonSerializer.SerializeToElement(restored).GetString() == display;
    }
    private static string Utf16(byte[] bytes) => BitConverter.IsLittleEndian
        ? new string(MemoryMarshal.Cast<byte, char>(bytes.AsSpan()))
        : string.Create(bytes.Length / 2, bytes, static (characters, source) =>
        {
            for (var i = 0; i < characters.Length; i++)
                characters[i] = (char)BinaryPrimitives.ReadUInt16LittleEndian(source.AsSpan(i * 2));
        });

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
