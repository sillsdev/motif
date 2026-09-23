using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Commands.Queries;

/// <summary>
/// The one place a word form's <see cref="ProjectStanding"/> is decided, so Texts and an Assessment never place the
/// same word in different rows.
/// </summary>
public static class ProjectStandings
{
    /// <summary>
    /// Ranks what the project holds for one form: an incorrect spelling first, then any approved analysis, then any
    /// candidate, then rejected analyses, and otherwise nothing. The order after spelling is FieldWorks' own guessing
    /// order, in which a human approval outranks a guess and a guess outranks a rejection.
    /// </summary>
    public static string Of(int approvedCount, int candidateCount, int rejectedCount, bool incorrectSpelling) =>
        incorrectSpelling ? ProjectStanding.IncorrectSpelling
        : approvedCount > 0 ? ProjectStanding.Approved
        : candidateCount > 0 ? ProjectStanding.Candidate
        : rejectedCount > 0 ? ProjectStanding.Rejected
        : ProjectStanding.NotPresent;
}
