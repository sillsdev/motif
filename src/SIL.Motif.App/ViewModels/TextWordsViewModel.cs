using System.ComponentModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.App.Controls.WordPresentation;
using SIL.Motif.App.Services;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Commands.SelectionReading;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Texts;
using SIL.Motif.Host.PanGloss;

namespace SIL.Motif.App.ViewModels;

/// <summary>
/// What the project holds for a word form, as one of the five rows Motif compares an Assessment against. A form
/// with analyses of more than one standing takes the best one, in FieldWorks' own guessing order: approved, then
/// candidates, then rejected ones. A spelling FieldWorks marks incorrect overrides them all.
/// </summary>
public enum WordProjectStatus
{
    /// <summary>The project holds no analysis of this form.</summary>
    NotPresent,

    /// <summary>The project approves at least one analysis of this form.</summary>
    Approved,

    /// <summary>
    /// The project holds analyses of this form, but a person has neither approved nor rejected any of them: they may
    /// come from FieldWorks' parser, its guesser, or be left over, and the workflow for them is the same. FieldWorks
    /// lists these as "Analysis Candidates".
    /// </summary>
    Candidate,

    /// <summary>Every analysis the project holds for this form has been rejected.</summary>
    Rejected,

    /// <summary>FieldWorks marks this spelling as incorrect, so it is not a word to analyse.</summary>
    IncorrectSpelling,
}

/// <summary>Maps a Texts status to its opinion mark and its label.</summary>
public static class WordProjectStatuses
{
    /// <summary>
    /// The opinion mark for <paramref name="status"/>: A, U or D in its box, or the dashed box for a form with nothing
    /// stored. An incorrect spelling is not an opinion, so it has no mark.
    /// </summary>
    public static Mark? MarkOf(WordProjectStatus status) =>
        status == WordProjectStatus.IncorrectSpelling ? null : Mark.Of(WindowWords.OpinionOf(status));

    /// <summary>Which row <paramref name="word"/> belongs to, ranked by <see cref="ProjectStandings.Of"/>.</summary>
    public static WordProjectStatus Of(TextWord word) => FromStanding(StandingOf(word));

    /// <summary>What the project holds for <paramref name="word"/>, as a <see cref="ProjectStanding"/> value.</summary>
    public static string StandingOf(TextWord word)
    {
        ArgumentNullException.ThrowIfNull(word);
        var candidates = word.Occurrences.Any(occurrence => occurrence.Status == InterlinearAnalysisStatus.Unapproved)
            ? Math.Max(word.CandidateCount, 1) : word.CandidateCount;
        return ProjectStandings.Of(word.Approved.Count, candidates, word.Disapproved.Count, word.IncorrectSpelling);
    }

    /// <summary>Reads a <see cref="ProjectStanding"/> wire value; anything unknown is treated as nothing stored.</summary>
    public static WordProjectStatus FromStanding(string? standing) => standing switch
    {
        ProjectStanding.Approved => WordProjectStatus.Approved,
        ProjectStanding.Candidate => WordProjectStatus.Candidate,
        ProjectStanding.Rejected => WordProjectStatus.Rejected,
        ProjectStanding.IncorrectSpelling => WordProjectStatus.IncorrectSpelling,
        _ => WordProjectStatus.NotPresent,
    };

    /// <summary>The short label a Texts filter shows for <paramref name="status"/>.</summary>
    public static string LabelOf(WordProjectStatus status, int approvedCount = 1) => status switch
    {
        WordProjectStatus.Approved when approvedCount > 1 => $"{WindowWords.LabelOf(status)}, {approvedCount} analyses",
        WordProjectStatus.NotPresent => ReadingGradeLabels.NotPresent,
        _ => WindowWords.LabelOf(status),
    };
}

/// <summary>
/// Displays the owned Selection's compact word summary, projecting only bounded rows and their requested detail.
/// The workspace replaces the reader when the chosen Texts or evidence change; pages release their models first.
/// </summary>
public sealed partial class TextWordsViewModel : ObservableObject
{
    private readonly SelectionViewModel _selection;
    private readonly WorkspaceSelection _selectionReads;
    private IReadOnlyList<TextWordRowViewModel> _projectRows = [];
    private string? _projectPath;
    private bool _acceptLoads = true;
    private event Action<WeakReference<object>>? RowCreatedForDiagnostics;

