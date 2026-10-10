using SIL.Motif.App.Services;
using SIL.Motif.Commands.SelectionReading;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.ViewModels;

/// <summary>Owns one displayed occurrence page for the open Word card, including its captured context.</summary>
internal sealed class SelectionWordOccurrences : IProgressivePageSource
{
    private readonly SelectionReader _reader;
    private readonly TextWordKey _word;
    private readonly IReadOnlyDictionary<Guid, SelectionTextSummary> _texts;
    private readonly Action<IReadOnlyList<WordOccurrenceRowViewModel>> _publish;
    private SelectionRead<WordOccurrences>? _read;
    private IReadOnlyList<WordOccurrenceRowViewModel> _rows = [];
    private CancellationTokenSource? _cancellation;
    private Task _pending = Task.CompletedTask;
    private long _generation;
    private bool _stopped;
    private readonly object _identity = new();

    public SelectionWordOccurrences(SelectionReader reader, SelectionWordSummary word,
        IReadOnlyList<SelectionTextSummary> texts, Action<IReadOnlyList<WordOccurrenceRowViewModel>> publish)
    {
        _reader = reader;
        _word = word.Key;
        Count = word.OccurrenceCount;
        _texts = texts.ToDictionary(text => text.TextId);
        _publish = publish;
    }

    public int Count { get; private set; }
    public int InitialOffset => 0;
    public Refusal? Refusal { get; private set; }

    public int IndexOf(object item) => item is WordOccurrenceRowViewModel row &&
        ReferenceEquals(row.SourceIdentity, _identity) ? row.SourceOffset : -1;

    public Task<IReadOnlyList<object>> OpenAsync() => ReadPageAsync(0, 20, CancellationToken.None);

    public Task<IReadOnlyList<object>> ReadPageAsync(int offset, int count, CancellationToken cancellationToken)
    {
        if (_stopped) return Task.FromResult<IReadOnlyList<object>>([]);
        if (count > 20) throw new ArgumentOutOfRangeException(nameof(count));
        if (_read?.Value.Start == offset && _rows.Count == Math.Min(count, Count - offset))
            return Task.FromResult<IReadOnlyList<object>>(_rows);
        var generation = ++_generation;
        Cancel(_cancellation);
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _cancellation = cancellation;
        var previous = _pending;
        var current = ReadCoreAsync(offset, count, generation, cancellation);
        _pending = previous.IsCompleted ? current : Task.WhenAll(previous, current);
        return current;
    }

    private async Task<IReadOnlyList<object>> ReadCoreAsync(int offset, int count, long generation,
        CancellationTokenSource cancellation)
    {
        SelectionRead<WordOccurrences>? read = null;
        var rows = new List<WordOccurrenceRowViewModel>();
        try
        {
            ReleaseCurrent();
            var outcome = await _reader.ReadOccurrencesAsync(_word, new OccurrenceRange(offset, count),
                cancellation.Token).ConfigureAwait(true);
            read = outcome.Value;
            if (_stopped || generation != _generation || cancellation.IsCancellationRequested) return [];
            Refusal = outcome.Refusal;
            if (!outcome.Succeeded) return [];
            for (var index = 0; index < read!.Value.Occurrences.Count; index++)
            {
                var item = read.Value.Occurrences[index];
                var text = _texts.GetValueOrDefault(item.Location.Anchor.TextId);
                rows.Add(new WordOccurrenceRowViewModel(new WordOccurrence(item.Location.Anchor.TextId,
                    text?.Title ?? string.Empty, item.Location.LineNumber, item.Sentence, item.Location.Status, null)
                {
                    SentenceStyle = item.SentenceStyle,
                    SentenceWritingSystem = item.SentenceWritingSystem,
                    TextTitleWritingSystem = text?.TitleWritingSystem,
                }, read.Lease.RegisterModel(SelectionModelKind.Line, SelectionModelUse.Pinned), _identity,
                    offset + index));
            }
            Count = read!.Value.TotalCount;
            _read = read;
            _rows = rows.ToArray();
            read = null;
            rows.Clear();
            _publish(_rows);
            return _rows;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            return [];
        }
        finally
        {
            foreach (var row in rows) row.Dispose();
            read?.Dispose();
            if (ReferenceEquals(_cancellation, cancellation)) _cancellation = null;
            cancellation.Dispose();
        }
    }

    public void ReleasePage()
    {
        ++_generation;
        Cancel(_cancellation);
        if (_pending.IsCompletedSuccessfully) _pending = Task.CompletedTask;
        ReleaseCurrent();
    }

    public async Task StopAsync()
    {
        _stopped = true;
        ReleasePage();
        await _pending.ConfigureAwait(true);
        _pending = Task.CompletedTask;
    }

    private void ReleaseCurrent()
    {
        foreach (var row in _rows) row.Dispose();
        _rows = [];
        _read?.Dispose();
        _read = null;
        _publish(_rows);
    }

    private static void Cancel(CancellationTokenSource? cancellation)
    {
        try { cancellation?.Cancel(); }
        catch (ObjectDisposedException) { }
    }
}
