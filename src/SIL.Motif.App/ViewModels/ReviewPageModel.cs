using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.App.Services;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.ViewModels;

/// <summary>
/// The Review changes page's model: the pending changes every page adds to, before any is written to FieldWorks,
/// counted beside the page's sidebar label.
/// </summary>
public sealed class ReviewPageModel : PageModel
{
    private CancellationTokenSource? _measurementCancellation;
    private CancellationTokenSource? _applyCancellation;

    public ReviewPageModel(WorkspaceContext context) : base(context)
    {
        Changes.PropertyChanged += OnChangesChanged;
        context.PropertyChanged += OnContextPropertyChanged;
        RemoveNonFittingCommand = new AsyncRelayCommand(RemoveNonFittingAsync,
            () => Changes.Items.Any(item => item.IsNoLongerFits));
        CheckAgainCommand = new AsyncRelayCommand(() => Changes.RecheckAsync(),
            () => HasNonFittingChanges && Context.CurrentEvidence?.Freshness != EvidenceFreshness.Stale);
        MeasureCommand = new AsyncRelayCommand(MeasureAsync,
            () => Changes.HasItems && Context.HasProject && !IsMeasuring);
        CancelMeasureCommand = new RelayCommand(() => _measurementCancellation?.Cancel(), () => IsMeasuring);
        ApplyCommand = new AsyncRelayCommand(ApplyAsync, () => CanApply);
        KeepEditingCommand = new RelayCommand(() => Context.OpenPage(
            Changes.Items.FirstOrDefault()?.OriginPage ?? WorkspacePage.Texts));
    }

    public ChangesViewModel Changes => Context.Changes;

    /// <summary>The open project's file name, which the apply card names.</summary>
    public string ProjectName => Context.ProjectName;

    /// <summary>Removes only the changes that no longer fit the FieldWorks project.</summary>
    public IAsyncRelayCommand RemoveNonFittingCommand { get; }

    /// <summary>Checks unchanged identities against the refreshed Baseline and renews their evidence.</summary>
    public IAsyncRelayCommand CheckAgainCommand { get; }

    /// <summary>Starts a Trial of the touched words only when the person asks for one.</summary>
    public IAsyncRelayCommand MeasureCommand { get; }

    /// <summary>Stops the active check of the touched words.</summary>
    public IRelayCommand CancelMeasureCommand { get; }

    /// <summary>Writes the measured changes as one atomic Apply.</summary>
    public IAsyncRelayCommand ApplyCommand { get; }

    /// <summary>Returns to the page where the first pending change was collected.</summary>
    public IRelayCommand KeepEditingCommand { get; }

    /// <summary>Whether the requested measurement is still running.</summary>
    public bool IsMeasuring { get; private set; }

    /// <summary>Whether Apply is writing the pending changes to FieldWorks.</summary>
    public bool IsApplying { get; private set; }

    /// <summary>Completed words, total words, and current word while measuring.</summary>
    public string MeasurementProgressText { get; private set; } = string.Empty;

    /// <summary>The result recorded when Apply wrote the changes.</summary>
    public ApplyProjection? Receipt { get; private set; }

    public bool HasReceipt => Receipt is not null;

    public bool HasNonFittingChanges => Changes.Items.Any(item => item.IsNoLongerFits);

    /// <summary>The Apply result in words without internal command vocabulary.</summary>
    public string ReceiptText => Receipt is null ? string.Empty :
        $"Changes applied to {ProjectName}. Receipt recorded at {Receipt.AppliedLogEntry.TimestampUtc}.";

    /// <summary>Why the last Apply could not finish.</summary>
    public string? ApplyError { get; private set; }

    /// <summary>The words on the action that writes the measured changes.</summary>
    public string ApplyButtonText => "Apply to FieldWorks project";

    /// <summary>The command's measured word counts for this exact pending revision.</summary>
    public string NumbersText { get; private set; } = "See what applying does to the numbers.";

    /// <summary>Why the last requested measurement could not complete.</summary>
    public string? MeasurementError { get; private set; }

    /// <summary>Whether the measured changes can be applied to the FieldWorks project.</summary>
    public bool CanApply => Changes.HasItems && Changes.Items.All(item => item.Fit is { StillFits: true }) &&
        Context.Baseline?.FieldWorksHeldProject != true &&
        Context.CurrentEvidence?.Freshness != EvidenceFreshness.Stale &&
        EvidenceComplete && !IsMeasuring && !IsApplying;

