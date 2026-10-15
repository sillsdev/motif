using System.Collections.Generic;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Handoff;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Commands.Catalog;

/// <summary>
/// The sole enumeration of Motif's command handlers (ADR 0043 decision 3): one entry per public
/// handler in <see cref="ProposalCommands"/>, <see cref="PendingChanges"/>,
/// <see cref="CorpusCommands"/>, <see cref="ConfigCommands"/>,
/// <see cref="ReportCommands"/>, <see cref="CompareCommands"/>, <see cref="ReviewNumbersCommand"/>,
/// <see cref="BaselineCaptureCommand"/>,
/// <see cref="Assess.AssessCommand"/>, <see cref="Assess.StatsCommand"/>, <see cref="SelectionCommands"/>,
/// <see cref="ProjectSetupCommands"/>, <see cref="Queries.TextInventoryQuery"/>, <see cref="OverviewCommand"/>,
/// <see cref="TimingCommand"/>, <see cref="Queries.GrammarCheckQuery"/>, <see cref="Queries.GrammarReadQuery"/>,
/// <see cref="HandoffCommand"/>,
/// <see cref="JobCommands"/>, <see cref="PendingChangesWorkflow"/>, <see cref="ReadStateCommands"/>,
/// <see cref="PhonologyExampleCommands"/>,
/// <see cref="Queries.WordContextQuery"/>, <see cref="Queries.WordTraceQuery"/>, <see cref="Queries.ObjectUsesQuery"/>, and <see cref="Queries.InspectQuery"/>.
/// The project setup group reaches <see cref="ProjectCreateCommand"/> and <see cref="ProjectInitializationCommand"/>.
/// </summary>
/// <remarks>
/// The report list reaches <see cref="ReportCommands.ListKinds"/>, waited Dry Runs reach
/// <see cref="JobCommands.WaitForDryRun"/>, waited Trials reach <see cref="JobCommands.WaitForJob"/>,
/// pending Trials reach <see cref="PendingChangesWorkflow.Measure"/>, every Apply verb reaches the one Apply path
/// in <see cref="PendingChangesWorkflow"/>, and a saved trace reaches <see cref="Queries.WordTraceQuery.Load"/>.
/// Each has its own entry. Every other name is the literal CLI
/// verb (or its complete space-separated nested form, such as <c>"jobs show"</c>) that reaches its one named
/// handler, pinned equal to <c>CliVerbCatalog.All</c> by
/// <c>CommandCatalogParityTests.EveryCataloguedCommandHasExactlyOneCliVerb</c>.
/// </remarks>
public static class CommandCatalog
{
    public static IReadOnlyList<CommandDescriptor> All { get; } = new[]
    {
        // ProposalCommands
        new CommandDescriptor("open", typeof(OpenRequest), typeof(ProjectSummaryProjection), CommandSurface.Released, AgentClass.Read),
        new CommandDescriptor("analyses", typeof(ManualAnalysesRequest), typeof(AnalysisAggregateProjection), CommandSurface.Released, AgentClass.Read),
        new CommandDescriptor("new", typeof(NewDraftRequest), typeof(DraftCreatedResponse), CommandSurface.AdvancedAi, AgentClass.Draft),
        new CommandDescriptor("pending-changes", typeof(PendingChangesRequest),
            typeof(PendingChangesSnapshot), CommandSurface.Developer, AgentClass.Read),
        new CommandDescriptor("put-pending-change", typeof(PutPendingChangeRequest),
            typeof(PendingChangesSnapshot), CommandSurface.Developer, AgentClass.HumanOnly),
        new CommandDescriptor("remove-analysis", typeof(RemoveAnalysisRequest),
            typeof(PendingChangesSnapshot), CommandSurface.Developer, AgentClass.HumanOnly),
        new CommandDescriptor("accept-new-set", typeof(AcceptNewSetRequest),
            typeof(PendingChangesSnapshot), CommandSurface.Developer, AgentClass.HumanOnly),
        new CommandDescriptor("remove-pending-change", typeof(RemovePendingChangeRequest),
            typeof(PendingChangesSnapshot), CommandSurface.Developer, AgentClass.HumanOnly),
        new CommandDescriptor("recheck-pending-changes", typeof(RecheckPendingChangesRequest),
            typeof(PendingChangesSnapshot), CommandSurface.Developer, AgentClass.HumanOnly),
        new CommandDescriptor("reconfirm-pending-change", typeof(ReconfirmPendingChangeRequest),
            typeof(PendingChangesSnapshot), CommandSurface.Developer, AgentClass.HumanOnly),
        new CommandDescriptor("apply --all-pending", typeof(ApplyPendingRequest),
            typeof(ApplyPendingResult), CommandSurface.Released, AgentClass.HumanOnly),
        new CommandDescriptor("trial --pending", typeof(MeasurePendingRequest),
            typeof(MeasurePendingResult), CommandSurface.Developer, AgentClass.HumanOnly),
        new CommandDescriptor("review-numbers", typeof(ReviewNumbersCommand.Request),
            typeof(ReviewNumbersResponse), CommandSurface.Developer, AgentClass.Read),
        new CommandDescriptor("add-set-gloss", typeof(AddSetGlossRequest), typeof(SetGlossAddedResponse), CommandSurface.AdvancedAi, AgentClass.Draft),
        new CommandDescriptor(
            "add-delete-lexeme-form", typeof(AddDeleteLexemeFormRequest), typeof(DeleteLexemeFormAddedResponse), CommandSurface.Developer, AgentClass.Draft),
        new CommandDescriptor(
            "compose-author-lexeme-form", typeof(ComposeAuthorLexemeFormRequest),
            typeof(ComposedOperationsResponse), CommandSurface.AdvancedAi, AgentClass.Draft),
        new CommandDescriptor(
            "compose-author-feature-structure", typeof(ComposeAuthorFeatureStructureRequest),
            typeof(ComposedOperationsResponse), CommandSurface.AdvancedAi, AgentClass.Draft),
        new CommandDescriptor("compose-author-feature-value", typeof(ComposeAuthorFeatureValueRequest),
            typeof(ComposedOperationsResponse), CommandSurface.AdvancedAi, AgentClass.Draft),
        new CommandDescriptor("compose-author-phoneme", typeof(ComposeAuthorPhonemeRequest),
            typeof(ComposedOperationsResponse), CommandSurface.AdvancedAi, AgentClass.Draft),
        new CommandDescriptor("compose-author-natural-class", typeof(ComposeAuthorNaturalClassRequest),
            typeof(ComposedOperationsResponse), CommandSurface.AdvancedAi, AgentClass.Draft),
        new CommandDescriptor("compose-edit-natural-class", typeof(ComposeEditNaturalClassRequest),
            typeof(ComposedOperationsResponse), CommandSurface.AdvancedAi, AgentClass.Draft),
        new CommandDescriptor("compose-relink-natural-class", typeof(ComposeRelinkNaturalClassRequest),
            typeof(ComposedOperationsResponse), CommandSurface.AdvancedAi, AgentClass.Draft),
        new CommandDescriptor("compose-author-environment", typeof(ComposeAuthorEnvironmentRequest),
            typeof(ComposedOperationsResponse), CommandSurface.AdvancedAi, AgentClass.Draft),
        new CommandDescriptor("compose-author-phonological-rule", typeof(ComposeAuthorPhonologicalRuleRequest),
            typeof(ComposedOperationsResponse), CommandSurface.AdvancedAi, AgentClass.Draft),
        new CommandDescriptor("compose-author-part-of-speech", typeof(ComposeAuthorPartOfSpeechRequest),
            typeof(ComposedOperationsResponse), CommandSurface.AdvancedAi, AgentClass.Draft),
        new CommandDescriptor("compose-author-inflection-feature", typeof(ComposeAuthorInflectionFeatureRequest),
            typeof(ComposedOperationsResponse), CommandSurface.AdvancedAi, AgentClass.Draft),
        new CommandDescriptor("compose-author-inflection-class", typeof(ComposeAuthorInflectionClassRequest),
            typeof(ComposedOperationsResponse), CommandSurface.AdvancedAi, AgentClass.Draft),
        new CommandDescriptor("compose-author-stem-name", typeof(ComposeAuthorStemNameRequest),
            typeof(ComposedOperationsResponse), CommandSurface.AdvancedAi, AgentClass.Draft),
        new CommandDescriptor("compose-author-lexical-entry", typeof(ComposeAuthorLexicalEntryRequest),
            typeof(ComposedOperationsResponse), CommandSurface.AdvancedAi, AgentClass.Draft),
        new CommandDescriptor("compose-author-alternate-form", typeof(ComposeAuthorAlternateFormRequest),
            typeof(ComposedOperationsResponse), CommandSurface.AdvancedAi, AgentClass.Draft),
        new CommandDescriptor("compose-author-affix-slot", typeof(ComposeAuthorAffixSlotRequest),
            typeof(ComposedOperationsResponse), CommandSurface.AdvancedAi, AgentClass.Draft),
        new CommandDescriptor("compose-author-affix-template", typeof(ComposeAuthorAffixTemplateRequest),
            typeof(ComposedOperationsResponse), CommandSurface.AdvancedAi, AgentClass.Draft),
        new CommandDescriptor("retire-allomorph", typeof(ComposeRetireAllomorphRequest),
            typeof(ComposedOperationsResponse), CommandSurface.AdvancedAi, AgentClass.Draft),
        new CommandDescriptor("retire-redundant-zero-affix", typeof(ComposeRetireRedundantZeroAffixRequest),
            typeof(ComposedOperationsResponse), CommandSurface.AdvancedAi, AgentClass.Draft),
        new CommandDescriptor("compose-edit-adhoc-prohibition", typeof(ComposeEditAdhocProhibitionRequest),
            typeof(ComposedOperationsResponse), CommandSurface.AdvancedAi, AgentClass.Draft),
        new CommandDescriptor("compose-edit-affix-slot", typeof(ComposeEditAffixSlotRequest),
            typeof(ComposedOperationsResponse), CommandSurface.AdvancedAi, AgentClass.Draft),
        new CommandDescriptor("compose-edit-affix-template", typeof(ComposeEditAffixTemplateRequest),
            typeof(ComposedOperationsResponse), CommandSurface.AdvancedAi, AgentClass.Draft),
        new CommandDescriptor("compose-edit-inflectional-affix", typeof(ComposeEditInflectionalAffixRequest),
            typeof(ComposedOperationsResponse), CommandSurface.AdvancedAi, AgentClass.Draft),
        new CommandDescriptor("compose-edit-part-of-speech-inflection-class-default",
            typeof(ComposeEditPartOfSpeechInflectionClassDefaultRequest), typeof(ComposedOperationsResponse),
            CommandSurface.AdvancedAi, AgentClass.Draft) { ExposeAsAgentTool = false },
        new CommandDescriptor("compose-edit-affix-inflection-classes",
            typeof(ComposeEditAffixInflectionClassesRequest), typeof(ComposedOperationsResponse),
            CommandSurface.AdvancedAi, AgentClass.Draft) { ExposeAsAgentTool = false },
        new CommandDescriptor("compose-edit-stem-allomorph-stem-name",
            typeof(ComposeEditStemAllomorphStemNameRequest), typeof(ComposedOperationsResponse),
            CommandSurface.AdvancedAi, AgentClass.Draft) { ExposeAsAgentTool = false },
        new CommandDescriptor("compose-edit-stem-inflection-class",
            typeof(ComposeEditStemInflectionClassRequest), typeof(ComposedOperationsResponse),
            CommandSurface.AdvancedAi, AgentClass.Draft) { ExposeAsAgentTool = false },
        new CommandDescriptor("compose-edit-allomorph-condition", typeof(ComposeEditAllomorphConditionRequest),
            typeof(ComposedOperationsResponse), CommandSurface.AdvancedAi, AgentClass.Draft),
        new CommandDescriptor("order-allomorphs", typeof(ComposeOrderAllomorphsRequest),
            typeof(ComposedOperationsResponse), CommandSurface.AdvancedAi, AgentClass.Draft),
        new CommandDescriptor("compose-author-text", typeof(ComposeAuthorTextRequest),
            typeof(ComposedOperationsResponse), CommandSurface.AdvancedAi, AgentClass.Draft),
        new CommandDescriptor("compose-edit-analysis-decision", typeof(ComposeEditAnalysisDecisionRequest),
            typeof(ComposedOperationsResponse), CommandSurface.AdvancedAi, AgentClass.Draft),
        new CommandDescriptor("compose-batch", typeof(ComposeBatchRequest),
            typeof(ComposedBatchResponse), CommandSurface.AdvancedAi, AgentClass.Draft),
        new CommandDescriptor("promote-gloss", typeof(PromoteGlossRequest), typeof(PromoteGlossAddedResponse), CommandSurface.Developer, AgentClass.Draft),
        new CommandDescriptor("label", typeof(LabelRequest), typeof(DraftFieldChangedResponse), CommandSurface.AdvancedAi, AgentClass.Draft),
        new CommandDescriptor("comment", typeof(CommentRequest), typeof(DraftFieldChangedResponse), CommandSurface.AdvancedAi, AgentClass.Draft),
        new CommandDescriptor("finalize", typeof(FinalizeRequest), typeof(ProposalFinalizedResponse), CommandSurface.AdvancedAi, AgentClass.Draft),
        new CommandDescriptor("discard-draft", typeof(DiscardDraftRequest), typeof(DraftDiscardedResponse), CommandSurface.Developer, AgentClass.HumanOnly),
        new CommandDescriptor("reopen", typeof(ReopenRequest), typeof(ReopenedResponse), CommandSurface.Developer, AgentClass.Draft),
        new CommandDescriptor("duplicate", typeof(DuplicateRequest), typeof(DuplicatedResponse), CommandSurface.Developer, AgentClass.Draft),
        new CommandDescriptor(
            "remove-operations", typeof(RemoveOperationsRequest), typeof(OperationsRemovedResponse), CommandSurface.AdvancedAi, AgentClass.Draft),
        new CommandDescriptor("split", typeof(SplitRequest), typeof(ProposalSplitResponse), CommandSurface.Developer, AgentClass.Draft),
        new CommandDescriptor("defer", typeof(DeferRequest), typeof(ProposalStatusChangedResponse), CommandSurface.Developer, AgentClass.HumanOnly),
        new CommandDescriptor("reject", typeof(RejectRequest), typeof(ProposalStatusChangedResponse), CommandSurface.Developer, AgentClass.HumanOnly),
        new CommandDescriptor("supersede", typeof(SupersedeRequest), typeof(ProposalStatusChangedResponse), CommandSurface.Developer, AgentClass.HumanOnly),
        new CommandDescriptor("list", typeof(ListProposalsRequest), typeof(ProposalListProjection), CommandSurface.AdvancedAi, AgentClass.Read),
        new CommandDescriptor("show", typeof(ShowProposalRequest), typeof(ProposalDetailProjection), CommandSurface.AdvancedAi, AgentClass.Read),
        new CommandDescriptor("preflight", typeof(PreflightRequest), typeof(PreflightResponse), CommandSurface.Developer, AgentClass.Evaluate),
        new CommandDescriptor("apply", typeof(ApplyRequest), typeof(ApplyPendingResult), CommandSurface.Released, AgentClass.HumanOnly),
        new CommandDescriptor("log", typeof(LogRequest), typeof(AppliedLogProjection), CommandSurface.Developer, AgentClass.Read),
        new CommandDescriptor("citations", typeof(CitationsRequest), typeof(CitationTraceResponse), CommandSurface.Developer, AgentClass.Read),

        // CorpusCommands
        new CommandDescriptor("add-corpus", typeof(AddCorpusRequest), typeof(CorpusAddedResponse), CommandSurface.Released, AgentClass.HumanOnly),
        new CommandDescriptor("add-document", typeof(AddDocumentRequest), typeof(CorpusDocumentAddedResponse), CommandSurface.Released, AgentClass.HumanOnly),
        new CommandDescriptor("add-corpus-bundle", typeof(AddCorpusBundleRequest), typeof(CorpusBundleAddedResponse), CommandSurface.Released, AgentClass.HumanOnly),
        new CommandDescriptor("corpora", typeof(ListCorporaRequest), typeof(CorpusListProjection), CommandSurface.Released, AgentClass.Read),
        new CommandDescriptor("show-corpus", typeof(ShowCorpusRequest), typeof(CorpusDetailProjection), CommandSurface.Released, AgentClass.Read),

        // ConfigCommands
        new CommandDescriptor("config show", typeof(ShowConfigRequest), typeof(ProjectConfigurationProjection), CommandSurface.Released, AgentClass.Read),

        // ReportCommands — one verb, two handlers (see remarks)
        new CommandDescriptor("report", typeof(ProduceReportRequest), typeof(ReportResponse), CommandSurface.Released, AgentClass.Read),
        new CommandDescriptor("report --list-kinds", typeof(ListReportKindsRequest), typeof(ReportKindListResponse), CommandSurface.Released, AgentClass.Read),
        new CommandDescriptor("parsimony", typeof(EnqueueParsimonyReportRequest), typeof(JobEnqueuedResponse), CommandSurface.AdvancedAi, AgentClass.Evaluate),
        new CommandDescriptor("parsimony --wait", typeof(WaitForParsimonyReportRequest), typeof(ParsimonyReportResponse), CommandSurface.AdvancedAi, AgentClass.Evaluate),
        new CommandDescriptor("parsimony --measure all --wait", typeof(WaitForParsimonyReportSetRequest), typeof(ParsimonyReportSetResponse), CommandSurface.AdvancedAi, AgentClass.Evaluate),
        new CommandDescriptor("parsimony --dry-run", typeof(EnqueueParsimonyCandidateRequest), typeof(JobEnqueuedResponse), CommandSurface.AdvancedAi, AgentClass.Evaluate),
        new CommandDescriptor("parsimony --dry-run --wait", typeof(WaitForParsimonyCandidateRequest), typeof(ParsimonyCandidateEvidenceResponse), CommandSurface.AdvancedAi, AgentClass.Evaluate),
        new CommandDescriptor("parsimony show", typeof(ShowParsimonyReportRequest), typeof(ParsimonyReportResponse), CommandSurface.AdvancedAi, AgentClass.Read),
        new CommandDescriptor("parsimony latest", typeof(ReadLatestParsimonyReportRequest), typeof(ParsimonyLatestReportResponse), CommandSurface.AdvancedAi, AgentClass.Read),
        new CommandDescriptor("parsimony retirement-review", typeof(ReadRetirementReviewRequest),
            typeof(RetirementReviewQueryResponse), CommandSurface.AdvancedAi, AgentClass.Read),
        new CommandDescriptor("parsimony measures", typeof(ListParsimonyMeasuresRequest), typeof(ParsimonyMeasureCatalogResponse), CommandSurface.AdvancedAi, AgentClass.Read),
        new CommandDescriptor("parsimony expectations", typeof(ReadParsimonyExpectationsRequest), typeof(ParsimonyExpectationProjection), CommandSurface.AdvancedAi, AgentClass.Read),
        new CommandDescriptor("parsimony view", typeof(ReadParsimonyViewRequest), typeof(ParsimonyNamedViewResponse), CommandSurface.AdvancedAi, AgentClass.Read),
        new CommandDescriptor("parsimony record-types", typeof(ListNotebookRecordTypesRequest),
            typeof(NotebookRecordTypesResponse), CommandSurface.AdvancedAi, AgentClass.Read),
        new CommandDescriptor("parsimony dispose", typeof(RecordParsimonyDispositionFromFindingRequest),
            typeof(ComposedOperationsResponse), CommandSurface.AdvancedAi, AgentClass.Draft),
        new CommandDescriptor("parsimony revise", typeof(ReviseParsimonyDispositionRequest),
            typeof(ComposedOperationsResponse), CommandSurface.AdvancedAi, AgentClass.Draft),
        new CommandDescriptor("parsimony retract", typeof(RetractParsimonyDispositionRequest),
            typeof(ComposedOperationsResponse), CommandSurface.AdvancedAi, AgentClass.Draft),
        new CommandDescriptor("parsimony negative confirm", typeof(ConfirmReviewedNegativeRequest),
            typeof(ComposedOperationsResponse), CommandSurface.AdvancedAi, AgentClass.HumanOnly),
        new CommandDescriptor("parsimony negative retract", typeof(RetractReviewedNegativeRequest),
            typeof(ComposedOperationsResponse), CommandSurface.AdvancedAi, AgentClass.HumanOnly),
        new CommandDescriptor("compose-record-parsimony-disposition", typeof(ComposeRecordParsimonyDispositionRequest),
            typeof(ComposedOperationsResponse), CommandSurface.AdvancedAi, AgentClass.Draft),

        // CompareCommands
        new CommandDescriptor("compare", typeof(ProduceComparisonRequest), typeof(CompareResponse), CommandSurface.Released, AgentClass.Read),

        // BaselineCaptureCommand
        new CommandDescriptor("baseline capture", typeof(BaselineCaptureRequest), typeof(BaselineCaptureResponse), CommandSurface.Released, AgentClass.Evaluate),

        // AssessCommand
        new CommandDescriptor("assess", typeof(AssessRequest), typeof(AssessCommandResponse), CommandSurface.Released, AgentClass.Evaluate),

        // StatsCommand
        new CommandDescriptor("stats", typeof(StatsRequest), typeof(StatsCommandResponse), CommandSurface.Released, AgentClass.Evaluate),

        // Saved default Selection
        new CommandDescriptor("project create", typeof(ProjectCreateRequest),
            typeof(ProjectCreatedResponse), CommandSurface.Developer, AgentClass.HumanOnly),
        new CommandDescriptor("project initialize", typeof(ProjectInitializationRequest),
            typeof(ProjectInitializationResponse), CommandSurface.AdvancedAi, AgentClass.HumanOnly),
        new CommandDescriptor("selection show", typeof(ReadDefaultSelectionRequest), typeof(DefaultSelectionResponse), CommandSurface.Released, AgentClass.Read),
        new CommandDescriptor("selection set-default", typeof(SetDefaultSelectionRequest), typeof(DefaultSelectionResponse), CommandSurface.Released, AgentClass.HumanOnly),
        new CommandDescriptor("selection set-limits", typeof(SetSelectionLimitsRequest), typeof(NamedSelectionProjection), CommandSurface.Released, AgentClass.HumanOnly),
        new CommandDescriptor("setup skip", typeof(SkipSetupRequest), typeof(ProjectSetupResponse), CommandSurface.Released, AgentClass.HumanOnly),
        new CommandDescriptor("store delete-refused", typeof(ProjectStoreResetRequest), typeof(ProjectStoreResetResponse), CommandSurface.Developer, AgentClass.HumanOnly),
        new CommandDescriptor("texts list", typeof(TextInventoryRequest), typeof(TextInventoryResponse), CommandSurface.Released, AgentClass.Read),
        new CommandDescriptor("word read-state", typeof(WordReadStateRequest), typeof(WordReadStateResponse), CommandSurface.Developer, AgentClass.Read),

        new CommandDescriptor("phonology-examples save", typeof(SavePhonologyExamplesRequest),
            typeof(PhonologyExamplesSavedResponse), CommandSurface.AdvancedAi, AgentClass.Draft),
        new CommandDescriptor("phonology-examples list", typeof(ListPhonologyExamplesRequest),
            typeof(PhonologyExamplesListResponse), CommandSurface.Released, AgentClass.Read),
        new CommandDescriptor("phonology-examples show", typeof(ShowPhonologyExamplesRequest),
            typeof(PhonologyExamplesPageResponse), CommandSurface.Released, AgentClass.Read),

        new CommandDescriptor("writing-systems", typeof(WritingSystemsRequest),
            typeof(WritingSystemsResponse), CommandSurface.Released, AgentClass.Read),

        // Overview and Timing
        new CommandDescriptor("overview", typeof(OverviewRequest), typeof(OverviewResponse), CommandSurface.Released, AgentClass.Read),
        new CommandDescriptor("difference produce", typeof(ProposalDifferenceProduceRequest), typeof(ProposalDifferenceResponse), CommandSurface.AdvancedAi, AgentClass.Evaluate),
        new CommandDescriptor("difference show", typeof(ProposalDifferenceShowRequest), typeof(ProposalDifferenceResponse), CommandSurface.AdvancedAi, AgentClass.Read),
        new CommandDescriptor("proposal trial", typeof(ReadTrialRequest), typeof(TrialRecord), CommandSurface.Developer, AgentClass.Read),
        new CommandDescriptor("warnings", typeof(WarningsRequest), typeof(WarningsResponse), CommandSurface.Released, AgentClass.Read),
        new CommandDescriptor("grammar check", typeof(GrammarCheckRequest), typeof(GrammarCheckResponse), CommandSurface.Released, AgentClass.Evaluate),
        new CommandDescriptor("grammar read", typeof(GrammarReadRequest), typeof(GrammarReadResponse), CommandSurface.AdvancedAi, AgentClass.Read),
        new CommandDescriptor("timing", typeof(TimingRequest), typeof(TimingResponse), CommandSurface.Released, AgentClass.Read),

        // ObjectUsesQuery: the words that use an object, the words it ran in, and what a set of words shares
        new CommandDescriptor("uses", typeof(ObjectUsesRequest), typeof(ObjectUsesResponse), CommandSurface.Developer, AgentClass.Read),

        // InspectQuery: one subject's facts, uses, timings and warnings, each section from its own source
        new CommandDescriptor("inspect", typeof(InspectRequest), typeof(InspectResponse), CommandSurface.Developer, AgentClass.Read),

        new CommandDescriptor("word-context", typeof(WordContextRequest), typeof(WordContextResponse), CommandSurface.Released, AgentClass.Read),

        // WordTraceQuery: one word's trace, live or from a saved file, as Try a Word reads it
        new CommandDescriptor("trace", typeof(WordTraceRequest), typeof(WordTraceResponse), CommandSurface.Released, AgentClass.Evaluate),
        new CommandDescriptor("trace --load", typeof(WordTraceLoadRequest), typeof(WordTraceResponse), CommandSurface.Released, AgentClass.Read),

        // HandoffCommand
        new CommandDescriptor("handoff", typeof(HandoffRequest), typeof(HandoffCommandResponse), CommandSurface.Released, AgentClass.HumanOnly),

        // JobCommands
        new CommandDescriptor(
            "baseline-refresh", typeof(EnqueueBaselineRefreshRequest), typeof(JobEnqueuedResponse), CommandSurface.Released, AgentClass.Evaluate),
        new CommandDescriptor("dry-run", typeof(EnqueueDryRunRequest), typeof(JobEnqueuedResponse), CommandSurface.AdvancedAi, AgentClass.Evaluate),
        new CommandDescriptor("dry-run --wait", typeof(WaitForDryRunRequest), typeof(DryRunProjection), CommandSurface.AdvancedAi, AgentClass.Evaluate),
        new CommandDescriptor("trial", typeof(EnqueueTrialRequest), typeof(JobEnqueuedResponse), CommandSurface.AdvancedAi, AgentClass.Evaluate),
        new CommandDescriptor("trial --wait", typeof(WaitForJobRequest), typeof(JobStatusResponse), CommandSurface.AdvancedAi, AgentClass.Evaluate),
        new CommandDescriptor("jobs show", typeof(ShowJobRequest), typeof(JobStatusResponse), CommandSurface.Released, AgentClass.Read),
        new CommandDescriptor("jobs assessments", typeof(JobAssessmentsRequest), typeof(JobAssessmentsResponse), CommandSurface.Released, AgentClass.Read),
        new CommandDescriptor("jobs list", typeof(ListActiveJobsRequest), typeof(JobQueueListResponse), CommandSurface.Released, AgentClass.Read),
        new CommandDescriptor("jobs cancel", typeof(CancelJobRequest), typeof(JobStatusResponse), CommandSurface.Released, AgentClass.HumanOnly),
        new CommandDescriptor("jobs requeue", typeof(RequeueJobRequest), typeof(JobStatusResponse), CommandSurface.Released, AgentClass.HumanOnly),
        new CommandDescriptor("jobs move", typeof(MoveJobRequest), typeof(JobStatusResponse), CommandSurface.Released, AgentClass.HumanOnly),

        // ProposalStandingsCommand
        new CommandDescriptor("proposals", typeof(ProposalStandingsRequest), typeof(ProposalStandings), CommandSurface.Released, AgentClass.Read),

        // ProposalLifecycleCommand
        new CommandDescriptor("delete --proposal", typeof(DeleteProposalRequest), typeof(ProposalDeletedResponse),
            CommandSurface.Released, AgentClass.HumanOnly),
        new CommandDescriptor("reject --proposal", typeof(DeclineProposalRequest),
            typeof(ProposalStatusChangedResponse), CommandSurface.Released, AgentClass.HumanOnly),
        new CommandDescriptor("reopen --proposal", typeof(ReopenProposalRequest), typeof(ReopenedResponse),
            CommandSurface.Released, AgentClass.HumanOnly),
        new CommandDescriptor("withdraw", typeof(WithdrawProposalRequest), typeof(ProposalStatusChangedResponse),
            CommandSurface.AdvancedAi, AgentClass.Draft),
        new CommandDescriptor("proposal review", typeof(ProposalReviewRequest), typeof(ProposalReviewResponse),
            CommandSurface.Released, AgentClass.Read),
        new CommandDescriptor("proposal looked-at", typeof(ReviewMarkRequest), typeof(ReviewMarkResponse),
            CommandSurface.Developer, AgentClass.HumanOnly),
        new CommandDescriptor("apply --proposal", typeof(ApplyProposalRequest), typeof(ApplyPendingResult),
            CommandSurface.Released, AgentClass.HumanOnly),
    };
}
