namespace SIL.Motif.Contract.Commands;

/// <summary>
/// The <see cref="Refusal.Code"/> values the window reaches, so a caller branches on a name the compiler
/// checks rather than on a string it retypes.
/// </summary>
/// <remarks>
/// Commands still spell their codes as literals, because the command catalogue pins every literal it
/// declares. What keeps the two in step is a test: every constant here must appear as a literal somewhere in
/// the commands or the window, pinned by `EveryCatalogueCodeIsOneACommandOrTheWindowDeclares`. So renaming the
/// last literal of a code fails the test suite, not the build. The test does not tie a code to a particular
/// command: a command that stops using a code another command still declares passes it. The CLI does not
/// read this list; it prints each command's own message.
/// </remarks>
public static class RefusalCodes
{
    public const string ParseAlreadyRunningHere = "parse.already-running-here";
    public const string ParseAlreadyRunning = "parse.already-running";
    public const string ApplyAppliedContentMismatch = "apply.applied-content-mismatch";
    public const string ApplyChangeNoLongerFits = "apply.change-no-longer-fits";
    public const string ApplyChangeUncertain = "apply.change-uncertain";
    public const string ApplyChangesChanged = "apply.changes-changed";
    public const string ApplyDrift = "apply.drift";
    public const string ApplyDryRunMissing = "apply.dry-run-missing";
    public const string ApplyNotReady = "apply.not-ready";
    public const string ApplyProjectInUse = "apply.project-in-use";
    public const string ApplyReconciliationNeeded = "apply.reconciliation-needed";
    public const string ApplyRegression = "apply.regression";
    public const string ApplyReopenFailed = "apply.reopen-failed";

    public const string AssessBaselineChanged = "assess.baseline-changed";
    public const string AssessInvalidLimit = "assess.invalid-limit";
    public const string AssessInvocationInconsistent = "assess.invocation-inconsistent";
    public const string AssessMeasurementsIncomplete = "assess.measurements-incomplete";
    public const string AssessParserUnavailable = "assess.parser-unavailable";
    public const string AssessUnsupportedKind = "assess.unsupported-kind";
    public const string AssessmentCancelled = "assessment.cancelled";

    public const string BaselineBusy = "baseline.busy";
    public const string BaselineCopyUnloadable = "baseline.copy-unloadable";
    public const string BaselineOwnedRootViolation = "baseline.owned-root-violation";
    public const string BaselineSourceIncomplete = "baseline.source-incomplete";
    public const string BaselineTextWordsUnreadable = "baseline.text-words-unreadable";

    public const string ChangeAnalysisIdentityRequired = "change.analysis-identity-required";
    public const string ChangeAnalysisOwnerInvalid = "change.analysis-owner-invalid";
    public const string ChangeAssessmentIncomplete = "change.assessment-incomplete";
    public const string ChangeAssessmentKind = "change.assessment-kind";
    public const string ChangeAssessmentMissing = "change.assessment-missing";
    public const string ChangeAssessmentRequired = "change.assessment-required";
    public const string ChangeAssessmentStale = "change.assessment-stale";
    public const string ChangeAssessmentWordMissing = "change.assessment-word-missing";
    public const string ChangeBaselineMissing = "change.baseline-missing";
    public const string ChangeCannotCompose = "change.cannot-compose";
    public const string ChangeInvalidIdentity = "change.invalid-identity";
    public const string ChangeNoEffect = "change.no-effect";
    public const string ChangeNotFound = "change.not-found";
    public const string ChangeProjectSaving = "change.project-saving";
    public const string ChangeOccurrenceUnavailable = "change.occurrence-unavailable";
    public const string ChangeOccurrenceWordformMismatch = "change.occurrence-wordform-mismatch";
    public const string ChangeReconfirmNotAllowed = "change.reconfirm-not-allowed";
    public const string ChangeReconfirmUnneeded = "change.reconfirm-unneeded";
    public const string ChangeReadingMissing = "change.reading-missing";
    public const string ChangeRefreshRequired = "change.refresh-required";
    public const string ChangeRevisionConflict = "change.revision-conflict";
    public const string ChangeScopeInvalid = "change.scope-invalid";
    public const string ChangeSlotOccupied = "change.slot-occupied";
    public const string ChangeStoredAnalysisMissing = "change.stored-analysis-missing";
    public const string ChangeTextUnavailable = "change.text-unavailable";
    public const string ChangeWordformAmbiguous = "change.wordform-ambiguous";
    public const string ChangeWordformChanged = "change.wordform-changed";
    public const string ChangeWordformMissing = "change.wordform-missing";

    public const string ConfigInvalid = "config.invalid";
    public const string DraftRevisionConflict = "draft.revision-conflict";