    /// <summary>What prevents Apply, in words shown beside the action.</summary>
    public string ApplyBlockReason => IsApplying ? "Applying changes to FieldWorks..." :
        !Changes.HasItems ? "Choose a change in Texts to begin." :
        Changes.Items.Any(item => item.IsNoLongerFits)
            ? "No longer fits: remove the changes that no longer fit before applying."
            : Context.CurrentEvidence?.Freshness == EvidenceFreshness.Stale
                ? "FieldWorks saved since these numbers were measured. Refresh before applying."
            : Context.Baseline?.FieldWorksHeldProject == true
                ? "FieldWorks has this project open. Close it before applying changes."
                : !EvidenceComplete ? "See what applying does to the numbers before applying." : string.Empty;

    private bool EvidenceComplete { get; set; }

    private async Task MeasureAsync()
    {
        if (Context.ProjectPath is not { } project || Changes.Snapshot.DraftId is not { } draft) return;
        _measurementCancellation?.Dispose();
        _measurementCancellation = new CancellationTokenSource();
        IsMeasuring = true;
        EvidenceComplete = false;
        MeasurementError = null;
        OnPropertyChanged(nameof(IsMeasuring));
        OnPropertyChanged(nameof(CanApply));
        OnPropertyChanged(nameof(MeasurementError));
        MeasureCommand.NotifyCanExecuteChanged();
        CancelMeasureCommand.NotifyCanExecuteChanged();
        ApplyCommand.NotifyCanExecuteChanged();
        var revision = Changes.Snapshot.Revision;
        var words = Changes.Snapshot.Changes.Select(change => change.Word)
            .Distinct(StringComparer.Ordinal).ToArray();
        CommandOutcome<MeasurePendingResult> result;
        try
        {
            result = await Context.Commands.MeasurePendingAsync(
                new MeasurePendingRequest(project, draft, revision, words,
                    Context.Evidence?.Assessment.Measurements.FirstOrDefault(measurement =>
                        measurement.Kind == "Correctness")?.AssessmentId),
                new Progress<MeasureProgress>(OnMeasurementProgress),
                _measurementCancellation.Token).ConfigureAwait(true);
        }
        finally
        {
            IsMeasuring = false;
            OnPropertyChanged(nameof(IsMeasuring));
            MeasureCommand.NotifyCanExecuteChanged();
            CancelMeasureCommand.NotifyCanExecuteChanged();
            ApplyCommand.NotifyCanExecuteChanged();
        }
        if (revision != Changes.Snapshot.Revision) return;
        EvidenceComplete = result.Succeeded && result.Value is { EvidenceComplete: true } measured &&
            measured.Revision == revision;
        MeasurementError = result.Refusal is { } measureRefusal
            ? UserFacingRefusal.MessageOf(measureRefusal)
            : !EvidenceComplete ? "Some words did not finish or their analysis could not be checked." : null;
        if (result.Value is { } evidence) NumbersText = evidence.NumbersText;
        OnPropertyChanged(nameof(NumbersText));
        OnPropertyChanged(nameof(MeasurementError));
        if (result.Refusal?.Code == "trial.changes-changed") await Changes.ReloadAsync().ConfigureAwait(true);
        OnPropertyChanged(nameof(CanApply));
        OnPropertyChanged(nameof(ApplyBlockReason));
        ApplyCommand.NotifyCanExecuteChanged();
    }

    private void OnMeasurementProgress(MeasureProgress progress)
    {
        MeasurementProgressText = $"{progress.Completed} of {progress.Total} words checked" +
            (progress.CurrentWord is { Length: > 0 } word ? $" · {word}" : string.Empty);
        OnPropertyChanged(nameof(MeasurementProgressText));
    }

