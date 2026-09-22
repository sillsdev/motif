using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.Services;

public sealed partial class CommandClient
{
    public Task<CommandOutcome<ProjectHistoryResponse>> GetProjectHistoryAsync(
        ProjectHistoryRequest request, CancellationToken cancellationToken) =>
        Task.Run(() => ProjectHistoryQuery.Query(request));

    public Task<CommandOutcome<GrammarCheckResponse>> CheckGrammarAsync(
        GrammarCheckRequest request, CancellationToken cancellationToken) =>
        OneAtATime(() => GrammarCheckQuery.Query(request, cancellationToken));

    public Task<CommandOutcome<TextWordsResponse>> ListTextWordsAsync(
        TextWordsRequest request, CancellationToken cancellationToken) =>
        OneAtATime(() => TextWordsQuery.Query(request));

    public Task<CommandOutcome<WordTraceResponse>> TraceWordAsync(
        WordTraceRequest request, CancellationToken cancellationToken) =>
        OneAtATime(() => WordTraceQuery.Query(request, cancellationToken));
}
