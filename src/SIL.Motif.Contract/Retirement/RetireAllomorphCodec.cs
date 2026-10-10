using System;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SIL.Motif.Contract.Canonicalization;

namespace SIL.Motif.Contract.Retirement;

public static class RetireAllomorphCodec
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        MaxDepth = 32,
        Converters = { new JsonStringEnumConverter<AllomorphRetirementScope>(JsonNamingPolicy.CamelCase, false) }
    };

    public static RetireAllomorphIntentDocument Parse(string json)
    {
        try
        {
            AllomorphRetirementValidation.Require(Encoding.UTF8.GetByteCount(json) <= 16 * 1024 * 1024,
                "Standalone retirement envelope exceeds 16 MiB.");
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
            AllomorphRetirementCodec.CheckObject(document.RootElement);
            AllomorphRetirementValidation.Require(document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("format", out _) &&
                document.RootElement.TryGetProperty("version", out _), "Missing format/version.");
            var value = JsonSerializer.Deserialize<RetireAllomorphIntentDocument>(json, Options)
                ?? throw new FormatException("Null standalone retirement intent.");
            AllomorphRetirementValidation.Validate(value);
            return value;
        }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException)
        {
            throw new FormatException("Invalid closed standalone allomorph retirement: " + error.Message, error);
        }
    }

    public static string ToJson(RetireAllomorphIntentDocument value)
    {
        var json = JsonSerializer.Serialize(value, Options);
        Parse(json);
        return CanonicalJson.Canonicalize(json);
    }

    public static string IntentDigest(RetireAllomorphIntentDocument value) =>
        Canonicalization.IntentDigest.Sha256Of(CanonicalJson.CanonicalizeToUtf8(ToJson(value)));
}
