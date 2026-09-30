using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.App.Services;
using SIL.Motif.App.Controls;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.ViewModels;

/// <summary>How the parser's answer for one occurrence compares with what the project stores there.</summary>
public enum OccurrenceVerdict
{
    /// <summary>The parser produced the analysis stored at this occurrence.</summary>
    Matches,

    /// <summary>An analysis is stored here, and the parser did not produce it.</summary>
    Differs,

    /// <summary>Nothing is stored here, and the parser proposes something.</summary>
    New,

    /// <summary>Nothing is stored here, and the parser found no way to build the word.</summary>
    NoParse,

    /// <summary>The parser stopped at a time or step limit before finishing this word.</summary>
    Limit,

    /// <summary>This word was not part of the Assessment.</summary>
    NotAssessed,
}

/// <summary>The Analyze texts filter chips for occurrence verdicts and available marking actions.</summary>
public enum ResultsInTextFilter
{
    All,
    Unread,
    Differs,
    New,
    NoParse,
    Matches,
    Limit,
    NotAssessed,
    NeedsALook,
}

/// <summary>
/// Analyze texts reads the selected Texts in place, with each word's stored analysis and parser verdict beneath it.
/// It rebuilds whenever the selected Texts load new words or
/// an Assessment finishes; a filter keeps only the lines holding a matching word and dims the rest of them.
/// </summary>
public sealed partial class ResultsInTextViewModel : ObservableObject
{
    private readonly TextWordsViewModel _texts;
    private readonly AssessViewModel _assess;
    private readonly Action<string> _showWord;
    private readonly Action<string> _tryWord;
    private readonly ChangesViewModel _changes;
    private readonly ICommandClient _commands;
    private IReadOnlyList<ResultsTokenViewModel> _allWords = [];
    private long _readStateGeneration;
    private long _readStateWriteVersion;
    private Task _readStateRefresh = Task.CompletedTask;