    public const string GrammarCheckCancelled = "grammarcheck.cancelled";
    public const string GrammarCheckMalformedFindings = "grammarcheck.malformed-findings";
    public const string GrammarCheckParserRefused = "grammarcheck.parser-refused";
    public const string GrammarCheckParserUnavailable = "grammarcheck.parser-unavailable";
    public const string GrammarCheckTimedOut = "grammarcheck.timed-out";
    public const string GrammarCheckUnsupportedSchema = "grammarcheck.unsupported-schema";

    public const string HandoffCancelled = "handoff.cancelled";
    public const string HandoffDestinationExists = "handoff.destination-exists";
    public const string HandoffInvocationMismatch = "handoff.invocation-mismatch";
    public const string HandoffInvocationNotFound = "handoff.invocation-not-found";
    public const string HandoffInvocationRequired = "handoff.invocation-required";
    public const string HandoffParserUnavailable = "handoff.parser-unavailable";
    public const string HandoffSourceUnavailable = "handoff.source-unavailable";
    public const string HandoffStatisticsUnavailable = "handoff.statistics-unavailable";
    public const string HandoffTextNotFound = "handoff.text-not-found";

    public const string JobWaitCancelled = "job.wait-cancelled";
    public const string JobWaitTimeout = "job.wait-timeout";
    public const string PreflightUnavailable = "preflight.unavailable";

    public const string ProjectBusy = "project.busy";
    public const string ProjectInUse = "project.in-use";
    public const string ProjectInvalid = "project.invalid";
    public const string ProjectNotFound = "project.not-found";
    public const string ProjectOperationIo = "project.operation-io";
    public const string ProjectStoreIo = "project.store-io";
    public const string ProjectUnloadable = "project.unloadable";

    /// <summary>Waiting for the window's one project gate was cancelled before the command started.</summary>
    public const string ProjectWaitCancelled = "project.wait-cancelled";

    public const string ReviewAssessmentNotFound = "review.assessment-not-found";
    public const string ReviewNumbersUnavailable = "review.numbers-unavailable";
    public const string ReviewWrongAssessmentKind = "review.wrong-assessment-kind";

    public const string SelectionDefaultMissing = "selection.default-missing";
    public const string SelectionEmpty = "selection.empty";
    public const string SelectionInvalid = "selection.invalid";
    public const string SelectionInvalidTimeLimit = "selection.invalid-time-limit";
    public const string SelectionTextNotFound = "selection.text-not-found";

    public const string StatsCancelled = "stats.cancelled";
    public const string StatsInvalidEvidence = "stats.invalid-evidence";
    public const string StatsNoAssessment = "stats.no-assessment";
    public const string StatsNoCache = "stats.no-cache";
    public const string StatsNoEvidence = "stats.no-evidence";
    public const string StatsParserRefused = "stats.parser-refused";
    public const string StatsParserUnavailable = "stats.parser-unavailable";
    public const string StatsTimedOut = "stats.timed-out";

    public const string StoreInconsistent = "store.inconsistent";

    /// <summary>
    /// The project's Motif store was made by another version of Motif and is refused rather than migrated.
    /// Its facts carry the store's own path under <c>storePath</c>, so the window can name the file and,
    /// before 1.0, offer to delete it so Motif recreates it.
    /// </summary>
    public const string StoreOtherVersion = "store.other-version";

    public const string StoreUnsupported = "store.unsupported";

    public const string TextsWordsCancelled = "texts.words-cancelled";

    /// <summary>Reading a Text's words failed in the window's own adapter rather than in a command.</summary>
    public const string TextsWordsQueryFailed = "texts.words-query-failed";

    public const string TimingInvalidOverride = "timing.invalid-override";
    public const string TimingNoAssessment = "timing.no-assessment";
    public const string TimingNoBaseline = "timing.no-baseline";
    public const string TimingOverrideNotFound = "timing.override-not-found";
    public const string TimingWordSetNotFound = "timing.word-set-not-found";
    public const string TimingWrongKind = "timing.wrong-kind";

    public const string TrialChangesChanged = "trial.changes-changed";
    public const string TrialMeasurementIncomplete = "trial.measurement-incomplete";
    public const string TrialNothingPending = "trial.nothing-pending";

    public const string WordTraceCancelled = "wordtrace.cancelled";
    public const string WordTraceMalformedDiagnostic = "wordtrace.malformed-diagnostic";
    public const string WordTraceMalformedOutput = "wordtrace.malformed-output";
    public const string WordTraceNoBaseline = "wordtrace.no-baseline";
    public const string WordTraceParserRefused = "wordtrace.parser-refused";
    public const string WordTraceParserUnavailable = "wordtrace.parser-unavailable";
}
