namespace SIL.Motif.Contract.Commands;

/// <summary>
/// The <see cref="Refusal.Code"/> values the window reaches, so a caller branches on a name the compiler
/// checks rather than on a string it retypes.
/// </summary>
/// <remarks>
/// This class, <see cref="HostRefusalCodes"/> and the Parsimony codes together are the closed registry of
/// every refusal code, pinned by `RegistryCoversEveryCodeTheSourceUses`: a code the source spells that no
/// constant names fails the suite. Some call sites still spell their code as a literal. Codes shown in the window
/// must appear as a literal or a reference in the commands or the window, pinned by
/// `EveryCatalogueCodeIsOneACommandOrTheWindowDeclares`. The CLI does not read this list; it prints each
/// command's own message.
/// </remarks>
public static class RefusalCodes
{
    public const string HandAnalysisStale = "hand-analysis.stale";
    public const string HandAnalysisAmbiguousWordform = "hand-analysis.ambiguous-wordform";
    public const string HandAnalysisInvalidLink = "hand-analysis.invalid-link";
    public const string HandAnalysisTypeMismatch = "hand-analysis.type-mismatch";
    public const string HandAnalysisDisapproved = "hand-analysis.disapproved";
    public const string HandAnalysisRequestIdReused = "hand-analysis.request-id-reused";
    public const string HandAnalysisBusy = "hand-analysis.busy";
    public const string HandAnalysisStoreUnavailable = "hand-analysis.store-unavailable";
    public const string HandAnalysisOutcomeUnknown = "hand-analysis.outcome-unknown";
    public const string HandAnalysisSnapshotExpired = "hand-analysis.snapshot-expired";
    public const string HandAnalysisIndexUnavailable = "hand-analysis.index-unavailable";
    public const string HandAnalysisInvalidCursor = "hand-analysis.invalid-cursor";
    public const string HandAnalysisInvalidPageLimit = "hand-analysis.invalid-page-limit";
    public const string HandAnalysisSourceUnavailable = "hand-analysis.source-unavailable";
    public const string HandAnalysisInvalidBuffer = "hand-analysis.invalid-buffer";
    public const string HandAnalysisIncompleteReferences = "hand-analysis.incomplete-references";
    public const string HandAnalysisStaleReadSet = "hand-analysis.stale-read-set";
    public const string HandAnalysisReferenced = "hand-analysis.referenced";
    public const string HandAnalysisDeletionPlanMismatch = "hand-analysis.deletion-plan-mismatch";
    public const string HandAnalysisUnsupportedOperation = "hand-analysis.unsupported-operation";
    public const string HandAnalysisIdentityMismatch = "hand-analysis.identity-mismatch";

    /// <summary>The saved Text Progress projection has not been published or needs reconciliation.</summary>
    public const string ProgressIndexNotReady = "progress.index-not-ready";
    /// <summary>A Progress cursor is malformed or belongs to another scope or filter.</summary>
    public const string ProgressInvalidCursor = "progress.invalid-cursor";
    /// <summary>A Progress request has an unsupported scope, detail or page size.</summary>
    public const string ProgressInvalidRequest = "progress.invalid-request";
    /// <summary>A Progress page belongs to a revision that has since changed.</summary>
    public const string ProgressRevisionChanged = "progress.revision-changed";
    /// <summary>A requested Text is absent from the saved Progress projection.</summary>
    public const string ProgressTextNotFound = "progress.text-not-found";
    public const string ActivityLogUnreadable = "activity.log-unreadable";
    public const string ParseAlreadyRunningHere = "parse.already-running-here";
    public const string ParseAlreadyRunning = "parse.already-running";
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
    public const string OperationCascadingDelete = "operation.cascading-delete";
    public const string OperationInvalidDependency = "operation.invalid-dependency";
    public const string ApplyReopenFailed = "apply.reopen-failed";

    public const string AssessBaselineChanged = "assess.baseline-changed";
    public const string AssessCollectEmpty = "assess.collect-empty";
    public const string AssessInvalidLimit = "assess.invalid-limit";
    public const string AssessInvocationInconsistent = "assess.invocation-inconsistent";
    public const string AssessMeasurementsIncomplete = "assess.measurements-incomplete";
    /// <summary>Motif or the parser exhausted memory while assessing, including a parser memory-ceiling stop.</summary>
    public const string AssessOutOfMemory = "assess.out-of-memory";
    public const string AssessParserKilled = "assess.parser-killed";
    public const string AssessParserMemoryLimit = "assess.parser-memory-limit";
    public const string AssessParserUnavailable = "assess.parser-unavailable";
    /// <summary>The Assessment could not provide usable evidence.</summary>
    public const string AssessUnavailable = "assess.unavailable";
    public const string AssessTimingDetailSelection = "assess.timing-detail-selection";
    public const string AssessUnsupportedKind = "assess.unsupported-kind";
    public const string AssessWordsAmbiguous = "assess.words-ambiguous";
    public const string AssessWordsFileEmpty = "assess.words-file-empty";
    public const string AssessWordsMultiWord = "assess.words-multi-word";
    public const string AssessWordsFileNotText = "assess.words-file-not-text";
    public const string AssessWordsFileTooLarge = "assess.words-file-too-large";
    public const string AssessWordsFileBadRow = "assess.words-file-bad-row";
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

    /// <summary>A capped read's offset, limit or page is out of range or contradicts another paging flag.</summary>
    public const string PagingInvalid = "paging.invalid";

