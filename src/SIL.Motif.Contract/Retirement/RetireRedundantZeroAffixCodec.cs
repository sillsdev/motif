using System;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SIL.Motif.Contract.Canonicalization;
using SIL.Motif.Contract.Ids;

namespace SIL.Motif.Contract.Retirement;

public static class RetireRedundantZeroAffixCodec
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        MaxDepth = 16
    };

    public static RetireRedundantZeroAffixIntentDocument Parse(string json)
    {
        try
        {
            if (Encoding.UTF8.GetByteCount(json) > 1024 * 1024)
                throw new FormatException("Zero-affix retirement intent exceeds 1 MiB.");
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("format", out _) ||
                !root.TryGetProperty("version", out _))
                throw new FormatException("Missing format/version.");
            var value = JsonSerializer.Deserialize<RetireRedundantZeroAffixIntentDocument>(json, Options)
                ?? throw new FormatException("Null zero-affix retirement intent.");
            if (value.Format != "motif-retire-redundant-zero-affix" || value.Version != 1)
                throw new FormatException("Unsupported zero-affix retirement format/version.");
            if (value.Retirement is null || !CanonicalId.TryParse(value.Retirement.Entry, out _))
                throw new FormatException("Retirement requires one canonical entry identity.");
            return value;
        }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException)
        {
            throw new FormatException("Invalid closed zero-affix retirement intent: " + error.Message, error);
        }
    }

    public static string ToJson(RetireRedundantZeroAffixIntentDocument value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var json = JsonSerializer.Serialize(value, Options);
        Parse(json);
        return CanonicalJson.Canonicalize(json);
    }

    public static string IntentDigest(RetireRedundantZeroAffixIntentDocument value) =>
        Canonicalization.IntentDigest.Sha256Of(CanonicalJson.CanonicalizeToUtf8(ToJson(value)));
}
