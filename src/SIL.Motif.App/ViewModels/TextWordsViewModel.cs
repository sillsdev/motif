using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.App.Services;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
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
/// Reads words from the selected Texts for the Words table: one row per form and wordform identity, its
/// occurrences, and what the project holds for it. Reloads whenever <see cref="SelectionViewModel.ChosenTextIds"/>
/// changes. A newer selection cancels the read it replaces, and only the current generation's answer is ever
/// applied, so a rapid run of checkbox clicks shows the last one's words. A read that fails clears the words it
/// would have replaced and sets <see cref="Refusal"/> instead of throwing.
/// </summary>
public sealed partial class TextWordsViewModel : ObservableObject
{
    private readonly ICommandClient _commandClient;
    private readonly SelectionViewModel _selection;
    private readonly List<TextWordRowViewModel> _all = [];
    private readonly object _loadGate = new();
    private readonly HashSet<Task> _activeLoads = [];
    private string? _projectPath;
    private int _generation;
    private bool _acceptLoads = true;
    private CancellationTokenSource? _reloadCancellation;

    public TextWordsViewModel(ICommandClient commandClient, SelectionViewModel selection)
    {
        ArgumentNullException.ThrowIfNull(commandClient);
        ArgumentNullException.ThrowIfNull(selection);
        _commandClient = commandClient;
        _selection = selection;
        _selection.PropertyChanged += OnSelectionPropertyChanged;
        ReloadCommand = new AsyncRelayCommand(ReloadAsync,
            AsyncRelayCommandOptions.AllowConcurrentExecutions | AsyncRelayCommandOptions.FlowExceptionsToTaskScheduler);
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
    }

    public ObservableCollection<TextWordRowViewModel> Rows { get; } = [];

    public IReadOnlyList<TextWordRowViewModel> ProjectWords => _all;

    /// <summary>Whether the current Baseline contains any Texts to open from a word row.</summary>
    public bool HasAvailableTexts => _selection.HasTexts;

    /// <summary>
    /// Reloads the words for the Texts checked now. A Text selection change runs it, and each run cancels the
    /// read it supersedes. Its execution task never faults: a failed read becomes <see cref="Refusal"/>.
    /// </summary>
    public IAsyncRelayCommand ReloadCommand { get; }

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

    public int CheckedWordCount => _all.Count(row => row.IsChecked);

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

    /// <summary>The last answer read for the checked Texts, for other views of the same words.</summary>
    [ObservableProperty]
    private TextWordsResponse? _response;

    [ObservableProperty]
    private int _approvedCount;

    public int WordCount => _all.Select(row => row.Form).Distinct(StringComparer.Ordinal).Count();

    public string SummaryText => IsLoading ? "Reading words in selected texts…"
        : WordCount == 0
        ? _selection.PastedWordEntries.Count is var pasted and > 0
            ? $"{pasted} pasted word{(pasted == 1 ? string.Empty : "s")} to test; check a text to see its words here"
            : "No words to test yet"
        : $"{WordCount:N0} word{(WordCount == 1 ? string.Empty : "s")} · {OccurrenceCount:N0} place{(OccurrenceCount == 1 ? string.Empty : "s")}";

    public int AllCount => _all.Count;
    public int ApprovedFilterCount => _all.Count(row => row.Status == WordProjectStatus.Approved);
    public int CandidateFilterCount => _all.Count(row => row.Status == WordProjectStatus.Candidate);
    public int RejectedFilterCount => _all.Count(row => row.Status == WordProjectStatus.Rejected);
    public int NotPresentFilterCount => _all.Count(row => row.Status == WordProjectStatus.NotPresent);
    public int IncorrectSpellingFilterCount => _all.Count(row => row.Status == WordProjectStatus.IncorrectSpelling);
    public int SeveralFilterCount => _all.Count(row => row.HasSeveralAnalyses);

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnStatusFilterChanged(WordProjectStatus? value) => ApplyFilter();
    partial void OnSeveralOnlyChanged(bool value) => ApplyFilter();

    /// <summary>Sets the project to read words from and immediately reloads for whatever is checked now.</summary>
    public async Task SetProjectAsync(string? fwDataPath, CancellationToken cancellationToken = default)
    {
        ClearProject();
        if (fwDataPath is null) return;
        lock (_loadGate) _projectPath = fwDataPath;
        await ReloadAsync(cancellationToken).ConfigureAwait(true);
    }

    internal void ClearProject()
    {
        CancellationTokenSource? superseded;
        lock (_loadGate)
        {
            superseded = _reloadCancellation;
            _reloadCancellation = null;
            _acceptLoads = true;
            _projectPath = null;
            _generation++;
        }
        Cancel(superseded);
        ClearWords();
        SearchText = string.Empty;
        StatusFilter = null;
        SeveralOnly = false;
        HasBaseline = true;
        IsLoading = false;
        Refusal = null;
    }

    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        TaskCompletionSource completed;
        CancellationTokenSource loadCancellation;
        CancellationTokenSource? superseded;
        string path;
        IReadOnlyList<Guid> textIds;
        int generation;
        lock (_loadGate)
        {
            if (!_acceptLoads || _projectPath is not { } projectPath) return;
            path = projectPath;
            textIds = _selection.ChosenTextIds;
            generation = ++_generation;
            superseded = _reloadCancellation;
            loadCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _reloadCancellation = loadCancellation;
            completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _activeLoads.Add(completed.Task);
            IsLoading = true;
            Refusal = null;
        }

