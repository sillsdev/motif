using Avalonia.Threading;
using SIL.Motif.Commands.SelectionReading;

namespace SIL.Motif.App.ViewModels;

public sealed partial class TextWordsViewModel
{
    internal ResultsInTextViewModel? WordCardActions { get; set; }

    private const int RowCapacity = 128;
    private readonly Dictionary<TextWordKey, SummaryRow> _summaryRows = [];
    private readonly LinkedList<TextWordKey> _rowOrder = [];
    private readonly HashSet<TextWordKey> _checkedKeys = [];
    private readonly HashSet<TextWordKey> _detailQueue = [];
    private readonly HashSet<TextWordRowViewModel> _visibleRows = new(ReferenceEqualityComparer.Instance);
    private CancellationTokenSource? _detailCancellation;
    private bool _detailScheduled;
    private Task _detailPending = Task.CompletedTask;
    private SelectionWordOccurrences? _cardOccurrences;
    private IDisposable? _cardRegistration;
    private TextWordRowViewModel? _openRow;
    private long _rowsGeneration;
    private long _cardGeneration;
    private Func<string, bool?>? _readState;

    internal void ApplyReadState(Func<string, bool?> readState)
    {
        _readState = readState;
        foreach (var entry in _summaryRows.Values) entry.Row.ApplyReadState(readState(entry.Row.Form));
    }

    private IReadOnlyList<ContextWordShape> _shapes = [];
    private Dictionary<TextWordKey, ContextWordShape> _shapeByKey = [];

    /// <summary>The whole Context's rows, one for each written shape in its writing system.</summary>
    internal IReadOnlyList<SelectionWordSummary> ShapeRows => _shapes.Select(shape => shape.Row).ToArray();

    /// <summary>The shape a row stands for, so its places can be read from every wordform recorded under it.</summary>
    internal ContextWordShape? ShapeOf(TextWordKey key) => _shapeByKey.GetValueOrDefault(key);

    /// <summary>The row that is open in the list, or null.</summary>
    internal TextWordRowViewModel? OpenRow => _openRow;

    /// <summary>Raised after the open row changes or closes, so the page can show or drop its places panel.</summary>
    internal event Action? OpenRowChanged;

    private sealed record SummaryRow(TextWordRowViewModel Row, LinkedListNode<TextWordKey> Position);

    private void PublishSummaryRows()
    {
        _shapes = ContextWordShape.Group(_selectionReads.Summary?.Words ?? []);
        _shapeByKey = _shapes.ToDictionary(shape => shape.Row.Key);
        var words = _shapes.Select(shape => shape.Row).ToArray();
        var currentKeys = words.Select(word => word.Key).ToHashSet();
        if (_selectionReads.Summary is not null) _checkedKeys.IntersectWith(currentKeys);
        var generation = _rowsGeneration;
        _projectRows = new LazyProjectionList<SelectionWordSummary, TextWordRowViewModel>(words,
            GetSummaryRow, row => row.Summary!, () => generation == _rowsGeneration && _acceptLoads);
        OnPropertyChanged(nameof(ProjectWords));
        ApplySummaryFilter();
        RaiseCheckedWords();
        HandOffCheckedWordsCommand.NotifyCanExecuteChanged();
    }

    private static AssessWordRowViewModel? ShownAssessmentOf(SelectionWordSummary? summary,
        AssessWordRowViewModel? candidate) => summary?.Assessment?.AssessmentId is { } producing &&
        candidate?.Source.Origin?.AssessmentId == producing ? candidate : null;

    private void ApplySummaryFilter()
    {
        var matches = ShapeRows.AsEnumerable();
        if (StatusFilter is { } status)
            matches = matches.Where(word => WordProjectStatuses.FromStanding(word.ProjectStanding) == status);
        if (SeveralOnly) matches = matches.Where(word => word.ChosenAnalysisCount > 1);
        if (!string.IsNullOrWhiteSpace(SearchText))
            matches = matches.Where(word => word.Key.Form.Contains(SearchText.Trim(), StringComparison.CurrentCultureIgnoreCase));
        var ordered = matches.OrderByDescending(word => word.PlaceCount ?? ListCountOf(word) ?? -1).ToArray();
        _hiddenByFilter = ShapeRows.Count - ordered.Length;
        OnPropertyChanged(nameof(FilterHidesText));
        var generation = _rowsGeneration;
        Rows = new LazyProjectionList<SelectionWordSummary, TextWordRowViewModel>(ordered,
            GetSummaryRow, row => row.Summary!, () => generation == _rowsGeneration && _acceptLoads);
        DisplayRows = new LazyProjectionList<SelectionWordSummary, SelectionWordRowPosition>(ordered,
            summary => new SelectionWordRowPosition(summary, () => generation == _rowsGeneration && _acceptLoads
                ? GetSummaryRow(summary) : null), position => position.Summary,
            () => generation == _rowsGeneration && _acceptLoads);
        OnPropertyChanged(nameof(Rows));
        OnPropertyChanged(nameof(DisplayRows));
    }

