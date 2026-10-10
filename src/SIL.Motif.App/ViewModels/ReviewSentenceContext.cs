using SIL.Motif.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using SIL.Motif.Commands.SelectionReading;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.ViewModels;

/// <summary>Review's plain sentence strip, independently leased and without Analyze actions or token references.</summary>
internal sealed class ReviewSentenceContext : ObservableObject, IProgressivePageSource
{
    private readonly SelectionReader _reader;
    private readonly OccurrenceAnchor _occurrence;
    private SelectionRead<SelectionSentenceContext>? _read;
    private IReadOnlyList<ReviewSentenceToken> _tokens = [];
    private CancellationTokenSource? _cancellation;
    private Task _pending = Task.CompletedTask;
    private long _generation;
    private bool _stopped;
    private readonly object _identity = new();

    public ReviewSentenceContext(SelectionReader reader, OccurrenceAnchor occurrence)
    {
        _reader = reader;
        _occurrence = occurrence;
    }

    public int Count { get; private set; }
    public int InitialOffset { get; private set; }
    public IReadOnlyList<ReviewSentenceToken> Tokens => _tokens;
    public Refusal? Refusal { get; private set; }

    public int IndexOf(object item) => item is ReviewSentenceToken token &&
        ReferenceEquals(token.SourceIdentity, _identity) ? token.SourceOffset : -1;

    public Task<bool> OpenAsync(CancellationToken cancellationToken = default) =>
        LoadAsync(null, 20, cancellationToken);

    public async Task<IReadOnlyList<object>> ReadPageAsync(int offset, int count, CancellationToken cancellationToken)
    {
        if (_stopped) return [];
        if (_read?.Value.SourceTokenOffset == offset && _read.Value.Tokens.Count == Math.Min(count, Count - offset))
            return _tokens;
        return await LoadAsync(offset, count, cancellationToken).ConfigureAwait(true) ? _tokens : [];
    }

    private Task<bool> LoadAsync(int? offset, int count, CancellationToken cancellationToken)
    {
        var generation = ++_generation;
        Cancel(_cancellation);
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _cancellation = cancellation;
        var previous = _pending;
        var current = LoadCoreAsync(offset, count, generation, cancellation);
        _pending = previous.IsCompleted ? current : Task.WhenAll(previous, current);
        return current;
    }

    private async Task<bool> LoadCoreAsync(int? offset, int count, long generation,
        CancellationTokenSource cancellation)
    {
        SelectionRead<SelectionSentenceContext>? read = null;
        var tokens = new List<ReviewSentenceToken>();
        try
        {
            ReleaseCurrent();
            if (_stopped) return false;
            var outcome = await _reader.ReadSentenceAsync(_occurrence, offset, count, cancellation.Token)
                .ConfigureAwait(true);
            read = outcome.Value;
            if (_stopped || generation != _generation || cancellation.IsCancellationRequested) return false;
            Refusal = outcome.Refusal;
            if (!outcome.Succeeded) return false;
            for (var index = 0; index < read!.Value.Tokens.Count; index++)
                tokens.Add(new ReviewSentenceToken(read.Value.Tokens[index], read.Value.SourceTokenOffset + index,
                    read.Lease, _identity, read.Value.SentenceStyle));
            Count = read.Value.SourceTokenCount;
            InitialOffset = read.Value.SourceTokenOffset;
            _read = read;
            _tokens = tokens.ToArray();
            read = null;
            tokens.Clear();
            OnPropertyChanged(nameof(Tokens));
            OnPropertyChanged(nameof(Refusal));
            return true;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            return false;
        }
        finally
        {
            foreach (var token in tokens) token.Dispose();
            read?.Dispose();
            if (ReferenceEquals(_cancellation, cancellation)) _cancellation = null;
            cancellation.Dispose();
        }
    }

    public void ReleasePage()
    {
        ++_generation;
        Cancel(_cancellation);
        ReleaseCurrent();
        OnPropertyChanged(nameof(Tokens));
    }

    public async Task StopAsync()
    {
        _stopped = true;
        ReleasePage();
        await _pending.ConfigureAwait(true);
    }

    private void ReleaseCurrent()
    {
        foreach (var token in _tokens) token.Dispose();
        _tokens = [];
        _read?.Dispose();
        _read = null;
    }

    private static void Cancel(CancellationTokenSource? cancellation)
    {
        try { cancellation?.Cancel(); }
        catch (ObjectDisposedException) { }
    }
}

/// <summary>A plain captured token displayed by Review, owning exactly one explicit model pin.</summary>
public sealed class ReviewSentenceToken : IDisposable
{
    private IDisposable? _registration;

    internal ReviewSentenceToken(SelectionSentenceToken source, int offset, SelectionReadLease lease,
        object identity, string sentenceStyle)
    {
        Text = source.Text;
        TextWritingSystem = source.TextWritingSystem;
        SentenceStyle = sentenceStyle;
        Form = source.Form;
        Occurrence = source.Occurrence;
        SourceOffset = offset;
        SourceIdentity = identity;
        _registration = lease.RegisterModel(SelectionModelKind.Token, SelectionModelUse.Pinned);
    }

    public string Text { get; }
    public string? TextWritingSystem { get; }
    public string SentenceStyle { get; }
    public string? Form { get; }
    public OccurrenceAnchor? Occurrence { get; }
    internal int SourceOffset { get; }
    internal object SourceIdentity { get; }

    /// <inheritdoc />
    public void Dispose() => Interlocked.Exchange(ref _registration, null)?.Dispose();
}
