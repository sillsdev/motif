using SIL.Motif.Commands.Catalog;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.Services;

public sealed partial class CommandClient
{
    public Task<CommandOutcome<OverviewResponse>> OverviewAsync(
        OverviewRequest request, CancellationToken cancellationToken) =>
        Task.Run(() => OverviewCommand.Overview(request));

    public Task<CommandOutcome<CurrentEvidenceSnapshot>> ReadCurrentEvidenceAsync(
        string projectPath, CancellationToken cancellationToken) =>
        Task.Run(() => CurrentEvidenceQuery.ReadCurrentEvidence(projectPath), cancellationToken);

    public Task<CommandOutcome<TimingResponse>> TimingAsync(
        TimingRequest request, CancellationToken cancellationToken) =>
        Task.Run(() => TimingCommand.Timing(request));

    public Task<CommandOutcome<ProjectHistoryResponse>> GetProjectHistoryAsync(
        ProjectHistoryRequest request, CancellationToken cancellationToken) =>
        Task.Run(() => ProjectHistoryQuery.Query(request));

    public Task<CommandOutcome<GrammarCheckResponse>> CheckGrammarAsync(
        GrammarCheckRequest request, CancellationToken cancellationToken) =>
        OneAtATime(() => GrammarCheckQuery.Query(request, cancellationToken));

    public Task<CommandOutcome<StoredGrammarCheckResponse>> ReadStoredGrammarCheckAsync(
        GrammarCheckRequest request, CancellationToken cancellationToken) =>
        Task.Run(() => StoredGrammarCheckQuery.Query(request), cancellationToken);

    public Task<CommandOutcome<TextWordsResponse>> ListTextWordsAsync(
        TextWordsRequest request, CancellationToken cancellationToken) =>
        Task.Run(() => TextWordsQuery.Query(request, cancellationToken));

    public Task<CommandOutcome<WordTraceResponse>> TraceWordAsync(
        WordTraceRequest request, CancellationToken cancellationToken) =>
        OneAtATime(() => WordTraceQuery.Query(request, cancellationToken));

}