    public TextWordsViewModel(ICommandClient commandClient, SelectionViewModel selection,
        WorkspaceSelection selectionReads)
    {
        ArgumentNullException.ThrowIfNull(commandClient);
        ArgumentNullException.ThrowIfNull(selection);
        _selection = selection;
        _selectionReads = selectionReads ?? throw new ArgumentNullException(nameof(selectionReads));
        _selectionReads.PropertyChanged += OnReaderPropertyChanged;
        _selectionReads.Replacing += ReleaseSummaryRows;
        _selection.PropertyChanged += OnSelectionPropertyChanged;
        HandOffCheckedWordsCommand = new RelayCommand(HandOffCheckedWords, CanHandOffCheckedWords);
        OpenWordCommand = new RelayCommand<string>(word =>
        {
            if (!string.IsNullOrWhiteSpace(word)) OpenWord?.Invoke(word);
        });
        SetStatusFilterCommand = new RelayCommand<WordProjectStatus?>(status =>
        {
            SeveralOnly = false;
            StatusFilter = status;
        });
        ShowSeveralCommand = new RelayCommand(() =>
        {
            StatusFilter = null;
            SeveralOnly = true;
        });
        OnReaderPropertyChanged(_selectionReads, new PropertyChangedEventArgs(nameof(WorkspaceSelection.Summary)));
        IsLoading = _selectionReads.IsLoading;
        Refusal = _selectionReads.Refusal;
    }

    public IReadOnlyList<TextWordRowViewModel> Rows { get; private set; } = [];

    public IReadOnlyList<object> DisplayRows { get; private set; } = [];

    public IReadOnlyList<TextWordRowViewModel> ProjectWords => _projectRows;

    internal int MaterializedRowCount => _summaryRows.Count;

    internal IDisposable ObserveRowCreation(Action<WeakReference<object>> observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        RowCreatedForDiagnostics += observer;
        return new RowCreationObservation(this, observer);
    }

    internal int? OccurrenceCountOf(string form) =>
        _selectionReads.Summary is { } summary && summary.OccurrencesByForm.TryGetValue(Normalize(form), out var count)
            ? count : null;

    internal Guid? WordformIdOf(string form)
    {
        var normalized = Normalize(form);
        var ids = (_selectionReads.Summary?.Words ?? []).Where(word => word.Key.Form == normalized)
            .SelectMany(word => word.Actions.CandidateWordformIds).Distinct().Take(2).ToArray();
        return ids.Length == 1 ? ids[0] : null;
    }

