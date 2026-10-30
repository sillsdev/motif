namespace SIL.Motif.Contract.Commands;

/// <summary>
/// The <see cref="Refusal.Code"/> values the window reaches, so a caller branches on a name the compiler
/// checks rather than on a string it retypes.
/// </summary>
/// <remarks>
/// This class, <see cref="HostRefusalCodes"/> and the Parsimony codes together are the closed registry of
/// every refusal code, pinned by `RegistryCoversEveryCodeTheSourceUses`: a code the source spells that no
/// constant names fails the suite. Some call sites still spell their code as a literal. Every constant here
/// must appear as a literal or a reference in the commands or the window, pinned by
/// `EveryCatalogueCodeIsOneACommandOrTheWindowDeclares`. The CLI does not read this list; it prints each
/// command's own message.
/// </remarks>
public static class RefusalCodes
{
    public const string ActivityLogUnreadable = "activity.log-unreadable";
    public const string ParseAlreadyRunningHere = "parse.already-running-here";
    public const string ParseAlreadyRunning = "parse.already-running";
    public const string ApplyAppliedContentMismatch = "apply.applied-content-mismatch";
    public const string ApplyChangeNoLongerFits = "apply.change-no-longer-fits";
    public const string ApplyChangeUncertain = "apply.change-uncertain";
    public const string ApplyChangesChanged = "apply.changes-changed";
    public const string ApplyDrift = "apply.drift";
    public const string ApplyDryRunMissing = "apply.dry-run-missing";
    public const string ApplyAutonomousForce = "apply.autonomous-force";
    public const string ApplyMessageInvalid = "apply.message-invalid";
    public const string ApplyMessageRequired = "apply.message-required";
    public const string ApplyNoteUnavailable = "apply.note-unavailable";
    public const string ApplyNotReady = "apply.not-ready";
    public const string ApplyPreconditionFailed = "apply.precondition-failed";
    public const string ApplyProjectInUse = "apply.project-in-use";
    public const string ApplyReconciliationNeeded = "apply.reconciliation-needed";
    public const string ApplyRegression = "apply.regression";
    public const string OperationCascadingDelete = "operation.cascading-delete";
    public const string OperationInvalidDependency = "operation.invalid-dependency";
    public const string ApplyReopenFailed = "apply.reopen-failed";

    public const string AssessBaselineChanged = "assess.baseline-changed";
    public const string AssessInvalidLimit = "assess.invalid-limit";
    public const string AssessInvocationInconsistent = "assess.invocation-inconsistent";
    public const string AssessMeasurementsIncomplete = "assess.measurements-incomplete";
    public const string AssessParserMemoryLimit = "assess.parser-memory-limit";
    public const string AssessParserUnavailable = "assess.parser-unavailable";
    public const string AssessTimingDetailSelection = "assess.timing-detail-selection";
    public const string AssessUnsupportedKind = "assess.unsupported-kind";
    public const string AssessWordsAmbiguous = "assess.words-ambiguous";
    public const string AssessWordsFileEmpty = "assess.words-file-empty";
    public const string AssessWordsFileNotText = "assess.words-file-not-text";
    public const string AssessWordsFileTooLarge = "assess.words-file-too-large";
    public const string AssessWordsFileUnreadable = "assess.words-file-unreadable";
    public const string AssessmentStoppedByBudget = "assessment.stopped-by-budget";
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

    public const string EvidenceInvalidPage = "evidence.invalid-page";
    public const string EvidenceUnknownTier = "evidence.unknown-tier";

    public const string GrammarCheckCancelled = "grammarcheck.cancelled";
    public const string GrammarCheckFailed = "grammarcheck.failed";
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

    public const string PhonologyExamplesAssessmentNotFound = "phonology-examples.assessment-not-found";
    public const string PhonologyExamplesBaselineChanged = "phonology-examples.baseline-changed";
    public const string PhonologyExamplesEntryNotFound = "phonology-examples.entry-not-found";
    public const string PhonologyExamplesExampleNotFound = "phonology-examples.example-not-found";
    public const string PhonologyExamplesInvalid = "phonology-examples.invalid";
    public const string PhonologyExamplesNoBaseline = "phonology-examples.no-baseline";
    public const string PhonologyExamplesProposalNotFound = "phonology-examples.proposal-not-found";
    public const string PhonologyExamplesRevisionConflict = "phonology-examples.revision-conflict";
    public const string PhonologyExamplesRevisionNotFound = "phonology-examples.revision-not-found";

