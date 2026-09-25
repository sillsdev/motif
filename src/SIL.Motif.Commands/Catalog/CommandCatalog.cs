using System.Collections.Generic;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Handoff;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Jobs;
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
/// <see cref="TimingCommand"/>, <see cref="HandoffCommand"/>,
/// <see cref="JobCommands"/>, and <see cref="PendingChangesWorkflow"/>.
/// </summary>
/// <remarks>
/// The report list reaches <see cref="ReportCommands.ListKinds"/>, waited Dry Runs reach
/// <see cref="JobCommands.WaitForDryRun"/>, waited Trials reach <see cref="JobCommands.WaitForJob"/>,
/// pending Trials reach <see cref="PendingChangesWorkflow.Measure"/>, and pending Apply reaches
/// <see cref="PendingChangesWorkflow.Apply"/>. Each has its own entry. Every other name is the literal CLI
/// verb (or its complete space-separated nested form, such as <c>"jobs show"</c>) that reaches its one named
/// handler, pinned equal to <c>CliVerbCatalog.All</c> by
/// <c>CommandCatalogParityTests.EveryCataloguedCommandHasExactlyOneCliVerb</c>.
/// </remarks>
public static class CommandCatalog
{
    public static IReadOnlyList<CommandDescriptor> All { get; } = new[]
    {
        // ProposalCommands
        new CommandDescriptor("open", typeof(OpenRequest), typeof(ProjectSummaryProjection), CommandSurface.Released),
        new CommandDescriptor("analyses", typeof(ManualAnalysesRequest), typeof(AnalysisAggregateProjection), CommandSurface.Released),
        new CommandDescriptor("new", typeof(NewDraftRequest), typeof(DraftCreatedResponse), CommandSurface.Developer),
        new CommandDescriptor("pending-changes", typeof(PendingChangesRequest),
            typeof(PendingChangesSnapshot), CommandSurface.Developer),
        new CommandDescriptor("put-pending-change", typeof(PutPendingChangeRequest),
            typeof(PendingChangesSnapshot), CommandSurface.Developer),
        new CommandDescriptor("remove-pending-change", typeof(RemovePendingChangeRequest),
            typeof(PendingChangesSnapshot), CommandSurface.Developer),
        new CommandDescriptor("recheck-pending-changes", typeof(RecheckPendingChangesRequest),
            typeof(PendingChangesSnapshot), CommandSurface.Developer),
        new CommandDescriptor("apply --all-pending", typeof(ApplyPendingRequest),
            typeof(ApplyProjection), CommandSurface.Released),
        new CommandDescriptor("trial --pending", typeof(MeasurePendingRequest),
            typeof(MeasurePendingResult), CommandSurface.Developer),
        new CommandDescriptor("review-numbers", typeof(ReviewNumbersCommand.Request),
            typeof(ReviewNumbersCommand.Response), CommandSurface.Developer),
        new CommandDescriptor("add-set-gloss", typeof(AddSetGlossRequest), typeof(SetGlossAddedResponse), CommandSurface.Developer),
        new CommandDescriptor(
            "add-delete-lexeme-form", typeof(AddDeleteLexemeFormRequest), typeof(DeleteLexemeFormAddedResponse), CommandSurface.Developer),
        new CommandDescriptor(
            "compose-author-lexeme-form", typeof(ComposeAuthorLexemeFormRequest),
            typeof(ComposedOperationsResponse), CommandSurface.Developer),
        new CommandDescriptor(
            "compose-author-feature-structure", typeof(ComposeAuthorFeatureStructureRequest),
            typeof(ComposedOperationsResponse), CommandSurface.Developer),
        new CommandDescriptor("promote-gloss", typeof(PromoteGlossRequest), typeof(PromoteGlossAddedResponse), CommandSurface.Developer),
        new CommandDescriptor("label", typeof(LabelRequest), typeof(DraftFieldChangedResponse), CommandSurface.Developer),
        new CommandDescriptor("comment", typeof(CommentRequest), typeof(DraftFieldChangedResponse), CommandSurface.Developer),
        new CommandDescriptor("finalize", typeof(FinalizeRequest), typeof(ProposalFinalizedResponse), CommandSurface.Developer),
        new CommandDescriptor("discard-draft", typeof(DiscardDraftRequest), typeof(DraftDiscardedResponse), CommandSurface.Developer),
        new CommandDescriptor("reopen", typeof(ReopenRequest), typeof(ReopenedResponse), CommandSurface.Developer),
        new CommandDescriptor("duplicate", typeof(DuplicateRequest), typeof(DuplicatedResponse), CommandSurface.Developer),
        new CommandDescriptor(
            "remove-operations", typeof(RemoveOperationsRequest), typeof(OperationsRemovedResponse), CommandSurface.Developer),
        new CommandDescriptor("split", typeof(SplitRequest), typeof(ProposalSplitResponse), CommandSurface.Developer),
        new CommandDescriptor("defer", typeof(DeferRequest), typeof(ProposalStatusChangedResponse), CommandSurface.Developer),
        new CommandDescriptor("reject", typeof(RejectRequest), typeof(ProposalStatusChangedResponse), CommandSurface.Developer),
        new CommandDescriptor("supersede", typeof(SupersedeRequest), typeof(ProposalStatusChangedResponse), CommandSurface.Developer),
        new CommandDescriptor("list", typeof(ListProposalsRequest), typeof(ProposalListProjection), CommandSurface.Developer),
        new CommandDescriptor("show", typeof(ShowProposalRequest), typeof(ProposalDetailProjection), CommandSurface.Developer),
        new CommandDescriptor("preflight", typeof(PreflightRequest), typeof(PreflightResponse), CommandSurface.Developer),
        new CommandDescriptor("apply", typeof(ApplyRequest), typeof(ApplyProjection), CommandSurface.Developer),
        new CommandDescriptor("log", typeof(LogRequest), typeof(AppliedLogProjection), CommandSurface.Developer),

        // CorpusCommands
        new CommandDescriptor("add-corpus", typeof(AddCorpusRequest), typeof(CorpusAddedResponse), CommandSurface.Released),
        new CommandDescriptor("add-document", typeof(AddDocumentRequest), typeof(CorpusDocumentAddedResponse), CommandSurface.Released),
        new CommandDescriptor("add-corpus-bundle", typeof(AddCorpusBundleRequest), typeof(CorpusBundleAddedResponse), CommandSurface.Released),
        new CommandDescriptor("corpora", typeof(ListCorporaRequest), typeof(CorpusListProjection), CommandSurface.Released),
        new CommandDescriptor("show-corpus", typeof(ShowCorpusRequest), typeof(CorpusDetailProjection), CommandSurface.Released),

        // ConfigCommands
        new CommandDescriptor("config show", typeof(ShowConfigRequest), typeof(ProjectConfigurationProjection), CommandSurface.Released),

        // ReportCommands — one verb, two handlers (see remarks)
        new CommandDescriptor("report", typeof(ProduceReportRequest), typeof(ReportResponse), CommandSurface.Released),
        new CommandDescriptor("report --list-kinds", typeof(ListReportKindsRequest), typeof(ReportKindListResponse), CommandSurface.Released),

        // CompareCommands
        new CommandDescriptor("compare", typeof(ProduceComparisonRequest), typeof(CompareResponse), CommandSurface.Released),

        // BaselineCaptureCommand
        new CommandDescriptor("baseline capture", typeof(BaselineCaptureRequest), typeof(BaselineCaptureResponse), CommandSurface.Released),

        // AssessCommand
        new CommandDescriptor("assess", typeof(AssessRequest), typeof(AssessCommandResponse), CommandSurface.Released),

        // StatsCommand
        new CommandDescriptor("stats", typeof(StatsRequest), typeof(StatsCommandResponse), CommandSurface.Released),

        // Saved default Selection
        new CommandDescriptor("selection show", typeof(ReadDefaultSelectionRequest), typeof(DefaultSelectionResponse), CommandSurface.Released),
        new CommandDescriptor("selection set-default", typeof(SetDefaultSelectionRequest), typeof(DefaultSelectionResponse), CommandSurface.Released),
        new CommandDescriptor("setup skip", typeof(SkipSetupRequest), typeof(ProjectSetupResponse), CommandSurface.Released),
        new CommandDescriptor("texts list", typeof(TextInventoryRequest), typeof(TextInventoryResponse), CommandSurface.Released),

        // Overview and Timing
        new CommandDescriptor("overview", typeof(OverviewRequest), typeof(OverviewResponse), CommandSurface.Released),
        new CommandDescriptor("warnings", typeof(WarningsRequest), typeof(WarningsResponse), CommandSurface.Released),
        new CommandDescriptor("timing", typeof(TimingRequest), typeof(TimingResponse), CommandSurface.Released),

        // HandoffCommand
        new CommandDescriptor("handoff", typeof(HandoffRequest), typeof(HandoffCommandResponse), CommandSurface.Released),

        // JobCommands
        new CommandDescriptor(
            "baseline-refresh", typeof(EnqueueBaselineRefreshRequest), typeof(JobEnqueuedResponse), CommandSurface.Released),
        new CommandDescriptor("dry-run", typeof(EnqueueDryRunRequest), typeof(JobEnqueuedResponse), CommandSurface.Developer),
        new CommandDescriptor("dry-run --wait", typeof(WaitForDryRunRequest), typeof(DryRunProjection), CommandSurface.Developer),
        new CommandDescriptor("trial", typeof(EnqueueTrialRequest), typeof(JobEnqueuedResponse), CommandSurface.Developer),
        new CommandDescriptor("trial --wait", typeof(WaitForJobRequest), typeof(JobStatusResponse), CommandSurface.Developer),
        new CommandDescriptor("jobs show", typeof(ShowJobRequest), typeof(JobStatusResponse), CommandSurface.Released),
        new CommandDescriptor("jobs assessments", typeof(JobAssessmentsRequest), typeof(JobAssessmentsResponse), CommandSurface.Released),
        new CommandDescriptor("jobs list", typeof(ListActiveJobsRequest), typeof(JobQueueListResponse), CommandSurface.Released),
        new CommandDescriptor("jobs cancel", typeof(CancelJobRequest), typeof(JobStatusResponse), CommandSurface.Released),
        new CommandDescriptor("jobs requeue", typeof(RequeueJobRequest), typeof(JobStatusResponse), CommandSurface.Released),
        new CommandDescriptor("jobs move", typeof(MoveJobRequest), typeof(JobStatusResponse), CommandSurface.Released),
    };
}
