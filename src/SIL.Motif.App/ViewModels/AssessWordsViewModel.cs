using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.ViewModels;

/// <summary>The Results Words list's filter chips: each is its own bucket, not a partition of the others.</summary>
public enum ResultsWordFilter
{
    All,
    Missed,
    Disapproved,
    NoOpinion,
    NoParse,
    Limit,
}

/// <summary>
/// The words one Assessment parsed, as a list: one row per word, filtered by a search box and one of the
/// Results stage's chips, all within what is already in memory.
/// </summary>
public sealed partial class AssessWordsViewModel : ObservableObject
{
    private readonly List<AssessWordRowViewModel> _all = [];

    public AssessWordsViewModel() => SetFilterCommand = new RelayCommand<ResultsWordFilter>(filter => SelectedFilter = filter);

    public ObservableCollection<AssessWordRowViewModel> Rows { get; } = [];

    /// <summary>Chooses one of the six filter chips, replacing whichever was chosen before.</summary>
    public IRelayCommand<ResultsWordFilter> SetFilterCommand { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAny))]
    [NotifyPropertyChangedFor(nameof(CountSummary))]
    [NotifyPropertyChangedFor(nameof(AllCount))]
    [NotifyPropertyChangedFor(nameof(MissedCount))]
    [NotifyPropertyChangedFor(nameof(DisapprovedCount))]
    [NotifyPropertyChangedFor(nameof(NoOpinionCount))]
    [NotifyPropertyChangedFor(nameof(NoParseCount))]
    [NotifyPropertyChangedFor(nameof(LimitCount))]
    private int _totalCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountSummary))]
    private int _shownCount;

    [ObservableProperty]
    private string _wordFilter = string.Empty;

    [ObservableProperty]
    private ResultsWordFilter _selectedFilter = ResultsWordFilter.All;

    [ObservableProperty]
    private AssessWordRowViewModel? _selectedRow;

    public bool HasAny => TotalCount > 0;

    public string CountSummary => ShownCount == TotalCount
        ? $"{TotalCount} word(s)"
        : $"{ShownCount} of {TotalCount} word(s) match the filters";

    public int AllCount => _all.Count;
    public int MissedCount => _all.Count(row => row.VsProject == "Missed");
    public int DisapprovedCount => _all.Count(row => row.VsProject == "Disapproved");
    public int NoOpinionCount => _all.Count(row => row.VsProject == "No opinion");
    public int NoParseCount => _all.Count(row => row.Result == "No parse");
    public int LimitCount => _all.Count(row => row.Result is "Time limit" or "Step limit");

    /// <summary>
    /// Replaces every row with <paramref name="words"/>, or clears the table for <see langword="null"/>.
    /// <paramref name="occurrenceCounts"/> looks up how many times a word's form occurs in the chosen Texts,
    /// when that Texts-stage data is available; a word not found there shows no occurrence count.
    /// </summary>
    public void Load(IReadOnlyList<AssessmentWordResult>? words, Func<string, int?>? occurrenceCounts = null)
    {
        _all.Clear();
        if (words is not null)
            _all.AddRange(words.Select(word => new AssessWordRowViewModel(word, occurrenceCounts?.Invoke(word.Word))));
        TotalCount = _all.Count;
        Refresh();
    }

    partial void OnWordFilterChanged(string value) => Refresh();
    partial void OnSelectedFilterChanged(ResultsWordFilter value) => Refresh();

    private void Refresh()
    {
        Rows.Clear();
        var matches = _all.Where(Matches);
        foreach (var row in matches) Rows.Add(row);
        ShownCount = Rows.Count;
        if (SelectedRow is null || !Rows.Contains(SelectedRow)) SelectedRow = Rows.FirstOrDefault();
    }

    private bool Matches(AssessWordRowViewModel row) =>
        (string.IsNullOrWhiteSpace(WordFilter) ||
            row.Word.Contains(WordFilter.Trim(), StringComparison.CurrentCultureIgnoreCase)) &&
        SelectedFilter switch
        {
            ResultsWordFilter.Missed => row.VsProject == "Missed",
            ResultsWordFilter.Disapproved => row.VsProject == "Disapproved",
            ResultsWordFilter.NoOpinion => row.VsProject == "No opinion",
            ResultsWordFilter.NoParse => row.Result == "No parse",
            ResultsWordFilter.Limit => row.Result is "Time limit" or "Step limit",
            _ => true,
        };
}

/// <summary>
/// One parsed word as the Results list and detail show it: a short result, the readings graded against
/// the project's own analyses, and the words the project approved that the parser never produced.
/// </summary>
public sealed class AssessWordRowViewModel
{
    public AssessWordRowViewModel(AssessmentWordResult word, int? occurrenceCount = null)
    {
        ArgumentNullException.ThrowIfNull(word);
        Word = word.Word;
        OccurrenceCount = occurrenceCount;
        Result = word.Outcome switch
        {
            "analysed" => "Parsed",
            "no-analysis" => "No parse",
            "timed-out" => "Time limit",
            "capped" => "Step limit",
            "skipped" => "Skipped",
            var other => other,
        };
        IsParsed = word.Outcome == "analysed";
        IsFailed = word.Outcome == "no-analysis";
        IsIncomplete = word.IsIncomplete;
        ReadingCount = word.Morphology?.Analyses.Count ?? 0;
        Correctness = word.Correctness is { Expected: > 0 } correctness
            ? $"{correctness.Matched}/{correctness.Expected} approved"
            : string.Empty;
        ElapsedMs = word.ElapsedMs;
        Attempts = word.Attempts;
        Passes = word.Passes;

        var grades = word.ReadingGrades;
        var readings = word.Readings ?? Unresolved(word.Morphology);
        Readings = readings.Select((reading, index) => new ParserReadingViewModel(
                index + 1, reading, grades is { Count: var count } && index < count ? grades[index] : null))
            .ToArray();
        ReadingText = string.Join(" | ", Readings.Select(reading => reading.Text));
        MissedApproved = (word.MissedApproved ?? [])
            .Select((reading, index) => new ParserReadingViewModel(index + 1, reading, "missed"))
            .ToArray();

        VsProject = MissedApproved.Count > 0 ? "Missed"
            : Readings.Any(reading => reading.Grade == "disapproved") ? "Disapproved"
            : Readings.Any(reading => reading.Grade == "approved") ? "Approved"
            : Readings.Count > 0 && grades is not null ? "No opinion"
            : "—";

        TryWordLink = word.TryWordLink is { } link ? new Uri(link) : null;
        Unavailable = word.Correctness?.Unavailable ?? word.Morphology?.Unavailable ?? [];
        Detail = $"{word.CompletionStatus}. {word.EvidenceStatus}";
    }

