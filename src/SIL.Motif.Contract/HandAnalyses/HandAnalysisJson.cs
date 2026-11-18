using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Json.Schema;
using SIL.Motif.Contract.Ids;

namespace SIL.Motif.Contract.HandAnalyses;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record HandAnalysisValidationError(string Pointer, string Message);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record HandAnalysisParseResult(HandAnalysisInput? Input, IReadOnlyList<HandAnalysisValidationError> Errors);

/// <summary>Validates the closed wire schema before constructing typed intent. Errors use RFC 6901 pointers.</summary>
public static class HandAnalysisJson
{
    private static readonly Lazy<JsonSchema> Schema = new(() =>
    {
        using var stream = typeof(HandAnalysisJson).Assembly.GetManifestResourceStream(
            "SIL.Motif.Contract.Schemas.hand-analysis.schema.json")!;
        return JsonSchema.FromText(new StreamReader(stream).ReadToEnd(),
            new BuildOptions { SchemaRegistry = new SchemaRegistry() });
    });

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        AllowOutOfOrderMetadataProperties = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new PortableGuidConverter() }
    };

    public static HandAnalysisParseResult Parse(string json)
    {
        var errors = new List<HandAnalysisValidationError>();
        try
        {
            using var document = JsonDocument.Parse(json);
            DuplicateMembers(document.RootElement, "", errors);
            var evaluation = Schema.Value.Evaluate(document.RootElement,
                new EvaluationOptions { OutputFormat = OutputFormat.Hierarchical });
            Collect(evaluation, errors);
            if (errors.Count != 0) return new(null, errors.Distinct().ToArray());
            var input = JsonSerializer.Deserialize<HandAnalysisInput>(json, Options)!;
            HandAnalysisLocalReferences.Validate(input, errors);
            return errors.Count == 0 ? new(input, []) : new(null, errors);
        }
        catch (JsonException exception)
        {
            return new(null, [new("", exception.Message)]);
        }
    }

    /// <summary>Uses the input's polymorphic branches and portable UUID spelling when producing editable JSON.</summary>
    public static string Serialize(HandAnalysisInput input)
    {
        var node = JsonSerializer.SerializeToNode(input, Options)!;
        if (input.Occurrence is HandAnalysisTextAnchor)
            node["expected"]!["assignmentId"] = input.Expected.AssignmentId is { } id
                ? System.Text.Json.Nodes.JsonValue.Create(id) : null;
        return node.ToJsonString();
    }

    public static bool TryId(string? value, out Guid id)
    {
        if (Guid.TryParseExact(value, "D", out id)) return true;
        if (!CanonicalId.TryParse(value, out var canonical)) return false;
        id = canonical.ToGuid();
        return true;
    }

    private static void Collect(EvaluationResults result, List<HandAnalysisValidationError> errors)
    {
        if (result.IsValid) return;
        if (result.Errors is not null)
            foreach (var error in result.Errors.Values)
                errors.Add(new(result.InstanceLocation.ToString(), error));
        if (result.Details is not null)
            foreach (var child in result.Details) Collect(child, errors);
    }

    private static void DuplicateMembers(JsonElement value, string pointer, List<HandAnalysisValidationError> errors)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                var at = pointer + "/" + Escape(property.Name);
                if (!names.Add(property.Name)) errors.Add(new(at, "Duplicate property."));
                DuplicateMembers(property.Value, at, errors);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in value.EnumerateArray()) DuplicateMembers(item, pointer + "/" + index++, errors);
        }
    }

    public static string Escape(string name) => name.Replace("~", "~0", StringComparison.Ordinal)
        .Replace("/", "~1", StringComparison.Ordinal);

    private sealed class PortableGuidConverter : JsonConverter<Guid>
    {
        public override Guid Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) =>
            TryId(reader.GetString(), out var id) ? id : throw new JsonException("Expected a portable object id.");
        public override void Write(Utf8JsonWriter writer, Guid value, JsonSerializerOptions options) => writer.WriteStringValue(value);
    }
}
