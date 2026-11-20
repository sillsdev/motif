using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;

namespace SIL.Motif.App.ViewModels;

public sealed partial class ProposalDocumentViewModel
{
    public IAsyncRelayCommand RunDryRunCommand { get; }
    public IRelayCommand CancelDryRunCommand { get; }

    [ObservableProperty] private bool _isDryRunActive;
    [ObservableProperty] private string _dryRunProgress = string.Empty;
    [ObservableProperty] private WindowRefusal? _dryRunRefusal;

    private bool CanRunDryRun => !_stopped && ShowsReadiness && DifferenceChecked && !IsDryRunActive &&
        _review?.Readiness is { MissingBoundDryRun: true } &&
        !_context.ProposalApply.IsApplying && _context.ProjectPath == ProjectPath;

    partial void OnIsDryRunActiveChanged(bool value) => RaiseApply();

    private async Task RunDryRunAsync(CancellationToken cancellationToken)
    {
        if (!CanRunDryRun) return;
        IsDryRunActive = true;
        DryRunProgress = MotifAppStrings.ProposalDryRunQueued;
        DryRunRefusal = null;
        var progress = new Progress<JobStatusResponse>(status =>
        {
            if (_stopped || !IsDryRunActive || cancellationToken.IsCancellationRequested) return;
            DryRunProgress = status.Status switch
            {
                JobStatus.Queued or JobStatus.WaitingForProjectHost => MotifAppStrings.ProposalDryRunQueued,
                JobStatus.WaitingForBaseline => MotifAppStrings.ProposalDryRunWaitingForBaseline,
                JobStatus.CompletedDryRunOnly => MotifAppStrings.ProposalDryRunBinding,
                _ => MotifAppStrings.ProposalDryRunRunning,
            };
        });
        try
        {
            var version = MotifProductVersion.CurrentText;
            var queued = await _context.Commands.EnqueueDryRunAsync(
                new EnqueueDryRunRequest(ProjectPath, version, ProposalId), cancellationToken).ConfigureAwait(true);
            if (!queued.Succeeded)
            {
                ShowDryRunRefusal(queued.Refusal!);
                return;
            }

            var outcome = await _context.Commands.WaitForDryRunAsync(
                new WaitForDryRunRequest(ProjectPath, version, ProposalId, queued.Value!.JobId, JobCommands.DefaultWaitTimeout),
                progress, cancellationToken).ConfigureAwait(true);
            if (_stopped || _context.ProjectPath != ProjectPath) return;
            if (!outcome.Succeeded)
            {
                ShowDryRunRefusal(outcome.Refusal!);
                return;
            }
            if (!outcome.Value!.EvidenceCurrent)
                DryRunRefusal = WindowRefusal.Plain(MotifAppStrings.ProposalDryRunOutdated);
            await RefreshAsync().ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (!_stopped) DryRunRefusal = WindowRefusal.Plain(MotifAppStrings.ProposalDryRunCancelled);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (!_stopped) DryRunRefusal = WindowRefusal.Failure(
                RefusalCodes.WindowDryRunFailed, MotifAppStrings.ProposalDryRunFailed, exception);
        }
        finally
        {
            IsDryRunActive = false;
        }
    }

    private void ShowDryRunRefusal(Refusal refusal)
    {
        if (_stopped) return;
        DryRunRefusal = refusal.Reason == FailureReason.Cancelled
            ? WindowRefusal.Plain(MotifAppStrings.ProposalDryRunCancelled)
            : WindowRefusal.From(refusal, MotifAppStrings.ProposalDryRunFailed);
    }

    private void CancelDryRun()
    {
        RunDryRunCommand.Cancel();
        DryRunProgress = MotifAppStrings.ProposalDryRunCancelling;
        CancelDryRunCommand.NotifyCanExecuteChanged();
    }
}