    private async Task ApplyAsync()
    {
        if (!CanApply || Context.ProjectPath is not { } project || Changes.Snapshot.DraftId is not { } draft)
            return;
        IsApplying = true;
        _applyCancellation?.Dispose();
        _applyCancellation = new CancellationTokenSource();
        OnPropertyChanged(nameof(IsApplying));
        OnPropertyChanged(nameof(CanApply));
        OnPropertyChanged(nameof(ApplyBlockReason));
        CommandOutcome<ApplyPendingResult> result;
        try
        {
            result = await Context.Commands.ApplyPendingAsync(new ApplyPendingRequest(
                project, draft, Changes.Snapshot.Revision, Environment.UserName),
                _applyCancellation.Token).ConfigureAwait(true);
        }
        finally
        {
            var cancellation = _applyCancellation;
            _applyCancellation = null;
            cancellation?.Dispose();
            IsApplying = false;
            OnPropertyChanged(nameof(IsApplying));
            OnPropertyChanged(nameof(CanApply));
            OnPropertyChanged(nameof(ApplyBlockReason));
            ApplyCommand.NotifyCanExecuteChanged();
        }
        if (Context.ProjectPath != project) return;
        if (!result.Succeeded)
        {
            ApplyError = UserFacingRefusal.MessageOf(result.Refusal!);
            OnPropertyChanged(nameof(ApplyError));
            await Changes.ReloadAsync().ConfigureAwait(true);
            return;
        }
        Receipt = result.Value!.Receipt;
        ApplyError = null;
        if (result.Value.Applied) Context.AppliedSinceRefresh = true;
        await Changes.ReloadAsync().ConfigureAwait(true);
        OnPropertyChanged(nameof(Receipt));
        OnPropertyChanged(nameof(HasReceipt));
        OnPropertyChanged(nameof(ReceiptText));
        OnPropertyChanged(nameof(ApplyError));
    }

    private async Task RemoveNonFittingAsync()
    {
        foreach (var change in Changes.Items.Where(item => item.IsNoLongerFits).ToArray())
            await Changes.RemoveCommand.ExecuteAsync(change).ConfigureAwait(true);
    }

    private void OnChangesChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ChangesViewModel.Snapshot))
        {
            EvidenceComplete = false;
            NumbersText = "See what applying does to the numbers.";
            OnPropertyChanged(nameof(NumbersText));
        }
        if (e.PropertyName == nameof(ChangesViewModel.Count))
        {
            Badge = Changes.Count > 0 ? Changes.Count.ToString(CultureInfo.CurrentCulture) : string.Empty;
            EvidenceComplete = false;
            OnPropertyChanged(nameof(CanApply));
            OnPropertyChanged(nameof(ApplyBlockReason));
            OnPropertyChanged(nameof(HasNonFittingChanges));
            RemoveNonFittingCommand.NotifyCanExecuteChanged();
            CheckAgainCommand.NotifyCanExecuteChanged();
            MeasureCommand.NotifyCanExecuteChanged();
            ApplyCommand.NotifyCanExecuteChanged();
        }
    }

    private void OnContextPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WorkspaceContext.ProjectName)) OnPropertyChanged(nameof(ProjectName));
        if (e.PropertyName == nameof(WorkspaceContext.ProjectPath))
        {
            _measurementCancellation?.Cancel();
            _applyCancellation?.Cancel();
        }
        if (e.PropertyName is nameof(WorkspaceContext.Baseline) or nameof(WorkspaceContext.CurrentEvidence))
        {
            OnPropertyChanged(nameof(CanApply));
            OnPropertyChanged(nameof(ApplyBlockReason));
            CheckAgainCommand.NotifyCanExecuteChanged();
            ApplyCommand.NotifyCanExecuteChanged();
        }
    }

    protected override void OnProjectCleared()
    {
        _measurementCancellation?.Cancel();
        _applyCancellation?.Cancel();
        Receipt = null;
        ApplyError = null;
        MeasurementError = null;
        NumbersText = "See what applying does to the numbers.";
        EvidenceComplete = false;
        OnPropertyChanged(nameof(Receipt));
        OnPropertyChanged(nameof(HasReceipt));
        OnPropertyChanged(nameof(ReceiptText));
        OnPropertyChanged(nameof(ApplyError));
        OnPropertyChanged(nameof(MeasurementError));
        OnPropertyChanged(nameof(NumbersText));
        OnPropertyChanged(nameof(CanApply));
    }

    protected override async Task OnStopWorkAsync()
    {
        _measurementCancellation?.Cancel();
        _applyCancellation?.Cancel();
        if (MeasureCommand.ExecutionTask is { } running) await running.ConfigureAwait(true);
        if (ApplyCommand.ExecutionTask is { } applying) await applying.ConfigureAwait(true);
    }
}