    private sealed class RowCreationObservation(TextWordsViewModel owner, Action<WeakReference<object>> observer) : IDisposable
    {
        private TextWordsViewModel? _owner = owner;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _owner, null) is { } current)
                current.RowCreatedForDiagnostics -= observer;
        }
    }

    /// <summary>Whether the current Baseline contains any Texts to open from a word row.</summary>
    public bool HasAvailableTexts => _selection.HasTexts;

    /// <summary>Opens a word's detail in the Analyze text reader.</summary>
    public IRelayCommand<string> OpenWordCommand { get; }

    public IRelayCommand HandOffCheckedWordsCommand { get; }

    private Action<IReadOnlyList<string>>? _handOff;

    /// <summary>The workspace action that opens AI Handoff on these words.</summary>
    public Action<IReadOnlyList<string>>? HandOff
    {
        get => _handOff;
        set
        {
            if (_handOff == value) return;
            _handOff = value;
            OnPropertyChanged();
            HandOffCheckedWordsCommand.NotifyCanExecuteChanged();
        }
    }

    public int CheckedWordCount => _checkedKeys.Count;

    public string HandOffCheckedWordsLabel => CheckedWordCount switch
    {
        0 => WindowCopy.AiHandoff,
        1 => WindowCopy.AiHandoffForThisWord,
        var count => WindowCopy.AiHandoffForWordCount(count),
    };

    public string HandOffCheckedWordsHelpText => CheckedWordCount == 0 ? WindowCopy.TickWordsFirst : string.Empty;

    /// <summary>The navigation action used when someone opens a word from the list.</summary>
    public Action<string>? OpenWord { get; set; }

    /// <summary>Where a word row's next steps lead for a word the latest parse did not reach.</summary>
    public WordRowRoutes? WordRowRoutes { get; set; }

    /// <summary>Chooses one of the Words table's status filter chips, or <see langword="null"/> for All.</summary>
    public IRelayCommand<WordProjectStatus?> SetStatusFilterCommand { get; }

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShownRefusal))]
    private Refusal? _refusal;

    /// <summary>The current refusal in the window's words, with the command's own account under Details.</summary>
    public WindowRefusal? ShownRefusal => Refusal is null ? null : WindowRefusal.From(Refusal);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAllFilter))]
    private WordProjectStatus? _statusFilter;

    // Replaces the status filter rather than narrowing it, so one chip is ever active.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAllFilter))]
    private bool _severalOnly;

    public bool IsAllFilter => StatusFilter is null && !SeveralOnly;

    public IRelayCommand ShowSeveralCommand { get; }

    [ObservableProperty]
    private bool _hasBaseline = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SummaryText))]
    private bool _isLoading;

    [ObservableProperty]
    private int _occurrenceCount;

    [ObservableProperty]
    private int _approvedCount;

    public int WordCount => _selectionReads.Summary?.DistinctFormCount ?? 0;

    public string SummaryText => IsLoading ? "Reading words in selected texts…"
        : WordCount == 0
        ? _selection.PastedWordEntries.Count is var pasted and > 0
            ? $"{pasted} pasted word{(pasted == 1 ? string.Empty : "s")} to test; check a text to see its words here"
            : "No words to test yet"
        : $"{WordCount:N0} word{(WordCount == 1 ? string.Empty : "s")} · {OccurrenceCount:N0} place{(OccurrenceCount == 1 ? string.Empty : "s")}";

    public int AllCount => _selectionReads.Summary?.WordRowCount ?? 0;
    public int ApprovedFilterCount => CountStanding(ProjectStanding.Approved);
    public int CandidateFilterCount => CountStanding(ProjectStanding.Candidate);
    public int RejectedFilterCount => CountStanding(ProjectStanding.Rejected);
    public int NotPresentFilterCount => CountStanding(ProjectStanding.NotPresent);
    public int IncorrectSpellingFilterCount => CountStanding(ProjectStanding.IncorrectSpelling);
    public int SeveralFilterCount => _selectionReads.Summary?.SeveralAnalysisRowCount ?? 0;

    private int CountStanding(string standing) =>
        _selectionReads.Summary?.WordRowsByProjectStanding.GetValueOrDefault(standing) ?? 0;

    private static string Normalize(string form) => form.Trim().Normalize(System.Text.NormalizationForm.FormD);

    private void OnReaderPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WorkspaceSelection.IsLoading)) IsLoading = _selectionReads.IsLoading;
        if (e.PropertyName == nameof(WorkspaceSelection.Refusal)) Refusal = _selectionReads.Refusal;
        if (e.PropertyName != nameof(WorkspaceSelection.Summary)) return;
        _readState = null;
        var summary = _selectionReads.Summary;
        OccurrenceCount = summary?.PhysicalOccurrenceCount ?? 0;
        ApprovedCount = summary?.Words.Count(word => word.ApprovedAnalysisCount > 0) ?? 0;
        HasBaseline = summary is not null;
        _selection.ClearTextCounts();
        foreach (var text in summary?.Texts ?? [])
            _selection.SetTextCounts(text.TextId, text.OccurrenceCount, text.DistinctFormCount);
        PublishSummaryRows();
        RaiseCounts();
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnStatusFilterChanged(WordProjectStatus? value) => ApplyFilter();
    partial void OnSeveralOnlyChanged(bool value) => ApplyFilter();

    /// <summary>Sets the project name used by rows that borrow the workspace reader.</summary>
    public Task SetProjectAsync(string? fwDataPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ClearProject();
        _projectPath = fwDataPath;
        if (fwDataPath is not null)
            OnReaderPropertyChanged(_selectionReads, new PropertyChangedEventArgs(nameof(WorkspaceSelection.Summary)));
        IsLoading = _selectionReads.IsLoading;
        Refusal = _selectionReads.Refusal;
        return Task.CompletedTask;
    }

    internal void ClearProject()
    {
        _acceptLoads = true;
        _projectPath = null;
        ReleaseSummaryRows();
        _projectRows = [];
        Rows = [];
        DisplayRows = [];
        _checkedKeys.Clear();
        OccurrenceCount = 0;
        ApprovedCount = 0;
        SearchText = string.Empty;
        StatusFilter = null;
        SeveralOnly = false;
        HasBaseline = false;
        IsLoading = false;
        Refusal = null;
        OnPropertyChanged(nameof(Rows));
        OnPropertyChanged(nameof(DisplayRows));
        OnPropertyChanged(nameof(ProjectWords));
        RaiseCheckedWords();
        HandOffCheckedWordsCommand.NotifyCanExecuteChanged();
        RaiseCounts();
    }

    /// <summary>Releases row ownership and drains outstanding visible detail and card reads.</summary>
    public async Task StopAsync()
    {
        _acceptLoads = false;
        IsLoading = false;
        ReleaseSummaryRows();
        await Task.WhenAll(_detailPending, CardPending).ConfigureAwait(true);
    }

    private static void Cancel(CancellationTokenSource? cancellation)
    {
        if (cancellation is null) return;
        try { cancellation.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    private void ApplyFilter() => ApplySummaryFilter();

    private Func<string, AssessWordRowViewModel?>? _assessed;

    internal void ShowAssessmentSource(Func<string, AssessmentWordResult?> source) => ShowAssessment(word =>
        source(word) is { } result ? new AssessWordRowViewModel(result, OccurrenceCountOf(word), WordRowRoutes) : null);

    /// <summary>
    /// Shows, on each word, what the latest Assessment came to for it; <see langword="null"/> clears that column.
    /// Kept across reloads, so words from a newly checked Text show their result too once it exists.
    /// </summary>
    public void ShowAssessment(Func<string, AssessWordRowViewModel?>? assessed)
    {
        _assessed = assessed;
        foreach (var entry in _summaryRows.Values.ToArray())
            entry.Row.ShowAssessment(ShownAssessmentOf(entry.Row.Summary, assessed?.Invoke(entry.Row.Form)));
    }

    private void RaiseCounts()
    {
        OnPropertyChanged(nameof(WordCount));
        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(AllCount));
        OnPropertyChanged(nameof(ApprovedFilterCount));
        OnPropertyChanged(nameof(CandidateFilterCount));
        OnPropertyChanged(nameof(RejectedFilterCount));
        OnPropertyChanged(nameof(NotPresentFilterCount));
        OnPropertyChanged(nameof(IncorrectSpellingFilterCount));
        OnPropertyChanged(nameof(SeveralFilterCount));
    }

    private bool CanHandOffCheckedWords() => HandOff is not null && CheckedWordCount > 0;

    private void RaiseCheckedWords()
    {
        OnPropertyChanged(nameof(CheckedWordCount));
        OnPropertyChanged(nameof(HandOffCheckedWordsLabel));
        OnPropertyChanged(nameof(HandOffCheckedWordsHelpText));
    }

    private void HandOffCheckedWords() =>
        HandOff?.Invoke(_checkedKeys.Select(key => key.Form).Distinct().ToArray());

    private void OnWordRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(TextWordRowViewModel.IsChecked)) return;
        if (sender is TextWordRowViewModel { Summary: { } summary } row)
        {
            if (row.IsChecked) _checkedKeys.Add(summary.Key);
            else _checkedKeys.Remove(summary.Key);
        }
        RaiseCheckedWords();
        HandOffCheckedWordsCommand.NotifyCanExecuteChanged();
    }

    private void OnSelectionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SelectionViewModel.HasTexts)) OnPropertyChanged(nameof(HasAvailableTexts));
        if (e.PropertyName == nameof(SelectionViewModel.PastedWords)) OnPropertyChanged(nameof(SummaryText));
    }
}

