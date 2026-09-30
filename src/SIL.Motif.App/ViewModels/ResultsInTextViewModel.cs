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
        AcceptNewSetForSelectedTextCommand = new AsyncRelayCommand(AcceptNewSetForSelectedTextAsync,
            CanAcceptNewSetForSelectedText);
        AcceptNewSetForSelectionCommand = new AsyncRelayCommand(AcceptNewSetForSelectionAsync,
            CanAcceptNewSetForSelection);
        RemoveAnalysesForSelectedTextCommand = new AsyncRelayCommand(RemoveAnalysesForSelectedTextAsync,
            () => SelectedText is not null);
        RemoveAnalysesForSelectionCommand = new AsyncRelayCommand(RemoveAnalysesForSelectionAsync,
            CanRemoveAnalysesForSelection);
        AddParserReadingsForSelectedTextCommand = new AsyncRelayCommand(AddParserReadingsForSelectedTextAsync,
            CanAddParserReadingsForSelectedText);
        AddParserReadingsForSelectionCommand = new AsyncRelayCommand(AddParserReadingsForSelectionAsync,
            CanAddParserReadingsForSelection);
        MarkSpellingsIncorrectForSelectedTextCommand = new AsyncRelayCommand(
            MarkSpellingsIncorrectForSelectedTextAsync, () => SelectedText is not null);
        MarkSpellingsIncorrectForSelectionCommand = new AsyncRelayCommand(
            MarkSpellingsIncorrectForSelectionAsync, () => _allWords.Count > 0);
        UndoChangesForSelectedTextCommand = new AsyncRelayCommand(UndoChangesForSelectedTextAsync,
            CanUndoChangesForSelectedText);
        UndoChangesForSelectionCommand = new AsyncRelayCommand(UndoChangesForSelectionAsync,
            CanUndoChangesForSelection);
        AddCheckedParserReadingsCommand = new AsyncRelayCommand(AddCheckedParserReadingsAsync,
            () => CanAddParserReadings(CheckedTokens));
        AcceptCheckedNewSetCommand = new AsyncRelayCommand(AcceptCheckedNewSetAsync, CanAcceptCheckedNewSet);
        RemoveCheckedAnalysesCommand = new AsyncRelayCommand(RemoveCheckedAnalysesAsync,
            CanRemoveCheckedAnalyses);
        MarkCheckedSpellingsIncorrectCommand = new AsyncRelayCommand(
            MarkCheckedSpellingsIncorrectAsync, () => CheckedTokens.Length > 0);
        UndoCheckedChangesCommand = new AsyncRelayCommand(UndoCheckedChangesAsync, CanUndoCheckedChanges);
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
    /// <summary>Accepts the missing readings from the current complete Assessment for the selected Text.</summary>
    public IAsyncRelayCommand AcceptNewSetForSelectedTextCommand { get; }

    /// <summary>Accepts the missing readings from the current complete Assessment for its Selection.</summary>
    public IAsyncRelayCommand AcceptNewSetForSelectionCommand { get; }

    /// <summary>Removes every stored analysis found in the selected Text.</summary>
    public IAsyncRelayCommand RemoveAnalysesForSelectedTextCommand { get; }

    /// <summary>Removes the distinct stored analyses found across the checked Texts.</summary>
    public IAsyncRelayCommand RemoveAnalysesForSelectionCommand { get; }

    /// <summary>Adds each missing parser reading as Unknown for the selected Text.</summary>
    public IAsyncRelayCommand AddParserReadingsForSelectedTextCommand { get; }

    /// <summary>Adds each missing parser reading as Unknown for the current Selection.</summary>
    public IAsyncRelayCommand AddParserReadingsForSelectionCommand { get; }

    /// <summary>Marks each distinct spelling Incorrect in the selected Text.</summary>
    public IAsyncRelayCommand MarkSpellingsIncorrectForSelectedTextCommand { get; }

    /// <summary>Marks each distinct spelling Incorrect in the current Selection.</summary>
    public IAsyncRelayCommand MarkSpellingsIncorrectForSelectionCommand { get; }

    /// <summary>Removes the pending changes for words in the selected Text.</summary>
    public IAsyncRelayCommand UndoChangesForSelectedTextCommand { get; }

    /// <summary>Removes the pending changes for words in the current Selection.</summary>
    public IAsyncRelayCommand UndoChangesForSelectionCommand { get; }

    /// <summary>Adds each missing reading as Unknown for the checked occurrences.</summary>
    public IAsyncRelayCommand AddCheckedParserReadingsCommand { get; }

    /// <summary>Accepts missing readings for checked words from the complete Assessment.</summary>
    public IAsyncRelayCommand AcceptCheckedNewSetCommand { get; }

    /// <summary>Removes stored analyses for the checked occurrences.</summary>
    public IAsyncRelayCommand RemoveCheckedAnalysesCommand { get; }

    /// <summary>Marks each checked spelling Incorrect once.</summary>
    public IAsyncRelayCommand MarkCheckedSpellingsIncorrectCommand { get; }

    /// <summary>Removes pending changes for the checked occurrences.</summary>
    public IAsyncRelayCommand UndoCheckedChangesCommand { get; }

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

    public bool HasCheckedWords => CheckedWordCount > 0;

    public bool HasCheckedUncertainChanges => CheckedTokens.Any(token =>
        _changes.Items.Any(change => change.Word == token.Form && change.IsUncertain));

    private ResultsTokenViewModel[] CheckedTokens => _allWords.Where(token => token.IsSelectedForActions).ToArray();

    public int AllCount => _allWords.Count;
    public int UnreadCount => _allWords.Count(token => token.Marking.IsUnread);
    public int SelectedReadStateCount => _allWords.Count(token => token.IsSelectedForReadState);
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
        if (newValue is not null) newValue.PropertyChanged += OnSelectedTokenPropertyChanged;
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
        NotifyCheckedCommands();

        var reselected = Texts.FirstOrDefault(text => text.Title == previousTitle) ?? Texts.FirstOrDefault();
        if (ReferenceEquals(reselected, SelectedText)) RefreshLines();
        else SelectedText = reselected;
    }

    private void RefreshLines()
    {
        VisibleLines.Clear();
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
                if (any) VisibleLines.Add(line);
            }
        }
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

    private ResultsTokenViewModel[] SelectedReadStateOccurrences() =>
        _allWords.Where(token => token.IsSelectedForReadState).ToArray();

    private void OnTokenPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ResultsTokenViewModel.IsSelectedForReadState))
        {
            OnPropertyChanged(nameof(SelectedReadStateCount));
            OnPropertyChanged(nameof(HasSelectedReadStateOccurrences));
            MarkSelectionReadCommand.NotifyCanExecuteChanged();
            MarkSelectionUnreadCommand.NotifyCanExecuteChanged();
        }

        if (e.PropertyName == nameof(ResultsTokenViewModel.IsSelectedForActions))
        {
            OnPropertyChanged(nameof(CheckedWordCount));
            OnPropertyChanged(nameof(HasCheckedWords));
            OnPropertyChanged(nameof(HasCheckedUncertainChanges));
            NotifyCheckedCommands();
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

    private bool CanAcceptNewSetForSelectedText() => _changes.AssessmentId is not null &&
        HasCompleteAssessmentForSelectedText();

    private bool CanAcceptNewSetForSelection() => _changes.AssessmentId is not null &&
        _assess.Result?.Words is { Count: > 0 } words && words.All(IsCompleteParse);

    private bool CanRemoveAnalysesForSelection() => _allWords.SelectMany(token => token.Marking.FieldWorksAnalyses)
        .Any(analysis => !string.IsNullOrWhiteSpace(analysis.StoredAnalysisId));

    private bool HasCompleteAssessmentForSelectedText()
    {
        if (SelectedText is null || _assess.Result is not { } assessment) return false;
        var results = assessment.Words.GroupBy(word => word.Word, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var forms = SelectedText.Lines.SelectMany(line => line.Tokens).Where(token => token.IsWord)
            .Select(token => token.Form).Distinct(StringComparer.Ordinal).ToArray();
        return forms.Length > 0 && forms.All(form => results.TryGetValue(form, out var result) && IsCompleteParse(result));
    }

    private static bool IsCompleteParse(AssessmentWordResult result) => !result.IsIncomplete &&
        result.Outcome is not ("skipped" or "capped" or "timed-out") &&
        result.Morphology is { Capped: false, TimedOut: false, InvalidShape: false, Unavailable.Count: 0 };

    private Task AcceptNewSetForSelectedTextAsync() => SelectedText is { } text &&
        _changes.AssessmentId is { } assessmentId
            ? _changes.AcceptNewSetAsync(assessmentId, textId: text.TextId) : Task.CompletedTask;

    private Task AcceptNewSetForSelectionAsync() => _changes.AssessmentId is { } assessmentId
        ? _changes.AcceptNewSetAsync(assessmentId, selection: true) : Task.CompletedTask;

    private Task RemoveAnalysesForSelectedTextAsync() => SelectedText is { } text
        ? _changes.RemoveAnalysesInTextAsync(text.TextId) : Task.CompletedTask;

    private Task RemoveAnalysesForSelectionAsync()
    {
        var ids = _allWords.SelectMany(token => token.Marking.FieldWorksAnalyses)
            .Select(analysis => analysis.StoredAnalysisId).Distinct(StringComparer.Ordinal).ToArray();
        return ids.Length == 0 ? Task.CompletedTask : _changes.RemoveAnalysesAsync(ids);
    }

    private static bool CanAddParserReadings(IEnumerable<ResultsTokenViewModel> tokens) =>
        tokens.Any(HasParserOnlyReading);

    private bool CanAcceptCheckedNewSet()
    {
        if (_changes.AssessmentId is null || _assess.Result?.Words is not { Count: > 0 } words) return false;
        var results = words.GroupBy(word => word.Word, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var forms = CheckedTokens.Select(token => token.Form).Distinct(StringComparer.Ordinal).ToArray();
        return forms.Length > 0 && forms.All(form => results.TryGetValue(form, out var result) && IsCompleteParse(result));
    }

    private bool CanRemoveCheckedAnalyses() => CheckedTokens.SelectMany(token => token.Marking.FieldWorksAnalyses)
        .Any(analysis => !string.IsNullOrWhiteSpace(analysis.StoredAnalysisId));

    private bool CanUndoCheckedChanges() => HasChangesFor(CheckedTokens);

    private bool CanRecheckCheckedChanges() => HasCheckedUncertainChanges;

    private Task AddCheckedParserReadingsAsync() => AddParserReadingsAsUnknownAsync(CheckedTokens);

    private async Task AcceptCheckedNewSetAsync()
    {
        if (_changes.AssessmentId is not { } assessmentId) return;
        foreach (var token in DistinctWords(CheckedTokens))
            if (token.WordformId is { } wordformId)
                await _changes.AcceptNewSetAsync(assessmentId, CanonicalId.FromGuid(wordformId).Value)
                    .ConfigureAwait(true);
    }

    private Task RemoveCheckedAnalysesAsync()
    {
        var ids = CheckedTokens.SelectMany(token => token.Marking.FieldWorksAnalyses)
            .Select(analysis => analysis.StoredAnalysisId).Distinct(StringComparer.Ordinal).ToArray();
        return ids.Length == 0 ? Task.CompletedTask : _changes.RemoveAnalysesAsync(ids);
    }

    private Task MarkCheckedSpellingsIncorrectAsync() => MarkSpellingsIncorrectAsync(CheckedTokens);

    private Task UndoCheckedChangesAsync() => UndoChangesAsync(CheckedTokens);

    private bool CanAddParserReadingsForSelectedText() => SelectedText is not null &&
        SelectedText.Lines.SelectMany(line => line.Tokens).Any(HasParserOnlyReading);

    private bool CanAddParserReadingsForSelection() => _allWords.Any(HasParserOnlyReading);

    private static bool HasParserOnlyReading(ResultsTokenViewModel token) =>
        token.Marking.PanGlossReadings.Any(reading => reading.IsParserOnly);

    private Task AddParserReadingsForSelectedTextAsync() => SelectedText is { } text
        ? AddParserReadingsAsUnknownAsync(text.Lines.SelectMany(line => line.Tokens))
        : Task.CompletedTask;

    private Task AddParserReadingsForSelectionAsync() => AddParserReadingsAsUnknownAsync(_allWords);

    private async Task AddParserReadingsAsUnknownAsync(IEnumerable<ResultsTokenViewModel> tokens)
    {
        foreach (var token in DistinctWords(tokens))
        {
            foreach (var (reading, index) in token.Marking.PanGlossReadings
                         .Select((reading, index) => (reading, index))
                         .Where(item => item.reading.IsParserOnly)
                         .DistinctBy(item => ProjectAnalysisKey.For(item.reading.Analysis)))
            {
                await _changes.AddFromMarkingAsync(new AnalysisMarkingAction(
                    AnalysisMarkingActionKind.Add, "Add as Unknown", null, reading.Analysis, index,
                    "Not in FieldWorks", "Unknown", ChangeKinds.AddCandidate), token).ConfigureAwait(true);
            }
        }
    }

    private Task MarkSpellingsIncorrectForSelectedTextAsync() => SelectedText is { } text
        ? MarkSpellingsIncorrectAsync(text.Lines.SelectMany(line => line.Tokens))
        : Task.CompletedTask;

    private Task MarkSpellingsIncorrectForSelectionAsync() => MarkSpellingsIncorrectAsync(_allWords);

    private async Task MarkSpellingsIncorrectAsync(IEnumerable<ResultsTokenViewModel> tokens)
    {
        foreach (var token in DistinctWords(tokens))
            await _changes.AddFromTextAsync(ChangeKinds.IncorrectSpelling, token).ConfigureAwait(true);
    }

    private bool CanUndoChangesForSelectedText() => SelectedText is { } text &&
        HasChangesFor(text.Lines.SelectMany(line => line.Tokens));

    private bool CanUndoChangesForSelection() => HasChangesFor(_allWords);

    private bool HasChangesFor(IEnumerable<ResultsTokenViewModel> tokens)
    {
        var forms = tokens.Where(token => token.IsWord).Select(token => token.Form)
            .ToHashSet(StringComparer.Ordinal);
        return _changes.Items.Any(change => forms.Contains(change.Word));
    }

    private Task UndoChangesForSelectedTextAsync() => SelectedText is { } text
        ? UndoChangesAsync(text.Lines.SelectMany(line => line.Tokens))
        : Task.CompletedTask;

    private Task UndoChangesForSelectionAsync() => UndoChangesAsync(_allWords);

    private async Task UndoChangesAsync(IEnumerable<ResultsTokenViewModel> tokens)
    {
        var forms = tokens.Where(token => token.IsWord).Select(token => token.Form)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var change in _changes.Items.Where(change => forms.Contains(change.Word))
                     .DistinctBy(change => change.ChangeId).ToArray())
            await _changes.RemoveCommand.ExecuteAsync(change).ConfigureAwait(true);
    }

    private static IEnumerable<ResultsTokenViewModel> DistinctWords(IEnumerable<ResultsTokenViewModel> tokens) =>
        tokens.Where(token => token.IsWord).DistinctBy(token => token.WordformId is { } id
            ? $"id:{id:N}" : $"word:{token.Form}");

    private void NotifyScopeCommands()
    {
        AcceptNewSetForSelectedTextCommand.NotifyCanExecuteChanged();
        AcceptNewSetForSelectionCommand.NotifyCanExecuteChanged();
        RemoveAnalysesForSelectedTextCommand.NotifyCanExecuteChanged();
        RemoveAnalysesForSelectionCommand.NotifyCanExecuteChanged();
        AddParserReadingsForSelectedTextCommand.NotifyCanExecuteChanged();
        AddParserReadingsForSelectionCommand.NotifyCanExecuteChanged();
        MarkSpellingsIncorrectForSelectedTextCommand.NotifyCanExecuteChanged();
        MarkSpellingsIncorrectForSelectionCommand.NotifyCanExecuteChanged();
        UndoChangesForSelectedTextCommand.NotifyCanExecuteChanged();
        UndoChangesForSelectionCommand.NotifyCanExecuteChanged();
        NotifyCheckedCommands();
    }

    private void NotifyCheckedCommands()
    {
        AddCheckedParserReadingsCommand.NotifyCanExecuteChanged();
        AcceptCheckedNewSetCommand.NotifyCanExecuteChanged();
        RemoveCheckedAnalysesCommand.NotifyCanExecuteChanged();
        MarkCheckedSpellingsIncorrectCommand.NotifyCanExecuteChanged();
        UndoCheckedChangesCommand.NotifyCanExecuteChanged();
        RecheckCheckedChangesCommand.NotifyCanExecuteChanged();
    }

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
        await StageMarkingActionAsync(token, new AnalysisMarkingAction(choice.Kind, choice.Label,
            choice.StoredAnalysisId, choice.Reading, choice.ReadingIndex, choice.Now, choice.AfterApply,
            choice.ChangeKind))
            .ConfigureAwait(true);
    }

    private async Task StageMarkingActionAsync(ResultsTokenViewModel token, AnalysisMarkingAction action)
    {
        if (action.Kind == AnalysisMarkingActionKind.KeepFieldWorks)
        {
            await MarkReadAsync(token).ConfigureAwait(true);
            return;
        }
        if (action.Kind == AnalysisMarkingActionKind.RemoveAnalysis)
        {
            if (token.WordformId is { } wordformId && action.StoredAnalysisId is { } analysisId)
                await _changes.RemoveAnalysisAsync(CanonicalId.FromGuid(wordformId).Value, token.Form, analysisId)
                    .ConfigureAwait(true);
        }
        else if (action.Kind == AnalysisMarkingActionKind.AcceptNewSet)
        {
            if (_changes.AssessmentId is { } assessmentId && token.WordformId is { } wordformId)
                await _changes.AcceptNewSetAsync(assessmentId, CanonicalId.FromGuid(wordformId).Value)
                    .ConfigureAwait(true);
        }
        else
        {
            var staged = await _changes.AddFromMarkingAsync(action, token).ConfigureAwait(true);
            if (!staged) return;
        }
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
        TextId = text.TextId;
        Lines = text.Lines.Select(line => new ResultsLineViewModel(
            text.Title, line, results, projectWords, text.TextId)).ToArray();
    }

    public string Title { get; }

    /// <summary>The FieldWorks Text identity used by Text-scoped marking commands.</summary>
    public Guid TextId { get; }
    public IReadOnlyList<ResultsLineViewModel> Lines { get; }
}

