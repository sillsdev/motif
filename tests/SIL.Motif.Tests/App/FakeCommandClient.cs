using SIL.Motif.App.Services;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Handoff;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Tests.App;

/// <summary>
/// A deterministic <see cref="ICommandClient"/> for view-model tests: no process, no Avalonia, and no
/// wall-clock race. Each of the four commands is independently scripted to complete, refuse, replay
/// progress steps first, or block until its caller cancels — the block resolves purely from the
/// <see cref="CancellationToken"/> passed to the call, never from a timer.
/// </summary>
public sealed class FakeCommandClient : ICommandClient
{
    private Func<BaselineCaptureRequest, CancellationToken, Task<CommandOutcome<BaselineCaptureResponse>>>
        _captureBaseline = (_, _) => throw NotConfigured(nameof(CaptureBaselineAsync));

    private Func<AssessRequest, IProgress<AssessmentProgress>, CancellationToken,
        Task<CommandOutcome<AssessCommandResponse>>> _assess =
        (_, _, _) => throw NotConfigured(nameof(AssessAsync));

    private Func<StatsRequest, CancellationToken, Task<CommandOutcome<StatsCommandResponse>>> _stats =
        (_, _) => throw NotConfigured(nameof(StatsAsync));

    private Func<HandoffRequest, IProgress<AssessmentProgress>, CancellationToken,
        Task<CommandOutcome<HandoffCommandResponse>>> _handoff =
        (_, _, _) => throw NotConfigured(nameof(HandoffAsync));

    private Func<CancellationToken, Task<IReadOnlyList<KnownProjectSummary>>> _listKnownProjects =
        _ => throw NotConfigured(nameof(ListKnownProjectsAsync));

    private Func<CurrentBaselineRequest, CancellationToken, Task<CommandOutcome<CurrentBaselineResponse>>>
        _currentBaseline = (_, _) => throw NotConfigured(nameof(GetCurrentBaselineAsync));

    private Func<TextInventoryRequest, CancellationToken, Task<CommandOutcome<TextInventoryResponse>>>
        _listTexts = (_, _) => throw NotConfigured(nameof(ListTextsAsync));

    public List<BaselineCaptureRequest> CaptureBaselineRequests { get; } = [];
    public List<AssessRequest> AssessRequests { get; } = [];
    public List<StatsRequest> StatsRequests { get; } = [];
    public List<HandoffRequest> HandoffRequests { get; } = [];
    public List<CurrentBaselineRequest> CurrentBaselineRequests { get; } = [];
    public List<TextInventoryRequest> ListTextsRequests { get; } = [];

    public void OnCaptureBaseline(
        Func<BaselineCaptureRequest, CancellationToken, Task<CommandOutcome<BaselineCaptureResponse>>> behavior) =>
        _captureBaseline = behavior;

    public void CaptureBaselineCompletesWith(BaselineCaptureResponse response) =>
        OnCaptureBaseline((_, _) => Completed(response));

    public void CaptureBaselineRefusesWith(Refusal refusal) =>
        OnCaptureBaseline((_, _) => Refused<BaselineCaptureResponse>(refusal));

    public void CaptureBaselineBlocksUntilCancelled(Refusal onCancelled) =>
        OnCaptureBaseline((_, cancellationToken) => BlockUntilCancelled<BaselineCaptureResponse>(
            cancellationToken, onCancelled));

    public void OnAssess(
        Func<AssessRequest, IProgress<AssessmentProgress>, CancellationToken,
            Task<CommandOutcome<AssessCommandResponse>>> behavior) =>
        _assess = behavior;

    public void AssessCompletesWith(
        AssessCommandResponse response, params AssessmentProgress[] progressSteps) =>
        OnAssess((_, progress, _) => ReportThenComplete(progress, progressSteps, response));

    public void AssessRefusesWith(
        Refusal refusal, params AssessmentProgress[] progressSteps) =>
        OnAssess((_, progress, _) => ReportThenRefuse<AssessCommandResponse>(progress, progressSteps, refusal));

    public void AssessBlocksUntilCancelled(
        Refusal onCancelled, params AssessmentProgress[] progressSteps) =>
        OnAssess((_, progress, cancellationToken) =>
        {
            Report(progress, progressSteps);
            return BlockUntilCancelled<AssessCommandResponse>(cancellationToken, onCancelled);
        });

    public void OnStats(
        Func<StatsRequest, CancellationToken, Task<CommandOutcome<StatsCommandResponse>>> behavior) =>
        _stats = behavior;

    public void StatsCompletesWith(StatsCommandResponse response) =>
        OnStats((_, _) => Completed(response));

    public void StatsRefusesWith(Refusal refusal) =>
        OnStats((_, _) => Refused<StatsCommandResponse>(refusal));

    public void OnHandoff(
        Func<HandoffRequest, IProgress<AssessmentProgress>, CancellationToken,
            Task<CommandOutcome<HandoffCommandResponse>>> behavior) =>
        _handoff = behavior;

    public void HandoffCompletesWith(
        HandoffCommandResponse response, params AssessmentProgress[] progressSteps) =>
        OnHandoff((_, progress, _) => ReportThenComplete(progress, progressSteps, response));

    public void HandoffRefusesWith(
        Refusal refusal, params AssessmentProgress[] progressSteps) =>
        OnHandoff((_, progress, _) => ReportThenRefuse<HandoffCommandResponse>(progress, progressSteps, refusal));

