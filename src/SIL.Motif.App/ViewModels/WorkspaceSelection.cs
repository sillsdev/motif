using CommunityToolkit.Mvvm.ComponentModel;
using SIL.Motif.App.Services;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Commands.SelectionReading;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.PanGloss;

namespace SIL.Motif.App.ViewModels;

/// <summary>Owns the window's Selection reader and its compact summary until that Selection is replaced.</summary>
public sealed class WorkspaceSelection : ObservableObject
{
    private readonly ICommandClient _commands;
    private CancellationTokenSource? _cancellation;
    private Task _pending = Task.CompletedTask;
    private long _generation;
    private SelectionRead<SelectionSummary>? _summaryRead;
    private SelectionReader? _reader;
    private Refusal? _refusal;
    private bool _isLoading;
    private object? _cardOwner;
    private Action? _closeCard;
    private string? _listSha256;
    private IReadOnlyDictionary<string, int>? _listCounts;
    private SelectionReader? _listReader;
    private CancellationTokenSource? _listRead;
    private Task _listPending = Task.CompletedTask;

    public WorkspaceSelection(ICommandClient commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        _commands = commands;
    }

    /// <summary>
    /// Whether this holder reads and keeps the saved word list's counts. The Proposal lens leaves it off, because
    /// nothing there shows them and the project's own holder already keeps the one list.
    /// </summary>
    internal bool HoldsListCounts { get; init; } = true;

    /// <summary>The current reader; pages borrow it and release their detail leases before replacement.</summary>
    public SelectionReader? Reader => _reader;

    internal SavedProjectNavigation? Navigation { get; private set; }
    internal Task Pending => _pending;
    internal long Generation => _generation;

    /// <summary>Shared counts and word identities without display lines, occurrences or morphology.</summary>
    public SelectionSummary? Summary => _summaryRead?.Value;

    /// <summary>
    /// The counts of the saved word list the current Selection's words came from, each word in NFD, or
    /// <see langword="null"/> while they are being read, when the list cannot be read, or when the held counts are for
    /// another list. One list is held for the open project, by the hash the summary names; it is not a parser cache.
    /// </summary>
    internal IReadOnlyDictionary<string, int>? ListCounts =>
        Summary?.WordListSha256 is { } sha && sha == _listSha256 ? _listCounts : null;

    /// <summary>Finishes when the word list now being read has been held or given up.</summary>
    internal Task ListCountsPending => _listPending;

    /// <summary>How many times a saved word list has been read since the window started, each read once.</summary>
    internal int ListReadCount { get; private set; }

    /// <summary>How many words' counts are held now, whichever Selection is shown; zero when none are.</summary>
    internal int HeldListWordCount => _listCounts?.Count ?? 0;

    /// <summary>The count the word list gives a form, or <see langword="null"/> when it gives none or none is held.</summary>
    internal int? ListCountOf(string form) =>
        ListCounts is { } counts && counts.TryGetValue(form, out var count) ? count : null;

    /// <summary>Why the current Selection could not be read.</summary>
    public Refusal? Refusal => _refusal;

    /// <summary>Whether the latest requested Selection is still being opened or summarized.</summary>
    public bool IsLoading => _isLoading;

    /// <summary>Notifies model owners to release registrations and detail before the old reader is disposed.</summary>
    internal event Action? Replacing;

    /// <summary>
    /// Whether the current <see cref="Replacing"/> is a reload that will publish a successor, so a reader may hold
    /// what it shows until then; false when the Selection is being cleared or stopped.
    /// </summary>
    internal bool IsReplacingForReload { get; private set; }

    /// <summary>Registers a realized summary row; its model owner must dispose the registration on release.</summary>
    internal IDisposable RegisterRow() => (_summaryRead ?? throw new InvalidOperationException(
        "There is no Selection summary to display.")).Lease.RegisterModel(SelectionModelKind.Row);