/// <summary>One line of a Text in the Results In text view.</summary>
public sealed class ResultsLineViewModel
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
                    : null)).ToArray();
    }

    public int Number { get; }
    public Guid TextId { get; }
    public Guid ParagraphId { get; }
    public Guid SegmentId { get; }
    public IReadOnlyList<ResultsTokenViewModel> Tokens { get; }
}

/// <summary>
/// One token of a line: punctuation, or a word with the analysis stored at this occurrence and the parser's
/// verdict on it — whether the parser produced that analysis, something else, or nothing.
/// </summary>
public sealed partial class ResultsTokenViewModel : ObservableObject
{
    private readonly TextToken _source;
    private readonly AssessmentWordResult? _assessment;
    private bool _isUnread = true;

    [ObservableProperty]
    private bool _isSelectedForReadState;

    public ResultsTokenViewModel(string title, int line, TextToken token, AssessmentWordResult? result,
        TextWordRowViewModel? projectWord = null, string? location = null, OccurrenceAnchor? occurrence = null)
    {
        ArgumentNullException.ThrowIfNull(token);
        _source = token;
        _assessment = result;
        Text = token.Text;
        Form = token.Form ?? token.Text;
        IsWord = token.Form is not null;
        Location = location ?? $"{title}, line {line}";
        Occurrence = occurrence;
        WordformId = token.WordformId;
        OccurrenceIndex = token.OccurrenceIndex;
        WordLink = token.WordLink is { } link ? new Uri(link) : null;
        Stored = token.Analysis?.Morphs.Select(morph => new ParserReadingMorphViewModel(morph)).ToArray() ?? [];
        ProjectSummary = projectWord?.ProjectSummary ?? "No project entry is loaded for this word.";
        ProjectStatusLabel = projectWord?.StatusLabel ?? ReadingGradeLabels.NotPresent;
        ProjectStatusVerdict = projectWord?.Verdict ?? global::SIL.Motif.App.ViewModels.Verdict.New;
        ProjectApprovedAnalyses = projectWord?.ApprovedAnalyses ?? [];

        var storedKey = token.Analysis?.Key;
        var analyses = result?.Morphology?.Analyses ?? [];
        var keys = analyses.Select(ProjectAnalysisKey.For).ToArray();
        var resolved = result?.Readings;
        var grades = result?.ReadingGrades;
        Readings = keys.Select((key, index) => new ResultsReadingViewModel(
                ReadingText(resolved is not null && index < resolved.Count ? resolved[index] : null),
                grades is not null && index < grades.Count ? grades[index] : null,
                storedKey is not null && key == storedKey, analyses[index], index,
                resolved is not null && index < resolved.Count ? resolved[index] : null))
            .ToArray();

        Verdict = !IsWord || result is null || result.Outcome == "skipped" ? OccurrenceVerdict.NotAssessed
            : storedKey is not null && keys.Contains(storedKey) ? OccurrenceVerdict.Matches
            : result.IsIncomplete ? OccurrenceVerdict.Limit
            : storedKey is not null ? OccurrenceVerdict.Differs
            : keys.Length > 0 ? OccurrenceVerdict.New
            : OccurrenceVerdict.NoParse;

        var first = Readings.FirstOrDefault()?.Text;
        var others = Readings.Count - 1;
        ParserLine = Verdict switch
        {
            OccurrenceVerdict.Matches => others > 0 ? $"✓ parser agrees, with {others} other reading{Plural(others)}" : "✓ parser agrees",
            OccurrenceVerdict.Differs when first is null => "✗ parser: no parse",
            OccurrenceVerdict.Differs => $"≠ parser: {first}" + (others > 0 ? $" (+{others})" : string.Empty),
            OccurrenceVerdict.New => $"parser: {first}" + (others > 0 ? $" (+{others})" : string.Empty),
            OccurrenceVerdict.NoParse => "no parse",
            OccurrenceVerdict.Limit => "parser stopped at a limit",
            _ when result?.Outcome == "skipped" => "skipped: a character the grammar does not define",
            _ => "not in this Assessment",
        };
        VerdictLabel = Verdict switch
        {
            OccurrenceVerdict.Matches => "Parser agrees with what is stored here",
            OccurrenceVerdict.Differs when Readings.Count == 0 => "An analysis is stored here, and the parser found no parse",
            OccurrenceVerdict.Differs => "Parser differs from what is stored here",
            OccurrenceVerdict.New => "Nothing stored here; the parser proposes an analysis",
            OccurrenceVerdict.NoParse => "Nothing stored here, and the parser found no parse",
            OccurrenceVerdict.Limit => "The parser stopped at a time or step limit",
            _ when result?.Outcome == "skipped" => "The parser skipped this word: it has a character the grammar's character table does not define",
            _ => "This word was not part of the Assessment",
        };
        Marking = AnalysisMarkingState.Create(token, result, _isUnread);
        FieldWorksAnalyses = Marking.FieldWorksAnalyses
            .Select(analysis => new FieldWorksAnalysisDisplayViewModel(analysis)).ToArray();
    }