    private TextWordRowViewModel GetSummaryRow(SelectionWordSummary summary)
    {
        if (_summaryRows.TryGetValue(summary.Key, out var cached))
        {
            _rowOrder.Remove(cached.Position);
            _rowOrder.AddLast(cached.Position);
            return cached.Row;
        }
        MakeSummaryRowRoom();
        var row = new TextWordRowViewModel(summary, _selectionReads!.RegisterRow(), WordRowRoutes,
            Path.GetFileNameWithoutExtension(_projectPath), _selectionReads.Reader!.Context.ExpectedWriteContext(),
            ShapeOf(summary.Key)?.HasSeveralWordforms == true) { IsChecked = _checkedKeys.Contains(summary.Key) };
        row.ApplyListCount(ListCountOf(summary));
        row.WordCardTokenFactory = result => row.CreateCardToken(result, WordCardActions,
            _selectionReads.RegisterCardToken());
        row.ShowAssessment(ShownAssessmentOf(summary, _assessed?.Invoke(row.Form)));
        if (_readState is { } readState) row.ApplyReadState(readState(row.Form));
        row.PropertyChanged += OnWordRowPropertyChanged;
        row.CardChanged += OnSummaryCardChanged;
        _summaryRows.Add(summary.Key, new SummaryRow(row, _rowOrder.AddLast(summary.Key)));
        RowCreatedForDiagnostics?.Invoke(new WeakReference<object>(row));
        return row;
    }

    // Realization requests morphology; indexing the compact list alone must not load it.
    internal void RealizeRow(TextWordRowViewModel row)
    {
        if (row.Summary is not { } summary || !_summaryRows.TryGetValue(summary.Key, out var cached) ||
            !ReferenceEquals(cached.Row, row)) return;
        _visibleRows.Add(row);
        if (row.HasStoredDetail) return;
        _detailQueue.Add(summary.Key);
        if (_detailScheduled) return;
        _detailScheduled = true;
        Dispatcher.UIThread.Post(() =>
        {
            var previous = _detailPending;
            var current = LoadVisibleDetailsAsync();
            _detailPending = previous.IsCompleted ? current : Task.WhenAll(previous, current);
        });
    }

    internal void ReleaseVisibleRow(TextWordRowViewModel row) => _visibleRows.Remove(row);

    internal void ShowVisibleRows()
    {
        PublishSummaryRows();
    }

    internal void HideVisibleRows()
    {
        ReleaseSummaryRows();
    }

    internal Task VisibleDetailsPending => _detailPending;
    internal Task CardPending { get; private set; } = Task.CompletedTask;

    private async Task LoadVisibleDetailsAsync()
    {
        try
        {
            while (_detailQueue.Count > 0)
                await LoadVisibleDetailBatchAsync().ConfigureAwait(true);
        }
        finally
        {
            _detailScheduled = false;
        }
    }

    internal async Task SettleVisibleDetailsAsync()
    {
        do
        {
            Dispatcher.UIThread.RunJobs();
            await _detailPending.ConfigureAwait(true);
        }
        while (_detailScheduled || _detailQueue.Count > 0 || !_detailPending.IsCompleted);
    }

