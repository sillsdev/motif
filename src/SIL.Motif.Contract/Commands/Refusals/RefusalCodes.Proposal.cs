namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCodes
{
    public const string ProposalClosed = "proposal.closed";
    public const string ProposalCommentInvalid = "proposal.comment-invalid";
    public const string ProposalPictureInvalid = "proposal.picture-invalid";
    public const string ProposalRationaleEmpty = "proposal.rationale-empty";
    public const string ProposalRationaleFindings = "proposal.rationale-findings";
    public const string ProposalRationaleUnchecked = "proposal.rationale-unchecked";
    public const string ProposalRationaleStale = "proposal.rationale-stale";
    /// <summary>A comparison needs two to eight distinct Proposals.</summary>
    public const string ProposalCompareProposalsInvalid = "proposal.compare-proposals-invalid";
    /// <summary>A Proposal has no completed Trial with a retained evidence export to compare.</summary>
    public const string ProposalCompareTrialMissing = "proposal.compare-trial-missing";
    /// <summary>The Trials were measured on different Baselines, so their gains and losses are not comparable.</summary>
    public const string ProposalCompareBaselineMismatch = "proposal.compare-baseline-mismatch";
    /// <summary>A Trial was not measured on the Baseline the comparison was asked to use.</summary>
    public const string ProposalCompareAtMismatch = "proposal.compare-at-mismatch";
    /// <summary>A newer Trial has replaced one the comparison's earlier pages were read from.</summary>
    public const string ProposalCompareSnapshotChanged = "proposal.compare-snapshot-changed";
    /// <summary>The Proposal was already applied.</summary>
    public const string ProposalAlreadyApplied = "proposal.already-applied";
    /// <summary>An applied Proposal and its Receipts cannot be deleted.</summary>
    public const string ProposalAppliedFinal = "proposal.applied-final";
    /// <summary>The Proposal's Assessment results are still pinned, retained or cited, so it is kept with them.</summary>
    public const string ProposalEvidenceRetained = "proposal.evidence-retained";
    /// <summary>The Proposal has an open Draft that must be finalized or discarded first.</summary>
    public const string ProposalHasDraft = "proposal.has-draft";
    /// <summary>A Draft failed Proposal validation.</summary>
    public const string ProposalInconsistent = "proposal.inconsistent";
    /// <summary>A Proposal id is not a Motif id.</summary>
    public const string ProposalInvalidId = "proposal.invalid-id";
    /// <summary>The Proposal cannot move to the requested status from its current one.</summary>
    public const string ProposalInvalidStatus = "proposal.invalid-status";
    /// <summary>Only a declined, superseded or withdrawn Proposal can be deleted.</summary>
    public const string ProposalNotClosed = "proposal.not-closed";
    /// <summary>The Proposal's status no longer allows changing it.</summary>
    public const string ProposalNotEditable = "proposal.not-editable";
    /// <summary>The store holds no Proposal with that id.</summary>
    public const string ProposalNotFound = "proposal.not-found";
    /// <summary>The Proposal is the user's own pending changes and is handled on Review changes.</summary>
    public const string ProposalPendingChanges = "proposal.pending-changes";
    /// <summary>A Phonology Example Set revision still cites the Proposal, so it cannot be deleted.</summary>
    public const string ProposalRetainedByExampleSet = "proposal.retained-by-example-set";
    /// <summary>A split assigns one operation to more than one group.</summary>
    public const string ProposalSplitDuplicateOperation = "proposal.split-duplicate-operation";
    /// <summary>A Proposal was named as its own replacement.</summary>
    public const string ProposalSupersedeSelf = "proposal.supersede-self";
    /// <summary>The Proposal was replaced by another one.</summary>
    public const string ProposalSuperseded = "proposal.superseded";
    /// <summary>A Trial of the Proposal is running, so it cannot be deleted.</summary>
    public const string ProposalTrialRunning = "proposal.trial-running";
    /// <summary>A Proposal list was filtered by a status that does not exist.</summary>
    public const string ProposalUnknownStatus = "proposal.unknown-status";
    /// <summary>The stored content of a Proposal or Draft does not parse as a Proposal, so it was not used.</summary>
    public const string ProposalUnreadable = "proposal.unreadable";
}
