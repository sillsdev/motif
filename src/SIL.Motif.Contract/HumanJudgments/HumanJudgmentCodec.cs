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
/// Pure value codec for a Notebook judgment: readable sentence, hash-sealed data and visible ID.
/// Does not load, install or write a project. Only the reason after the sentence is meant to be edited.
/// </summary>
public static class HumanJudgmentCodec
{
    private const string HashProperty = "contentHash";
    private const string CrockfordAlphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
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

    /// <summary>
    /// Formats the three stored parts of one judgment: the readable sentence, the hash-sealed data and the visible
    /// ID. The data never contains the reason, which lives in the record's Description paragraphs after the sentence.
    /// </summary>
    public static FormattedJudgment Format(HumanJudgment value)
    {
        value = Normalize(value);
        var node = JsonSerializer.SerializeToNode(value, Options)!.AsObject();
        node.Remove("reason");
        var payload = CanonicalJson.CanonicalizeToUtf8(node.ToJsonString(Options));
        HumanJudgmentValidation.Require(payload.Length <= MaximumEnvelopeBytes, "Encoded envelope exceeds 64 KiB.");
        node[HashProperty] = ContentHashOf(node);
        var data = CanonicalJson.Canonicalize(node.ToJsonString(Options));
        return new FormattedJudgment(Statement(value), data, VisibleId(value.JudgmentId), value.Reason);
    }

    /// <summary>
    /// Returns the typeable ID shown to people: <c>MJ-</c> and eight Crockford base32 characters derived from the
    /// logical judgment id, so every revision of one judgment shares it and it can be checked against the data.
    /// </summary>
    public static string VisibleId(string judgmentId)
    {
        var digest = SHA256.HashData(StrictUtf8.GetBytes(HumanJudgmentValidation.Identity(judgmentId)));
        var text = new StringBuilder(8);
        ulong bits = 0;
        for (var index = 0; index < 5; index++) bits = (bits << 8) | digest[index];
        for (var shift = 35; shift >= 0; shift -= 5) text.Append(CrockfordAlphabet[(int)((bits >> shift) & 31)]);
        return "MJ-" + text.ToString(0, 4) + "-" + text.ToString(4, 4);
    }

    /// <summary>Reduces an ID a person typed or edited to its comparable form: upper case, no hyphen or space.</summary>
    public static string NormalizeVisibleId(string? text) =>
        (text ?? "").Normalize(NormalizationForm.FormC).Replace("-", "").Replace(" ", "").Trim().ToUpperInvariant();

    /// <summary>The paragraphs a reason occupies after the sentence; none when there is no reason.</summary>
    public static IReadOnlyList<string> ReasonParagraphs(string? reason) =>
        string.IsNullOrEmpty(reason)
            ? Array.Empty<string>()
            : reason.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

    /// <summary>
    /// Reads the hash-sealed data of one record. Throws <see cref="HumanJudgmentFormatException"/> with the failing
    /// check: damaged data, a failed hash, or an identity that is not this record's or this project's.
    /// </summary>
    public static HumanJudgment ParseData(string data, string projectId, string recordId)
    {
        JsonObject node;
        try
        {
            HumanJudgmentValidation.Require(data.Length <= 100000, "Judgment data exceeds its bound.");
            data = data.Normalize(NormalizationForm.FormD);
            node = JsonNode.Parse(data) as JsonObject
                ?? throw new FormatException("The data is not a JSON object.");
        }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException
            or FormatException)
        {
            throw new HumanJudgmentFormatException(HumanJudgmentFailure.Damaged,
                "The data field on this record is not valid Motif data.", error);
        }

