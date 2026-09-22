using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Tests.App;

// Each defaults to an empty success, so a test that never mentions these queries is not failed by them.
public sealed partial class FakeCommandClient
{
    private Func<ProjectHistoryRequest, CancellationToken, Task<CommandOutcome<ProjectHistoryResponse>>>
        _projectHistory = (_, _) => Completed(new ProjectHistoryResponse([]));

    private Func<GrammarCheckRequest, CancellationToken, Task<CommandOutcome<GrammarCheckResponse>>>
        _checkGrammar = (_, _) => Completed(new GrammarCheckResponse([], HasBaseline: true));

    private Func<TextWordsRequest, CancellationToken, Task<CommandOutcome<TextWordsResponse>>>
        _listTextWords = (_, _) => Completed(new TextWordsResponse([], [], HasBaseline: true));

    private Func<WordTraceRequest, CancellationToken, Task<CommandOutcome<WordTraceResponse>>>
        _traceWord = (_, _) => Refused<WordTraceResponse>(
            new Refusal("trace.not-configured", FailureReason.Refused, "No trace configured."));

    public List<ProjectHistoryRequest> ProjectHistoryRequests { get; } = [];
    public List<GrammarCheckRequest> CheckGrammarRequests { get; } = [];
    public List<TextWordsRequest> ListTextWordsRequests { get; } = [];
    public List<WordTraceRequest> TraceWordRequests { get; } = [];

    public void OnProjectHistory(
        Func<ProjectHistoryRequest, CancellationToken, Task<CommandOutcome<ProjectHistoryResponse>>> behavior) =>
        _projectHistory = behavior;

    public void ProjectHistoryIs(ProjectHistoryResponse response) => OnProjectHistory((_, _) => Completed(response));

    public void OnCheckGrammar(
        Func<GrammarCheckRequest, CancellationToken, Task<CommandOutcome<GrammarCheckResponse>>> behavior) =>
        _checkGrammar = behavior;

    public void CheckGrammarCompletesWith(GrammarCheckResponse response) =>
        OnCheckGrammar((_, _) => Completed(response));

    public void CheckGrammarRefusesWith(Refusal refusal) =>
        OnCheckGrammar((_, _) => Refused<GrammarCheckResponse>(refusal));

    public void OnListTextWords(
        Func<TextWordsRequest, CancellationToken, Task<CommandOutcome<TextWordsResponse>>> behavior) =>
        _listTextWords = behavior;

    public void ListTextWordsCompletesWith(TextWordsResponse response) =>
        OnListTextWords((_, _) => Completed(response));

    public void OnTraceWord(
        Func<WordTraceRequest, CancellationToken, Task<CommandOutcome<WordTraceResponse>>> behavior) =>
        _traceWord = behavior;

    public void TraceWordCompletesWith(WordTraceResponse response) => OnTraceWord((_, _) => Completed(response));

    public Task<CommandOutcome<ProjectHistoryResponse>> GetProjectHistoryAsync(
        ProjectHistoryRequest request, CancellationToken cancellationToken)
    {
        ProjectHistoryRequests.Add(request);
        return _projectHistory(request, cancellationToken);
    }

    public Task<CommandOutcome<GrammarCheckResponse>> CheckGrammarAsync(
        GrammarCheckRequest request, CancellationToken cancellationToken)
    {
        CheckGrammarRequests.Add(request);
        return _checkGrammar(request, cancellationToken);
    }

    public Task<CommandOutcome<TextWordsResponse>> ListTextWordsAsync(
        TextWordsRequest request, CancellationToken cancellationToken)
    {
        ListTextWordsRequests.Add(request);
        return _listTextWords(request, cancellationToken);
    }

    public Task<CommandOutcome<WordTraceResponse>> TraceWordAsync(
        WordTraceRequest request, CancellationToken cancellationToken)
    {
        TraceWordRequests.Add(request);
        return _traceWord(request, cancellationToken);
    }
}
