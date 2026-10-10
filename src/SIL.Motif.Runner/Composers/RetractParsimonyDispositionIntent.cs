using System.Text.Json;
using System.Text.Json.Serialization;
using SIL.Motif.Contract.HumanJudgments;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Parsing;

namespace SIL.Motif.Runner.Composers;

/// <summary>A proposed withdrawal of one saved Parsimony disposition.</summary>
public sealed record RetractParsimonyDispositionIntent(
    CanonicalId RecordId,
    IReadOnlyList<JudgmentPredecessor> ExpectedHeads);

/// <summary>Parses the closed input for withdrawing one saved Parsimony disposition.</summary>
public static class RetractParsimonyDispositionIntentParser
{
    private const string ConstructName = "RetractParsimonyDisposition";
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        MaxDepth = 32,
    };

    /// <summary>Returns a typed withdrawal after validating exact head identities and digests.</summary>
    public static RetractParsimonyDispositionIntent Parse(JsonElement authored)
    {
        if (authored.ValueKind != JsonValueKind.Object)
            throw new ContractParseException($"'{ConstructName}': the authored intent must be a JSON object.");
        RejectDuplicates(authored);
        foreach (var property in authored.EnumerateObject())
            if (property.Name is not ("recordId" or "expectedHeads"))
                throw new ContractParseException($"'{ConstructName}': unknown property '{property.Name}'.");
        if (!authored.TryGetProperty("recordId", out var recordValue) || recordValue.ValueKind != JsonValueKind.String)
            throw new ContractParseException($"'{ConstructName}': 'recordId' must be a portable id.");
        if (!CanonicalId.TryParse(recordValue.GetString(), out var recordId, out var idError))
            throw new ContractParseException($"'{ConstructName}': 'recordId' is invalid: {idError}");
        if (!authored.TryGetProperty("expectedHeads", out var headsValue) || headsValue.ValueKind != JsonValueKind.Array)
            throw new ContractParseException($"'{ConstructName}': 'expectedHeads' must be a nonempty array.");

        JudgmentPredecessor[] heads;
        try
        {
            heads = JsonSerializer.Deserialize<JudgmentPredecessor[]>(headsValue.GetRawText(), Options)
                ?? throw new ContractParseException($"'{ConstructName}': 'expectedHeads' cannot be null.");
        }
        catch (JsonException error)
        {
            throw new ContractParseException($"'{ConstructName}': invalid 'expectedHeads': {error.Message}");
        }
        if (heads.Length is < 1 or > 256 || heads.Any(head => head is null ||
                !CanonicalId.TryParse(head.RevisionId, out _) || !IsCanonicalDigest(head.ContentDigest)) ||
            heads.Select(head => head.RevisionId).Distinct(StringComparer.Ordinal).Count() != heads.Length)
            throw new ContractParseException($"'{ConstructName}': 'expectedHeads' contains an invalid or repeated head.");
        return new RetractParsimonyDispositionIntent(recordId, Array.AsReadOnly(heads));
    }

    private static bool IsCanonicalDigest(string? value) =>
        value is { Length: 71 } && value.StartsWith("sha256:", StringComparison.Ordinal) &&
        value.AsSpan(7).IndexOfAnyExcept("0123456789abcdef") < 0;

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
