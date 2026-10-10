using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Retirement;

namespace SIL.Motif.Contract.Responses;

/// <summary>One report kind <c>report --list-kinds</c> names, and what it means.</summary>
public sealed record ReportKindResponse(string Kind, string Description);

/// <summary>The <c>report --list-kinds</c> report: every kind that may be asked for.</summary>
public sealed record ReportKindListResponse(IReadOnlyList<ReportKindResponse> Kinds);

/// <summary>One computed and stored Report, as <c>report</c> prints it.</summary>
public sealed record ReportResponse(string ReportId, string? AssessmentId, string Kind, string Text)
{
    /// <summary>The exact Selection words measured by this Assessment.</summary>
    public IReadOnlyList<string> SelectionWords { get; init; } = [];

    /// <summary>The number of word searches represented by a correctness Report.</summary>
    public int? TotalSearches { get; init; }

    /// <summary>The number of those searches that completed with usable morphology evidence.</summary>
    public int? CompletedSearches { get; init; }
}

/// <summary>The stored Parsimony Report and the exact frozen inputs it names.</summary>
public sealed record ParsimonyReportResponse(
    string ReportId,
    ParsimonyReportInputs Inputs,
    IReadOnlyList<SIL.Motif.Contract.Parsimony.ParsimonyFinding> Findings,
    string Text)
{
    /// <summary>The exact Assessments whose parser cases were captured in the report bundle.</summary>
    public IReadOnlyList<string> AssessmentIds { get; init; } = [];

    /// <summary>Full eligibility counts and availability states for every requested measure.</summary>
    public IReadOnlyList<ParsimonyMeasureRun> MeasureRuns { get; init; } = [];

    /// <summary>Distinct wordform, reading, lexeme, and token denominators from the frozen evidence.</summary>
    public ParsimonyJoinQuality? JoinQuality { get; init; }

    /// <summary>The frozen Active and Suppressed membership derived from this Report's Baseline judgments.</summary>
    public ParsimonyDispositionProjection? DispositionProjection { get; init; }

    /// <summary>Information lines for checks that could not look; never counted as findings. May be empty.</summary>
    public required IReadOnlyList<ParsimonyNote> Notes { get; init; }
}

/// <summary>The newest stored Parsimony Report, named by its id and the Parsimony bundle its inputs were measured in.</summary>
public sealed record ParsimonyLatestReportResponse(string ReportId, string BundleId);

/// <summary>A frozen analysis file's schema identity and byte digest.</summary>
public sealed record ParsimonyArtifactDigest(int SchemaVersion, string Sha256);

/// <summary>The immutable inputs bound to a Parsimony Report.</summary>
[JsonConverter(typeof(ParsimonyReportInputsJsonConverter))]
public sealed record ParsimonyReportInputs
{
    public ParsimonyReportInputs(
        string bundleId,
        BaselineToken baselineToken,
        string inputKind,
        string? candidate,
        string modelFingerprint,
        ParsimonyArtifactDigest grammarFacts,
        ParsimonyArtifactDigest evidence,
        string? selectionSha256,
        string? expectationRevisionSha256,
        IReadOnlyList<string> assessmentIds,
        ParsimonyEvidenceScopeKind evidenceScope = ParsimonyEvidenceScopeKind.DefaultSelection,
        RetirementExpectationTranslation? retirementExpectationTranslation = null)
    {
        BundleId = RequireText(bundleId, nameof(bundleId));
        BaselineToken = baselineToken ?? throw new ArgumentNullException(nameof(baselineToken));
        InputKind = RequireText(inputKind, nameof(inputKind));
        if (InputKind is not ("baseline" or "candidate"))
            throw new ArgumentException("Input kind must be 'baseline' or 'candidate'.", nameof(inputKind));
        Candidate = candidate is null ? null : RequireText(candidate, nameof(candidate));
        if ((InputKind == "candidate") != (Candidate is not null))
            throw new ArgumentException("Candidate identity must be present only for candidate input.", nameof(candidate));
        ModelFingerprint = RequireText(modelFingerprint, nameof(modelFingerprint));
        GrammarFacts = ValidateArtifact(grammarFacts, nameof(grammarFacts));
        Evidence = ValidateArtifact(evidence, nameof(evidence));
        SelectionSha256 = ValidateDigest(selectionSha256, nameof(selectionSha256));
        ExpectationRevisionSha256 = ValidateDigest(expectationRevisionSha256, nameof(expectationRevisionSha256));
        ArgumentNullException.ThrowIfNull(assessmentIds);
        if (assessmentIds.Any(string.IsNullOrWhiteSpace) ||
            assessmentIds.Distinct(StringComparer.Ordinal).Count() != assessmentIds.Count)
            throw new ArgumentException("Assessment references must be nonblank and unique.", nameof(assessmentIds));
        AssessmentIds = Array.AsReadOnly(assessmentIds.ToArray());
        if (!Enum.IsDefined(evidenceScope))
            throw new ArgumentOutOfRangeException(nameof(evidenceScope));
        EvidenceScope = evidenceScope;
        if (retirementExpectationTranslation is not null)
        {
            var normalized = RetirementExpectationTranslationCodec.Parse(
                RetirementExpectationTranslationCodec.ToJson(retirementExpectationTranslation));
            if (normalized.Original.Baseline != BaselineToken)
                throw new ArgumentException("Retirement expectations must name this Report's Baseline.",
                    nameof(retirementExpectationTranslation));
            RetirementExpectationTranslation = normalized;
        }
    }

