using System.Text.Json;
using System.Text.Json.Serialization;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Review;

/// <summary>Serialization settings for the strict public review contract.</summary>
public static class ReviewJson
{
    /// <summary>The report's camel-case JSON settings.</summary>
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        PropertyNameCaseInsensitive = false,
        NumberHandling = JsonNumberHandling.Strict,
        Converters = { new ReviewBaselineTokenConverter() },
    };

}

/// <summary>Reads only the declared TrialReviewResult variants.</summary>
public sealed class TrialReviewResultConverter : JsonConverter<TrialReviewResult>
{
    public override TrialReviewResult Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("state", out var kind) ||
            kind.ValueKind != JsonValueKind.String)
            throw new JsonException("Missing review discriminator.");
        return kind.GetString() switch
        {
            "no-trial" => JsonSerializer.Deserialize<NoTrialResult>(doc.RootElement, options)!,
            "pending" => JsonSerializer.Deserialize<PendingTrialResult>(doc.RootElement, options)!,
            "report" => JsonSerializer.Deserialize<AvailableTrialResult>(doc.RootElement, options)!,
            _ => throw new JsonException("Unknown review discriminator."),
        };
    }

    public override void Write(Utf8JsonWriter writer, TrialReviewResult value, JsonSerializerOptions options)
    {
        switch (value)
        {
            case NoTrialResult item: JsonSerializer.Serialize(writer, item, options); break;
            case PendingTrialResult item: JsonSerializer.Serialize(writer, item, options); break;
            case AvailableTrialResult item: JsonSerializer.Serialize(writer, item, options); break;
            default: throw new JsonException("Unknown review variant.");
        }
    }
}

/// <summary>Reads only the declared FlagFacts variants.</summary>
public sealed class FlagFactsConverter : JsonConverter<FlagFacts>
{
    public override FlagFacts Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("kind", out var kind) ||
            kind.ValueKind != JsonValueKind.String)
            throw new JsonException("Missing review discriminator.");
        return kind.GetString() switch
        {
            "word-failures" => JsonSerializer.Deserialize<NewWordFacts>(doc.RootElement, options)!,
            "reading-loss" => JsonSerializer.Deserialize<ReadingLossFacts>(doc.RootElement, options)!,
            "negative-accepted" => JsonSerializer.Deserialize<NegativeFacts>(doc.RootElement, options)!,
            "parser-work" => JsonSerializer.Deserialize<WorkChange>(doc.RootElement, options)!,
            "new-findings" => JsonSerializer.Deserialize<NewFindingFacts>(doc.RootElement, options)!,
            _ => throw new JsonException("Unknown review discriminator."),
        };
    }

    public override void Write(Utf8JsonWriter writer, FlagFacts value, JsonSerializerOptions options)
    {
        switch (value)
        {
            case NewWordFacts item: JsonSerializer.Serialize(writer, item, options); break;
            case ReadingLossFacts item: JsonSerializer.Serialize(writer, item, options); break;
            case NegativeFacts item: JsonSerializer.Serialize(writer, item, options); break;
            case WorkChange item: JsonSerializer.Serialize(writer, item, options); break;
            case NewFindingFacts item: JsonSerializer.Serialize(writer, item, options); break;
            default: throw new JsonException("Unknown review variant.");
        }
    }
}

/// <summary>Reads only the declared ReviewRow variants.</summary>
public sealed class ReviewRowConverter : JsonConverter<ReviewRow>
{
    public override ReviewRow Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("rowKind", out var kind) ||
            kind.ValueKind != JsonValueKind.String)
            throw new JsonException("Missing review discriminator.");
        return kind.GetString() switch
        {
            "word" => JsonSerializer.Deserialize<WordRow>(doc.RootElement, options)!,
            "token" => JsonSerializer.Deserialize<TokenRow>(doc.RootElement, options)!,
            "cell" => JsonSerializer.Deserialize<CellRow>(doc.RootElement, options)!,
            "warning" => JsonSerializer.Deserialize<DiagnosticRow>(doc.RootElement, options)!,
            "parsimony" => JsonSerializer.Deserialize<FindingRow>(doc.RootElement, options)!,
            "effect" => JsonSerializer.Deserialize<EffectRow>(doc.RootElement, options)!,
            "evidence" => JsonSerializer.Deserialize<EvidenceRow>(doc.RootElement, options)!,
            "reading" => JsonSerializer.Deserialize<ReadingRow>(doc.RootElement, options)!,
            "expectation" => JsonSerializer.Deserialize<ExpectationRow>(doc.RootElement, options)!,
            "source" => JsonSerializer.Deserialize<SourceRow>(doc.RootElement, options)!,
            "set" => JsonSerializer.Deserialize<SetRow>(doc.RootElement, options)!,
            _ => throw new JsonException("Unknown review discriminator."),
        };
    }

    public override void Write(Utf8JsonWriter writer, ReviewRow value, JsonSerializerOptions options)
    {
        switch (value)
        {
            case WordRow item: JsonSerializer.Serialize(writer, item, options); break;
            case TokenRow item: JsonSerializer.Serialize(writer, item, options); break;
            case CellRow item: JsonSerializer.Serialize(writer, item, options); break;
            case DiagnosticRow item: JsonSerializer.Serialize(writer, item, options); break;
            case FindingRow item: JsonSerializer.Serialize(writer, item, options); break;
            case EffectRow item: JsonSerializer.Serialize(writer, item, options); break;
            case EvidenceRow item: JsonSerializer.Serialize(writer, item, options); break;
            case ReadingRow item: JsonSerializer.Serialize(writer, item, options); break;
            case ExpectationRow item: JsonSerializer.Serialize(writer, item, options); break;
            case SourceRow item: JsonSerializer.Serialize(writer, item, options); break;
            case SetRow item: JsonSerializer.Serialize(writer, item, options); break;
            default: throw new JsonException("Unknown review variant.");
        }
    }
}

