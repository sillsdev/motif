using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;

namespace SIL.Motif.App.ViewModels;

/// <summary>Owns one Proposal's discussion, bounded evidence reads and Apply confirmation.</summary>
public sealed partial class ProposalDocumentViewModel : ObservableObject
{
    private readonly WorkspaceContext _context;
    private CancellationTokenSource? _cancellation;
    private readonly List<Task> _reads = [];
    private int _generation;
    private bool _stopped;
    private bool _hasStartedRead;
    private DifferenceIdentity? _differenceIdentity;
    private bool _isCheckedForComparison;
    private ProposalReviewResponse? _review;
    private BaselineSemanticIdentity? _baselineIdentity;
    private object? _evidenceSet;

    internal ProposalDocumentViewModel(WorkspaceContext context, string projectPath, string proposalId,
        Func<TimeSpan, CancellationToken, Task>? parsimonyDelay = null)
    {
        _context = context;
        ProjectPath = projectPath;
        _baselineIdentity = context.Baseline?.Token?.SemanticIdentity;
        _evidenceSet = context.Evidence.Set;
        ProposalId = proposalId;
        Difference = new ProposalDifferenceViewModel(context.Commands.ReadProposalDifferenceAsync,
            context.OpenWord, context.Commands.ProduceProposalDifferenceAsync,
            () => [.. context.Selection.AllTexts.Select(text => new DifferenceTextChoice(text.Id, text.Title))]);
        Evidence = new ProposalEvidenceColumnsViewModel(Difference, context.TextStyles);
        Parsimony = new ProposalParsimonyViewModel(context.Commands.ReadProposalParsimonyStatusAsync,
            parsimonyDelay, context.Commands.ReadParsimonyReportAsync, context.Commands.ReadParsimonyViewAsync);
        Pictures = new ProposalPicturesViewModel(context.Commands.ShowProposalPicturesAsync,
            context.Commands.AddProposalPictureAsync, context.Commands.RemoveProposalPictureAsync, context.PictureIntake);
        AddCommentCommand = new RelayCommand(AddComment, () => CanAddComment);
        MeasurePendingCommand = new AsyncRelayCommand(MeasurePendingAsync,
            () => !_stopped && IsPending && !_context.PendingMeasurement.IsMeasuring);
        PrepareApplyCommand = new AsyncRelayCommand(PrepareApplyAsync, () => CanPrepareApply);
        ConfirmApplyCommand = new AsyncRelayCommand(ConfirmApplyAsync);
        CancelApplyCommand = new RelayCommand(() => IsConfirmingApply = false);
        RefreshCommand = new AsyncRelayCommand(() => RefreshAsync());
        TryCommand = new AsyncRelayCommand(TryAsync, () => !_stopped);
        Difference.PropertyChanged += OnDifferenceStatusChanged;
        _context.Evidence.PropertyChanged += OnEvidenceChanged;
        _context.Selection.PropertyChanged += OnSelectionChanged;
        _context.ProposalApply.PropertyChanged += OnApplyChanged;
        _context.Changes.PropertyChanged += OnApplyChanged;
        _context.PendingMeasurement.PropertyChanged += OnApplyChanged;
        _context.PropertyChanged += OnApplyChanged;
    }