    internal IDisposable RegisterLine() => (_summaryRead ?? throw new InvalidOperationException(
        "There is no Selection summary to display.")).Lease.RegisterModel(SelectionModelKind.Line);

    internal IDisposable RegisterCard(object owner, Action close)
    {
        var previous = _closeCard;
        _closeCard = null;
        _cardOwner = null;
        previous?.Invoke();
        var registration = (_summaryRead ?? throw new InvalidOperationException(
            "There is no Selection summary to display.")).Lease.RegisterModel(SelectionModelKind.Card);
        _cardOwner = owner;
        _closeCard = close;
        return new CardOwnership(() =>
        {
            registration.Dispose();
            if (!ReferenceEquals(_cardOwner, owner)) return;
            _cardOwner = null;
            _closeCard = null;
        });
    }

    private sealed class CardOwnership(Action release) : IDisposable
    {
        private Action? _release = release;
        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }

    internal IDisposable RegisterCardToken() => (_summaryRead ?? throw new InvalidOperationException(
        "There is no Selection summary to display.")).Lease.RegisterModel(SelectionModelKind.Token, SelectionModelUse.Pinned);

    /// <summary>Replaces the reader with exactly the requested Selection and named evidence.</summary>
    public Task ReloadAsync(string projectPath, IReadOnlyList<Guid> textIds, IReadOnlyList<string> addedWords,
        CurrentEvidenceSnapshot? evidence = null, CancellationToken cancellationToken = default,
        EvidenceSet? shownAssessment = null)
    {
        var generation = ++_generation;
        _isLoading = true;
        OnPropertyChanged(nameof(IsLoading));
        Cancel(_cancellation);
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _cancellation = cancellation;
        var request = new OpenSelectionReaderRequest(projectPath, textIds.ToArray(), addedWords.ToArray())
        {
            EvidenceSnapshot = evidence,
            ShownAssessment = shownAssessment is null ? null : shownAssessment with
            {
                ReplacementAssessmentIds = Array.AsReadOnly(shownAssessment.ReplacementAssessmentIds.ToArray()),
                MeasurementIds = new Dictionary<string, string>(shownAssessment.MeasurementIds, StringComparer.Ordinal),
                MeasuredWords = Array.AsReadOnly(shownAssessment.MeasuredWords.ToArray()),
            },
        };
        var previous = _pending;
        var current = ReloadCoreAsync(request, generation, cancellation);
        _pending = previous.IsCompleted ? current : Task.WhenAll(previous, current);
        return current;
    }

    private async Task ReloadCoreAsync(OpenSelectionReaderRequest request, long generation,
        CancellationTokenSource cancellation)
    {
        SelectionReader? opened = null;
        SelectionRead<SelectionSummary>? summary = null;
        try
        {
            var previous = ReleaseCurrent(reloading: true);
            if (previous is not null) await previous.DisposeAsync().ConfigureAwait(true);
            if (generation != _generation || cancellation.IsCancellationRequested) return;
            var outcome = await _commands.OpenSelectionReaderAsync(request, cancellation.Token).ConfigureAwait(true);
            opened = outcome.Value;
            if (generation != _generation || cancellation.IsCancellationRequested) return;
            if (!outcome.Succeeded)
            {
                _refusal = outcome.Refusal;
                Notify();
                return;
            }
            var read = await opened!.ReadSummaryAsync(new SelectionViewRequest(), cancellation.Token)
                .ConfigureAwait(true);
            summary = read.Value;
            if (generation != _generation || cancellation.IsCancellationRequested) return;
            if (!read.Succeeded)
            {
                _refusal = read.Refusal;
                Notify();
                return;
            }
            var navigation = await Task.Run(() => SavedProjectNavigation.Read(request.ProjectPath,
                opened.Context.Baseline.ProjectIdentity), cancellation.Token).ConfigureAwait(true);
            if (generation != _generation || cancellation.IsCancellationRequested) return;
            _reader = opened;
            Navigation = navigation;
            _summaryRead = summary;
            KeepListCounts(_reader, summary?.Value.WordListSha256);
            opened = null;
            summary = null;
            _refusal = null;
            Notify();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (generation != _generation || cancellation.IsCancellationRequested) return;
            _refusal = new Refusal("selection-reader.open-failed", FailureReason.Refused, exception.Message);
            Notify();
        }
        finally
        {
            summary?.Dispose();
            if (opened is not null) await opened.DisposeAsync().ConfigureAwait(true);
            if (ReferenceEquals(_cancellation, cancellation)) _cancellation = null;
            cancellation.Dispose();
            if (generation == _generation)
            {
                _isLoading = false;
                OnPropertyChanged(nameof(IsLoading));
            }
        }
    }

