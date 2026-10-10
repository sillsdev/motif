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
/// <see cref="TimingCommand"/>, <see cref="Queries.GrammarCheckQuery"/>, <see cref="HandoffCommand"/>,
/// <see cref="JobCommands"/>, <see cref="PendingChangesWorkflow"/>, <see cref="ReadStateCommands"/>,
/// <see cref="Queries.WordContextQuery"/>, <see cref="Queries.WordTraceQuery"/>, <see cref="Queries.ObjectUsesQuery"/>, and <see cref="Queries.InspectQuery"/>.
/// The project setup group reaches <see cref="ProjectInitializationCommand"/>.
/// </summary>
/// <remarks>
/// The report list reaches <see cref="ReportCommands.ListKinds"/>, waited Dry Runs reach
/// <see cref="JobCommands.WaitForDryRun"/>, waited Trials reach <see cref="JobCommands.WaitForJob"/>,
/// pending Trials reach <see cref="PendingChangesWorkflow.Measure"/>, and pending Apply reaches
/// <see cref="PendingChangesWorkflow.Apply"/>, and a saved trace reaches <see cref="Queries.WordTraceQuery.Load"/>.
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
        new CommandDescriptor("new", typeof(NewDraftRequest), typeof(DraftCreatedResponse), CommandSurface.Developer, AgentClass.Draft) { AgentTool = "motif_start_proposal" },
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
        new CommandDescriptor("add-set-gloss", typeof(AddSetGlossRequest), typeof(SetGlossAddedResponse), CommandSurface.Developer, AgentClass.Draft) { AgentTool = "motif_set_gloss" },
        new CommandDescriptor(
            "add-delete-lexeme-form", typeof(AddDeleteLexemeFormRequest), typeof(DeleteLexemeFormAddedResponse), CommandSurface.Developer, AgentClass.Draft),
        new CommandDescriptor(
            "compose-author-lexeme-form", typeof(ComposeAuthorLexemeFormRequest),
            typeof(ComposedOperationsResponse), CommandSurface.Developer, AgentClass.Draft) { AgentTool = "motif_add_lexeme_form" },
        new CommandDescriptor(
            "compose-author-feature-structure", typeof(ComposeAuthorFeatureStructureRequest),
            typeof(ComposedOperationsResponse), CommandSurface.Developer, AgentClass.Draft) { AgentTool = "motif_add_feature_structure" },
        new CommandDescriptor("compose-author-feature-value", typeof(ComposeAuthorFeatureValueRequest),
            typeof(ComposedOperationsResponse), CommandSurface.AdvancedAi, AgentClass.Draft) { AgentTool = "motif_add_feature_value" },
        new CommandDescriptor("compose-author-phoneme", typeof(ComposeAuthorPhonemeRequest),
            typeof(ComposedOperationsResponse), CommandSurface.AdvancedAi, AgentClass.Draft) { AgentTool = "motif_add_phoneme" },
        new CommandDescriptor("compose-author-natural-class", typeof(ComposeAuthorNaturalClassRequest),
            typeof(ComposedOperationsResponse), CommandSurface.AdvancedAi, AgentClass.Draft) { AgentTool = "motif_add_natural_class" },
        new CommandDescriptor("compose-edit-natural-class", typeof(ComposeEditNaturalClassRequest),
            typeof(ComposedOperationsResponse), CommandSurface.AdvancedAi, AgentClass.Draft) { AgentTool = "motif_edit_natural_class" },
        new CommandDescriptor("compose-relink-natural-class", typeof(ComposeRelinkNaturalClassRequest),
            typeof(ComposedOperationsResponse), CommandSurface.AdvancedAi, AgentClass.Draft) { AgentTool = "motif_relink_natural_class" },
        new CommandDescriptor("compose-author-environment", typeof(ComposeAuthorEnvironmentRequest),
            typeof(ComposedOperationsResponse), CommandSurface.AdvancedAi, AgentClass.Draft) { AgentTool = "motif_add_environment" },
        new CommandDescriptor("compose-author-phonological-rule", typeof(ComposeAuthorPhonologicalRuleRequest),
            typeof(ComposedOperationsResponse), CommandSurface.AdvancedAi, AgentClass.Draft)
            { AgentTool = "motif_add_phonological_rule" },
        new CommandDescriptor("compose-author-affix-slot", typeof(ComposeAuthorAffixSlotRequest),
            typeof(ComposedOperationsResponse), CommandSurface.AdvancedAi, AgentClass.Draft)
            { AgentTool = "motif_add_affix_slot" },
        new CommandDescriptor("compose-author-affix-template", typeof(ComposeAuthorAffixTemplateRequest),
            typeof(ComposedOperationsResponse), CommandSurface.AdvancedAi, AgentClass.Draft)
            { AgentTool = "motif_add_affix_template" },
        new CommandDescriptor("retire-allomorph", typeof(ComposeRetireAllomorphRequest),
            typeof(ComposedOperationsResponse), CommandSurface.AdvancedAi, AgentClass.Draft)
            { AgentTool = "motif_retire_allomorph" },
        new CommandDescriptor("retire-redundant-zero-affix", typeof(ComposeRetireRedundantZeroAffixRequest),
            typeof(ComposedOperationsResponse), CommandSurface.AdvancedAi, AgentClass.Draft)
            { AgentTool = "motif_retire_redundant_zero_affix" },
        new CommandDescriptor("compose-edit-adhoc-prohibition", typeof(ComposeEditAdhocProhibitionRequest),
            typeof(ComposedOperationsResponse), CommandSurface.AdvancedAi, AgentClass.Draft)
            { AgentTool = "motif_edit_adhoc_prohibition" },
        new CommandDescriptor("compose-edit-affix-slot", typeof(ComposeEditAffixSlotRequest),
            typeof(ComposedOperationsResponse), CommandSurface.AdvancedAi, AgentClass.Draft)
            { AgentTool = "motif_edit_affix_slot" },
        new CommandDescriptor("compose-edit-affix-template", typeof(ComposeEditAffixTemplateRequest),
            typeof(ComposedOperationsResponse), CommandSurface.AdvancedAi, AgentClass.Draft)
            { AgentTool = "motif_edit_affix_template" },
        new CommandDescriptor("compose-edit-inflectional-affix", typeof(ComposeEditInflectionalAffixRequest),
            typeof(ComposedOperationsResponse), CommandSurface.AdvancedAi, AgentClass.Draft)
            { AgentTool = "motif_edit_inflectional_affix" },
        new CommandDescriptor("compose-edit-allomorph-condition", typeof(ComposeEditAllomorphConditionRequest),
            typeof(ComposedOperationsResponse), CommandSurface.AdvancedAi, AgentClass.Draft)
            { AgentTool = "motif_edit_allomorph_condition" },
        new CommandDescriptor("order-allomorphs", typeof(ComposeOrderAllomorphsRequest),
            typeof(ComposedOperationsResponse), CommandSurface.AdvancedAi, AgentClass.Draft)
            { AgentTool = "motif_order_allomorphs" },
        new CommandDescriptor("promote-gloss", typeof(PromoteGlossRequest), typeof(PromoteGlossAddedResponse), CommandSurface.Developer, AgentClass.Draft),
        new CommandDescriptor("label", typeof(LabelRequest), typeof(DraftFieldChangedResponse), CommandSurface.Developer, AgentClass.Draft),
        new CommandDescriptor("comment", typeof(CommentRequest), typeof(DraftFieldChangedResponse), CommandSurface.Developer, AgentClass.Draft),
        new CommandDescriptor("finalize", typeof(FinalizeRequest), typeof(ProposalFinalizedResponse), CommandSurface.Developer, AgentClass.Draft) { AgentTool = "motif_finish_proposal" },
        new CommandDescriptor("discard-draft", typeof(DiscardDraftRequest), typeof(DraftDiscardedResponse), CommandSurface.Developer, AgentClass.HumanOnly),
        new CommandDescriptor("reopen", typeof(ReopenRequest), typeof(ReopenedResponse), CommandSurface.Developer, AgentClass.Draft),
        new CommandDescriptor("duplicate", typeof(DuplicateRequest), typeof(DuplicatedResponse), CommandSurface.Developer, AgentClass.Draft),
        new CommandDescriptor(
            "remove-operations", typeof(RemoveOperationsRequest), typeof(OperationsRemovedResponse), CommandSurface.Developer, AgentClass.Draft) { AgentTool = "motif_remove_operations" },
        new CommandDescriptor("split", typeof(SplitRequest), typeof(ProposalSplitResponse), CommandSurface.Developer, AgentClass.Draft),
        new CommandDescriptor("defer", typeof(DeferRequest), typeof(ProposalStatusChangedResponse), CommandSurface.Developer, AgentClass.HumanOnly),
        new CommandDescriptor("reject", typeof(RejectRequest), typeof(ProposalStatusChangedResponse), CommandSurface.Developer, AgentClass.HumanOnly),
        new CommandDescriptor("supersede", typeof(SupersedeRequest), typeof(ProposalStatusChangedResponse), CommandSurface.Developer, AgentClass.HumanOnly),
        new CommandDescriptor("list", typeof(ListProposalsRequest), typeof(ProposalListProjection), CommandSurface.Developer, AgentClass.Read) { AgentTool = "motif_proposals" },
        new CommandDescriptor("show", typeof(ShowProposalRequest), typeof(ProposalDetailProjection), CommandSurface.Developer, AgentClass.Read) { AgentTool = "motif_proposals" },
        new CommandDescriptor("preflight", typeof(PreflightRequest), typeof(PreflightResponse), CommandSurface.Developer, AgentClass.Evaluate),
        new CommandDescriptor("apply", typeof(ApplyRequest), typeof(ApplyProjection), CommandSurface.Developer, AgentClass.HumanOnly),
        new CommandDescriptor("log", typeof(LogRequest), typeof(AppliedLogProjection), CommandSurface.Developer, AgentClass.Read),

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
        new CommandDescriptor("parsimony", typeof(EnqueueParsimonyReportRequest), typeof(JobEnqueuedResponse), CommandSurface.AdvancedAi, AgentClass.Evaluate) { AgentTool = "motif_run_parsimony_measure" },
        new CommandDescriptor("parsimony --wait", typeof(WaitForParsimonyReportRequest), typeof(ParsimonyReportResponse), CommandSurface.AdvancedAi, AgentClass.Evaluate) { AgentTool = "motif_run_parsimony_measure" },
        new CommandDescriptor("parsimony --dry-run", typeof(EnqueueParsimonyCandidateRequest), typeof(JobEnqueuedResponse), CommandSurface.AdvancedAi, AgentClass.Evaluate),
        new CommandDescriptor("parsimony --dry-run --wait", typeof(WaitForParsimonyCandidateRequest), typeof(ParsimonyCandidateEvidenceResponse), CommandSurface.AdvancedAi, AgentClass.Evaluate),
        new CommandDescriptor("parsimony show", typeof(ShowParsimonyReportRequest), typeof(ParsimonyReportResponse), CommandSurface.AdvancedAi, AgentClass.Read) { AgentTool = "motif_parsimony_report" },
        new CommandDescriptor("parsimony latest", typeof(ReadLatestParsimonyReportRequest), typeof(ParsimonyLatestReportResponse), CommandSurface.AdvancedAi, AgentClass.Read),
        new CommandDescriptor("parsimony retirement-review", typeof(ReadRetirementReviewRequest),
            typeof(RetirementReviewQueryResponse), CommandSurface.AdvancedAi, AgentClass.Read)
            { AgentTool = "motif_retirement_review" },
        new CommandDescriptor("parsimony measures", typeof(ListParsimonyMeasuresRequest), typeof(ParsimonyMeasureCatalogResponse), CommandSurface.AdvancedAi, AgentClass.Read) { AgentTool = "motif_parsimony_measures" },
        new CommandDescriptor("parsimony expectations", typeof(ReadParsimonyExpectationsRequest), typeof(ParsimonyExpectationProjection), CommandSurface.AdvancedAi, AgentClass.Read),
        new CommandDescriptor("parsimony view", typeof(ReadParsimonyViewRequest), typeof(ParsimonyNamedViewResponse), CommandSurface.AdvancedAi, AgentClass.Read) { AgentTool = "motif_parsimony_view" },
        new CommandDescriptor("parsimony record-types", typeof(ListNotebookRecordTypesRequest),
            typeof(NotebookRecordTypesResponse), CommandSurface.AdvancedAi, AgentClass.Read)
            { AgentTool = "motif_parsimony_record_types" },
        new CommandDescriptor("parsimony dispose", typeof(RecordParsimonyDispositionFromFindingRequest),
            typeof(ComposedOperationsResponse), CommandSurface.AdvancedAi, AgentClass.Draft)
            { AgentTool = "motif_dispose_parsimony_finding" },
        new CommandDescriptor("parsimony revise", typeof(ReviseParsimonyDispositionRequest),
            typeof(ComposedOperationsResponse), CommandSurface.AdvancedAi, AgentClass.Draft)
            { AgentTool = "motif_revise_parsimony_disposition" },
        new CommandDescriptor("parsimony retract", typeof(RetractParsimonyDispositionRequest),
            typeof(ComposedOperationsResponse), CommandSurface.AdvancedAi, AgentClass.Draft)
            { AgentTool = "motif_retract_parsimony_disposition" },
        new CommandDescriptor("parsimony negative confirm", typeof(ConfirmReviewedNegativeRequest),
            typeof(ComposedOperationsResponse), CommandSurface.AdvancedAi, AgentClass.HumanOnly),
        new CommandDescriptor("parsimony negative retract", typeof(RetractReviewedNegativeRequest),
            typeof(ComposedOperationsResponse), CommandSurface.AdvancedAi, AgentClass.HumanOnly),
        new CommandDescriptor("compose-record-parsimony-disposition", typeof(ComposeRecordParsimonyDispositionRequest),
            typeof(ComposedOperationsResponse), CommandSurface.AdvancedAi, AgentClass.Draft)
            { AgentTool = "motif_record_parsimony_disposition" },

        // CompareCommands
        new CommandDescriptor("compare", typeof(ProduceComparisonRequest), typeof(CompareResponse), CommandSurface.Released, AgentClass.Read),

        // BaselineCaptureCommand
        new CommandDescriptor("baseline capture", typeof(BaselineCaptureRequest), typeof(BaselineCaptureResponse), CommandSurface.Released, AgentClass.Evaluate) { AgentTool = "motif_capture_baseline" },

        // AssessCommand
        new CommandDescriptor("assess", typeof(AssessRequest), typeof(AssessCommandResponse), CommandSurface.Released, AgentClass.Evaluate),

        // StatsCommand
        new CommandDescriptor("stats", typeof(StatsRequest), typeof(StatsCommandResponse), CommandSurface.Released, AgentClass.Evaluate),

        // Saved default Selection
        new CommandDescriptor("project initialize", typeof(ProjectInitializationRequest),
            typeof(ProjectInitializationResponse), CommandSurface.AdvancedAi, AgentClass.HumanOnly),
        new CommandDescriptor("selection show", typeof(ReadDefaultSelectionRequest), typeof(DefaultSelectionResponse), CommandSurface.Released, AgentClass.Read),
        new CommandDescriptor("selection set-default", typeof(SetDefaultSelectionRequest), typeof(DefaultSelectionResponse), CommandSurface.Released, AgentClass.HumanOnly),
        new CommandDescriptor("selection set-limits", typeof(SetSelectionLimitsRequest), typeof(NamedSelectionProjection), CommandSurface.Released, AgentClass.HumanOnly),
        new CommandDescriptor("setup skip", typeof(SkipSetupRequest), typeof(ProjectSetupResponse), CommandSurface.Released, AgentClass.HumanOnly),
        new CommandDescriptor("store delete-refused", typeof(ProjectStoreResetRequest), typeof(ProjectStoreResetResponse), CommandSurface.Developer, AgentClass.HumanOnly),
        new CommandDescriptor("texts list", typeof(TextInventoryRequest), typeof(TextInventoryResponse), CommandSurface.Released, AgentClass.Read) { AgentTool = "motif_texts" },
        new CommandDescriptor("word read-state", typeof(WordReadStateRequest), typeof(WordReadStateResponse), CommandSurface.Developer, AgentClass.Read),

        new CommandDescriptor("writing-systems", typeof(WritingSystemsRequest),
            typeof(WritingSystemsResponse), CommandSurface.Released, AgentClass.Read) { AgentTool = "motif_writing_systems" },

        // Overview and Timing
        new CommandDescriptor("overview", typeof(OverviewRequest), typeof(OverviewResponse), CommandSurface.Released, AgentClass.Read) { AgentTool = "motif_overview" },
        new CommandDescriptor("warnings", typeof(WarningsRequest), typeof(WarningsResponse), CommandSurface.Released, AgentClass.Read) { AgentTool = "motif_findings" },
        new CommandDescriptor("grammar check", typeof(GrammarCheckRequest), typeof(GrammarCheckResponse), CommandSurface.Released, AgentClass.Evaluate) { AgentTool = "motif_check_grammar" },
        new CommandDescriptor("timing", typeof(TimingRequest), typeof(TimingResponse), CommandSurface.Released, AgentClass.Read),

        // ObjectUsesQuery: the words that use an object, the words it ran in, and what a set of words shares
        new CommandDescriptor("uses", typeof(ObjectUsesRequest), typeof(ObjectUsesResponse), CommandSurface.Developer, AgentClass.Read),

        // InspectQuery: one subject's facts, uses, timings and warnings, each section from its own source
        new CommandDescriptor("inspect", typeof(InspectRequest), typeof(InspectResponse), CommandSurface.Developer, AgentClass.Read),

        new CommandDescriptor("word-context", typeof(WordContextRequest), typeof(WordContextResponse), CommandSurface.Released, AgentClass.Read) { AgentTool = "motif_word" },

        // WordTraceQuery: one word's trace, live or from a saved file, as Try a Word reads it
        new CommandDescriptor("trace", typeof(WordTraceRequest), typeof(WordTraceResponse), CommandSurface.Released, AgentClass.Evaluate) { AgentTool = "motif_try_word" },
        new CommandDescriptor("trace --load", typeof(WordTraceLoadRequest), typeof(WordTraceResponse), CommandSurface.Released, AgentClass.Read),

        // HandoffCommand
        new CommandDescriptor("handoff", typeof(HandoffRequest), typeof(HandoffCommandResponse), CommandSurface.Released, AgentClass.HumanOnly),

        // JobCommands
        new CommandDescriptor(
            "baseline-refresh", typeof(EnqueueBaselineRefreshRequest), typeof(JobEnqueuedResponse), CommandSurface.Released, AgentClass.Evaluate),
        new CommandDescriptor("dry-run", typeof(EnqueueDryRunRequest), typeof(JobEnqueuedResponse), CommandSurface.Developer, AgentClass.Evaluate) { AgentTool = "motif_dry_run" },
        new CommandDescriptor("dry-run --wait", typeof(WaitForDryRunRequest), typeof(DryRunProjection), CommandSurface.Developer, AgentClass.Evaluate) { AgentTool = "motif_dry_run" },
        new CommandDescriptor("trial", typeof(EnqueueTrialRequest), typeof(JobEnqueuedResponse), CommandSurface.Developer, AgentClass.Evaluate) { AgentTool = "motif_trial" },
        new CommandDescriptor("trial --wait", typeof(WaitForJobRequest), typeof(JobStatusResponse), CommandSurface.Developer, AgentClass.Evaluate) { AgentTool = "motif_trial" },
        new CommandDescriptor("jobs show", typeof(ShowJobRequest), typeof(JobStatusResponse), CommandSurface.Released, AgentClass.Read) { AgentTool = "motif_job" },
        new CommandDescriptor("jobs assessments", typeof(JobAssessmentsRequest), typeof(JobAssessmentsResponse), CommandSurface.Released, AgentClass.Read),
        new CommandDescriptor("jobs list", typeof(ListActiveJobsRequest), typeof(JobQueueListResponse), CommandSurface.Released, AgentClass.Read),
        new CommandDescriptor("jobs cancel", typeof(CancelJobRequest), typeof(JobStatusResponse), CommandSurface.Released, AgentClass.HumanOnly),
        new CommandDescriptor("jobs requeue", typeof(RequeueJobRequest), typeof(JobStatusResponse), CommandSurface.Released, AgentClass.HumanOnly),
        new CommandDescriptor("jobs move", typeof(MoveJobRequest), typeof(JobStatusResponse), CommandSurface.Released, AgentClass.HumanOnly),
    };
}
