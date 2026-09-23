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
        ? (TotalCount == 1 ? "1 word" : $"{TotalCount} words")
        : $"{ShownCount} of {TotalCount} words match the filters";

    public int AllCount => _all.Count;

    /// <summary>The row for <paramref name="word"/> exactly as spelled, or <see langword="null"/> when this Assessment did not try it.</summary>
    public AssessWordRowViewModel? Find(string word) => _all.FirstOrDefault(row => row.Word == word);
    public int MissedCount => _all.Count(row => row.VsProject == "Missed");
    public int DisapprovedCount => _all.Count(row => row.VsProject == "Disapproved");
    public int NoOpinionCount => _all.Count(row => row.VsProject == "No opinion");
    public int NoParseCount => _all.Count(row => row.Result == "No parse");
    public int LimitCount => _all.Count(row => row.StoppedAtALimit);

    /// <summary>Whether enough words stopped at a limit that the limit, not the grammar, may be what failed them.</summary>
    public bool ManyStoppedAtALimit => AllCount > 0 && LimitCount >= 10 && LimitCount * 10 >= AllCount;

    public int SkippedCount => _all.Count(row => row.Result == "Skipped");

    public bool AnySkipped => SkippedCount > 0;

    public string SkippedHint => SkippedCount == 1
        ? "1 word was skipped: it has a character the grammar's character table does not define."
        : $"{SkippedCount:N0} words were skipped: each has a character the grammar's character table does not define.";

    public bool AnyStoppedAtALimit => LimitCount > 0;

    public string LimitHint => LimitCount == 1
        ? "1 word stopped at a limit before it finished. Given longer, it may parse."
        : $"{LimitCount:N0} words stopped at a limit before they finished. Given longer, some may parse.";

    /// <summary>
    /// What the parser came to, word by word, as parts that add up to <see cref="AllCount"/>: a word that stopped at
    /// a limit counts there even if it found readings first, since its search did not finish.
    /// </summary>
    public IReadOnlyList<OutcomeSegment> Outcomes { get; private set; } = [];

    /// <summary>Every word of the Assessment, whatever the filters, for views that count the whole run.</summary>
    public IReadOnlyList<AssessWordRowViewModel> AllRows => _all;

    /// <summary>
    /// The same parts as <see cref="Outcomes"/> in one line, counted in words rather than searches, such as
    /// "209 words: 130 parsed, 22 no parse, 57 stopped at a limit". Empty until an Assessment is loaded.
    /// </summary>
    public string OutcomeSummary => TotalCount == 0
        ? string.Empty
        : $"{(TotalCount == 1 ? "1 word" : $"{TotalCount:N0} words")}: {string.Join(", ", Outcomes.Select(segment => $"{segment.CountText} {segment.Label}"))}";

    private IReadOnlyList<OutcomeSegment> CountOutcomes() =>
    [
        .. new[]
        {
            new OutcomeSegment(Verdict.Agrees, _all.Count(row => row.IsParsed && !row.StoppedAtALimit), "parsed"),
            new OutcomeSegment(Verdict.NoResult, _all.Count(row => row.IsFailed && !row.StoppedAtALimit), "no parse"),
            new OutcomeSegment(Verdict.Limit, _all.Count(row => row.StoppedAtALimit), "stopped at a limit"),
            new OutcomeSegment(Verdict.Several, _all.Count(row => row.Result == "Skipped" && !row.StoppedAtALimit), "skipped"),
        }.Where(segment => segment.Count > 0),
    ];

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
        Outcomes = CountOutcomes();
        OnPropertyChanged(nameof(Outcomes));
        OnPropertyChanged(nameof(OutcomeSummary));
        OnPropertyChanged(nameof(ManyStoppedAtALimit));
        OnPropertyChanged(nameof(AnyStoppedAtALimit));
        OnPropertyChanged(nameof(SkippedCount));
        OnPropertyChanged(nameof(AnySkipped));
        OnPropertyChanged(nameof(SkippedHint));
        OnPropertyChanged(nameof(LimitHint));
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
            ResultsWordFilter.Limit => row.StoppedAtALimit,
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
        Standing = word.ProjectStanding is { } standing ? WordProjectStatuses.FromStanding(standing) : null;
        ReadingCount = word.Morphology?.Analyses.Count ?? 0;
        Correctness = word.Correctness is { Expected: > 0 } correctness
            ? $"{correctness.Matched} of {correctness.Expected} approved analyses produced"
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
            .Select((reading, index) => new ParserReadingViewModel(index + 1, reading,
                Result == "Skipped" ? "not-tried" : IsIncomplete ? "not-reached" : "missed"))
            .ToArray();

        // A word never tried, or stopped at a limit, may still have the approved analysis: neither is a miss.
        VsProject = MissedApproved.Count > 0 ? (Result == "Skipped" ? "Not tried" : IsIncomplete ? "Not reached" : "Missed")
            : Readings.Any(reading => reading.Grade == "disapproved") ? "Disapproved"
            : Readings.Any(reading => reading.Grade == "approved") ? "Approved"
            : Readings.Any(reading => reading.Grade == "candidate") ? "Candidate"
            : Readings.Count > 0 && grades is not null ? "No opinion"
            : "—";

        TryWordLink = word.TryWordLink is { } link ? new Uri(link) : null;
        Unavailable = word.Correctness?.Unavailable ?? word.Morphology?.Unavailable ?? [];
        Detail = $"{word.CompletionStatus}. {word.EvidenceStatus}";
        CompletionStatus = word.CompletionStatus;
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

    /// <summary>What the project held for this word when it was assessed, or <see langword="null"/> if it was not read.</summary>
    public WordProjectStatus? Standing { get; }

    /// <summary>The Assessment's own account of whether this word's search finished, and if not, what stopped it.</summary>
    public string CompletionStatus { get; }

    /// <summary>Whether a limit stopped the search, including a word that found readings before it stopped.</summary>
    public bool StoppedAtALimit => IsIncomplete || Result is "Time limit" or "Step limit";
    public int ReadingCount { get; }

    public string ReadingCountText => ReadingCount == 1 ? "1 reading" : $"{ReadingCount} readings";

    /// <summary>Why a word has no readings: a limit or a skip is not the parser finding no way to build it.</summary>
    public string NoReadingsText => Result switch
    {
        "Time limit" => "No readings: the parser stopped at its time limit before it could finish.",
        "Step limit" => "No readings: the parser stopped at its step limit before it could finish.",
        "Skipped" => "The parser did not try this word: it has a character the grammar's character table does not define.",
        _ => "No readings: the parser found no way to build this word.",
    };
    public string Correctness { get; }
    public int? ElapsedMs { get; }
    public int? Attempts { get; }
    public int? Passes { get; }

    /// <summary>Whether the parser needed more than its first pass; zero passes is the ordinary case and says nothing.</summary>
    public bool HasPasses => Passes is > 0;

    /// <summary>Grade against the project's own analyses: Approved, Disapproved, Candidate, No opinion, Missed, or none.</summary>
    public string VsProject { get; }

    /// <summary>Whether there is a comparison to show; a word with no readings and nothing missed has none.</summary>
    public bool HasVsProject => VsProject != "—";

    /// <summary>The shared meaning behind <see cref="VsProject"/>: a missed approved analysis is a no result.</summary>
    public Verdict Meaning => VsProject switch
    {
        "Approved" => Verdict.Agrees,
        "Disapproved" => Verdict.Differs,
        "Candidate" => Verdict.Candidate,
        "No opinion" => Verdict.New,
        "Missed" => Verdict.NoResult,
        _ => Verdict.Limit,
    };

    /// <summary>How long the parser took, or nothing for a word it never tried.</summary>
    public string ElapsedText => Result == "Skipped" || ElapsedMs is not { } ms ? string.Empty
        : ms == 0 ? "<1 ms" : $"{ms:N0} ms";

    /// <summary>The shared meaning behind <see cref="Result"/>: what the parser itself came to.</summary>
    public Verdict ResultMeaning => Result switch
    {
        "Parsed" => Verdict.Agrees,
        "No parse" => Verdict.NoResult,
        "Time limit" or "Step limit" or "Skipped" => Verdict.Limit,
        _ => Verdict.New,
    };

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
        IsMissed = grade is "missed" or "not-reached" or "not-tried";
        GradeLabel = grade switch
        {
            "approved" => "Project ✓",
            "disapproved" => "Disapproved",
            "missed" => "Missed",
            "not-reached" => "Not reached",
            "not-tried" => "Not tried",
            "candidate" => "Candidate",
            "no-opinion" => "No opinion",
            _ => string.Empty,
        };
    }

    public int Number { get; }
    public IReadOnlyList<ParserReadingMorphViewModel> Morphs { get; }

    /// <summary>The reading as <c>forms = glosses</c>, for copying and searching.</summary>
    public string Text { get; }

    public string? Grade { get; }

    /// <summary>The shared meaning behind <see cref="Grade"/>, so a reading is coloured like everything else.</summary>
    public Verdict Meaning => Grade switch
    {
        "approved" => Verdict.Agrees,
        "disapproved" => Verdict.Differs,
        "candidate" => Verdict.Candidate,
        "missed" => Verdict.NoResult,
        "not-reached" or "not-tried" => Verdict.Limit,
        _ => Verdict.New,
    };

    /// <summary>What a missed approved analysis means: not produced, or not reached before a limit.</summary>
    public string MissedExplanation => Grade switch
    {
        "not-reached" => "The project approves this analysis; the parser stopped at its limit before reaching it.",
        "not-tried" => "The project approves this analysis; the parser did not try this word.",
        _ => "The project approves this analysis; the parser did not produce it.",
    };

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