    public string Text { get; }

    /// <summary>The accessible name of the checkbox that includes this occurrence in bulk actions.</summary>
    public string SelectionAutomationName => $"Select {Form} for actions";

    /// <summary>The word's form as the Assessment names it, for finding it in the Words view.</summary>
    public string Form { get; }

    public bool IsWord { get; }

    public AnalysisMarkingState Marking { get; private set; }
    /// <summary>Every stored analysis with its opinion mark and interlinear morphemes.</summary>
    public IReadOnlyList<FieldWorksAnalysisDisplayViewModel> FieldWorksAnalyses { get; }

    /// <summary>The opinion mark used beside the word, or the dashed mark when nothing is stored.</summary>
    public OpinionMarkKind PrimaryOpinionMarkKind => FieldWorksAnalyses.Count == 0
        ? OpinionMarkKind.None : FieldWorksAnalyses[0].OpinionMarkKind;

    /// <summary>Whether FieldWorks stores at least one analysis for the word.</summary>
    public bool HasFieldWorksAnalyses => FieldWorksAnalyses.Count > 0;

    /// <summary>Whether PanGloss agrees with every stored reading.</summary>
    public bool IsPanGlossSame => Marking.PanGlossClass == AnalysisMarkingClass.Same;

    /// <summary>Whether PanGloss conflicts with a stored reading or opinion.</summary>
    public bool IsPanGlossDifferent => Marking.PanGlossClass is AnalysisMarkingClass.Conflict or
        AnalysisMarkingClass.Different;

