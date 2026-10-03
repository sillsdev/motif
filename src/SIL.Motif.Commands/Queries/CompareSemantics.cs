using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Analysis;

namespace SIL.Motif.Commands.Queries;

/// <summary>The parser column used for one word in the Compare matrix.</summary>
public enum CompareColumnKind { Match, NoMatch, NoParse, Timeout, Skipped }

/// <summary>The shared meaning of one project standing and parser outcome.</summary>
public enum CompareFamilyKind { Good, Fine, Violation, Review, New, Nobody, Unknown, None }

/// <summary>Facts the App and catalog commands use to place one word in the same Compare cell.</summary>
public sealed record CompareWordFacts(
    string? Standing,
    string Outcome,
    bool IsIncomplete,
    ParseWordEvidence? Morphology,
    IReadOnlyList<string>? ReadingGrades,
    int MissedApprovedCount)
{
    /// <summary>The identity comparison captured by the run, independent of readable Baseline context.</summary>
    public WordAnalysisComparison? AnalysisComparison { get; init; }

    /// <summary>Optional Baseline analyses with their individual identities and opinions.</summary>
    public IReadOnlyList<ParserReading>? StoredAnalyses { get; init; }

    /// <summary>Whether an empty stored-analysis list is established, rather than unread.</summary>
    public bool StoredAnalysesAvailable { get; init; }

    /// <summary>Recorded Approved analyses absent from the result.</summary>
    public IReadOnlyList<ParserReading>? MissedApproved { get; init; }

    /// <summary>The comparison facts carried by a shared Assessment row.</summary>
    public static CompareWordFacts Of(AssessmentWordResult word) => new(
        word.ProjectStanding, word.Outcome, word.IsIncomplete, word.Morphology, word.ReadingGrades,
        word.MissedApproved?.Count ?? 0)
    {
        StoredAnalyses = word.StoredAnalyses,
        StoredAnalysesAvailable = word.StoredAnalysesAvailable,
        AnalysisComparison = word.AnalysisComparison,
        MissedApproved = word.MissedApproved,
    };
}

/// <summary>One Compare cell and its shared product meaning.</summary>
public sealed record ComparePlacement(string Standing, CompareColumnKind Column);

/// <summary>One canonical definition of the Compare matrix's placement and meaning rules.</summary>
public static class CompareSemantics
{
    private static readonly FixFirstRule[] FixFirstRules =
    [
        new(ProjectStanding.Approved, CompareColumnKind.NoParse, FixFirstCategory.ApprovedNoParse, 1,
            "Approved × No parse", "An approved analysis was not built."),
        new(ProjectStanding.Approved, CompareColumnKind.NoMatch, FixFirstCategory.ApprovedNoMatch, 2,
            "Approved × No match", "An approved analysis was not built."),
        new(ProjectStanding.Rejected, CompareColumnKind.Match, FixFirstCategory.RejectedRebuilt, 3,
            "Rejected but rebuilt", "The parser rebuilt an analysis the project rejected."),
        new(ProjectStanding.Candidate, CompareColumnKind.NoParse, FixFirstCategory.CandidateNoParse, 4,
            "Candidate × No parse", "The parser could not rebuild this candidate."),
    ];

    /// <summary>Projects the same comparison from a full Assessment word row.</summary>
    public static WordComparison Compare(AssessmentWordResult word) => word.Comparison ?? Compare(CompareWordFacts.Of(word));

