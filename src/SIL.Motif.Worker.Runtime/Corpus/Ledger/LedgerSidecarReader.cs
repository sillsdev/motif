using System.Text.Json;

namespace SIL.Motif.Worker.Corpus.Ledger;

/// <summary>Reads <c>ledger.meta.json</c>: the ten required keys, exactly typed, duplicates refused.</summary>
internal static class LedgerSidecarReader
{
    private static readonly string[] Folds = ["none", "lower", "lower+apostrophe"];

    public static LedgerSidecar Read(byte[] bytes)
    {
        RejectDuplicateKeys(bytes);
        JsonDocument document;
        try { document = JsonDocument.Parse(bytes); }
        catch (JsonException)
        {
            throw new LedgerRefusalException(LedgerRefusalCode.SidecarInvalidJson, "The sidecar is not valid JSON.");
        }
        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new LedgerRefusalException(LedgerRefusalCode.SidecarInvalidJson, "The sidecar is not a JSON object.");
            var sha = Text(root, "tokenizerSha256");
            if (sha.Length != 64 || !sha.All(char.IsAsciiHexDigit)) throw Invalid("tokenizerSha256");
            var fold = Text(root, "fold");
            if (!Folds.Contains(fold, StringComparer.Ordinal)) throw Invalid("fold");
            var normalization = Text(root, "normalization");
            if (normalization is not ("NFC" or "NFD")) throw Invalid("normalization");
            return new(LedgerTsvReader.Sha256(bytes), Text(root, "tokenizerId"), sha.ToLowerInvariant(), fold, normalization,
                SourceIds(root), Scope(root), Text(root, "frequencySource"), Text(root, "grammarRevision"),
                Count(root, "rowCount"), Count(root, "tokenSum"));
        }
    }

    private static void RejectDuplicateKeys(byte[] bytes)
    {
        var scopes = new Stack<HashSet<string>?>();
        try
        {
            var reader = new Utf8JsonReader(bytes);
            while (reader.Read())
                switch (reader.TokenType)
                {
                    case JsonTokenType.StartObject: scopes.Push(new(StringComparer.Ordinal)); break;
                    case JsonTokenType.StartArray: scopes.Push(null); break;
                    case JsonTokenType.EndObject or JsonTokenType.EndArray: scopes.Pop(); break;
                    case JsonTokenType.PropertyName when !scopes.Peek()!.Add(reader.GetString()!):
                        throw new LedgerRefusalException(LedgerRefusalCode.SidecarDuplicateKey, "The sidecar repeats a key.");
                }
        }
        catch (JsonException)
        {
            throw new LedgerRefusalException(LedgerRefusalCode.SidecarInvalidJson, "The sidecar is not valid JSON.");
        }
    }

    public static void Validate(LedgerSidecar sidecar, LedgerFile ledger, string? declaredLedgerSha256 = null)
    {
        if (declaredLedgerSha256 is not null && !string.Equals(declaredLedgerSha256, ledger.Digest, StringComparison.OrdinalIgnoreCase))
            throw new LedgerRefusalException(LedgerRefusalCode.DigestMismatch, "The sidecar names another ledger digest.");
        if (sidecar.RowCount != ledger.Rows.Count)
            throw new LedgerRefusalException(LedgerRefusalCode.RowCountMismatch, "The sidecar rowCount disagrees with the ledger.");
        if (sidecar.TokenSum != ledger.TokenSum)
            throw new LedgerRefusalException(LedgerRefusalCode.TokenSumMismatch, "The sidecar tokenSum disagrees with the ledger.");
    }

    public static string? DeclaredString(byte[] sidecarBytes, string key)
    {
        using var document = JsonDocument.Parse(sidecarBytes);
        return document.RootElement.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;
    }

    private static JsonElement Required(JsonElement root, string key) => root.TryGetProperty(key, out var value)
        ? value : throw new LedgerRefusalException(LedgerRefusalCode.SidecarMissingKey, $"The sidecar has no {key}.");

    private static LedgerRefusalException Invalid(string key) =>
        new(LedgerRefusalCode.SidecarInvalidValue, $"The sidecar value for {key} is invalid.");

    private static string Text(JsonElement root, string key)
    {
        var value = Required(root, key);
        return value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text ? text : throw Invalid(key);
    }

    private static long Count(JsonElement root, string key)
    {
        var value = Required(root, key);
        return value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var count) && count >= 0 ? count : throw Invalid(key);
    }

    private static IReadOnlyList<string> SourceIds(JsonElement root)
    {
        var value = Required(root, "sourceIds");
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() == 0) throw Invalid("sourceIds");
        var ids = new List<string>();
        foreach (var item in value.EnumerateArray())
            if (item.ValueKind != JsonValueKind.String || item.GetString() is not { Length: > 0 } id || ids.Contains(id))
                throw Invalid("sourceIds");
            else ids.Add(id);
        return ids;
    }

    private static string Scope(JsonElement root)
    {
        var value = Required(root, "scopeFilter");
        var nonempty = value.ValueKind switch
        {
            JsonValueKind.String => value.GetString()!.Length > 0,
            JsonValueKind.Array => value.GetArrayLength() > 0,
            JsonValueKind.Object => value.EnumerateObject().Any(),
            _ => false,
        };
        return nonempty ? (value.ValueKind == JsonValueKind.String ? value.GetString()! : value.GetRawText()) : throw Invalid("scopeFilter");
    }
}
