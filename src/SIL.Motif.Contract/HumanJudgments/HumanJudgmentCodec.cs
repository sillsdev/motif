using System;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using SIL.Motif.Contract.Parsimony;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using SIL.Motif.Contract.Canonicalization;
using SIL.Motif.Contract.Ids;

namespace SIL.Motif.Contract.HumanJudgments;

/// <summary>
/// Pure value codec for the reserved Notebook String. Does not load, install or write a project.
/// The reader validates the readable sentence against the marker; only the quoted reason is editable.
/// </summary>
public static class HumanJudgmentCodec
{
    private const string Marker = " [motif-human-judgment:v1:";
    private const string ReasonDelimiter = " Reason: ";
    private const int MaximumEnvelopeBytes = 65536;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly JsonSerializerOptions Options = CreateOptions();
    private static readonly IReadOnlyDictionary<string, string> ReadableFormat = LoadReadableFormat();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            RespectRequiredConstructorParameters = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            AllowOutOfOrderMetadataProperties = true,
            MaxDepth = 32
        };
        options.Converters.Add(new ClosedJudgmentEnumConverter<ParsimonyDispositionKind>());
        options.Converters.Add(new ClosedJudgmentEnumConverter<JudgmentActorKind>());
        options.Converters.Add(new ClosedJudgmentEnumConverter<JudgmentEdgeRole>());
        options.Converters.Add(new ClosedJudgmentEnumConverter<JudgmentGroupRole>());
        return options;
    }

    /// <summary>Parses a closed logical JSON value; context identities bind it to its containing project/record.</summary>
    public static HumanJudgment ParseJson(string json, string projectId, string recordId)
    {
        try
        {
            HumanJudgmentValidation.Require(StrictUtf8.GetByteCount(json) <= MaximumEnvelopeBytes + 8192,
                "Judgment envelope exceeds its UTF-8 bound.");
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
            RejectDuplicates(document.RootElement);
            HumanJudgmentValidation.Require(document.RootElement.TryGetProperty("format", out _) &&
                document.RootElement.TryGetProperty("version", out _), "Missing format/version.");
            var value = JsonSerializer.Deserialize<HumanJudgment>(json, Options)
                ?? throw new FormatException("Null judgment.");
            HumanJudgmentValidation.Validate(value);
            HumanJudgmentValidation.Require(HumanJudgmentValidation.SameId(value.ProjectId, projectId),
                "Judgment belongs to another project.");
            HumanJudgmentValidation.Require(HumanJudgmentValidation.SameId(value.RevisionId, recordId),
                "Revision identity must equal its Notebook record identity.");
            return Normalize(value);
        }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException)
        {
            throw new FormatException("Invalid closed human-judgment value: " + error.Message, error);
        }
    }

    /// <summary>Emits canonical logical JSON, including the parsed reason, for portable contracts and digests.</summary>
    public static string ToJson(HumanJudgment value) =>
        CanonicalJson.Canonicalize(JsonSerializer.Serialize(Normalize(value), Options));

    /// <summary>Formats a single readable value; the encoded envelope never contains a second copy of reason.</summary>
    public static string Format(HumanJudgment value)
    {
        value = Normalize(value);
        var node = JsonSerializer.SerializeToNode(value, Options)!.AsObject();
        node.Remove("reason");
        var payload = CanonicalJson.CanonicalizeToUtf8(node.ToJsonString(Options));
        HumanJudgmentValidation.Require(payload.Length <= MaximumEnvelopeBytes, "Encoded envelope exceeds 64 KiB.");
        var reason = value.Reason is null ? "" : ReasonDelimiter + JsonSerializer.Serialize(value.Reason, Options);
        return Statement(value) + reason + Marker + Base64Url.Encode(payload) + "]";
    }

    /// <summary>
    /// Reads one final strict marker and its matching sentence. Malformed input throws FormatException;
    /// callers must retain the original text as unavailable input rather than suppressing a finding.
    /// </summary>
    public static HumanJudgment Parse(string physical, string projectId, string recordId)
    {
        try
        {
            HumanJudgmentValidation.Require(physical.Length <= 100000, "Readable judgment exceeds its bound.");
            var start = physical.LastIndexOf(Marker, StringComparison.Ordinal);
            HumanJudgmentValidation.Require(start >= 0 && physical.EndsWith(']'), "Missing or malformed final marker.");
            var encoded = physical.Substring(start + Marker.Length, physical.Length - start - Marker.Length - 1);
            HumanJudgmentValidation.Require(encoded.Length <= (MaximumEnvelopeBytes + 2) / 3 * 4,
                "Encoded envelope exceeds 64 KiB.");
            var bytes = Base64Url.Decode(encoded);
            HumanJudgmentValidation.Require(bytes.Length <= MaximumEnvelopeBytes && Base64Url.Encode(bytes) == encoded,
                "Marker must use strict canonical unpadded base64url.");
            var json = StrictUtf8.GetString(bytes);
            using var document = JsonDocument.Parse(json);
            HumanJudgmentValidation.Require(!document.RootElement.TryGetProperty("reason", out _),
                "Marker cannot carry a second copy of reason.");
            HumanJudgmentValidation.Require(CanonicalJson.Canonicalize(json) == json, "Marker JSON must be RFC 8785 canonical.");
            var value = ParseJson(json, projectId, recordId);
            var statement = Statement(value);
            var readable = physical[..start].Normalize(NormalizationForm.FormD);
            HumanJudgmentValidation.Require(readable.StartsWith(statement, StringComparison.Ordinal),
                "Readable statement disagrees with its marker.");
            var tail = readable[statement.Length..];
            string? reason = null;
            if (tail.Length != 0)
            {
                HumanJudgmentValidation.Require(tail.StartsWith(ReasonDelimiter, StringComparison.Ordinal),
                    "Unexpected text after readable statement.");
                var quoted = tail[ReasonDelimiter.Length..];
                HumanJudgmentValidation.Require(quoted.StartsWith('"') && quoted.EndsWith('"'),
                    "Reason must be exactly one JSON-quoted string.");
                reason = JsonSerializer.Deserialize<string>(quoted, Options);
            }
            return Normalize(value with { Reason = reason });
        }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException)
        {
            throw new FormatException("Invalid readable human judgment: " + error.Message, error);
        }
    }

    /// <summary>Hashes normalized logical content with SHA-256 over RFC 8785, including optional reason.</summary>
    public static string LogicalDigest(HumanJudgment value) => Hash(ToJson(value));

    /// <summary>Returns the exact subject key; group members are a set and directed edge roles remain ordered.</summary>
    public static string SubjectKey(HumanJudgmentSubject subject)
    {
        HumanJudgmentValidation.Subject(subject);
        var node = JsonSerializer.SerializeToNode(subject, Options)!;
        NormalizeNode(node, normalizeIds: true);
        if (subject is GroupJudgmentSubject group)
            node["members"] = JsonSerializer.SerializeToNode(group.Members
                .OrderBy(HumanJudgmentValidation.ObjectKey, StringComparer.Ordinal), Options);
        NormalizeNode(node, normalizeIds: true);
        return Hash(CanonicalJson.Canonicalize(node.ToJsonString(Options)));
    }

    private static HumanJudgment Normalize(HumanJudgment value)
    {
        HumanJudgmentValidation.Validate(value);
        if (value.Body is DispositionJudgment { Subject: GroupJudgmentSubject group } disposition)
            value = value with { Body = disposition with { Subject = group with
            { Members = group.Members.OrderBy(HumanJudgmentValidation.ObjectKey, StringComparer.Ordinal).ToArray() } } };
        value = value with
        {
            Replaces = value.Replaces.OrderBy(h => HumanJudgmentValidation.Identity(h.RevisionId), StringComparer.Ordinal).ToArray(),
            Reason = string.IsNullOrEmpty(value.Reason) ? null : value.Reason
        };
        var node = JsonSerializer.SerializeToNode(value, Options)!;
        NormalizeNode(node, normalizeIds: false);
        var normalized = JsonSerializer.Deserialize<HumanJudgment>(node, Options)!;
        HumanJudgmentValidation.Validate(normalized);
        return normalized;
    }

    private static void NormalizeNode(JsonNode node, bool normalizeIds)
    {
        if (node is JsonObject obj)
        {
            foreach (var pair in obj.ToArray())
            {
                if (pair.Value is JsonValue value && value.TryGetValue<string>(out var text))
                {
                    var isIdentity = pair.Key is "id" or "owningEntryId";
                    obj[pair.Key] = normalizeIds && isIdentity
                        ? HumanJudgmentValidation.Identity(text) : text.Normalize(NormalizationForm.FormD);
                }
                else if (pair.Value is not null) NormalizeNode(pair.Value, normalizeIds);
            }
        }
        else if (node is JsonArray array)
        {
            for (var index = 0; index < array.Count; index++)
            {
                if (array[index] is JsonValue value && value.TryGetValue<string>(out var text))
                    array[index] = text.Normalize(NormalizationForm.FormD);
                else if (array[index] is { } child) NormalizeNode(child, normalizeIds);
            }
        }
    }

    private static IReadOnlyDictionary<string, string> LoadReadableFormat()
    {
        using var stream = typeof(HumanJudgmentCodec).Assembly.GetManifestResourceStream(
            "SIL.Motif.Contract.HumanJudgments.ReadableFormats.en-v1.json")
            ?? throw new InvalidOperationException("Missing en-v1 readable format resource.");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
    }

    private static string Render(string key, params object[] values) =>
        string.Format(CultureInfo.InvariantCulture, ReadableFormat[key], values);

    private static string Statement(HumanJudgment value) =>
        (value.ResolvesConflict ? ReadableFormat["resolution"] : "") + BodyStatement(value);

    private static string BodyStatement(HumanJudgment value) => value.Body switch
    {
        DispositionJudgment disposition => Render("disposition",
            ReadableFormat[disposition.Disposition.ToString().ToLowerInvariant()], disposition.SubjectCaption,
            disposition.MeasureCaption) + (disposition.Question is null ? "" : Render("question", disposition.Question)),
        ReviewedNegativeJudgment { Target: SurfaceNegativeTarget } negative =>
            Render("surface", negative.Form, negative.Context),
        ReviewedNegativeJudgment { Target: ReadingNegativeTarget reading } negative =>
            Render("reading", negative.Form, negative.Context,
                string.Join("; ", reading.Morphs.Select(m => Render("morph", m.FormCaption, m.MsaCaption)))),
        RetractionJudgment => Render("retraction", value.JudgmentId),
        _ => throw new FormatException("Unknown judgment kind.")
    };

    private static string Hash(string json) =>
        "sha256:" + Convert.ToHexStringLower(SHA256.HashData(StrictUtf8.GetBytes(json)));

    private static void RejectDuplicates(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var keys = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                HumanJudgmentValidation.Require(keys.Add(property.Name), "Duplicate JSON property.");
                RejectDuplicates(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var child in element.EnumerateArray()) RejectDuplicates(child);
    }
}