    public const string EvidenceInvalidPage = "evidence.invalid-page";
    public const string EvidenceUnknownTier = "evidence.unknown-tier";
    /// <summary>An export cursor is not one an earlier page of the same export returned.</summary>
    public const string EvidenceInvalidCursor = "evidence.invalid-cursor";
    /// <summary>The export file could not be written, so nothing was left at the path.</summary>
    public const string EvidenceOutUnwritable = "evidence.out-unwritable";
    /// <summary>A decision evidence record was staged without its decisionEvidence entry.</summary>
    public const string EvidenceMissing = "evidence.missing";
    /// <summary>A decision evidence record names something that is not an existing wordform or analysis.</summary>
    public const string EvidenceWrongTarget = "evidence.wrong-target";

    /// <summary>An agent evaluation names a built-in agent, such as the default user or parser agent.</summary>
    public const string AgentBuiltIn = "agent.built-in";
    /// <summary>An agent evaluation names an agent that is marked human.</summary>
    public const string AgentHuman = "agent.human";
    /// <summary>An agent evaluation uses the human agent's name for a non-human agent.</summary>
    public const string AgentHumanName = "agent.human-name";
    /// <summary>The identity named for an agent already belongs to something that is not an agent.</summary>
    public const string AgentIdentityConflict = "agent.identity-conflict";
    /// <summary>An agent identity already exists under another name or version.</summary>
    public const string AgentVersionConflict = "agent.version-conflict";

    /// <summary>Two lists share the Grammar Guide name, or two records share an abbreviation.</summary>
    public const string GdrAmbiguous = "guide.ambiguous";
    /// <summary>The exact Baseline file is missing, so no Grammar Guide section can be read.</summary>
    public const string GdrBaselineUnavailable = "guide.baseline-unavailable";
    /// <summary>The current Baseline is not the one the request named.</summary>
    public const string GdrBaselineChanged = "guide.baseline-changed";
    /// <summary>A style in the project is based on itself, so no style can be resolved.</summary>
    public const string GdrStyleCycle = "guide.style-cycle";
    /// <summary>A project path for a link names more than one object.</summary>
    public const string GdrLinkAmbiguous = "guide.link-ambiguous";
    /// <summary>A project path for a link names an object no FieldWorks tool opens.</summary>
    public const string GdrLinkNoTool = "guide.link-no-tool";
    /// <summary>A project path for a link names nothing in the project.</summary>
    public const string GdrLinkNotFound = "guide.link-not-found";
    /// <summary>A link's project path is not a path to an object.</summary>
    public const string GdrLinkPathInvalid = "guide.link-path-invalid";
    /// <summary>A guide show request named no section.</summary>
    public const string GdrEmptyId = "guide.empty-id";
    /// <summary>The project has no Baseline to read the Grammar Guide from.</summary>
    public const string GdrNoBaseline = "guide.no-baseline";
    /// <summary>No Grammar Guide section has that id or number.</summary>
    public const string GdrNotFound = "guide.not-found";
    public const string GdrRecordMoved = "guide.record-moved";
    /// <summary>The Markdown given for a Grammar Guide section cannot be read.</summary>
    public const string GdrMarkdownInvalid = "guide.markdown-invalid";
    /// <summary>A Grammar Guide judgement call is missing its option or its reason.</summary>
    public const string GdrDecisionInvalid = "guide.decision-invalid";

    /// <summary>More than one possibility list is named "Motif Sources", so no Source list can be chosen.</summary>
    public const string SourceListAmbiguous = "source.list-ambiguous";
    /// <summary>The project's Source list has no Source with that id.</summary>
    public const string SourceNotFound = "source.not-found";
    /// <summary>A Source read named no id.</summary>
    public const string SourceIdInvalid = "source.id-invalid";
    /// <summary>A chapter reference is not a book and a chapter number, such as MAT 5.</summary>
    public const string SourceRefInvalid = "source.ref-invalid";
    /// <summary>The folder named for a Source does not exist on this machine.</summary>
    public const string SourceFolderMissing = "source.folder-missing";
    /// <summary>The Source's opened Paratext text has no such chapter, so it cannot be made Current.</summary>
    public const string SourceChapterNotInSource = "source.chapter-not-in-source";
    /// <summary>The id of a new Source is already used by a Source in the list or staged in the pending changes.</summary>
    public const string SourceIdInUse = "source.id-in-use";
    /// <summary>The id of a new Source belongs to a removed Source; restore that Source instead.</summary>
    public const string SourceIdRemoved = "source.id-removed";
    /// <summary>The Context text does not parse in the Source's versification.</summary>
    public const string SourceContextInvalid = "source.context-invalid";
    /// <summary>The versification is not one of the built-in names.</summary>
    public const string SourceVersificationUnknown = "source.versification-unknown";
    /// <summary>The Source kind is modelled but not built.</summary>
    public const string SourceKindUnsupported = "source.kind-unsupported";
    /// <summary>The Source's text has no word at this location, so there is no Occurrence to approve or clear.</summary>
    public const string SourceOccurrenceNotFound = "source.occurrence-not-found";
    /// <summary>The word at this location has no letter, so it is not an Occurrence that can be approved.</summary>
    public const string SourceOccurrenceNotEligible = "source.occurrence-not-eligible";
    /// <summary>The analysis chosen belongs to a different wordform than the word at this location.</summary>
    public const string SourceAnalysisMismatch = "source.analysis-mismatch";
    /// <summary>Approving needs a stored analysis, or a parser reading with its wordform, and the request gave neither or both.</summary>
    public const string SourceAnalysisNeeded = "source.analysis-needed";
    /// <summary>The wordform for a parser reading is not in the project, so its analysis cannot be added.</summary>
    public const string SourceWordformMissing = "source.wordform-missing";
    /// <summary>An AI named the reserved pending-changes Draft for a Source edit; it must use a named Proposal.</summary>
    public const string SourceAiNeedsProposal = "source.ai-needs-proposal";
    /// <summary>The Source has no derived word-place index.</summary>
    public const string SourceIndexMissing = "source.index-missing";
    /// <summary>The Source index is stale or has a refresh pending.</summary>
    public const string SourceIndexStale = "source.index-stale";
    /// <summary>The requested page size is outside the bounded read contract.</summary>
    public const string SourcePassageLimitInvalid = "source.passage-limit-invalid";
    /// <summary>The cursor is malformed or belongs to another Source or Passage.</summary>
    public const string SourcePassageCursorInvalid = "source.passage-cursor-invalid";
    /// <summary>The indexed generation changed since the continuation was issued.</summary>
    public const string SourceGenerationChanged = "source.generation-changed";


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
    /// <summary>The supplied job is outside the wait's project, kind or Proposal.</summary>
    public const string JobWaitIdentityMismatch = "job.wait-identity-mismatch";
    /// <summary>The job kind has no execution lease for a terminal wait to confirm.</summary>
    public const string JobWaitKindUnsupported = "job.wait-kind-unsupported";

