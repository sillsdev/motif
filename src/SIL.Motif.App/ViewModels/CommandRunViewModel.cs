using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.ViewModels;

/// <summary>The lifecycle shared by one cancellable command-backed application run.</summary>
public enum RunState
{
    /// <summary>No run is active and no terminal result is being shown.</summary>
    Idle,

    /// <summary>The command is running and can be cancelled.</summary>
    Running,

    /// <summary>Cancellation was requested and the command is unwinding.</summary>
    Cancelling,

    /// <summary>The command returned a typed success.</summary>
    Completed,

    /// <summary>The command returned a typed cancellation refusal.</summary>
    Cancelled,

    /// <summary>The command returned a refusal other than cancellation.</summary>
    Refused,
}

/// <summary>
/// Owns the lifecycle, cancellation, progress, and typed outcome for one application command run.
/// Adapters provide readiness, execution, and success-specific projection while this module preserves
/// the invariant that only a completed command can publish a response.
/// </summary>
public abstract partial class CommandRunViewModel<TResponse> : ObservableObject, IProgress<AssessmentProgress>, IAsyncDisposable
    where TResponse : class
{
    private CancellationTokenSource? _cts;
    private bool _runInFlight;
    private int _runGeneration;

    protected CommandRunViewModel()
    {
        RunCommand = new AsyncRelayCommand(RunAsync, CanRun);
        CancelCommand = new RelayCommand(Cancel, CanCancel);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsActive))]
    private RunState _state = RunState.Idle;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIndeterminate))]
    [NotifyPropertyChangedFor(nameof(ProgressFraction))]
    private AssessmentProgress? _progress;

    [ObservableProperty]
    private TResponse? _result;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RefusalFacts))]
    private Refusal? _refusal;

    public IAsyncRelayCommand RunCommand { get; }

    public IRelayCommand CancelCommand { get; }

    /// <summary>Whether this run is executing or unwinding after cancellation.</summary>
    public bool IsActive => State is RunState.Running or RunState.Cancelling;

    /// <summary>Whether the current progress stage has no meaningful total.</summary>
    public bool IsIndeterminate => Progress?.Total is null;

    /// <summary>The current progress stage's completion fraction, or zero when no total exists.</summary>
    public double ProgressFraction =>
        Progress is { Total: { } total } && total > 0 ? (double)Progress.Completed / total : 0d;

    /// <summary>The current refusal's facts, formatted for an expandable application details list.</summary>
    public IReadOnlyList<string> RefusalFacts =>
        Refusal is null ? [] : Refusal.Facts.Select(fact => $"{fact.Key}: {fact.Value}").ToList();

    /// <summary>Clears the shared result and progress and invokes the adapter's reset hook.</summary>
    public void Reset()
    {
        _runGeneration++;
        _cts?.Cancel();
        Progress = null;
        Result = null;
        Refusal = null;
        State = RunState.Idle;
        OnReset();
        RunCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Cancels an in-flight run and awaits it, so no command outlives this view model.</summary>
    public async ValueTask DisposeAsync()
    {
        Cancel();
        if (_runInFlight && RunCommand.ExecutionTask is { } running)
            await running.ConfigureAwait(false);
    }

    protected abstract bool CanStartCore();

    protected abstract Task<CommandOutcome<TResponse>> ExecuteCoreAsync(CancellationToken cancellationToken);

    /// <summary>Performs any asynchronous preflight; false leaves the current state untouched.</summary>
    protected virtual Task<bool> PrepareRunAsync() => Task.FromResult(true);

    protected virtual void OnRunStarting() { }

    protected virtual void OnRunSucceeded(TResponse response) { }

    protected virtual void OnReset() { }

    partial void OnStateChanged(RunState value)
    {
        RunCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        OnRunStateChanged(value);
    }

    protected virtual void OnRunStateChanged(RunState value) { }

    void IProgress<AssessmentProgress>.Report(AssessmentProgress value) => Progress = value;

    private bool CanRun() =>
        !_runInFlight &&
        State is (RunState.Idle or RunState.Completed or RunState.Cancelled or RunState.Refused) &&
        CanStartCore();

    private bool CanCancel() => State == RunState.Running;

    private async Task RunAsync()
    {
        if (!CanRun()) return;

        var generation = ++_runGeneration;
        _runInFlight = true;
        RunCommand.NotifyCanExecuteChanged();

        CancellationTokenSource? cts = null;
        try
        {
            if (!await PrepareRunAsync().ConfigureAwait(true) || generation != _runGeneration) return;

            cts = new CancellationTokenSource();
            _cts = cts;
            Progress = null;
            Result = null;
            Refusal = null;
            OnRunStarting();
            State = RunState.Running;

            var outcome = await ExecuteCoreAsync(cts.Token).ConfigureAwait(true);
            if (generation != _runGeneration) return;

            if (outcome.Succeeded)
            {
                Result = outcome.Value;
                OnRunSucceeded(outcome.Value!);
                State = RunState.Completed;
            }
            else
            {
                Refusal = outcome.Refusal;
                State = outcome.Refusal!.Reason == FailureReason.Cancelled
                    ? RunState.Cancelled
                    : RunState.Refused;
            }
        }
        finally
        {
            if (ReferenceEquals(_cts, cts)) _cts = null;
            cts?.Dispose();
            _runInFlight = false;
            RunCommand.NotifyCanExecuteChanged();
        }
    }

    private void Cancel()
    {
        if (_cts is null) return;
        State = RunState.Cancelling;
        _cts.Cancel();
    }
}