    /// <summary>
    /// Compares recorded evidence and any available Baseline identities. A Disapproved match keeps its opinion
    /// even if the same morphology is also Approved or Unknown. Stopped searches never establish absence.
    /// </summary>
    public static WordComparison Compare(CompareWordFacts word)
    {
        ArgumentNullException.ThrowIfNull(word);
        var standing = NormalizeStanding(word.Standing);
        var incomplete = StoppedAtLimit(word.Outcome, word.IsIncomplete, word.Morphology);
        var refusal = ParserRefusals.Of(word.Morphology, word.Outcome);
        var completed = !incomplete && refusal is null && word.Outcome != "unassessed";
        var stored = word.StoredAnalyses ?? [];
        var contextAvailable = (word.StoredAnalysesAvailable || word.StoredAnalyses is { Count: > 0 }) &&
            stored.All(analysis => analysis.Identity is not null && analysis.StoredAnalysisId is not null) &&
            (word.Morphology is not null || word.Outcome == "no-analysis");
        var identitiesAvailable = word.AnalysisComparison is not null || contextAvailable;
        var readings = word.AnalysisComparison?.Readings ?? (word.Morphology?.Analyses ?? []).Select((reading, index) => new ComparedReading(index,
            contextAvailable ? stored.Where(analysis => AnalysisMorphologyMatcher.Matches(reading, analysis.Identity!))
                .Select(analysis => new ComparedAnalysis(analysis.StoredAnalysisId!,
                    analysis.StoredAnalysisOpinion ?? ReadingGrade.Candidate)).ToArray() : [],
            word.ReadingGrades is { } grades && index < grades.Count ? grades[index] : null)).ToArray();
        var matchedIds = readings.SelectMany(reading => reading.Matches).Select(match => match.AnalysisId)
            .ToHashSet(StringComparer.Ordinal);
        var rebuilt = readings.SelectMany(reading => reading.Matches)
            .Where(match => match.Opinion == ReadingGrade.Disapproved).Distinct().ToArray();
        var missingApproved = !completed ? [] : contextAvailable
            ? stored.Where(analysis => analysis.StoredAnalysisOpinion == ReadingGrade.Approved &&
                !matchedIds.Contains(analysis.StoredAnalysisId!)).ToArray()
            : word.MissedApproved ?? [];
        var missingApprovedAnalyses = !completed ? [] : word.AnalysisComparison?.UnbuiltApproved ??
            missingApproved.Where(analysis => analysis.StoredAnalysisId is not null)
                .Select(analysis => new ComparedAnalysis(analysis.StoredAnalysisId!, ReadingGrade.Approved)).ToArray();
        var undecidedNotBuilt = !completed ? [] : word.AnalysisComparison?.UnbuiltUndecided ??
            (contextAvailable ? stored.Where(analysis =>
                (analysis.StoredAnalysisOpinion ?? ReadingGrade.Candidate) == ReadingGrade.Candidate &&
                !matchedIds.Contains(analysis.StoredAnalysisId!))
                .Select(analysis => new ComparedAnalysis(analysis.StoredAnalysisId!, ReadingGrade.Candidate)).ToArray() : []);
        var gradesForPlacement = word.ReadingGrades ?? readings.Select(reading =>
            reading.Matches.Any(match => match.Opinion == ReadingGrade.Approved) ? ReadingGrade.Approved :
            reading.Matches.Any(match => match.Opinion == ReadingGrade.Disapproved) ? ReadingGrade.Disapproved :
            reading.Matches.Any(match => match.Opinion == ReadingGrade.Candidate) ? ReadingGrade.Candidate :
            ReadingGrade.NoOpinion).ToArray();
        var rebuiltDisapproved = rebuilt.Length > 0 || gradesForPlacement.Contains(ReadingGrade.Disapproved);
        var column = PlaceCore(word, standing, gradesForPlacement,
            Math.Max(word.MissedApprovedCount, Math.Max(missingApproved.Count, missingApprovedAnalyses.Count)), rebuiltDisapproved);
        var code = refusal is not null ? "refused" : MeaningCodeOf(standing, column);
        if (completed && rebuiltDisapproved)
            code = standing switch
            {
                ProjectStanding.Candidate or ProjectStanding.Approved => "disapproved-rebuilt",
                ProjectStanding.Rejected => "disapproved-built-anyway",
                _ => code,
            };
        var (headline, family) = MeaningOfCode(code);
        var detail = refusal?.Reason ?? (undecidedNotBuilt.Count switch
        {
            1 => "Your undecided analysis wasn't built",
            > 1 => "Your undecided analyses weren't built",
            _ => string.Empty,
        });
        if (code == "disapproved-rebuilt" && Math.Max(missingApproved.Count, missingApprovedAnalyses.Count) > 0)
            detail = detail.Length == 0 ? "Your Approved analysis wasn't built" :
                detail + "; your Approved analysis wasn't built";
        return new WordComparison(standing, WordRowProjection.OutcomeOf(column), code, headline,
            WordRowProjection.ToneOf(family))
        {
            Detail = detail,
            IsIncomplete = incomplete,
            Availability = identitiesAvailable ? AnalysisComparisonAvailability.Available :
                word.ReadingGrades is not null ? AnalysisComparisonAvailability.RecordedGradesOnly :
                AnalysisComparisonAvailability.Unavailable,
            Readings = readings,
            MissingApproved = missingApproved,
            MissingApprovedAnalyses = missingApprovedAnalyses,
            RebuiltDisapproved = rebuilt,
            ExtraReadingIndices = identitiesAvailable
                ? readings.Where(reading => reading.Matches.Count == 0).Select(reading => reading.Index).ToArray() : [],
            UndecidedNotBuilt = undecidedNotBuilt,
        };
    }