    public const string JobWaitCleanupUnconfirmed = "job.wait-cleanup-unconfirmed";
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
    public const string ProposalCommentInvalid = "proposal.comment-invalid";
    public const string ProposalPictureInvalid = "proposal.picture-invalid";
    public const string RequestInvalid = "request.invalid";
    public const string HypothesisInvalid = "hypothesis.invalid";
    public const string HypothesisNotFound = "hypothesis.not-found";
    public const string HypothesisRule = "hypothesis.rule";
    /// <summary>A disposition request names no form, a malformed form, an unknown category, no evidence, or a wrong intended form.</summary>
    public const string DispositionInvalid = "disposition.invalid";
    /// <summary>A word type already stands with another disposition and the write did not ask to replace it.</summary>
    public const string DispositionContradiction = "disposition.contradiction";
    /// <summary>A coverage request names no ledger, sidecar or frequency file, or gives a limit or frequency format outside its range.</summary>
    public const string CoverageInvalidRequest = "coverage.invalid-request";
    /// <summary>A named ledger, sidecar, frequency, audit or protected-set path cannot be read.</summary>
    public const string CoverageFileUnreadable = "coverage.file-unreadable";
    public const string CoverageNotUtf8 = "coverage.not-utf8";
    public const string CoverageEmptyFile = "coverage.empty-file";
    public const string CoverageBadHeader = "coverage.bad-header";
    public const string CoverageWrongFieldCount = "coverage.wrong-field-count";
    public const string CoverageEmptyWord = "coverage.empty-word";
    public const string CoverageEmptyGrammarRevision = "coverage.empty-grammar-revision";
    public const string CoverageBadTokens = "coverage.bad-tokens";
    public const string CoverageBadState = "coverage.bad-state";
    public const string CoverageBadExpectedReadings = "coverage.bad-expected-readings";
    public const string CoverageRowCountMismatch = "coverage.row-count-mismatch";
    public const string CoverageTokenSumMismatch = "coverage.token-sum-mismatch";
    public const string CoverageDigestMismatch = "coverage.digest-mismatch";
    public const string CoverageSidecarMissing = "coverage.sidecar-missing";
    public const string CoverageSidecarInvalidJson = "coverage.sidecar-invalid-json";
    public const string CoverageSidecarDuplicateKey = "coverage.sidecar-duplicate-key";
    public const string CoverageSidecarMissingKey = "coverage.sidecar-missing-key";
    public const string CoverageSidecarInvalidValue = "coverage.sidecar-invalid-value";
    public const string CoverageFrequencyMalformed = "coverage.frequency-malformed";
    public const string CoverageCheckMalformed = "coverage.check-malformed";
    public const string CoverageCheckPinMismatch = "coverage.check-pin-mismatch";
    public const string RequestNoRequest = "request.no-request";
    public const string RequestNotFound = "request.not-found";
    public const string RequestNotOpen = "request.not-open";
    public const string RequestProposalOpen = "request.proposal-open";

    /// <summary>An import plan declines its whole file, so nothing in it was lowered to operations.</summary>
    public const string RecordImportPlanRefused = "record-import.plan-refused";
    /// <summary>No Runner kind covers the verb a record-file change needs on this class and field.</summary>
    public const string RecordImportFieldUncovered = "record-import.field-uncovered";
    /// <summary>A kind covers the field, but no payload shape is authored for this change.</summary>
    public const string RecordImportShapeUnsupported = "record-import.shape-unsupported";
    /// <summary>The project's value is no longer the one the file started from.</summary>
    public const string RecordImportValueStale = "record-import.value-stale";
    /// <summary>The change names an object, list member or text the project cannot take as written.</summary>
    public const string RecordImportNotExecutable = "record-import.not-executable";
    /// <summary>The importer declines this one change (its kind says why); the rest of the file is still planned.</summary>
    public const string RecordImportChangeRefused = "record-import.change-refused";
    /// <summary>The record file cannot be read from the path given.</summary>
    public const string RecordImportFileUnreadable = "record-import.file-unreadable";
    /// <summary>The project changed between the preview and the stage, so what would be staged is no longer what was previewed.</summary>
    public const string RecordImportChangedSincePreview = "record-import.changed-since-preview";

    /// <summary>Strict was asked for without a Draft name, so there is no stage for it to govern.</summary>
    public const string RecordImportStrictNeedsDraft = "record-import.strict-needs-draft";

    /// <summary>Strict mode refused to stage an import in which some change was refused.</summary>
    public const string RecordImportStrictRefused = "record-import.strict-refused";

