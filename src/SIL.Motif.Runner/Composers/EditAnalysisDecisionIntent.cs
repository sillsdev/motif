using System.Text.Json;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Parsing;

namespace SIL.Motif.Runner.Composers;

/// <summary>The human decisions an analysis edit can record.</summary>
public static class AnalysisDecisions
{
    /// <summary>Approve one stored analysis.</summary>
    public const string Approve = "approve";
    /// <summary>Disapprove one stored analysis.</summary>
    public const string Disapprove = "disapprove";
    /// <summary>Return one stored analysis to the unknown state.</summary>
    public const string Unknown = "unknown";
    /// <summary>Mark one wordform's spelling as incorrect.</summary>
    public const string Misspelled = "misspelled";
    /// <summary>Store one parser reading under a wordform as a new analysis in the unknown state.</summary>
    public const string AddReading = "add-reading";
}

/// <summary>One morph of a parser reading, identified by exact canonical identities.</summary>
/// <param name="Form">The allomorph, when the reading names one.</param>
/// <param name="Msa">The morphosyntactic analysis, when the reading names one.</param>
/// <param name="InflType">The inflectional type of a variant, when the reading names one.</param>
/// <param name="GuessedString">The surface text of a guessed morph that has no allomorph.</param>
public sealed record ReadingMorph(CanonicalId? Form, CanonicalId? Msa, CanonicalId? InflType, string? GuessedString);

/// <summary>One human decision about an existing analysis or wordform.</summary>
/// <param name="Decision">One of the <see cref="AnalysisDecisions"/> values.</param>
/// <param name="Analysis">The stored analysis, for approve, disapprove and unknown.</param>
/// <param name="Wordform">The wordform, for misspelled and add-reading.</param>
/// <param name="Morphs">The ordered morphs of the reading, for add-reading.</param>
public sealed record EditAnalysisDecisionIntent(string Decision, CanonicalId? Analysis, CanonicalId? Wordform,
    IReadOnlyList<ReadingMorph>? Morphs = null);

/// <summary>
/// Parses <c>{ "decision": "approve|disapprove|unknown", "analysis": id }</c> or
/// <c>{ "decision": "misspelled", "wordform": id }</c>, or <c>{ "decision": "add-reading", "wordform": id,
/// "morphs": [ { "form": id, "msa": id, "inflType": id, "guessedString": "..." } ] }</c>. Closed to any other or repeated property.
/// </summary>
public static class EditAnalysisDecisionIntentParser
{
    private const string ConstructName = "EditAnalysisDecision";

    /// <summary>Reads the decision and exactly the one identity it needs.</summary>
    public static EditAnalysisDecisionIntent Parse(JsonElement authored)
    {
        SoundSystemIntentParsing.Object(authored, ConstructName, "decision", "analysis", "wordform", "morphs");
        var decision = SoundSystemIntentParsing.Text(authored, "decision");
        var hasAnalysis = authored.TryGetProperty("analysis", out _);
        var hasWordform = authored.TryGetProperty("wordform", out _);
        var hasMorphs = authored.TryGetProperty("morphs", out _);
        if (hasMorphs != (decision == AnalysisDecisions.AddReading))
            throw new ContractParseException($"'{ConstructName}': only 'add-reading' takes 'morphs', and it requires them.");
        switch (decision)
        {
            case AnalysisDecisions.Approve or AnalysisDecisions.Disapprove or AnalysisDecisions.Unknown:
                if (!hasAnalysis || hasWordform)
                    throw new ContractParseException($"'{ConstructName}': '{decision}' takes 'analysis' and no 'wordform'.");
                return new(decision, SoundSystemIntentParsing.Id(authored, "analysis"), null);
            case AnalysisDecisions.Misspelled:
                if (!hasWordform || hasAnalysis)
                    throw new ContractParseException($"'{ConstructName}': 'misspelled' takes 'wordform' and no 'analysis'.");
                return new(decision, null, SoundSystemIntentParsing.Id(authored, "wordform"));
            case AnalysisDecisions.AddReading:
                if (!hasWordform || hasAnalysis)
                    throw new ContractParseException($"'{ConstructName}': 'add-reading' takes 'wordform' and no 'analysis'.");
                return new(decision, null, SoundSystemIntentParsing.Id(authored, "wordform"), Morphs(authored));
            default:
                throw new ContractParseException(
                    $"'{ConstructName}': 'decision' must be approve, disapprove, unknown, misspelled or add-reading.");
        }
    }

    private static ReadingMorph[] Morphs(JsonElement authored)
    {
        var morphs = SoundSystemIntentParsing.Array(authored, "morphs").Select(morph =>
        {
            SoundSystemIntentParsing.Object(morph, ConstructName + " morph", "form", "msa", "inflType", "guessedString");
            CanonicalId? Optional(string name) => morph.TryGetProperty(name, out _)
                ? SoundSystemIntentParsing.Id(morph, name) : null;
            string? guessed = null;
            if (morph.TryGetProperty("guessedString", out var text))
            {
                guessed = text.ValueKind == JsonValueKind.String ? text.GetString() : null;
                if (string.IsNullOrWhiteSpace(guessed))
                    throw new ContractParseException($"'{ConstructName}': 'guessedString' must be a nonblank string.");
            }
            var result = new ReadingMorph(Optional("form"), Optional("msa"), Optional("inflType"), guessed);
            if (result.Form is null && result.Msa is null && result.InflType is null && guessed is null)
                throw new ContractParseException($"'{ConstructName}': each morph names at least one property.");
            return result;
        }).ToArray();
        return morphs.Length > 0 ? morphs
            : throw new ContractParseException($"'{ConstructName}': 'morphs' needs at least one morph.");
    }
}