    // A list held, being read for this reader, or given up for this hash is left alone; any other is read once.
    private void KeepListCounts(SelectionReader? reader, string? sha256)
    {
        if (!HoldsListCounts || reader is null || sha256 is null)
        {
            DropListCounts();
            return;
        }
        if (sha256 == _listSha256 && (_listRead is null || ReferenceEquals(_listReader, reader))) return;
        DropListCounts();
        _listSha256 = sha256;
        _listReader = reader;
        var read = _listRead = new CancellationTokenSource();
        ListReadCount++;
        _listPending = ReadListCountsAsync(reader, sha256, read);
    }

    private async Task ReadListCountsAsync(SelectionReader reader, string sha256, CancellationTokenSource read)
    {
        IReadOnlyDictionary<string, int>? counts = null;
        var failed = false;
        try { counts = await reader.ReadWordListCountsAsync(sha256, read.Token).ConfigureAwait(true); }
        catch (OperationCanceledException) { }
        catch (Exception) { failed = true; }
        finally { read.Dispose(); }
        if (!ReferenceEquals(_listRead, read)) return;
        _listRead = null;
        _listReader = null;
        // A read that outlived its reader or failed proves nothing about the list, so the next publish reads it again.
        if (failed || !ReferenceEquals(_reader, reader))
        {
            _listSha256 = null;
            return;
        }
        _listCounts = counts;
        OnPropertyChanged(nameof(ListCounts));
    }

    private void DropListCounts()
    {
        Cancel(_listRead);
        _listRead = null;
        _listReader = null;
        _listSha256 = null;
        if (_listCounts is null) return;
        _listCounts = null;
        OnPropertyChanged(nameof(ListCounts));
    }

    /// <summary>Immediately removes the old Selection; outstanding disposal remains part of the stop barrier.</summary>
    internal void Clear()
    {
        DropListCounts();
        ++_generation;
        _isLoading = false;
        Cancel(_cancellation);
        var previous = ReleaseCurrent();
        if (previous is not null)
        {
            previous.Dispose();
            _pending = Task.WhenAll(_pending, previous.DisposeAsync().AsTask());
        }
    }

    /// <summary>Cancels replacement and waits for all superseded opens before releasing the current reader.</summary>
    public async Task StopAsync()
    {
        DropListCounts();
        ++_generation;
        _isLoading = false;
        Cancel(_cancellation);
        try { await _pending.ConfigureAwait(true); }
        finally
        {
            var previous = ReleaseCurrent();
            if (previous is not null) await previous.DisposeAsync().ConfigureAwait(true);
        }
    }

    private SelectionReader? ReleaseCurrent(bool reloading = false)
    {
        IsReplacingForReload = reloading;
        try { Replacing?.Invoke(); }
        finally { IsReplacingForReload = false; }
        var previous = _reader;
        _reader = null;
        Navigation = null;
        _summaryRead?.Dispose();
        _summaryRead = null;
        _refusal = null;
        Notify();
        return previous;
    }

    private void Notify()
    {
        OnPropertyChanged(nameof(Reader));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(Refusal));
        OnPropertyChanged(nameof(IsLoading));
    }

    private static void Cancel(CancellationTokenSource? cancellation)
    {
        try { cancellation?.Cancel(); }
        catch (ObjectDisposedException) { }
    }
}