    public const string ProposalClosed = "proposal.closed";
    public const string ProposalRationaleEmpty = "proposal.rationale-empty";

    public const string ProjectBackupUnreadable = "project.backup-unreadable";
    public const string ProjectBusy = "project.busy";
    public const string ProjectInUse = "project.in-use";
    public const string ProjectInvalid = "project.invalid";
    public const string ProjectNotFound = "project.not-found";
    public const string ProjectOperationIo = "project.operation-io";
    public const string ProjectStoreIo = "project.store-io";
    public const string ProjectTagInvalid = "project.tag-invalid";
    public const string ProjectTagTaken = "project.tag-taken";
    public const string ProjectTagUnavailable = "project.tag-unavailable";
    public const string ProjectUnloadable = "project.unloadable";

    /// <summary>Waiting for the window's one project gate was cancelled before the command started.</summary>
    public const string ProjectWaitCancelled = "project.wait-cancelled";

    public const string ReviewAssessmentNotFound = "review.assessment-not-found";
    public const string ReviewNumbersUnavailable = "review.numbers-unavailable";
    public const string ReviewWrongAssessmentKind = "review.wrong-assessment-kind";

    public const string SelectionDefaultMissing = "selection.default-missing";
    public const string SelectionEmpty = "selection.empty";
    public const string SelectionInvalid = "selection.invalid";
    public const string SelectionTextNotFound = "selection.text-not-found";

    public const string SelectionReaderCancelled = "selection-reader.cancelled";
    public const string SelectionReaderDisposed = "selection-reader.disposed";
    public const string SelectionReaderBusy = "selection-reader.busy";
    public const string SelectionReaderInvalid = "selection-reader.invalid";
    public const string SelectionReaderNoBaseline = "selection-reader.no-baseline";
    public const string SelectionReaderRangeInvalid = "selection-reader.range-invalid";
    public const string SelectionReaderOwnershipLimit = "selection-reader.ownership-limit";
    public const string SelectionReaderProjectPathRequired = "selection-reader.project-path-required";

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
    public const string TextsEvidenceChanged = "texts.evidence-changed";

    public const string TimingInvalidOverride = "timing.invalid-override";
    public const string TimingNoAssessment = "timing.no-assessment";
    public const string TimingNoBaseline = "timing.no-baseline";
    public const string TimingOverrideNotFound = "timing.override-not-found";
    public const string TimingWordSetNotFound = "timing.word-set-not-found";
    public const string TimingWrongKind = "timing.wrong-kind";

    public const string TrialChangesChanged = "trial.changes-changed";
    public const string TrialMeasurementIncomplete = "trial.measurement-incomplete";
    public const string TrialNothingPending = "trial.nothing-pending";
    /// <summary>A Motif update is being installed, so no Trial can be queued until it finishes.</summary>
    public const string TrialUpdateInProgress = "trial.update-in-progress";

    public const string WordTraceCancelled = "wordtrace.cancelled";
    public const string WordTraceMalformedDiagnostic = "wordtrace.malformed-diagnostic";
    public const string WordTraceMalformedOutput = "wordtrace.malformed-output";
    public const string WordTraceNoBaseline = "wordtrace.no-baseline";
    public const string WordTraceParserRefused = "wordtrace.parser-refused";
    public const string WordTraceParserUnavailable = "wordtrace.parser-unavailable";

    /// <summary>Another Apply is already running for this project.</summary>
    public const string ApplyInProgress = "apply.in-progress";
    /// <summary>The Proposal has not been tried, so it cannot be applied.</summary>
    public const string ApplyTrialMissing = "apply.trial-missing";
    /// <summary>The Proposal or the project changed since it was tried.</summary>
    public const string ApplyTrialStale = "apply.trial-stale";

    /// <summary>A replacement Assessment was asked for without a complete Assessment of this Baseline to replace.</summary>
    public const string AssessInvalidReplacement = "assess.invalid-replacement";

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

    /// <summary>A command that needs a Baseline found none for the project.</summary>
    public const string BaselineMissing = "baseline.missing";
    /// <summary>The command is offered only in Advanced AI mode.</summary>
    public const string CommandAdvancedAiModeRequired = "command.advanced-ai-mode-required";
    /// <summary>The command is offered only when developer commands are enabled.</summary>
    public const string CommandDeveloperCommandsRequired = "command.developer-commands-required";