    /// <summary>Whether PanGloss has readings beyond the approved FieldWorks readings.</summary>
    public bool IsPanGlossExtra => Marking.PanGlossClass == AnalysisMarkingClass.Extra;

    /// <summary>Whether PanGloss completed without finding a reading.</summary>
    public bool IsPanGlossNone => Marking.PanGlossClass == AnalysisMarkingClass.None;

    /// <summary>Whether PanGloss stopped before completing its search.</summary>
    public bool IsPanGlossCapped => Marking.PanGlossClass == AnalysisMarkingClass.Capped;

    /// <summary>The short PanGloss result shown in the word strip and hover summary.</summary>
    public string PanGlossSummary => Marking.PanGlossClass switch
    {
        AnalysisMarkingClass.Same => "Agrees with FieldWorks",
        AnalysisMarkingClass.Conflict => "Conflicts with a FieldWorks opinion",
        AnalysisMarkingClass.Different => "Different from FieldWorks",
        AnalysisMarkingClass.Extra => "Has additional readings",
        AnalysisMarkingClass.None => "No parse",
        AnalysisMarkingClass.Capped => "Search stopped at a limit",
        _ => "Not assessed",
    };

    /// <summary>Whether the word has a primary action available in its strip.</summary>
    public bool HasPrimaryAction => Marking.PrimaryAction is not null;