    /// <summary>Staging was asked for, but the file lowers to no operation, so no Draft was made.</summary>
    public const string RecordImportNothingToStage = "record-import.nothing-to-stage";
    /// <summary>The feature-structure text is not a bracketed list of feature:value pairs.</summary>
    public const string FsNotationSyntax = "fs-notation.syntax";
    /// <summary>The feature-structure text names a feature the project's feature system does not have.</summary>
    public const string FsNotationUnknownFeature = "fs-notation.unknown-feature";
    /// <summary>The feature-structure text gives a feature a value it does not have.</summary>
    public const string FsNotationUnknownValue = "fs-notation.unknown-value";
    /// <summary>More than one feature, or more than one value of a feature, has the label, so it names none.</summary>
    public const string FsNotationAmbiguousLabel = "fs-notation.ambiguous-label";
    /// <summary>The feature-structure text names one feature twice.</summary>
    public const string FsNotationRepeatedFeature = "fs-notation.repeated-feature";
    /// <summary>The feature-structure text names a complex feature, which the notation does not write.</summary>
    public const string FsNotationUnsupportedFeature = "fs-notation.unsupported-feature";
    /// <summary>The project's feature structure holds something the text cannot show, so a change to it cannot be checked.</summary>
    public const string FsNotationUnrenderable = "fs-notation.unrenderable";
    /// <summary>The rule-formula text of an affix process or phonological rule field is shown and an import does not write it.</summary>
    public const string RuleNotationReadOnly = "rule-notation.read-only";
    /// <summary>The rule-formula text is not well formed outside a bracket.</summary>
    public const string RuleNotationSyntax = "rule-notation.syntax";
    /// <summary>A repeat count in the rule-formula text is below 1 for its maximum or below its minimum.</summary>
    public const string RuleNotationBadRepeat = "rule-notation.bad-repeat";
    /// <summary>No phoneme or boundary marker of the first phoneme set has the code the rule-formula text writes.</summary>
    public const string RuleNotationUnknownPhoneme = "rule-notation.unknown-phoneme";
    /// <summary>No natural class has the abbreviation the rule-formula text writes.</summary>
    public const string RuleNotationUnknownNaturalClass = "rule-notation.unknown-natural-class";
    /// <summary>Two phonemes, markers or classes share the code or abbreviation the rule-formula text writes, so it names none.</summary>
    public const string RuleNotationAmbiguousLabel = "rule-notation.ambiguous-label";
    /// <summary>The rule-formula text uses one alpha letter for two features, two letters for one feature, or more than 24 letters.</summary>
    public const string RuleNotationAlphaMismatch = "rule-notation.alpha-mismatch";
    /// <summary>An output $n of the rule-formula text names a column beyond the input as it will stand.</summary>
    public const string RuleNotationIndexOutOfRange = "rule-notation.index-out-of-range";
    /// <summary>An input-only change would leave an output element pointing at a column it removes.</summary>
    public const string RuleNotationDanglingIndex = "rule-notation.dangling-index";
    /// <summary>The rule-formula text holds a construct its field cannot hold.</summary>
    public const string RuleNotationContextNotAllowed = "rule-notation.context-not-allowed";
    /// <summary>The rule-formula text is longer than the model allows: 16 inputs or 256 outputs.</summary>
    public const string RuleNotationLimitExceeded = "rule-notation.limit-exceeded";
    /// <summary>The rule-formula text needs an object the operation catalog cannot create.</summary>
    public const string RuleNotationUnsupportedClass = "rule-notation.unsupported-class";
    /// <summary>The project's rule-formula field holds something the text cannot show, so a change to it cannot be checked.</summary>
    public const string RuleNotationUnrenderable = "rule-notation.unrenderable";
    /// <summary>An object outside the rule refers to a context the rule-formula text would keep, change or delete.</summary>
    public const string RuleNotationSharedClass = "rule-notation.shared-class";
    /// <summary>The rule-formula text would delete a context that has a name or description, which the file cannot show.</summary>
    public const string RuleNotationContextHasData = "rule-notation.context-has-data";
    /// <summary>The page asked for is not a page number from 1.</summary>
    public const string RecordImportInvalidPage = "record-import.invalid-page";
    /// <summary>The request names neither one record nor all records of a kind, or both, or an unusable value.</summary>
    public const string RecordExportInvalidRequest = "record-export.invalid-request";
    /// <summary>The project has no such record, or no record kind of that name.</summary>
    public const string RecordExportNotFound = "record-export.not-found";
    /// <summary>The record file cannot be written at the path given, or a file is already there.</summary>
    public const string RecordExportOutputUnwritable = "record-export.output-unwritable";
    public const string ProposalRationaleEmpty = "proposal.rationale-empty";
    public const string ProposalRationaleFindings = "proposal.rationale-findings";
    public const string ProposalRationaleUnchecked = "proposal.rationale-unchecked";
    public const string ProposalRationaleStale = "proposal.rationale-stale";