        var stored = node[HashProperty]?.GetValue<string>();
        var sealedNode = (JsonObject)node.DeepClone();
        sealedNode.Remove(HashProperty);
        string expected;
        try
        {
            expected = ContentHashOf(sealedNode);
            sealedNode[HashProperty] = expected;
        }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException)
        {
            throw new HumanJudgmentFormatException(HumanJudgmentFailure.Damaged,
                "The data field on this record is not valid Motif data.", error);
        }
        if (stored != expected || CanonicalJson.Canonicalize(sealedNode.ToJsonString(Options)) != data)
            throw new HumanJudgmentFormatException(HumanJudgmentFailure.HashFailed,
                "hash failed on this record entry");

        sealedNode.Remove(HashProperty);
        if (sealedNode.ContainsKey("reason"))
            throw new HumanJudgmentFormatException(HumanJudgmentFailure.Damaged,
                "The data field on this record carries a reason; the reason belongs in the Description.");
        try
        {
            return ParseJson(sealedNode.ToJsonString(Options), projectId, recordId);
        }
        catch (FormatException error)
        {
            var identity = error.Message.Contains("belongs to another project", StringComparison.Ordinal) ||
                error.Message.Contains("Revision identity", StringComparison.Ordinal);
            throw new HumanJudgmentFormatException(
                identity ? HumanJudgmentFailure.Identity : HumanJudgmentFailure.Damaged, error.Message, error);
        }
    }

    /// <summary>
    /// Reads one record: the data decides the judgment, so a failure there is thrown. A changed ID field or
    /// sentence only adds a warning, and the reason is whatever follows the sentence paragraph.
    /// </summary>
    public static HumanJudgmentReading Read(
        string data, string? visibleIdField, IReadOnlyList<string> paragraphs, string projectId, string recordId)
    {
        var value = ParseData(data, projectId, recordId);
        var warnings = new List<HumanJudgmentWarning>();
        if (NormalizeVisibleId(visibleIdField) != NormalizeVisibleId(VisibleId(value.JudgmentId)))
            warnings.Add(HumanJudgmentWarning.For(HumanJudgmentWarningKind.IdMismatch));
        var statement = Statement(value);
        if (paragraphs.Count == 0 ||
            !string.Equals(paragraphs[0].Normalize(NormalizationForm.FormD), statement, StringComparison.Ordinal))
            warnings.Add(HumanJudgmentWarning.For(HumanJudgmentWarningKind.StatementEdited));
        var reason = paragraphs.Count > 1 ? string.Join("\n", paragraphs.Skip(1)) : null;
        return new HumanJudgmentReading(Normalize(value with { Reason = reason }), warnings);
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

    private static string ContentHashOf(JsonObject withoutHash)
    {
        var copy = (JsonObject)withoutHash.DeepClone();
        copy.Remove(HashProperty);
        return Hash(CanonicalJson.Canonicalize(copy.ToJsonString(Options)));
    }

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

/// <summary>The three stored parts of one judgment, plus the reason that belongs after the sentence.</summary>
/// <param name="Statement">The generated sentence, the first Description paragraph.</param>
/// <param name="Data">Canonical JSON including its <c>contentHash</c>, the data field's whole value.</param>
/// <param name="VisibleId">The typeable ID, the ID field's whole value.</param>
/// <param name="Reason">The optional reason, written as the paragraphs after the sentence.</param>
public sealed record FormattedJudgment(string Statement, string Data, string VisibleId, string? Reason);

/// <summary>Which check of a record's data failed.</summary>
public enum HumanJudgmentFailure
{
    /// <summary>The data is not a closed, valid Motif value.</summary>
    Damaged,

    /// <summary>The data parses but its content hash does not match.</summary>
    HashFailed,

    /// <summary>The data names another project or another record.</summary>
    Identity,
}

/// <summary>A record's data could not be used; <see cref="Failure"/> names the check.</summary>
public sealed class HumanJudgmentFormatException : FormatException
{
    /// <summary>Creates the failure for one check.</summary>
    public HumanJudgmentFormatException(HumanJudgmentFailure failure, string message, Exception? inner = null)
        : base(message, inner) => Failure = failure;

    /// <summary>The failing check.</summary>
    public HumanJudgmentFailure Failure { get; }
}

/// <summary>A warning that leaves the judgment in force.</summary>
public enum HumanJudgmentWarningKind
{
    /// <summary>The ID field differs from the ID derived from the data.</summary>
    IdMismatch,

    /// <summary>The sentence paragraph differs from the sentence the data generates.</summary>
    StatementEdited,
}

/// <summary>One warning and its words for the person.</summary>
public sealed record HumanJudgmentWarning(HumanJudgmentWarningKind Kind, string Message)
{
    /// <summary>The warning for one kind, in the words shown to the person.</summary>
    public static HumanJudgmentWarning For(HumanJudgmentWarningKind kind) => kind switch
    {
        HumanJudgmentWarningKind.IdMismatch => new(kind, "The judgment ID on this record does not match its data"),
        HumanJudgmentWarningKind.StatementEdited =>
            new(kind, "The sentence on this record was edited; Motif uses its data"),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}

/// <summary>A judgment read from one record together with the warnings that leave it in force.</summary>
public sealed record HumanJudgmentReading(HumanJudgment Judgment, IReadOnlyList<HumanJudgmentWarning> Warnings);