    private async Task LoadVisibleDetailBatchAsync()
    {
        var generation = _rowsGeneration;
        var requested = _detailQueue.Where(_summaryRows.ContainsKey).ToHashSet();
        var keys = _summaryRows.Where(pair => pair.Value.Row.HasStoredDetail || requested.Contains(pair.Key))
            .Select(pair => pair.Key).Take(RowCapacity).ToArray();
        _detailQueue.Clear();
        if (keys.Length == 0 || _selectionReads.Reader is not { } reader) return;
        _detailCancellation ??= new CancellationTokenSource();
        SelectionRead<SelectionWordDetails>? read = null;
        try
        {
            var outcome = await reader.ReadWordDetailsAsync(keys, _detailCancellation.Token).ConfigureAwait(true);
            read = outcome.Value;
            if (generation != _rowsGeneration) return;
            if (!outcome.Succeeded)
            {
                Refusal = outcome.Refusal;
                return;
            }
            var live = keys.Where(_summaryRows.ContainsKey).ToArray();
            if (live.Length != keys.Length)
            {
                _detailQueue.UnionWith(live);
                return;
            }
            if (live.Length == 0) return;
            var homographs = await ReadHomographAnalysesAsync(reader, live).ConfigureAwait(true);
            if (generation != _rowsGeneration) return;
            // A row may have been released while the other wordforms were read; those rows ask again when shown.
            live = live.Where(_summaryRows.ContainsKey).ToArray();
            if (live.Length != keys.Length)
            {
                _detailQueue.UnionWith(live);
                return;
            }
            var cohort = new WordDetailCohort(read!, live.Length);
            read = null;
            foreach (var key in live)
            {
                if (homographs.TryGetValue(key, out var others))
                    _summaryRows[key].Row.SetHomographDetails(others.PrimaryOrdinal, others.Others);
                _summaryRows[key].Row.SetStoredDetail(cohort.Value.Wordforms.GetValueOrDefault(key), cohort.TakeOwnership());
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            read?.Dispose();
        }
    }

    /// <summary>Makes the read of the other wordforms' analyses fail as a refused read would.</summary>
    internal bool HomographReadsRefused { get; set; }

    /// <summary>Reads the other wordforms' analyses for a shared row again; false while any is still unread.</summary>
    internal async Task<bool> ReadHomographsAsync(TextWordRowViewModel row)
    {
        if (row.Summary is not { } summary || _selectionReads.Reader is not { } reader) return false;
        _detailCancellation ??= new CancellationTokenSource();
        var read = await ReadHomographAnalysesAsync(reader, [summary.Key]).ConfigureAwait(true);
        if (read.TryGetValue(summary.Key, out var found) && _summaryRows.TryGetValue(summary.Key, out var cached) &&
            ReferenceEquals(cached.Row, row)) row.SetHomographDetails(found.PrimaryOrdinal, found.Others);
        return !row.HomographsUnread;
    }

    // A row that several wordforms share also needs each other wordform's stored analyses, read once for the batch.
    private async Task<Dictionary<TextWordKey, (int PrimaryOrdinal, IReadOnlyList<HomographAnalyses> Others)>>
        ReadHomographAnalysesAsync(SelectionReader reader, IReadOnlyList<TextWordKey> keys)
    {
        var result = new Dictionary<TextWordKey, (int, IReadOnlyList<HomographAnalyses>)>();
        var wanted = keys.Where(key => ShapeOf(key)?.HasSeveralWordforms == true).ToArray();
        if (wanted.Length == 0 || HomographReadsRefused) return result;
        var members = wanted.SelectMany(key => ShapeOf(key)!.Members.Select(member => member.Key)
            .Where(member => member != key)).Distinct().ToArray();
        var outcome = await reader.ReadWordDetailsAsync(members, _detailCancellation!.Token).ConfigureAwait(true);
        using var read = outcome.Value;
        if (!outcome.Succeeded) return result;
        var projectName = Path.GetFileNameWithoutExtension(_projectPath);
        foreach (var key in wanted)
        {
            var shape = ShapeOf(key)!;
            var ordinals = shape.Members.Select((member, index) => (member.Key, Ordinal: index + 1)).ToArray();
            var others = ordinals.Where(item => item.Key != key).Select(item => new HomographAnalyses(
                item.Key.WordformId, item.Ordinal, read!.Value.Wordforms.GetValueOrDefault(item.Key) is { } wordform
                    ? SelectionDisplayProjection.StoredAnalyses(wordform, projectName) : [])).ToArray();
            result[key] = (ordinals.Single(item => item.Key == key).Ordinal, others);
        }
        return result;
    }

    private void MakeSummaryRowRoom()
    {
        while (_summaryRows.Count >= RowCapacity)
        {
            var candidate = _rowOrder.First;
            while (candidate is not null && (ReferenceEquals(_summaryRows[candidate.Value].Row, _openRow) ||
                _visibleRows.Contains(_summaryRows[candidate.Value].Row))) candidate = candidate.Next;
            if (candidate is null) throw new InvalidOperationException("The Word row budget is occupied by displayed rows.");
            var row = _summaryRows[candidate.Value].Row;
            _summaryRows.Remove(candidate.Value);
            _rowOrder.Remove(candidate);
            ReleaseRow(row);
        }
    }

    private void ReleaseRow(TextWordRowViewModel row)
    {
        _visibleRows.Remove(row);
        row.PropertyChanged -= OnWordRowPropertyChanged;
        row.CardChanged -= OnSummaryCardChanged;
        row.Dispose();
    }

    private void ReleaseSummaryRows()
    {
        ++_rowsGeneration;
        Cancel(_detailCancellation);
        _detailCancellation?.Dispose();
        _detailCancellation = null;
        _detailQueue.Clear();
        ReleaseSummaryCard();
        foreach (var entry in _summaryRows.Values) ReleaseRow(entry.Row);
        _summaryRows.Clear();
        _rowOrder.Clear();
        Rows = [];
        DisplayRows = [];
        _projectRows = [];
        OnPropertyChanged(nameof(Rows));
        OnPropertyChanged(nameof(DisplayRows));
    }

    private void OnSummaryCardChanged(TextWordRowViewModel row, bool isOpen)
    {
        if (!isOpen)
        {
            if (ReferenceEquals(_openRow, row)) ReleaseSummaryCard();
            return;
        }
        if (ReferenceEquals(_openRow, row)) return;
        ReleaseSummaryCard();
        var previous = CardPending;
        _openRow = row;
        _cardRegistration = _selectionReads!.RegisterCard(this, ReleaseSummaryCard);
        var current = OpenSummaryCardAsync(row, ++_cardGeneration);
        CardPending = previous.IsCompleted ? current : Task.WhenAll(previous, current);
        OpenRowChanged?.Invoke();
    }

    private async Task OpenSummaryCardAsync(TextWordRowViewModel row, long generation)
    {
        if (_selectionReads.Reader is not { } reader || row.Summary is not { } summary) return;
        var source = new SelectionWordOccurrences(reader, summary, _selectionReads.Summary?.Texts ?? [],
            row.SetOccurrencePage);
        _cardOccurrences = source;
        await source.OpenAsync().ConfigureAwait(true);
        if (generation != _cardGeneration || !ReferenceEquals(_openRow, row)) return;
        if (source.Refusal is { } refusal) Refusal = refusal;
        row.SetOccurrenceSource(source);
    }

    private void ReleaseSummaryCard()
    {
        ++_cardGeneration;
        var previous = _openRow;
        _openRow = null;
        previous?.CloseCard();
        previous?.SetOccurrenceSource(null);
        _cardRegistration?.Dispose();
        _cardRegistration = null;
        if (_cardOccurrences is { } source)
            CardPending = Task.WhenAll(CardPending, source.StopAsync());
        _cardOccurrences = null;
        if (previous is not null) OpenRowChanged?.Invoke();
    }

    private sealed class WordDetailCohort(SelectionRead<SelectionWordDetails> read, int references)
    {
        private int _references = references;
        public SelectionWordDetails Value => read.Value;
        public IDisposable TakeOwnership() => new Ownership(this);
        private void Release()
        {
            if (Interlocked.Decrement(ref _references) == 0) read.Dispose();
        }
        private sealed class Ownership(WordDetailCohort cohort) : IDisposable
        {
            private WordDetailCohort? _cohort = cohort;
            public void Dispose()
            {
                Interlocked.Exchange(ref _cohort, null)?.Release();
            }
        }
    }
}

/// <summary>Identifies a compact Word row whose display model belongs to its owner's bounded cache.</summary>
internal sealed class SelectionWordRowPosition(SelectionWordSummary summary, Func<TextWordRowViewModel?> resolve)
{
    private WeakReference<TextWordRowViewModel>? _model;
    internal SelectionWordSummary Summary { get; } = summary;
    internal TextWordRowViewModel? CapturedModel => _model is { } model && model.TryGetTarget(out var row) ? row : null;
    public TextWordRowViewModel? Model
    {
        get
        {
            var row = resolve();
            _model = row is null ? null : new(row);
            return row;
        }
    }
}