    public const string QueryInvalid = "query.invalid";
    /// <summary>The requested grouping exceeds the bounded result size.</summary>
    public const string QueryTooManyGroups = "query.too-many-groups";
    /// <summary>The query asks about a value the Trial facts did not measure.</summary>
    public const string QueryNotMeasured = "query.not-measured";
    /// <summary>A scorer was asked to count in a unit its hits or exceptions are not in.</summary>
    public const string QueryMixedUnits = "query.mixed-units";
    /// <summary>A query's op does not fit the type of the column it names, such as a numeric comparison on text.</summary>
    public const string QueryOpTypeMismatch = "query.op-type-mismatch";
    /// <summary>A query uses a kind the schema reserves for a later release, or a realizes tier that is not built yet.</summary>
    public const string QueryNotYetAvailable = "query.not-yet-available";
    /// <summary><c>not realizes(...)</c> was asked over a variant set that is not complete, so the absence it would claim is not proven.</summary>
    public const string QueryRealizesNegationIncomplete = "query.realizes-negation-incomplete";
    /// <summary><c>realizes(M, attested)</c> found no stored analysis that uses the morpheme to read its forms from.</summary>
    public const string QueryRealizesNoParses = "query.realizes-no-parses";
    /// <summary><c>--twin</c> was given with no <c>form ~</c> or <c>form matches</c> regex to rewrite the matches of.</summary>
    public const string QueryTwinNeedsRegex = "query.twin-needs-regex";
    /// <summary><c>--twin</c> was given with several distinct <c>form ~</c> patterns, or one under <c>or</c>, so the rewrite has no single regex.</summary>
    public const string QueryTwinRegexAmbiguous = "query.twin-regex-ambiguous";
    /// <summary>A <c>--twin</c> template names a group its regex does not have, or is malformed.</summary>
    public const string QueryTwinGroupUnknown = "query.twin-group-unknown";
    /// <summary><c>realizes(M, attested)</c> was given an id that no morph of the parse and no entry of the project carries.</summary>
    public const string QueryRealizesIdUnknown = "query.realizes-id-unknown";
    /// <summary><c>realizes(M, attested)</c> could not build the parse index of the corpus: <c>pangloss batch</c> failed or its analyses file is malformed.</summary>
    public const string QueryRealizesIndexFailed = "query.realizes-index-failed";
    /// <summary><c>realizes</c> ran with no project to resolve the morpheme in; corpus mode names one with <c>--grammar</c>.</summary>
    public const string QueryRealizesNeedsProject = "query.realizes-needs-project";
    /// <summary>A readings query (<c>readings</c>, <c>only-via</c>, <c>--group-by cause</c>, <c>--what-if-retire</c>) ran with no grammar to parse the corpus with.</summary>
    public const string QueryReadingsNeedsGrammar = "query.readings-needs-grammar";
    /// <summary>A readings query named an entry, and only a project's lexicon says which entry owns a morph's analysis.</summary>
    public const string QueryReadingsNeedsProject = "query.readings-needs-project";
    /// <summary><c>only-via</c> or <c>--what-if-retire</c> named an id that no morph of the corpus parse and no entry of the project carries, so it would silently match nothing.</summary>
    public const string QueryRetireIdUnknown = "query.retire-id-unknown";
    /// <summary><c>realizes(M, possible)</c> or <c>generated</c> needs PanGloss to answer the effects or generate call and it could not.</summary>
    public const string QueryRealizesGrammarUnavailable = "query.realizes-grammar-unavailable";
    /// <summary><c>realizes(M, possible)</c> or <c>generated</c> asks PanGloss about one morpheme, so it needs the entry and analysis ids, not one id or a bare form.</summary>
    public const string QueryRealizesNeedsIdentity = "query.realizes-needs-identity";
    /// <summary>A motif.query.v2 document or text expression is malformed or names an unsupported version.</summary>
    public const string QueryDocumentInvalid = "query.document-invalid";
    public const string QueryNoTrialRows = "query.no-trial-rows";
    /// <summary>A Guide query's run line is not one well-formed <c>motif query</c> or <c>motif group</c> command.</summary>
    public const string GuideQueryRunInvalid = "guide-query.run-invalid";
    /// <summary>A Guide query's run line names a command that can change something or is not a query.</summary>
    public const string GuideQueryRunNotReadOnly = "guide-query.run-not-read-only";
    /// <summary>The Baseline or Proposal Trial copy a query was asked to read as of is not available.</summary>
    public const string QueryAsOfUnavailable = "query.as-of-unavailable";
    /// <summary>A corpus word list or segment file named by a corpus query cannot be read or is malformed.</summary>
    public const string QueryCorpusUnreadable = "query.corpus-unreadable";
    /// <summary>A corpus query option is out of range or its pattern is not a valid regular expression.</summary>
    public const string QueryCorpusInvalid = "query.corpus-invalid";
    /// <summary><c>--write-words</c> names a file that exists and <c>--force</c> was not given; nothing was written.</summary>
    public const string QueryWriteWordsExists = "query.write-words-exists";
    /// <summary><c>not morph(...)</c> or <c>not seq(...)</c> was asked over a corpus parse that is not complete, so the absence it would claim is not proven.</summary>
    public const string QuerySeqNegationIncomplete = "query.seq-negation-incomplete";
    /// <summary>An item of <c>morph(...)</c> or <c>seq(...)</c> matches no morph of the corpus parse, so the query would silently answer nothing.</summary>
    public const string QuerySeqItemUnknown = "query.seq-item-unknown";
    /// <summary>A <c>phon</c> or <c>env</c> predicate ran with no grammar to segment words and name natural classes; corpus mode needs <c>--grammar</c>.</summary>
    public const string QueryPhonNeedsGrammar = "query.phon-needs-grammar";
    /// <summary>The installed PanGloss does not declare the <c>match</c> serve op, so <c>phon</c> and <c>env</c> cannot run.</summary>
    public const string QueryPhonUnavailable = "query.phon-unavailable";
    /// <summary>PanGloss refused a <c>phon</c> or <c>env</c> pattern with a typed reason: a parse error, an unknown class or feature, or a construct it does not yet support.</summary>
    public const string QueryPhonPatternRefused = "query.phon-pattern-refused";
    /// <summary>The resident PanGloss failed, timed out or sent a reply Motif cannot read while matching a <c>phon</c> or <c>env</c> pattern.</summary>
    public const string QueryPhonFailed = "query.phon-failed";

