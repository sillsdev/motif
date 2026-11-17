using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.Contract.Requests;

namespace SIL.Motif.App.ViewModels;

/// <summary>One analysis a person can keep at the chosen places.</summary>
/// <param name="Id">The stored analysis identity, or a parser reading's stable key.</param>
/// <param name="Label">The reading as shown.</param>
/// <param name="IsGuess">Whether it is a guess nobody has approved.</param>
public sealed record WordReadingOption(string Id, string Label, bool IsGuess);

/// <summary>Pages the places a word occurs, for the panel that offers the keep.</summary>
public interface IWordOccurrenceSource
{
    /// <summary>How many places the word occurs in the Context.</summary>
    int Count { get; }

    /// <summary>The places from <paramref name="offset"/>, at most <paramref name="count"/> of them.</summary>
    Task<IReadOnlyList<OccurrenceChoiceViewModel>> ReadPageAsync(int offset, int count, CancellationToken cancellationToken);
}

/// <summary>The writes a keep, Not a Word or Ask Claude can make; each reports only what was really written.</summary>
public interface IWordOccurrenceActions
{
    /// <summary>
    /// Stages the analysis at each place and returns the anchors whose changes were written, in order. It stops
    /// at the first failure or at cancellation, so the result never includes an unwritten place.
    /// </summary>
    Task<IReadOnlyList<OccurrenceAnchor>> StageKeepAsync(WordReadingOption reading,
        IReadOnlyList<OccurrenceChoiceViewModel> places, CancellationToken cancellationToken);

    /// <summary>Stages Not a Word for the whole wordform; it is not tied to a place.</summary>
    Task<bool> StageNotAWordAsync(string form, CancellationToken cancellationToken);

    /// <summary>Records one Request about the word; it runs no Trial and no Apply.</summary>
    Task<string> AskAsync(string form, CancellationToken cancellationToken);
}

/// <summary>
/// The places one word occurs, which of them are checked, the reading to keep, and the Approve, Ask Claude
/// and Not a Word commands. Checked places survive paging; only written places are marked staged.
/// </summary>
public sealed partial class WordOccurrencesViewModel : ObservableObject
{
    public const int PageSize = 20;

    private readonly IWordOccurrenceSource _source;
    private readonly IWordOccurrenceActions _actions;
    private readonly HashSet<OccurrenceAnchor> _checked = [];
    private readonly HashSet<OccurrenceAnchor> _staged = [];
    private CancellationTokenSource? _running;

    [ObservableProperty]
    private WordReadingOption? _chosenReading;

    [ObservableProperty]
    private string? _statusSentence;

    [ObservableProperty]
    private bool _isBusy;

    public WordOccurrencesViewModel(string form, IReadOnlyList<WordReadingOption> readings,
        IWordOccurrenceSource source, IWordOccurrenceActions actions)
    {
        Form = form;
        Readings = readings;
        _source = source;
        _actions = actions;
        if (readings.Count == 1) _chosenReading = readings[0];
        PreviousPageCommand = new AsyncRelayCommand(() => ShowPageAsync(Math.Max(0, PageOffset - PageSize)), () => HasPreviousPage && !IsBusy);
        NextPageCommand = new AsyncRelayCommand(() => ShowPageAsync(PageOffset + PageSize), () => HasNextPage && !IsBusy);
        CheckGapsCommand = new RelayCommand(CheckShownGaps);
        ApproveCommand = new AsyncRelayCommand(ApproveAsync, () => CanApprove);
        NotAWordCommand = new AsyncRelayCommand(NotAWordAsync, () => !IsBusy);
        AskClaudeCommand = new AsyncRelayCommand(AskAsync, () => !IsBusy);
    }

    public string Form { get; }

    /// <summary>The writing system of the word as shown, so a right-to-left or special-font word reads correctly.</summary>
    public string? FormWritingSystem { get; init; }

    /// <summary>One word naming where this word stands: no analysis yet, only a guess, or a stored analysis.</summary>
    public string StateCaption => !HasReadings ? MotifAppStrings.OccurrencesStateNoAnalysis :
        Readings.All(reading => reading.IsGuess) ? MotifAppStrings.OccurrencesStateGuessed :
        MotifAppStrings.OccurrencesStateStored;

    /// <summary>The count of every place in the Context, as a sentence.</summary>
    public string PlacesText => PluralText.Format(MotifAppStrings.OccurrencesPlacesPattern, TotalCount);

    /// <summary>
    /// The label of the primary button: how many places it will approve, or, while it cannot act, what to do
    /// first. It never reads as a zero count.
    /// </summary>
    public string ApproveLabel => !HasReadings ? MotifAppStrings.OccurrencesApproveNothing :
        ChosenReading is null ? MotifAppStrings.OccurrencesApproveChooseReading :
        SelectedCount == 0 ? MotifAppStrings.OccurrencesApproveCheckPlace :
        PluralText.Format(MotifAppStrings.OccurrencesApprovePattern, SelectedCount);

