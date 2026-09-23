namespace SIL.Motif.Contract.Responses;

/// <summary>
/// What a FieldWorks project holds for a word form, as the wire value of one of the five standings Motif compares
/// an Assessment against. A form whose analyses have more than one standing takes the best, in FieldWorks' own
/// guessing order: approved, then candidate, then rejected; a spelling FieldWorks marks incorrect overrides them all.
/// </summary>
public static class ProjectStanding
{
    /// <summary>The project holds no analysis of this form.</summary>
    public const string NotPresent = "not-present";

    /// <summary>
    /// The project holds analyses of this form that no person has approved or rejected: FieldWorks' "Analysis
    /// Candidates", whoever produced them.
    /// </summary>
    public const string Candidate = "candidate";

    /// <summary>The project approves at least one analysis of this form.</summary>
    public const string Approved = "approved";

    /// <summary>Every analysis the project holds for this form has been rejected.</summary>
    public const string Rejected = "rejected";

    /// <summary>FieldWorks marks this spelling as incorrect, so it is not a word to analyse.</summary>
    public const string IncorrectSpelling = "incorrect-spelling";
}
