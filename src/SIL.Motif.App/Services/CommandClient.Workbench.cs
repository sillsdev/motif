using SIL.Motif.Commands;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Commands.SelectionReading;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.Services;

public sealed partial class CommandClient
{
    public Task<CommandOutcome<WordContextResponse>> ReadWordContextAsync(
        WordContextRequest request, CancellationToken cancellationToken) =>
        OneAtATime(() => WordContextQuery.Query(request), cancellationToken);

    public Task<CommandOutcome<OverviewResponse>> OverviewAsync(
        OverviewRequest request, CancellationToken cancellationToken) =>
        Task.Run(() => OverviewCommand.Overview(request));

    public Task<CommandOutcome<ProposalStandings>> ProposalStandingsAsync(
        ProposalStandingsRequest request, CancellationToken cancellationToken) =>
        cancellationToken.IsCancellationRequested
            ? Task.FromResult(WaitCancelled<ProposalStandings>())
            : Task.Run(() => ProposalStandingsCommand.Read(request));

    public Task<CommandOutcome<ProposalDeletedResponse>> DeleteProposalAsync(
        DeleteProposalRequest request, CancellationToken cancellationToken) =>
        cancellationToken.IsCancellationRequested
            ? Task.FromResult(WaitCancelled<ProposalDeletedResponse>())
            : Task.Run(() => ProposalLifecycleCommand.Delete(request));

    public Task<CommandOutcome<CurrentEvidenceSnapshot>> ReadCurrentEvidenceAsync(
        string projectPath, CancellationToken cancellationToken) =>
        Task.Run(() => CurrentEvidenceQuery.ReadCurrentEvidence(projectPath));

    public Task<CommandOutcome<TrialRecord?>> ReadTrialAsync(
        ReadTrialRequest request, CancellationToken cancellationToken) =>
        cancellationToken.IsCancellationRequested
            ? Task.FromResult(WaitCancelled<TrialRecord?>())
            : Task.Run(() => Trials.Read(request));

    public async Task<CommandOutcome<TrialEnqueued>> EnqueueTrialAsync(
        SIL.Motif.Commands.Requests.EnqueueTrialRequest request, CancellationToken cancellationToken)
    {
        var queued = await OneAtATime(() => TrialWorkflow.Enqueue(request, _managedRoot, _options.ParserPath,
                BaselineMode.BestEffort, cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        if (queued.Succeeded) _options.RunnerLauncher.Start(request.FwDataPath);
        return queued;
    }

    public Task<CommandOutcome<SIL.Motif.Contract.Jobs.JobStatusResponse>> WaitForJobAsync(
        WaitForJobRequest request, IProgress<SIL.Motif.Contract.Jobs.JobStatusResponse>? progress,
        CancellationToken cancellationToken) =>
        JobWait.WaitAsync(request.FwDataPath, request.JobId, progress, cancellationToken,
            request.Timeout == Timeout.InfiniteTimeSpan ? null : request.Timeout, request.ProductVersion,
            wakeRunner: () => _options.RunnerLauncher.Start(request.FwDataPath));

    public Task<CommandOutcome<TimingResponse>> TimingAsync(
        TimingRequest request, CancellationToken cancellationToken) =>
        Task.Run(() => TimingCommand.Timing(request));

    public Task<CommandOutcome<ProjectHistoryResponse>> GetProjectHistoryAsync(
        ProjectHistoryRequest request, CancellationToken cancellationToken) =>
        Task.Run(() => ProjectHistoryQuery.Query(request));

    public Task<CommandOutcome<GrammarCheckResponse>> CheckGrammarAsync(
        GrammarCheckRequest request, CancellationToken cancellationToken) =>
        OneAtATime(() => GrammarCheckQuery.Query(request, _options.ParserPath, cancellationToken), cancellationToken);

    public Task<CommandOutcome<StoredGrammarCheckResponse>> ReadStoredGrammarCheckAsync(
        GrammarCheckRequest request, CancellationToken cancellationToken) =>
        Task.Run(() => StoredGrammarCheckQuery.Query(request));

    public Task<CommandOutcome<SelectionReader>> OpenSelectionReaderAsync(
        OpenSelectionReaderRequest request, CancellationToken cancellationToken) =>
        SelectionReader.OpenAsync(request, cancellationToken);

    public Task<CommandOutcome<WordTraceResponse>> TraceWordAsync(
        WordTraceRequest request, CancellationToken cancellationToken, IProgress<AssessmentProgress>? progress = null) =>
        ParseOneAtATime(() => OneAtATime(() => WordTraceQuery.Query(request, _options.ParserPath,
            cancellationToken, progress is null ? null : progress.Report), cancellationToken));

    // The facts come from a scratch LibLCM cache of the Baseline, so the read waits its turn like the parser.
    public Task<CommandOutcome<InspectResponse>> InspectAsync(InspectRequest request, CancellationToken cancellationToken) =>
        OneAtATime(() => InspectQuery.Query(request), cancellationToken);

    public Task<CommandOutcome<RetirementReviewQueryResponse>> ReadRetirementReviewAsync(
        ReadRetirementReviewRequest request, CancellationToken cancellationToken) =>
        OneAtATime(() => ParsimonyCommands.ReadRetirementReview(request), cancellationToken);

    public Task<CommandOutcome<ParsimonyLatestReportResponse>> ReadLatestParsimonyReportAsync(
        ReadLatestParsimonyReportRequest request, CancellationToken cancellationToken) =>
        Task.Run(() => ParsimonyCommands.ReadLatest(request));

    public Task<CommandOutcome<ParsimonyReportResponse>> ReadParsimonyReportAsync(
        ShowParsimonyReportRequest request, CancellationToken cancellationToken) =>
        Task.Run(() => ParsimonyCommands.Show(request));

    public Task<CommandOutcome<ParsimonyNamedViewResponse>> ReadParsimonyViewAsync(
        ReadParsimonyViewRequest request, CancellationToken cancellationToken) =>
        Task.Run(() => ParsimonyViewsCommand.View(request), cancellationToken);

    public Task<CommandOutcome<NotebookRecordTypesResponse>> ListNotebookRecordTypesAsync(
        ListNotebookRecordTypesRequest request, CancellationToken cancellationToken) =>
        Task.Run(() => ParsimonyCommands.ListNotebookRecordTypes(request), cancellationToken);

    public Task<CommandOutcome<ComposedOperationsResponse>> RecordParsimonyDispositionAsync(
        RecordParsimonyDispositionFromFindingRequest request, CancellationToken cancellationToken) =>
        OneAtATime(() => ParsimonyCommands.RecordDisposition(request), cancellationToken);

    public Task<CommandOutcome<ComposedOperationsResponse>> RetractParsimonyDispositionAsync(
        RetractParsimonyDispositionRequest request, CancellationToken cancellationToken) =>
        OneAtATime(() => ParsimonyCommands.RetractDisposition(request), cancellationToken);

}