    public string BundleId { get; }
    public BaselineToken BaselineToken { get; }
    public string InputKind { get; }
    public string? Candidate { get; }
    public string ModelFingerprint { get; }
    public ParsimonyArtifactDigest GrammarFacts { get; }
    public ParsimonyArtifactDigest Evidence { get; }
    public string? SelectionSha256 { get; }
    public string? ExpectationRevisionSha256 { get; }
    public IReadOnlyList<string> AssessmentIds { get; }
    public ParsimonyEvidenceScopeKind EvidenceScope { get; }
    public RetirementExpectationTranslation? RetirementExpectationTranslation { get; }

    private static ParsimonyArtifactDigest ValidateArtifact(ParsimonyArtifactDigest? artifact, string name)
    {
        ArgumentNullException.ThrowIfNull(artifact, name);
        if (artifact.SchemaVersion < 1 || artifact.Sha256 is null || artifact.Sha256.Length != 64 ||
            artifact.Sha256.Any(character => !Uri.IsHexDigit(character) || char.IsUpper(character)))
            throw new ArgumentException("An artifact needs a positive schema version and a lowercase SHA-256 digest.", name);
        return artifact;
    }

    private static string? ValidateDigest(string? digest, string name)
    {
        if (digest is null) return null;
        if (digest.Length != 64 || digest.Any(character => !Uri.IsHexDigit(character) || char.IsUpper(character)))
            throw new ArgumentException("A digest must contain 64 lowercase hexadecimal characters.", name);
        return digest;
    }

    private static string RequireText(string? value, string name) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("A nonblank value is required.", name) : value;
}

/// <summary>Rejects unknown and repeated input fields, pinned by `ParsimonyInputsRejectUnknownFields` and `ParsimonyInputsRejectDuplicateFields`.</summary>
public sealed class ParsimonyReportInputsJsonConverter : JsonConverter<ParsimonyReportInputs>
{
    private static readonly string[] InputFields =
    [
        "bundleId", "baselineToken", "inputKind", "candidate", "modelFingerprint", "grammarFacts",
        "evidence", "selectionSha256", "expectationRevisionSha256", "assessmentIds", "evidenceScope",
        "retirementExpectationTranslation",
    ];

