using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;

namespace SIL.Motif.Tests.App.Walkthrough;

internal sealed partial class HoldingCommandClient
{
    public Task<CommandOutcome<ProjectHistoryResponse>> GetProjectHistoryAsync(
        ProjectHistoryRequest request, CancellationToken cancellationToken) =>
        _inner.GetProjectHistoryAsync(request, cancellationToken);

    public Task<CommandOutcome<GrammarCheckResponse>> CheckGrammarAsync(
        GrammarCheckRequest request, CancellationToken cancellationToken) =>
        _inner.CheckGrammarAsync(request, cancellationToken);

    public Task<CommandOutcome<TextWordsResponse>> ListTextWordsAsync(
        TextWordsRequest request, CancellationToken cancellationToken) =>
        _inner.ListTextWordsAsync(request, cancellationToken);

    public Task<CommandOutcome<WordTraceResponse>> TraceWordAsync(
        WordTraceRequest request, CancellationToken cancellationToken) =>
        _inner.TraceWordAsync(request, cancellationToken);
}