    public string ProjectPath { get; }
    public string ProposalId { get; }
    public bool IsPending => Card?.Card.IsPendingDraft == true;
    public IAsyncRelayCommand MeasurePendingCommand { get; }
    public PendingChangesMeasurement PendingMeasurement => _context.PendingMeasurement;
    public bool DiagnosticsVisible => _context.Diagnostics;
    public string Label => ProposalTitle.For(Detail?.Label is { } named && !ProposalTitle.IsUnnamed(named, ProposalId)
        ? named : Card?.Card.Label, ProposalId, IsPending || Detail?.DraftName is not null || Card?.Card.IsDraft == true,
        OperationDescriptions.FirstOrDefault());
    public string StatusText => Card is { } card && IsDecided(card.Card.Status) ? card.StatusWord :
        Detail is not { } detail ? Card?.StatusWord ?? string.Empty :
        detail.DraftName is not null && !IsDecided(detail.Status) ? MotifAppStrings.ProposalDraft : detail.Status switch
        {
            "proposed" => MotifAppStrings.ProposalReady,
            "deferred" => MotifAppStrings.ProposalDeferred,
            "applied" => MotifAppStrings.ProposalApplied,
            "rejected" => MotifAppStrings.ProposalDeclined,
            "superseded" => MotifAppStrings.ProposalSuperseded,
            "withdrawn" => MotifAppStrings.ProposalWithdrawn,
            _ => string.Empty,
        };
    public ProposalDifferenceViewModel Difference { get; }
    public ProposalEvidenceColumnsViewModel Evidence { get; }
    public ProposalParsimonyViewModel Parsimony { get; }
    public ProposalPicturesViewModel Pictures { get; }
    public Task Reading { get; private set; } = Task.CompletedTask;
    public Task DifferenceReading { get; private set; } = Task.CompletedTask;
    public Task PicturesLoading { get; private set; } = Task.CompletedTask;
    public IRelayCommand AddCommentCommand { get; }
    public IAsyncRelayCommand PrepareApplyCommand { get; }
    public IAsyncRelayCommand ConfirmApplyCommand { get; }
    public IRelayCommand CancelApplyCommand { get; }
    public IAsyncRelayCommand RefreshCommand { get; }
    /// <summary>Tries this Proposal; the one Try It or Try Again button on the page.</summary>
    public IAsyncRelayCommand TryCommand { get; }

    [ObservableProperty] private ProposalCardViewModel? _card;
    [ObservableProperty] private ProposalDetailProjection? _detail;
    [ObservableProperty] private RationaleEditorModel? _rationaleEditor;
    [ObservableProperty] private ObservableCollection<RationaleEditorModel> _comments = [];
    [ObservableProperty] private IReadOnlyList<string> _operationDescriptions = [];
    [ObservableProperty] private WindowRefusal? _detailRefusal;
    [ObservableProperty] private string _reviewProgress = string.Empty;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isOutdated;
    [ObservableProperty] private bool _isConfirmingApply;
    private object? _diagnosticsContent;
    public object? DiagnosticsContent
    {
        get => _diagnosticsContent;
        set => SetProperty(ref _diagnosticsContent, _context.Diagnostics ? value : null);
    }
    [ObservableProperty] private ApplyProjection? _receipt;
    [ObservableProperty] private WindowRefusal? _applyRefusal;

    public bool IsCheckedForComparison => _isCheckedForComparison;
    internal void SetChecked(bool value) => SetProperty(ref _isCheckedForComparison, value, nameof(IsCheckedForComparison));
    public bool CanAddComment => !_stopped && Detail is { } detail &&
        !IsDecided(detail.Status) && (Card?.Card is not { } card || !IsDecided(card.Status));
    public bool HasReceipt => Receipt is not null;
    public string ReceiptText => Receipt is { } receipt
        ? string.Format(CultureInfo.CurrentCulture, MotifAppStrings.ProposalReceiptFormat,
            receipt.ProposalId, receipt.AppliedLogEntry.TimestampUtc) : string.Empty;
    public RationaleEditorModel? UnsavedEditor =>
        RationaleEditor is { HasUnsavedChanges: true } rationale ? rationale :
        Comments.FirstOrDefault(editor => editor.HasUnsavedChanges);
    public bool CanPrepareApply => !_stopped && CanAddComment && !IsLoading && !_context.ProposalApply.IsApplying &&
        PrepareBlockReason is null;
    /// <summary>
    /// Why Apply cannot even be started for this Proposal, or null when it can: the window's Selection has no current
    /// Trial or no ready evidence for it. A pending Draft is measured by Check These Changes instead.
    /// </summary>
    public string? PrepareBlockReason
    {
        get
        {
            if (_stopped || IsLoading || !CanAddComment) return null;
            if (IsPending)
            {
                var pending = _context.ProposalApply.Blockers(_review, null, _context.RefreshProjectCommand, null);
                return pending.Count == 0 ? null : string.Join(" ", pending.Select(blocker => blocker.Sentence));
            }
            if (IsOutdated) return MotifAppStrings.ProposalDocumentOutdated;
            var standing = Card?.Standing ?? (_review?.Trial is { Freshness: TrialFreshness.Current }
                ? TrialStanding.Current : TrialStanding.NotTried);
            if (standing.Kind != TrialStandingKind.Current)
            {
                var words = TrialStandingWords.For(standing);
                return string.Format(CultureInfo.CurrentCulture, MotifAppStrings.ProposalApplyNeedsTrialFormat,
                    words.Action);
            }
            if (!Difference.IsShown)
                return Difference.ShowsMakeControls ? MotifAppStrings.ProposalApplyNeedsDifference
                    : MotifAppStrings.ProposalApplyDifferenceUnavailable;
            if (_review?.Readiness is not { } readiness) return MotifAppStrings.ApplyReadinessUnavailable;
            return readiness.IsReady ? null : string.Join(" ", readiness.Reasons.Select(reason =>
                readiness.DroppedGrammar is { } dropped && reason == readiness.DroppedGrammarReason
                    ? DroppedGrammarWords.Blocker(dropped) : reason));
        }
    }