    /// <summary>Captures identity evidence for storage without persisting display wording or tone.</summary>
    public static WordAnalysisComparison? Capture(CompareWordFacts facts)
    {
        var comparison = Compare(facts);
        return comparison.Availability == AnalysisComparisonAvailability.Available
            ? new WordAnalysisComparison(comparison.Readings, comparison.MissingApprovedAnalyses,
                comparison.UndecidedNotBuilt) : null;
    }

    /// <summary>The shared comparison's Matrix cell.</summary>
    public static ComparePlacement Place(CompareWordFacts word) => PlacementOf(Compare(word));

    /// <summary>The Matrix cell of an already-computed comparison.</summary>
    public static ComparePlacement PlacementOf(WordComparison comparison) => new(comparison.Standing,
        comparison.Outcome switch
        {
            WordRowOutcome.Same => CompareColumnKind.Match,
            WordRowOutcome.Different => CompareColumnKind.NoMatch,
            WordRowOutcome.NoParse => CompareColumnKind.NoParse,
            WordRowOutcome.Stopped => CompareColumnKind.Timeout,
            _ => CompareColumnKind.Skipped,
        });

    private static CompareColumnKind PlaceCore(CompareWordFacts word, string standing,
        IReadOnlyList<string> grades, int missedApproved, bool rebuiltDisapproved)
    {
        if (ParserRefusals.Of(word.Morphology, word.Outcome) is not null) return CompareColumnKind.NoParse;
        if (StoppedAtLimit(word.Outcome, word.IsIncomplete, word.Morphology)) return CompareColumnKind.Timeout;
        if (word.Outcome == "unassessed") return CompareColumnKind.Skipped;
        if (word.Outcome != "analysed" || (word.Morphology?.Analyses.Count ?? 0) == 0)
            return CompareColumnKind.NoParse;
        bool Built(string grade) => grades.Contains(grade);
        var matched = standing switch
        {
            ProjectStanding.Approved => Built(ReadingGrade.Approved) && !rebuiltDisapproved,
            ProjectStanding.Candidate => Built(ReadingGrade.Candidate) && !rebuiltDisapproved,
            ProjectStanding.Rejected => Built(ReadingGrade.Disapproved),
            _ => Built(ReadingGrade.Approved) || Built(ReadingGrade.Candidate) || Built(ReadingGrade.Disapproved),
        };
        return matched && missedApproved == 0 && grades.All(grade => grade != ReadingGrade.NoOpinion)
            ? CompareColumnKind.Match : CompareColumnKind.NoMatch;
    }

    /// <summary>Whether the search stopped at a time or step limit, including when it returned partial readings.</summary>
    public static bool StoppedAtLimit(string outcome, bool isIncomplete, ParseWordEvidence? morphology) =>
        isIncomplete || outcome is "timed-out" or "capped" ||
        morphology is { Capped: true } or { TimedOut: true };

    /// <summary>Ranks a named Matrix problem using the command's single rule table.</summary>
    /// <param name="word">The placement facts recorded for this word.</param>
    /// <param name="missedApproved">The approved readings the parser did not produce.</param>
    /// <returns>The ordered presentation data, or <see langword="null"/> when the word is outside the four groups.</returns>
    public static FixFirstPriority? FixFirst(CompareWordFacts word, IReadOnlyList<ParserReading>? missedApproved)
    {
        ArgumentNullException.ThrowIfNull(word);
        var comparison = Compare(word);
        if (comparison.MeaningCode == "refused") return null;
        var placement = PlacementOf(comparison);
        var rule = FixFirstRules.FirstOrDefault(candidate =>
            candidate.Standing == placement.Standing && candidate.Column == placement.Column);
        if (rule is null) return null;

        var explanation = comparison.MeaningCode == "disapproved-rebuilt"
            ? comparison.Headline + (comparison.Detail.Length == 0 ? string.Empty : ". " + comparison.Detail)
            : missedApproved is { Count: > 0 }
            ? ExpectedButNotBuilt(missedApproved)
            : rule.Explanation;
        return new FixFirstPriority(rule.Category, rule.Rank, rule.Label, explanation);
    }

