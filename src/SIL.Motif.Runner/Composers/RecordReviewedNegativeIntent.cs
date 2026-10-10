using System.Text.Json;
using System.Text.Json.Serialization;
using SIL.Motif.Contract.HumanJudgments;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Parsing;

namespace SIL.Motif.Runner.Composers;

/// <summary>The explicitly confirmed content staged as one reviewed-negative revision.</summary>
public sealed record RecordReviewedNegativeIntent(
    CanonicalId RecordTypeId,
    string CaseId,
    string WritingSystem,
    string Form,
    string Context,
    NegativeJudgmentTarget Target,
    string? WordformId = null,
    string? AnalysisId = null,
    string? Reason = null,
    string? JudgmentId = null,
    IReadOnlyList<JudgmentPredecessor>? ExpectedHeads = null,
    string? ActorId = null,
    string? ActorName = null,
    DateTimeOffset? JudgedAtUtc = null,
    CanonicalId? ReportId = null);

/// <summary>Parses the closed reviewed-negative intent authored by a human.</summary>
public static class RecordReviewedNegativeIntentParser
{
    private const string ConstructName = "RecordReviewedNegative";
    private static readonly string[] AllowedProperties =
    [
        "recordTypeId", "caseId", "writingSystem", "form", "context", "target", "wordformId", "analysisId",
        "reason", "judgmentId", "expectedHeads", "actorId", "actorName", "judgedAtUtc", "reportId",
    ];
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        MaxDepth = 32,
    };

    /// <summary>Returns a validated surface or exact ordered reading intent.</summary>
    public static RecordReviewedNegativeIntent Parse(JsonElement authored)
    {
        RequireObject(authored, "the authored intent");
        RejectDuplicates(authored);
        RejectUnknownProperties(authored, AllowedProperties, ConstructName);
        var targetElement = authored.TryGetProperty("target", out var targetValue)
            ? targetValue
            : throw new ContractParseException($"'{ConstructName}': 'target' is required.");
        var target = DeserializeTarget(targetElement);
        var expectedHeads = ReadExpectedHeads(authored);
        var judgmentId = OptionalId(authored, "judgmentId");
        if ((judgmentId is null) != (expectedHeads.Count == 0))
            throw new ContractParseException(
                $"'{ConstructName}': 'judgmentId' and nonempty 'expectedHeads' must be supplied together for a revision.");

        var intent = new RecordReviewedNegativeIntent(
            RequiredId(authored, "recordTypeId"),
            RequiredId(authored, "caseId").Value,
            RequiredString(authored, "writingSystem"),
            RequiredString(authored, "form"),
            RequiredString(authored, "context"),
            target,
            OptionalId(authored, "wordformId")?.Value,
            OptionalId(authored, "analysisId")?.Value,
            OptionalString(authored, "reason"),
            judgmentId?.Value,
            expectedHeads,
            OptionalString(authored, "actorId"),
            OptionalString(authored, "actorName"),
            OptionalTime(authored, "judgedAtUtc"),
            OptionalId(authored, "reportId"));

        try
        {
            var projectId = CanonicalId.Mint("project/").Value;
            var logicalId = intent.JudgmentId ?? intent.CaseId;
            _ = HumanJudgmentCodec.Format(new HumanJudgment(projectId, logicalId,
                CanonicalId.Mint("revision/").Value, expectedHeads,
                new ReviewedNegativeJudgment(intent.CaseId, intent.WritingSystem, intent.Form, intent.Context,
                    intent.Target, intent.WordformId, intent.AnalysisId), intent.Reason,
                new JudgmentActor(JudgmentActorKind.Human, intent.ActorId, intent.ActorName), intent.JudgedAtUtc,
                new JudgmentSource(intent.ReportId?.Value), expectedHeads.Count > 1));
        }
        catch (Exception error) when (error is ArgumentException or FormatException or InvalidOperationException)
        {
            throw new ContractParseException($"'{ConstructName}': invalid reviewed negative: {error.Message}");
        }

        return intent;
    }

    private static NegativeJudgmentTarget DeserializeTarget(JsonElement target)
    {
        RequireObject(target, "'target'");
        try
        {
            return JsonSerializer.Deserialize<NegativeJudgmentTarget>(target.GetRawText(), Options)
                ?? throw new ContractParseException($"'{ConstructName}': 'target' cannot be null.");
        }
        catch (JsonException error)
        {
            throw new ContractParseException($"'{ConstructName}': invalid closed 'target': {error.Message}");
        }
    }

    private static IReadOnlyList<JudgmentPredecessor> ReadExpectedHeads(JsonElement authored)
    {
        if (!authored.TryGetProperty("expectedHeads", out var value)) return [];
        if (value.ValueKind != JsonValueKind.Array)
            throw new ContractParseException($"'{ConstructName}': 'expectedHeads' must be an array.");
        try
        {
            return JsonSerializer.Deserialize<JudgmentPredecessor[]>(value.GetRawText(), Options)
                ?? throw new ContractParseException($"'{ConstructName}': 'expectedHeads' cannot be null.");
        }
        catch (JsonException error)
        {
            throw new ContractParseException($"'{ConstructName}': invalid 'expectedHeads': {error.Message}");
        }
    }

    private static CanonicalId RequiredId(JsonElement authored, string property) =>
        ParseId(RequiredString(authored, property), property);

    private static CanonicalId? OptionalId(JsonElement authored, string property) =>
        authored.TryGetProperty(property, out var value) && value.ValueKind != JsonValueKind.Null
            ? value.ValueKind == JsonValueKind.String
                ? ParseId(value.GetString()!, property)
                : throw new ContractParseException($"'{ConstructName}': '{property}' must be an id or null.")
            : null;

    private static CanonicalId ParseId(string value, string property) =>
        CanonicalId.TryParse(value, out var id, out var error) ? id :
            throw new ContractParseException($"'{ConstructName}': '{property}' ('{value}') is not a portable id: {error}");

    private static string RequiredString(JsonElement authored, string property) =>
        authored.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : throw new ContractParseException($"'{ConstructName}': '{property}' is required and must be a string.");

    private static string? OptionalString(JsonElement authored, string property) =>
        !authored.TryGetProperty(property, out var value) || value.ValueKind == JsonValueKind.Null
            ? null
            : value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : throw new ContractParseException($"'{ConstructName}': '{property}' must be a string or null.");

    private static DateTimeOffset? OptionalTime(JsonElement authored, string property)
    {
        if (!authored.TryGetProperty(property, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind != JsonValueKind.String ||
            !DateTimeOffset.TryParse(value.GetString(), System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind, out var result))
            throw new ContractParseException($"'{ConstructName}': '{property}' must be an explicit timestamp.");
        if (result.Offset != TimeSpan.Zero)
            throw new ContractParseException($"'{ConstructName}': '{property}' must use UTC.");
        return result;
    }

    private static void RequireObject(JsonElement value, string name)
    {
        if (value.ValueKind != JsonValueKind.Object)
            throw new ContractParseException($"'{ConstructName}': {name} must be a JSON object.");
    }

    private static void RejectUnknownProperties(JsonElement value, IReadOnlyCollection<string> allowed, string name)
    {
        foreach (var property in value.EnumerateObject())
            if (!allowed.Contains(property.Name, StringComparer.Ordinal))
                throw new ContractParseException($"'{name}': unknown property '{property.Name}'.");
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
