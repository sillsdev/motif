using System.Text.Json;
using System.Text.Json.Serialization;
using SIL.Motif.Contract.HumanJudgments;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Parsing;

namespace SIL.Motif.Runner.Composers;

/// <summary>A proposed update to an applied disposition's choice or optional reason.</summary>
public sealed record ReviseParsimonyDispositionIntent(
    CanonicalId RecordId,
    IReadOnlyList<JudgmentPredecessor> ExpectedHeads,
    ParsimonyDispositionKind Disposition,
    string? Reason = null,
    bool ClearReason = false,
    string? Question = null);

/// <summary>Parses the closed input for revising one saved Parsimony disposition.</summary>
public static class ReviseParsimonyDispositionIntentParser
{
    private const string ConstructName = "ReviseParsimonyDisposition";
    private static readonly string[] AllowedProperties =
        ["recordId", "expectedHeads", "disposition", "reason", "clearReason", "question"];
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        MaxDepth = 32,
    };

    /// <summary>Returns a typed revision after validating exact heads and the closed choice vocabulary.</summary>
    public static ReviseParsimonyDispositionIntent Parse(JsonElement authored)
    {
        RequireObject(authored);
        RejectDuplicates(authored);
        foreach (var property in authored.EnumerateObject())
            if (!AllowedProperties.Contains(property.Name, StringComparer.Ordinal))
                throw new ContractParseException($"'{ConstructName}': unknown property '{property.Name}'.");

        var recordId = ReadId(authored, "recordId");
        var heads = ReadHeads(authored);
        var disposition = ReadString(authored, "disposition") switch
        {
            "fix" => ParsimonyDispositionKind.Fix,
            "keep" => ParsimonyDispositionKind.Keep,
            "ask" => ParsimonyDispositionKind.Ask,
            "defer" => ParsimonyDispositionKind.Defer,
            var value => throw new ContractParseException(
                $"'{ConstructName}': 'disposition' has unknown value '{value}'."),
        };
        var hasQuestion = authored.TryGetProperty("question", out _);
        if ((disposition == ParsimonyDispositionKind.Ask) != hasQuestion)
            throw new ContractParseException(
                $"'{ConstructName}': 'question' is required only when disposition is 'ask'.");
        var question = hasQuestion ? ReadString(authored, "question") : null;
        var reason = ReadOptionalString(authored, "reason");
        var clearReason = ReadOptionalBoolean(authored, "clearReason");
        if (clearReason && reason is not null)
            throw new ContractParseException($"'{ConstructName}': 'reason' and 'clearReason' cannot be combined.");
        if (reason is not null && string.IsNullOrWhiteSpace(reason))
            throw new ContractParseException($"'{ConstructName}': 'reason' must not be blank.");

        return new ReviseParsimonyDispositionIntent(recordId, heads, disposition, reason, clearReason, question);
    }

    private static IReadOnlyList<JudgmentPredecessor> ReadHeads(JsonElement authored)
    {
        if (!authored.TryGetProperty("expectedHeads", out var value) || value.ValueKind != JsonValueKind.Array)
            throw new ContractParseException($"'{ConstructName}': 'expectedHeads' must be a nonempty array.");
        JudgmentPredecessor[] heads;
        try
        {
            heads = JsonSerializer.Deserialize<JudgmentPredecessor[]>(value.GetRawText(), Options)
                ?? throw new ContractParseException($"'{ConstructName}': 'expectedHeads' cannot be null.");
        }
        catch (JsonException error)
        {
            throw new ContractParseException($"'{ConstructName}': invalid 'expectedHeads': {error.Message}");
        }
        if (heads.Length is < 1 or > 256)
            throw new ContractParseException($"'{ConstructName}': 'expectedHeads' must contain 1 to 256 entries.");
        if (heads.Any(head => head is null || !CanonicalId.TryParse(head.RevisionId, out _) ||
                              !IsCanonicalDigest(head.ContentDigest)) ||
            heads.Select(head => head.RevisionId).Distinct(StringComparer.Ordinal).Count() != heads.Length)
            throw new ContractParseException($"'{ConstructName}': 'expectedHeads' contains an invalid or repeated head.");
        return Array.AsReadOnly(heads);
    }

    private static CanonicalId ReadId(JsonElement authored, string property)
    {
        if (!authored.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String)
            throw new ContractParseException($"'{ConstructName}': '{property}' must be a portable id.");
        return CanonicalId.TryParse(value.GetString(), out var id, out var error)
            ? id
            : throw new ContractParseException($"'{ConstructName}': '{property}' is invalid: {error}");
    }

    private static string ReadString(JsonElement authored, string property) =>
        authored.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : throw new ContractParseException($"'{ConstructName}': '{property}' must be a string.");

    private static string? ReadOptionalString(JsonElement authored, string property) =>
        !authored.TryGetProperty(property, out var value) || value.ValueKind == JsonValueKind.Null
            ? null
            : value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : throw new ContractParseException($"'{ConstructName}': '{property}' must be a string or null.");

    private static bool ReadOptionalBoolean(JsonElement authored, string property) =>
        !authored.TryGetProperty(property, out var value) || value.ValueKind == JsonValueKind.False
            ? false
            : value.ValueKind == JsonValueKind.True
                ? true
                : throw new ContractParseException($"'{ConstructName}': '{property}' must be a boolean.");

    private static bool IsCanonicalDigest(string? value) =>
        value is { Length: 71 } && value.StartsWith("sha256:", StringComparison.Ordinal) &&
        value.AsSpan(7).IndexOfAnyExcept("0123456789abcdef") < 0;

    private static void RequireObject(JsonElement authored)
    {
        if (authored.ValueKind != JsonValueKind.Object)
            throw new ContractParseException($"'{ConstructName}': the authored intent must be a JSON object.");
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
