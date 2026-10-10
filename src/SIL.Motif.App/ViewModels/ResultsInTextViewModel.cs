using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.App.Services;
using SIL.Motif.App.Controls;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Commands.SelectionReading;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Projection.Usage;

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

    /// <summary>The parser stopped at a time or search limit before finishing this word.</summary>
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
    NamedInWarning,
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
    private readonly HashSet<ResultsTokenViewModel> _cardTimingLoads = [];
    private ResultsTokenViewModel? _standaloneSelectedWord;
    private ResultsLineViewModel? _standaloneSelectedLine;
    private long _readStateGeneration;
    private long _readStateWriteVersion;
    // Per Text, how many Mark read or Mark unread answers have been applied, so an older load can tell it is stale.
    private readonly Dictionary<Guid, long> _readStateWritesApplied = [];
    private long _cardTimingGeneration;

    [ObservableProperty]
    private WindowRefusal? _readStateRefusal;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasReadStateNotice))]
    private string? _readStateNotice;

    public ResultsInTextViewModel(
        TextWordsViewModel texts, AssessViewModel assess, Action<string> showWord, Action<string> tryWord,
        ChangesViewModel changes, ICommandClient commands, WorkspaceSelection selection)
    {
        ArgumentNullException.ThrowIfNull(texts);
        ArgumentNullException.ThrowIfNull(assess);
        ArgumentNullException.ThrowIfNull(showWord);
        ArgumentNullException.ThrowIfNull(tryWord);
        _selectionOwner = selection ?? throw new ArgumentNullException(nameof(selection));
        _texts = texts;
        _assess = assess;
        _showWord = showWord;
        _tryWord = tryWord;
        _changes = changes ?? throw new ArgumentNullException(nameof(changes));
        _commands = commands ?? throw new ArgumentNullException(nameof(commands));
        SetFilterCommand = new RelayCommand<ResultsInTextFilter>(filter => Filter = filter);
        SelectAllWordsCommand = new RelayCommand(SelectAllWords, CanSelectAllWords);
        ClearSelectedWordsCommand = new RelayCommand(ClearSelectedWords, () => HasCheckedWords);
        ChooseWordsCommand = new RelayCommand(() => IsChoosingWords = !IsChoosingWords);
        ShowInWordsCommand = new RelayCommand(() => { if (SelectedToken is { } token) _showWord(token.Form); });
        TryWordCommand = new RelayCommand(() => { if (SelectedToken is { } token) _tryWord(token.Form); });
        OpenPanGlossGuideCommand = new RelayCommand(() => OpenPanGlossGuide?.Invoke());
        OpenWarningsCommand = new RelayCommand(() => OpenWarnings?.Invoke());
        RecheckChangesCommand = new AsyncRelayCommand(() => _changes.RecheckAsync(), CanRecheckChanges);
        AddChangeCommand = new AsyncRelayCommand<string>(AddSelectedChangeAsync, CanAddSelectedChange);
        StagePrimaryMarkingActionCommand = new AsyncRelayCommand(StagePrimaryMarkingActionAsync,
            CanStagePrimaryMarkingAction);
        StagePrimaryMarkingActionForTokenCommand = new AsyncRelayCommand<ResultsTokenViewModel>(
            StagePrimaryMarkingActionForTokenAsync, CanStagePrimaryMarkingActionForToken);
        StageMarkingChoiceCommand = new AsyncRelayCommand<AnalysisMarkingChoice>(StageMarkingChoiceAsync,
            CanStageMarkingChoice);
        AddChangeForTargetCommand = new AsyncRelayCommand<WordChangeAction>(AddChangeForTargetAsync,
            action => action?.IsAvailable == true);
        TryWordForTargetCommand = new RelayCommand<string>(form =>
        {
            if (!string.IsNullOrWhiteSpace(form)) _tryWord(form);
        });
        StageMarkingChoiceForTargetCommand = new AsyncRelayCommand<WordMarkingChoice>(StageCapturedMarkingChoiceAsync,
            choice => choice?.IsAvailable == true);
        TokenActionCommands = [AddChangeForTargetCommand, TryWordForTargetCommand, StageMarkingChoiceForTargetCommand];
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
        InitializeSelectionReader();
        Rebuild();
    }

    public ObservableCollection<ResultsTextViewModel> Texts { get; } = [];

    public IRelayCommand<ResultsInTextFilter> SetFilterCommand { get; }
    public IRelayCommand SelectAllWordsCommand { get; }
    public IRelayCommand ClearSelectedWordsCommand { get; }

    /// <summary>Opens the selected word in the Words view.</summary>
    public IRelayCommand ShowInWordsCommand { get; }

    /// <summary>Opens the selected word in the Words view and traces it there.</summary>
    public IRelayCommand TryWordCommand { get; }

    /// <summary>Opens the PanGloss guide in the window's Help popup.</summary>
    public IRelayCommand OpenPanGlossGuideCommand { get; }

    /// <summary>Opens the stored grammar warnings page.</summary>
    public IRelayCommand OpenWarningsCommand { get; }

    /// <summary>Checks pending changes against the latest project state.</summary>
    public IAsyncRelayCommand RecheckChangesCommand { get; }

    /// <summary>The window callback that shows the PanGloss guide.</summary>
    public Action? OpenPanGlossGuide { get; set; }

    /// <summary>The window callback that opens the stored grammar warnings page.</summary>
    public Action? OpenWarnings { get; set; }

    internal Task WarningEvidenceRefresh => _selectionWork;

    public IAsyncRelayCommand<string> AddChangeCommand { get; }

    public IAsyncRelayCommand StagePrimaryMarkingActionCommand { get; }

    /// <summary>Stages the primary action for the word whose strip contains the button.</summary>
    public IAsyncRelayCommand<ResultsTokenViewModel> StagePrimaryMarkingActionForTokenCommand { get; }

    public IAsyncRelayCommand<AnalysisMarkingChoice> StageMarkingChoiceCommand { get; }

    /// <summary>The one pending-action command shared by every token and standalone card.</summary>
    public IAsyncRelayCommand<WordChangeAction> AddChangeForTargetCommand { get; }

    /// <summary>The one Try a Word command whose parameter carries the card's spelling.</summary>
    public IRelayCommand<string> TryWordForTargetCommand { get; }

    /// <summary>The one Fix command whose parameter retains its original word and evidence.</summary>
    public IAsyncRelayCommand<WordMarkingChoice> StageMarkingChoiceForTargetCommand { get; }

    internal IReadOnlyList<System.Windows.Input.ICommand> TokenActionCommands { get; }

    public IAsyncRelayCommand<ResultsTokenViewModel> MarkTokenReadCommand { get; }

    public IAsyncRelayCommand<ResultsTokenViewModel> MarkTokenUnreadCommand { get; }

    public IAsyncRelayCommand MarkTextReadCommand { get; }

    public IAsyncRelayCommand MarkTextUnreadCommand { get; }

    public IAsyncRelayCommand MarkSelectionReadCommand { get; }

    public IAsyncRelayCommand MarkSelectionUnreadCommand { get; }
    /// <summary>Checks pending changes that affect checked occurrences again.</summary>
    public IAsyncRelayCommand RecheckCheckedChangesCommand { get; }

    public ChangesViewModel Changes => _changes;

    internal Task ReadStateRefresh => Task.WhenAll(_selectionWork, _readStateWrites);

    [ObservableProperty]
    private ResultsTextViewModel? _selectedText;

    [ObservableProperty]
    private ResultsInTextFilter _filter = ResultsInTextFilter.All;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedToken))]
    private ResultsTokenViewModel? _selectedToken;

    public bool HasSelectedToken => SelectedToken is not null;

    public int CheckedWordCount => _selectAllOccurrences ? AllCount - _uncheckedOccurrences.Count : _checkedOccurrences.Count;
    public string CheckedWordCountLabel => $"Selected {CheckedWordCount:N0} of {AllCount:N0} words";

    public bool HasCheckedWords => CheckedWordCount > 0;

    /// <summary>Whether the reader asked to choose words, which shows a checkbox on every word strip.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowWordCheckboxes))]
    [NotifyPropertyChangedFor(nameof(ChooseWordsLabel))]
    private bool _isChoosingWords;

    /// <summary>The Select menu's choosing toggle, named for what pressing it does.</summary>
    public string ChooseWordsLabel => IsChoosingWords ? "Stop choosing words" : "Choose words";

    /// <summary>Turns word choosing on or off from the Select menu.</summary>
    public IRelayCommand ChooseWordsCommand { get; }

    /// <summary>Whether word strips show their checkboxes: while choosing, or while any word is checked.</summary>
    public bool ShowWordCheckboxes => IsChoosingWords || HasCheckedWords;

    /// <summary>The Select menu's button, which counts the checked words once there are any.</summary>
    public string SelectMenuLabel => HasCheckedWords ? $"{CheckedWordCount} selected ▾" : "Select ▾";

    public bool HasCheckedUncertainChanges =>
        NativeChangesFor(AnalysisOperationScope.CheckedWords).Any(change => change.IsUncertain);

    public int AllCount => _nativeSummary?.SourcePositions.Count ?? 0;
    public int UnreadCount => AllCount - _readOccurrences.Count;
    public int SelectedReadStateCount => CheckedWordCount;
    public bool HasSelectedReadStateOccurrences => SelectedReadStateCount > 0;
    public bool HasReadStateNotice => !string.IsNullOrWhiteSpace(ReadStateNotice);
    public int DiffersCount => Count(OccurrenceVerdict.Differs);
    public int NewCount => Count(OccurrenceVerdict.New);
    public int NoParseCount => Count(OccurrenceVerdict.NoParse);
    public int MatchesCount => Count(OccurrenceVerdict.Matches);
    public int LimitCount => Count(OccurrenceVerdict.Limit);
    public int NotAssessedCount => Count(OccurrenceVerdict.NotAssessed);
    public int NeedsALookCount => _nativeAnalyze?.NeedsALookCount ?? 0;
    /// <summary>How many chosen word occurrences exact warning identities name.</summary>
    public int NamedInWarningCount => _nativeAnalyze?.NamedInWarningCount ?? 0;
    /// <summary>Whether stored exact or candidate warning evidence names any chosen word.</summary>
    public bool HasWarningEvidence => _nativePresentation?.Warnings.Count > 0;
    public bool HasNotAssessed => NotAssessedCount > 0;
    public int SelectedTextAnalysisCount => NativeAnalysisIds(AnalysisOperationScope.SelectedText).Count;
    public int ChosenTextsAnalysisCount => NativeAnalysisIds(AnalysisOperationScope.ChosenTexts).Count;
    public int SelectedTextWordCount => SelectedText?.Summary?.OccurrenceCount ?? 0;
    public string ScopeCountSummary =>
        $"{SelectedTextWordCount:N0} words in this Text · {AllCount:N0} words in all chosen Texts";
    public bool HasSelectedTextAnalyses => SelectedTextAnalysisCount > 0;
    public bool HasChosenTextAnalyses => ChosenTextsAnalysisCount > 0;
    public string SelectedTextRemoveHeader => $"Preview removal from this Text ({SelectedTextAnalysisCount:N0})";
    public string ChosenTextsRemoveHeader => $"Preview removal in all chosen Texts ({ChosenTextsAnalysisCount:N0})";
    public string SelectedTextRemovalPreview => NativeRemovalPreview(AnalysisOperationScope.SelectedText);
    public string ChosenTextsRemovalPreview => NativeRemovalPreview(AnalysisOperationScope.ChosenTexts);

    /// <summary>Why nothing is shown, or <see langword="null"/> when there are lines to read.</summary>
    public string? Message => _selectionOwner.IsLoading == true ? "Reading selected Texts…" :
        HasStandaloneSelectedWord ? null : Texts.Count == 0
            ? _assess.Result is null ? null : _texts.HasAvailableTexts
                ? "Choose a text in Texts to read the results in place."
                : "This Baseline has no Texts."
        : SelectedText is null ? "Choose a text to read."
        : (SelectedText.Summary?.LineCount ?? 0) == 0
            ? "This text has no lines split into words yet. Open it once in FieldWorks' Interlinear Texts, then refresh the Baseline."
        : DisplayedLineCount == 0 ? "No word in this text matches the chosen filter."
        : null;

    public bool HasMessage => Message is not null;

    /// <summary>Whether there is a text to read, so the text picker and filters have something to act on.</summary>
    public bool HasTexts => Texts.Count > 0;

    /// <summary>Whether an Assessment is available to compare with the selected Texts.</summary>
    public bool HasAssessment => _selectionOwner.Reader?.Context.AssessmentRootId is not null;

    /// <summary>Whether the selected Text has results to display.</summary>
    public bool HasResults => HasAssessment && !HasMessage;

    /// <summary>Whether the reader has lines to show, which it does before the first parse too.</summary>
    public bool HasLines => !HasMessage && DisplayedLineCount > 0;

    /// <summary>The Assessment action for the empty Analyze texts state.</summary>
    public IAsyncRelayCommand ParseWordsCommand => _assess.RunCommand;

    /// <summary>Whether the only thing missing is a checked Text, which the Texts page can supply.</summary>
    public bool NeedsTexts => _assess.Result is not null && Texts.Count == 0 && _texts.HasAvailableTexts;

    public TextOccurrenceLocation? LocateOccurrence(OccurrenceAnchor occurrence) => NativeLocation(occurrence);

    /// <summary>Opens the Texts page for the empty state that asks for a checked Text.</summary>
    public Action? OpenTexts { get; set; }

    /// <summary>Shows a clicked word's comparison in the side panel.</summary>
    public void SelectToken(ResultsTokenViewModel token)
    {
        ArgumentNullException.ThrowIfNull(token);
        if (token.IsWord)
        {
            SelectedToken = PinToken(token);
            AddChangeCommand.NotifyCanExecuteChanged();
        }
    }

    public ResultsTokenViewModel GetCardToken(AssessmentWordResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return NativeCardToken(result);
    }

    public async Task LoadCardTimingAsync(ResultsTokenViewModel token)
    {
        ArgumentNullException.ThrowIfNull(token);
        if (token.HasTimingEvidence || !_cardTimingLoads.Add(token)) return;
        try
        {
            var generation = ++_cardTimingGeneration;
            await ReadTokenTimingAsync(token, generation, requireSelected: false).ConfigureAwait(true);
        }
        finally
        {
            _cardTimingLoads.Remove(token);
        }
    }

    public void CloseTokenCard() => SelectedToken = null;

    public async Task MoveTokenCardAsync(int direction)
    {
        if (SelectedToken?.Occurrence is { } anchor && AdjacentVisibleOccurrence(anchor, direction) is { } adjacent)
        {
            var token = await ReadOccurrenceAsync(adjacent).ConfigureAwait(true);
            if (token is not null) await OpenTokenCardAsync(token).ConfigureAwait(true);
        }
    }

    /// <summary>Selects a word card and records that the reader opened it.</summary>
    /// <param name="token">The word occurrence whose comparison card was opened.</param>
    /// <returns>A task that completes after its Read state is saved.</returns>
    public async Task OpenTokenCardAsync(ResultsTokenViewModel token)
    {
        ArgumentNullException.ThrowIfNull(token);
        SelectToken(token);
        var displayed = SelectedToken ?? token;
        var generation = ++_cardTimingGeneration;
        var read = MarkReadAsync(displayed);
        var timing = ReadTokenTimingAsync(displayed, generation);
        await Task.WhenAll(read, timing).ConfigureAwait(true);
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
    public Task MarkSelectionReadAsync() => SetReadStateAsync(NativeReadTargets(AnalysisOperationScope.CheckedWords), true);

    /// <summary>Marks the selected word occurrences Unread.</summary>
    /// <returns>A task that completes after the store command returns.</returns>
    public Task MarkSelectionUnreadAsync() => SetReadStateAsync(NativeReadTargets(AnalysisOperationScope.CheckedWords), false);

    /// <summary>Selects a word from any page, building its detail even when it has no chosen-text occurrence.</summary>
    public void SelectWord(string word, string? wordformId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(word);
        TrackSelectionWork(SelectNativeWordAsync(word, wordformId));
    }

    partial void OnSelectedTextChanged(ResultsTextViewModel? value)
    {
        if (value is not null) _preferredTextId = value.TextId;
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

    partial void OnSearchTextChanged(string value) => RefreshLines();

    internal void ClearProject()
    {
        _readStateGeneration++;
        _cardTimingGeneration++;
        ClearNativeSelection();
        _preferredTextId = null;
        SelectedText = null;
        SelectedToken = null;
    }

    partial void OnSelectedTokenChanging(ResultsTokenViewModel? oldValue, ResultsTokenViewModel? newValue)
    {
        _cardTimingGeneration++;
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
        ReleasePreviousPin(value);
        foreach (var line in _linePages?.RealizedLines ?? [])
            {
                foreach (var token in line.Tokens)
                    token.IsCardOpen = value?.Occurrence is { } selected && token.Occurrence == selected;
                line.SetPinnedCard(value?.Occurrence is { } anchor && line.TextId == anchor.TextId &&
                    line.SegmentId == anchor.SegmentId && line.ParagraphId == anchor.ParagraphId ? value : null);
            }
        RefreshLines();
    }

    private int Count(OccurrenceVerdict verdict) => NativeVerdictCount(verdict);

    private void OnSourceChanged(object? sender, PropertyChangedEventArgs e)
    {
        // A reload can reuse its response; the row publication marks a complete load without building it twice.
        if (ReferenceEquals(sender, _texts) && e.PropertyName == nameof(TextWordsViewModel.ProjectWords)) Rebuild();
        else if (ReferenceEquals(sender, _texts) && e.PropertyName == nameof(TextWordsViewModel.HasAvailableTexts)) RefreshLines();
        else if (ReferenceEquals(sender, _assess) && e.PropertyName == nameof(AssessViewModel.Result)) Rebuild();
        NotifyScopeCommands();
    }

    private void Rebuild() => RebuildNativeSelection();

    private void RefreshLines() => RefreshNativeLines();

    private bool HasStandaloneSelectedWord => SelectedToken is { IsWord: true, Occurrence: null };

    private async Task ReadTokenTimingAsync(ResultsTokenViewModel token, long generation, bool requireSelected = true)
    {
        var assessment = _assess.Result;
        var expected = token.CaptureActionTarget(null).ExpectedContext;
        var captured = expected?.SelectionEvidence;
        var root = expected is null ? assessment is null ? null :
            ProjectEvidence.ParseTimeMeasurementOf(assessment) : captured?.RootAssessmentId;
        var replacements = (expected is null ? assessment?.TimingOverrideAssessmentIds :
            captured?.ReplacementAssessmentIds)?.ToArray() ?? [];
        if ((expected is null ? assessment is null : root is null) ||
            string.IsNullOrWhiteSpace(_assess.ProjectPath))
        {
            token.SetTimingEvidence(null, responseAvailable: false);
            return;
        }

        var outcome = await _commands.TimingAsync(new TimingRequest(
            _assess.ProjectPath,
            root,
            By: "rule",
            ExplicitWords: [token.Form],
            OverrideAssessmentIds: replacements), CancellationToken.None)
            .ConfigureAwait(true);
        if (requireSelected && (generation != _cardTimingGeneration || !ReferenceEquals(SelectedToken, token))) return;
        token.SetTimingEvidence(outcome.Succeeded ? outcome.Value : null, outcome.Succeeded);
    }

    private bool CanMarkTokenReadState(ResultsTokenViewModel? token) =>
        token is { IsWord: true, Occurrence: not null };

    private bool CanMarkSelectionReadState() => SelectedReadStateCount > 0;

    private bool CanSelectAllWords() => CheckedWordCount < AllCount;

    private void SelectAllWords() => SelectNativeOccurrences(true);

    private void ClearSelectedWords() => SelectNativeOccurrences(false);

    private void OnTokenPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ResultsTokenViewModel.IsSelectedForActions))
        {
            if (sender is ResultsTokenViewModel token) RecordNativeCheck(token);
            OnPropertyChanged(nameof(CheckedWordCount));
            OnPropertyChanged(nameof(CheckedWordCountLabel));
            OnPropertyChanged(nameof(HasCheckedWords));
            OnPropertyChanged(nameof(HasCheckedUncertainChanges));
            OnPropertyChanged(nameof(ShowWordCheckboxes));
            OnPropertyChanged(nameof(SelectMenuLabel));
            OnPropertyChanged(nameof(SelectedReadStateCount));
            OnPropertyChanged(nameof(HasSelectedReadStateOccurrences));
            MarkSelectionReadCommand.NotifyCanExecuteChanged();
            MarkSelectionUnreadCommand.NotifyCanExecuteChanged();
            SelectAllWordsCommand.NotifyCanExecuteChanged();
            ClearSelectedWordsCommand.NotifyCanExecuteChanged();
            NotifyScopeCommands();
        }
    }

    private Task SetReadStateAsync(IReadOnlyList<ResultsTokenViewModel> selection, bool isRead) =>
        SetReadStateAsync(selection.Where(token => token.IsWord)
            .Select(token => token.CaptureActionTarget(_changes.AssessmentId)).ToArray(), isRead);

    private Task SetReadStateAsync(IReadOnlyList<WordActionTarget> selection, bool isRead) =>
        TrackReadStateWrite(WriteReadStateAsync(selection, isRead));

    private async Task WriteReadStateAsync(IReadOnlyList<WordActionTarget> selection, bool isRead)
    {
        if (string.IsNullOrWhiteSpace(_assess.ProjectPath)) return;
        var groups = selection.Where(token => token.Occurrence is not null)
            .Distinct().GroupBy(token => token.Occurrence!.TextId).ToArray();
        var expectedContext = selection.FirstOrDefault()?.ExpectedContext;
        if (selection.Any(target => !SameEvidence(target.ExpectedContext, expectedContext)))
        {
            ReadStateRefusal = WindowRefusal.From(new Refusal("texts.mixed-evidence", FailureReason.Refused,
                "These words belong to different saved Selections. Reopen the Selection before marking them."));
            return;
        }
        var assessmentIds = expectedContext?.SelectionEvidence is { } evidence
            ? (evidence.RootAssessmentId is { } root ? new[] { root } : Array.Empty<string>())
                .Concat(evidence.ReplacementAssessmentIds)
                .Distinct(StringComparer.Ordinal).ToArray()
            : AssessmentIdsShownInWindow;
        var projectPath = _assess.ProjectPath;
        var generation = _readStateGeneration;
        using var usageAction = _commands.BeginUsageAction("word read-state",
            UsageArgumentShape.Text("fwDataPath"), UsageArgumentShape.Flag("isRead"),
            UsageArgumentShape.List("occurrences", selection.Count), UsageArgumentShape.List("texts", groups.Length));
        var version = ++_readStateWriteVersion;
        ReadStateRefusal = null;
        ReadStateNotice = null;
        var skipped = new List<OccurrenceAnchor>();
        foreach (var group in groups)
        {
            var occurrences = group.Select(token => token.Occurrence!).Distinct().ToArray();
            var outcome = await _commands.ReadWordStateAsync(new WordReadStateRequest(projectPath,
                group.Key, occurrences, isRead) { AssessmentIds = assessmentIds, ExpectedContext = expectedContext },
                CancellationToken.None).ConfigureAwait(true);
            if (generation != _readStateGeneration || version != _readStateWriteVersion) return;
            if (!outcome.Succeeded)
            {
                ReadStateRefusal = outcome.Refusal is { } refusal ? WindowRefusal.From(refusal) : null;
                return;
            }
            ApplyWrittenReadState(group.Key, outcome.Value!.ReadOccurrences);
            skipped.AddRange(outcome.Value.SkippedOccurrences);
        }
        ReadStateNotice = ReadStateSkipNotice(skipped.Count);
    }

    private static bool SameEvidence(ExpectedContext? left, ExpectedContext? right) => left == right ||
        left is not null && right is not null &&
        System.Text.Json.JsonSerializer.Serialize(left, SIL.Motif.Contract.MotifJson.CreateOptions()) ==
        System.Text.Json.JsonSerializer.Serialize(right, SIL.Motif.Contract.MotifJson.CreateOptions());

    private Task SetSelectedTextReadStateAsync(bool isRead) => TrackReadStateWrite(SetNativeTextReadStateAsync(isRead));

    private static string? ReadStateSkipNotice(int count) => count switch
    {
        0 => null,
        1 => "1 word occurrence was not marked Read because its paragraph has not been parsed.",
        _ => $"{count} word occurrences were not marked Read because their paragraphs have not been parsed.",
    };

    private IReadOnlyList<string> AssessmentIdsShownInWindow => _selectionOwner.Reader?.Context.Evidence is { } evidence
        ? (evidence.RootAssessmentId is { } root ? new[] { root } : Array.Empty<string>())
            .Concat(evidence.ReplacementAssessmentIds).Distinct(StringComparer.Ordinal).ToArray() : [];

    private void ApplyWrittenReadState(Guid textId, IReadOnlyList<OccurrenceAnchor> readOccurrences)
    {
        _readStateWritesApplied[textId] = _readStateWritesApplied.GetValueOrDefault(textId) + 1;
        ApplyReadState(textId, readOccurrences);
    }

    private void ApplyReadState(Guid textId, IReadOnlyList<OccurrenceAnchor> readOccurrences) =>
        ApplyNativeReadState(textId, readOccurrences);

    private bool CanAddSelectedChange(string? kind) => SelectedToken is { } token && CanAddChangeForToken(token, kind);

    private static bool CanAddChangeForToken(ResultsTokenViewModel token, string? kind) =>
        token.HasUniqueActionTarget && (kind switch
    {
        ChangeKinds.IncorrectSpelling => token.IsWord,
        ChangeKinds.AddCandidate => token is { IsWord: true, HasReadings: true },
        ChangeKinds.Approve or ChangeKinds.Reject or ChangeKinds.Candidate =>
            token is { IsWord: true, SelectedReading: not null },
        _ => false,
    });

    private Task AddSelectedChangeAsync(string? kind) => SelectedToken is { } token
        ? AddChangeForTokenAsync(token, kind) : Task.CompletedTask;

    private Task AddChangeForTokenAsync(ResultsTokenViewModel token, string? kind)
    {
        if (kind is null || !CanAddChangeForToken(token, kind)) return Task.CompletedTask;
        var readings = kind == ChangeKinds.AddCandidate ? token.Readings :
            token.SelectedReading is { } selected ? [selected] : [];
        return AddChangeForTargetAsync(new WordChangeAction(token.CaptureActionTarget(_changes.AssessmentId), kind,
            readings.Select(reading => new WordActionReading(reading.Analysis, reading.Text, reading.Index)).ToArray(), true));
    }

    private async Task AddChangeForTargetAsync(WordChangeAction? action)
    {
        if (action?.IsAvailable != true) return;
        using var historyAction = _changes.BeginStagingAction();
        using var usageAction = _commands.BeginUsageAction("put-pending-change",
            UsageArgumentShape.Text("fwDataPath"), UsageArgumentShape.Text("kind"));
        if (action.Kind == ChangeKinds.IncorrectSpelling)
            await _changes.AddFromTextAsync(action.Kind, action.Target).ConfigureAwait(true);
        else
            foreach (var reading in action.Readings)
                if (!await _changes.AddFromTextAsync(action.Kind, action.Target, reading).ConfigureAwait(true)) break;
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

    private bool CanStagePrimaryMarkingAction() => CanStagePrimaryMarkingActionForToken(SelectedToken);

    private static bool CanStagePrimaryMarkingActionForToken(ResultsTokenViewModel? token) =>
        token is { IsWord: true, Marking.PrimaryAction: { } action } &&
        token.AllowsMarkingAction(action.Kind, action.StoredAnalysisId, action.Reading);

    private bool CanStageMarkingChoice(AnalysisMarkingChoice? choice) =>
        SelectedToken is { IsWord: true } token && CanStageMarkingChoice(token, choice);

    internal static bool CanStageMarkingChoice(ResultsTokenViewModel token, AnalysisMarkingChoice? choice) =>
        token.IsWord && choice is not null && token.AllowsMarkingAction(choice.Kind, choice.StoredAnalysisId, choice.Reading) &&
        (token.Marking.FixChoices.Contains(choice) ||
            choice.StoredAnalysisId is { } analysisId && token.Marking.FieldWorksAnalyses
                .FirstOrDefault(analysis => analysis.StoredAnalysisId == analysisId) is { } analysis &&
            AnalysisMarkingState.OpinionChoicesFor(analysis).Contains(choice));

    private async Task StagePrimaryMarkingActionAsync()
    {
        if (SelectedToken is not { } token || token.Marking.PrimaryAction is not { } action ||
            !CanStagePrimaryMarkingActionForToken(token)) return;
        await StageMarkingActionAsync(token, action).ConfigureAwait(true);
    }

    private async Task StagePrimaryMarkingActionForTokenAsync(ResultsTokenViewModel? token)
    {
        if (token?.Marking.PrimaryAction is not { } action || !CanStagePrimaryMarkingActionForToken(token)) return;
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

    private Task StageCapturedMarkingChoiceAsync(WordMarkingChoice? captured) => captured?.IsAvailable != true
        ? Task.CompletedTask : StageMarkingActionAsync(captured.Target, new AnalysisMarkingAction(
            captured.Choice.Kind, captured.Choice.Label, captured.Choice.StoredAnalysisId, captured.Choice.Reading,
            captured.Choice.ReadingIndex, captured.Choice.Now, captured.Choice.AfterApply, captured.Choice.ChangeKind));

    private Task StageMarkingActionAsync(ResultsTokenViewModel token, AnalysisMarkingAction action) =>
        StageMarkingActionAsync(token.CaptureActionTarget(_changes.AssessmentId), action);

    private async Task StageMarkingActionAsync(WordActionTarget target, AnalysisMarkingAction action)
    {
        var command = action.Kind switch
        {
            AnalysisMarkingActionKind.KeepFieldWorks => "word read-state",
            AnalysisMarkingActionKind.RemoveAnalysis => "remove-analysis",
            AnalysisMarkingActionKind.AcceptNewSet => "accept-new-set",
            _ => "put-pending-change",
        };
        using var usageAction = _commands.BeginUsageAction(command,
            UsageArgumentShape.Text("fwDataPath"), UsageArgumentShape.Object("action"));
        if (action.Kind == AnalysisMarkingActionKind.KeepFieldWorks)
        {
            await SetReadStateAsync([target], true).ConfigureAwait(true);
            return;
        }
        var staged = false;
        if (action.Kind == AnalysisMarkingActionKind.RemoveAnalysis)
        {
            if (target.WordformId is { } wordformId && action.StoredAnalysisId is { } analysisId)
                staged = target.ExpectedContext is null ?
                    await _changes.RemoveAnalysisAsync(CanonicalId.FromGuid(wordformId).Value, target.Form, analysisId)
                        .ConfigureAwait(true) :
                    await _changes.RemoveCapturedAnalysisAsync(target, analysisId).ConfigureAwait(true);
        }
        else if (action.Kind == AnalysisMarkingActionKind.AcceptNewSet)
        {
            if (target.AssessmentId is { } assessmentId && target.WordformId is { } wordformId)
                if (target.ExpectedContext is null)
                    staged = await _changes.AcceptNewSetAsync(assessmentId, CanonicalId.FromGuid(wordformId).Value)
                        .ConfigureAwait(true);
                else
                {
                    staged = true;
                    var groupId = CanonicalId.Mint().Value;
                    foreach (var reading in target.ParserOnlyReadings)
                        if (!await _changes.AddFromTextAsync(ChangeKinds.AddCandidate, target, reading, groupId).ConfigureAwait(true))
                        { staged = false; break; }
                }
        }
        else
        {
            staged = await _changes.AddFromMarkingAsync(action, target).ConfigureAwait(true);
        }
        if (!staged) return;
        await SetReadStateAsync([target], true).ConfigureAwait(true);
        RefreshPendingMarkers();
    }

    private void RefreshPendingMarkers() => RefreshNativePending();

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
    internal ResultsTextViewModel(SelectionTextSummary summary)
    {
        Summary = summary;
        TextId = summary.TextId;
        Title = summary.Title;
        TitleWritingSystem = summary.TitleWritingSystem;
    }

    internal SelectionTextSummary Summary { get; }

    public string Title { get; }
    public string? TitleWritingSystem { get; }

    /// <summary>The FieldWorks Text identity used by Text-scoped marking commands.</summary>
    public Guid TextId { get; }
}

/// <summary>One line of a Text in the Results In text view, with the word card opened under it.</summary>
public sealed class ResultsLineViewModel : ObservableObject, IDisposable
{
    private IDisposable? _modelRegistration;
    private string? _capturedSentence;

    internal ResultsLineViewModel(Guid textId, SelectionLineHeader header, object tokenSource,
        IDisposable registration)
    {
        TextId = textId;
        Number = header.Number;
        ParagraphId = header.ParagraphId;
        SegmentId = header.SegmentId;
        SentenceStyle = "Normal";
        Tokens = [];
        TokenSource = tokenSource;
        _modelRegistration = registration;
    }

    internal void ShowPage(string title, TextLine line, TextLineFragment fragment,
        IReadOnlyDictionary<string, AssessmentWordResult> results, SelectionRead<TextLineSlice> read,
        Action<ResultsTokenViewModel>? prepare)
    {
        ReleasePage();
        SentenceStyle = fragment.SentenceStyle;
        SentenceWritingSystem = fragment.SentenceWritingSystem;
        _capturedSentence = fragment.Sentence;
        var location = $"{title}, line {Number}";
        var expected = read.Context.ExpectedWriteContext();
        var tokens = new List<ResultsTokenViewModel>();
        try
        {
            foreach (var token in line.Tokens)
            {
                var assessment = token.Form is { } form ? results.GetValueOrDefault(form) : null;
                var model = new ResultsTokenViewModel(title, Number, token, assessment, location: location,
                    occurrence: token.Form is not null ? new OccurrenceAnchor(TextId, ParagraphId, SegmentId,
                        token.OccurrenceIndex) : null, textId: TextId, lease: read.Lease, expectedContext: expected,
                    producingAssessmentId: token.Form is { } assessed &&
                        read.Value.Assessments.TryGetValue(assessed, out var facts) ? facts.AssessmentId : null);
                tokens.Add(model);
                prepare?.Invoke(model);
                model.PropertyChanged += OnTokenPropertyChanged;
            }
            Tokens = tokens.ToArray();
            tokens.Clear();
        }
        finally
        {
            foreach (var token in tokens)
            {
                token.PropertyChanged -= OnTokenPropertyChanged;
                token.Dispose();
            }
        }
        OnPropertyChanged(nameof(Tokens));
        OnPropertyChanged(nameof(Sentence));
        OnPropertyChanged(nameof(SentenceStyle));
        OnPropertyChanged(nameof(SentenceWritingSystem));
        OnPropertyChanged(nameof(NeedsALookSummary));
        OnPropertyChanged(nameof(OpenCard));
        OnPropertyChanged(nameof(HasOpenCard));
    }

    internal void ReleasePage()
    {
        foreach (var token in Tokens)
        {
            token.PropertyChanged -= OnTokenPropertyChanged;
            token.Dispose();
        }
        Tokens = [];
        OnPropertyChanged(nameof(Tokens));
        OnPropertyChanged(nameof(DisplayTokens));
        OnPropertyChanged(nameof(OpenCard));
        OnPropertyChanged(nameof(HasOpenCard));
    }
    private ResultsLineViewModel(ResultsTokenViewModel selectedWord)
    {
        ArgumentNullException.ThrowIfNull(selectedWord);
        Number = 0;
        TextId = Guid.Empty;
        ParagraphId = Guid.Empty;
        SegmentId = Guid.Empty;
        SentenceStyle = "Normal";
        Tokens = [selectedWord];
        IsStandalone = true;
        selectedWord.PropertyChanged += OnTokenPropertyChanged;
    }

    internal ResultsLineViewModel(string title, TextLine line,
        IReadOnlyDictionary<string, AssessmentWordResult> results, Guid textId, SelectionReadLease lease,
        string capturedSentence, SelectionReadContext readContext,
        IReadOnlyDictionary<string, SelectionAssessmentFacts> assessmentFacts)
    {
        ArgumentNullException.ThrowIfNull(line);
        _capturedSentence = capturedSentence;
        var location = $"{title}, line {line.Number}";
        ArgumentNullException.ThrowIfNull(lease);
        var expectedContext = readContext.ExpectedWriteContext();
        Number = line.Number;
        TextId = textId;
        ParagraphId = line.ParagraphId;
        SegmentId = line.SegmentId;
        SentenceStyle = line.SentenceStyle;
        SentenceWritingSystem = line.SentenceWritingSystem;
        var tokens = new List<ResultsTokenViewModel>();
        try
        {
            foreach (var token in line.Tokens)
                tokens.Add(new ResultsTokenViewModel(title, line.Number, token,
                    token.Form is { } form && results.TryGetValue(form, out var result) ? result : null,
                    location: location,
                    occurrence: token.Form is not null && textId != Guid.Empty && line.ParagraphId != Guid.Empty &&
                        line.SegmentId != Guid.Empty && token.OccurrenceIndex >= 0
                            ? new OccurrenceAnchor(textId, line.ParagraphId, line.SegmentId, token.OccurrenceIndex)
                            : null,
                    textId: textId, lease: lease,
                    expectedContext: expectedContext, producingAssessmentId: token.Form is { } assessedForm &&
                        assessmentFacts?.TryGetValue(assessedForm, out var facts) == true ? facts.AssessmentId : null));
            _modelRegistration = lease.RegisterModel(SelectionModelKind.Line);
            Tokens = tokens.ToArray();
        }
        catch
        {
            foreach (var token in tokens) token.Dispose();
            throw;
        }

        foreach (var token in Tokens) token.PropertyChanged += OnTokenPropertyChanged;
    }

    /// <summary>Detaches token notifications and releases all models owned by this displayed line.</summary>
    public void Dispose()
    {
        ReleasePage();
        Interlocked.Exchange(ref _modelRegistration, null)?.Dispose();
    }

    public int Number { get; }
    /// <summary>Whether the row has a source Text line number to show.</summary>
    public bool ShowsLineNumber => !IsStandalone;
    /// <summary>Whether the row represents a word opened without a chosen-Text occurrence.</summary>
    public bool IsStandalone { get; }
    public Guid TextId { get; }
    public Guid ParagraphId { get; }
    public Guid SegmentId { get; }
    public string SentenceStyle { get; private set; }
    public string? SentenceWritingSystem { get; private set; }
    public IReadOnlyList<ResultsTokenViewModel> Tokens { get; private set; }
    public object? TokenSource { get; }
    public object DisplayTokens => TokenSource ?? (IsStandalone
        ? Tokens.Select(token => new WeakReference<object>(token)).ToArray() : (object)Tokens);

    /// <summary>Places an opened word's card in the reader without inventing a Text occurrence.</summary>
    internal static ResultsLineViewModel ForSelectedWord(ResultsTokenViewModel selectedWord,
        IDisposable? registration = null) =>
        new(selectedWord ?? throw new ArgumentNullException(nameof(selectedWord))) { _modelRegistration = registration };

    private ResultsTokenViewModel? _pinnedCard;

    internal void SetPinnedCard(ResultsTokenViewModel? token)
    {
        if (ReferenceEquals(_pinnedCard, token)) return;
        _pinnedCard = token;
        OnPropertyChanged(nameof(OpenCard));
        OnPropertyChanged(nameof(HasOpenCard));
    }

    /// <summary>The word on this line whose card is open, which the line shows beneath its words.</summary>
    public ResultsTokenViewModel? OpenCard => _pinnedCard is { IsCardOpen: true } pin ? pin :
        Tokens.FirstOrDefault(token => token.IsCardOpen);

    /// <summary>Whether a word card is open under this line.</summary>
    public bool HasOpenCard => OpenCard is not null;

    /// <summary>The line as it reads, words and punctuation, above its word strips.</summary>
    public string Sentence => _capturedSentence ?? string.Concat(Tokens.Select((token, index) =>
        index > 0 && token.IsWord ? " " + token.Text : token.Text));

    /// <summary>How many of the line's words have an action waiting, or nothing when none do.</summary>
    public string NeedsALookSummary => Tokens.Count(token => token.IsWord && token.Marking.NeedsALook) switch
    {
        0 => string.Empty,
        var count => $"· {count} need{(count == 1 ? "s" : string.Empty)} a look",
    };

    private void OnTokenPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ResultsTokenViewModel.Marking))
        {
            OnPropertyChanged(nameof(NeedsALookSummary));
            return;
        }
        if (e.PropertyName != nameof(ResultsTokenViewModel.IsCardOpen)) return;
        OnPropertyChanged(nameof(OpenCard));
        OnPropertyChanged(nameof(HasOpenCard));
    }
}
