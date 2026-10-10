using System.Collections.Specialized;
using SIL.Motif.App.Services;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Commands.SelectionReading;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.PanGloss;

namespace SIL.Motif.App.ViewModels;

/// <summary>Coalesces realized line pages into one bounded reader result and releases their models together.</summary>
internal sealed class SelectionLinePages
{
    private readonly SelectionReader _reader;
    private readonly SelectionTextSummary _text;
    private readonly HashSet<SelectionLineHeader> _headers;
    private readonly IReadOnlyDictionary<int, SelectionLineHeader> _headersByNumber;
    private readonly IReadOnlyDictionary<string, AssessmentWordResult> _assessments;
    private readonly SavedProjectNavigation? _navigation;
    private readonly Action<ResultsTokenViewModel>? _prepare;
    private readonly Func<IDisposable> _registerLine;
    private readonly DisplayProjectionCache<SelectionLineHeader, ResultsLineViewModel> _lines;
    private readonly LazyProjectionList<SelectionLineHeader, ResultsLineViewModel> _lineSource;
    private readonly Dictionary<int, LinePage> _pages = [];
    private readonly Dictionary<int, ResultsLineViewModel> _displayedLines = [];
    private readonly List<WaitingPage> _waiting = [];
    private readonly CancellationTokenSource _lifetime = new();
    private SelectionRead<TextLineSlice>? _read;
    private CancellationTokenSource? _readCancellation;
    private Task _pending = Task.CompletedTask;
    private bool _running;
    private bool _stopped;
    private bool _visible = true;
    private long _version;
    private long _tokenGeneration;

    public SelectionLinePages(SelectionReader reader, SelectionTextSummary text, Func<IDisposable> registerLine,
        IReadOnlyDictionary<string, AssessmentWordResult>? assessments = null,
        SavedProjectNavigation? navigation = null, Action<ResultsTokenViewModel>? prepare = null)
    {
        _reader = reader;
        _text = text;
        _headers = new HashSet<SelectionLineHeader>(text.Lines, ReferenceEqualityComparer.Instance);
        _headersByNumber = text.Lines.ToDictionary(header => header.Number);
        _registerLine = registerLine;
        _assessments = assessments ?? new Dictionary<string, AssessmentWordResult>();
        _navigation = navigation;
        _prepare = prepare;
        _lines = new(CreateLine, ReleaseLine, capacity: 48);
        _lineSource = new LazyProjectionList<SelectionLineHeader, ResultsLineViewModel>(text.Lines, _lines.Get,
            line => _headersByNumber[line.Number], () => !_stopped && _visible);
    }

    internal Guid TextId => _text.TextId;
    public IReadOnlyList<ResultsLineViewModel> Lines => _lineSource;
    public IReadOnlyList<SelectionLineHeader> Headers => _visible && !_stopped ? _text.Lines : [];
    public ResultsLineViewModel RealizeLine(SelectionLineHeader header)
    {
        if (!_visible || _stopped) throw new InvalidOperationException("The Text page is hidden or closed.");
        if (!_headers.Contains(header))
            throw new ArgumentException("The line does not belong to this captured Text.", nameof(header));
        return _lines.Get(header);
    }

    internal ResultsLineViewModel? TryRealizeLine(SelectionLineHeader header) =>
        !_visible || _stopped || !_headers.Contains(header) ? null : _lines.Get(header);

    internal ResultsTokenViewModel? TokenAt(int lineNumber, int offset, long version) =>
        _pages.TryGetValue(lineNumber, out var page) && version == page.TokenGeneration && page.Published &&
        page.Line is { } line && offset >= page.Offset && offset < page.Offset + line.Tokens.Count
            ? line.Tokens[offset - page.Offset] : null;
    public IEnumerable<ResultsLineViewModel> RealizedLines => _lines.Models;
    public Task Pending => _pending;
    public Refusal? Refusal { get; private set; }
    public event Action? ReleasingTokens;
    public event Action<ResultsLineViewModel>? ReleasingLineTokens;
    public IDisposable Observe(Action<WeakReference<object>> observer) => _lines.Observe(observer);

    public Task<IReadOnlyList<object>> ReadLinePageAsync(int lineNumber, int tokenOffset,
        CancellationToken cancellationToken = default)
    {
        if (_stopped || !_visible) return Task.FromResult<IReadOnlyList<object>>([]);
        var header = _headersByNumber.GetValueOrDefault(lineNumber)
            ?? throw new ArgumentOutOfRangeException(nameof(lineNumber));
        var line = _lines.Get(header);
        return ReadAsync((LinePage)line.TokenSource!, tokenOffset, 20, cancellationToken);
    }

    private ResultsLineViewModel CreateLine(SelectionLineHeader header)
    {
        var source = new LinePage(this, header);
        var line = new ResultsLineViewModel(_text.TextId, header, source, _registerLine());
        _displayedLines.Add(header.Number, line);
        _pages.Add(header.Number, source);
        return line;
    }

