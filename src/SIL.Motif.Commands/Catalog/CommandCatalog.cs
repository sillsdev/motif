using System.Collections.Generic;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Handoff;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Commands.Catalog;

/// <summary>
/// The sole enumeration of Motif's command handlers (ADR 0043 decision 3): one entry per public
/// handler in <see cref="ProposalCommands"/>, <see cref="CorpusCommands"/>, <see cref="ConfigCommands"/>,
/// <see cref="ReportCommands"/>, <see cref="CompareCommands"/>, <see cref="BaselineCaptureCommand"/>,
/// <see cref="Assess.AssessCommand"/>, <see cref="Assess.StatsCommand"/>, <see cref="HandoffCommand"/>,
/// and <see cref="JobCommands"/>.
/// </summary>
/// <remarks>
/// Three names each cover two entries because one CLI verb reaches two distinct handlers under
/// different flags: <c>report</c> reaches <see cref="ReportCommands.Produce"/> by default and
/// <see cref="ReportCommands.ListKinds"/> under <c>--list-kinds</c>; <c>dry-run</c> reaches
/// <see cref="JobCommands.EnqueueDryRun"/> and then, under <c>--wait</c>,
/// <see cref="JobCommands.WaitForDryRun"/>; <c>trial</c> reaches <see cref="JobCommands.EnqueueTrial"/>
/// and then, under <c>--wait</c>, <see cref="JobCommands.WaitForJob"/>. Every other name is the literal
/// CLI verb (or, for a nested subcommand, its complete space-separated form, e.g. <c>"jobs show"</c>)
/// that reaches exactly the one handler it names — pinned equal to the CLI's own <c>CliVerbCatalog.All</c>,
/// by <c>CommandCatalogParityTests.EveryCataloguedCommandHasExactlyOneCliVerb</c>.
/// </remarks>
public static class CommandCatalog
{
    public static IReadOnlyList<CommandDescriptor> All { get; } = new[]
    {
        // ProposalCommands
        new CommandDescriptor("open", typeof(OpenRequest), typeof(ProjectSummaryProjection)),
        new CommandDescriptor("analyses", typeof(ManualAnalysesRequest), typeof(AnalysisAggregateProjection)),
        new CommandDescriptor("new", typeof(NewDraftRequest), typeof(DraftCreatedResponse)),
        new CommandDescriptor("add-set-gloss", typeof(AddSetGlossRequest), typeof(SetGlossAddedResponse)),
        new CommandDescriptor(
            "add-delete-lexeme-form", typeof(AddDeleteLexemeFormRequest), typeof(DeleteLexemeFormAddedResponse)),
        new CommandDescriptor(
            "compose-author-lexeme-form", typeof(ComposeAuthorLexemeFormRequest),
            typeof(ComposedOperationsResponse)),
        new CommandDescriptor(
            "compose-author-feature-structure", typeof(ComposeAuthorFeatureStructureRequest),
            typeof(ComposedOperationsResponse)),
        new CommandDescriptor("promote-gloss", typeof(PromoteGlossRequest), typeof(PromoteGlossAddedResponse)),
        new CommandDescriptor("label", typeof(LabelRequest), typeof(DraftFieldChangedResponse)),
        new CommandDescriptor("comment", typeof(CommentRequest), typeof(DraftFieldChangedResponse)),
        new CommandDescriptor("finalize", typeof(FinalizeRequest), typeof(ProposalFinalizedResponse)),
        new CommandDescriptor("discard-draft", typeof(DiscardDraftRequest), typeof(DraftDiscardedResponse)),
        new CommandDescriptor("reopen", typeof(ReopenRequest), typeof(ReopenedResponse)),
        new CommandDescriptor("duplicate", typeof(DuplicateRequest), typeof(DuplicatedResponse)),
        new CommandDescriptor(
            "remove-operations", typeof(RemoveOperationsRequest), typeof(OperationsRemovedResponse)),
        new CommandDescriptor("split", typeof(SplitRequest), typeof(ProposalSplitResponse)),
        new CommandDescriptor("defer", typeof(DeferRequest), typeof(ProposalStatusChangedResponse)),
        new CommandDescriptor("reject", typeof(RejectRequest), typeof(ProposalStatusChangedResponse)),
        new CommandDescriptor("supersede", typeof(SupersedeRequest), typeof(ProposalStatusChangedResponse)),
        new CommandDescriptor("list", typeof(ListProposalsRequest), typeof(ProposalListProjection)),
        new CommandDescriptor("show", typeof(ShowProposalRequest), typeof(ProposalDetailProjection)),
        new CommandDescriptor("apply", typeof(ApplyRequest), typeof(ApplyProjection)),
        new CommandDescriptor("log", typeof(LogRequest), typeof(AppliedLogProjection)),

        // CorpusCommands
        new CommandDescriptor("add-corpus", typeof(AddCorpusRequest), typeof(CorpusAddedResponse)),
        new CommandDescriptor("add-document", typeof(AddDocumentRequest), typeof(CorpusDocumentAddedResponse)),
        new CommandDescriptor("add-corpus-bundle", typeof(AddCorpusBundleRequest), typeof(CorpusBundleAddedResponse)),
        new CommandDescriptor("corpora", typeof(ListCorporaRequest), typeof(CorpusListProjection)),
        new CommandDescriptor("show-corpus", typeof(ShowCorpusRequest), typeof(CorpusDetailProjection)),

        // ConfigCommands
        new CommandDescriptor("config show", typeof(ShowConfigRequest), typeof(ProjectConfigurationProjection)),

        // ReportCommands — one verb, two handlers (see remarks)
        new CommandDescriptor("report", typeof(ProduceReportRequest), typeof(ReportResponse)),
        new CommandDescriptor("report --list-kinds", typeof(ListReportKindsRequest), typeof(ReportKindListResponse)),

        // CompareCommands
        new CommandDescriptor("compare", typeof(ProduceComparisonRequest), typeof(CompareResponse)),

        // BaselineCaptureCommand
        new CommandDescriptor("baseline capture", typeof(BaselineCaptureRequest), typeof(BaselineCaptureResponse)),

        // AssessCommand
        new CommandDescriptor("assess", typeof(AssessRequest), typeof(AssessCommandResponse)),

        // StatsCommand
        new CommandDescriptor("stats", typeof(StatsRequest), typeof(StatsCommandResponse)),

        // HandoffCommand
        new CommandDescriptor("handoff", typeof(HandoffRequest), typeof(HandoffCommandResponse)),

        // JobCommands
        new CommandDescriptor(
            "baseline-refresh", typeof(EnqueueBaselineRefreshRequest), typeof(JobEnqueuedResponse)),
        new CommandDescriptor("dry-run", typeof(EnqueueDryRunRequest), typeof(JobEnqueuedResponse)),
        new CommandDescriptor("dry-run --wait", typeof(WaitForDryRunRequest), typeof(DryRunProjection)),
        new CommandDescriptor("trial", typeof(EnqueueTrialRequest), typeof(JobEnqueuedResponse)),
        new CommandDescriptor("trial --wait", typeof(WaitForJobRequest), typeof(JobStatusResponse)),
        new CommandDescriptor("jobs show", typeof(ShowJobRequest), typeof(JobStatusResponse)),
        new CommandDescriptor("jobs assessments", typeof(JobAssessmentsRequest), typeof(JobAssessmentsResponse)),
        new CommandDescriptor("jobs list", typeof(ListActiveJobsRequest), typeof(JobQueueListResponse)),
        new CommandDescriptor("jobs cancel", typeof(CancelJobRequest), typeof(JobStatusResponse)),
        new CommandDescriptor("jobs requeue", typeof(RequeueJobRequest), typeof(JobStatusResponse)),
        new CommandDescriptor("jobs move", typeof(MoveJobRequest), typeof(JobStatusResponse)),
    };
}