    /// <summary>What Applying would lose of the approved analyses, as far as the window can say, or empty.</summary>
    public string ApplyLossNotice => IsPending || Card?.Card.Trial is not { LostApprovedKnown: true } trial ||
        trial.Freshness != TrialFreshness.Current ? string.Empty
        : trial.LostApproved == 0 ? MotifAppStrings.ProposalApplyLosesNone
        : string.Format(CultureInfo.CurrentCulture, MotifAppStrings.ProposalApplyLosesFormat,
            PluralText.Format(MotifAppStrings.ApplyLostCountPattern, trial.LostApproved));

    /// <summary>Opens FieldWorks' own help on backing up a project, shown beside the Apply confirmation.</summary>
    public IAsyncRelayCommand OpenBackupHelpCommand => _openBackupHelp ??= new AsyncRelayCommand(async () =>
    {
        if (_context.UriLauncher is { } launcher)
            await launcher.LaunchAsync(new Uri(Services.AppLinks.FieldWorksBackupHelp)).ConfigureAwait(true);
    });
    private IAsyncRelayCommand? _openBackupHelp;
    public bool CanApply => !_stopped && !IsLoading && !IsOutdated && CanAddComment &&
        _context.ProjectPath == ProjectPath && (IsPending || Difference.IsShown) &&
        (IsPending ? _context.Changes.TargetProposalId is null : _context.Changes.TargetProposalId == ProposalId &&
            _context.Lens?.ProposalId == ProposalId) && _context.ProposalApply.CanApply(_review, null,
            _context.RefreshProjectCommand, null);
    public string ApplyBlockReason => PrepareBlockReason is { } cannotStart ? cannotStart :
        !IsPending && _context.Changes.TargetProposalId != ProposalId
        ? MotifAppStrings.ProposalActivateForApply : IsOutdated ? MotifAppStrings.ProposalDocumentOutdated :
        string.Join(" ", _context.ProposalApply.Blockers(_review, null, _context.RefreshProjectCommand, null)
            .Select(blocker => blocker.Sentence));