    public override ParsimonyReportInputs Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var fields = ReadObject(document.RootElement, "Parsimony inputs", InputFields,
            "candidate", "selectionSha256", "expectationRevisionSha256", "retirementExpectationTranslation");
        var nestedOptions = new JsonSerializerOptions(options) { PropertyNameCaseInsensitive = true };
        try
        {
            var baseline = ReadBaselineToken(fields["baselineToken"], nestedOptions);
            return new ParsimonyReportInputs(
                ReadText(fields["bundleId"], "bundleId"),
                baseline,
                ReadText(fields["inputKind"], "inputKind"),
                ReadNullableText(fields["candidate"], "candidate"),
                ReadText(fields["modelFingerprint"], "modelFingerprint"),
                ReadArtifact(fields["grammarFacts"], "grammarFacts"),
                ReadArtifact(fields["evidence"], "evidence"),
                ReadNullableText(fields["selectionSha256"], "selectionSha256"),
                ReadNullableText(fields["expectationRevisionSha256"], "expectationRevisionSha256"),
                ReadTextArray(fields["assessmentIds"], "assessmentIds"),
                ReadEnum<ParsimonyEvidenceScopeKind>(fields["evidenceScope"], "evidenceScope"),
                fields.TryGetValue("retirementExpectationTranslation", out var retirement) &&
                retirement.ValueKind != JsonValueKind.Null
                    ? RetirementExpectationTranslationCodec.Parse(retirement.GetRawText())
                    : null);
        }
        catch (ArgumentException exception)
        {
            throw new JsonException("Parsimony inputs contain an invalid value.", exception);
        }
    }

    public override void Write(Utf8JsonWriter writer, ParsimonyReportInputs value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("bundleId", value.BundleId);
        var nestedOptions = new JsonSerializerOptions(options) { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        writer.WritePropertyName("baselineToken");
        JsonSerializer.Serialize(writer, value.BaselineToken, nestedOptions);
        writer.WriteString("inputKind", value.InputKind);
        WriteNullableText(writer, "candidate", value.Candidate);
        writer.WriteString("modelFingerprint", value.ModelFingerprint);
        WriteArtifact(writer, "grammarFacts", value.GrammarFacts);
        WriteArtifact(writer, "evidence", value.Evidence);
        WriteNullableText(writer, "selectionSha256", value.SelectionSha256);
        WriteNullableText(writer, "expectationRevisionSha256", value.ExpectationRevisionSha256);
        writer.WritePropertyName("assessmentIds");
        JsonSerializer.Serialize(writer, value.AssessmentIds, options);
        writer.WritePropertyName("evidenceScope");
        JsonSerializer.Serialize(writer, value.EvidenceScope, options);
        if (value.RetirementExpectationTranslation is not null)
        {
            writer.WritePropertyName("retirementExpectationTranslation");
            writer.WriteRawValue(RetirementExpectationTranslationCodec.ToJson(value.RetirementExpectationTranslation));
        }
        writer.WriteEndObject();
    }

    private static TEnum ReadEnum<TEnum>(JsonElement value, string name) where TEnum : struct, Enum
    {
        if (value.ValueKind != JsonValueKind.String)
            throw new JsonException($"{name} must be a supported value.");
        try
        {
            var parsed = JsonSerializer.Deserialize<TEnum>(value.GetRawText());
            return Enum.IsDefined(parsed) ? parsed : throw new JsonException($"{name} must be a supported value.");
        }
        catch (JsonException)
        {
            throw new JsonException($"{name} must be a supported value.");
        }
    }

    private static BaselineToken ReadBaselineToken(JsonElement value, JsonSerializerOptions options)
    {
        var fields = ReadObject(value, "baselineToken",
        [
            "projectIdentity", "semanticSnapshotDigest", "projectionVersion", "capturedUtc", "bundleDigest",
            "capturedHostSessionId", "capturedEditGeneration",
        ], "capturedHostSessionId", "capturedEditGeneration");
        BaselineToken? token;
        try
        {
            token = JsonSerializer.Deserialize<BaselineToken>(value.GetRawText(), options);
        }
        catch (ArgumentException exception)
        {
            throw new JsonException("baselineToken contains an invalid value.", exception);
        }
        if (token is null)
            throw new JsonException("baselineToken is required.");
        return token;
    }

    private static ParsimonyArtifactDigest ReadArtifact(JsonElement value, string name)
    {
        var fields = ReadObject(value, name, ["schemaVersion", "sha256"]);
        if (!fields["schemaVersion"].TryGetInt32(out var version))
            throw new JsonException($"{name}.schemaVersion must be an integer.");
        return new ParsimonyArtifactDigest(version, ReadText(fields["sha256"], $"{name}.sha256"));
    }

    private static Dictionary<string, JsonElement> ReadObject(
        JsonElement value, string name, IEnumerable<string> allowed, params string[] optional)
    {
        if (value.ValueKind != JsonValueKind.Object)
            throw new JsonException($"{name} must be an object.");
        var allowedNames = new HashSet<string>(allowed, StringComparer.Ordinal);
        var optionalNames = new HashSet<string>(optional, StringComparer.Ordinal);
        var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            if (!allowedNames.Contains(property.Name))
                throw new JsonException($"{name} contains unknown field '{property.Name}'.");
            if (!fields.TryAdd(property.Name, property.Value))
                throw new JsonException($"{name} contains duplicate field '{property.Name}'.");
        }
        foreach (var field in allowedNames)
        {
            if (!optionalNames.Contains(field) && !fields.ContainsKey(field))
                throw new JsonException($"{name} is missing required field '{field}'.");
        }
        return fields;
    }

    private static string ReadText(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!
            : throw new JsonException($"{name} must be a nonblank string.");

    private static string? ReadNullableText(JsonElement value, string name) => value.ValueKind switch
    {
        JsonValueKind.Null => null,
        JsonValueKind.String => value.GetString(),
        _ => throw new JsonException($"{name} must be a string or null."),
    };

    private static IReadOnlyList<string> ReadTextArray(JsonElement value, string name)
    {
        if (value.ValueKind != JsonValueKind.Array)
            throw new JsonException($"{name} must be an array.");
        return value.EnumerateArray().Select((entry, index) => ReadText(entry, $"{name}[{index}]")).ToArray();
    }

    private static void WriteArtifact(Utf8JsonWriter writer, string name, ParsimonyArtifactDigest artifact)
    {
        writer.WritePropertyName(name);
        writer.WriteStartObject();
        writer.WriteNumber("schemaVersion", artifact.SchemaVersion);
        writer.WriteString("sha256", artifact.Sha256);
        writer.WriteEndObject();
    }

    private static void WriteNullableText(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is null) writer.WriteNull(name);
        else writer.WriteString(name, value);
    }
}
