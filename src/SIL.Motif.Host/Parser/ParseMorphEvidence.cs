using System.Text.Json;
using System.Text.Json.Serialization;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Host.Parser;

/// <summary>Validates the closed FieldWorks morphology batch contract before it becomes evidence.</summary>
public static class ParseMorphEvidence
{
    public const string Schema = "fieldworks-parse-analysis/v1";
    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
        AllowDuplicateProperties = false,
    };

    public static IReadOnlyList<ParseWordEvidence> Read(string jsonl, IReadOnlyList<string> words)
    {
        try
        {
            var rows = jsonl.Split('\n').Where(line => !string.IsNullOrWhiteSpace(line))
                .Select(line => JsonSerializer.Deserialize<ParseWordEvidence>(line, JsonOptions)
                    ?? throw new JsonException("Null case.")).ToArray();
            if (rows.Length != words.Count) throw new JsonException("Missing or extra batch cases.");
            for (var index = 0; index < rows.Length; index++)
            {
                var row = rows[index];
                if (row.Schema != Schema || row.Index != index || row.Word != words[index] || row.ElapsedMs < 0)
                    throw new JsonException("Unknown schema or mismatched batch case.");
                if (row.InvalidShape && (row.Capped || row.TimedOut || row.Analyses.Count != 0 || row.Unavailable.Count != 0))
                    throw new JsonException("An invalid shape cannot contain search findings or limits.");
                if (row.Unavailable.Any(string.IsNullOrWhiteSpace)) throw new JsonException("Missing projection reason.");
                foreach (var analysis in row.Analyses)
                {
                    if (analysis is null || analysis.Morphs is null || analysis.Morphs.Count == 0)
                        throw new JsonException("An analysis must contain morphs.");
                    foreach (var morph in analysis.Morphs)
                    {
                        if (morph is null) throw new JsonException("Null morph.");
                        CheckGuid(morph.Form);
                        CheckGuid(morph.Msa);
                        CheckGuid(morph.InflType);
                        if (morph.Form is null || morph.Msa is null)
                            throw new JsonException("Every morph requires authoritative Form and MSA GUIDs.");
                    }
                }
            }
            return rows;
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            throw new InvalidDataException("Invalid or obsolete parse morphology evidence: " + exception.Message, exception);
        }
    }

    /// <summary>Validates canonical source GUIDs and literal forms in frozen approved readings.</summary>
    public static void ValidateExpectations(IReadOnlyList<ApprovedMorphology> expectations)
    {
        try
        {
            foreach (var expected in expectations)
            {
                if (expected is null || expected.Morphs is null) throw new JsonException("Null approved reading.");
                CheckGuid(expected.SourceWordformGuid);
                foreach (var morph in expected.Morphs)
                {
                    if (morph is null || morph.Forms is null || morph.Forms.Any(value => value is null))
                        throw new JsonException("Null approved morph or literal form.");
                    CheckGuid(morph.Form);
                    CheckGuid(morph.Msa);
                    CheckGuid(morph.InflType);
                }
            }
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Invalid frozen morphology expectations: " + exception.Message, exception);
        }
    }

    private static void CheckGuid(string? value)
    {
        if (value is not null && (!Guid.TryParseExact(value, "D", out var guid) || guid == Guid.Empty || guid.ToString("D") != value))
            throw new JsonException("A source identity must be a canonical nonempty GUID.");
    }
}

/// <summary>Matches all approved morphologies using FieldWorks' ordered reference and guessed-string rules.</summary>
public static class MorphologyCorrectness
{
    public static WordCorrectness Compare(ParseWordEvidence result, IReadOnlyList<ApprovedMorphology> expectations)
    {
        ParseMorphEvidence.ValidateExpectations(expectations);
        var distinct = expectations.DistinctBy(item => JsonSerializer.Serialize(item, ParseMorphEvidence.JsonOptions)).ToArray();
        var unavailable = result.Unavailable.ToList();
        if (distinct.Any(item => item.Morphs.Count == 0 || item.Morphs.Any(morph => morph.Form is null || morph.Msa is null)))
            unavailable.Add("An approved reading lacks authoritative Form or MSA identity.");
        var unmatched = distinct.Select((expected, index) => (expected, index))
            .Where(item => !result.Analyses.Any(actual => Matches(actual, item.expected)))
            .Select(item => item.index).ToArray();
        var status = result.Capped || result.TimedOut ? "incomplete"
            : result.InvalidShape ? "unavailable"
            : distinct.Length == 0 ? "no-expectations"
            : unmatched.Length == 0 ? "covered"
            : unavailable.Count > 0 ? "unavailable" : "unmatched";
        return new WordCorrectness(distinct.Length, distinct.Length - unmatched.Length, status, distinct, unmatched)
        { Unavailable = unavailable };
    }

    private static bool Matches(ParseAnalysis actual, ApprovedMorphology expected) =>
        actual.Morphs.Count > 0 && actual.Morphs.Count == expected.Morphs.Count &&
        actual.Morphs.Zip(expected.Morphs).All(pair => pair.First.Form is not null && pair.First.Msa is not null &&
            pair.First.Form == pair.Second.Form &&
            pair.First.Msa == pair.Second.Msa && pair.First.InflType == pair.Second.InflType &&
            (pair.First.GuessedString is null || pair.Second.Forms.Contains(pair.First.GuessedString, StringComparer.Ordinal)));
}
