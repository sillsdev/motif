namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCodes
{
    public const string ApplyAppliedContentMismatch = "apply.applied-content-mismatch";
    public const string ApplyChangeNoLongerFits = "apply.change-no-longer-fits";
    public const string ApplyChangeUncertain = "apply.change-uncertain";
    public const string ApplyChangesChanged = "apply.changes-changed";
    public const string ApplyDrift = "apply.drift";
    /// <summary>The Proposal introduced grammar the parser dropped with only a warning, so it is not ready to apply.</summary>
    public const string ApplyDroppedGrammar = "apply.dropped-grammar";
    public const string ApplyDryRunMissing = "apply.dry-run-missing";
    public const string ApplyAutonomousForce = "apply.autonomous-force";
    public const string ApplyMessageInvalid = "apply.message-invalid";
    public const string ApplyMessageRequired = "apply.message-required";
    public const string ApplyNoteUnavailable = "apply.note-unavailable";
    /// <summary>The save returned but the project file does not hold the change, so nothing was recorded as applied.</summary>
    public const string ApplyNotSaved = "apply.not-saved";
    public const string ApplyPendingChanges = "apply.pending-changes";
    /// <summary>The project has not declared an Autonomous Apply gate.</summary>
    public const string ApplyGateUndeclared = "apply.gate-undeclared";
    /// <summary>The newest Trial has no current green gate for Autonomous Apply.</summary>
    public const string ApplyTrialGate = "apply.trial-gate";
    public const string ApplyNotReady = "apply.not-ready";
    public const string ApplyPreconditionFailed = "apply.precondition-failed";
    public const string ApplyProjectInUse = "apply.project-in-use";
    public const string ApplyReconciliationNeeded = "apply.reconciliation-needed";
    public const string ApplyRegression = "apply.regression";
    public const string ApplyReopenFailed = "apply.reopen-failed";
    /// <summary>Another Apply is already running for this project.</summary>
    public const string ApplyInProgress = "apply.in-progress";
    /// <summary>The Proposal has not been tried, so it cannot be applied.</summary>
    public const string ApplyTrialMissing = "apply.trial-missing";
    /// <summary>The Proposal or the project changed since it was tried.</summary>
    public const string ApplyTrialStale = "apply.trial-stale";
    /// <summary>The Trial receipt Apply was asked to honour is missing, unreadable, altered or not a passing receipt.</summary>
    public const string ApplyReceiptInvalid = "apply.receipt-invalid";
    /// <summary>The Trial receipt names another Proposal revision, Baseline, Trial or gate code than the ones Apply would use now.</summary>
    public const string ApplyReceiptStale = "apply.receipt-stale";
}