    public void HandoffBlocksUntilCancelled(
        Refusal onCancelled, params AssessmentProgress[] progressSteps) =>
        OnHandoff((_, progress, cancellationToken) =>
        {
            Report(progress, progressSteps);
            return BlockUntilCancelled<HandoffCommandResponse>(cancellationToken, onCancelled);
        });

    public void OnListKnownProjects(
        Func<CancellationToken, Task<IReadOnlyList<KnownProjectSummary>>> behavior) =>
        _listKnownProjects = behavior;

    public void KnownProjectsListIs(IReadOnlyList<KnownProjectSummary> projects) =>
        OnListKnownProjects(_ => Task.FromResult(projects));

    public void OnGetCurrentBaseline(
        Func<CurrentBaselineRequest, CancellationToken, Task<CommandOutcome<CurrentBaselineResponse>>> behavior) =>
        _currentBaseline = behavior;

    public void CurrentBaselineCompletesWith(CurrentBaselineResponse response) =>
        OnGetCurrentBaseline((_, _) => Completed(response));

    public void CurrentBaselineRefusesWith(Refusal refusal) =>
        OnGetCurrentBaseline((_, _) => Refused<CurrentBaselineResponse>(refusal));

    public void OnListTexts(
        Func<TextInventoryRequest, CancellationToken, Task<CommandOutcome<TextInventoryResponse>>> behavior) =>
        _listTexts = behavior;

    public void ListTextsCompletesWith(TextInventoryResponse response) =>
        OnListTexts((_, _) => Completed(response));

    public void ListTextsRefusesWith(Refusal refusal) =>
        OnListTexts((_, _) => Refused<TextInventoryResponse>(refusal));

    public Task<CommandOutcome<BaselineCaptureResponse>> CaptureBaselineAsync(
        BaselineCaptureRequest request, CancellationToken cancellationToken)
    {
        CaptureBaselineRequests.Add(request);
        return _captureBaseline(request, cancellationToken);
    }

    public Task<IReadOnlyList<KnownProjectSummary>> ListKnownProjectsAsync(CancellationToken cancellationToken) =>
        _listKnownProjects(cancellationToken);

    public Task<CommandOutcome<CurrentBaselineResponse>> GetCurrentBaselineAsync(
        CurrentBaselineRequest request, CancellationToken cancellationToken)
    {
        CurrentBaselineRequests.Add(request);
        return _currentBaseline(request, cancellationToken);
    }

    public Task<CommandOutcome<TextInventoryResponse>> ListTextsAsync(
        TextInventoryRequest request, CancellationToken cancellationToken)
    {
        ListTextsRequests.Add(request);
        return _listTexts(request, cancellationToken);
    }

    public Task<CommandOutcome<AssessCommandResponse>> AssessAsync(
        AssessRequest request, IProgress<AssessmentProgress> progress, CancellationToken cancellationToken)
    {
        AssessRequests.Add(request);
        return _assess(request, progress, cancellationToken);
    }

    public Task<CommandOutcome<StatsCommandResponse>> StatsAsync(
        StatsRequest request, CancellationToken cancellationToken)
    {
        StatsRequests.Add(request);
        return _stats(request, cancellationToken);
    }

    public Task<CommandOutcome<HandoffCommandResponse>> HandoffAsync(
        HandoffRequest request, IProgress<AssessmentProgress> progress, CancellationToken cancellationToken)
    {
        HandoffRequests.Add(request);
        return _handoff(request, progress, cancellationToken);
    }

    private static void Report(IProgress<AssessmentProgress> progress, IReadOnlyList<AssessmentProgress> steps)
    {
        foreach (var step in steps) progress.Report(step);
    }

    private static Task<CommandOutcome<T>> ReportThenComplete<T>(
        IProgress<AssessmentProgress> progress, IReadOnlyList<AssessmentProgress> steps, T response)
        where T : class
    {
        Report(progress, steps);
        return Completed(response);
    }

    private static Task<CommandOutcome<T>> ReportThenRefuse<T>(
        IProgress<AssessmentProgress> progress, IReadOnlyList<AssessmentProgress> steps, Refusal refusal)
        where T : class
    {
        Report(progress, steps);
        return Refused<T>(refusal);
    }

    private static Task<CommandOutcome<T>> Completed<T>(T response) where T : class =>
        Task.FromResult(CommandOutcome<T>.Success(response));

    private static Task<CommandOutcome<T>> Refused<T>(Refusal refusal) where T : class =>
        Task.FromResult(CommandOutcome<T>.Refused(refusal));

    // Resolves only from cancellationToken, never a timer: the same call is safe under any test speed.
    private static Task<CommandOutcome<T>> BlockUntilCancelled<T>(
        CancellationToken cancellationToken, Refusal onCancelled) where T : class
    {
        var completion = new TaskCompletionSource<CommandOutcome<T>>(TaskCreationOptions.RunContinuationsAsynchronously);
        cancellationToken.Register(() => completion.TrySetResult(CommandOutcome<T>.Refused(onCancelled)));
        return completion.Task;
    }

    private static InvalidOperationException NotConfigured(string method) =>
        new($"FakeCommandClient.{method} was called with no behavior configured.");
}