    private void ReleaseLine(ResultsLineViewModel line)
    {
        ReleasingLineTokens?.Invoke(line);
        if (line.TokenSource is LinePage page) page.ReleasePage();
        _pages.Remove(line.Number);
        _displayedLines.Remove(line.Number);
        line.Dispose();
    }

    private Task<IReadOnlyList<object>> ReadAsync(LinePage page, int offset, int count,
        CancellationToken cancellationToken)
    {
        if (_stopped || !_visible || !_pages.TryGetValue(page.Header.Number, out var current) || !ReferenceEquals(page, current))
            return Task.FromResult<IReadOnlyList<object>>([]);
        if (offset < 0 || offset > page.Count || count is < 0 or > 20)
            throw new ArgumentOutOfRangeException(nameof(offset));
        var take = Math.Min(count, page.Count - offset);
        if (page.Offset == offset && page.Take == take && page.Published &&
            _read?.PresentationStamp == _reader.Diagnostics.PresentationStamp)
        {
            page.Active = true;
            return Task.FromResult<IReadOnlyList<object>>(page.Line!.Tokens);
        }
        MakeTokenRoom(page, take);
        if (!page.Active || page.Offset != offset || page.Take != take)
        {
            page.Active = true;
            page.Offset = offset;
            page.Take = take;
            page.Published = false;
            Changed();
        }
        var completion = new TaskCompletionSource<IReadOnlyList<object>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _waiting.Add(new WaitingPage(page, offset, completion));
        Schedule();
        return completion.Task.WaitAsync(cancellationToken);
    }

