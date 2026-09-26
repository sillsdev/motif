using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.ViewModels;

/// <summary>
/// The window's word for each <see cref="ReadingGrade"/>, shared by the Analyze texts and Review reading lists so
/// both name one reading's grade alike.
/// </summary>
public static class ReadingGradeLabels
{
    /// <summary>
    /// The window's word for what the project holds when it stores nothing like a reading or a word, the glossary's
    /// "not present". A constant, so a filter can match on it.
    /// </summary>
    public const string NotPresent = "Not present";

    /// <summary>The label for <paramref name="grade"/>, or an empty string for a value that is not a grade.</summary>
    public static string Of(string? grade) => grade switch
    {
        ReadingGrade.Approved => "Approved",
        ReadingGrade.Disapproved => "Rejected",
        ReadingGrade.Candidate => "Candidate",
        ReadingGrade.NoOpinion => NotPresent,
        _ => string.Empty,
    };
}
