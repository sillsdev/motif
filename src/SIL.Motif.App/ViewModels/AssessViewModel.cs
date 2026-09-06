using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.App.Services;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.ViewModels;

/// <summary>The run strip's own state machine for one synchronous Assessment invocation.</summary>
public enum AssessRunState
{
    /// <summary>No run has started, or the previous one already reached a terminal state.</summary>
    Idle,

    /// <summary>The command is running; Run is disabled and Cancel is available.</summary>
    Running,

    /// <summary>Cancel was requested; waiting for the command to unwind and return its refusal.</summary>
    Cancelling,

    /// <summary>The command returned a typed success.</summary>
    Completed,

    /// <summary>The command returned <c>assessment.cancelled</c> after a requested cancellation.</summary>
    Cancelled,

    /// <summary>The command returned a refusal other than cancellation.</summary>
    Refused,
}

/// <summary>
/// Runs one synchronous Assessment over the current Selection, reporting the command's own progress
/// stages and honouring cancellation. Never invents progress finer than <see cref="AssessmentStage"/>
/// reports, and never turns an unexpected exception from <see cref="ICommandClient"/> into a Refusal —
/// that distinction belongs to the command, not this view model.
/// </summary>
public sealed partial class AssessViewModel : ObservableObject, IProgress<AssessmentProgress>, IAsyncDisposable
{
    private const string CancelledRefusalCode = "assessment.cancelled";

    private readonly ICommandClient _commandClient;
    private readonly SelectionViewModel _selection;
    private CancellationTokenSource? _cts;

    public AssessViewModel(ICommandClient commandClient, SelectionViewModel selection)
    {
        ArgumentNullException.ThrowIfNull(commandClient);
        ArgumentNullException.ThrowIfNull(selection);
        _commandClient = commandClient;
        _selection = selection;
        _selection.PropertyChanged += OnSelectionPropertyChanged;
        RunCommand = new AsyncRelayCommand(RunAsync, CanRun);
        CancelCommand = new RelayCommand(Cancel, CanCancel);
    }

    /// <summary>The project this Assessment measures, or <c>null</c> before a project has been chosen.</summary>
    [ObservableProperty]
    private string? _projectPath;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsActive))]
    private AssessRunState _state = AssessRunState.Idle;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIndeterminate))]
    [NotifyPropertyChangedFor(nameof(ProgressFraction))]
    private AssessmentProgress? _progress;

    // The command's typed success, once State reaches AssessRunState.Completed.
    [ObservableProperty]
    private AssessCommandResponse? _result;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RefusalFacts))]
    private Refusal? _refusal;

    public IAsyncRelayCommand RunCommand { get; }

    public IRelayCommand CancelCommand { get; }

    /// <summary>Whether project and Selection controls should show disabled while a run is in flight.</summary>
    public bool IsActive => State is AssessRunState.Running or AssessRunState.Cancelling;

    /// <summary>
    /// Whether the progress bar must render indeterminate: PanGloss's own stages do not all carry a total,
    /// and a missing <see cref="AssessmentProgress.Total"/> is never replaced with a fabricated denominator.
    /// </summary>
    public bool IsIndeterminate => Progress?.Total is null;

    /// <summary>The current stage's own completion fraction, meaningful only when <see cref="IsIndeterminate"/> is false.</summary>
    public double ProgressFraction =>
        Progress is { Total: { } total } && total > 0 ? (double)Progress.Completed / total : 0d;

    /// <summary>The current <see cref="Refusal"/>'s facts, rendered as one line per entry for an expandable details list.</summary>
    public IReadOnlyList<string> RefusalFacts =>
        Refusal is null ? [] : Refusal.Facts.Select(fact => $"{fact.Key}: {fact.Value}").ToList();

    partial void OnProjectPathChanged(string? value) => RunCommand.NotifyCanExecuteChanged();

    partial void OnStateChanged(AssessRunState value)
    {
        RunCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
    }

    void IProgress<AssessmentProgress>.Report(AssessmentProgress value) => Progress = value;

    private void OnSelectionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SelectionViewModel.CanAssess)) RunCommand.NotifyCanExecuteChanged();
    }

    private bool CanRun() =>
        ProjectPath is not null && _selection.CanAssess
        && State is AssessRunState.Idle or AssessRunState.Completed or AssessRunState.Cancelled
            or AssessRunState.Refused;

    private bool CanCancel() => State == AssessRunState.Running;

    private async Task RunAsync()
    {
        if (ProjectPath is null) return;

        var cts = new CancellationTokenSource();
        _cts = cts;
        Progress = null;
        Result = null;
        Refusal = null;
        State = AssessRunState.Running;

        try
        {
            var request = new AssessRequest(ProjectPath, _selection.BuildRequest());
            var outcome = await _commandClient.AssessAsync(request, this, cts.Token);
            if (outcome.Succeeded)
            {
                Result = outcome.Value;
                State = AssessRunState.Completed;
            }
            else
            {
                Refusal = outcome.Refusal;
                State = outcome.Refusal!.Code == CancelledRefusalCode
                    ? AssessRunState.Cancelled
                    : AssessRunState.Refused;
            }
        }
        finally
        {
            _cts = null;
            cts.Dispose();
        }
    }

    private void Cancel()
    {
        if (_cts is null) return;
        State = AssessRunState.Cancelling;
        _cts.Cancel();
    }

    /// <summary>Cancels an in-flight run and awaits it, so nothing keeps running past this view model's lifetime.</summary>
    public async ValueTask DisposeAsync()
    {
        Cancel();
        if (RunCommand.ExecutionTask is { } running) await running.ConfigureAwait(false);
    }
}