    /// <summary>A comparison named an Assessment that is not in the store.</summary>
    public const string ComparisonAssessmentNotFound = "comparison.assessment-not-found";
    /// <summary>The two Assessments cannot be compared.</summary>
    public const string ComparisonRefused = "comparison.refused";

    /// <summary>A corpus bundle could not be read.</summary>
    public const string CorpusBundleInvalid = "corpus.bundle-invalid";
    /// <summary>A corpus document could not be added or read.</summary>
    public const string CorpusDocumentInvalid = "corpus.document-invalid";
    /// <summary>The named corpus has no such document.</summary>
    public const string CorpusDocumentNotFound = "corpus.document-not-found";
    /// <summary>A corpus request is not valid.</summary>
    public const string CorpusInvalid = "corpus.invalid";
    /// <summary>The store holds no corpus with that id.</summary>
    public const string CorpusNotFound = "corpus.not-found";

    /// <summary>The exact Baseline file behind the current evidence is missing.</summary>
    public const string CurrentEvidenceBaselineUnavailable = "current-evidence.baseline-unavailable";

    /// <summary>The project changed after the Trial ran, so its recorded decisions may differ.</summary>
    public const string DifferenceBaselineChanged = "difference.baseline-changed";
    /// <summary>One side of a Difference holds several different parses for a word.</summary>
    public const string DifferenceDuplicateParseRows = "difference.duplicate-parse-rows";
    /// <summary>A Difference category code is not one the product defines.</summary>
    public const string DifferenceInvalidBucket = "difference.invalid-bucket";
    /// <summary>A Difference page offset or limit is out of range.</summary>
    public const string DifferenceInvalidPage = "difference.invalid-page";
    /// <summary>The Proposal id for a Difference is not a Motif id.</summary>
    public const string DifferenceInvalidProposal = "difference.invalid-proposal";
    /// <summary>A Difference decision state is not one the product defines.</summary>
    public const string DifferenceInvalidState = "difference.invalid-state";
    /// <summary>The Text id for a Difference is not a GUID.</summary>
    public const string DifferenceInvalidText = "difference.invalid-text";
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
    /// <summary>The current Baseline has no Text with the requested id.</summary>
    public const string DifferenceTextNotFound = "difference.text-not-found";
    /// <summary>The Trial did not complete, so there is nothing to compare.</summary>
    public const string DifferenceTrialIncomplete = "difference.trial-incomplete";
    /// <summary>Captured decisions hold an opinion that is not approved, disapproved or unknown.</summary>
    public const string DifferenceUnexpectedOpinion = "difference.unexpected-opinion";

    /// <summary>A composer search named both a goal and a Subject, or neither.</summary>
    public const string ComposerGoalOrSubject = "composer.goal-or-subject";
    /// <summary>A composer search asked for a level of detail other than concise or detailed.</summary>
    public const string ComposerInvalidDetail = "composer.invalid-detail";
    /// <summary>A composer search named a Subject that no composer serves.</summary>
    public const string ComposerUnknownSubject = "composer.unknown-subject";

    /// <summary>A Draft request or its content is not valid.</summary>
    public const string DraftInvalid = "draft.invalid";

    /// <summary>A part-of-speech-only sense analysis needs a linked variant.</summary>
    public const string VariantComponentRequired = "draft.variant-component-required";

    /// <summary>An analysis contains information the empty-sense deletion cannot safely remove.</summary>
    public const string SenseAnalysisContent = "draft.sense-analysis-content";
    /// <summary>The selected object is not a variant reference owned by an entry.</summary>
    public const string VariantInvalidReference = "variant.invalid-reference";
    /// <summary>The selected component is not an entry or sense.</summary>
    public const string VariantInvalidComponent = "variant.invalid-component";
    /// <summary>The selected type is not a variant type.</summary>
    public const string VariantInvalidType = "variant.invalid-type";
    /// <summary>A variant cannot refer to its own entry or sense.</summary>
    public const string VariantSelfLink = "variant.self-link";
    /// <summary>The link would create a cycle of variants.</summary>
    public const string VariantCycle = "variant.cycle";
    /// <summary>The entry to make a variant is not an entry.</summary>
    public const string VariantInvalidEntry = "variant.invalid-entry";
    /// <summary>The entry already has a variant reference to that component.</summary>
    public const string VariantAlreadyLinked = "variant.already-linked";
    /// <summary>The variant reference changed after the link was composed.</summary>
    public const string VariantStaleReference = "variant.stale-reference";