    private void Changed()
    {
        ++_version;
        try { _readCancellation?.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    private void Schedule()
    {
        if (_running || _stopped || !_visible) return;
        _running = true;
        _pending = PumpAsync();
    }

    private async Task PumpAsync()
    {
        await Task.Yield();
        try
        {
            while (!_stopped)
            {
                var active = _pages.Values.Where(page => page.Active || page.Published).OrderBy(page => page.Header.Number).ToArray();
                if (active.Length == 0) break;
                var version = _version;
                using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
                _readCancellation = cancellation;
                var outcome = await _reader.ReadLinePagesAsync(_text.TextId, active.Select(page =>
                    new TextLinePage(page.Header.Number, page.Offset, page.Take)).ToArray(), cancellation.Token)
                    .ConfigureAwait(true);
                var acquired = outcome.Value;
                try
                {
                    if (_stopped) break;
                    if (version != _version) continue;
                    Refusal = outcome.Refusal;
                    if (!outcome.Succeeded)
                    {
                        ReleaseTokens();
                        CompleteWaiting();
                        break;
                    }
                    var read = outcome.Value!;
                    var stampChanged = _read is not null && _read.PresentationStamp != read.PresentationStamp;
                    var projected = SelectionDisplayProjection.Lines(read.Value, _navigation);
                    var results = ResolveAssessments(read.Value);
                    try
                    {
                        for (var index = 0; index < read.Value.Lines.Count; index++)
                        {
                            var fragment = read.Value.Lines[index];
                            var page = _pages[fragment.LineNumber];
                            if (!page.Published || stampChanged || page.PublishedOffset != page.Offset || page.PublishedTake != page.Take)
                            {
                                ReleasingLineTokens?.Invoke(page.Line!);
                                page.Line!.ShowPage(_text.Title, projected[index], fragment, results, read, _prepare);
                                page.TokenGeneration = ++_tokenGeneration;
                                for (var tokenIndex = 0; tokenIndex < page.Line!.Tokens.Count; tokenIndex++)
                                    page.Line.Tokens[tokenIndex].BindStripTarget(new SelectionTokenPosition(this,
                                        page.Header.Number, page.Offset + tokenIndex, page.TokenGeneration));
                                page.PublishedOffset = page.Offset;
                                page.PublishedTake = page.Take;
                                page.Published = true;
                                page.NotifyPending = true;
                            }
                        }
                        var previous = _read;
                        _read = read;
                        acquired = null;
                        previous?.Dispose();
                        foreach (var page in active.Where(page => page.NotifyPending))
                        {
                            page.NotifyPending = false;
                            page.Notify();
                        }
                        CompleteWaiting();
                        if (version == _version) break;
                    }
                    catch
                    {
                        ReleaseTokens();
                        throw;
                    }
                }
                finally { acquired?.Dispose(); }
            }
        }
        finally
        {
            _readCancellation = null;
            _running = false;
            CompleteWaiting();
        }
    }

    private IReadOnlyDictionary<string, AssessmentWordResult> ResolveAssessments(TextLineSlice slice) =>
        slice.Assessments.ToDictionary(pair => pair.Key, pair =>
        {
            var facts = pair.Value;
            return _assessments.TryGetValue(pair.Key, out var result) &&
                result.Origin?.AssessmentId == facts.AssessmentId ? result :
                new AssessmentWordResult(pair.Key, facts.Outcome, facts.IsIncomplete, string.Empty, null, null)
                {
                    Morphology = facts.Morphology,
                    ReadingGrades = facts.ReadingGrades,
                    ProjectStanding = facts.ProjectStanding,
                    AnalysisComparison = facts.AnalysisComparison,
                    Origin = facts.Origin,
                    OccurrenceCount = facts.OccurrenceCount,
                    MissedApproved = facts.MissedApproved,
                };
        }, StringComparer.Ordinal);

    private void CompleteWaiting()
    {
        foreach (var waiter in _waiting)
            waiter.Completion.TrySetResult(waiter.Page.Active && waiter.Page.Published &&
                waiter.Page.Offset == waiter.Offset ? waiter.Page.Line?.Tokens ?? [] : []);
        _waiting.Clear();
    }

    private void ReleaseTokens()
    {
        ReleasingTokens?.Invoke();
        foreach (var page in _pages.Values)
        {
            page.Line?.ReleasePage();
            page.Published = false;
        }
        _read?.Dispose();
        _read = null;
    }

    private void Release(LinePage page)
    {
        if (!page.Active) return;
        page.Active = false;
        if (page.Published) return;
        Changed();
        Schedule();
    }

    private void MakeTokenRoom(LinePage requested, int take)
    {
        var retained = _pages.Values.Sum(page => page.Line?.Tokens.Count ?? 0);
        var extra = take - (requested.Line?.Tokens.Count ?? 0);
        foreach (var page in _pages.Values.Where(page => !page.Active && page.Published &&
                     !ReferenceEquals(page, requested)).ToArray())
        {
            if (retained + extra <= 512) break;
            retained -= page.Line?.Tokens.Count ?? 0;
            _lines.Remove(page.Header);
        }
        if (retained + extra > 512) throw new InvalidOperationException("The displayed pages exceed 512 source tokens.");
    }

    public void Clear()
    {
        _visible = false;
        Changed();
        ReleaseTokens();
        _lines.Clear();
        CompleteWaiting();
    }

    public void Show()
    {
        if (!_stopped) _visible = true;
    }

    public async Task StopAsync()
    {
        _stopped = true;
        _lifetime.Cancel();
        Clear();
        await _pending.ConfigureAwait(true);
        _pending = Task.CompletedTask;
    }

    private sealed record WaitingPage(LinePage Page, int Offset,
        TaskCompletionSource<IReadOnlyList<object>> Completion);

    private sealed class LinePage(SelectionLinePages owner, SelectionLineHeader header) :
        IProgressivePageSource, INotifyCollectionChanged
    {
        public SelectionLineHeader Header { get; } = header;
        public ResultsLineViewModel? Line => owner._pages.GetValueOrDefault(Header.Number) == this
            ? owner._displayedLines.GetValueOrDefault(Header.Number) : null;
        public bool Active { get; set; }
        public bool Published { get; set; }
        public long TokenGeneration { get; set; }
        public int PublishedOffset { get; set; }
        public int PublishedTake { get; set; }
        public bool NotifyPending { get; set; }
        public int Offset { get; set; }
        public int Take { get; set; }
        public int Count => !owner._stopped && owner._visible && owner._pages.TryGetValue(Header.Number, out var current) &&
            ReferenceEquals(current, this) ? Header.TokenCount : 0;
        public int InitialOffset => Offset;
        public int IndexOf(object item)
        {
            if (item is SelectionTokenPosition position && ReferenceEquals(position.Owner, owner) &&
                position.LineNumber == Header.Number) return position.Offset;
            var tokens = Line?.Tokens ?? [];
            for (var index = 0; index < tokens.Count; index++)
                if (ReferenceEquals(tokens[index], item)) return Offset + index;
            return -1;
        }
        public event NotifyCollectionChangedEventHandler? CollectionChanged;
        public void Notify() => CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(
            NotifyCollectionChangedAction.Replace, Line?.Tokens.ToArray() ?? [], Line?.Tokens.ToArray() ?? [], 0));
        public async Task<IReadOnlyList<object>> ReadPageAsync(int offset, int count, CancellationToken cancellationToken)
        {
            var tokens = await owner.ReadAsync(this, offset, count, cancellationToken).ConfigureAwait(true);
            return Enumerable.Range(0, tokens.Count).Select(index =>
                (object)new SelectionTokenPosition(owner, Header.Number, offset + index, TokenGeneration)).ToArray();
        }
        public void ReleasePage() => owner.Release(this);
    }
}

/// <summary>A source position whose current display model belongs to the leased page, not its recycled container.</summary>
internal sealed class SelectionTokenPosition(SelectionLinePages owner, int lineNumber, int offset, long version)
{
    internal SelectionLinePages Owner { get; } = owner;
    public int LineNumber { get; } = lineNumber;
    public int Offset { get; } = offset;
    public ResultsTokenViewModel? Model => Owner.TokenAt(LineNumber, Offset, version);
}
