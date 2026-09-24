using SIL.Motif.Contract.Responses;

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
    int MissedApprovedCount);

/// <summary>One Compare cell and its shared product meaning.</summary>
public sealed record ComparePlacement(string Standing, CompareColumnKind Column);

/// <summary>One canonical definition of the Compare matrix's placement and meaning rules.</summary>
public static class CompareSemantics
{
    /// <summary>Places a word using the same all-approved-analyses rule in both the App and Overview.</summary>
    public static ComparePlacement Place(CompareWordFacts word)
    {
        ArgumentNullException.ThrowIfNull(word);
        var standing = NormalizeStanding(word.Standing);
        if (StoppedAtLimit(word.Outcome, word.IsIncomplete, word.Morphology))
            return new ComparePlacement(standing, CompareColumnKind.Timeout);
        if (StringComparer.Ordinal.Equals(word.Outcome, "skipped"))
            return new ComparePlacement(standing, CompareColumnKind.Skipped);
        if (!StringComparer.Ordinal.Equals(word.Outcome, "analysed"))
            return new ComparePlacement(standing, CompareColumnKind.NoParse);

        var grades = word.ReadingGrades ?? Array.Empty<string>();
        bool Built(string grade) => grades.Any(value => StringComparer.Ordinal.Equals(value, grade));
        var matched = standing switch
        {
            ProjectStanding.Approved => Built("approved") && word.MissedApprovedCount == 0,
            ProjectStanding.Candidate => Built("candidate"),
            ProjectStanding.Rejected => Built("disapproved"),
            _ => Built("approved") || Built("candidate") || Built("disapproved"),
        };
        return new ComparePlacement(standing, matched ? CompareColumnKind.Match : CompareColumnKind.NoMatch);
    }

    /// <summary>Whether the search stopped at a time or step limit, including when it returned partial readings.</summary>
    public static bool StoppedAtLimit(string outcome, bool isIncomplete, ParseWordEvidence? morphology) =>
        isIncomplete || outcome is "timed-out" or "capped" ||
        morphology is { Capped: true } or { TimedOut: true };

    /// <summary>Returns the label and category the Compare matrix assigns to one cell.</summary>
    public static (string Label, CompareFamilyKind Family) MeaningOf(string? standing, CompareColumnKind column)
    {
        var row = NormalizeStanding(standing);
        if (column == CompareColumnKind.Timeout) return ("Unknown", CompareFamilyKind.Unknown);
        if (column == CompareColumnKind.Skipped) return ("Not tested", CompareFamilyKind.Unknown);
        return (row, column) switch
        {
            (ProjectStanding.NotPresent, CompareColumnKind.Match) => ("Cannot happen", CompareFamilyKind.None),
            (ProjectStanding.NotPresent, CompareColumnKind.NoMatch) => ("New: the parser proposes", CompareFamilyKind.New),
            (ProjectStanding.NotPresent, _) => ("Nobody can analyse it", CompareFamilyKind.Nobody),
            (ProjectStanding.Candidate, CompareColumnKind.Match) => ("Confirms the candidate", CompareFamilyKind.Good),
            (ProjectStanding.Candidate, CompareColumnKind.NoMatch) => ("Differs: review", CompareFamilyKind.Review),
            (ProjectStanding.Candidate, _) => ("Grammar can't build it", CompareFamilyKind.Review),
            (ProjectStanding.Approved, CompareColumnKind.Match) => ("Kept", CompareFamilyKind.Good),
            (ProjectStanding.Approved, CompareColumnKind.NoMatch) => ("Violation: built other", CompareFamilyKind.Violation),
            (ProjectStanding.Approved, _) => ("Violation: lost", CompareFamilyKind.Violation),
            (ProjectStanding.Rejected, CompareColumnKind.Match) => ("Violation: built anyway", CompareFamilyKind.Violation),
            (ProjectStanding.Rejected, _) => ("Fine", CompareFamilyKind.Fine),
            (_, CompareColumnKind.Match) => ("Builds a misspelling", CompareFamilyKind.Review),
            (_, CompareColumnKind.NoMatch) => ("Over-generates", CompareFamilyKind.Review),
            _ => ("Correct", CompareFamilyKind.Fine),
        };
    }

    private static string NormalizeStanding(string? standing) => standing switch
    {
        ProjectStanding.Approved => ProjectStanding.Approved,
        ProjectStanding.Candidate => ProjectStanding.Candidate,
        ProjectStanding.Rejected => ProjectStanding.Rejected,
        ProjectStanding.IncorrectSpelling => ProjectStanding.IncorrectSpelling,
        _ => ProjectStanding.NotPresent,
    };
}