    [ObservableProperty]
    private WindowRefusal? _readStateRefusal;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasReadStateNotice))]
    private string? _readStateNotice;

    public ResultsInTextViewModel(
        TextWordsViewModel texts, AssessViewModel assess, Action<string> showWord, Action<string> tryWord,
        ChangesViewModel changes, ICommandClient commands)
    {
        ArgumentNullException.ThrowIfNull(texts);
        ArgumentNullException.ThrowIfNull(assess);
        ArgumentNullException.ThrowIfNull(showWord);
        ArgumentNullException.ThrowIfNull(tryWord);
        _texts = texts;
        _assess = assess;
        _showWord = showWord;
        _tryWord = tryWord;
        _changes = changes ?? throw new ArgumentNullException(nameof(changes));
        _commands = commands ?? throw new ArgumentNullException(nameof(commands));
        SetFilterCommand = new RelayCommand<ResultsInTextFilter>(filter => Filter = filter);
        SelectAllWordsCommand = new RelayCommand(SelectAllWords, CanSelectAllWords);
        ClearSelectedWordsCommand = new RelayCommand(ClearSelectedWords, () => HasCheckedWords);
        ShowInWordsCommand = new RelayCommand(() => { if (SelectedToken is { } token) _showWord(token.Form); });
        TryWordCommand = new RelayCommand(() => { if (SelectedToken is { } token) _tryWord(token.Form); });
        OpenPanGlossGuideCommand = new RelayCommand(() => OpenPanGlossGuide?.Invoke());
        RecheckChangesCommand = new AsyncRelayCommand(() => _changes.RecheckAsync(), CanRecheckChanges);
        AddChangeCommand = new AsyncRelayCommand<string>(AddSelectedChangeAsync, CanAddSelectedChange);
        StagePrimaryMarkingActionCommand = new AsyncRelayCommand(StagePrimaryMarkingActionAsync,
            CanStagePrimaryMarkingAction);
        StagePrimaryMarkingActionForTokenCommand = new AsyncRelayCommand<ResultsTokenViewModel>(
            StagePrimaryMarkingActionForTokenAsync, CanStagePrimaryMarkingActionForToken);
        StageMarkingChoiceCommand = new AsyncRelayCommand<AnalysisMarkingChoice>(StageMarkingChoiceAsync,
            CanStageMarkingChoice);
        MarkTokenReadCommand = new AsyncRelayCommand<ResultsTokenViewModel>(
            token => token is null ? Task.CompletedTask : MarkReadAsync(token), CanMarkTokenReadState);
        MarkTokenUnreadCommand = new AsyncRelayCommand<ResultsTokenViewModel>(
            token => token is null ? Task.CompletedTask : MarkUnreadAsync(token), CanMarkTokenReadState);
        MarkTextReadCommand = new AsyncRelayCommand(MarkTextReadAsync, () => SelectedText is not null);
        MarkTextUnreadCommand = new AsyncRelayCommand(MarkTextUnreadAsync, () => SelectedText is not null);
        MarkSelectionReadCommand = new AsyncRelayCommand(MarkSelectionReadAsync, CanMarkSelectionReadState);
        MarkSelectionUnreadCommand = new AsyncRelayCommand(MarkSelectionUnreadAsync, CanMarkSelectionReadState);
        InitializeScopeCommands();
        RecheckCheckedChangesCommand = new AsyncRelayCommand(() => _changes.RecheckAsync(),
            CanRecheckCheckedChanges);
        _texts.PropertyChanged += OnSourceChanged;
        _assess.PropertyChanged += OnSourceChanged;
        _changes.Items.CollectionChanged += OnChangesChanged;
        Rebuild();
    }

    public ObservableCollection<ResultsTextViewModel> Texts { get; } = [];

    /// <summary>The selected Text's lines that hold a word matching <see cref="Filter"/>; every line under All.</summary>
    public ObservableCollection<ResultsLineViewModel> VisibleLines { get; } = [];

    public IRelayCommand<ResultsInTextFilter> SetFilterCommand { get; }
    public IRelayCommand SelectAllWordsCommand { get; }
    public IRelayCommand ClearSelectedWordsCommand { get; }

    /// <summary>Opens the selected word in the Words view.</summary>
    public IRelayCommand ShowInWordsCommand { get; }

    /// <summary>Opens the selected word in the Words view and traces it there.</summary>
    public IRelayCommand TryWordCommand { get; }

    /// <summary>Opens the PanGloss guide in the window's Help popup.</summary>
    public IRelayCommand OpenPanGlossGuideCommand { get; }

    /// <summary>Checks pending changes against the latest project state.</summary>
    public IAsyncRelayCommand RecheckChangesCommand { get; }

    /// <summary>The window callback that shows the PanGloss guide.</summary>
    public Action? OpenPanGlossGuide { get; set; }

    public IAsyncRelayCommand<string> AddChangeCommand { get; }

    public IAsyncRelayCommand StagePrimaryMarkingActionCommand { get; }

    /// <summary>Stages the primary action for the word whose strip contains the button.</summary>
    public IAsyncRelayCommand<ResultsTokenViewModel> StagePrimaryMarkingActionForTokenCommand { get; }

    public IAsyncRelayCommand<AnalysisMarkingChoice> StageMarkingChoiceCommand { get; }

    public IAsyncRelayCommand<ResultsTokenViewModel> MarkTokenReadCommand { get; }

    public IAsyncRelayCommand<ResultsTokenViewModel> MarkTokenUnreadCommand { get; }

    public IAsyncRelayCommand MarkTextReadCommand { get; }

    public IAsyncRelayCommand MarkTextUnreadCommand { get; }

    public IAsyncRelayCommand MarkSelectionReadCommand { get; }

    public IAsyncRelayCommand MarkSelectionUnreadCommand { get; }
    /// <summary>Checks pending changes that affect checked occurrences again.</summary>
    public IAsyncRelayCommand RecheckCheckedChangesCommand { get; }

    public ChangesViewModel Changes => _changes;

    internal Task ReadStateRefresh => _readStateRefresh;

    [ObservableProperty]
    private ResultsTextViewModel? _selectedText;

    [ObservableProperty]
    private ResultsInTextFilter _filter = ResultsInTextFilter.All;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedToken))]
    private ResultsTokenViewModel? _selectedToken;

    public bool HasSelectedToken => SelectedToken is not null;

    public int CheckedWordCount => _allWords.Count(token => token.IsSelectedForActions);
    public string CheckedWordCountLabel => $"Selected {CheckedWordCount} of {AllCount} words";

    public bool HasCheckedWords => CheckedWordCount > 0;

    public bool HasCheckedUncertainChanges => CheckedTokens.Any(token =>
        _changes.Items.Any(change => change.Word == token.Form && change.IsUncertain));

    private ResultsTokenViewModel[] CheckedTokens => _allWords.Where(token => token.IsSelectedForActions).ToArray();

    public int AllCount => _allWords.Count;
    public int UnreadCount => _allWords.Count(token => token.Marking.IsUnread);
    public int SelectedReadStateCount => CheckedWordCount;
    public bool HasSelectedReadStateOccurrences => SelectedReadStateCount > 0;
    public bool HasReadStateNotice => !string.IsNullOrWhiteSpace(ReadStateNotice);
    public int DiffersCount => Count(OccurrenceVerdict.Differs);
    public int NewCount => Count(OccurrenceVerdict.New);
    public int NoParseCount => Count(OccurrenceVerdict.NoParse);
    public int MatchesCount => Count(OccurrenceVerdict.Matches);
    public int LimitCount => Count(OccurrenceVerdict.Limit);
    public int NotAssessedCount => Count(OccurrenceVerdict.NotAssessed);
    public int NeedsALookCount => _allWords.Count(token => token.Marking.NeedsALook);
    public bool HasNotAssessed => NotAssessedCount > 0;
    public int SelectedTextAnalysisCount => StoredAnalysisIds(SelectedTextWords()).Count;
    public int ChosenTextsAnalysisCount => StoredAnalysisIds(_allWords).Count;
    public int SelectedTextWordCount => SelectedTextWords().Count();
    public string ScopeCountSummary =>
        $"{SelectedTextWordCount} words in this Text · {AllCount} words in all chosen Texts";
    public bool HasSelectedTextAnalyses => SelectedTextAnalysisCount > 0;
    public bool HasChosenTextAnalyses => ChosenTextsAnalysisCount > 0;
    public string SelectedTextRemoveHeader => $"Preview removal from this Text ({SelectedTextAnalysisCount})";
    public string ChosenTextsRemoveHeader => $"Preview removal in all chosen Texts ({ChosenTextsAnalysisCount})";
    public string SelectedTextRemovalPreview => RemovalUsesPreview(SelectedTextWords());
    public string ChosenTextsRemovalPreview => RemovalUsesPreview(_allWords);

    /// <summary>Why nothing is shown, or <see langword="null"/> when there are lines to read.</summary>
    public string? Message => _assess.Result is null ? null
        : Texts.Count == 0 ? "Check a text in Texts to read the results in place."
        : SelectedText is null ? "Choose a text to read."
        : SelectedText.Lines.Count == 0
            ? "This text has no lines split into words yet. Open it once in FieldWorks' Interlinear Texts, then refresh the Baseline."
        : VisibleLines.Count == 0 ? "No word in this text matches the chosen filter."
        : null;

    public bool HasMessage => Message is not null;

    /// <summary>Whether there is a text to read, so the text picker and filters have something to act on.</summary>
    public bool HasTexts => Texts.Count > 0;

    /// <summary>Whether an Assessment is available to compare with the selected Texts.</summary>
    public bool HasAssessment => _assess.Result is not null;

    /// <summary>Whether the selected Text has results to display.</summary>
    public bool HasResults => HasAssessment && !HasMessage;

    /// <summary>The Assessment action for the empty Analyze texts state.</summary>
    public IAsyncRelayCommand ParseWordsCommand => _assess.RunCommand;

    /// <summary>Whether the only thing missing is a checked Text, which the Texts page can supply.</summary>
    public bool NeedsTexts => _assess.Result is not null && Texts.Count == 0;

    /// <summary>Finds the loaded sentence that contains the requested source occurrence.</summary>
    public ResultsLineViewModel? FindOccurrenceLine(OccurrenceAnchor occurrence) => Texts
        .SelectMany(text => text.Lines)
        .FirstOrDefault(line => line.TextId == occurrence.TextId &&
            line.ParagraphId == occurrence.ParagraphId && line.SegmentId == occurrence.SegmentId &&
            line.Tokens.Any(token => token.Occurrence == occurrence));

    public TextOccurrenceLocation? LocateOccurrence(OccurrenceAnchor occurrence)
    {
        for (var textIndex = 0; textIndex < Texts.Count; textIndex++)
        for (var lineIndex = 0; lineIndex < Texts[textIndex].Lines.Count; lineIndex++)
        {
            var text = Texts[textIndex];
            var line = text.Lines[lineIndex];
            var token = line.Tokens.FirstOrDefault(candidate => candidate.Occurrence == occurrence);
            if (token is not null)
                return new TextOccurrenceLocation(textIndex, line.Number, token.OccurrenceIndex,
                    $"{text.Title}, line {line.Number}, word {token.OccurrenceIndex + 1}");
        }
        return null;
    }

    /// <summary>Opens the Texts page for the empty state that asks for a checked Text.</summary>
    public Action? OpenTexts { get; set; }

    /// <summary>Shows a clicked word's comparison in the side panel.</summary>
    public void SelectToken(ResultsTokenViewModel token)
    {
        ArgumentNullException.ThrowIfNull(token);
        if (token.IsWord)
        {
            SelectedToken = token;
            AddChangeCommand.NotifyCanExecuteChanged();
        }
    }

    public void CloseTokenCard() => SelectedToken = null;

    public async Task MoveTokenCardAsync(int direction)
    {
        if (direction is not (-1 or 1) || SelectedToken is null || SelectedText is null) return;
        var words = SelectedText.Lines.SelectMany(line => line.Tokens).Where(token => token.IsWord).ToArray();
        var current = Array.IndexOf(words, SelectedToken);
        var next = current + direction;
        if (current < 0 || next < 0 || next >= words.Length) return;
        await OpenTokenCardAsync(words[next]).ConfigureAwait(true);
    }

    /// <summary>Selects a word card and records that the reader opened it.</summary>
    /// <param name="token">The word occurrence whose comparison card was opened.</param>
    /// <returns>A task that completes after its Read state is saved.</returns>
    public async Task OpenTokenCardAsync(ResultsTokenViewModel token)
    {
        ArgumentNullException.ThrowIfNull(token);
        SelectToken(token);
        await MarkReadAsync(token).ConfigureAwait(true);
    }

    /// <summary>Marks one word occurrence Read.</summary>
    /// <param name="token">The occurrence to mark Read.</param>
    /// <returns>A task that completes after the store command returns.</returns>
    public Task MarkReadAsync(ResultsTokenViewModel token)
    {
        ArgumentNullException.ThrowIfNull(token);
        return SetReadStateAsync([token], true);
    }

    /// <summary>Marks one word occurrence Unread.</summary>
    /// <param name="token">The occurrence to mark Unread.</param>
    /// <returns>A task that completes after the store command returns.</returns>
    public Task MarkUnreadAsync(ResultsTokenViewModel token)
    {
        ArgumentNullException.ThrowIfNull(token);
        return SetReadStateAsync([token], false);
    }

    /// <summary>Marks the supplied word occurrences Read.</summary>
    /// <param name="selection">The occurrences to mark Read.</param>
    /// <returns>A task that completes after the store command returns.</returns>
    public Task MarkReadAsync(IReadOnlyList<ResultsTokenViewModel> selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        return SetReadStateAsync(selection, true);
    }

    /// <summary>Marks the supplied word occurrences Unread.</summary>
    /// <param name="selection">The occurrences to mark Unread.</param>
    /// <returns>A task that completes after the store command returns.</returns>
    public Task MarkUnreadAsync(IReadOnlyList<ResultsTokenViewModel> selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        return SetReadStateAsync(selection, false);
    }

    /// <summary>Marks every word occurrence in the selected Text Read.</summary>
    /// <returns>A task that completes after the store command returns.</returns>
    public Task MarkTextReadAsync() => SetSelectedTextReadStateAsync(true);

    /// <summary>Marks every word occurrence in the selected Text Unread.</summary>
    /// <returns>A task that completes after the store command returns.</returns>
    public Task MarkTextUnreadAsync() => SetSelectedTextReadStateAsync(false);

    /// <summary>Marks the selected word occurrences Read.</summary>
    /// <returns>A task that completes after the store command returns.</returns>
    public Task MarkSelectionReadAsync() => MarkReadAsync(SelectedReadStateOccurrences());

    /// <summary>Marks the selected word occurrences Unread.</summary>
    /// <returns>A task that completes after the store command returns.</returns>
    public Task MarkSelectionUnreadAsync() => MarkUnreadAsync(SelectedReadStateOccurrences());

    /// <summary>Selects a word from any page, building its detail even when it has no chosen-text occurrence.</summary>
    public void SelectWord(string word)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(word);
        var token = Texts.SelectMany(text => text.Lines).SelectMany(line => line.Tokens)
            .FirstOrDefault(candidate => candidate.IsWord && candidate.Form == word);
        if (token is not null)
        {
            SelectToken(token);
            return;
        }

        var result = _assess.Result?.Words.FirstOrDefault(candidate => candidate.Word == word);
        if (result is null)
        {
            SelectedToken = null;
            return;
        }

        var projectWord = _texts.ProjectWords.FirstOrDefault(candidate => candidate.Form == word);
        SelectToken(new ResultsTokenViewModel("Assessment", 0, new TextToken(word, word, null, null), result,
            projectWord, "Not in a chosen text"));
    }

    partial void OnSelectedTextChanged(ResultsTextViewModel? value)
    {
        RefreshLines();
        OnPropertyChanged(nameof(SelectedTextAnalysisCount));
        OnPropertyChanged(nameof(SelectedTextWordCount));
        OnPropertyChanged(nameof(ScopeCountSummary));
        OnPropertyChanged(nameof(HasSelectedTextAnalyses));
        OnPropertyChanged(nameof(SelectedTextRemoveHeader));
        OnPropertyChanged(nameof(SelectedTextRemovalPreview));
        MarkTextReadCommand.NotifyCanExecuteChanged();
        MarkTextUnreadCommand.NotifyCanExecuteChanged();
        NotifyScopeCommands();
    }

    partial void OnFilterChanged(ResultsInTextFilter value) => RefreshLines();

    internal void ClearProject()
    {
        _readStateGeneration++;
        SelectedText = null;
        SelectedToken = null;
    }

    partial void OnSelectedTokenChanging(ResultsTokenViewModel? oldValue, ResultsTokenViewModel? newValue)
    {
        if (oldValue is not null) oldValue.PropertyChanged -= OnSelectedTokenPropertyChanged;
        if (oldValue is not null) oldValue.IsCardOpen = false;
        if (newValue is not null) newValue.PropertyChanged += OnSelectedTokenPropertyChanged;
        if (newValue is not null) newValue.IsCardOpen = true;
    }

    partial void OnSelectedTokenChanged(ResultsTokenViewModel? value)
    {
        AddChangeCommand.NotifyCanExecuteChanged();
        StagePrimaryMarkingActionCommand.NotifyCanExecuteChanged();
        StageMarkingChoiceCommand.NotifyCanExecuteChanged();
    }

    private int Count(OccurrenceVerdict verdict) => _allWords.Count(token => token.Verdict == verdict);

    private void OnSourceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (ReferenceEquals(sender, _texts) && e.PropertyName is nameof(TextWordsViewModel.Response) or nameof(TextWordsViewModel.ProjectWords)) Rebuild();
        else if (ReferenceEquals(sender, _assess) && e.PropertyName == nameof(AssessViewModel.Result)) Rebuild();
        NotifyScopeCommands();
    }

    private void Rebuild()
    {
        foreach (var token in _allWords) token.PropertyChanged -= OnTokenPropertyChanged;
        var readStateGeneration = _readStateGeneration;
        var previousTitle = SelectedText?.Title;
        var results = (_assess.Result?.Words ?? [])
            .GroupBy(word => word.Word, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var projectWords = _texts.ProjectWords.ToDictionary(row => row.Form, StringComparer.Ordinal);

        Texts.Clear();
        if (_texts.Response is { } response)
        {
            foreach (var text in response.Texts) Texts.Add(new ResultsTextViewModel(text, results, projectWords));
        }
        foreach (var token in _allWords) token.PropertyChanged -= OnTokenPropertyChanged;
        _allWords = Texts.SelectMany(text => text.Lines).SelectMany(line => line.Tokens).Where(token => token.IsWord).ToArray();
        foreach (var token in _allWords) token.PropertyChanged += OnTokenPropertyChanged;
        foreach (var token in _allWords)
        {
            token.Actions = this;
            token.StageMarkingChoiceForTokenCommand = new AsyncRelayCommand<AnalysisMarkingChoice>(
                choice => StageMarkingChoiceForTokenAsync(token, choice),
                choice => CanStageMarkingChoice(token, choice));
        }
        OnPropertyChanged(nameof(ChosenTextsAnalysisCount));
        OnPropertyChanged(nameof(HasChosenTextAnalyses));
        OnPropertyChanged(nameof(ChosenTextsRemoveHeader));
        OnPropertyChanged(nameof(ChosenTextsRemovalPreview));
        OnPropertyChanged(nameof(AllCount));
        OnPropertyChanged(nameof(CheckedWordCountLabel));
        SelectAllWordsCommand.NotifyCanExecuteChanged();
        ClearSelectedWordsCommand.NotifyCanExecuteChanged();
        _readStateRefresh = RefreshReadStateAsync(readStateGeneration);
        SelectedToken = null;
        AddChangeCommand.NotifyCanExecuteChanged();
        RefreshPendingMarkers();
        OnPropertyChanged(nameof(AllCount));
        OnPropertyChanged(nameof(UnreadCount));
        OnPropertyChanged(nameof(SelectedReadStateCount));
        OnPropertyChanged(nameof(HasSelectedReadStateOccurrences));
        OnPropertyChanged(nameof(DiffersCount));
        OnPropertyChanged(nameof(NewCount));
        OnPropertyChanged(nameof(NoParseCount));
        OnPropertyChanged(nameof(MatchesCount));
        OnPropertyChanged(nameof(LimitCount));
        OnPropertyChanged(nameof(NotAssessedCount));
        OnPropertyChanged(nameof(NeedsALookCount));
        OnPropertyChanged(nameof(HasNotAssessed));
        OnPropertyChanged(nameof(CheckedWordCount));
        OnPropertyChanged(nameof(HasCheckedWords));
        NotifyScopeCommands();

        var reselected = Texts.FirstOrDefault(text => text.Title == previousTitle) ?? Texts.FirstOrDefault();
        if (ReferenceEquals(reselected, SelectedText)) RefreshLines();
        else SelectedText = reselected;
    }

    private void RefreshLines()
    {
        var matchingLines = new List<ResultsLineViewModel>();
        if (HasAssessment)
        {
            foreach (var line in SelectedText?.Lines ?? [])
            {
                var any = false;
                foreach (var token in line.Tokens.Where(token => token.IsWord))
                {
                    var matches = MatchesFilter(token);
                    token.IsDimmed = !matches;
                    any |= matches;
                }
                if (any) matchingLines.Add(line);
            }
        }
        for (var index = 0; index < matchingLines.Count; index++)
        {
            var line = matchingLines[index];
            if (index < VisibleLines.Count && ReferenceEquals(VisibleLines[index], line)) continue;
            var currentIndex = VisibleLines.IndexOf(line);
            if (currentIndex > index) VisibleLines.Move(currentIndex, index);
            else VisibleLines.Insert(index, line);
        }
        while (VisibleLines.Count > matchingLines.Count) VisibleLines.RemoveAt(VisibleLines.Count - 1);
        OnPropertyChanged(nameof(Message));
        OnPropertyChanged(nameof(HasMessage));
        OnPropertyChanged(nameof(HasTexts));
        OnPropertyChanged(nameof(HasAssessment));
        OnPropertyChanged(nameof(HasResults));
        OnPropertyChanged(nameof(NeedsTexts));
    }

    private bool MatchesFilter(ResultsTokenViewModel token) => Filter switch
    {
        ResultsInTextFilter.Unread => token.Marking.IsUnread,
        ResultsInTextFilter.Differs => token.Verdict == OccurrenceVerdict.Differs,
        ResultsInTextFilter.New => token.Verdict == OccurrenceVerdict.New,
        ResultsInTextFilter.NoParse => token.Verdict == OccurrenceVerdict.NoParse,
        ResultsInTextFilter.Limit => token.Verdict == OccurrenceVerdict.Limit,
        ResultsInTextFilter.NotAssessed => token.Verdict == OccurrenceVerdict.NotAssessed,
        ResultsInTextFilter.Matches => token.Verdict == OccurrenceVerdict.Matches,
        ResultsInTextFilter.NeedsALook => token.Marking.NeedsALook,
        _ => true,
    };

    private bool CanMarkTokenReadState(ResultsTokenViewModel? token) =>
        token is { IsWord: true, Occurrence: not null };

    private bool CanMarkSelectionReadState() => SelectedReadStateCount > 0;

    private bool CanSelectAllWords() => _allWords.Any(token => !token.IsSelectedForActions);

    private void SelectAllWords()
    {
        foreach (var token in _allWords) token.IsSelectedForActions = true;
    }

    private void ClearSelectedWords()
    {
        foreach (var token in _allWords) token.IsSelectedForActions = false;
    }

    private IEnumerable<ResultsTokenViewModel> SelectedTextWords() =>
        SelectedText?.Lines.SelectMany(line => line.Tokens).Where(token => token.IsWord) ?? [];

    private static HashSet<string> StoredAnalysisIds(IEnumerable<ResultsTokenViewModel> tokens) => tokens
        .SelectMany(token => token.Marking.FieldWorksAnalyses)
        .Select(analysis => analysis.StoredAnalysisId).OfType<string>()
        .Where(id => !string.IsNullOrWhiteSpace(id))
        .ToHashSet(StringComparer.Ordinal);

    private static string RemovalUsesPreview(IEnumerable<ResultsTokenViewModel> tokens)
    {
        var affected = tokens.Where(token => token.Marking.FieldWorksAnalyses.Any(analysis =>
                !string.IsNullOrWhiteSpace(analysis.StoredAnalysisId)))
            .GroupBy(token => token.Form, StringComparer.Ordinal)
            .Select(group => new
            {
                Word = group.Key,
                AnalysisCount = group.SelectMany(token => token.Marking.FieldWorksAnalyses)
                    .Select(analysis => analysis.StoredAnalysisId).OfType<string>()
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Distinct(StringComparer.Ordinal).Count(),
                OccurrenceCount = group.Select(token => token.Occurrence)
                    .OfType<OccurrenceAnchor>().Distinct().Count(),
            }).ToArray();
        return affected.Length == 0 ? "No stored analyses are used in this scope."
            : $"{affected.Sum(item => item.AnalysisCount)} stored analyses on {affected.Length} words are used here: " +
              string.Join(", ", affected.Select(item =>
                  $"{item.Word} ({item.AnalysisCount} analyses, {item.OccurrenceCount} occurrences)")) +
              ". Staging removes each analysis from its word form everywhere in the project.";
    }

    private ResultsTokenViewModel[] SelectedReadStateOccurrences() =>
        CheckedTokens;

    private void OnTokenPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ResultsTokenViewModel.IsSelectedForActions))
        {
            OnPropertyChanged(nameof(CheckedWordCount));
            OnPropertyChanged(nameof(CheckedWordCountLabel));
            OnPropertyChanged(nameof(HasCheckedWords));
            OnPropertyChanged(nameof(HasCheckedUncertainChanges));
            OnPropertyChanged(nameof(SelectedReadStateCount));
            OnPropertyChanged(nameof(HasSelectedReadStateOccurrences));
            MarkSelectionReadCommand.NotifyCanExecuteChanged();
            MarkSelectionUnreadCommand.NotifyCanExecuteChanged();
            SelectAllWordsCommand.NotifyCanExecuteChanged();
            ClearSelectedWordsCommand.NotifyCanExecuteChanged();
            NotifyScopeCommands();
        }
    }

    private async Task SetReadStateAsync(IReadOnlyList<ResultsTokenViewModel> selection, bool isRead)
    {
        if (string.IsNullOrWhiteSpace(_assess.ProjectPath)) return;
        var groups = selection.Where(token => token.IsWord && token.Occurrence is not null)
            .Distinct().GroupBy(token => token.Occurrence!.TextId).ToArray();
        var generation = _readStateGeneration;
        var version = ++_readStateWriteVersion;
        ReadStateRefusal = null;
        ReadStateNotice = null;
        var skipped = new List<OccurrenceAnchor>();
        foreach (var group in groups)
        {
            var occurrences = group.Select(token => token.Occurrence!).Distinct().ToArray();
            var outcome = await _commands.ReadWordStateAsync(new WordReadStateRequest(_assess.ProjectPath,
                group.Key, occurrences, isRead) { AssessmentIds = AssessmentIdsShownInWindow },
                CancellationToken.None).ConfigureAwait(true);
            if (generation != _readStateGeneration || version != _readStateWriteVersion) return;
            if (!outcome.Succeeded)
            {
                ReadStateRefusal = outcome.Refusal is { } refusal ? WindowRefusal.From(refusal) : null;
                return;
            }
            ApplyReadState(group.Key, outcome.Value!.ReadOccurrences);
            skipped.AddRange(outcome.Value.SkippedOccurrences);
        }
        ReadStateNotice = ReadStateSkipNotice(skipped.Count);
    }

    private async Task SetSelectedTextReadStateAsync(bool isRead)
    {
        if (SelectedText is not { } text || string.IsNullOrWhiteSpace(_assess.ProjectPath)) return;
        var generation = _readStateGeneration;
        var version = ++_readStateWriteVersion;
        ReadStateRefusal = null;
        ReadStateNotice = null;
        var outcome = await _commands.ReadWordStateAsync(new WordReadStateRequest(_assess.ProjectPath,
            text.TextId, IsRead: isRead) { AssessmentIds = AssessmentIdsShownInWindow },
            CancellationToken.None).ConfigureAwait(true);
        if (generation != _readStateGeneration || version != _readStateWriteVersion) return;
        if (!outcome.Succeeded)
        {
            ReadStateRefusal = outcome.Refusal is { } refusal ? WindowRefusal.From(refusal) : null;
            return;
        }
        ApplyReadState(text.TextId, outcome.Value!.ReadOccurrences);
        ReadStateNotice = ReadStateSkipNotice(outcome.Value.SkippedOccurrences.Count);
    }

    private static string? ReadStateSkipNotice(int count) => count switch
    {
        0 => null,
        1 => "1 word occurrence was not marked Read because its paragraph has not been parsed.",
        _ => $"{count} word occurrences were not marked Read because their paragraphs have not been parsed.",
    };

    private async Task RefreshReadStateAsync(long generation)
    {
        if (string.IsNullOrWhiteSpace(_assess.ProjectPath)) return;
        var textIds = _allWords.Where(token => token.Occurrence is not null)
            .Select(token => token.Occurrence!.TextId).Distinct().ToArray();
        foreach (var textId in textIds)
        {
            var outcome = await _commands.ReadWordStateAsync(new WordReadStateRequest(_assess.ProjectPath,
                textId) { AssessmentIds = AssessmentIdsShownInWindow }, CancellationToken.None).ConfigureAwait(true);
            if (generation != _readStateGeneration) return;
            if (outcome.Succeeded) ApplyReadState(textId, outcome.Value!.ReadOccurrences);
        }
    }

    private IReadOnlyList<string> AssessmentIdsShownInWindow => _assess.Result is { } result
        ? result.Measurements.Where(measurement => measurement.Kind == AssessmentKinds.ParseTime)
            .Select(measurement => measurement.AssessmentId).Concat(result.TimingOverrideAssessmentIds)
            .Distinct(StringComparer.Ordinal).ToArray()
        : [];

    private void ApplyReadState(Guid textId, IReadOnlyList<OccurrenceAnchor> readOccurrences)
    {
        var read = readOccurrences.ToHashSet();
        foreach (var token in _allWords.Where(token => token.Occurrence?.TextId == textId))
            token.SetReadState(token.Occurrence is { } occurrence && read.Contains(occurrence));
        OnPropertyChanged(nameof(UnreadCount));
        RefreshLines();
    }

    private bool CanAddSelectedChange(string? kind) => kind switch
    {
        ChangeKinds.IncorrectSpelling => SelectedToken is { IsWord: true },
        ChangeKinds.AddCandidate => SelectedToken is { IsWord: true, HasReadings: true },
        ChangeKinds.Approve or ChangeKinds.Reject or ChangeKinds.Candidate =>
            SelectedToken is { IsWord: true, SelectedReading: not null },
        _ => false,
    };

    private async Task AddSelectedChangeAsync(string? kind)
    {
        if (SelectedToken is not { } token || kind is null) return;
        var readings = kind == ChangeKinds.AddCandidate ? token.Readings :
            token.SelectedReading is { } selected ? [selected] : [];
        if (kind == ChangeKinds.IncorrectSpelling)
        {
            await _changes.AddFromTextAsync(kind, token).ConfigureAwait(true);
        }
        else
        {
            foreach (var reading in readings)
                await _changes.AddFromTextAsync(kind, token, reading).ConfigureAwait(true);
        }
        RefreshPendingMarkers();
    }

    private void OnChangesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshPendingMarkers();
        RecheckChangesCommand.NotifyCanExecuteChanged();
    }

    private bool CanRecheckChanges() => _changes.Items.Any(change => change.IsUncertain);

    private void OnSelectedTokenPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ResultsTokenViewModel.SelectedReading))
            AddChangeCommand.NotifyCanExecuteChanged();
    }

    private bool CanStagePrimaryMarkingAction() =>
        SelectedToken is { IsWord: true, Marking.PrimaryAction: not null };

    private static bool CanStagePrimaryMarkingActionForToken(ResultsTokenViewModel? token) =>
        token is { IsWord: true, Marking.PrimaryAction: not null };

    private bool CanStageMarkingChoice(AnalysisMarkingChoice? choice) =>
        SelectedToken is { IsWord: true } token && choice is not null && token.Marking.FixChoices.Contains(choice);

    private static bool CanStageMarkingChoice(ResultsTokenViewModel token, AnalysisMarkingChoice? choice) =>
        token.IsWord && choice is not null && token.Marking.FixChoices.Contains(choice);

    private async Task StagePrimaryMarkingActionAsync()
    {
        if (SelectedToken is not { } token || token.Marking.PrimaryAction is not { } action) return;
        await StageMarkingActionAsync(token, action).ConfigureAwait(true);
    }

    private async Task StagePrimaryMarkingActionForTokenAsync(ResultsTokenViewModel? token)
    {
        if (token?.Marking.PrimaryAction is not { } action) return;
        await StageMarkingActionAsync(token, action).ConfigureAwait(true);
    }

    private async Task StageMarkingChoiceAsync(AnalysisMarkingChoice? choice)
    {
        if (SelectedToken is not { } token || choice is null) return;
        await StageMarkingChoiceForTokenAsync(token, choice).ConfigureAwait(true);
    }

    private async Task StageMarkingChoiceForTokenAsync(ResultsTokenViewModel token, AnalysisMarkingChoice? choice)
    {
        if (!CanStageMarkingChoice(token, choice) || choice is null) return;
        await StageMarkingActionAsync(token, new AnalysisMarkingAction(choice.Kind, choice.Label,
            choice.StoredAnalysisId, choice.Reading, choice.ReadingIndex, choice.Now, choice.AfterApply,
            choice.ChangeKind)).ConfigureAwait(true);
    }

    private async Task StageMarkingActionAsync(ResultsTokenViewModel token, AnalysisMarkingAction action)
    {
        if (action.Kind == AnalysisMarkingActionKind.KeepFieldWorks)
        {
            await MarkReadAsync(token).ConfigureAwait(true);
            return;
        }
        var staged = false;
        if (action.Kind == AnalysisMarkingActionKind.RemoveAnalysis)
        {
            if (token.WordformId is { } wordformId && action.StoredAnalysisId is { } analysisId)
                staged = await _changes.RemoveAnalysisAsync(CanonicalId.FromGuid(wordformId).Value, token.Form,
                    analysisId).ConfigureAwait(true);
        }
        else if (action.Kind == AnalysisMarkingActionKind.AcceptNewSet)
        {
            if (_changes.AssessmentId is { } assessmentId && token.WordformId is { } wordformId)
                staged = await _changes.AcceptNewSetAsync(assessmentId, CanonicalId.FromGuid(wordformId).Value)
                    .ConfigureAwait(true);
        }
        else
        {
            staged = await _changes.AddFromMarkingAsync(action, token).ConfigureAwait(true);
        }
        if (!staged) return;
        await MarkReadAsync(token).ConfigureAwait(true);
        RefreshPendingMarkers();
    }

    private void RefreshPendingMarkers()
    {
        var pending = _changes.Items.GroupBy(item => item.Word, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        foreach (var token in Texts.SelectMany(text => text.Lines).SelectMany(line => line.Tokens).Where(token => token.IsWord))
        {
            pending.TryGetValue(token.Form, out var changes);
            var relevant = changes?.Where(change => change.Occurrence is null ||
                change.Occurrence == token.Occurrence).ToArray();
            token.PendingState = PendingChangeStates.FromChanges(relevant);
            token.IsPending = token.PendingState != PendingChangeState.None;
            token.SetStagedMarkings(relevant ?? []);
            token.IsUncertainChanged = false;
        }
        foreach (var line in Texts.SelectMany(text => text.Lines))
        {
            var words = line.Tokens.Where(token => token.IsWord).ToArray();
            foreach (var change in _changes.Items.Where(item => item.IsUncertain && item.AfterWords.Count > 0))
            {
                if (change.Occurrence is not { } occurrence ||
                    line.TextId != occurrence.TextId || line.ParagraphId != occurrence.ParagraphId ||
                    line.SegmentId != occurrence.SegmentId ||
                    !words.Any(token => token.Occurrence == occurrence)) continue;
                if (words.Length != change.AfterWords.Count) continue;
                var matches = words.Select((token, index) => (token, expected: change.AfterWords[index]))
                    .All(pair => pair.token.Occurrence?.Index == pair.expected.Index &&
                        pair.token.WordformId is { } wordformId &&
                        CanonicalId.FromGuid(wordformId).Value == pair.expected.WordformId &&
                        pair.token.Form.Normalize(System.Text.NormalizationForm.FormD) ==
                        pair.expected.Form.Normalize(System.Text.NormalizationForm.FormD));
                if (!matches) continue;
                foreach (var (token, expected) in words.Zip(change.AfterWords))
                    if (expected.IsChanged) token.IsUncertainChanged = true;
            }
        }
        OnPropertyChanged(nameof(NeedsALookCount));
        OnPropertyChanged(nameof(HasCheckedUncertainChanges));
        RefreshLines();
        OnPropertyChanged(nameof(Changes));
    }
}