    /// <summary>The count of places checked, as a sentence.</summary>
    public string SelectedText => PluralText.Format(MotifAppStrings.OccurrencesSelectedPattern, SelectedCount);

    /// <summary>Which places the shown page covers, so a long list can be paged.</summary>
    public string RangeText => TotalCount == 0 ? string.Empty : string.Format(CultureInfo.CurrentCulture,
        MotifAppStrings.OccurrencesRangeFormat, PageOffset + Math.Min(1, Page.Count), PageOffset + Page.Count, TotalCount);

    public bool HasPreviousPage => PageOffset > 0;
    public bool HasNextPage => PageOffset + PageSize < TotalCount;
    public bool HasPages => TotalCount > PageSize;
    /// <summary>
    /// The word's readings in one line: the chooser below is the one place to pick, and it is shown only when
    /// there is more than one to pick from; a single reading is named here and already chosen.
    /// </summary>
    public string ReadingSummary => Readings.Count switch
    {
        0 => string.Empty,
        1 => Readings[0].Label,
        _ => PluralText.Format(MotifAppStrings.OccurrencesReadingCountPattern, Readings.Count),
    };

    public bool HasReadingChoice => Readings.Count > 1;
    public bool HasStatus => !string.IsNullOrEmpty(StatusSentence);
    public bool HasNoReadings => !HasReadings;
    public IReadOnlyList<WordReadingOption> Readings { get; }
    public ObservableCollection<OccurrenceChoiceViewModel> Page { get; } = [];
    public int TotalCount => _source.Count;
    public int PageOffset { get; private set; }

    /// <summary>The places checked, across every page.</summary>
    public int SelectedCount => _checked.Count;

    /// <summary>The anchors of the checked places, on any page.</summary>
    public IReadOnlyCollection<OccurrenceAnchor> CheckedAnchors => _checked.ToArray();

    /// <summary>The places that were really staged by this panel.</summary>
    public IReadOnlyCollection<OccurrenceAnchor> StagedAnchors => _staged;

    /// <summary>Several readings and none chosen: the person must choose before a keep.</summary>
    public bool NeedsReadingChoice => Readings.Count > 1 && ChosenReading is null;

    /// <summary>Whether there is any analysis to keep; a word with none has no enabled keep.</summary>
    public bool HasReadings => Readings.Count > 0;

    public bool CanApprove => !IsBusy && HasReadings && ChosenReading is not null && SelectedCount > 0;

    /// <summary>The scope Not a Word really has: the spelling, project wide, never one place.</summary>
    public string NotAWordScope => string.Format(CultureInfo.CurrentCulture, MotifAppStrings.OccurrencesNotAWordScopeFormat, Form);

    /// <summary>Text slot filled by the Why This Analysis fold; the panel never fills it itself.</summary>
    [ObservableProperty]
    private object? _diagnosticContent;

    public ICommand PreviousPageCommand { get; }
    public ICommand NextPageCommand { get; }
    public ICommand CheckGapsCommand { get; }
    public ICommand ApproveCommand { get; }
    public ICommand NotAWordCommand { get; }
    public ICommand AskClaudeCommand { get; }

    partial void OnChosenReadingChanged(WordReadingOption? value)
    {
        OnPropertyChanged(nameof(ApproveLabel));
        RefreshCommands();
    }
    partial void OnIsBusyChanged(bool value) => RefreshCommands();

    /// <summary>Loads a page for a command; a failure becomes the status sentence.</summary>
    private async Task ShowPageAsync(int offset)
    {
        try { await LoadPageAsync(offset).ConfigureAwait(true); }
        catch (OperationCanceledException) { }
        catch (InvalidOperationException refusal) { StatusSentence = refusal.Message; }
        catch (Exception) { StatusSentence = MotifAppStrings.OccurrencesPageNotRead; }
    }

