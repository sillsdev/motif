using CommunityToolkit.Mvvm.ComponentModel;
using SIL.Motif.Commands.SelectionReading;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.PanGloss;

namespace SIL.Motif.App.ViewModels;

/// <summary>Owns the displayed lines and tokens for one bounded Text range, together with their reader lease.</summary>
internal sealed class SelectionTextViewport : ObservableObject
{
    private SelectionRead<TextLineSlice>? _read;
    private IReadOnlyList<ResultsLineViewModel> _lines = [];
    private TextLineRange? _range;
    private CancellationTokenSource? _cancellation;
    private Task _pending = Task.CompletedTask;
    private long _generation;
    private Refusal? _refusal;

    public IReadOnlyList<ResultsLineViewModel> Lines => _lines;
    public TextLineSlice? Slice => _read?.Value;
    public Refusal? Refusal => _refusal;
    public SelectionReadContext? Context => _read?.Context;
    public event Action? Releasing;

    public Task ShowAsync(SelectionReader reader, SelectionTextSummary text, TextLineRange range,
        IReadOnlyDictionary<string, AssessmentWordResult>? assessments = null, SavedProjectNavigation? navigation = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(range);
        if (range.LineCount > 32 || range.TokenLimit > 512)
            throw new ArgumentOutOfRangeException(nameof(range), "A displayed range holds at most 32 lines and 512 tokens.");
        if (_read?.Context.Generation == reader.Diagnostics.Generation && _read.Value.TextId == text.TextId &&
            _range == range && _read.PresentationStamp == reader.Diagnostics.PresentationStamp &&
            !reader.Diagnostics.IsObsolete && !reader.Diagnostics.IsDisposed)
            return Task.CompletedTask;
        var generation = ++_generation;
        Cancel(_cancellation);
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _cancellation = cancellation;
        var previous = _pending;
        var current = ShowCoreAsync(reader, text, range, assessments, navigation, generation, cancellation);
        _pending = previous.IsCompleted ? current : Task.WhenAll(previous, current);
        return current;
    }

    private async Task ShowCoreAsync(SelectionReader reader, SelectionTextSummary text, TextLineRange range,
        IReadOnlyDictionary<string, AssessmentWordResult>? assessments, SavedProjectNavigation? navigation,
        long generation, CancellationTokenSource cancellation)
    {
        SelectionRead<TextLineSlice>? read = null;
        var models = new List<ResultsLineViewModel>();
        try
        {
            ClearRange();
            var outcome = await reader.ReadLinesAsync(text.TextId, range, cancellation.Token).ConfigureAwait(true);
            read = outcome.Value;
            if (generation != _generation || cancellation.IsCancellationRequested) return;
            if (!outcome.Succeeded)
            {
                _refusal = outcome.Refusal;
                OnPropertyChanged(nameof(Refusal));
                return;
            }
            var records = SelectionDisplayProjection.Lines(read!.Value, navigation);
            for (var index = 0; index < records.Count; index++)
                models.Add(new ResultsLineViewModel(text.Title, records[index], assessments ?? EmptyAssessments,
                    textId: text.TextId, lease: read.Lease, capturedSentence: read.Value.Lines[index].Sentence,
                    readContext: read.Context, assessmentFacts: read.Value.Assessments));
            _read = read;
            _lines = models.ToArray();
            _range = range;
            read = null;
            models.Clear();
            Notify();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        finally
        {
            foreach (var model in models) model.Dispose();
            read?.Dispose();
            if (ReferenceEquals(_cancellation, cancellation)) _cancellation = null;
            cancellation.Dispose();
        }
    }

    public void Clear()
    {
        ++_generation;
        Cancel(_cancellation);
        ClearRange();
    }

    public async Task StopAsync()
    {
        Clear();
        await _pending.ConfigureAwait(true);
    }

    private void ClearRange()
    {
        Releasing?.Invoke();
        foreach (var line in _lines) line.Dispose();
        _lines = [];
        _read?.Dispose();
        _read = null;
        _range = null;
        _refusal = null;
        Notify();
    }

    private void Notify()
    {
        OnPropertyChanged(nameof(Lines));
        OnPropertyChanged(nameof(Slice));
        OnPropertyChanged(nameof(Context));
        OnPropertyChanged(nameof(Refusal));
    }

    private static void Cancel(CancellationTokenSource? cancellation)
    {
        try { cancellation?.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    private static readonly IReadOnlyDictionary<string, AssessmentWordResult> EmptyAssessments =
        new Dictionary<string, AssessmentWordResult>(StringComparer.Ordinal);
}
