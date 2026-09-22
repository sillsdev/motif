using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace SIL.Motif.Host.PanGloss;

public sealed record PanGlossTraceAnalysis(
    string? AnalysisId,
    int? Index,
    string? LegacyMorphemes,
    string? Surface,
    IReadOnlyList<PanGlossTraceMorph> Morphs,
    string Availability,
    JsonElement Raw)
{
    public string? ProjectionStatus { get; init; }
    public string? ProjectionError { get; init; }
}

public sealed record PanGlossTraceMorph(
    string? Identity,
    string? Form,
    string? Headword,
    string? Gloss,
    string? Category,
    string? Slot,
    string? InflectionClass,
    string? Features,
    string? GuessedString,
    string? FieldWorksLink,
    JsonElement Raw)
{
    public string? FormId { get; init; }
    public string? EntryId { get; init; }
    public string? MsaId { get; init; }
    public string? InflTypeId { get; init; }
    public string? IdentityQuality { get; init; }
    public string? FormWritingSystem { get; init; }
    public string? HeadwordWritingSystem { get; init; }
    public string? GlossWritingSystem { get; init; }
    public string? FormSourceId { get; init; }
    public string? HeadwordSourceId { get; init; }
    public string? GlossSourceId { get; init; }
    public string? CategoryId { get; init; }
    public string? CategoryName { get; init; }
    public string? CategoryAbbreviation { get; init; }
    public string? SlotId { get; init; }
    public bool? SlotOptional { get; init; }
    public string? InflectionClassId { get; init; }
    public string? InflectionClassName { get; init; }
    public string? InflectionClassAbbreviation { get; init; }
    public string? FeaturesSource { get; init; }
    public string? FeaturesStatus { get; init; }
}

public sealed record PanGlossTraceAttempt(
    string? AttemptId,
    bool? Succeeded,
    string? Status,
    string? EventType,
    string? FailureReason,
    string? FailureContext,
    string? FailureRequired,
    string? FailureActual,
    string? FailureEnvironment,
    string? SourceIdentityKind,
    string? SourceIdentityId,
    string? SourceIdentityQuality,
    IReadOnlyList<PanGlossTraceMorph> Morphs,
    JsonElement Raw);

public sealed record PanGlossTraceDiagnosticDocument(
    string SchemaVersion,
    string Word,
    string Signature,
    PanGlossTraceNode? Root,
    PanGlossTraceDetails Details,
    IReadOnlyList<PanGlossTraceAnalysis> Analyses,
    IReadOnlyList<PanGlossTraceAttempt> Attempts,
    string RawJson)
{
    public bool IsV2 => string.Equals(SchemaVersion, PanGlossTraceDiagnosticReader.SchemaV2, StringComparison.Ordinal);
}

public sealed class PanGlossTraceDiagnosticFormatException : FormatException
{
    public PanGlossTraceDiagnosticFormatException(string message) : base(message) { }
    public PanGlossTraceDiagnosticFormatException(string message, Exception innerException) : base(message, innerException) { }
}

public static class PanGlossTraceDiagnosticReader
{
    public const string SchemaV1 = "pangloss.trace-details.v1";
    public const string SchemaV2 = "pangloss.trace-details.v2";

