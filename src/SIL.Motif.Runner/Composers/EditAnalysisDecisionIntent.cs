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
}

/// <summary>One human decision about an existing analysis or wordform.</summary>
/// <param name="Decision">One of the <see cref="AnalysisDecisions"/> values.</param>
/// <param name="Analysis">The stored analysis, for approve, disapprove and unknown.</param>
/// <param name="Wordform">The wordform, for misspelled.</param>
public sealed record EditAnalysisDecisionIntent(string Decision, CanonicalId? Analysis, CanonicalId? Wordform);

/// <summary>
/// Parses <c>{ "decision": "approve|disapprove|unknown", "analysis": id }</c> or
/// <c>{ "decision": "misspelled", "wordform": id }</c>. Closed to any other or repeated property.
/// </summary>
public static class EditAnalysisDecisionIntentParser
{
    private const string ConstructName = "EditAnalysisDecision";

    /// <summary>Reads the decision and exactly the one identity it needs.</summary>
    public static EditAnalysisDecisionIntent Parse(JsonElement authored)
    {
        SoundSystemIntentParsing.Object(authored, ConstructName, "decision", "analysis", "wordform");
        var decision = SoundSystemIntentParsing.Text(authored, "decision");
        var hasAnalysis = authored.TryGetProperty("analysis", out _);
        var hasWordform = authored.TryGetProperty("wordform", out _);
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
            default:
                throw new ContractParseException(
                    $"'{ConstructName}': 'decision' must be approve, disapprove, unknown or misspelled.");
        }
    }
}