    public const string ProjectBackupUnreadable = "project.backup-unreadable";
    public const string ProjectBusy = "project.busy";
    /// <summary>A runner that <c>worker stop</c> ended did not exit within the wait.</summary>
    public const string WorkerStopFailed = "worker.stop-failed";
    public const string ProjectStartupBusy = "project.startup-busy";
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

    public const string RuleAttemptsInvalid = "rule-attempts.invalid";
    public const string RuleAttemptsNoBaseline = "rule-attempts.no-baseline";
    public const string RuleAttemptsPlanInvalid = "rule-attempts.plan-invalid";

    public const string SelectionDefaultMissing = "selection.default-missing";
    public const string SelectionEmpty = "selection.empty";
    public const string SelectionInvalid = "selection.invalid";
    public const string SelectionNotFound = "selection.not-found";
    public const string SelectionParsimonyNameIsDefault = "selection.parsimony-name-is-default";
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

    /// <summary>
    /// The Motif store beside the project is registered to a different project, as when a project is copied
    /// or renamed together with its store. Its facts carry <c>storePath</c> and <c>registeredProjectPath</c>.
    /// </summary>
    public const string StoreOtherProject = "store.other-project";

    /// <summary><c>store relocate</c> was run without the confirmation phrase.</summary>
    public const string StoreRelocateConfirmationRequired = "store.relocate-confirmation-required";

    /// <summary><c>store relocate</c> found no project GUID to show the old and new paths hold one project.</summary>
    public const string StoreRelocateIdentityUnproven = "store.relocate-identity-unproven";

    /// <summary><c>store relocate</c> found no Motif store beside the project.</summary>
    public const string StoreRelocateNoStore = "store.relocate-no-store";

    public const string StoreUnsupported = "store.unsupported";

    public const string TextsWordsCancelled = "texts.words-cancelled";
    public const string TextsEvidenceChanged = "texts.evidence-changed";

    public const string TimingInvalidOverride = "timing.invalid-override";
    public const string TimingNoAssessment = "timing.no-assessment";
    public const string TimingNoBaseline = "timing.no-baseline";
    public const string TimingOverrideNotFound = "timing.override-not-found";
    public const string TimingWordSetNotFound = "timing.word-set-not-found";
    public const string TimingWrongKind = "timing.wrong-kind";

    /// <summary>A continuation belongs to another report, selection, or parent.</summary>
    public const string ReviewCursorInvalid = "review.cursor-invalid";
    /// <summary>The report summary remains available but its requested detail is not retained.</summary>
    public const string ReviewDetailUnavailable = "review.detail-unavailable";

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
    /// <summary>A group request names no ending, start or pattern, or an invalid one.</summary>
    public const string GroupRequestInvalid = "group.request-invalid";
    /// <summary>The Baseline or Proposal Trial a group read was asked to use is not available.</summary>
    public const string GroupAsOfUnavailable = "group.as-of-unavailable";
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

    public const string RejectionTraceRequestInvalid = "rejection-trace.request-invalid";
    public const string RejectionTraceUnsupported = "rejection-trace.unsupported";
    public const string RejectionTraceCaptureRefused = "rejection-trace.capture-refused";
    public const string RejectionTraceMalformed = "rejection-trace.malformed";
    public const string RejectionTraceCaptureLimit = "rejection-trace.capture-limit";
    /// <summary>A why-not request names no word, no candidate, or a candidate PanGloss cannot be given, or is over a cap.</summary>
    public const string WhyNotRequestInvalid = "why-not.request-invalid";
    /// <summary>The installed PanGloss does not declare the why-not capability Motif reads, so it cannot answer.</summary>
    public const string WhyNotParserUnsupported = "why-not.parser-unsupported";
    /// <summary>PanGloss rejected the why-not input, such as an unknown morph id, and said why.</summary>
    public const string WhyNotInputRefused = "why-not.input-refused";
    /// <summary>PanGloss wrote a why-not report Motif cannot read or that does not match the candidates asked.</summary>
    public const string WhyNotMalformed = "why-not.malformed";
    /// <summary>The why-not run ended without a complete report, by the wall-clock limit or an unreadable result.</summary>
    public const string WhyNotUnfinished = "why-not.unfinished";
    /// <summary>A forms-explain request names no morpheme or word, names both, or is over a cap.</summary>
    public const string FormsExplainRequestInvalid = "forms-explain.request-invalid";
    /// <summary>The installed PanGloss does not declare a traced <c>generate</c> serve op, so it cannot generate forms.</summary>
    public const string FormsExplainParserUnsupported = "forms-explain.parser-unsupported";
    /// <summary>A morpheme named in a forms-explain request matches nothing in the project's lexicon.</summary>
    public const string FormsExplainMorphemeUnresolved = "forms-explain.morpheme-unresolved";
    /// <summary>A morpheme named in a forms-explain request matches more than one entry or analysis.</summary>
    public const string FormsExplainMorphemeAmbiguous = "forms-explain.morpheme-ambiguous";
    /// <summary>PanGloss rejected the generate request, such as an unknown entry, and said why.</summary>
    public const string FormsExplainParserRefused = "forms-explain.parser-refused";
    /// <summary>PanGloss wrote a generate answer Motif cannot read.</summary>
    public const string FormsExplainMalformed = "forms-explain.malformed";
    /// <summary>The resident PanGloss failed or timed out before it answered the generate request.</summary>
    public const string FormsExplainUnfinished = "forms-explain.unfinished";
    /// <summary>No analysis of the word could be explained: it has none, or every one has a morph the lexicon cannot name.</summary>
    public const string FormsExplainNoReading = "forms-explain.no-reading";
    /// <summary>A why-lost request names no word or no Proposal id, or a word or cap Motif cannot trace.</summary>
    public const string WhyLostRequestInvalid = "why-lost.request-invalid";
    /// <summary>The Baseline and Trial captures were made with grammar hashes that do not compare, so no diff is read.</summary>
    public const string WhyLostIncomparable = "why-lost.incomparable";
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
    /// <summary>The command is offered only to a caller that declares Autonomous mode.</summary>
    public const string CommandAutonomousRequired = "command.autonomous-required";
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
    /// <summary>A Difference place offset or limit was given without the word whose places they page.</summary>
    public const string DifferenceOccurrenceNeedsWord = "difference.occurrence-needs-word";
    /// <summary>A Difference page offset or limit is out of range.</summary>
    public const string DifferenceInvalidPage = "difference.invalid-page";
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