    // Readings nobody resolved against the project still show, by count and guessed form, rather than vanish.
    private static IReadOnlyList<ParserReading> Unresolved(ParseWordEvidence? evidence) =>
        (evidence?.Analyses ?? []).Select(analysis => new ParserReading(analysis.Morphs
            .Select(morph => new ParserReadingMorph(
                morph.GuessedString ?? "?", string.Empty, string.Empty, null, morph.GuessedString is not null, null))
            .ToArray())).ToArray();

    public string Word { get; }
    public int? OccurrenceCount { get; }
    public bool HasOccurrenceCount => OccurrenceCount is not null;
    public string Result { get; }
    public bool IsParsed { get; }
    public bool IsFailed { get; }
    public bool IsIncomplete { get; }
    public int ReadingCount { get; }
    public string Correctness { get; }
    public int? ElapsedMs { get; }
    public int? Attempts { get; }
    public int? Passes { get; }

    /// <summary>Grade against the project's own analyses: Approved, Disapproved, No opinion, Missed, or none available.</summary>
    public string VsProject { get; }

    public IReadOnlyList<ParserReadingViewModel> Readings { get; }
    public bool HasReadings => Readings.Count > 0;

    /// <summary>Approved analyses of this word the parser did not produce.</summary>
    public IReadOnlyList<ParserReadingViewModel> MissedApproved { get; }

    /// <summary>Every reading on one line, for sorting and searching by what the parser found.</summary>
    public string ReadingText { get; }

    public Uri? TryWordLink { get; }
    public bool HasTryWordLink => TryWordLink is not null;
    public IReadOnlyList<string> Unavailable { get; }
    public string Detail { get; }
}

/// <summary>One numbered reading of a word, as a row of interlinear morphs, graded against the project.</summary>
public sealed class ParserReadingViewModel
{
    public ParserReadingViewModel(int number, ParserReading reading, string? grade)
    {
        ArgumentNullException.ThrowIfNull(reading);
        Number = number;
        Morphs = reading.Morphs.Select(morph => new ParserReadingMorphViewModel(morph)).ToArray();
        Text = string.Join(" + ", Morphs.Select(morph => morph.Form)) + " = " +
               string.Join(" + ", Morphs.Select(morph => morph.Gloss.Length == 0 ? "?" : morph.Gloss));
        Grade = grade;
        IsApproved = grade == "approved";
        IsDisapproved = grade == "disapproved";
        IsMissed = grade == "missed";
        GradeLabel = grade switch
        {
            "approved" => "Project ✓",
            "disapproved" => "Disapproved",
            "missed" => "Missed",
            "no-opinion" => "No opinion",
            _ => string.Empty,
        };
    }

    public int Number { get; }
    public IReadOnlyList<ParserReadingMorphViewModel> Morphs { get; }

    /// <summary>The reading as <c>forms = glosses</c>, for copying and searching.</summary>
    public string Text { get; }

    public string? Grade { get; }
    public bool IsApproved { get; }
    public bool IsDisapproved { get; }
    public bool IsMissed { get; }
    public string GradeLabel { get; }

    /// <summary>Whether this row is one the parser actually produced, as opposed to a missed approved analysis.</summary>
    public bool ShowParserProduced => !IsMissed;

    /// <summary>Whether the project approves this reading's morphology.</summary>
    public bool ShowProjectApproved => IsApproved || IsMissed;
}

/// <summary>One morph of a reading: form over gloss over category, the form linking to its entry.</summary>
public sealed class ParserReadingMorphViewModel
{
    public ParserReadingMorphViewModel(ParserReadingMorph morph)
    {
        ArgumentNullException.ThrowIfNull(morph);
        Form = morph.Form;
        Gloss = morph.Gloss;
        Category = morph.InflectionType is { Length: > 0 } inflection
            ? $"{morph.Category} ({inflection})"
            : morph.Category;
        Guessed = morph.Guessed;
        Link = morph.FieldWorksLink is { } link ? new Uri(link) : null;
    }

    public string Form { get; }
    public string Gloss { get; }

    /// <summary>The gloss, or a placeholder a reader can still see and click when the project gives none.</summary>
    public string GlossOrPlaceholder => Gloss.Length == 0 ? "?" : Gloss;

    /// <summary>The accessible name of this morph's link into FieldWorks.</summary>
    public string LinkName => $"Open the entry for {Form} in FieldWorks";
    public string Category { get; }
    public bool Guessed { get; }
    public Uri? Link { get; }
    public bool HasLink => Link is not null;
    public bool HasNoLink => Link is null;
}
