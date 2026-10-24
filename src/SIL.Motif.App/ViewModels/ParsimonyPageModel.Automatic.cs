using SIL.Motif.Commands.Requests;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Host.Parsimony;

namespace SIL.Motif.App.ViewModels;

public sealed partial class ParsimonyPageModel
{
    private CancellationTokenSource? _automaticCancellation;
    private Task _automaticWork = Task.CompletedTask;
    private int _operationGeneration;
    private string? _operationPath;
    private BaselineToken? _operationBaseline;
    private BaselineToken? _reusableBaseline;
    private bool _openCompleted;
    private bool _canProduce;

    internal Task AutomaticWork => _automaticWork;

    protected override void OnProjectOpenCompleted()
    {
        _openCompleted = true;
        _ = StartAutomatic();
    }

    protected override async Task OnBaselineCapturedAsync(CancellationToken cancellationToken)
    {
        if (!Context.AdvancedAiModeEnabled || Context.ProjectPath is not { } path) return;
        if (!_automaticWork.IsCompleted && _operationPath == path && _operationBaseline == Context.Baseline?.Token)
            return;
        ++_readGeneration;
        _remainingCancellation?.Cancel();
        await StopAutomaticAsync().ConfigureAwait(true);
        cancellationToken.ThrowIfCancellationRequested();
        var readGeneration = _readGeneration + 1;
        await LoadAsync(path, cancellationToken).ConfigureAwait(true);
        if (readGeneration != _readGeneration || Context.ProjectPath != path || cancellationToken.IsCancellationRequested) return;
        _ = StartAutomatic();
    }

    protected override async Task OnStopWorkAsync()
    {
        ++_readGeneration;
        _remainingCancellation?.Cancel();
        try { await StopAutomaticAsync().ConfigureAwait(true); }
        finally { await StopRemainingAsync().ConfigureAwait(true); }
    }

    private Task StartAutomatic(bool retry = false)
    {
        if (!Context.AdvancedAiModeEnabled || !_openCompleted || !_canProduce || Context.ProjectPath is not { } path ||
            Context.Baseline is not { HasBaseline: true, Token: { } token } || _reusableBaseline == token)
            return Task.CompletedTask;
        if (!_automaticWork.IsCompleted) return _automaticWork;
        if (!retry && _operationPath == path && _operationBaseline == token) return _automaticWork;
        _automaticCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _automaticCancellation = cancellation;
        _operationPath = path;
        _operationBaseline = token;
        var generation = ++_operationGeneration;
        State = ParsimonyPageState.Loading;
        Message = string.Empty;
        NextStep = string.Empty;
        _automaticWork = RunAutomaticAsync(path, token, generation, cancellation.Token);
        return _automaticWork;
    }

    private async Task RunAutomaticAsync(string path, BaselineToken token, int generation, CancellationToken cancellationToken)
    {
        try
        {
            var queued = await Context.Commands.EnqueueParsimonyReportAsync(new EnqueueParsimonyReportRequest(
                path, MotifProductVersion.CurrentText, ParsimonyAdmission.AllMeasures,
                ParsimonyEvidenceScopeKind.ProjectApproved), cancellationToken).ConfigureAwait(true);
            if (!queued.Succeeded)
            {
                if (IsCurrentOperation(path, token, generation, cancellationToken)) ShowRefusal(queued.Refusal!);
                return;
            }
            // Even a superseded enqueue must enter the wait so its cancelled token cancels the durable job.
            var waited = await WaitOwnedJobAsync(path, queued.Value!.JobId, cancellationToken).ConfigureAwait(true);
            if (!IsCurrentOperation(path, token, generation, cancellationToken)) return;
            if (!waited.Succeeded)
            {
                ShowRefusal(waited.Refusal!);
                return;
            }
            if (waited.Value!.Status != JobStatus.Completed)
            {
                ShowRefusal(new Refusal(ParsimonyRefusalCodes.JobFailed, FailureReason.Refused,
                    MotifAppStrings.ParsimonyJobFailed));
                return;
            }
            var current = await Context.Commands.GetCurrentBaselineAsync(new CurrentBaselineRequest(path), cancellationToken)
                .ConfigureAwait(true);
            if (!IsCurrentOperation(path, token, generation, cancellationToken)) return;
            if (!current.Succeeded)
            {
                ShowRefusal(current.Refusal!);
                return;
            }
            if (current.Value?.Token != token)
            {
                ShowRefusal(new Refusal(ParsimonyRefusalCodes.ReportBaselineMismatch, FailureReason.Refused,
                    MotifAppStrings.ParsimonyBaselineChanged));
                return;
            }
            await LoadAsync(path, cancellationToken).ConfigureAwait(true);
            if (IsCurrentOperation(path, token, generation, cancellationToken) && _reusableBaseline != token)
                ShowRefusal(new Refusal(ParsimonyRefusalCodes.ResultMissing, FailureReason.StoreInconsistent,
                    MotifAppStrings.ParsimonyReportUnavailable));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            if (IsCurrentOperation(path, token, generation, cancellationToken))
                ShowRefusal(new Refusal(ParsimonyRefusalCodes.AutomaticFailed, FailureReason.Refused, exception.Message));
        }
    }

    private async Task<CommandOutcome<JobStatusResponse>> WaitOwnedJobAsync(string path, string jobId,
        CancellationToken cancellationToken)
    {
        var request = new WaitForJobRequest(path, jobId, MotifProductVersion.CurrentText, Timeout.InfiniteTimeSpan);
        CommandOutcome<JobStatusResponse>? outcome = null;
        Exception? fault = null;
        try
        {
            outcome = await Context.Commands.WaitForJobAsync(request, null, cancellationToken).ConfigureAwait(true);
        }
        catch (Exception exception) { fault = exception; }
        if (outcome?.Succeeded != true)
        {
            try
            {
                await Context.Commands.WaitForJobAsync(request, null, new CancellationToken(canceled: true))
                    .ConfigureAwait(true);
            }
            finally
            {
                // A cancellation request does not prove that the runner released its Baseline and parser yet.
                var drained = await Context.Commands.WaitForJobAsync(request, null, CancellationToken.None)
                    .ConfigureAwait(true);
                if (!drained.Succeeded) throw new InvalidOperationException(drained.Refusal!.Message);
            }
        }
        if (fault is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(fault).Throw();
        return outcome!;
    }

    private bool IsCurrentOperation(string path, BaselineToken token, int generation, CancellationToken cancellationToken) =>
        !cancellationToken.IsCancellationRequested && generation == _operationGeneration &&
        Context.AdvancedAiModeEnabled && Context.ProjectPath == path && Context.Baseline?.Token == token;

    private async Task StopAutomaticAsync()
    {
        ++_operationGeneration;
        var cancellation = _automaticCancellation;
        var work = _automaticWork;
        cancellation?.Cancel();
        try { await work.ConfigureAwait(true); }
        finally
        {
            if (ReferenceEquals(cancellation, _automaticCancellation))
            {
                _automaticCancellation = null;
                _operationPath = null;
                _operationBaseline = null;
                cancellation?.Dispose();
            }
        }
    }

    private async Task StopRemainingAsync()
    {
        var cancellation = _remainingCancellation;
        var work = RemainingLoad;
        cancellation?.Cancel();
        await work.ConfigureAwait(true);
        if (ReferenceEquals(cancellation, _remainingCancellation))
        {
            _remainingCancellation = null;
            RemainingLoad = Task.CompletedTask;
            cancellation?.Dispose();
        }
    }
}