    /// <summary>The read-only hover summary for the stored opinion and current PanGloss result.</summary>
    public string HoverSummary => $"{Form} · {FieldWorksSummary} · PanGloss: {PanGlossSummary}";

    private string FieldWorksSummary => FieldWorksAnalyses.Count == 0
        ? "No analysis in FieldWorks"
        : string.Join(", ", FieldWorksAnalyses.Select(analysis => analysis.Opinion));
    public string Location { get; }
    public Uri? WordLink { get; }
    public OccurrenceAnchor? Occurrence { get; }
    public Guid? WordformId { get; }
    public int OccurrenceIndex { get; }
    public bool HasWordLink => WordLink is not null;
    public bool HasNoWordLink => IsWord && WordLink is null;
    public string WordLinkName => $"Open {Text} in FieldWorks";

    /// <summary>The morphs of the analysis stored at this occurrence, each linked to its entry.</summary>
    public IReadOnlyList<ParserReadingMorphViewModel> Stored { get; }

    public string ProjectSummary { get; }

    public string ProjectStatusLabel { get; }

    public Verdict ProjectStatusVerdict { get; }

    public IReadOnlyList<ProjectAnalysisViewModel> ProjectApprovedAnalyses { get; }

    public bool HasProjectApprovedAnalyses => ProjectApprovedAnalyses.Count > 0;

