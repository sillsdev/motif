using SIL.Motif.App.Services;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Handoff;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Tests.App.Walkthrough;

internal sealed class HoldingCommandClient : ICommandClient
{
    private readonly ICommandClient _inner;
    private readonly TaskCompletionSource _assessGate;
    private readonly TaskCompletionSource _handoffGate;

    internal HoldingCommandClient(
        ICommandClient inner, bool holdAssess = false, bool holdHandoff = false)
    {
        _inner = inner;
        _assessGate = NewGate(holdAssess);
        _handoffGate = NewGate(holdHandoff);
    }

    internal void ReleaseAssess() => _assessGate.TrySetResult();

    internal void ReleaseHandoff() => _handoffGate.TrySetResult();

    public Task<CommandOutcome<BaselineCaptureResponse>> CaptureBaselineAsync(
        BaselineCaptureRequest request, CancellationToken cancellationToken) =>
        _inner.CaptureBaselineAsync(request, cancellationToken);

    public Task<IReadOnlyList<KnownProjectSummary>> ListKnownProjectsAsync(
        CancellationToken cancellationToken) => _inner.ListKnownProjectsAsync(cancellationToken);

    public Task<CommandOutcome<CurrentBaselineResponse>> GetCurrentBaselineAsync(
        CurrentBaselineRequest request, CancellationToken cancellationToken) =>
        _inner.GetCurrentBaselineAsync(request, cancellationToken);

    public Task<CommandOutcome<TextInventoryResponse>> ListTextsAsync(
        TextInventoryRequest request, CancellationToken cancellationToken) =>
        _inner.ListTextsAsync(request, cancellationToken);

    public async Task<CommandOutcome<AssessCommandResponse>> AssessAsync(
        AssessRequest request, IProgress<AssessmentProgress> progress, CancellationToken cancellationToken)
    {
        await _assessGate.Task.ConfigureAwait(false);
        return await _inner.AssessAsync(request, progress, cancellationToken).ConfigureAwait(false);
    }

    public Task<CommandOutcome<StatsCommandResponse>> StatsAsync(
        StatsRequest request, CancellationToken cancellationToken) =>
        _inner.StatsAsync(request, cancellationToken);

    public async Task<CommandOutcome<HandoffCommandResponse>> HandoffAsync(
        HandoffRequest request, IProgress<AssessmentProgress> progress, CancellationToken cancellationToken)
    {
        await _handoffGate.Task.ConfigureAwait(false);
        return await _inner.HandoffAsync(request, progress, cancellationToken).ConfigureAwait(false);
    }

    private static TaskCompletionSource NewGate(bool held)
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!held) gate.SetResult();
        return gate;
    }
}