        try
        {
            Cancel(superseded);
            var outcome = await _commandClient.ListTextWordsAsync(
                    new TextWordsRequest(path, textIds), loadCancellation.Token)
                .ConfigureAwait(true);
            if (generation != _generation) return;

            if (!outcome.Succeeded)
            {
                // A read the caller cancelled says nothing about the words, so the page stays as it is.
                if (!loadCancellation.IsCancellationRequested) ShowFailure(outcome.Refusal);
                return;
            }

            HasBaseline = outcome.Value!.HasBaseline;
            Response = outcome.Value;

            foreach (var row in _all) row.PropertyChanged -= OnWordRowPropertyChanged;
            _all.Clear();
            _all.AddRange(outcome.Value.Words.Select(word => new TextWordRowViewModel(word, WordRowRoutes,
                Path.GetFileNameWithoutExtension(path))));
            foreach (var row in _all) row.PropertyChanged += OnWordRowPropertyChanged;
            RaiseCheckedWords();
            HandOffCheckedWordsCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(ProjectWords));
            if (_assessed is { } assessed)
                foreach (var row in _all) row.ShowAssessment(assessed(row.Form));
            OccurrenceCount = outcome.Value.OccurrenceCount;
            ApprovedCount = _all.Count(row => row.HasApproved);
            RaiseCounts();
            ApplyFilter();

            _selection.ClearTextCounts();
            foreach (var textId in textIds)
            {
                var occurrences = _all.Sum(row => row.Occurrences.Count(occurrence => occurrence.TextId == textId));
                var distinct = _all.Count(row => row.Occurrences.Any(occurrence => occurrence.TextId == textId));
                _selection.SetTextCounts(textId, occurrences, distinct);
            }
        }
        catch (OperationCanceledException) when (loadCancellation.IsCancellationRequested || cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (generation == _generation)
                ShowFailure(new Refusal("texts.words-query-failed", FailureReason.Refused, exception.Message));
        }
        finally
        {
            lock (_loadGate)
            {
                _activeLoads.Remove(completed.Task);
                if (ReferenceEquals(_reloadCancellation, loadCancellation)) _reloadCancellation = null;
                if (generation == _generation) IsLoading = false;
            }
            completed.SetResult();
            loadCancellation.Dispose();
        }
    }

    /// <summary>Stops accepting word reads and returns once every active read has finished.</summary>
    public async Task StopAsync()
    {
        Task[] activeLoads;
        CancellationTokenSource? activeCancellation;
        lock (_loadGate)
        {
            _acceptLoads = false;
            _generation++;
            activeCancellation = _reloadCancellation;
            _reloadCancellation = null;
            IsLoading = false;
            activeLoads = _activeLoads.ToArray();
        }
        Cancel(activeCancellation);
        await Task.WhenAll(activeLoads).ConfigureAwait(true);
    }

    // Empties the Words table and its counts, so no earlier selection's words outlive a reset or a failure.
    private void ClearWords()
    {
        foreach (var row in _all) row.PropertyChanged -= OnWordRowPropertyChanged;
        _all.Clear();
        Rows.Clear();
        OccurrenceCount = 0;
        ApprovedCount = 0;
        Response = null;
        RaiseCheckedWords();
        HandOffCheckedWordsCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(ProjectWords));
        RaiseCounts();
    }

    private void ShowFailure(Refusal? refusal)
    {
        ClearWords();
        _selection.ClearTextCounts();
        Refusal = refusal;
    }

    private static void Cancel(CancellationTokenSource? cancellation)
    {
        if (cancellation is null) return;
        try { cancellation.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    private void ApplyFilter()
    {
        Rows.Clear();
        var matches = _all.AsEnumerable();
        if (StatusFilter is { } status) matches = matches.Where(row => row.Status == status);
        if (SeveralOnly) matches = matches.Where(row => row.HasSeveralAnalyses);
        if (!string.IsNullOrWhiteSpace(SearchText))
            matches = matches.Where(row => row.Form.Contains(SearchText.Trim(), StringComparison.CurrentCultureIgnoreCase));
        // The most frequent words first: a fix that helps them helps the most of the text.
        foreach (var row in matches.OrderByDescending(row => row.OccurrenceCount)) Rows.Add(row);
    }

    private Func<string, AssessWordRowViewModel?>? _assessed;

    /// <summary>
    /// Shows, on each word, what the latest Assessment came to for it; <see langword="null"/> clears that column.
    /// Kept across reloads, so words from a newly checked Text show their result too once it exists.
    /// </summary>
    public void ShowAssessment(Func<string, AssessWordRowViewModel?>? assessed)
    {
        _assessed = assessed;
        foreach (var row in _all) row.ShowAssessment(assessed?.Invoke(row.Form));
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
        HandOff?.Invoke(_all.Where(row => row.IsChecked).Select(row => row.Form).ToArray());

    private void OnWordRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(TextWordRowViewModel.IsChecked)) return;
        RaiseCheckedWords();
        HandOffCheckedWordsCommand.NotifyCanExecuteChanged();
    }

    private void OnSelectionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SelectionViewModel.HasTexts)) OnPropertyChanged(nameof(HasAvailableTexts));
        if (e.PropertyName == nameof(SelectionViewModel.PastedWords)) OnPropertyChanged(nameof(SummaryText));
        if (e.PropertyName == nameof(SelectionViewModel.ChosenTextIds)) ReloadCommand.Execute(null);
    }
}

