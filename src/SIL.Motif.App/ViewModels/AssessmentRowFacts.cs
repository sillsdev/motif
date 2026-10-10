using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.ViewModels;

internal sealed record AssessmentRowFacts(AssessmentWordResult Source, int? OccurrenceCount)
{
    public string Word => Source.Word;
    public WordComparison Comparison { get; } = CompareSemantics.Compare(Source);
    public bool StoppedAtALimit => CompareSemantics.StoppedAtLimit(Source.Outcome, Source.IsIncomplete, Source.Morphology);
    public bool IsRefused => Comparison.MeaningCode == "refused";
    public bool IsParsed => !IsRefused && Source.Outcome == "analysed";
    public bool IsFailed => !IsRefused && Source.Outcome == "no-analysis";
    public string Result => IsRefused ? ParserRefusals.Title : Source.Outcome switch
    {
        "analysed" => "Parsed",
        "no-analysis" => "No parse",
        "timed-out" => "Time limit",
        "capped" => "Search limit",
        "skipped" => ParserRefusals.Title,
        var outcome => outcome,
    };
    public string VsProject => Source.MissedApproved is { Count: > 0 }
        ? IsRefused ? ParserRefusals.Title : Source.IsIncomplete ? "Not reached" : "Missed"
        : Source.ReadingGrades?.Contains(ReadingGrade.Disapproved) == true ? "Disapproved"
        : Source.ReadingGrades?.Contains(ReadingGrade.Approved) == true ? "Approved"
        : Source.ReadingGrades?.Contains(ReadingGrade.Candidate) == true ? "Unknown"
        : (Source.Readings?.Count ?? Source.Morphology?.Analyses.Count ?? 0) > 0 && Source.ReadingGrades is not null
            ? ReadingGradeLabels.NotPresent : "—";
    public (WordProjectStatus Row, CompareColumnKind Column) Place
    {
        get
        {
            var placement = CompareSemantics.PlacementOf(Comparison);
            return (WordProjectStatuses.FromStanding(placement.Standing), placement.Column);
        }
    }
    public WordProjectStatus Row => Place.Row;
    public CompareColumnKind Column => Place.Column;
    public WordProjectStatus? Standing => Source.ProjectStanding is { } standing
        ? WordProjectStatuses.FromStanding(standing) : null;
    public int? Occurrences => OccurrenceCount;
    public int? ElapsedMs => Source.ElapsedMs;
    public FixFirstPriority? FixFirst => Source.FixFirst;
    public int ReadingCount => Source.Morphology?.Analyses.Count ?? 0;
    public bool IsChecked { get; set; }
    public PendingChangeState PendingState { get; set; }
    public string? StagedOpinionText { get; set; }

    public WordChangeAction CaptureMatrixAction(string kind, WordActionTarget target) => new(target, kind,
        kind == ChangeKinds.AddCandidate ? (Source.Morphology?.Analyses ?? []).Select((analysis, index) =>
            new WordActionReading(analysis, $"Analysis {index + 1}: {ReadingText(index)}", index)).ToArray() : [], true);

    private string ReadingText(int index)
    {
        if (Source.Readings is { } readings)
            return index < readings.Count ? string.Join(" + ", readings[index].Morphs.Select(morph => morph.Form))
                + " = " + string.Join(" + ", readings[index].Morphs.Select(morph => morph.Gloss.Length == 0 ? "?" : morph.Gloss))
                : "Unresolved";
        var morphs = Source.Morphology!.Analyses[index].Morphs;
        return string.Join(" + ", morphs.Select(morph => morph.GuessedString ?? "?")) + " = " +
            string.Join(" + ", morphs.Select(_ => "?"));
    }
}

// Metadata survives scrolling; only display models participate in this bounded lifetime.
internal sealed class DisplayProjectionCache<TSource, TModel>(Func<TSource, TModel> create, Action<TModel> release,
    Func<TModel, bool>? isPinned = null, int capacity = 128) where TSource : class where TModel : class
{
    private readonly Dictionary<TSource, (TModel Model, LinkedListNode<TSource> Position)> _items =
        new(ReferenceEqualityComparer.Instance);
    private readonly LinkedList<TSource> _order = [];
    private event Action<WeakReference<object>>? Created;

    public IEnumerable<TModel> Models => _items.Values.Select(item => item.Model);
    public int Count => _items.Count;
    public IDisposable Observe(Action<WeakReference<object>> observer)
    {
        Created += observer;
        return new Observation(() => Created -= observer);
    }

    public TModel Get(TSource source)
    {
        if (_items.TryGetValue(source, out var cached))
        {
            _order.Remove(cached.Position);
            _order.AddLast(cached.Position);
            return cached.Model;
        }
        while (_items.Count >= capacity)
        {
            var candidate = _order.First;
            while (candidate is not null && isPinned?.Invoke(_items[candidate.Value].Model) == true)
                candidate = candidate.Next;
            if (candidate is null) throw new InvalidOperationException("Every cached display row is pinned.");
            var model = _items[candidate.Value].Model;
            _items.Remove(candidate.Value);
            _order.Remove(candidate);
            release(model);
        }
        var created = create(source);
        _items.Add(source, (created, _order.AddLast(source)));
        Created?.Invoke(new WeakReference<object>(created));
        return created;
    }

    public bool Remove(TSource source)
    {
        if (!_items.Remove(source, out var entry)) return false;
        _order.Remove(entry.Position);
        release(entry.Model);
        return true;
    }

    public void Clear()
    {
        foreach (var model in Models) release(model);
        _items.Clear();
        _order.Clear();
    }

    private sealed class Observation(Action stop) : IDisposable
    {
        private Action? _stop = stop;
        public void Dispose() => Interlocked.Exchange(ref _stop, null)?.Invoke();
    }
}