    /// <summary>The form's morph type changed since the edit was composed.</summary>
    public const string MorphTypeStaleSource = "morph-type.stale-source";

    /// <summary>The morph-type edit would leave the supported stem family.</summary>
    public const string MorphTypeIncompatibleFamily = "morph-type.incompatible-family";

    /// <summary>The target is not a stem analysis of a proclitic, enclitic or clitic entry.</summary>
    public const string CliticAttachmentNotClitic = "clitic-attachment.not-clitic";

    /// <summary>A named category is not a part of speech in the project.</summary>
    public const string CliticAttachmentInvalidCategory = "clitic-attachment.invalid-category";

    /// <summary>The clitic's Attaches to Categories changed since the edit was composed.</summary>
    public const string CliticAttachmentStaleSource = "clitic-attachment.stale-source";

    /// <summary>A Draft with that name already exists.</summary>
    public const string DraftNameCollision = "draft.name-collision";
    /// <summary>No Draft has that name.</summary>
    public const string DraftNotFound = "draft.not-found";

    /// <summary>Reading grammar objects needs a Baseline and the project has none.</summary>
    public const string GrammarReadBaselineMissing = "grammar-read.baseline-missing";
    /// <summary>The current Baseline file is missing, so grammar objects cannot be read.</summary>
    public const string GrammarReadBaselineUnavailable = "grammar-read.baseline-unavailable";
    /// <summary>A grammar read request is not valid.</summary>
    public const string GrammarReadInvalidRequest = "grammar-read.invalid-request";

    /// <summary>No grammar check could run, so there are no findings to report.</summary>
    public const string GrammarCheckNotRun = "grammarcheck.not-run";

    /// <summary>The selected trace does not record the Baseline that produced it.</summary>
    public const string HandoffTraceBaselineUnavailable = "handoff.trace-baseline-unavailable";
    /// <summary>A trace Handoff does not hold only that trace and its captured Baseline.</summary>
    public const string HandoffTraceUnavailable = "handoff.trace-unavailable";

    /// <summary>The project has no analysis writing system to initialize with.</summary>
    public const string InitializationAnalysisWritingSystemRequired = "initialization.analysis-writing-system-required";
    /// <summary>Initialization was cancelled before the field definition changed.</summary>
    public const string InitializationCancelled = "initialization.cancelled";
    /// <summary>Initialization was asked for without the confirmation phrase.</summary>
    public const string InitializationConfirmationRequired = "initialization.confirmation-required";
    /// <summary>Initialization did not save, so the project is unchanged.</summary>
    public const string InitializationNotSaved = "initialization.not-saved";
    /// <summary>Initialization may or may not have saved; the project needs inspection.</summary>
    public const string InitializationOutcomeUnknown = "initialization.outcome-unknown";
    /// <summary>A private recovery copy could not be made, so the project was not changed.</summary>
    public const string InitializationRecoveryCopyFailed = "initialization.recovery-copy-failed";

    /// <summary>An inspect request named no subject.</summary>
    public const string InspectInvalidRequest = "inspect.invalid-request";

    /// <summary>The job already finished, so there is nothing to cancel.</summary>
    public const string JobAlreadyFinished = "job.already-finished";
    /// <summary>A job recorded an Assessment that is missing from the project store.</summary>
    public const string JobAssessmentsInconsistent = "job.assessments-inconsistent";
    /// <summary>The Dry Run job finished without completing.</summary>
    public const string JobDryRunIncomplete = "job.dry-run-incomplete";
    /// <summary>A job id is missing or not valid.</summary>
    public const string JobInvalidId = "job.invalid-id";
    /// <summary>A job was asked to move before itself.</summary>
    public const string JobInvalidMove = "job.invalid-move";
    /// <summary>Trial words are blank or carry surrounding whitespace.</summary>
    public const string JobInvalidWords = "job.invalid-words";
    /// <summary>The job to move before is not an active job.</summary>
    public const string JobMoveTargetNotFound = "job.move-target-not-found";
    /// <summary>The parser a queued job was pinned to can no longer be started, so the job was failed.</summary>
    public const string JobParserUnavailable = "job.parser-unavailable";
    /// <summary>The job recorded no Assessments.</summary>
    public const string JobNoAssessments = "job.no-assessments";
    /// <summary>The job is still running, so it cannot be requeued.</summary>
    public const string JobNotFinished = "job.not-finished";
    /// <summary>The project records no job with that id.</summary>
    public const string JobNotFound = "job.not-found";

