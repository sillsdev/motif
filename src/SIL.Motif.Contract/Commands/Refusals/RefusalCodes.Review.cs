namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCodes
{
    public const string ReviewAssessmentNotFound = "review.assessment-not-found";
    public const string ReviewNumbersUnavailable = "review.numbers-unavailable";
    public const string ReviewWrongAssessmentKind = "review.wrong-assessment-kind";
    /// <summary>The report summary remains available but its requested detail is not retained.</summary>
    public const string ReviewDetailUnavailable = "review.detail-unavailable";
    /// <summary>A review mark lacks an entry, a content digest or an evidence digest.</summary>
    public const string ReviewInvalidMark = "review.invalid-mark";
    /// <summary>A Proposal's changes could not be read as a list.</summary>
    public const string ReviewUnreadable = "review.unreadable";
}