    public bool HasStored => Stored.Count > 0;
    public bool HasNothingStored => IsWord && Stored.Count == 0;

    /// <summary>Every reading the parser produced for this word, graded, with the one stored here marked.</summary>
    public IReadOnlyList<ResultsReadingViewModel> Readings { get; }

    [ObservableProperty]
    private ResultsReadingViewModel? _selectedReading;

    [ObservableProperty]
    private bool _isPending;

    [ObservableProperty]
    private bool _isUncertainChanged;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PendingChangeStatus))]
    private PendingChangeState _pendingState;

    public string? PendingChangeStatus => PendingChangeStates.Label(PendingState);

    public bool HasReadings => Readings.Count > 0;

    public OccurrenceVerdict Verdict { get; }

    /// <summary>The short line under the word: the parser's answer against what is stored.</summary>
    public string ParserLine { get; }

    /// <summary>The verdict as a sentence, for the side panel.</summary>
    public string VerdictLabel { get; }

    public bool IsMatch => Verdict == OccurrenceVerdict.Matches;
    public bool IsDiffers => Verdict == OccurrenceVerdict.Differs;
    public bool IsNew => Verdict == OccurrenceVerdict.New;
    public bool IsNoParse => Verdict is OccurrenceVerdict.NoParse or OccurrenceVerdict.Limit;

    internal void SetReadState(bool isRead)
    {
        _isUnread = !isRead;
        Marking = Marking with { IsUnread = _isUnread };
        OnPropertyChanged(nameof(Marking));
    }

    internal void SetStagedMarkings(IReadOnlyList<ChangeViewModel> changes)
    {
        StagedChanges = changes.Select(change => new StagedMarkingDisplayViewModel(change)).ToArray();
        Marking = AnalysisMarkingState.Create(_source, _assessment, _isUnread);
        Marking = Marking.WithStagedTransitions(changes.Select(change => change.StagedTransition with
        {
            StoredAnalysisId = change.StoredAnalysisId,
            ReadingIndex = change.ReadingIndex,
            FitStatus = change.Fit?.Status,
        }).ToArray());
        OnPropertyChanged(nameof(StagedChanges));
        OnPropertyChanged(nameof(Marking));
    }

    /// <summary>Pending changes that affect this word and can be undone from its strip.</summary>
    public IReadOnlyList<StagedMarkingDisplayViewModel> StagedChanges { get; private set; } = [];

    /// <summary>The shared meaning behind <see cref="Verdict"/>, used for its colour and glyph.</summary>
    public Verdict Meaning => Verdict switch
    {
        OccurrenceVerdict.Matches => ViewModels.Verdict.Agrees,
        OccurrenceVerdict.Differs => ViewModels.Verdict.Differs,
        OccurrenceVerdict.New => ViewModels.Verdict.New,
        OccurrenceVerdict.NoParse => ViewModels.Verdict.NoResult,
        _ => ViewModels.Verdict.Limit,
    };

    /// <summary>Whether the parser also produced a reading the project has rejected for this word.</summary>
    public bool HasDisapprovedReading => Readings.Any(reading => reading.IsDisapproved);

    /// <summary>What the disapproved marker says when a reader stops on it.</summary>
    public string DisapprovedTip => $"The parser also produced a reading the project has rejected for {Text}.";

    /// <summary>Whether the active filter passes over this word, so it recedes rather than disappears.</summary>
    [ObservableProperty]
    private bool _isDimmed;

    [ObservableProperty]
    private bool _isSelectedForActions;

    // Forms already carry their own hyphens ("a-", "-a"), so they join as written; glosses join with one.
    private static string ReadingText(ParserReading? reading) => reading is null ? "?"
        : JoinForms(reading.Morphs.Select(morph => morph.Form)) + " ‘" +
          string.Join("-", reading.Morphs.Select(morph => morph.Gloss.Length == 0 ? "?" : morph.Gloss)) + "’";

    private static string JoinForms(IEnumerable<string> forms)
    {
        var parts = forms.ToArray();
        return parts.Any(form => form.StartsWith('-') || form.EndsWith('-'))
            ? string.Concat(parts).Replace("--", "-", StringComparison.Ordinal)
            : string.Join("-", parts);
    }

    private static string Plural(int count) => count == 1 ? string.Empty : "s";
}

/// <summary>One parser reading of a word as the side panel lists it.</summary>
public sealed class ResultsReadingViewModel
{
    public ResultsReadingViewModel(string text, string? grade, bool isStoredHere,
        ParseAnalysis? analysis = null, int index = -1, ParserReading? reading = null)
    {
        Text = text;
        IsStoredHere = isStoredHere;
        GradeLabel = ReadingGradeLabels.Of(grade);
        IsDisapproved = grade == ReadingGrade.Disapproved;
        Analysis = analysis;
        Index = index;
        Morphs = reading?.Morphs.Select(morph => new ParserReadingMorphViewModel(morph)).ToArray() ?? [];
    }

    public string Text { get; }

    public string GradeLabel { get; }
    public bool HasGrade => GradeLabel.Length > 0;
    public bool IsDisapproved { get; }

    public ParseAnalysis? Analysis { get; }

    public int Index { get; }

    /// <summary>Whether this is the analysis stored at the occurrence being looked at.</summary>
    public bool IsStoredHere { get; }

    public IReadOnlyList<ParserReadingMorphViewModel> Morphs { get; }

    public bool HasMorphs => Morphs.Count > 0;
}
