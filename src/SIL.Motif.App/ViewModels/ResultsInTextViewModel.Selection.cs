using System.ComponentModel;
using SIL.Motif.Commands.SelectionReading;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.ViewModels;

public sealed partial class ResultsInTextViewModel
{
    private readonly WorkspaceSelection _selectionOwner;
    private SelectionSummary? _nativeSummary;
    private SelectionAnalyzeFacts? _nativeAnalyze;
    private long _analyzeVersion;
    private Dictionary<TextWordKey, SelectionWordSummary> _nativeWords = [];
    private Dictionary<OccurrenceAnchor, SelectionSourcePosition> _nativePositions = [];
    private SelectionPresentationState? _nativePresentation;
    private SelectionRead<SelectionPresentationState>? _presentationRead;
    private SelectionLinePages? _linePages;
    private IDisposable? _lineObservation;
    private Task _selectionWork = Task.CompletedTask;
    private Task _readStateWrites = Task.CompletedTask;
    private long _selectionVersion;
    private bool _readerVisible = true;
    private Guid? _preferredTextId;
    private bool _selectAllOccurrences;
    private bool _applyingChecks;
    private readonly HashSet<OccurrenceAnchor> _checkedOccurrences = [];
    private readonly HashSet<OccurrenceAnchor> _uncheckedOccurrences = [];
    private readonly HashSet<OccurrenceAnchor> _readOccurrences = [];
    private readonly HashSet<OccurrenceAnchor> _uncertainHighlights = [];
    private IReadOnlyList<SelectionLineHeader> _visibleHeaders = [];
    private IReadOnlyList<SelectionLinePosition> _linePositions = [];
    private ResultsTokenViewModel? _pinnedToken;
    private IDisposable? _pinnedCard;

    public object DisplayedLines => _standaloneSelectedLine is { } standalone
        ? new object[] { new WeakReference<object>(standalone) }.Concat(_linePositions).ToArray() : _linePositions;
    internal int DisplayedLineCount => _visibleHeaders.Count + (_standaloneSelectedLine is null ? 0 : 1);
    internal IReadOnlyList<SelectionLineHeader> VisibleHeaders => _visibleHeaders;
    internal SelectionLinePages? LinePages => _linePages;
    internal Task SelectionRefresh => _selectionWork;
    internal event Action<OccurrenceAnchor>? OccurrenceRequested;
    internal event Func<OccurrenceAnchor, Task<ResultsTokenViewModel?>>? OccurrenceRealizationRequested;
    internal event Action<WeakReference<object>>? NativeModelCreated;

    private void InitializeSelectionReader()
    {
        _selectionOwner.Replacing += ClearNativeSelection;
        _selectionOwner.PropertyChanged += OnSelectionPublished;
    }

    private void OnSelectionPublished(object? sender, PropertyChangedEventArgs change)
    {
        if (change.PropertyName == nameof(WorkspaceSelection.Summary)) RebuildNativeSelection();
        else if (change.PropertyName == nameof(WorkspaceSelection.IsLoading))
        {
            OnPropertyChanged(nameof(Message));
            OnPropertyChanged(nameof(HasMessage));
            OnPropertyChanged(nameof(HasLines));
        }
    }

    private void TrackSelectionWork(Task work) => _selectionWork = _selectionWork.IsCompleted ? work :
        Task.WhenAll(_selectionWork, work);

    // Kept apart from selection work so focus moves never wait on a store write.
    private Task TrackReadStateWrite(Task write)
    {
        _readStateWrites = _readStateWrites.IsCompleted ? write : Task.WhenAll(_readStateWrites, write);
        return write;
    }

    private void ReleaseLinePages()
    {
        if (_linePages is not { } pages) return;
        _linePages = null;
        _lineObservation?.Dispose();
        _lineObservation = null;
        pages.ReleasingTokens -= ReleaseRealizedTokenHandlers;
        pages.ReleasingLineTokens -= ReleaseLineTokenHandlers;
        ReleaseRealizedTokenHandlers(pages);
        TrackSelectionWork(pages.StopAsync());
    }