    /// <summary>A composer search named both a goal and a Subject, or neither.</summary>
    public const string ComposerGoalOrSubject = "composer.goal-or-subject";
    /// <summary>A composer search asked for a level of detail other than concise or detailed.</summary>
    public const string ComposerInvalidDetail = "composer.invalid-detail";
    /// <summary>A composer search named a Subject that no composer serves.</summary>
    public const string ComposerUnknownSubject = "composer.unknown-subject";

    /// <summary>A Draft request or its content is not valid.</summary>
    public const string DraftInvalid = "draft.invalid";

    /// <summary>A compose-batch <c>forEach</c> asked for a <c>targetsFile</c> from a caller that has no batch file folder, such as MCP.</summary>
    public const string DraftForEachTargetsFileUnavailable = "draft.foreach-targets-file-unavailable";
    /// <summary>A compose-batch <c>forEach</c> names no targets, so it would compose nothing.</summary>
    public const string DraftForEachEmpty = "draft.foreach-empty";
    /// <summary>A compose-batch intent uses a <c>{...}</c> placeholder that <c>forEach</c> does not define.</summary>
    public const string DraftForEachUnknownPlaceholder = "draft.foreach-unknown-placeholder";
    /// <summary>A compose-batch intent uses a <c>{target}</c> placeholder but carries no <c>forEach</c>.</summary>
    public const string DraftForEachPlaceholderWithoutForEach = "draft.foreach-placeholder-without-foreach";
    /// <summary>A compose-batch <c>forEach</c> sits inside another <c>forEach</c> entry.</summary>
    public const string DraftForEachNested = "draft.foreach-nested";
    /// <summary>A compose-batch <c>forEach</c> target names no object, or no unique object, in the project.</summary>
    public const string DraftForEachTargetUnresolved = "draft.foreach-target-unresolved";

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
    /// <summary>A named morpheme is not a morphosyntactic analysis of an entry.</summary>
    public const string AdhocProhibitionInvalidMorpheme = "adhoc-prohibition.invalid-morpheme";
    /// <summary>A named allomorph is not a form of an entry.</summary>
    public const string AdhocProhibitionInvalidAllomorph = "adhoc-prohibition.invalid-allomorph";
    /// <summary>The same prohibition already exists in the project or earlier in the Draft.</summary>
    public const string AdhocProhibitionDuplicate = "adhoc-prohibition.duplicate";
    /// <summary>The first morpheme or allomorph also appears in the rest.</summary>
    public const string AdhocProhibitionSelf = "adhoc-prohibition.self";
    /// <summary>The project has no morphology data to own the prohibition.</summary>
    public const string AdhocProhibitionNoMorphologyData = "adhoc-prohibition.no-morphology-data";

    /// <summary>A named left, right or result category is not a part of speech in the project.</summary>
    public const string CompoundRuleInvalidCategory = "compound-rule.invalid-category";
    /// <summary>The same compound rule already exists in the project or earlier in the Draft.</summary>
    public const string CompoundRuleDuplicate = "compound-rule.duplicate";
    /// <summary>The project has no morphology data to own the compound rule.</summary>
    public const string CompoundRuleNoMorphologyData = "compound-rule.no-morphology-data";

    /// <summary>An affix repeat count is not a whole number from 1 to the cap.</summary>
    public const string AffixMaxAppsInvalidCount = "affix-max-apps.invalid-count";
    /// <summary>The id given for an affix repeat count is not an affix's morphosyntactic analysis.</summary>
    public const string AffixMaxAppsNotAffixMsa = "affix-max-apps.not-affix-msa";
    /// <summary>The project's repeat count for the affix is not the one the intent expected.</summary>
    public const string AffixMaxAppsStale = "affix-max-apps.stale";

    /// <summary>The variant reference changed after the link was composed.</summary>
    public const string VariantStaleReference = "variant.stale-reference";

    /// <summary>The form's morph type changed since the edit was composed.</summary>
    public const string MorphTypeStaleSource = "morph-type.stale-source";

    /// <summary>A rich-text field no longer holds the text its edit began from.</summary>
    public const string TextFieldStale = "text.field-stale";
    /// <summary>A rich-text edit names a paragraph or character style the project does not have.</summary>
    public const string TextStyleUnknown = "text.style-unknown";
    /// <summary>A segment's Notes changed since the Note edit was composed.</summary>
    public const string TextNotesStaleSource = "text-notes.stale-source";

    /// <summary>The morph-type edit would leave the supported stem family.</summary>
    public const string MorphTypeIncompatibleFamily = "morph-type.incompatible-family";

    /// <summary>The target is not a stem analysis of a proclitic, enclitic or clitic entry.</summary>
    public const string CliticAttachmentNotClitic = "clitic-attachment.not-clitic";

