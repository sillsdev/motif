namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCodes
{
    public const string AssessmentStoppedByBudget = "assessment.stopped-by-budget";
    public const string AssessmentCancelled = "assessment.cancelled";
    /// <summary>An analyses aggregate was asked for from an Assessment that has no per-word ParseTime or Correctness rows.</summary>
    public const string AssessmentAggregateUnavailable = "assessment.aggregate-unavailable";
    /// <summary>The exact Baseline an Assessment was made against is no longer available.</summary>
    public const string AssessmentBaselineUnavailable = "assessment.baseline-unavailable";
    /// <summary>Previously selected Assessment evidence is no longer retained.</summary>
    public const string AssessmentExpired = "assessment.expired";
    /// <summary>An Assessment's stored evidence could not be read.</summary>
    public const string AssessmentInvalidEvidence = "assessment.invalid-evidence";
    /// <summary>An Assessment id is not a valid Motif id.</summary>
    public const string AssessmentInvalidId = "assessment.invalid-id";
    /// <summary>The named Assessment is not in the Motif store.</summary>
    public const string AssessmentNotFound = "assessment.not-found";
}
