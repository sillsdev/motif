namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCodes
{
    /// <summary>The project changed after the Trial ran, so its recorded decisions may differ.</summary>
    public const string DifferenceBaselineChanged = "difference.baseline-changed";
    /// <summary>Making a Difference was cancelled before anything was saved.</summary>
    public const string DifferenceCancelled = "difference.cancelled";
    /// <summary>One side of a Difference holds several different parses for a word.</summary>
    public const string DifferenceDuplicateParseRows = "difference.duplicate-parse-rows";
    /// <summary>None of a Proposal's saved Differences can be read, so each is named and can be made again.</summary>
    public const string DifferenceUnreadable = "difference.unreadable";
    /// <summary>The Proposal is decided, so a new comparison would lose its word detail at once.</summary>
    public const string DifferenceProposalDecided = "difference.proposal-decided";
    /// <summary>A Difference category code is not one the product defines.</summary>
    public const string DifferenceInvalidBucket = "difference.invalid-bucket";
    /// <summary>A Difference limitation code is not one the product defines.</summary>
    public const string DifferenceInvalidLimitation = "difference.invalid-limitation";
    /// <summary>The Proposal id for a Difference is not a Motif id.</summary>
    public const string DifferenceInvalidProposal = "difference.invalid-proposal";
    /// <summary>A Difference decision state is not one the product defines.</summary>
    public const string DifferenceInvalidState = "difference.invalid-state";
    /// <summary>The reference for a Difference is neither human nor source-validated.</summary>
    public const string DifferenceInvalidReference = "difference.invalid-reference";
    /// <summary>The Text id for a Difference is not a GUID.</summary>
    public const string DifferenceInvalidText = "difference.invalid-text";
    /// <summary>The word for a Difference word read is empty.</summary>
    public const string DifferenceInvalidWord = "difference.invalid-word";
    /// <summary>The project has no Baseline to compare decisions against.</summary>
    public const string DifferenceNoBaseline = "difference.no-baseline";
    /// <summary>The Trial has no before-side Assessment to compare with.</summary>
    public const string DifferenceNoBefore = "difference.no-before";
    /// <summary>The Trial recorded no ParseTime Assessment to compare.</summary>
    public const string DifferenceNoParseAssessment = "difference.no-parse-assessment";
    /// <summary>The Proposal has no completed Trial, or none with the requested job id.</summary>
    public const string DifferenceNoTrial = "difference.no-trial";
    /// <summary>No saved Difference matches the request.</summary>
    public const string DifferenceNone = "difference.none";
    /// <summary>A Text or reference was named without the Proposal whose saved Differences it chooses among.</summary>
    public const string DifferenceTextNeedsProposal = "difference.text-needs-proposal";
    /// <summary>The current Baseline has no Text with the requested id.</summary>
    public const string DifferenceTextNotFound = "difference.text-not-found";
    /// <summary>The Trial did not complete, so there is nothing to compare.</summary>
    public const string DifferenceTrialIncomplete = "difference.trial-incomplete";
    /// <summary>Captured decisions hold an opinion that is not approved, disapproved or unknown.</summary>
    public const string DifferenceUnexpectedOpinion = "difference.unexpected-opinion";
}