    partial void OnCardChanged(ProposalCardViewModel? value)
    {
        OnPropertyChanged(nameof(Label));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(IsPending));
        OnPropertyChanged(nameof(CanAddComment));
        AddCommentCommand.NotifyCanExecuteChanged();
        FreezeEditorsIfClosed();
        RaiseApply();
    }
    partial void OnDetailChanged(ProposalDetailProjection? value)
    {
        OnPropertyChanged(nameof(Label));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(CanAddComment));
        AddCommentCommand.NotifyCanExecuteChanged();
        RaiseApply();
    }
    partial void OnOperationDescriptionsChanged(IReadOnlyList<string> value) => OnPropertyChanged(nameof(Label));
    partial void OnIsLoadingChanged(bool value) => RaiseApply();
    partial void OnIsOutdatedChanged(bool value) => RaiseApply();
    partial void OnReceiptChanged(ApplyProjection? value)
    {
        OnPropertyChanged(nameof(HasReceipt));
        OnPropertyChanged(nameof(ReceiptText));
    }

    internal Task EnsureLoadedAsync() => _hasStartedRead ? Reading : RefreshAsync();

    private sealed record DifferenceIdentity(string? IntentDigest, string? TrialJobId,
        BaselineSemanticIdentity? Baseline, DifferenceOffer Offer);

    public Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (_stopped || _context.ProjectPath != ProjectPath) return Task.CompletedTask;
        _hasStartedRead = true;
        var generation = ++_generation;
        _cancellation?.Cancel();
        var cancellation = _cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        IsLoading = true;
        IsConfirmingApply = false;
        DetailRefusal = null;
        _review = null;
        Evidence.SetCurrent(false);
        Reading = ReadAsync(generation, cancellation);
        _reads.RemoveAll(task => task.IsCompleted);
        _reads.Add(Reading);
        return Reading;
    }

    // A refused read leaves no evidence to stand on, so the old Difference goes unless a make is running.
    private void ForgetDifference()
    {
        if (Difference.Maker.IsActive) return;
        _differenceIdentity = null;
        Difference.Clear();
    }

    private bool Current(int generation) => !_stopped && generation == _generation && _context.ProjectPath == ProjectPath;

    private async Task ReadAsync(int generation, CancellationTokenSource cancellation)
    {
        try
        {
            var shown = await _context.Commands.ShowProposalAsync(new ShowProposalRequest(ProjectPath,
                MotifProductVersion.CurrentText, ProposalId), cancellation.Token).ConfigureAwait(true);
            if (!Current(generation) || cancellation.IsCancellationRequested) return;
            if (!shown.Succeeded)
            {
                Detail = null;
                DetailRefusal = WindowRefusal.From(shown.Refusal!);
                ForgetDifference();
                return;
            }
            if (shown.Value!.ProposalId != ProposalId)
            {
                Detail = null;
                DetailRefusal = WindowRefusal.Plain(MotifAppStrings.ProposalDocumentUnreadable);
                ForgetDifference();
                return;
            }
            var detail = Detail = shown.Value!;
            if (RationaleEditor is { IsEditing: true } editing)
                editing.ReconcileRationale(detail);
            else RationaleEditor = new RationaleEditorModel(_context, detail);
            ReconcileComments(detail);
            FreezeEditorsIfClosed();
            var review = await _context.Commands.ReadProposalReviewAsync(new ProposalReviewRequest(ProjectPath,
                ProposalId), cancellation.Token).ConfigureAwait(true);
            if (!Current(generation) || cancellation.IsCancellationRequested) return;
            if (!review.Succeeded) DetailRefusal = WindowRefusal.From(review.Refusal!);
            _review = review.Succeeded && review.Value!.ProposalId == ProposalId ? review.Value : null;
            var words = _review?.Entries.OfType<WordChangeEntry>().ToArray() ?? [];
            OperationDescriptions = detail.Operations.Select(operation => ProposalOperationSentence.For(operation,
                words.Where(word => word.OperationIds.Contains(operation.OperationId))
                    .Select(word => word.Change.WordformId).Distinct(StringComparer.Ordinal).Count())).ToArray();
            ReviewProgress = _review is null ? string.Empty : string.Format(CultureInfo.CurrentCulture,
                MotifAppStrings.ProposalReviewProgressFormat, _review.LookedAtCount, _review.Entries.Count);
            IsOutdated = _context.Evidence.IsStale || Card?.Standing.Kind == TrialStandingKind.NeedsAnotherTry;
            var offer = Offer();
            var identity = new DifferenceIdentity(detail.CurrentIntentDigest,
                _review?.Trial?.JobId ?? Card?.Card.Trial?.JobId, _context.Baseline?.Token?.SemanticIdentity, offer);
            var previous = _differenceIdentity;
            var reload = Difference.State is ProposalDifferenceState.None or ProposalDifferenceState.Refused ||
                identity != previous;
            // A make in progress survives a refresh unless the Trial or intent it was made for changed.
            var keptForMake = reload && Difference.Maker.IsActive && previous is not null &&
                previous.IntentDigest == identity.IntentDigest && previous.TrialJobId == identity.TrialJobId;
            if (keptForMake) reload = false;
            // The skipped reload leaves the identity behind, so the refresh after the make finishes still sees the change.
            if (!keptForMake) _differenceIdentity = identity;
            DifferenceReading = reload
                ? Difference.LoadAsync(ProjectPath, ProposalId, cancellation.Token, offer)
                : Difference.RefreshSavedAsync(offer, cancellation.Token);
            PicturesLoading = Pictures.LoadAsync(ProjectPath, ProposalId,
                detail.DraftName is null && !IsDecided(detail.Status) &&
                (Card?.Card is not { } card || !IsDecided(card.Status)), cancellation.Token);
            await Task.WhenAll(DifferenceReading, PicturesLoading).ConfigureAwait(true);
            if (Current(generation) && !cancellation.IsCancellationRequested) Evidence.SetCurrent(!IsOutdated, detail, _review?.Trial?.JobId ?? Card?.Card.Trial?.JobId,
                _context.Baseline?.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (Current(generation)) DetailRefusal = WindowRefusal.Failure(WindowRefusal.ProposalDocumentReadCode,
                MotifAppStrings.ProposalDocumentUnreadable, exception);
        }
        finally
        {
            if (Current(generation)) IsLoading = false;
            if (ReferenceEquals(_cancellation, cancellation)) _cancellation = null;
            cancellation.Dispose();
        }
    }

    private DifferenceOffer Offer() => (Detail is { } detail && IsDecided(detail.Status)) ||
        (Card?.Card is { } card && IsDecided(card.Status))
        ? DifferenceOffer.Decided : Detail?.Status == "deferred" || Card?.Card.Status == "deferred" ? DifferenceOffer.Deferred :
        _review?.Trial is null && (Card is null || Card.Standing.Kind == TrialStandingKind.NotTried)
            ? DifferenceOffer.NotTried : DifferenceOffer.Available;

    private void ReconcileComments(ProposalDetailProjection detail)
    {
        var edits = Comments.Where(editor => editor.IsEditing).ToArray();
        var fresh = detail.Comments ?? [];
        var reconciled = new List<RationaleEditorModel>();
        foreach (var comment in fresh)
        {
            var editing = edits.FirstOrDefault(editor => editor.CommentId == comment.CommentId);
            if (editing is null) reconciled.Add(new RationaleEditorModel(_context, detail, comment, RemoveComment));
            else
            {
                editing.ReconcileComment(comment, detail, ReplaceAfterCancel);
                reconciled.Add(editing);
            }
        }
        foreach (var editing in edits.Where(editor => !editor.CommentId.HasValue ||
                     fresh.All(comment => comment.CommentId != editor.CommentId)))
        {
            editing.ReconcileComment(null, detail, ReplaceAfterCancel);
            reconciled.Add(editing);
        }
        Comments = new ObservableCollection<RationaleEditorModel>(reconciled);
        void ReplaceAfterCancel(RationaleEditorModel editor)
        {
            var index = Comments.IndexOf(editor);
            if (index < 0) return;
            var current = fresh.FirstOrDefault(comment => comment.CommentId == editor.CommentId);
            if (current is null) Comments.RemoveAt(index);
            else Comments[index] = new RationaleEditorModel(_context, detail, current, RemoveComment);
        }
    }

    private void FreezeEditorsIfClosed()
    {
        if (Detail is null || CanAddComment) return;
        Pictures.CanEdit = false;
        RationaleEditor?.Freeze();
        foreach (var editor in Comments) editor.Freeze();
    }

    private void AddComment()
    {
        if (CanAddComment) Comments.Add(new RationaleEditorModel(_context, Detail!, null, RemoveComment));
    }
    private void RemoveComment(RationaleEditorModel editor) => Comments.Remove(editor);

    public void InvalidateEvidence()
    {
        ++_generation;
        _cancellation?.Cancel();
        IsLoading = false;
        IsOutdated = true;
        _review = null;
        IsConfirmingApply = false;
        Evidence.SetCurrent(false);
        Difference.Clear();
        RaiseApply();
    }

    private void OnEvidenceChanged(object? sender, PropertyChangedEventArgs e)
    {
        var identity = _context.Baseline?.Token?.SemanticIdentity;
        var changed = identity != _baselineIdentity || !Equals(_evidenceSet, _context.Evidence.Set);
        _baselineIdentity = identity;
        _evidenceSet = _context.Evidence.Set;
        if (_context.Evidence.IsStale || changed) InvalidateEvidence();
        else RaiseApply();
    }
    private void OnSelectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SelectionViewModel.ChosenTextIds) or nameof(SelectionViewModel.PastedWords) or
            nameof(SelectionViewModel.AllWordforms) or nameof(SelectionViewModel.RetryFailed))
            InvalidateEvidence();
        if (e.PropertyName == nameof(SelectionViewModel.TextCount)) Difference.RefreshTexts();
    }

    private void OnApplyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(DiagnosticsVisible));
        if (!_context.Diagnostics)
        {
            DiagnosticsContent = null;
            Parsimony.Stop();
        }
        RaiseApply();
    }
    private void RaiseApply()
    {
        OnPropertyChanged(nameof(CanPrepareApply));
        OnPropertyChanged(nameof(PrepareBlockReason));
        OnPropertyChanged(nameof(ApplyLossNotice));
        PrepareApplyCommand.NotifyCanExecuteChanged();
        MeasurePendingCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanApply));
        OnPropertyChanged(nameof(ApplyBlockReason));
        RaiseStatus();
    }

    private async Task MeasurePendingAsync()
    {
        if (_stopped || !IsPending || _context.ProjectPath != ProjectPath) return;
        await _context.LeaveProposalAsync().ConfigureAwait(true);
        if (_stopped || _context.ProjectPath != ProjectPath || _context.Changes.TargetProposalId is not null) return;
        await _context.PendingMeasurement.MeasureCommand.ExecuteAsync(null).ConfigureAwait(true);
        await RefreshAsync().ConfigureAwait(true);
    }

    private async Task PrepareApplyAsync()
    {
        if (!CanPrepareApply || _context.ProjectPath != ProjectPath) return;
        if (IsPending) await _context.LeaveProposalAsync().ConfigureAwait(true);
        else await _context.OpenProposalAsync(ProposalId, CancellationToken.None, Label).ConfigureAwait(true);
        if (_stopped || _context.ProjectPath != ProjectPath ||
            (!IsPending && _context.Lens?.ProposalId != ProposalId)) return;
        await RefreshAsync().ConfigureAwait(true);
        IsConfirmingApply = CanApply;
    }

    private async Task ConfirmApplyAsync()
    {
        if (!IsConfirmingApply || !CanApply) return;
        IsConfirmingApply = false;
        var pending = IsPending;
        var result = _context.ProposalApply;
        await result.ApplyCurrentAsync(_review).ConfigureAwait(true);
        if (_stopped || _context.ProjectPath != ProjectPath) return;
        Receipt = pending || result.Receipt?.ProposalId == ProposalId ? result.Receipt : null;
        ApplyRefusal = result.ApplyRefusal;
        await RefreshAsync().ConfigureAwait(true);
    }

    internal async Task StopAsync()
    {
        if (_stopped) return;
        _stopped = true;
        ++_generation;
        _cancellation?.Cancel();
        Difference.PropertyChanged -= OnDifferenceStatusChanged;
        _context.Evidence.PropertyChanged -= OnEvidenceChanged;
        _context.Selection.PropertyChanged -= OnSelectionChanged;
        _context.ProposalApply.PropertyChanged -= OnApplyChanged;
        _context.Changes.PropertyChanged -= OnApplyChanged;
        _context.PendingMeasurement.PropertyChanged -= OnApplyChanged;
        _context.PropertyChanged -= OnApplyChanged;
        RationaleEditor?.Freeze();
        foreach (var editor in Comments) editor.Freeze();
        DiagnosticsContent = null;
        Parsimony.Stop();
        Pictures.Clear();
        await Task.WhenAll(_reads.Append(Difference.StopAsync())).ConfigureAwait(true);
        Difference.Clear();
        IsLoading = false;
        Detail = null;
    }

    private static bool IsDecided(string status) => status is "applied" or "rejected" or "superseded" or "withdrawn";
}
