using System.Buffers;
using System.Text;
using System.Text.Json;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Phonology;

public sealed record ValidatedPhonologyExamples(
    string Name,
    IReadOnlyList<PhonologyExample> Examples,
    string? ExpectedRevisionId,
    string? ProposalId,
    string ExecutableSha256,
    string CitedRevisionSha256,
    int PayloadUtf8Bytes);

public static class PhonologyExampleValidation
{
    public const int MaxPayloadUtf8Bytes = 32 * 1024 * 1024;
    public const int MaxExamples = 500;
    public const int MaxExpectations = 16;
    public const int MaxPageSize = 100;
    public const int MaxSerializedPageUtf8Bytes = 4 * 1024 * 1024;

    public static ValidatedPhonologyExamples ValidateAndCopy(string? name,
        IReadOnlyList<PhonologyExample>? examples, string? expectedRevisionId = null, string? proposalId = null)
    {
        var normalizedName = NormalizeBoundedText(name, "name", 128, trim: true);
        if (examples is null || examples.Count is < 1 or > MaxExamples)
            throw new ArgumentException($"An Example Set must contain 1–{MaxExamples} examples.", nameof(examples));

        if (expectedRevisionId is not null && !CanonicalId.TryParse(expectedRevisionId, out _))
            throw new ArgumentException("The expected revision id must be a canonical id.", nameof(expectedRevisionId));
        if (proposalId is not null && !CanonicalId.TryParse(proposalId, out _))
            throw new ArgumentException("The Proposal id must be a canonical id.", nameof(proposalId));

        var copied = new PhonologyExample[examples.Count];
        var exampleIds = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < examples.Count; index++)
        {
            var example = examples[index] ?? throw new ArgumentException(
                "An Example Set cannot contain a null example.", nameof(examples));
            var id = NormalizeBoundedText(example.Id, $"examples[{index}].id", 128);
            if (!exampleIds.Add(id))
                throw new ArgumentException($"Example id '{id}' is repeated.", nameof(examples));
            var underlying = NormalizeBoundedText(example.Underlying, $"examples[{index}].underlying", 256);
            if (example.Expected is null || example.Expected.Count is < 1 or > MaxExpectations)
                throw new ArgumentException(
                    $"Each example must contain 1–{MaxExpectations} expected forms.", nameof(examples));
            var expected = new string[example.Expected.Count];
            var expectedForms = new HashSet<string>(StringComparer.Ordinal);
            for (var expectedIndex = 0; expectedIndex < example.Expected.Count; expectedIndex++)
            {
                var form = NormalizeBoundedText(example.Expected[expectedIndex],
                    $"examples[{index}].expected[{expectedIndex}]", 256);
                if (!expectedForms.Add(form))
                    throw new ArgumentException("An example cannot repeat an expected form.", nameof(examples));
                expected[expectedIndex] = form;
            }

            var context = CopyContext(example.Context, index);
            var citation = CopyCitation(example.Citation, index);
            copied[index] = new PhonologyExample(id, underlying, Array.AsReadOnly(expected), context, citation);
        }

        var immutableExamples = Array.AsReadOnly(copied);
        var payloadBytes = JsonSerializer.SerializeToUtf8Bytes(
            new SavePayload(normalizedName, immutableExamples, expectedRevisionId, proposalId),
            PhonologyJson.CreateOptions()).Length;
        if (payloadBytes > MaxPayloadUtf8Bytes)
            throw new ArgumentException($"The Example Set payload exceeds {MaxPayloadUtf8Bytes} UTF-8 bytes.", nameof(examples));