    /// <summary>The default presentation for a Matrix cell; individual words may carry more specific evidence.</summary>
    public static (string Label, CompareFamilyKind Family) MeaningOf(string? standing, CompareColumnKind column) =>
        MeaningOfCode(MeaningCodeOf(standing, column));

    /// <summary>A stable semantic grouping key, independent of the rendered wording.</summary>
    public static string MeaningCodeOf(string? standing, CompareColumnKind column)
    {
        var row = NormalizeStanding(standing);
        if (column == CompareColumnKind.Timeout) return "unknown";
        if (column == CompareColumnKind.Skipped) return "not-parsed";
        return (row, column) switch
        {
            (ProjectStanding.NotPresent, CompareColumnKind.Match) => "no-comparison",
            (ProjectStanding.NotPresent, CompareColumnKind.NoMatch) => "new",
            (ProjectStanding.NotPresent, _) => "nobody",
            (ProjectStanding.Candidate, CompareColumnKind.Match) => "confirmed",
            (ProjectStanding.Candidate, CompareColumnKind.NoMatch) => "undecided-different",
            (ProjectStanding.Candidate, _) => "undecided-not-built",
            (ProjectStanding.Approved, CompareColumnKind.Match) => "kept",
            (ProjectStanding.Approved, CompareColumnKind.NoMatch) => "approved-different",
            (ProjectStanding.Approved, _) => "lost",
            (ProjectStanding.Rejected, CompareColumnKind.Match) => "disapproved-built-anyway",
            (ProjectStanding.Rejected, _) => "disapproved-not-built",
            (_, CompareColumnKind.Match) => "misspelling-built",
            (_, CompareColumnKind.NoMatch) => "over-generates",
            _ => "correct",
        };
    }

    /// <summary>Presentation for a stable comparison meaning; unknown codes are refused.</summary>
    public static (string Label, CompareFamilyKind Family) MeaningOfCode(string code) => code switch
    {
        "unknown" => ("Unknown yet", CompareFamilyKind.Unknown),
        "not-parsed" => ("Not parsed", CompareFamilyKind.Unknown),
        "refused" => (ParserRefusals.Title, CompareFamilyKind.Unknown),
        "no-comparison" => ("Nothing to compare", CompareFamilyKind.None),
        "new" => ("New: PanGloss proposes", CompareFamilyKind.New),
        "nobody" => ("Nobody can analyze", CompareFamilyKind.Nobody),
        "confirmed" => ("PanGloss confirms", CompareFamilyKind.Good),
        "undecided-different" => ("Differs: have a look", CompareFamilyKind.Review),
        "undecided-not-built" => ("Grammar can't build it", CompareFamilyKind.Review),
        "kept" => ("Kept", CompareFamilyKind.Good),
        "approved-different" => ("Built something else", CompareFamilyKind.Violation),
        "lost" => ("Lost", CompareFamilyKind.Violation),
        "disapproved-built-anyway" => ("Built anyway", CompareFamilyKind.Violation),
        "disapproved-rebuilt" => ("Rebuilt an analysis you Disapproved", CompareFamilyKind.Violation),
        "disapproved-not-built" => ("Fine", CompareFamilyKind.Fine),
        "misspelling-built" => ("Builds a misspelling", CompareFamilyKind.Review),
        "over-generates" => ("Over-generates", CompareFamilyKind.Review),
        "correct" => ("Correct", CompareFamilyKind.Fine),
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown comparison meaning."),
    };

    private static string NormalizeStanding(string? standing) => standing switch
    {
        ProjectStanding.Approved => ProjectStanding.Approved,
        ProjectStanding.Candidate => ProjectStanding.Candidate,
        ProjectStanding.Rejected => ProjectStanding.Rejected,
        ProjectStanding.IncorrectSpelling => ProjectStanding.IncorrectSpelling,
        _ => ProjectStanding.NotPresent,
    };

    private static string ExpectedButNotBuilt(IReadOnlyList<ParserReading> readings)
    {
        var first = readings[0];
        var forms = string.Join(" + ", first.Morphs.Select(morph => morph.Form));
        var glosses = string.Join(" + ", first.Morphs.Select(morph => morph.Gloss.Length == 0 ? "?" : morph.Gloss));
        return readings.Count == 1
            ? $"Expected {forms} {glosses}, not built."
            : $"Expected {forms} {glosses}, not built ({readings.Count} approved analyses missed).";
    }

    private sealed record FixFirstRule(
        string Standing, CompareColumnKind Column, FixFirstCategory Category, int Rank, string Label, string Explanation);
}
