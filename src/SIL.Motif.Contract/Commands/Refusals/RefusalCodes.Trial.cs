namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCodes
{
    public const string TrialChangesChanged = "trial.changes-changed";
    public const string TrialMeasurementIncomplete = "trial.measurement-incomplete";
    /// <summary>The newest Trial identity cannot fit the compact live response.</summary>
    public const string TrialLatestTooLarge = "trial.latest-too-large";
    public const string TrialNothingPending = "trial.nothing-pending";
    /// <summary>The Baseline changed after the Trial's review inputs were captured.</summary>
    public const string TrialReviewBaselineChanged = "trial.review-baseline-changed";
    /// <summary>The exact Trial has no readable retained evidence export.</summary>
    public const string TrialExportUnavailable = "trial.export-unavailable";
    /// <summary>A Reading delta request names no Trial, two Trials, an unknown class or a bad page.</summary>
    public const string TrialDeltaRequestInvalid = "trial.delta-request-invalid";
    /// <summary>Some forms were not measured on both sides or have no complete Reading identity, so no manifest is exact.</summary>
    public const string TrialDeltaIncomplete = "trial.delta-incomplete";
    /// <summary>A manifest file could not be written to the requested directory.</summary>
    public const string TrialDeltaManifestUnwritable = "trial.delta-manifest-unwritable";
    /// <summary>The Trial's frozen review inputs could not be captured or read.</summary>
    public const string TrialReviewInputUnavailable = "trial.review-input-unavailable";
    /// <summary>A Trial without the base sample was asked for with no word list, or with options that contradict it.</summary>
    public const string TrialNoBaseSampleInvalid = "trial.no-base-sample-invalid";
    /// <summary>A working-set Trial measured only a chosen word list, so it cannot gate, apply or be compared with a full Trial.</summary>
    public const string TrialWorkingSetNotAGate = "trial.working-set-not-a-gate";
    /// <summary>Two working-set Trials were measured on different word lists, so they cannot be compared.</summary>
    public const string TrialWorkingSetListsDiffer = "trial.working-set-lists-differ";
    /// <summary>The exact inputs needed for a report-bound diagnostic trace are unavailable.</summary>
    public const string TrialTraceUnavailable = "trial.trace-unavailable";
    /// <summary>The Proposal or its Draft was discarded, deleted or finalized while its Trial was being queued.</summary>
    public const string TrialProposalGone = "trial.proposal-gone";
    /// <summary>A Motif update is being installed, so no Trial can be queued until it finishes.</summary>
    public const string TrialUpdateInProgress = "trial.update-in-progress";
    /// <summary><c>--touched</c> was asked without a word list, or with all words, so there is no list to add the touched words to.</summary>
    public const string TrialTouchedNeedsWords = "trial.touched-needs-words";
    /// <summary><c>--touched</c> found no stored Baseline Assessment matching the current Baseline to read analyses from.</summary>
    public const string TrialTouchedNoBaseline = "trial.touched-no-baseline";
    /// <summary><c>--touched</c> resolved none of the Proposal's edited objects to an allomorph or grammatical info in the Baseline.</summary>
    public const string TrialTouchedNoObjects = "trial.touched-no-objects";
    /// <summary>The Proposal has not been tried yet.</summary>
    public const string TrialNone = "trial.none";
    /// <summary>The Trial's scratch project was removed, so the trace cannot be reopened.</summary>
    public const string TrialScratchGone = "trial.scratch-gone";
    /// <summary>The receipt file could not be written to the requested path.</summary>
    public const string TrialReceiptUnwritable = "trial.receipt-unwritable";
}
