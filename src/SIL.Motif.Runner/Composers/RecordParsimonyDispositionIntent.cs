using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using SIL.Motif.Contract.HumanJudgments;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Parsing;

namespace SIL.Motif.Runner.Composers;

/// <summary>The exact finding context and choice an agent may stage for human review.</summary>
public sealed record RecordParsimonyDispositionIntent(
    CanonicalId RecordTypeId,
    string MeasureId,
    HumanJudgmentSubject Subject,
    ParsimonyDispositionKind Disposition,
    string EvidenceDigest,
    string EvidenceContract,
    string SubjectCaption,
    string MeasureCaption,
    string? Reason = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Question = null,
    CanonicalId? ReportId = null);

/// <summary>Parses the closed semantic intent for recording one Parsimony disposition.</summary>
public static class RecordParsimonyDispositionIntentParser
{
    private const string ConstructName = "RecordParsimonyDisposition";
    private static readonly string[] AllowedProperties =
    [
        "recordTypeId", "measureId", "subject", "disposition", "evidenceDigest",
        "evidenceContract", "subjectCaption", "measureCaption", "reason", "question", "reportId",
    ];

    private static readonly JsonSerializerOptions SubjectOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        MaxDepth = 32,
    };

    /// <summary>Returns a typed value for the closed Parsimony disposition intent object.</summary>
    public static RecordParsimonyDispositionIntent Parse(JsonElement authored)
    {
        RequireObject(authored, "the authored construct");
        RejectDuplicates(authored);
        RejectUnknownProperties(authored, AllowedProperties, ConstructName);

        var disposition = GetRequiredString(authored, "disposition") switch
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

        var subjectElement = authored.TryGetProperty("subject", out var subjectValue)
            ? subjectValue
            : throw new ContractParseException($"'{ConstructName}': 'subject' is required.");
        RequireObject(subjectElement, "'subject'");
        var subject = DeserializeSubject(subjectElement);
        try
        {
            _ = HumanJudgmentCodec.SubjectKey(subject);
        }
        catch (Exception error) when (error is ArgumentException or FormatException or InvalidOperationException)
        {
            throw new ContractParseException($"'{ConstructName}': invalid 'subject': {error.Message}");
        }

        return new RecordParsimonyDispositionIntent(
            GetRequiredCanonicalId(authored, "recordTypeId"),
            GetRequiredString(authored, "measureId"),
            subject,
            disposition,
            GetRequiredString(authored, "evidenceDigest"),
            GetRequiredString(authored, "evidenceContract"),
            GetRequiredString(authored, "subjectCaption"),
            GetRequiredString(authored, "measureCaption"),
            GetOptionalString(authored, "reason"),
            hasQuestion ? GetRequiredString(authored, "question") : null,
            authored.TryGetProperty("reportId", out _) ? GetRequiredCanonicalId(authored, "reportId") : null);
    }

    private static HumanJudgmentSubject DeserializeSubject(JsonElement subject)
    {
        try
        {
            return JsonSerializer.Deserialize<HumanJudgmentSubject>(subject.GetRawText(), SubjectOptions)
                ?? throw new ContractParseException($"'{ConstructName}': 'subject' cannot be null.");
        }
        catch (JsonException error)
        {
            throw new ContractParseException($"'{ConstructName}': invalid closed 'subject': {error.Message}");
        }
    }

    private static string GetRequiredString(JsonElement authored, string propertyName)
    {
        if (!authored.TryGetProperty(propertyName, out var element) || element.ValueKind != JsonValueKind.String)
            throw new ContractParseException($"'{ConstructName}': '{propertyName}' is required and must be a string.");
        return element.GetString()!;
    }

    private static string? GetOptionalString(JsonElement authored, string propertyName)
    {
        if (!authored.TryGetProperty(propertyName, out var element)) return null;
        if (element.ValueKind == JsonValueKind.Null) return null;
        if (element.ValueKind != JsonValueKind.String)
            throw new ContractParseException($"'{ConstructName}': '{propertyName}' must be a string or null.");
        return element.GetString();
    }

    private static CanonicalId GetRequiredCanonicalId(JsonElement authored, string propertyName)
    {
        var text = GetRequiredString(authored, propertyName);
        if (!CanonicalId.TryParse(text, out var id, out var error))
            throw new ContractParseException(
                $"'{ConstructName}': '{propertyName}' ('{text}') is not a valid canonical id: {error}");
        return id;
    }

    private static void RequireObject(JsonElement value, string description)
    {
        if (value.ValueKind != JsonValueKind.Object)
            throw new ContractParseException($"'{ConstructName}': {description} must be a JSON object.");
    }

    private static void RejectUnknownProperties(
        JsonElement value, IReadOnlyCollection<string> allowedProperties, string objectName)
    {
        foreach (var property in value.EnumerateObject())
            if (!allowedProperties.Contains(property.Name, StringComparer.Ordinal))
                throw new ContractParseException($"'{objectName}': unknown property '{property.Name}'.");
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
        {
            foreach (var item in value.EnumerateArray()) RejectDuplicates(item);
        }
    }
}
