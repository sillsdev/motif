using SIL.Motif.Commands.Catalog;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Commands.SelectionReading;
using SIL.Motif.Contract.Commands;
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

    public Task<CommandOutcome<CurrentEvidenceSnapshot>> ReadCurrentEvidenceAsync(
        string projectPath, CancellationToken cancellationToken) =>
        Task.Run(() => CurrentEvidenceQuery.ReadCurrentEvidence(projectPath));

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

}