/// <summary>Reads only the declared EvidencePin variants.</summary>
public sealed class EvidencePinConverter : JsonConverter<EvidencePin>
{
    public override EvidencePin Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("kind", out var kind) ||
            kind.ValueKind != JsonValueKind.String)
            throw new JsonException("Missing review discriminator.");
        return kind.GetString() switch
        {
            "set" => JsonSerializer.Deserialize<SetEvidencePin>(doc.RootElement, options)!,
            "scope" => JsonSerializer.Deserialize<ScopeEvidencePin>(doc.RootElement, options)!,
            "checkpoint" => JsonSerializer.Deserialize<CheckpointEvidencePin>(doc.RootElement, options)!,
            "expectation" => JsonSerializer.Deserialize<ExpectationEvidencePin>(doc.RootElement, options)!,
            "check" => JsonSerializer.Deserialize<CheckEvidencePin>(doc.RootElement, options)!,
            "parsimony" => JsonSerializer.Deserialize<ParsimonyEvidencePin>(doc.RootElement, options)!,
            _ => throw new JsonException("Unknown review discriminator."),
        };
    }

    public override void Write(Utf8JsonWriter writer, EvidencePin value, JsonSerializerOptions options)
    {
        switch (value)
        {
            case SetEvidencePin item: JsonSerializer.Serialize(writer, item, options); break;
            case ScopeEvidencePin item: JsonSerializer.Serialize(writer, item, options); break;
            case CheckpointEvidencePin item: JsonSerializer.Serialize(writer, item, options); break;
            case ExpectationEvidencePin item: JsonSerializer.Serialize(writer, item, options); break;
            case CheckEvidencePin item: JsonSerializer.Serialize(writer, item, options); break;
            case ParsimonyEvidencePin item: JsonSerializer.Serialize(writer, item, options); break;
            default: throw new JsonException("Unknown review variant.");
        }
    }
}

internal sealed class ReviewBaselineTokenConverter : JsonConverter<BaselineToken>
{
    public ReviewBaselineTokenConverter() { }
    public override BaselineToken Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        string[] fields = ["projectIdentity", "semanticSnapshotDigest", "projectionVersion", "capturedUtc",
            "bundleDigest", "capturedHostSessionId", "capturedEditGeneration"];
        if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Any(item => !fields.Contains(item.Name)) ||
            fields.Any(field => !root.TryGetProperty(field, out _)))
            throw new JsonException("Invalid Baseline token fields.");
        try
        {
            var generation = root.GetProperty("capturedEditGeneration");
            long? value = generation.ValueKind == JsonValueKind.Null ? null : generation.GetInt64();
            if (value is < 0 or > 9007199254740991L) throw new JsonException("Invalid Baseline generation.");
            return new BaselineToken(root.GetProperty("projectIdentity").GetString()!,
                root.GetProperty("semanticSnapshotDigest").GetString()!, root.GetProperty("projectionVersion").GetString()!,
                root.GetProperty("capturedUtc").GetString()!, root.GetProperty("bundleDigest").GetString()!,
                root.GetProperty("capturedHostSessionId").GetString(), value);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or FormatException)
        {
            throw new JsonException("Invalid Baseline token.", error);
        }
    }

    public override void Write(Utf8JsonWriter writer, BaselineToken value, JsonSerializerOptions options)
    {
        if (value.CapturedEditGeneration is < 0 or > 9007199254740991L)
            throw new JsonException("Invalid Baseline generation.");
        writer.WriteStartObject();
        writer.WriteString("projectIdentity", value.ProjectIdentity);
        writer.WriteString("semanticSnapshotDigest", value.SemanticSnapshotDigest);
        writer.WriteString("projectionVersion", value.ProjectionVersion);
        writer.WriteString("capturedUtc", value.CapturedUtc);
        writer.WriteString("bundleDigest", value.BundleDigest);
        writer.WriteString("capturedHostSessionId", value.CapturedHostSessionId);
        if (value.CapturedEditGeneration is long generation) writer.WriteNumber("capturedEditGeneration", generation);
        else writer.WriteNull("capturedEditGeneration");
        writer.WriteEndObject();
    }
}
