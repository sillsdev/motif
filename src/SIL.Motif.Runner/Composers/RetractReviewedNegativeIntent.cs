using System.Text.Json;
using System.Text.Json.Serialization;
using SIL.Motif.Contract.HumanJudgments;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Parsing;

namespace SIL.Motif.Runner.Composers;

/// <summary>The exact current heads a human confirms withdrawing from negative evidence.</summary>
public sealed record RetractReviewedNegativeIntent(
    CanonicalId RecordTypeId,
    string JudgmentId,
    IReadOnlyList<JudgmentPredecessor> ExpectedHeads,
    string? Reason = null,
    string? ActorId = null,
    string? ActorName = null,
    DateTimeOffset? JudgedAtUtc = null);

/// <summary>Parses a closed retraction intent with exact expected revision digests.</summary>
public static class RetractReviewedNegativeIntentParser
{
    private const string ConstructName = "RetractReviewedNegative";
    private static readonly string[] AllowedProperties =
        ["recordTypeId", "judgmentId", "expectedHeads", "reason", "actorId", "actorName", "judgedAtUtc"];
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        MaxDepth = 32,
    };

    /// <summary>Returns a validated one-head withdrawal or explicit multi-head resolution.</summary>
    public static RetractReviewedNegativeIntent Parse(JsonElement authored)
    {
        if (authored.ValueKind != JsonValueKind.Object)
            throw new ContractParseException($"'{ConstructName}': the authored intent must be a JSON object.");
        RejectDuplicates(authored);
        foreach (var property in authored.EnumerateObject())
            if (!AllowedProperties.Contains(property.Name, StringComparer.Ordinal))
                throw new ContractParseException($"'{ConstructName}': unknown property '{property.Name}'.");
        if (!authored.TryGetProperty("expectedHeads", out var headsElement) ||
            headsElement.ValueKind != JsonValueKind.Array)
            throw new ContractParseException($"'{ConstructName}': 'expectedHeads' must be a nonempty array.");

        JudgmentPredecessor[] heads;
        try
        {
            heads = JsonSerializer.Deserialize<JudgmentPredecessor[]>(headsElement.GetRawText(), Options)
                ?? throw new ContractParseException($"'{ConstructName}': 'expectedHeads' cannot be null.");
        }
        catch (JsonException error)
        {
            throw new ContractParseException($"'{ConstructName}': invalid 'expectedHeads': {error.Message}");
        }
        if (heads.Length is < 1 or > 256)
            throw new ContractParseException($"'{ConstructName}': 'expectedHeads' must contain 1 to 256 entries.");

        var intent = new RetractReviewedNegativeIntent(
            ReadId(authored, "recordTypeId"), ReadId(authored, "judgmentId").Value,
            Array.AsReadOnly(heads), ReadOptionalString(authored, "reason"),
            ReadOptionalString(authored, "actorId"), ReadOptionalString(authored, "actorName"),
            ReadTime(authored, "judgedAtUtc"));
        try
        {
            _ = HumanJudgmentCodec.Format(new HumanJudgment(CanonicalId.Mint("project/").Value,
                intent.JudgmentId, CanonicalId.Mint("revision/").Value, intent.ExpectedHeads,
                new RetractionJudgment(), intent.Reason,
                new JudgmentActor(JudgmentActorKind.Human, intent.ActorId, intent.ActorName), intent.JudgedAtUtc,
                ResolvesConflict: intent.ExpectedHeads.Count > 1));
        }
        catch (Exception error) when (error is ArgumentException or FormatException or InvalidOperationException)
        {
            throw new ContractParseException($"'{ConstructName}': invalid retraction: {error.Message}");
        }
        return intent;
    }

    private static CanonicalId ReadId(JsonElement authored, string property)
    {
        if (!authored.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String)
            throw new ContractParseException($"'{ConstructName}': '{property}' must be a portable id.");
        return CanonicalId.TryParse(value.GetString(), out var id, out var error)
            ? id
            : throw new ContractParseException($"'{ConstructName}': '{property}' is invalid: {error}");
    }

    private static string? ReadOptionalString(JsonElement authored, string property) =>
        !authored.TryGetProperty(property, out var value) || value.ValueKind == JsonValueKind.Null
            ? null
            : value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : throw new ContractParseException($"'{ConstructName}': '{property}' must be a string or null.");

    private static DateTimeOffset? ReadTime(JsonElement authored, string property)
    {
        if (!authored.TryGetProperty(property, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind != JsonValueKind.String ||
            !DateTimeOffset.TryParse(value.GetString(), System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind, out var result) || result.Offset != TimeSpan.Zero)
            throw new ContractParseException($"'{ConstructName}': '{property}' must be an explicit UTC timestamp.");
        return result;
    }

    private static void RejectDuplicates(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(property.Name))
                    throw new ContractParseException($"'{ConstructName}': duplicate property '{property.Name}'.");
                RejectDuplicates(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray()) RejectDuplicates(item);
    }
}
