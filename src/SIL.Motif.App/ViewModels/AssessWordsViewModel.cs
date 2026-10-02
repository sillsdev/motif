using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.PanGloss;

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
/// result filters, all within what is already in memory.
/// </summary>
public sealed partial class AssessWordsViewModel : ObservableObject
{
    private readonly List<AssessWordRowViewModel> _all = [];

    public AssessWordsViewModel() => SetFilterCommand = new RelayCommand<ResultsWordFilter>(filter => SelectedFilter = filter);

    /// <summary>Where every word row's next steps lead; the page that hosts the rows fills them in.</summary>
    public WordRowRoutes Routes { get; } = new();

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

    /// <summary>
    /// <paramref name="word"/> as another page lists it: its row and card from this parse, or a Not parsed row when
    /// the parse did not reach it. <paramref name="routes"/> replaces the row's next steps for that page only.
    /// </summary>
    public ListedWordViewModel Listed(string word, WordRowRoutes? routes = null) => Find(word) is { } found
        ? ListedWordViewModel.Of(found, routes)
        : new ListedWordViewModel(WordRowViewModel.NotParsed(word, routes: routes ?? Routes));
    public int MissedCount => _all.Count(row => row.VsProject == "Missed");
    public int DisapprovedCount => _all.Count(row => row.VsProject == "Disapproved");
    public int NoOpinionCount => _all.Count(row => row.VsProject == ReadingGradeLabels.NotPresent);
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
            new OutcomeSegment(Mark.Same, _all.Count(row => row.IsParsed && !row.StoppedAtALimit), "parsed"),
            new OutcomeSegment(Mark.NoParse, _all.Count(row => row.IsFailed && !row.StoppedAtALimit), "no parse"),
            new OutcomeSegment(Mark.Stopped, _all.Count(row => row.StoppedAtALimit), "stopped at a limit"),
            new OutcomeSegment(Mark.NotParsed, _all.Count(row => row.Result == "Skipped" && !row.StoppedAtALimit), "skipped"),
        }.Where(segment => segment.Count > 0),
    ];

    /// <summary>
    /// Replaces every row with <paramref name="words"/>, or clears the table for <see langword="null"/>.
    /// <paramref name="occurrenceCounts"/> looks up how many times a word's form occurs in the chosen Texts,
    /// when data for the selected Texts is available; a word not found there shows no occurrence count.
    /// </summary>
    public void Load(IReadOnlyList<AssessmentWordResult>? words, Func<string, int?>? occurrenceCounts = null)
    {
        _all.Clear();
        if (words is not null)
            _all.AddRange(words.Select(word => new AssessWordRowViewModel(word, occurrenceCounts?.Invoke(word.Word), Routes)));
        TotalCount = _all.Count;
        Outcomes = CountOutcomes();
        OnPropertyChanged(nameof(AllRows));
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

    /// <summary>
    /// Tells every word row whether it is unread: <paramref name="isUnread"/> answers for a word form, or gives
    /// <see langword="null"/> when no place the word occurs has a known read state.
    /// </summary>
    public void ApplyReadState(Func<string, bool?> isUnread)
    {
        ArgumentNullException.ThrowIfNull(isUnread);
        foreach (var row in _all) row.WordRow.IsUnread = isUnread(row.Word);
    }

    partial void OnWordFilterChanged(string value) => Refresh();
    partial void OnSelectedFilterChanged(ResultsWordFilter value) => Refresh();

    private IReadOnlySet<string>? _onlyWords;

    /// <summary>Lists only <paramref name="words"/>, the Compare matrix's chosen cells, on top of the chips and search.</summary>
    public void ShowOnly(IReadOnlySet<string>? words)
    {
        _onlyWords = words;
        Refresh();
    }

    private void Refresh()
    {
        Rows.Clear();
        var matches = _all.Where(Matches);
        foreach (var row in matches) Rows.Add(row);
        ShownCount = Rows.Count;
        if (SelectedRow is null || !Rows.Contains(SelectedRow)) SelectedRow = Rows.FirstOrDefault();
    }

    private bool Matches(AssessWordRowViewModel row) =>
        (_onlyWords is null || _onlyWords.Contains(row.Word)) &&
        (string.IsNullOrWhiteSpace(WordFilter) ||
            row.Word.Contains(WordFilter.Trim(), StringComparison.CurrentCultureIgnoreCase)) &&
        SelectedFilter switch
        {
            ResultsWordFilter.Missed => row.VsProject == "Missed",
            ResultsWordFilter.Disapproved => row.VsProject == "Disapproved",
            ResultsWordFilter.NoOpinion => row.VsProject == ReadingGradeLabels.NotPresent,
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
    public AssessWordRowViewModel(AssessmentWordResult word, int? occurrenceCount = null, WordRowRoutes? routes = null)
    {
        ArgumentNullException.ThrowIfNull(word);
        Source = word;
        Marking = AnalysisMarkingState.Create(word);
        Word = word.Word;
        Outcome = word.Outcome;
        Morphology = word.Morphology;
        FixFirst = word.FixFirst;
        OccurrenceCount = occurrenceCount ?? word.OccurrenceCount;
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
        ExpectedAnalysis = word.ExpectedAnalysis is { } expected
            ? new ParserReadingViewModel(1, expected, expected.StoredAnalysisOpinion) : null;

        // A word never tried, or stopped at a limit, may still have the approved analysis: neither is a miss.
        VsProject = MissedApproved.Count > 0 ? (Result == "Skipped" ? "Not tried" : IsIncomplete ? "Not reached" : "Missed")
            : Readings.Any(reading => reading.Grade == ReadingGrade.Disapproved) ? "Disapproved"
            : Readings.Any(reading => reading.Grade == ReadingGrade.Approved) ? "Approved"
            : Readings.Any(reading => reading.Grade == ReadingGrade.Candidate) ? "Unknown"
            : Readings.Count > 0 && grades is not null ? ReadingGradeLabels.NotPresent
            : "—";

        TryWordLink = word.TryWordLink is { } link ? new Uri(link) : null;
        Unavailable = word.Correctness?.Unavailable ?? word.Morphology?.Unavailable ?? [];
        Detail = $"{word.CompletionStatus}. {word.EvidenceStatus}";
        CompletionStatus = word.CompletionStatus;
        WordRow = new WordRowViewModel(WordRowProjection.Of(word, CompareViewModel.Place(this).Column,
            new WordRowFacts(Places: OccurrenceCount)), routes);
    }

    // Readings nobody resolved against the project still show, by count and guessed form, rather than vanish.
    private static IReadOnlyList<ParserReading> Unresolved(ParseWordEvidence? evidence) =>
        (evidence?.Analyses ?? []).Select(analysis => new ParserReading(analysis.Morphs
            .Select(morph => new ParserReadingMorph(
                morph.GuessedString ?? "?", string.Empty, string.Empty, null, morph.GuessedString is not null, null))
            .ToArray())).ToArray();

    public string Word { get; }

    /// <summary>The Assessment's result for the word, which identity queries such as shared morphemes read.</summary>
    public AssessmentWordResult Source { get; }
    public AnalysisMarkingState Marking { get; }
    public string Outcome { get; }
    public ParseWordEvidence? Morphology { get; }
    public FixFirstPriority? FixFirst { get; }
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
    public bool StoppedAtALimit => CompareSemantics.StoppedAtLimit(Outcome, IsIncomplete, Morphology);
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

    /// <summary>Grade against the project's own analyses: Approved, Disapproved, Candidate, Not present, Missed, or none.</summary>
    public string VsProject { get; }

    /// <summary>Whether there is a comparison to show; a word with no readings and nothing missed has none.</summary>
    public bool HasVsProject => VsProject != "—";

    /// <summary>What PanGloss built for this word against what FieldWorks holds: its Matrix column.</summary>
    public ParserOutcome ParserOutcome => WindowWords.OutcomeOf(CompareViewModel.Place(this).Column);

    /// <summary>How long the parser took, or nothing for a word it never tried.</summary>
    public string ElapsedText => Result == "Skipped" || ElapsedMs is not { } ms ? string.Empty
        : ms == 0 ? "<1 ms" : $"{ms:N0} ms";

    public IReadOnlyList<ParserReadingViewModel> Readings { get; }
    public bool HasReadings => Readings.Count > 0;

    /// <summary>Approved analyses of this word the parser did not produce.</summary>
    public IReadOnlyList<ParserReadingViewModel> MissedApproved { get; }

    /// <summary>The one stored project analysis selected as this word's expected analysis.</summary>
    public ParserReadingViewModel? ExpectedAnalysis { get; }

    /// <summary>Every reading on one line, for sorting and searching by what the parser found.</summary>
    public string ReadingText { get; }

    public Uri? TryWordLink { get; }
    public bool HasTryWordLink => TryWordLink is not null;

    /// <summary>The word as every page's word row shows it, placed where the Matrix places it.</summary>
    public WordRowViewModel WordRow { get; }
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
        StoredAnalysisId = reading.StoredAnalysisId;
        StoredAnalysisOpinion = reading.StoredAnalysisOpinion;
        Text = string.Join(" + ", Morphs.Select(morph => morph.Form)) + " = " +
               string.Join(" + ", Morphs.Select(morph => morph.Gloss.Length == 0 ? "?" : morph.Gloss));
        Grade = grade;
        IsApproved = grade == ReadingGrade.Approved;
        IsDisapproved = grade == ReadingGrade.Disapproved;
        IsMissed = grade is "missed" or "not-reached" or "not-tried";
        GradeLabel = grade switch
        {
            ReadingGrade.Approved => "Project ✓",
            "missed" => "Missed",
            "not-reached" => "Not reached",
            "not-tried" => "Not tried",
            _ => ReadingGradeLabels.Of(grade),
        };
    }

    public int Number { get; }
    public IReadOnlyList<ParserReadingMorphViewModel> Morphs { get; }

    /// <summary>The canonical ID of the stored project analysis, or <see langword="null"/> for parser output.</summary>
    public string? StoredAnalysisId { get; }

    /// <summary>The project's opinion of the stored analysis, or <see langword="null"/> when it is not known.</summary>
    public string? StoredAnalysisOpinion { get; }

    /// <summary>The reading as <c>forms = glosses</c>, for copying and searching.</summary>
    public string Text { get; }

    public string? Grade { get; }

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
    private const string UnknownForm = "unknown morpheme";
    private readonly string _glossPlaceholder = "?";
    private readonly string? _entry;
    private readonly string _toolName;

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
        _entry = morph.Entry;
        _toolName = FieldWorksLinks.ToolNameOf(morph.FieldWorksLink);
        AllomorphId = morph.AllomorphId;
        GrammaticalInfoId = morph.GrammaticalInfoId;
    }

    private ParserReadingMorphViewModel(ParserReadingMorph morph, string form, string glossPlaceholder) : this(morph)
    {
        Form = form;
        _glossPlaceholder = glossPlaceholder;
    }

    /// <summary>
    /// A morpheme from a parser trace, which may not know a morpheme's form or gloss: an unnamed one reads
    /// "unknown morpheme" and a missing gloss a dash, never a bare question mark.
    /// </summary>
    public static ParserReadingMorphViewModel ForTrace(ParserReadingMorph morph)
    {
        ArgumentNullException.ThrowIfNull(morph);
        var form = string.IsNullOrWhiteSpace(morph.Form) || morph.Form.Trim() == "?" ? UnknownForm : morph.Form;
        return new ParserReadingMorphViewModel(morph, form, "—");
    }

    public string Form { get; }
    public string Gloss { get; }

    /// <summary>The gloss, or a placeholder a reader can still see and click when the project gives none.</summary>
    public string GlossOrPlaceholder => Gloss.Length == 0 ? _glossPlaceholder : Gloss;

    /// <summary>The text of this morph's link into FieldWorks, naming the FieldWorks tool it opens.</summary>
    public string LinkText => $"{_toolName} ↗";

    /// <summary>The accessible name and tip of this morph's link: what opens, and in which FieldWorks tool.</summary>
    public string LinkName => _entry is { Length: > 0 } entry
        ? $"Open {entry} in {_toolName}"
        : $"Open the entry for {Form} in {_toolName}";
    /// <summary>The tip of a morph whose form is itself its link: which form opens, and in which FieldWorks tool.</summary>
    public string FormLinkTip => $"Open {Form} in {_toolName}";
    public string Category { get; }
    public bool Guessed { get; }
    public Uri? Link { get; }
    public bool HasLink => Link is not null;
    public bool HasNoLink => Link is null;

    /// <summary>The GUID of the allomorph this morph uses, or <see langword="null"/> when the reading names none.</summary>
    public string? AllomorphId { get; }

    /// <summary>The GUID of the grammatical info this morph uses, or <see langword="null"/> when the reading names none.</summary>
    public string? GrammaticalInfoId { get; }

    /// <summary>The morph as the inspector looks it up, by identity; <see langword="null"/> when the reading names no id.</summary>
    public InspectorSubject? InspectSubject => InspectorSubject.Morpheme(AllomorphId, GrammaticalInfoId, Form, Gloss, "authored");

    /// <summary>What the reading itself says about the morph, for the inspector to show beside FieldWorks' facts.</summary>
    public IReadOnlyList<InspectorDetail> Captured =>
        InspectorDetail.Recorded(("Form", Form), ("Gloss", Gloss), ("Category", Category));
}