    /// <summary>The project has several definitions of the reserved judgment field.</summary>
    public const string JudgmentFieldAmbiguous = "judgment.field-ambiguous";
    /// <summary>A reserved judgment field has an incompatible schema.</summary>
    public const string JudgmentFieldIncompatible = "judgment.field-incompatible";
    /// <summary>The project holds judgments in an old format that Motif no longer reads.</summary>
    public const string JudgmentOldFormat = "judgment.old-format";

    /// <summary>The machine store of Known projects could not be read consistently.</summary>
    public const string MachineStoreInconsistent = "machine-store.inconsistent";

    /// <summary>An operation id is missing or not valid.</summary>
    public const string OperationInvalidId = "operation.invalid-id";
    /// <summary>An operation target is not a valid canonical id.</summary>
    public const string OperationInvalidTarget = "operation.invalid-target";
    /// <summary>An operation's writing system is empty.</summary>
    public const string OperationInvalidWritingSystem = "operation.invalid-writing-system";

    /// <summary>The saved user preferences could not be used.</summary>
    public const string PreferencesRefused = "preferences.refused";
    /// <summary>Saving user preferences failed, so choices last only until Motif closes.</summary>
    public const string PreferencesUnavailable = "preferences.unavailable";

    /// <summary>A project already exists at the chosen name and folder.</summary>
    public const string ProjectCreateExists = "project-create.exists";
    /// <summary>LibLCM could not create the project; what Motif wrote was removed.</summary>
    public const string ProjectCreateFailed = "project-create.failed";
    /// <summary>The parent folder for a new project does not exist.</summary>
    public const string ProjectCreateFolderNotFound = "project-create.folder-not-found";
    /// <summary>The parent folder for a new project is not a usable path.</summary>
    public const string ProjectCreateInvalidFolder = "project-create.invalid-folder";
    /// <summary>A new project's name is not allowed.</summary>
    public const string ProjectCreateInvalidName = "project-create.invalid-name";
    /// <summary>A word-forming character is not a single visible character.</summary>
    public const string ProjectCreateInvalidWordForming = "project-create.invalid-word-forming";
    /// <summary>A new project's writing system tag is not a valid BCP 47 tag.</summary>
    public const string ProjectCreateInvalidWritingSystem = "project-create.invalid-writing-system";

    /// <summary>A project-level refusal with no more specific code.</summary>
    public const string ProjectRefused = "project.refused";
    /// <summary>FieldWorks is saving the project, so the command was refused as busy.</summary>
    public const string ProjectSaving = "project.saving";
    /// <summary>The project's saved data could not be read.</summary>
    public const string ProjectUnreadable = "project.unreadable";
    /// <summary>The project lacks a writing system the command needs.</summary>
    public const string ProjectWritingSystemMissing = "project.writing-system-missing";

    /// <summary>The Proposal was already applied.</summary>
    public const string ProposalAlreadyApplied = "proposal.already-applied";
    /// <summary>An applied Proposal and its Receipts cannot be deleted.</summary>
    public const string ProposalAppliedFinal = "proposal.applied-final";
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

    /// <summary>A report named an Assessment that is not in the store.</summary>
    public const string ReportAssessmentNotFound = "report.assessment-not-found";
    /// <summary>A report kind is not one the product defines.</summary>
    public const string ReportInvalidKind = "report.invalid-kind";
    /// <summary>A report could not be produced from the named Assessment.</summary>
    public const string ReportRefused = "report.refused";

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

    /// <summary>A review mark lacks an entry, a content digest or an evidence digest.</summary>
    public const string ReviewInvalidMark = "review.invalid-mark";
    /// <summary>A Proposal's changes could not be read as a list.</summary>
    public const string ReviewUnreadable = "review.unreadable";

    /// <summary>The window could not open the Selection reader.</summary>
    public const string SelectionReaderOpenFailed = "selection-reader.open-failed";
    /// <summary>The saved texts changed under the reader since it was opened.</summary>
    public const string SelectionReaderPresentationChanged = "selection-reader.presentation-changed";