        return new ValidatedPhonologyExamples(normalizedName, immutableExamples, expectedRevisionId, proposalId,
            PhonologyExampleDigest.Executable(immutableExamples),
            PhonologyExampleDigest.CitedRevision(normalizedName, proposalId, immutableExamples), payloadBytes);
    }

    public static SavePhonologyExamplesRequest ParseSaveFile(string json, string projectPath)
    {
        ArgumentNullException.ThrowIfNull(json);
        if (Encoding.UTF8.GetByteCount(json) > MaxPayloadUtf8Bytes)
            throw new ArgumentException($"The Example Set payload exceeds {MaxPayloadUtf8Bytes} UTF-8 bytes.", nameof(json));
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            RequireObject(root, "save file");
            EnsureUniqueProperties(root);
            EnsureOnlyProperties(root, "save file", "name", "examples", "expectedRevisionId", "proposalId");
            var name = ReadRequiredString(root, "name");
            if (!root.TryGetProperty("examples", out var examplesNode) ||
                examplesNode.ValueKind != JsonValueKind.Array)
                throw new ArgumentException("The save file must include an examples array.", nameof(json));
            ValidateExamplesJson(examplesNode);
            var examples = JsonSerializer.Deserialize<PhonologyExample[]>(examplesNode.GetRawText(),
                PhonologyJson.CreateOptions()) ?? throw new ArgumentException("The examples array is null.", nameof(json));
            var expectedRevisionId = ReadOptionalString(root, "expectedRevisionId");
            var proposalId = ReadOptionalString(root, "proposalId");
            var validated = ValidateAndCopy(name, examples, expectedRevisionId, proposalId);
            return new SavePhonologyExamplesRequest(projectPath, validated.Name, validated.Examples,
                validated.ExpectedRevisionId, validated.ProposalId);
        }
        catch (ArgumentException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or FormatException or InvalidOperationException)
        {
            throw new ArgumentException("The save file is not a valid closed Example Set payload.", nameof(json), exception);
        }
    }

    /// <summary>The largest step ceiling a phonology check may request.</summary>
    public const ulong MaxStepCap = 50_000_000;

    /// <summary>The largest work ceiling a phonology check may request.</summary>
    public const ulong MaxWorkCap = 5_000_000_000;

    /// <summary>The longest shared deadline, in milliseconds, a phonology check may request.</summary>
    public const uint MaxTimeoutMs = 600_000;

    /// <summary>The ceilings a phonology check uses when its request names none.</summary>
    public static readonly PhonologyLimits DefaultLimits = new(1_000_000, 100_000_000, 60_000);

    /// <summary>Whether the ceilings are positive and within the producer's accepted range.</summary>
    /// <param name="limits">The requested request-wide ceilings.</param>
    public static bool AreLimitsInRange(PhonologyLimits limits) =>
        limits is { StepCap: > 0 and <= MaxStepCap, WorkCap: > 0 and <= MaxWorkCap, TimeoutMs: > 0 and <= MaxTimeoutMs };

    public static void ValidatePageSize(int pageSize)
    {
        if (pageSize is < 1 or > MaxPageSize)
            throw new ArgumentOutOfRangeException(nameof(pageSize), $"Page size must be 1–{MaxPageSize}.");
    }

    public static void ValidateRevisionId(string revisionId)
    {
        if (!CanonicalId.TryParse(revisionId, out _))
            throw new ArgumentException("A canonical revision id is required.", nameof(revisionId));
    }

    public static void ValidateSetListCursor(PhonologySetListCursor? cursor, string projectIdentity)
    {
        if (cursor is null) return;
        if (!StringComparer.Ordinal.Equals(cursor.ProjectIdentity, projectIdentity) ||
            !CanonicalId.TryParse(cursor.AfterSetId, out _))
            throw new ArgumentException("The Example Set cursor belongs to a different project or is malformed.", nameof(cursor));
    }

    public static void ValidatePageCursor(PhonologyPageCursor? cursor, string revisionId)
    {
        if (cursor is null) return;
        if (!StringComparer.Ordinal.Equals(cursor.OwnerId, revisionId) || cursor.AfterOrdinal < 0)
            throw new ArgumentException("The page cursor belongs to a different revision or is malformed.", nameof(cursor));
    }

    private static PhonologyGenerationContext CopyContext(PhonologyGenerationContext? context, int index) => context switch
    {
        PhonologyGenerationContext.HcXmlEntry hc => new PhonologyGenerationContext.HcXmlEntry(
            NormalizeBoundedText(hc.EntryId, $"examples[{index}].context.entryId", 256)),
        PhonologyGenerationContext.FwdataEntry fw when fw.MsaClass == PhonologyMsaClass.MoStemMsa =>
            new PhonologyGenerationContext.FwdataEntry(
                NormalizeGuid(fw.EntryGuid, $"examples[{index}].context.entryGuid"),
                NormalizeGuid(fw.MsaGuid, $"examples[{index}].context.msaGuid"), fw.MsaClass),
        PhonologyGenerationContext.FwdataEntry => throw new ArgumentException(
            $"Example {index} has an unsupported MSA class.", nameof(context)),
        null => throw new ArgumentException($"Example {index} needs a generation context.", nameof(context)),
        _ => throw new ArgumentException($"Example {index} has an unsupported generation context.", nameof(context)),
    };

    private static CitationView? CopyCitation(CitationView? citation, int index)
    {
        if (citation is null) return null;
        try
        {
            var normalized = OperationCitation.Create(citation.Source, citation.Locator);
            return new CitationView(normalized.Source, normalized.Locator);
        }
        catch (ContractParseException exception)
        {
            throw new ArgumentException($"Example {index} has an invalid Citation.", nameof(citation), exception);
        }
    }

    private static string NormalizeGuid(string? value, string parameterName)
    {
        if (!Guid.TryParse(value, out var parsed))
            throw new ArgumentException("A FieldWorks source identity must be a GUID.", parameterName);
        return parsed.ToString("D").ToLowerInvariant();
    }

    private static string NormalizeBoundedText(string? value, string parameterName, int maxScalars, bool trim = false)
    {
        var text = trim ? value?.Trim() : value;
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("A non-empty value is required.", parameterName);
        var scalarCount = 0;
        var remaining = text.AsSpan();
        while (!remaining.IsEmpty)
        {
            var status = Rune.DecodeFromUtf16(remaining, out _, out var consumed);
            if (status != OperationStatus.Done)
                throw new ArgumentException("Text must contain valid Unicode scalar values.", parameterName);
            scalarCount++;
            remaining = remaining[consumed..];
        }
        if (scalarCount > maxScalars)
            throw new ArgumentException($"A value must contain at most {maxScalars} Unicode scalar values.", parameterName);
        return text;
    }

    private static void ValidateExamplesJson(JsonElement examples)
    {
        foreach (var example in examples.EnumerateArray())
        {
            RequireObject(example, "example");
            EnsureOnlyProperties(example, "example", "id", "underlying", "expected", "context", "citation");
            RequireProperties(example, "id", "underlying", "expected", "context");
            if (example.TryGetProperty("context", out var context)) ValidateContextJson(context);
            if (example.TryGetProperty("citation", out var citation) && citation.ValueKind != JsonValueKind.Null)
            {
                RequireObject(citation, "citation");
                EnsureOnlyProperties(citation, "citation", "source", "locator");
                RequireProperties(citation, "source");
            }
        }
    }

    private static void ValidateContextJson(JsonElement context)
    {
        RequireObject(context, "context");
        var kind = ReadRequiredString(context, "kind");
        switch (kind)
        {
            case "hcXmlEntry":
                EnsureOnlyProperties(context, "context", "kind", "entryId");
                RequireProperties(context, "entryId");
                break;
            case "fwdataEntry":
                EnsureOnlyProperties(context, "context", "kind", "entryGuid", "msaGuid", "msaClass");
                RequireProperties(context, "entryGuid", "msaGuid", "msaClass");
                if (!StringComparer.Ordinal.Equals(ReadRequiredString(context, "msaClass"), "MoStemMsa"))
                    throw new ArgumentException("Only the MoStemMsa context class is supported.");
                break;
            default:
                throw new ArgumentException("The generation context kind is not supported.");
        }
    }

    private static void EnsureUniqueProperties(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in element.EnumerateObject())
                {
                    if (!names.Add(property.Name))
                        throw new ArgumentException($"The JSON object repeats '{property.Name}'.");
                    EnsureUniqueProperties(property.Value);
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray()) EnsureUniqueProperties(item);
                break;
        }
    }

    private static void RequireObject(JsonElement value, string name)
    {
        if (value.ValueKind != JsonValueKind.Object)
            throw new ArgumentException($"The {name} must be a JSON object.");
    }

    private static void EnsureOnlyProperties(JsonElement value, string name, params string[] allowed)
    {
        var accepted = new HashSet<string>(allowed, StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
            if (!accepted.Contains(property.Name))
                throw new ArgumentException($"The {name} has unknown field '{property.Name}'.");
    }

    private static void RequireProperties(JsonElement value, params string[] required)
    {
        foreach (var name in required)
            if (!value.TryGetProperty(name, out _))
                throw new ArgumentException($"The JSON object is missing '{name}'.");
    }

    private static string ReadRequiredString(JsonElement value, string name) =>
        value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()!
            : throw new ArgumentException($"The field '{name}' must be a string.");

    private static string? ReadOptionalString(JsonElement value, string name)
    {
        if (!value.TryGetProperty(name, out var property) || property.ValueKind == JsonValueKind.Null) return null;
        if (property.ValueKind == JsonValueKind.String) return property.GetString();
        throw new ArgumentException($"The field '{name}' must be a string or null.");
    }

    private sealed record SavePayload(
        string Name,
        IReadOnlyList<PhonologyExample> Examples,
        string? ExpectedRevisionId,
        string? ProposalId);
}