    public static PanGlossTraceDiagnosticDocument Read(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new PanGlossTraceDiagnosticFormatException("The diagnostic document is empty.");

        try
        {
            using var parsed = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 512 });
            return ReadDocument(parsed.RootElement, json);
        }
        catch (PanGlossTraceDiagnosticFormatException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw new PanGlossTraceDiagnosticFormatException(
                "The diagnostic document is malformed JSON: " + exception.Message, exception);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or ArgumentException or FormatException or OverflowException)
        {
            throw new PanGlossTraceDiagnosticFormatException(
                "The diagnostic document contains an invalid field: " + exception.Message, exception);
        }
    }

    private static PanGlossTraceDiagnosticDocument ReadDocument(JsonElement envelope, string rawJson)
    {
        if (envelope.ValueKind != JsonValueKind.Object)
            throw new PanGlossTraceDiagnosticFormatException("The diagnostic document must be a JSON object.");

        var schema = RequiredString(envelope, "schemaVersion");
        if (schema is not SchemaV1 and not SchemaV2)
            throw new PanGlossTraceDiagnosticFormatException(
                $"Unsupported diagnostic schema '{schema}'. Motif supports {SchemaV1} and {SchemaV2}.");

        var search = RequiredObject(envelope, "search");
        var result = RequiredObject(envelope, "result");
        var categories = RequiredObject(envelope, "categories");
        if (!envelope.TryGetProperty("trace", out var trace))
            throw new PanGlossTraceDiagnosticFormatException("The diagnostic document is missing the \"trace\" field.");

        var root = trace.ValueKind == JsonValueKind.Null ? null : ReadNode(trace);
        var details = new PanGlossTraceDetails(
            RequiredBool(search, "capped"),
            RequiredBool(search, "timedOut"),
            RequiredBool(search, "invalidShape"),
            RequiredLong(search, "steps"),
            RequiredLong(search, "elapsedNs"),
            RequiredBool(result, "guessed"),
            categories.EnumerateObject().Select(ReadCategory).ToArray())
        {
            ReportedCompleted = RequiredBool(search, "completed"),
        };

        var analyses = ReadAnalyses(result);
        var attempts = root is null ? [] : ReadAttempts(root);
        return new PanGlossTraceDiagnosticDocument(
            schema,
            RequiredString(envelope, "word"),
            RequiredString(result, "signature"),
            root,
            details,
            analyses,
            attempts,
            rawJson);
    }

    private static IReadOnlyList<PanGlossTraceAnalysis> ReadAnalyses(JsonElement result)
    {
        if (!result.TryGetProperty("analyses", out var analyses))
            return [];
        if (analyses.ValueKind != JsonValueKind.Array)
            throw new PanGlossTraceDiagnosticFormatException("\"result.analyses\" must be a JSON array.");
        return analyses.EnumerateArray().Select(ReadAnalysis).ToArray();
    }

    private static PanGlossTraceAnalysis ReadAnalysis(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
            throw new PanGlossTraceDiagnosticFormatException("Each recorded analysis must be a JSON object.");

        var morphs = ReadMorphs(value, "morphs");
        var projection = value.TryGetProperty("projection", out var projectionElement) &&
            projectionElement.ValueKind == JsonValueKind.Object;
        var projectionStatus = projection ? OptionalString(projectionElement, "status") : null;
        var projectionError = projection ? Text(projectionElement, "error") : null;
        return new PanGlossTraceAnalysis(
            OptionalString(value, "analysisId") ?? OptionalString(value, "id"),
            OptionalInt(value, "index"),
            OptionalString(value, "morphemes"),
            Text(value, "surface"),
            morphs,
            projectionStatus is not null ? (projectionStatus is "available" or "recorded" ? "recorded" : "unavailable") : (morphs.Count > 0 ? "recorded" : "legacy"),
            value.Clone())
        {
            ProjectionStatus = projectionStatus,
            ProjectionError = projectionError,
        };
    }

    private static IReadOnlyList<PanGlossTraceAttempt> ReadAttempts(PanGlossTraceNode root)
    {
        var attempts = new List<PanGlossTraceAttempt>();
        Walk(root);
        return attempts;

        void Walk(PanGlossTraceNode node)
        {
            if (node.Type is "Successful" or "Failed" || IsTerminalOutcome(node.OutcomeStatus))
            {
                attempts.Add(new PanGlossTraceAttempt(
                    null,
                    node.OutcomeStatus is "successful" or "succeeded" or "success" || node.Type == "Successful"
                        ? true
                        : node.OutcomeStatus is "failed" or "failure" || node.Type == "Failed" ? false : null,
                    node.OutcomeStatus,
                    node.OutcomeEventType,
                    node.FailureReason,
                    node.FailureContext,
                    node.FailureRequired,
                    node.FailureActual,
                    node.FailureEnvironment,
                    node.SourceIdentityKind,
                    node.SourceIdentityId,
                    node.SourceIdentityQuality,
                    node.AttemptedMorphs,
                    default));
            }

            foreach (var child in node.Children) Walk(child);
        }
    }

    private static IReadOnlyList<PanGlossTraceMorph> ReadMorphs(JsonElement owner, string name)
    {
        if (!owner.TryGetProperty(name, out var element))
            return [];
        if (element.ValueKind != JsonValueKind.Array)
            throw new PanGlossTraceDiagnosticFormatException($"\"{name}\" must be a JSON array.");
        return element.EnumerateArray().Select(ReadMorph).ToArray();
    }

    private static PanGlossTraceMorph ReadMorph(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
            throw new PanGlossTraceDiagnosticFormatException("Each recorded morph must be a JSON object.");

        var identityElement = value.TryGetProperty("identity", out var identity) &&
            identity.ValueKind == JsonValueKind.Object ? identity : default;
        var msaElement = value.TryGetProperty("msa", out var msa) &&
            msa.ValueKind == JsonValueKind.Object ? msa : default;
        var categoryElement = msaElement.ValueKind == JsonValueKind.Object &&
            msaElement.TryGetProperty("category", out var category) &&
            category.ValueKind == JsonValueKind.Object ? category : default;
        var slotElement = value.TryGetProperty("slot", out var slot) &&
            slot.ValueKind == JsonValueKind.Object ? slot : default;
        var inflectionElement = value.TryGetProperty("inflectionClass", out var inflection) &&
            inflection.ValueKind == JsonValueKind.Object ? inflection : default;
        var featuresElement = value.TryGetProperty("features", out var features) &&
            features.ValueKind == JsonValueKind.Object ? features : default;

        var formId = OptionalString(identityElement, "formId");
        var entryId = OptionalString(identityElement, "entryId");
        var msaId = OptionalString(identityElement, "msaId");
        var inflTypeId = OptionalString(identityElement, "inflTypeId");
        var featureText = Text(featuresElement, "value") ?? Text(featuresElement, "text") ??
            (featuresElement.ValueKind == JsonValueKind.Object ? featuresElement.GetRawText() : Text(value, "features"));
        return new PanGlossTraceMorph(
            formId ?? entryId ?? OptionalString(identityElement, "id") ?? OptionalString(value, "id"),
            Text(value, "form"),
            Text(value, "headword"),
            Text(value, "gloss"),
            Text(categoryElement, "name") ?? Text(categoryElement, "abbreviation") ?? Text(value, "category") ??
                Text(msaElement, "category"),
            Text(slotElement, "name") ?? Text(value, "slot"),
            Text(inflectionElement, "name") ?? Text(inflectionElement, "abbreviation") ??
                Text(value, "inflectionClass"),
            featureText,
            OptionalString(value, "guessedString"),
            OptionalString(value, "fieldWorksLink"),
            value.Clone())
        {
            FormId = formId,
            EntryId = entryId,
            MsaId = msaId,
            InflTypeId = inflTypeId,
            IdentityQuality = OptionalString(identityElement, "quality"),
            FormWritingSystem = OptionalStringObject(value, "form", "writingSystem"),
            HeadwordWritingSystem = OptionalStringObject(value, "headword", "writingSystem"),
            GlossWritingSystem = OptionalStringObject(value, "gloss", "writingSystem"),
            FormSourceId = OptionalStringObject(value, "form", "sourceId"),
            HeadwordSourceId = OptionalStringObject(value, "headword", "sourceId"),
            GlossSourceId = OptionalStringObject(value, "gloss", "sourceId"),
            CategoryId = OptionalString(categoryElement, "id"),
            CategoryName = Text(categoryElement, "name"),
            CategoryAbbreviation = Text(categoryElement, "abbreviation"),
            SlotId = OptionalString(slotElement, "id"),
            SlotOptional = OptionalBool(slotElement, "optional"),
            InflectionClassId = OptionalString(inflectionElement, "id"),
            InflectionClassName = Text(inflectionElement, "name"),
            InflectionClassAbbreviation = Text(inflectionElement, "abbreviation"),
            FeaturesSource = Text(featuresElement, "source"),
            FeaturesStatus = OptionalString(featuresElement, "status"),
        };
    }

    private static PanGlossTraceNode ReadNode(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new PanGlossTraceDiagnosticFormatException("A trace node must be a JSON object.");
        var children = Required(element, "children", JsonValueKind.Array).EnumerateArray().Select(ReadNode).ToArray();
        var node = new PanGlossTraceNode(
            RequiredString(element, "type"),
            OptionalString(element, "source"),
            OptionalInt(element, "subrule"),
            OptionalString(element, "failureReason"),
            Text(element, "outputShape"),
            Text(element, "inputShape"),
            children);

        var outcome = element.TryGetProperty("outcome", out var outcomeElement) &&
            outcomeElement.ValueKind == JsonValueKind.Object ? outcomeElement : default;
        node = node with
        {
            OutcomeStatus = OptionalString(outcome, "status"),
            OutcomeEventType = OptionalString(outcome, "eventType"),
            FailureContext = ReadContext(element),
            FailureRequired = ReadContextField(element, "required"),
            FailureActual = ReadContextField(element, "actual"),
            FailureEnvironment = ReadContextField(element, "environment"),
            SourceIdentityKind = ReadIdentityField(element, "kind"),
            SourceIdentityId = ReadIdentityField(element, "id"),
            SourceIdentityQuality = ReadIdentityField(element, "quality"),
            AttemptedMorphs = ReadMorphs(element, "attemptedMorphs"),
        };
        return node;
    }
    private static bool IsTerminalOutcome(string? status) => status is "successful" or "succeeded" or "success" or "failed" or "failure" or "blocked";

    private static string? ReadContext(JsonElement owner)
    {
        if (!owner.TryGetProperty("failureContext", out var context) ||
            context.ValueKind != JsonValueKind.Object) return null;
        return OptionalString(context, "reason") ?? Text(context, "status");
    }



    private static string? ReadContextField(JsonElement owner, string field)
    {
        if (!owner.TryGetProperty("failureContext", out var context) ||
            context.ValueKind != JsonValueKind.Object) return null;
        return Text(context, field);
    }

    private static string? ReadIdentityField(JsonElement owner, string field)
    {
        if (!owner.TryGetProperty("sourceIdentity", out var identity) ||
            identity.ValueKind != JsonValueKind.Object) return null;
        return OptionalString(identity, field);
    }

    private static string? Text(JsonElement owner, string name)
    {
        if (owner.ValueKind != JsonValueKind.Object || !owner.TryGetProperty(name, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind == JsonValueKind.String) return value.GetString();
        if (value.ValueKind != JsonValueKind.Object) return value.GetRawText();
        return OptionalString(value, "text") ?? OptionalString(value, "name") ?? OptionalString(value, "abbreviation") ??
            OptionalString(value, "status") ?? OptionalString(value, "source") ?? value.GetRawText();
    }

    private static PanGlossTraceCategory ReadCategory(JsonProperty property)
    {
        var value = property.Value;
        if (value.ValueKind != JsonValueKind.Object)
            throw new PanGlossTraceDiagnosticFormatException($"Category '{property.Name}' must be a JSON object.");
        var timed = RequiredBool(value, "timingAvailable");
        var self = Required(value, "selfElapsedNs", timed ? JsonValueKind.Number : JsonValueKind.Null);
        return new PanGlossTraceCategory(
            property.Name,
            RequiredLong(value, "attempts"),
            RequiredLong(value, "work"),
            RequiredLong(value, "outputs"),
            RequiredLong(value, "notApplied"),
            RequiredLong(value, "noRoot"),
            RequiredLong(value, "surfaceMismatch"),
            RequiredLong(value, "uses"),
            timed ? self.GetInt64() : null);
    }

    private static JsonElement Required(JsonElement owner, string name, JsonValueKind kind) =>
        owner.TryGetProperty(name, out var value) && value.ValueKind == kind
            ? value
            : throw new PanGlossTraceDiagnosticFormatException($"\"{name}\" must be a JSON {kind}.");

    private static JsonElement RequiredObject(JsonElement owner, string name) => Required(owner, name, JsonValueKind.Object);
    private static string RequiredString(JsonElement owner, string name) => Required(owner, name, JsonValueKind.String).GetString()!;
    private static long RequiredLong(JsonElement owner, string name) => Required(owner, name, JsonValueKind.Number).GetInt64();

    private static bool RequiredBool(JsonElement owner, string name) =>
        owner.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : throw new PanGlossTraceDiagnosticFormatException($"\"{name}\" must be a JSON boolean.");

    private static string? OptionalString(JsonElement owner, string name) =>
        owner.ValueKind == JsonValueKind.Object && owner.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int? OptionalInt(JsonElement owner, string name) =>
        owner.ValueKind == JsonValueKind.Object && owner.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.Number ? value.GetInt32() : null;

    private static bool? OptionalBool(JsonElement owner, string name) =>
        owner.ValueKind == JsonValueKind.Object && owner.TryGetProperty(name, out var value) &&
        value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : null;

    private static string? OptionalStringObject(JsonElement owner, string property, string name)
    {
        if (owner.ValueKind != JsonValueKind.Object || !owner.TryGetProperty(property, out var value) ||
            value.ValueKind != JsonValueKind.Object) return null;
        return OptionalString(value, name);
    }
}