    /// <summary>A Selection limit policy is missing or not valid.</summary>
    public const string SelectionInvalidLimits = "selection.invalid-limits";
    /// <summary>The retry source is not a Baseline ParseTime Assessment.</summary>
    public const string SelectionRetrySourceInvalid = "selection.retry-source-invalid";
    /// <summary>The retry source Assessment belongs to a different Baseline.</summary>
    public const string SelectionRetrySourceMismatch = "selection.retry-source-mismatch";
    /// <summary>The retry source Assessment is not in the store.</summary>
    public const string SelectionRetrySourceNotFound = "selection.retry-source-not-found";
    /// <summary>The retry source Assessment belongs to a different project.</summary>
    public const string SelectionRetrySourceProjectMismatch = "selection.retry-source-project-mismatch";
    /// <summary>A retry Selection was asked for without a source Assessment id.</summary>
    public const string SelectionRetrySourceRequired = "selection.retry-source-required";
    /// <summary>A source Assessment id was given without a retry kind.</summary>
    public const string SelectionRetrySourceWithoutRetry = "selection.retry-source-without-retry";
    /// <summary>The retry parser-work threshold is invalid.</summary>
    public const string SelectionRetryWorkInvalid = "selection.retry-work-invalid";
    /// <summary>The saved Selection changed since the window last read it.</summary>
    public const string SelectionRevisionConflict = "selection.revision-conflict";

    /// <summary>Forwarded arguments already name a format that the JSON request would override.</summary>
    public const string StatsFormatConflict = "stats.format-conflict";
    /// <summary>The named Assessment is not part of the Proposal's Trial.</summary>
    public const string StatsWrongAssessment = "stats.wrong-assessment";
    /// <summary>The named Assessment did not collect per-object statistics.</summary>
    public const string StatsWrongKind = "stats.wrong-kind";

    /// <summary>Words from different saved Selections were marked together.</summary>
    public const string TextsMixedEvidence = "texts.mixed-evidence";
    /// <summary>The exact source occurrence is absent from the Baseline.</summary>
    public const string TextsOccurrenceNotFound = "texts.occurrence-not-found";

    /// <summary>A timing request lacks a valid grouping or a positive top count.</summary>
    public const string TimingInvalidRequest = "timing.invalid-request";
    /// <summary>A timing word set is not valid.</summary>
    public const string TimingInvalidWordSet = "timing.invalid-word-set";
    /// <summary>The named Assessment is not part of the Proposal's Trial.</summary>
    public const string TimingWrongAssessment = "timing.wrong-assessment";

    /// <summary>The Proposal has not been tried yet.</summary>
    public const string TrialNone = "trial.none";
    /// <summary>The Trial's scratch project was removed, so the trace cannot be reopened.</summary>
    public const string TrialScratchGone = "trial.scratch-gone";

    /// <summary>An uses request names nothing to look up.</summary>
    public const string UsesInvalidRequest = "uses.invalid-request";
    /// <summary>No stored Assessment matches the current Baseline and default Selection.</summary>
    public const string UsesNoAssessment = "uses.no-assessment";

    /// <summary>A window check found a problem the user must fix before continuing.</summary>
    public const string WindowCheck = "window.check";
    /// <summary>A diagnostic could not be saved or copied.</summary>
    public const string WindowDiagnosticNotWritten = "window.diagnostic-not-written";
    /// <summary>A saved diagnostic file could not be read at all.</summary>
    public const string WindowDiagnosticUnreadable = "window.diagnostic-unreadable";
    /// <summary>The action needs an open project and none is open.</summary>
    public const string WindowNoProject = "window.no-project";
    /// <summary>The window could not open the chosen project.</summary>
    public const string WindowOpenFailed = "window.open-failed";
    /// <summary>The window could not open a Proposal.</summary>
    public const string WindowOpenProposalFailed = "window.open-proposal-failed";

    /// <summary>The Baseline changed before the word was read.</summary>
    public const string WordContextBaselineChanged = "word-context.baseline-changed";
    /// <summary>The exact Baseline file is missing, so the word's analyses cannot be read.</summary>
    public const string WordContextBaselineUnavailable = "word-context.baseline-unavailable";
    /// <summary>A word-context request was empty.</summary>
    public const string WordContextEmptyWord = "word-context.empty-word";

    /// <summary>Reading a word's state was cancelled.</summary>
    public const string WordReadStateCancelled = "word.read-state-cancelled";
    /// <summary>A word read-state request is not valid.</summary>
    public const string WordReadStateInvalid = "word.read-state-invalid";

    /// <summary>A saved trace file could not be read.</summary>
    public const string WordTraceDiagnosticUnreadable = "wordtrace.diagnostic-unreadable";
}