/// <summary>One wordform spelling as the Words table shows it: its occurrences and its own project analyses.</summary>
public sealed partial class TextWordRowViewModel : ObservableObject, IDisposable
{
    private Func<AssessmentWordResult, ResultsTokenViewModel?>? _wordCardTokenFactory;
    private readonly WordPresentationKey _presentationKey;
    private long _presentationRevision;
    private ListedWordViewModel? _observedListed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Presentation))]
    [NotifyPropertyChangedFor(nameof(PresentationState))]
    private bool _isChecked;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Presentation))]
    private string? _stagedText;

    // What the latest Assessment came to for this word; null before one, or when the word was not in it.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Listed))]
    [NotifyPropertyChangedFor(nameof(HasLastResult))]
    [NotifyPropertyChangedFor(nameof(LastResultMark))]
    [NotifyPropertyChangedFor(nameof(LastResultLabel))]
    private AssessWordRowViewModel? _lastResult;

    public bool HasLastResult => LastResult is not null;

    /// <summary>What PanGloss built for this word in the latest parse, as its outcome mark.</summary>
    public Mark? LastResultMark => LastResult is { } result ? Mark.Of(result.ParserOutcome) : null;

    /// <summary>The latest parse's outcome in the window's words.</summary>
    public string LastResultLabel => LastResult is { } result ? WindowWords.Of(result.ParserOutcome) : string.Empty;

    internal void ShowAssessment(AssessWordRowViewModel? result) => LastResult = result;

    partial void OnLastResultChanged(AssessWordRowViewModel? value)
    {
        if (_observedAssessmentWordRow is not null)
            _observedAssessmentWordRow.PropertyChanged -= OnAssessmentWordRowPropertyChanged;
        _observedAssessmentWordRow = value?.WordRow;
        if (_observedAssessmentWordRow is not null)
            _observedAssessmentWordRow.PropertyChanged += OnAssessmentWordRowPropertyChanged;
        ResetListed();
    }

    private void OnAssessmentWordRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(WordRowViewModel.IsUnread) || sender is not WordRowViewModel source ||
            _listed is not { } listed || listed.Row.IsUnread == source.IsUnread) return;
        listed.Row.IsUnread = source.IsUnread;
        _presentationRevision++;
        OnPropertyChanged(nameof(Presentation));
    }

    private void OnListedPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ListedWordViewModel.IsOpen))
            OnPropertyChanged(nameof(PresentationState));
    }

    internal Func<AssessmentWordResult, ResultsTokenViewModel?>? WordCardTokenFactory
    {
        get => _wordCardTokenFactory;
        set
        {
            _wordCardTokenFactory = value;
            if (LastResult is { } result) OnLastResultChanged(result);
        }
    }

    private ListedWordViewModel? _listed;
    private ListedWordViewModel? _notParsed;
    private bool _hasReadState;
    private bool? _isUnread;

    internal void ApplyReadState(bool? isUnread)
    {
        if (_hasReadState && _isUnread == isUnread) return;
        _hasReadState = true;
        _isUnread = isUnread;
        if ((_listed ?? _notParsed) is { } listed) listed.Row.IsUnread = isUnread;
        _presentationRevision++;
        OnPropertyChanged(nameof(Presentation));
    }
    private TextWord _word;
    private readonly WordRowRoutes? _routes;
    private readonly string? _projectName;
    private NotifyCollectionChangedEventHandler? _pendingChangesHandler;
    private WordRowViewModel? _observedAssessmentWordRow;

    private AssessWordRowViewModel ProjectAssessment(AssessWordRowViewModel result)
    {
        var stored = _word.Analyses.Select(analysis => new ParserReading(analysis.Morphs)
        {
            StoredAnalysisId = analysis.StoredAnalysisId,
            StoredAnalysisOpinion = analysis.StoredAnalysisOpinion,
            Identity = analysis.Identity,
        }).ToArray();
        var expected = stored.FirstOrDefault(analysis => analysis.StoredAnalysisOpinion == ReadingGrade.Approved)
            ?? (stored.Length == 1 ? stored[0] : null);
        // A spelling's retained comparison cannot establish a particular wordform's matches or opinions.
        var source = result.Source with
        {
            Comparison = null,
            AnalysisComparison = null,
            ReadingGrades = null,
            Correctness = null,
            MissedApproved = null,
            FixFirst = null,
            TryWordLink = _projectName is not null && WordformId is { } wordformId
                ? FieldWorksLinks.ForTarget(_projectName, new FieldWorksLinkTarget("Analyses", wordformId)) : null,
            StoredAnalyses = stored,
            StoredAnalysesAvailable = true,
            ExpectedAnalysis = expected,
            ProjectStanding = Summary?.ProjectStanding ?? Summary?.ProjectStanding ?? WordProjectStatuses.StandingOf(_word),
            OccurrenceCount = OccurrenceCount,
        };
        var projected = new AssessWordRowViewModel(source, OccurrenceCount, _routes);
        projected.WordRow.IsUnread = result.WordRow.IsUnread;
        return projected;
    }

    /// <summary>
    /// The word as the Word list shows it: its row and card from the latest parse, or, before one reaches it, what
    /// FieldWorks holds and Not parsed.
    /// </summary>
    public ListedWordViewModel Listed
    {
        get
        {
            var listed = LastResult is { } result
                ? _listed ??= ListedWordViewModel.Of(ProjectAssessment(result), _routes, _wordCardTokenFactory)
                : _notParsed ??= new ListedWordViewModel(WordRowViewModel.NotParsed(_word.Form,
                    Summary?.ProjectStanding ?? WordProjectStatuses.StandingOf(_word),
                    (_word.Approved.FirstOrDefault() ?? (_word.Analyses.Count == 1 ? _word.Analyses[0] : null))?.Morphs,
                    OccurrenceCount, _routes, _word.FormWritingSystem));
            if (_hasReadState) listed.Row.IsUnread = _isUnread;
            if (!ReferenceEquals(_observedListed, listed))
            {
                if (_observedListed is not null) _observedListed.PropertyChanged -= OnListedPropertyChanged;
                _observedListed = listed;
                listed.PropertyChanged += OnListedPropertyChanged;
                listed.PropertyChanged += OnListedChanged;
            }
            return listed;
        }
    }

    /// <summary>The module input tied to this row model's stable item identity and current evidence revision.</summary>
    public WordPresentation Presentation => new(_presentationKey, _presentationRevision, Listed.Row,
        WordListOwner.WordList, StagedText);

    /// <summary>The list state adapter that retains open and checked state on the item model.</summary>
    public WordInteractionState PresentationState
    {
        get => new(_presentationKey, Listed.IsOpen, IsChecked);
        set
        {
            if (value.Key != _presentationKey) return;
            Listed.IsOpen = value.IsOpen;
            if (value.IsChecked is { } isChecked) IsChecked = isChecked;
        }
    }

    public TextWordRowViewModel(TextWord word, WordRowRoutes? routes = null, string? projectName = null,
        Func<AssessmentWordResult, ResultsTokenViewModel?>? wordCardTokenFactory = null)
    {
        ArgumentNullException.ThrowIfNull(word);
        _word = word;
        _presentationKey = new WordPresentationKey(word.WordformGuid is { Length: > 0 } wordformGuid
            ? $"wordform:{wordformGuid}" : $"text-word:{Guid.NewGuid():N}");
        _routes = routes;
        _projectName = projectName;
        _wordCardTokenFactory = wordCardTokenFactory;
        if (_routes?.Changes is { } changes)
        {
            _pendingChangesHandler = (_, _) => UpdateStagedText();
            changes.Items.CollectionChanged += _pendingChangesHandler;
            UpdateStagedText();
        }
        Form = word.Form;
        FormWritingSystem = word.FormWritingSystem;
        WordformId = word.WordformGuid is { } id ? Guid.Parse(id) : null;
        OccurrenceCount = word.Occurrences.Count;
        HasApproved = word.Approved.Count > 0;

        var chosenKeys = word.Occurrences.Select(occurrence => occurrence.Analysis?.Key)
            .Where(key => key is not null).Distinct().ToList();
        Status = WordProjectStatuses.Of(word);
        HasSeveralAnalyses = chosenKeys.Count > 1;
        StatusLabel = WordProjectStatuses.LabelOf(Status, word.Approved.Count);

        ProjectSummary = HasSeveralAnalyses
            ? $"{chosenKeys.Count} analyses: {string.Join(", ", DistinctGlosses(word))}"
            : Status switch
            {
                WordProjectStatus.Approved => DistinctGlosses(word).FirstOrDefault() ?? string.Empty,
                WordProjectStatus.Candidate => "Unknown: stored, but nobody has approved it",
                WordProjectStatus.Rejected => "Only disapproved analyses are stored",
                WordProjectStatus.IncorrectSpelling => "FieldWorks marks this spelling as incorrect",
                _ => "Not analysed in the project",
            };
    }

    internal SelectionWordSummary? Summary { get; }
    private readonly ExpectedContext? _expectedContext;
    private ResultsTokenViewModel? _ownedCardToken;
    private IDisposable? _registration;
    private IDisposable? _detailOwnership;
    private TextWordsProjectedWordform? _storedWordform;
    internal bool HasStoredDetail => _detailOwnership is not null;
    internal event Action<TextWordRowViewModel, bool>? CardChanged;

    internal TextWordRowViewModel(SelectionWordSummary summary, IDisposable registration,
        WordRowRoutes? routes, string? projectName, ExpectedContext expectedContext) : this(new TextWord(summary.Key.Form,
            summary.Key.WordformId?.ToString("D"), [], [], [], summary.CandidateCount, summary.IncorrectSpelling)
            { FormWritingSystem = summary.Key.WritingSystem }, routes, projectName)
    {
        Summary = summary;
        _expectedContext = expectedContext;
        _registration = registration;
        WordformId = summary.Key.WordformId ?? (summary.Actions.CandidateWordformIds.Count == 1
            ? summary.Actions.CandidateWordformIds[0] : null);
        OccurrenceCount = summary.OccurrenceCount;
        HasApproved = summary.ApprovedAnalysisCount > 0;
        Status = WordProjectStatuses.FromStanding(summary.ProjectStanding);
        HasSeveralAnalyses = summary.ChosenAnalysisCount > 1;
        StatusLabel = WordProjectStatuses.LabelOf(Status, summary.ApprovedAnalysisCount);
        ProjectSummary = StatusLabel;
    }

    internal void SetStoredDetail(TextWordsProjectedWordform? wordform, IDisposable ownership)
    {
        var unchanged = HasStoredDetail && ReferenceEquals(_storedWordform, wordform);
        _detailOwnership?.Dispose();
        _detailOwnership = ownership;
        if (unchanged) return;
        _storedWordform = wordform;
        if (wordform is not null)
        {
            var stored = SelectionDisplayProjection.StoredAnalyses(wordform, _projectName);
            _word = _word with { Analyses = stored,
                Approved = stored.Where(analysis => analysis.StoredAnalysisOpinion == ReadingGrade.Approved).ToArray(),
                Disapproved = stored.Where(analysis => analysis.StoredAnalysisOpinion == ReadingGrade.Disapproved).ToArray() };
        }
        var chosen = _word.Analyses.Where(analysis => CanonicalId.TryParse(analysis.StoredAnalysisId, out var id) &&
            Summary!.ChosenAnalysisIds.Contains(id.ToGuid()));
        ProjectSummary = HasSeveralAnalyses
            ? $"{Summary!.ChosenAnalysisCount} analyses: {string.Join(", ", chosen.Select(GlossOf).Distinct())}"
            : Status switch
            {
                WordProjectStatus.Approved => _word.Approved.Select(GlossOf).FirstOrDefault() ?? string.Empty,
                WordProjectStatus.Candidate => "Unknown: stored, but nobody has approved it",
                WordProjectStatus.Rejected => "Only disapproved analyses are stored",
                WordProjectStatus.IncorrectSpelling => "FieldWorks marks this spelling as incorrect",
                _ => "Not analysed in the project",
            };
        OnPropertyChanged(nameof(ProjectSummary));
        _approvedAnalyses = null;
        ResetListed();
        UpdateStagedText();
    }

    internal void SetOccurrences(WordOccurrences occurrences, IReadOnlyList<SelectionTextSummary> texts)
    {
        var titles = texts.ToDictionary(text => text.TextId);
        _occurrences = occurrences.Occurrences.Select(item => new WordOccurrenceRowViewModel(new WordOccurrence(
            item.Location.Anchor.TextId, titles.GetValueOrDefault(item.Location.Anchor.TextId)?.Title ?? string.Empty,
            item.Location.LineNumber, item.Sentence, item.Location.Status, null)
            { SentenceStyle = item.SentenceStyle, SentenceWritingSystem = item.SentenceWritingSystem,
              TextTitleWritingSystem = titles.GetValueOrDefault(item.Location.Anchor.TextId)?.TitleWritingSystem })).ToArray();
        OnPropertyChanged(nameof(Occurrences));
    }

    internal ResultsTokenViewModel CreateCardToken(AssessmentWordResult result,
        ResultsInTextViewModel? actions, IDisposable registration)
    {
        _ownedCardToken?.Dispose();
        var token = new ResultsTokenViewModel("Selection", 0, new TextToken(Form, Form, null, null)
        {
            WordformId = WordformId, StoredAnalyses = _word.Analyses,
            IncorrectSpelling = _word.IncorrectSpelling, FormWritingSystem = FormWritingSystem,
            TextWritingSystem = FormWritingSystem, WordLink = result.TryWordLink,
        }, result, location: "From the selected Assessment", expectedContext: _expectedContext,
            producingAssessmentId: Summary?.Assessment?.AssessmentId, actionFacts: Summary?.Actions) { Actions = actions };
        token.OwnRegistration(registration);
        return _ownedCardToken = token;
    }

    internal void CloseCard()
    {
        if ((_listed ?? _notParsed) is { } listed) listed.IsOpen = false;
        _occurrences = [];
        _ownedCardToken?.Dispose();
        _ownedCardToken = null;
        OnPropertyChanged(nameof(Occurrences));
    }

    private void ResetListed()
    {
        _ownedCardToken?.Dispose();
        _ownedCardToken = null;
        var previous = _listed ?? _notParsed;
        var wasOpen = previous?.IsOpen == true;
        if (previous is not null)
        {
            previous.PropertyChanged -= OnListedChanged;
            previous.PropertyChanged -= OnListedPropertyChanged;
        }
        _observedListed = null;
        _presentationRevision++;
        _listed = null;
        _notParsed = null;
        OnPropertyChanged(nameof(Listed));
        OnPropertyChanged(nameof(Presentation));
        OnPropertyChanged(nameof(PresentationState));
        if (wasOpen) Listed.IsOpen = true;
    }

    private void OnListedChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ListedWordViewModel.IsOpen) && sender is ListedWordViewModel listed)
            CardChanged?.Invoke(this, listed.IsOpen);
    }

    /// <summary>Releases this row's stored detail, registration and pending-change subscription.</summary>
    public void Dispose()
    {
        CloseCard();
        DetachPendingChanges();
        if ((_listed ?? _notParsed) is { } listed) listed.PropertyChanged -= OnListedChanged;
        _detailOwnership?.Dispose();
        _detailOwnership = null;
        Interlocked.Exchange(ref _registration, null)?.Dispose();
    }

    internal TextWord Source => _word;

    internal static bool HasSeveral(TextWord word) => word.Occurrences
        .Select(occurrence => occurrence.Analysis?.Key).OfType<string>().Distinct().Take(2).Count() > 1;

    public string Form { get; }
    public string? FormWritingSystem { get; }
    public Guid? WordformId { get; private set; }
    public int OccurrenceCount { get; private set; }
    public bool HasApproved { get; private set; }
    public WordProjectStatus Status { get; private set; }

    /// <summary>Whether this spelling carries different analyses in different places, usually homographs.</summary>
    public bool HasSeveralAnalyses { get; private set; }

    /// <summary>The opinion mark behind <see cref="Status"/>; an incorrect spelling has none.</summary>
    public Mark? StatusMark => WordProjectStatuses.MarkOf(Status);

    public string StatusLabel { get; private set; }
    public string ProjectSummary { get; private set; }
    private IReadOnlyList<WordOccurrenceRowViewModel>? _occurrences;
    private IReadOnlyList<ProjectAnalysisViewModel>? _approvedAnalyses;

    public IReadOnlyList<WordOccurrenceRowViewModel> Occurrences => _occurrences ??=
        _word.Occurrences.Select(occurrence => new WordOccurrenceRowViewModel(occurrence)).ToArray();
    /// <summary>The card's complete occurrence source, displaying one leased context page at a time.</summary>
    public object OccurrenceSource => (object?)_occurrenceSource ?? Occurrences;
    private SelectionWordOccurrences? _occurrenceSource;

    internal void SetOccurrenceSource(SelectionWordOccurrences? source)
    {
        _occurrenceSource = source;
        OnPropertyChanged(nameof(OccurrenceSource));
    }

    internal void SetOccurrencePage(IReadOnlyList<WordOccurrenceRowViewModel> rows)
    {
        _occurrences = rows;
        OnPropertyChanged(nameof(Occurrences));
    }
    public IReadOnlyList<ProjectAnalysisViewModel> ApprovedAnalyses => _approvedAnalyses ??=
        _word.Approved.Select(analysis => new ProjectAnalysisViewModel(analysis)).ToArray();

    internal Task<KeyboardOpinionShortcutResult> StageOpinionShortcutAsync(KeyboardShortcutBehavior behavior) =>
        KeyboardOpinionShortcuts.StageAsync(Form, WordformId, _word.Analyses.Select(analysis =>
            new KeyboardStoredAnalysis(analysis.StoredAnalysisId ?? string.Empty,
                analysis.StoredAnalysisOpinion ?? ReadingGrade.Candidate, analysis.Morphs)).ToArray(),
            _routes, behavior, _expectedContext);

    private void UpdateStagedText()
    {
        if (_routes?.Changes is not { } changes) return;
        var storedIds = _word.Analyses.Select(analysis => analysis.StoredAnalysisId).ToHashSet(StringComparer.Ordinal);
        StagedText = changes.Items.FirstOrDefault(change => change.Word == Form &&
            change.StoredAnalysisId is { } id && storedIds.Contains(id) &&
            change.Kind is ChangeKinds.Approve or ChangeKinds.Reject or ChangeKinds.Candidate)?.TransitionText;
    }

    internal void DetachPendingChanges()
    {
        if (_pendingChangesHandler is not { } handler || _routes?.Changes is not { } changes) return;
        changes.Items.CollectionChanged -= handler;
        _pendingChangesHandler = null;
    }

    private static string GlossOf(ProjectAnalysis analysis) => string.Join(" ", analysis.Morphs.Select(morph => morph.Gloss));

    private static IEnumerable<string> DistinctGlosses(TextWord word) =>
        word.Occurrences.Select(occurrence => occurrence.Analysis)
            .Where(analysis => analysis is not null)
            .Select(analysis => string.Join(" ", analysis!.Morphs.Select(morph => morph.Gloss)))
            .Distinct();
}