    public async Task LoadPageAsync(int offset, CancellationToken cancellationToken = default)
    {
        var places = await _source.ReadPageAsync(offset, PageSize, cancellationToken).ConfigureAwait(true);
        PageOffset = offset;
        foreach (var old in Page) old.PropertyChanged -= OnPlaceChanged;
        Page.Clear();
        foreach (var place in places)
        {
            // A place the source already shows staged (from the pending Changes) is kept staged and unchecked.
            if (place.IsStaged) { _staged.Add(place.Anchor); _checked.Remove(place.Anchor); }
            place.IsChecked = _checked.Contains(place.Anchor);
            if (_staged.Contains(place.Anchor)) { place.MarkStaged(); place.IsChecked = false; _checked.Remove(place.Anchor); }
            place.PropertyChanged += OnPlaceChanged;
            Page.Add(place);
        }
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(PlacesText));
        OnPropertyChanged(nameof(RangeText));
        OnPropertyChanged(nameof(HasPreviousPage));
        OnPropertyChanged(nameof(HasNextPage));
        OnPropertyChanged(nameof(HasPages));
        (PreviousPageCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
        (NextPageCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
    }

    /// <summary>Checks or unchecks one place by anchor; unchecked places are never staged.</summary>
    public void SetChecked(OccurrenceAnchor anchor, bool isChecked)
    {
        if (!(isChecked ? _checked.Add(anchor) : _checked.Remove(anchor))) return;
        if (Page.FirstOrDefault(place => place.Anchor == anchor) is { } shown && shown.IsChecked != isChecked)
            shown.IsChecked = isChecked;
        RefreshSelection();
    }

    /// <summary>Checks every unapproved place on the shown page.</summary>
    public void CheckShownGaps()
    {
        foreach (var place in Page.Where(place => place.CanCheck)) SetChecked(place.Anchor, true);
    }

    private void OnPlaceChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(OccurrenceChoiceViewModel.IsChecked) || sender is not OccurrenceChoiceViewModel place) return;
        if (place.IsChecked) _checked.Add(place.Anchor); else _checked.Remove(place.Anchor);
        RefreshSelection();
    }

    private void RefreshSelection()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectedText));
        OnPropertyChanged(nameof(ApproveLabel));
        OnPropertyChanged(nameof(CanApprove));
        RefreshCommands();
    }

    private void RefreshCommands()
    {
        OnPropertyChanged(nameof(CanApprove));
        OnPropertyChanged(nameof(NeedsReadingChoice));
        (ApproveCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
        (NotAWordCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
        (AskClaudeCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
        (PreviousPageCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
        (NextPageCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
    }

    partial void OnStatusSentenceChanged(string? value) => OnPropertyChanged(nameof(HasStatus));

    /// <summary>Cancels a keep in flight; places not yet written stay unmarked.</summary>
    public void Cancel() => _running?.Cancel();

    private async Task ApproveAsync()
    {
        if (!CanApprove || ChosenReading is not { } reading) return;
        var places = _checked.Select(anchor => Page.FirstOrDefault(place => place.Anchor == anchor))
            .Where(place => place is not null).Cast<OccurrenceChoiceViewModel>().ToList();
        var missing = _checked.Where(anchor => Page.All(place => place.Anchor != anchor)).ToList();
        _running = new CancellationTokenSource();
        IsBusy = true;
        try
        {
            if (missing.Count > 0) places.AddRange(await ReadCheckedOffPageAsync(missing, _running.Token).ConfigureAwait(true));
            var written = await _actions.StageKeepAsync(reading, places, _running.Token).ConfigureAwait(true);
            foreach (var anchor in written)
            {
                _staged.Add(anchor);
                _checked.Remove(anchor);
                if (Page.FirstOrDefault(place => place.Anchor == anchor) is { } shown)
                {
                    shown.IsChecked = false;
                    shown.MarkStaged();
                }
            }
            StatusSentence = written.Count == places.Count
                ? PluralText.Format(MotifAppStrings.OccurrencesStagedStatusPattern, written.Count)
                : string.Format(CultureInfo.CurrentCulture, MotifAppStrings.OccurrencesPartlyStagedStatusFormat, written.Count, places.Count);
        }
        catch (Exception)
        {
            // The written list is lost with the exception, so reload from the source, which reads what is staged.
            try { await LoadPageAsync(PageOffset).ConfigureAwait(true); } catch (Exception) { }
            // A refusal or failure is a sentence, never an exception out of the command (it would reach the UI thread).
            StatusSentence = MotifAppStrings.OccurrencesNotStaged;
        }
        finally
        {
            IsBusy = false;
            _running.Dispose();
            _running = null;
            RefreshSelection();
        }
    }

    private async Task<IReadOnlyList<OccurrenceChoiceViewModel>> ReadCheckedOffPageAsync(
        IReadOnlyList<OccurrenceAnchor> anchors, CancellationToken cancellationToken)
    {
        var found = new List<OccurrenceChoiceViewModel>();
        var wanted = anchors.ToHashSet();
        for (var offset = 0; offset < _source.Count && found.Count < wanted.Count; offset += PageSize)
        {
            var places = await _source.ReadPageAsync(offset, PageSize, cancellationToken).ConfigureAwait(true);
            found.AddRange(places.Where(place => wanted.Contains(place.Anchor)));
        }
        return found;
    }

    private async Task NotAWordAsync()
    {
        IsBusy = true;
        try
        {
            StatusSentence = await _actions.StageNotAWordAsync(Form, CancellationToken.None).ConfigureAwait(true)
                ? string.Format(CultureInfo.CurrentCulture, MotifAppStrings.OccurrencesNotAWordStagedFormat, Form)
                : MotifAppStrings.OccurrencesNotStaged;
        }
        // A host failure is a sentence, never an exception out of the command (it would reach the UI thread).
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            StatusSentence = MotifAppStrings.OccurrencesNotStaged;
        }
        finally { IsBusy = false; }
    }

    private async Task AskAsync()
    {
        IsBusy = true;
        try { StatusSentence = await _actions.AskAsync(Form, CancellationToken.None).ConfigureAwait(true); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            StatusSentence = MotifAppStrings.OccurrencesAskNotSent;
        }
        finally { IsBusy = false; }
    }
}
