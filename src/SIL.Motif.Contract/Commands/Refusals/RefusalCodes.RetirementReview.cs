namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCodes
{
    /// <summary>More than one staged Draft names the same Proposal id.</summary>
    public const string RetirementReviewDraftAmbiguous = "retirement-review.draft-ambiguous";
    /// <summary>No staged Draft has the requested Proposal id.</summary>
    public const string RetirementReviewDraftNotFound = "retirement-review.draft-not-found";
    /// <summary>The staged retirement review could not be read safely.</summary>
    public const string RetirementReviewEvidenceInconsistent = "retirement-review.evidence-inconsistent";
    /// <summary>The Draft Proposal id is not valid.</summary>
    public const string RetirementReviewInvalidDraft = "retirement-review.invalid-draft";
    /// <summary>The latest Dry Run describes an earlier revision of the Draft.</summary>
    public const string RetirementReviewStaleDryRun = "retirement-review.stale-dry-run";
}