/// <summary>One wordform spelling as the Words table shows it: its occurrences and its own project analyses.</summary>
public sealed partial class TextWordRowViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isChecked;

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
        var wasOpen = _listed?.IsOpen == true;
        _listed = value is null ? null : ListedWordViewModel.Of(ProjectAssessment(value), _routes);
        if (_listed is not null) _listed.IsOpen = wasOpen;
        else if (wasOpen) _notParsed.IsOpen = true;
    }

    private ListedWordViewModel? _listed;
    private readonly ListedWordViewModel _notParsed;
    private readonly TextWord _word;
    private readonly WordRowRoutes? _routes;
    private readonly string? _projectName;

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
            ProjectStanding = WordProjectStatuses.StandingOf(_word),
            OccurrenceCount = OccurrenceCount,
        };
        return new AssessWordRowViewModel(source, OccurrenceCount, _routes);
    }

    /// <summary>
    /// The word as the Word list shows it: its row and card from the latest parse, or, before one reaches it, what
    /// FieldWorks holds and Not parsed.
    /// </summary>
    public ListedWordViewModel Listed => _listed ?? _notParsed;

    public TextWordRowViewModel(TextWord word, WordRowRoutes? routes = null, string? projectName = null)
    {
        ArgumentNullException.ThrowIfNull(word);
        _word = word;
        _routes = routes;
        _projectName = projectName;
        Form = word.Form;
        WordformId = word.WordformGuid is { } id ? Guid.Parse(id) : null;
        var held = word.Approved.FirstOrDefault() ?? (word.Analyses.Count == 1 ? word.Analyses[0] : null);
        _notParsed = new ListedWordViewModel(WordRowViewModel.NotParsed(word.Form, WordProjectStatuses.StandingOf(word),
            held?.Morphs, word.Occurrences.Count, routes));
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

        Occurrences = word.Occurrences.Select(occurrence => new WordOccurrenceRowViewModel(occurrence)).ToArray();
        ApprovedAnalyses = word.Approved.Select(analysis => new ProjectAnalysisViewModel(analysis)).ToArray();
    }

    public string Form { get; }
    public Guid? WordformId { get; }
    public int OccurrenceCount { get; }
    public bool HasApproved { get; }
    public WordProjectStatus Status { get; }

    /// <summary>Whether this spelling carries different analyses in different places, usually homographs.</summary>
    public bool HasSeveralAnalyses { get; }

    /// <summary>The opinion mark behind <see cref="Status"/>; an incorrect spelling has none.</summary>
    public Mark? StatusMark => WordProjectStatuses.MarkOf(Status);

    public string StatusLabel { get; }
    public string ProjectSummary { get; }
    public IReadOnlyList<WordOccurrenceRowViewModel> Occurrences { get; }
    public IReadOnlyList<ProjectAnalysisViewModel> ApprovedAnalyses { get; }

    private static IEnumerable<string> DistinctGlosses(TextWord word) =>
        word.Occurrences.Select(occurrence => occurrence.Analysis)
            .Where(analysis => analysis is not null)
            .Select(analysis => string.Join(" ", analysis!.Morphs.Select(morph => morph.Gloss)))
            .Distinct();
}

/// <summary>One place a word occurs, as the Words table's expanded row shows it.</summary>
public sealed class WordOccurrenceRowViewModel
{
    public WordOccurrenceRowViewModel(WordOccurrence occurrence)
    {
        ArgumentNullException.ThrowIfNull(occurrence);
        TextId = occurrence.TextId;
        TextTitle = occurrence.TextTitle;
        Line = occurrence.Line;
        Sentence = occurrence.Sentence;
        Status = occurrence.Status;
        Analysis = occurrence.Analysis is { } analysis ? new ProjectAnalysisViewModel(analysis) : null;
        Location = $"{TextTitle}, line {Line}";
    }

    public Guid TextId { get; }
    public string TextTitle { get; }
    public int Line { get; }
    public string Sentence { get; }
    public string Status { get; }
    public ProjectAnalysisViewModel? Analysis { get; }
    public string Location { get; }
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