/// <summary>A source position used to order and label a change from a Text occurrence.</summary>
/// <param name="TextOrder">The Text's position in the loaded source.</param>
/// <param name="LineOrder">The line's position within the Text.</param>
/// <param name="WordIndex">The zero-based word position within the line.</param>
/// <param name="Description">The location shown beside a Review change.</param>
public sealed record TextOccurrenceLocation(int TextOrder, int LineOrder, int WordIndex, string Description);

/// <summary>One chosen Text, line by line, with every word compared against the Assessment.</summary>
public sealed class ResultsTextViewModel
{
    public ResultsTextViewModel(TextLines text, IReadOnlyDictionary<string, AssessmentWordResult> results,
        IReadOnlyDictionary<string, TextWordRowViewModel>? projectWords = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(results);
        TextId = text.TextId;
        Title = text.Title;
        Lines = text.Lines.Select(line => new ResultsLineViewModel(
            text.Title, line, results, projectWords, text.TextId)).ToArray();
    }

    public string Title { get; }

    /// <summary>The FieldWorks Text identity used by Text-scoped marking commands.</summary>
    public Guid TextId { get; }
    public IReadOnlyList<ResultsLineViewModel> Lines { get; }
}

/// <summary>One line of a Text in the Results In text view, with the word card opened under it.</summary>
public sealed class ResultsLineViewModel : ObservableObject
{
    public ResultsLineViewModel(string title, TextLine line, IReadOnlyDictionary<string, AssessmentWordResult> results,
        IReadOnlyDictionary<string, TextWordRowViewModel>? projectWords = null, Guid textId = default)
    {
        ArgumentNullException.ThrowIfNull(line);
        Number = line.Number;
        TextId = textId;
        ParagraphId = line.ParagraphId;
        SegmentId = line.SegmentId;
        Tokens = line.Tokens.Select(token => new ResultsTokenViewModel(title, line.Number, token,
            token.Form is { } form && results.TryGetValue(form, out var result) ? result : null,
            token.Form is { } projectForm && projectWords is not null && projectWords.TryGetValue(projectForm, out var projectWord)
                ? projectWord : null,
            occurrence: token.Form is not null && textId != Guid.Empty && line.ParagraphId != Guid.Empty &&
                line.SegmentId != Guid.Empty && token.OccurrenceIndex >= 0
                    ? new OccurrenceAnchor(textId, line.ParagraphId, line.SegmentId, token.OccurrenceIndex)
                    : null,
            textId: textId)).ToArray();
        foreach (var token in Tokens) token.PropertyChanged += OnTokenPropertyChanged;
    }

    public int Number { get; }
    public Guid TextId { get; }
    public Guid ParagraphId { get; }
    public Guid SegmentId { get; }
    public IReadOnlyList<ResultsTokenViewModel> Tokens { get; }

    /// <summary>The word on this line whose card is open, which the line shows beneath its words.</summary>
    public ResultsTokenViewModel? OpenCard => Tokens.FirstOrDefault(token => token.IsCardOpen);

    /// <summary>Whether a word card is open under this line.</summary>
    public bool HasOpenCard => OpenCard is not null;

    private void OnTokenPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ResultsTokenViewModel.IsCardOpen)) return;
        OnPropertyChanged(nameof(OpenCard));
        OnPropertyChanged(nameof(HasOpenCard));
    }
}