    private void ReleaseLineTokenHandlers(ResultsLineViewModel line)
    {
        foreach (var token in line.Tokens) token.PropertyChanged -= OnTokenPropertyChanged;
    }

    private void ReleaseRealizedTokenHandlers() => ReleaseRealizedTokenHandlers(_linePages);

    private void ReleaseRealizedTokenHandlers(SelectionLinePages? pages)
    {
        foreach (var token in pages?.RealizedLines.SelectMany(line => line.Tokens) ?? [])
            token.PropertyChanged -= OnTokenPropertyChanged;
    }

    private void ClearNativeSelection()
    {
        ++_selectionVersion;
        ++_readStateGeneration;
        SelectedToken = null;
        ReleasePreviousPin(null);
        ReleaseLinePages();
        _presentationRead?.Dispose();
        _presentationRead = null;
        _nativePresentation = null;
        _nativeSummary = null;
        _nativeAnalyze = null;
        ++_analyzeVersion;
        _nativeWords.Clear();
        _nativePositions.Clear();
        _checkedOccurrences.Clear();
        _uncheckedOccurrences.Clear();
        _readOccurrences.Clear();
        _uncertainHighlights.Clear();
        _readStateWritesApplied.Clear();
        UpdateAssessmentReadState();
        _selectAllOccurrences = false;
        _visibleHeaders = [];
        _linePositions = [];
        Texts.Clear();
        SelectedText = null;
        NotifyNativeCounts();
        RefreshNativeLines();
    }

    private void RebuildNativeSelection()
    {
        var summary = _selectionOwner.Summary;
        if (ReferenceEquals(summary, _nativeSummary)) return;
        var previous = SelectedText?.TextId ?? _preferredTextId;
        ClearNativeSelection();
        if (summary is null) return;
        _nativeSummary = summary;
        _nativeAnalyze = summary.Analyze;
        _nativeWords = summary.Words.ToDictionary(word => word.Key);
        _nativePositions = summary.SourcePositions.ToDictionary(position => position.Location.Anchor);
        RefreshNativeUncertainty();
        foreach (var text in summary.Texts) Texts.Add(new ResultsTextViewModel(text));
        SelectedText = Texts.FirstOrDefault(text => text.TextId == previous) ?? Texts.FirstOrDefault();
        NotifyNativeCounts();
        RefreshNativeLines();
        TrackSelectionWork(RefreshNativePresentationAsync(_selectionVersion));
    }

    private async Task RefreshNativePresentationAsync(long version)
    {
        if (_selectionOwner.Reader is not { } reader) return;
        var writes = _readStateWritesApplied.ToDictionary();
        var outcome = await reader.ReadPresentationAsync().ConfigureAwait(true);
        var acquired = outcome.Value;
        try
        {
            if (version != _selectionVersion || !ReferenceEquals(reader, _selectionOwner.Reader)) return;
            if (!outcome.Succeeded && outcome.Refusal?.Code == "selection-reader.presentation-changed" &&
                _readStateWritesApplied.Any(pair => pair.Value != writes.GetValueOrDefault(pair.Key)))
            {
                await RefreshNativePresentationAsync(version).ConfigureAwait(true);
                return;
            }
            if (!outcome.Succeeded)
            {
                ReadStateRefusal = outcome.Refusal is { } refusal ? WindowRefusal.From(refusal) : null;
                return;
            }
            _presentationRead?.Dispose();
            _presentationRead = acquired;
            _nativePresentation = acquired!.Value;
            acquired = null;
            var changed = _readStateWritesApplied.Where(pair => pair.Value != writes.GetValueOrDefault(pair.Key))
                .Select(pair => pair.Key).ToHashSet();
            _readOccurrences.RemoveWhere(anchor => !changed.Contains(anchor.TextId));
            _readOccurrences.UnionWith(_nativePresentation.ReadOccurrences.Values.SelectMany(value => value)
                .Where(anchor => !changed.Contains(anchor.TextId)));
            foreach (var token in NativeRealizedTokens()) PrepareNativeTokenState(token);
            UpdateAssessmentReadState();
            await RefreshNativeAnalyzeAsync().ConfigureAwait(true);
            NotifyNativeCounts();
            RefreshNativeLines();
        }
        finally { acquired?.Dispose(); }
    }

