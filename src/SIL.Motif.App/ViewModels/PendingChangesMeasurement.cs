using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Projection.Usage;

namespace SIL.Motif.App.ViewModels;

/// <summary>Measures the current pending Draft on request and retains only evidence for its current revision.</summary>
public sealed partial class PendingChangesMeasurement : ObservableObject
{
    private readonly WorkspaceContext _context;
    private CancellationTokenSource? _cancellation;
    private int _generation;

    public PendingChangesMeasurement(WorkspaceContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
        ParseProgress = new ParseProgressViewModel(context.Clock);
        MeasureCommand = new AsyncRelayCommand(MeasureAsync, () => context.Changes.HasItems && context.HasProject && !IsMeasuring);
        CancelCommand = new RelayCommand(() => _cancellation?.Cancel(), () => IsMeasuring);
        context.Changes.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ChangesViewModel.Snapshot) or nameof(ChangesViewModel.Count))
            {
                ClearEvidence();
                MeasureCommand.NotifyCanExecuteChanged();
            }
        };
    }

    public ParseProgressViewModel ParseProgress { get; }
    public IAsyncRelayCommand MeasureCommand { get; }
    public IRelayCommand CancelCommand { get; }

    [ObservableProperty]
    private bool _isMeasuring;
    [ObservableProperty]
    private bool _evidenceComplete;
    [ObservableProperty]
    private IReadOnlyList<string> _wordsLosingApprovedAnalysis = [];
    [ObservableProperty]
    private WindowRefusal? _refusal;
    [ObservableProperty]
    private ReviewNumbersResponse? _numbers;
    [ObservableProperty]
    private string _progressText = string.Empty;

    private async Task MeasureAsync()
    {
        var changes = _context.Changes;
        if (_context.ProjectPath is not { } project || changes.Snapshot.DraftId is not { } draft) return;
        var generation = ++_generation;
        using var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        using var usageAction = _context.Commands.BeginUsageAction("trial --pending",
            UsageArgumentShape.Text("fwDataPath"), UsageArgumentShape.List("words",
                changes.Snapshot.Changes.Select(change => change.Word).Distinct(StringComparer.Ordinal).Count()));
        IsMeasuring = true;
        ClearEvidence();
        Refusal = null;
        MeasureCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        var revision = changes.Snapshot.Revision;
        CommandOutcome<MeasurePendingResult> result;
        try
        {
            var refreshStopped = await _context.RefreshBeforeCheckAsync(cancellation.Token).ConfigureAwait(true);
            if (generation != _generation || cancellation.IsCancellationRequested || _context.ProjectPath != project ||
                changes.Snapshot.DraftId != draft) return;
            revision = changes.Snapshot.Revision;
            var words = changes.Snapshot.Changes.Select(change => change.Word).Distinct(StringComparer.Ordinal).ToArray();
            result = refreshStopped is not null
                ? CommandOutcome<MeasurePendingResult>.Refused(refreshStopped)
                : await ParseProgress.TrackAsync(() => _context.Commands.MeasurePendingAsync(
                    new MeasurePendingRequest(project, draft, revision, words, _context.Evidence.CorrectnessAssessmentId),
                    new Progress<MeasureProgress>(progress =>
                    {
                        if (generation == _generation && !cancellation.IsCancellationRequested) OnProgress(progress);
                    }), cancellation.Token)).ConfigureAwait(true);
        }
        finally
        {
            if (ReferenceEquals(_cancellation, cancellation)) _cancellation = null;
            IsMeasuring = false;
            MeasureCommand.NotifyCanExecuteChanged();
            CancelCommand.NotifyCanExecuteChanged();
        }
        if (generation != _generation || cancellation.IsCancellationRequested || _context.ProjectPath != project ||
            changes.Snapshot.DraftId != draft || revision != changes.Snapshot.Revision) return;
        EvidenceComplete = result.Succeeded && result.Value is { EvidenceComplete: true } measured && measured.Revision == revision;
        WordsLosingApprovedAnalysis = result.Value is { } checkedChanges && checkedChanges.Revision == revision
            ? checkedChanges.Numbers.WordsLosingApprovedAnalysis : [];
        Refusal = result.Refusal is { } refusal ? WindowRefusal.From(refusal)
            : !EvidenceComplete ? WindowRefusal.Plain(MotifAppStrings.ApplyIncompleteMeasurement) : null;
        Numbers = result.Value is { } evidence && evidence.Revision == revision ? evidence.Numbers : null;
        if (result.Refusal?.Code == RefusalCodes.TrialChangesChanged) await changes.ReloadAsync().ConfigureAwait(true);
    }

    private void OnProgress(MeasureProgress progress)
    {
        if (progress.WordProgress is { } word)
            ParseProgress.Report(new AssessmentProgress(AssessmentStage.Parsing, word.Completed, word.Total,
                MotifAppStrings.MeasureCheckingWords)
            {
                CurrentWord = word.CurrentWord, PerWordLimitMs = word.PerWordLimitMs,
                StoppedWords = word.StoppedWords, SlowestWord = word.SlowestWord,
            });
        else
            ParseProgress.Report(new AssessmentProgress(AssessmentStage.SelectingWords, progress.Completed,
                progress.Total, MotifAppStrings.MeasurePreparingWords));
        ProgressText = string.Format(CultureInfo.CurrentCulture, MotifAppStrings.MeasureWordsCheckedFormat,
            progress.Completed, progress.Total);
    }

    private void ClearEvidence()
    {
        EvidenceComplete = false;
        WordsLosingApprovedAnalysis = [];
        Numbers = null;
    }

    internal void Reset()
    {
        _generation++;
        _cancellation?.Cancel();
        ClearEvidence();
        Refusal = null;
        ProgressText = string.Empty;
    }

    internal async Task StopAsync()
    {
        Reset();
        try
        {
            if (MeasureCommand.ExecutionTask is { } running) await running.ConfigureAwait(true);
        }
        catch (OperationCanceledException) { }
    }
}
