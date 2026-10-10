using SIL.Motif.Contract.Responses;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Host.Analysis;
using SIL.Motif.Host.Parser;

namespace SIL.Motif.Commands.SelectionReading;

/// <summary>The parser result after comparison with the exact stored analyses for one wordform.</summary>
public enum SelectionAnalysisClass
{
    Same,
    Conflict,
    Different,
    Extra,
    None,
    Capped,
    NotAssessed,
    Refused,
}

/// <summary>One parser reading and its exact matching stored analysis identities.</summary>
public sealed record SelectionAnalysisReadingFacts(
    int Index,
    IReadOnlyList<string> MatchingAnalysisIds,
    IReadOnlyList<string> MatchingOpinions)
{
    public bool MatchesStored => MatchingAnalysisIds.Count > 0;
    public bool IsParserOnly => !MatchesStored;
}

/// <summary>Pure parser-agreement facts shared by the Selection reader and the window.</summary>
public sealed record SelectionAnalysisClassification(
    SelectionAnalysisClass Class,
    IReadOnlyList<SelectionAnalysisReadingFacts> Readings);

/// <summary>Builds parser-agreement classes from compact semantic identities.</summary>
public static class SelectionAnalysisRules
{
    /// <summary>Matches each parser reading to stored identities and classifies their agreement.</summary>
    public static SelectionAnalysisClassification Build(bool incorrectSpelling, string? outcome,
        bool isIncomplete, ParseWordEvidence? morphology, IReadOnlyList<ParserReading> storedAnalyses)
    {
        ArgumentNullException.ThrowIfNull(storedAnalyses);
        var stored = storedAnalyses
            .Select(analysis => (Id: analysis.StoredAnalysisId ?? string.Empty,
                Opinion: NormalizeOpinion(analysis.StoredAnalysisOpinion),
                analysis.Identity)).ToArray();
        var readings = (morphology?.Analyses ?? []).Select((analysis, index) =>
        {
            var matches = stored.Where(candidate => candidate.Identity is not null &&
                AnalysisMorphologyMatcher.Matches(analysis, candidate.Identity)).ToArray();
            return new SelectionAnalysisReadingFacts(index,
                matches.Select(candidate => candidate.Id).ToArray(),
                matches.Select(candidate => candidate.Opinion).Distinct(StringComparer.Ordinal).ToArray());
        }).ToArray();

        var marking = Classify(incorrectSpelling, outcome, isIncomplete, morphology, stored, readings);
        return new SelectionAnalysisClassification(marking, readings);
    }

    /// <summary>Unknown stored opinions share the same comparison label as Candidate.</summary>
    public static string NormalizeOpinion(string? opinion) => opinion is null or "unknown" ? ReadingGrade.Candidate : opinion;

    /// <summary>Whether the comparison offers an unstaged opinion or parser action under ADR 0049.</summary>
    public static bool NeedsALook(SelectionAnalysisClassification classification,
        IReadOnlyDictionary<string, string> opinions)
    {
        ArgumentNullException.ThrowIfNull(classification);
        ArgumentNullException.ThrowIfNull(opinions);
        if (classification.Class is SelectionAnalysisClass.NotAssessed or SelectionAnalysisClass.Refused or
            SelectionAnalysisClass.Capped) return false;
        if (classification.Class == SelectionAnalysisClass.None && opinions.Values.Contains(ReadingGrade.Approved))
            return false;
        if (classification.Class != SelectionAnalysisClass.Same)
            return opinions.Count > 0 || classification.Readings.Any(reading => reading.IsParserOnly);
        var firstOpinion = classification.Readings.Select(reading => opinions.FirstOrDefault(opinion =>
                reading.MatchingAnalysisIds.Contains(opinion.Key, StringComparer.Ordinal)).Value)
            .FirstOrDefault(opinion => opinion is not null) ?? opinions.Values.FirstOrDefault() ?? ReadingGrade.NoOpinion;
        return firstOpinion == ReadingGrade.Candidate && classification.Readings.Any(reading =>
            reading.MatchingAnalysisIds.Any(id => opinions.GetValueOrDefault(id) == ReadingGrade.Candidate));
    }

    private static SelectionAnalysisClass Classify(bool incorrectSpelling, string? outcome, bool incomplete,
        ParseWordEvidence? morphology,
        IReadOnlyList<(string Id, string Opinion, ApprovedMorphology? Identity)> stored,
        IReadOnlyList<SelectionAnalysisReadingFacts> readings)
    {
        if (outcome is null or "unassessed") return SelectionAnalysisClass.NotAssessed;
        if (ParserRefusals.Of(morphology, outcome) is not null) return SelectionAnalysisClass.Refused;
        if (incorrectSpelling && readings.Count > 0) return SelectionAnalysisClass.Conflict;
        if (readings.Any(reading => reading.MatchingAnalysisIds.Any(id => stored.Any(analysis =>
                analysis.Id == id && analysis.Opinion == ReadingGrade.Disapproved))))
            return SelectionAnalysisClass.Conflict;
        if (incomplete) return SelectionAnalysisClass.Capped;
        if (readings.Count == 0) return SelectionAnalysisClass.None;
        if (stored.Any(analysis => analysis.Opinion == ReadingGrade.Approved &&
                !readings.Any(reading => reading.MatchingAnalysisIds.Contains(analysis.Id, StringComparer.Ordinal))))
            return SelectionAnalysisClass.Conflict;
        if (readings.All(reading => reading.MatchesStored)) return SelectionAnalysisClass.Same;
        if (readings.Any(reading => reading.IsParserOnly) && readings.Any(reading =>
                reading.MatchingAnalysisIds.Any(id => stored.Any(analysis => analysis.Id == id &&
                    analysis.Opinion == ReadingGrade.Approved))))
            return SelectionAnalysisClass.Extra;
        return SelectionAnalysisClass.Different;
    }
}