    /// <summary>A named category is not a part of speech in the project.</summary>
    public const string CliticAttachmentInvalidCategory = "clitic-attachment.invalid-category";

    /// <summary>The clitic's Attaches to Categories changed since the edit was composed.</summary>
    public const string CliticAttachmentStaleSource = "clitic-attachment.stale-source";

    /// <summary>The Draft has authored content that automatic cleanup must preserve.</summary>
    public const string DraftAuthoredContent = "draft.authored-content";
    /// <summary>The Draft's Trial results are still pinned or retained, so it is kept.</summary>
    public const string DraftEvidenceRetained = "draft.evidence-retained";
    /// <summary>A Draft with that name already exists.</summary>
    public const string DraftNameCollision = "draft.name-collision";
    /// <summary>No Draft has that name.</summary>
    public const string DraftNotFound = "draft.not-found";
    /// <summary>A phonology Example Set keeps the Draft's results.</summary>
    public const string DraftRetainedByExampleSet = "draft.retained-by-example-set";
    /// <summary>The pending-changes Draft belongs to the person's window and is not discarded by name from the CLI.</summary>
    public const string DraftDiscardPendingChanges = "draft.discard-pending-changes";
    /// <summary>The Draft is being tried; wait for the try to finish before discarding it.</summary>
    public const string DraftTrialRunning = "draft.trial-running";

    /// <summary>A lexical-entry read named no entry or sense id.</summary>
    public const string LexicalEntryNoId = "lexical-entry.no-id";
    /// <summary>A lexical-entry read named an id that is not a portable id.</summary>
    public const string LexicalEntryInvalidId = "lexical-entry.invalid-id";
    /// <summary>A lexical-entry read named more ids than one call reads.</summary>
    public const string LexicalEntryTooManyIds = "lexical-entry.too-many-ids";
    /// <summary>The current Baseline file is missing, so lexical entries cannot be read.</summary>
    public const string LexicalEntryBaselineUnavailable = "lexical-entry.baseline-unavailable";

    /// <summary>Reading grammar objects needs a Baseline and the project has none.</summary>
    public const string GrammarReadBaselineMissing = "grammar-read.baseline-missing";
    /// <summary>The current Baseline file is missing, so grammar objects cannot be read.</summary>
    public const string GrammarReadBaselineUnavailable = "grammar-read.baseline-unavailable";
    /// <summary>A grammar read request is not valid.</summary>
    public const string GrammarReadInvalidRequest = "grammar-read.invalid-request";

    /// <summary>Reading the lexicon needs a Baseline and the project has none.</summary>
    public const string LexiconFindBaselineMissing = "lexicon-find.baseline-missing";
    /// <summary>The current Baseline file is missing, so the lexicon cannot be read.</summary>
    public const string LexiconFindBaselineUnavailable = "lexicon-find.baseline-unavailable";
    /// <summary>A lexicon find request is not valid: a bad page, regular expression or filter.</summary>
    public const string LexiconFindInvalidRequest = "lexicon-find.invalid-request";

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

    /// <summary>A reference write names an object outside the possibility list FieldWorks offers for the field.</summary>
    public const string ReferenceWrongList = "reference.wrong-list";

    /// <summary>A reference write names an object of a class the field does not refer to.</summary>
    public const string ReferenceWrongClass = "reference.wrong-class";

    /// <summary>A reference write names an object the project does not contain.</summary>
    public const string ReferenceUnknown = "reference.unknown";

    /// <summary>A custom-field operation names a field the target's class does not have.</summary>
    public const string CustomFieldMissing = "custom-field.missing";
    /// <summary>A custom-field operation names a stock field, which its own kinds write.</summary>
    public const string CustomFieldNotCustom = "custom-field.not-custom";
    /// <summary>A custom-field operation's kind does not write the field's property type.</summary>
    public const string CustomFieldWrongType = "custom-field.wrong-type";
    /// <summary>A custom-field reference names an object of a class the field does not refer to.</summary>
    public const string CustomFieldWrongClass = "custom-field.wrong-class";
    /// <summary>A custom-field reference names an object outside the list the field was created over.</summary>
    public const string CustomFieldWrongList = "custom-field.wrong-list";
    /// <summary>A custom-field text names a writing system the field's selector does not allow.</summary>
    public const string CustomFieldWritingSystem = "custom-field.writing-system";

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
    /// <summary>The window could not read a Proposal's saved Difference.</summary>
    public const string WindowDifferenceReadFailed = "window.difference-read-failed";
    /// <summary>A check stopped because the Refresh run before it was cancelled while it waited.</summary>
    public const string WindowRefreshCancelled = "window.refresh-cancelled";
    /// <summary>A check stopped because the Refresh run before it failed or captured no Baseline.</summary>
    public const string WindowRefreshFailed = "window.refresh-failed";
    /// <summary>A check stopped because the Refresh before it could not start while other work held the project.</summary>
    public const string WindowRefreshUnavailable = "window.refresh-unavailable";

    /// <summary>The window could not make a Proposal's Difference.</summary>
    public const string WindowDifferenceMakeFailed = "window.difference-make-failed";
    /// <summary>The window could not run and record a Proposal's Dry Run.</summary>
    public const string WindowDryRunFailed = "window.dry-run-failed";
    /// <summary>The window could not open the chosen project.</summary>
    public const string WindowOpenFailed = "window.open-failed";
    /// <summary>The window could not open a Proposal.</summary>
    public const string WindowOpenProposalFailed = "window.open-proposal-failed";
    /// <summary>The window could not read an open Proposal document's evidence.</summary>
    public const string WindowProposalDocumentReadFailed = "window.proposal-document-read";

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