    internal void ShowReader()
    {
        _readerVisible = true;
        RefreshNativeLines();
    }

    internal void HideReader()
    {
        _readerVisible = false;
        SelectedToken = null;
        ReleasePreviousPin(null);
        ReleaseLinePages();
        _visibleHeaders = [];
        _linePositions = [];
        OnPropertyChanged(nameof(DisplayedLines));
    }

    internal async Task StopAsync()
    {
        HideReader();
        await _selectionWork.ConfigureAwait(true);
        await _readStateWrites.ConfigureAwait(true);
    }

    private void RefreshNativeLines()
    {
        if (!_readerVisible || SelectedText?.Summary is not { } text || _selectionOwner.Reader is not { } reader)
        {
            ReleaseLinePages();
            _visibleHeaders = [];
        }
        else
        {
            if (_linePages is null || _linePages.TextId != text.TextId)
            {
                ReleaseLinePages();
                _linePages = new SelectionLinePages(reader, text, _selectionOwner.RegisterLine,
                    (_assess.Result?.Words ?? []).GroupBy(word => word.Word, StringComparer.Ordinal)
                        .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal),
                    navigation: _selectionOwner.Navigation, prepare: PrepareNativeToken);
                _linePages.ReleasingTokens += ReleaseRealizedTokenHandlers;
                _linePages.ReleasingLineTokens += ReleaseLineTokenHandlers;
                _lineObservation = _linePages.Observe(model => NativeModelCreated?.Invoke(model));
            }
            var positions = _nativeSummary!.SourcePositions.Where(position => position.Location.Anchor.TextId == text.TextId &&
                NativeMatches(position)).ToArray();
            var matching = positions.Select(position => position.Location.LineNumber).ToHashSet();
            _visibleHeaders = Filter == ResultsInTextFilter.All && string.IsNullOrWhiteSpace(SearchText)
                ? text.Lines : text.Lines.Where(header => matching.Contains(header.Number)).ToArray();
            foreach (var token in NativeRealizedTokens())
                token.IsDimmed = token.Occurrence is { } anchor && _nativePositions.TryGetValue(anchor, out var position) &&
                    !NativeMatches(position);
        }
        if (!_linePositions.Select(position => position.Header).SequenceEqual(_visibleHeaders))
            _linePositions = _visibleHeaders.Select(header => new SelectionLinePosition(this, header)).ToArray();
        if (_readerVisible && HasStandaloneSelectedWord && SelectedToken is { } selected)
        {
            if (!ReferenceEquals(_standaloneSelectedWord, selected))
            {
                _standaloneSelectedLine?.Dispose();
                _standaloneSelectedWord = selected;
                _standaloneSelectedLine = ResultsLineViewModel.ForSelectedWord(selected, _selectionOwner.RegisterLine());
                NativeModelCreated?.Invoke(new WeakReference<object>(_standaloneSelectedLine));
            }
        }
        else
        {
            _standaloneSelectedLine?.Dispose();
            _standaloneSelectedLine = null;
            _standaloneSelectedWord = null;
        }
        OnPropertyChanged(nameof(DisplayedLines));
        OnPropertyChanged(nameof(Message));
        OnPropertyChanged(nameof(HasMessage));
        OnPropertyChanged(nameof(HasTexts));
        OnPropertyChanged(nameof(HasAssessment));
        OnPropertyChanged(nameof(HasResults));
        OnPropertyChanged(nameof(HasLines));
        OnPropertyChanged(nameof(NeedsTexts));
    }

    internal ResultsLineViewModel RealizeLine(SelectionLineHeader header)
    {
        var line = (_linePages ?? throw new InvalidOperationException("No Text is being displayed.")).RealizeLine(header);
        line.SetPinnedCard(SelectedToken?.Occurrence is { } anchor && line.TextId == anchor.TextId &&
            line.ParagraphId == anchor.ParagraphId && line.SegmentId == anchor.SegmentId ? SelectedToken : null);
        return line;
    }

    internal OccurrenceAnchor? FirstOccurrenceInLine(SelectionLineHeader header) =>
        _nativeSummary?.SourcePositions.FirstOrDefault(position => position.Location.Anchor.TextId == SelectedText?.TextId &&
            position.Location.LineNumber == header.Number)?.Location.Anchor;

    internal int SourceTokenOffset(OccurrenceAnchor anchor) => _nativePositions.GetValueOrDefault(anchor)?.Location.TokenOffset ?? 0;

    internal OccurrenceAnchor? AdjacentVisibleOccurrence(OccurrenceAnchor anchor, int direction)
    {
        if (direction is not (-1 or 1)) return null;
        var lines = _visibleHeaders.Select(header => header.Number).ToHashSet();
        var positions = (_nativeSummary?.SourcePositions ?? []).Where(position =>
            position.Location.Anchor.TextId == SelectedText?.TextId && lines.Contains(position.Location.LineNumber)).ToArray();
        var index = Array.FindIndex(positions, position => position.Location.Anchor == anchor);
        var next = index + direction;
        return index >= 0 && next >= 0 && next < positions.Length ? positions[next].Location.Anchor : null;
    }

    private IEnumerable<ResultsTokenViewModel> NativeRealizedTokens() =>
        (_linePages?.RealizedLines.SelectMany(line => line.Tokens) ?? []).Concat(
            _pinnedToken is { } pin ? new[] { pin } : []).Distinct();

    private void PrepareNativeToken(ResultsTokenViewModel token)
    {
        NativeModelCreated?.Invoke(new WeakReference<object>(token));
        var key = new TextWordKey(token.WordformId, token.Form, token.FormWritingSystem);
        if (_nativeWords.TryGetValue(key, out var word)) token.BindActionFacts(word.Actions);
        token.Actions = this;
        PrepareNativeTokenState(token);
        token.PropertyChanged += OnTokenPropertyChanged;
    }

    private void PrepareNativeTokenState(ResultsTokenViewModel token)
    {
        token.IsCardOpen = ReferenceEquals(token, SelectedToken) || SelectedToken?.Occurrence is { } selected &&
            token.Occurrence == selected;
        token.SetReadState(token.Occurrence is { } anchor && _readOccurrences.Contains(anchor));
        _applyingChecks = true;
        try { token.IsSelectedForActions = token.Occurrence is { } occurrence && NativeIsChecked(occurrence); }
        finally { _applyingChecks = false; }
        var relevant = _changes.Items.Where(change => change.Addresses(token)).ToArray();
        token.PendingState = PendingChangeStates.FromChanges(relevant);
        token.IsPending = relevant.Length > 0;
        token.SetStagedMarkings(relevant);
        token.IsUncertainChanged = token.Occurrence is { } changed && _uncertainHighlights.Contains(changed);
        token.SetWarningEvidence(ResultsTokenViewModel.WarningEvidenceFor(token.Form,
            _nativePresentation is { } presentation ? new GrammarCheckResponse(presentation.Warnings, true) : null,
            _assess.Result?.Words ?? []));
    }

    private OccurrenceVerdict NativeVerdict(TextWordKey key) => _nativeAnalyze?.Verdicts.GetValueOrDefault(key) switch
    {
        SelectionOccurrenceVerdict.Matches => OccurrenceVerdict.Matches,
        SelectionOccurrenceVerdict.Differs => OccurrenceVerdict.Differs,
        SelectionOccurrenceVerdict.New => OccurrenceVerdict.New,
        SelectionOccurrenceVerdict.NoParse => OccurrenceVerdict.NoParse,
        SelectionOccurrenceVerdict.Limit => OccurrenceVerdict.Limit,
        _ => OccurrenceVerdict.NotAssessed,
    };

    private int NativeVerdictCount(OccurrenceVerdict verdict) => _nativeAnalyze?.Counts.GetValueOrDefault(
        Enum.Parse<SelectionOccurrenceVerdict>(verdict.ToString())) ?? 0;

    private async Task RefreshNativeAnalyzeAsync()
    {
        if (_nativeSummary is not { } summary) return;
        var version = ++_analyzeVersion;
        var presentation = _nativePresentation;
        var pending = _changes.Snapshot.Changes.ToArray();
        var facts = await Task.Run(() => SelectionAnalyzeProjection.Build(summary, pending, presentation)).ConfigureAwait(true);
        if (version != _analyzeVersion || !ReferenceEquals(summary, _nativeSummary)) return;
        _nativeAnalyze = facts;
        OnPropertyChanged(nameof(NeedsALookCount));
        OnPropertyChanged(nameof(NamedInWarningCount));
    }

    private bool NativeNamedWarning(TextWordKey key) =>
        _nativePresentation?.WarningsByWord.GetValueOrDefault(key)?.Any(warning =>
            warning.YourWords?.Match == WarningWordsMatch.Identity) == true;

    private bool NativeNeedsALook(SelectionSourcePosition position) =>
        _nativeWords[position.Word].Actions.NeedsALook && !_changes.Items.Any(change =>
            change.Addresses(position.Word.WordformId, position.Location.Anchor));

    private bool NativeMatches(SelectionSourcePosition position)
    {
        if (!string.IsNullOrWhiteSpace(SearchText) && !position.Word.Form.Contains(SearchText.Trim(),
            StringComparison.CurrentCultureIgnoreCase)) return false;
        if (!HasAssessment && Filter != ResultsInTextFilter.Unread) return true;
        return Filter switch
        {
            ResultsInTextFilter.All => true,
            ResultsInTextFilter.Unread => !_readOccurrences.Contains(position.Location.Anchor),
            ResultsInTextFilter.NeedsALook => NativeNeedsALook(position),
            ResultsInTextFilter.NamedInWarning => NativeNamedWarning(position.Word),
            _ => NativeVerdict(position.Word).ToString() == Filter.ToString(),
        };
    }

    private bool NativeIsChecked(OccurrenceAnchor anchor) => _selectAllOccurrences ?
        !_uncheckedOccurrences.Contains(anchor) : _checkedOccurrences.Contains(anchor);

    private void RecordNativeCheck(ResultsTokenViewModel token)
    {
        if (_applyingChecks || token.Occurrence is not { } anchor || !_nativePositions.ContainsKey(anchor)) return;
        var set = _selectAllOccurrences ? _uncheckedOccurrences : _checkedOccurrences;
        if (token.IsSelectedForActions != _selectAllOccurrences) set.Add(anchor);
        else set.Remove(anchor);
    }

    private void SelectNativeOccurrences(bool all)
    {
        _selectAllOccurrences = all;
        _checkedOccurrences.Clear();
        _uncheckedOccurrences.Clear();
        foreach (var token in NativeRealizedTokens()) PrepareNativeTokenState(token);
        NotifyNativeCounts();
    }

    private void NotifyNativeCounts()
    {
        foreach (var name in new[] { nameof(AllCount), nameof(UnreadCount), nameof(CheckedWordCount),
            nameof(CheckedWordCountLabel), nameof(HasCheckedWords), nameof(HasCheckedUncertainChanges),
            nameof(ShowWordCheckboxes), nameof(SelectMenuLabel), nameof(SelectedReadStateCount),
            nameof(HasSelectedReadStateOccurrences), nameof(DiffersCount), nameof(NewCount), nameof(NoParseCount),
            nameof(MatchesCount), nameof(LimitCount), nameof(NotAssessedCount), nameof(NeedsALookCount),
            nameof(NamedInWarningCount), nameof(HasWarningEvidence), nameof(HasNotAssessed),
            nameof(ChosenTextsAnalysisCount), nameof(HasChosenTextAnalyses), nameof(ChosenTextsRemoveHeader),
            nameof(ChosenTextsRemovalPreview) }) OnPropertyChanged(name);
        SelectAllWordsCommand.NotifyCanExecuteChanged();
        ClearSelectedWordsCommand.NotifyCanExecuteChanged();
        MarkSelectionReadCommand.NotifyCanExecuteChanged();
        MarkSelectionUnreadCommand.NotifyCanExecuteChanged();
        NotifyScopeCommands();
    }

    private void RefreshNativeUncertainty()
    {
        _uncertainHighlights.Clear();
        foreach (var change in _changes.Items.Where(item => item.IsUncertain && item.AfterWords.Count > 0))
        {
            if (change.Occurrence is not { } anchor || !_nativePositions.ContainsKey(anchor)) continue;
            var positions = _nativePositions.Values.Where(position => position.Location.Anchor.TextId == anchor.TextId &&
                position.Location.Anchor.ParagraphId == anchor.ParagraphId && position.Location.Anchor.SegmentId == anchor.SegmentId)
                .OrderBy(position => position.Location.Anchor.Index).ToArray();
            var expected = change.AfterWords;
            if (positions.Length != expected.Count || !positions.Zip(expected).All(pair =>
                pair.First.Location.Anchor.Index == pair.Second.Index && pair.First.Word.WordformId is { } id &&
                CanonicalId.FromGuid(id).Value == pair.Second.WordformId && pair.First.Word.Form ==
                pair.Second.Form.Normalize(System.Text.NormalizationForm.FormD))) continue;
            foreach (var word in expected.Where(word => word.IsChanged)) _uncertainHighlights.Add(anchor with { Index = word.Index });
        }
    }

    private void RefreshNativePending()
    {
        RefreshNativeUncertainty();
        TrackSelectionWork(RefreshNativeAnalyzeAsync());
        foreach (var token in NativeRealizedTokens()) PrepareNativeTokenState(token);
        NotifyNativeCounts();
        RefreshNativeLines();
    }

    private void ApplyNativeReadState(Guid textId, IReadOnlyList<OccurrenceAnchor> read)
    {
        _readOccurrences.RemoveWhere(anchor => anchor.TextId == textId);
        _readOccurrences.UnionWith(read.Where(_nativePositions.ContainsKey));
        UpdateAssessmentReadState();
        foreach (var token in NativeRealizedTokens()) PrepareNativeTokenState(token);
        NotifyNativeCounts();
        RefreshNativeLines();
    }

    private void UpdateAssessmentReadState()
    {
        var knownTexts = (_nativePresentation?.ReadOccurrences.Keys ?? []).Concat(_readStateWritesApplied.Keys).ToHashSet();
        var unread = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var position in _nativeSummary?.SourcePositions ?? [])
        {
            if (!knownTexts.Contains(position.Location.Anchor.TextId)) continue;
            unread[position.Word.Form] = unread.GetValueOrDefault(position.Word.Form) ||
                !_readOccurrences.Contains(position.Location.Anchor);
        }
        bool? IsUnread(string form) => unread.TryGetValue(form.Normalize(System.Text.NormalizationForm.FormD),
            out var value) ? value : null;
        _assess.Words.ApplyReadState(IsUnread);
        _texts.ApplyReadState(IsUnread);
    }

    private async Task SetNativeTextReadStateAsync(bool isRead)
    {
        if (SelectedText is not { } text || _selectionOwner.Reader is not { } reader ||
            string.IsNullOrWhiteSpace(_assess.ProjectPath)) return;
        var generation = _readStateGeneration;
        var version = ++_readStateWriteVersion;
        var expected = reader.Context.ExpectedWriteContext();
        var assessmentIds = expected.SelectionEvidence is { } evidence ?
            (evidence.RootAssessmentId is { } root ? new[] { root } : []).Concat(evidence.ReplacementAssessmentIds)
                .Distinct(StringComparer.Ordinal).ToArray() : [];
        ReadStateRefusal = null;
        ReadStateNotice = null;
        var outcome = await _commands.ReadWordStateAsync(new WordReadStateRequest(_assess.ProjectPath, text.TextId,
            IsRead: isRead) { ExpectedContext = expected, AssessmentIds = assessmentIds }, CancellationToken.None)
            .ConfigureAwait(true);
        if (generation != _readStateGeneration || version != _readStateWriteVersion) return;
        if (outcome.Succeeded)
        {
            ApplyWrittenReadState(text.TextId, outcome.Value!.ReadOccurrences);
            ReadStateNotice = ReadStateSkipNotice(outcome.Value.SkippedOccurrences.Count);
        }
        else ReadStateRefusal = outcome.Refusal is { } refusal ? WindowRefusal.From(refusal) : null;
    }

    private TextOccurrenceLocation? NativeLocation(OccurrenceAnchor anchor)
    {
        if (!_nativePositions.TryGetValue(anchor, out var position)) return null;
        var location = position.Location;
        var title = _nativeSummary!.Texts[location.TextOrder].Title;
        return new TextOccurrenceLocation(location.TextOrder, location.LineNumber, anchor.Index,
            $"{title}, line {location.LineNumber}, word {anchor.Index + 1}");
    }

    internal void NavigateOccurrence(OccurrenceAnchor anchor) => OccurrenceRequested?.Invoke(anchor);

    internal bool SelectOccurrenceText(OccurrenceAnchor anchor, bool resetFilters = true)
    {
        if (!_nativePositions.ContainsKey(anchor)) return false;
        SelectedText = Texts.First(text => text.TextId == anchor.TextId);
        if (resetFilters) { Filter = ResultsInTextFilter.All; SearchText = string.Empty; }
        return true;
    }

    internal async Task<ResultsTokenViewModel?> ReadOccurrenceAsync(OccurrenceAnchor anchor, bool resetFilters = true,
        bool realizeViewport = true)
    {
        if (!SelectOccurrenceText(anchor, resetFilters)) return null;
        var version = _selectionVersion;
        if (realizeViewport && OccurrenceRealizationRequested is { } requested)
            foreach (var handler in requested.GetInvocationList().Cast<Func<OccurrenceAnchor, Task<ResultsTokenViewModel?>>>())
            {
                var visible = await handler(anchor).ConfigureAwait(true);
                if (version != _selectionVersion) return null;
                if (visible is not null) return visible;
            }
        var position = _nativePositions[anchor];
        if (_linePages is not { } pages) return null;
        var tokens = await pages.ReadLinePageAsync(position.Location.LineNumber, position.Location.TokenOffset / 20 * 20)
            .ConfigureAwait(true);
        return version == _selectionVersion && ReferenceEquals(pages, _linePages) ?
            tokens.OfType<ResultsTokenViewModel>().FirstOrDefault(token => token.Occurrence == anchor) : null;
    }

    private async Task SelectNativeWordAsync(string word, string? wordformId)
    {
        var generation = _selectionOwner.Generation;
        await _selectionOwner.Pending.ConfigureAwait(true);
        if (_nativeSummary is null || generation != _selectionOwner.Generation) return;
        Guid? id = CanonicalId.TryParse(wordformId, out var canonical) ? canonical.ToGuid() : null;
        var position = _nativeSummary?.SourcePositions.FirstOrDefault(position => position.Word.Form == word &&
            (wordformId is null || position.Word.WordformId == id));
        if (position is not null)
        {
            var token = await ReadOccurrenceAsync(position.Location.Anchor).ConfigureAwait(true);
            if (token is not null) { SelectToken(token); NavigateOccurrence(position.Location.Anchor); }
            return;
        }
        if (_assess.Result?.Words.FirstOrDefault(result => result.Word == word) is { } result)
        {
            var token = NativeCardToken(result, id);
            ReleasePreviousPin(null);
            token.OwnRegistration(_selectionOwner.RegisterCardToken());
            NativeModelCreated?.Invoke(new WeakReference<object>(token));
            _pinnedCard = _selectionOwner.RegisterCard(this, CloseTokenCard);
            _pinnedToken = token;
            PrepareNativeTokenState(token);
            SelectedToken = token;
        }
    }

    private ResultsTokenViewModel PinToken(ResultsTokenViewModel token)
    {
        if (ReferenceEquals(token, _pinnedToken)) return token;
        ReleasePreviousPin(null);
        var pin = token.CopyForCard();
        pin.OwnRegistration(_selectionOwner.RegisterCardToken());
        NativeModelCreated?.Invoke(new WeakReference<object>(pin));
        _pinnedCard = _selectionOwner.RegisterCard(this, CloseTokenCard);
        _pinnedToken = pin;
        pin.Actions = this;
        PrepareNativeTokenState(pin);
        return pin;
    }

    private void ReleasePreviousPin(ResultsTokenViewModel? current)
    {
        if (_pinnedToken is null || ReferenceEquals(current, _pinnedToken)) return;
        _pinnedToken.Dispose();
        _pinnedToken = null;
        _pinnedCard?.Dispose();
        _pinnedCard = null;
    }

    private ResultsTokenViewModel NativeCardToken(AssessmentWordResult result, Guid? requestedId = null)
    {
        var words = _nativeWords.Values.Where(word => word.Key.Form == result.Word).ToArray();
        var ids = words.SelectMany(word => word.Actions.CandidateWordformIds).Distinct().ToArray();
        var target = requestedId ?? (ids.Length == 1 ? ids[0] : (Guid?)null);
        var facts = words.FirstOrDefault(word => target is { } exact && word.Key.WordformId == exact) ??
            words.FirstOrDefault(word => word.Key.WordformId is null &&
                (target is null || word.Actions.CandidateWordformIds.Count == 1 &&
                    word.Actions.CandidateWordformIds[0] == target));
        var stored = (facts is null || facts.Actions.CandidateWordformIds.Count != 1 ? [] : result.StoredAnalyses).Select(analysis => new SIL.Motif.Commands.Queries.ProjectAnalysis(
            string.Empty, analysis.Morphs)
        {
            StoredAnalysisId = analysis.StoredAnalysisId,
            StoredAnalysisOpinion = analysis.StoredAnalysisOpinion,
            Identity = analysis.Identity,
        }).ToArray();
        var source = new SIL.Motif.Commands.Queries.TextToken(result.Word, result.Word, null, null)
        { WordformId = target, StoredAnalyses = stored,
            IncorrectSpelling = facts?.IncorrectSpelling == true };
        var token = new ResultsTokenViewModel("Assessment", 0, source, result,
            location: "Not in a chosen text", expectedContext: _selectionOwner.Reader?.Context.ExpectedWriteContext(),
            producingAssessmentId: result.Origin?.AssessmentId, actionFacts: facts?.Actions ??
                new SelectionWordActionFacts(false, false, false, false, false, []) { CandidateWordformIds = ids },
            storedAnalysesAvailable: facts?.Actions.CandidateWordformIds.Count == 1);
        token.Actions = this;
        return token;
    }
}

internal sealed record SelectionLinePosition(ResultsInTextViewModel Owner, SelectionLineHeader Header)
{
    public ResultsLineViewModel? Model => Owner.LinePages?.TryRealizeLine(Header);
}
