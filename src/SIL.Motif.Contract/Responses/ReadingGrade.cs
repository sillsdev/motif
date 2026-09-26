namespace SIL.Motif.Contract.Responses;

/// <summary>
/// The wire value of what a FieldWorks project holds for one parser reading of a word: the grade an Assessment
/// records per reading, and the opinion a stored analysis carries when a change is reviewed. A reading's grade is
/// distinct from its word's <see cref="ProjectStanding"/>: a word can be approved while one of its readings is
/// rejected.
/// </summary>
public static class ReadingGrade
{
    /// <summary>The reading is an analysis the project approves.</summary>
    public const string Approved = "approved";

    /// <summary>
    /// The reading is an analysis the project rejected. The wire keeps FieldWorks' own word for the opinion; the
    /// window calls it rejected.
    /// </summary>
    public const string Disapproved = "disapproved";

    /// <summary>The reading is an analysis the project holds without a human verdict.</summary>
    public const string Candidate = "candidate";

    /// <summary>The project holds nothing like the reading.</summary>
    public const string NoOpinion = "no-opinion";
}
