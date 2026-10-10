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

    public WorkspaceSelection(ICommandClient commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        _commands = commands;
    }

    /// <summary>The current reader; pages borrow it and release their detail leases before replacement.</summary>
    public SelectionReader? Reader => _reader;

    internal SavedProjectNavigation? Navigation { get; private set; }
    internal Task Pending => _pending;
    internal long Generation => _generation;

    /// <summary>Shared counts and word identities without display lines, occurrences or morphology.</summary>
    public SelectionSummary? Summary => _summaryRead?.Value;

    /// <summary>Why the current Selection could not be read.</summary>
    public Refusal? Refusal => _refusal;

    /// <summary>Whether the latest requested Selection is still being opened or summarized.</summary>
    public bool IsLoading => _isLoading;

    /// <summary>Notifies model owners to release registrations and detail before the old reader is disposed.</summary>
    internal event Action? Replacing;

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
        SelectionAssessmentEvidence? shownAssessment = null)
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
                MeasurementAssessmentIds = Array.AsReadOnly(shownAssessment.MeasurementAssessmentIds.ToArray()),
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
            var previous = ReleaseCurrent();
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

    /// <summary>Immediately removes the old Selection; outstanding disposal remains part of the stop barrier.</summary>
    internal void Clear()
    {
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

    private SelectionReader? ReleaseCurrent()
    {
        Replacing?.Invoke();
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