/// <summary>One place a word occurs, as the Words table's expanded row shows it.</summary>
public sealed class WordOccurrenceRowViewModel : IDisposable
{
    private IDisposable? _registration;
    internal object? SourceIdentity { get; }
    internal int SourceOffset { get; }

    public WordOccurrenceRowViewModel(WordOccurrence occurrence) : this(occurrence, null, null, 0) { }

    internal WordOccurrenceRowViewModel(WordOccurrence occurrence, IDisposable? registration, object? identity, int offset)
    {
        ArgumentNullException.ThrowIfNull(occurrence);
        _registration = registration;
        SourceIdentity = identity;
        SourceOffset = offset;
        TextId = occurrence.TextId;
        TextTitle = occurrence.TextTitle;
        Line = occurrence.Line;
        Sentence = occurrence.Sentence;
        SentenceStyle = occurrence.SentenceStyle;
        SentenceWritingSystem = occurrence.SentenceWritingSystem;
        TextTitleWritingSystem = occurrence.TextTitleWritingSystem;
        Status = occurrence.Status;
        Analysis = occurrence.Analysis is { } analysis ? new ProjectAnalysisViewModel(analysis) : null;
        Location = $"{TextTitle}, line {Line}";
    }

    public Guid TextId { get; }
    public string TextTitle { get; }
    public int Line { get; }
    public string Sentence { get; }
    public string SentenceStyle { get; }
    public string? SentenceWritingSystem { get; }
    public string? TextTitleWritingSystem { get; }
    public string Status { get; }
    public ProjectAnalysisViewModel? Analysis { get; }
    public string Location { get; }

    /// <inheritdoc />
    public void Dispose() => Interlocked.Exchange(ref _registration, null)?.Dispose();
}

/// <summary>One analysis the project holds, as morphs a person reads.</summary>
public sealed class ProjectAnalysisViewModel
{
    public ProjectAnalysisViewModel(ProjectAnalysis analysis)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        Morphs = analysis.Morphs.Select(morph => new ParserReadingMorphViewModel(morph)).ToArray();
        Gloss = string.Join(" ", Morphs.Select(morph => morph.Gloss.Length == 0 ? "?" : morph.Gloss));
    }

    public IReadOnlyList<ParserReadingMorphViewModel> Morphs { get; }
    public string Gloss { get; }
}
