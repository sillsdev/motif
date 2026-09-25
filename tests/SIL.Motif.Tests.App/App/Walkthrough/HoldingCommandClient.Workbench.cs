using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Tests.App.Walkthrough;

internal sealed partial class HoldingCommandClient
{
    public Task<CommandOutcome<OverviewResponse>> OverviewAsync(
        OverviewRequest request, CancellationToken cancellationToken) =>
        _inner.OverviewAsync(request, cancellationToken);

    public Task<CommandOutcome<CurrentEvidenceSnapshot>> ReadCurrentEvidenceAsync(
        string projectPath, CancellationToken cancellationToken) =>
        _inner.ReadCurrentEvidenceAsync(projectPath, cancellationToken);

    public Task<CommandOutcome<TimingResponse>> TimingAsync(
        TimingRequest request, CancellationToken cancellationToken) =>
        _inner.TimingAsync(request, cancellationToken);

    public Task<CommandOutcome<ProjectHistoryResponse>> GetProjectHistoryAsync(
        ProjectHistoryRequest request, CancellationToken cancellationToken) =>
        _inner.GetProjectHistoryAsync(request, cancellationToken);

    public Task<CommandOutcome<GrammarCheckResponse>> CheckGrammarAsync(
        GrammarCheckRequest request, CancellationToken cancellationToken) =>
        _inner.CheckGrammarAsync(request, cancellationToken);

    public Task<CommandOutcome<StoredGrammarCheckResponse>> ReadStoredGrammarCheckAsync(
        GrammarCheckRequest request, CancellationToken cancellationToken) =>
        _inner.ReadStoredGrammarCheckAsync(request, cancellationToken);

    public Task<CommandOutcome<TextWordsResponse>> ListTextWordsAsync(
        TextWordsRequest request, CancellationToken cancellationToken) =>
        _inner.ListTextWordsAsync(request, cancellationToken);

    public Task<CommandOutcome<WordTraceResponse>> TraceWordAsync(
        WordTraceRequest request, CancellationToken cancellationToken) =>
        _inner.TraceWordAsync(request, cancellationToken);

}
