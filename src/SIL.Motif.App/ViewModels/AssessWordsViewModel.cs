using Avalonia.Collections;
using CommunityToolkit.Mvvm.ComponentModel;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.ViewModels;

/// <summary>
/// The words one Assessment parsed, as a table: one row per word, sortable by any column and filtered by a
/// separate search per column, all within what is already in memory.
/// </summary>
/// <remarks>
/// <see cref="Rows"/> is the grid's own collection view rather than a list rebuilt on each keystroke, so a
/// sort the person chose by clicking a header survives every change to a filter.
/// </remarks>
public sealed partial class AssessWordsViewModel : ObservableObject
{
    private readonly List<AssessWordRowViewModel> _all = [];

    public AssessWordsViewModel()
    {
        Rows = new DataGridCollectionView(_all) { Filter = Matches };
    }

    /// <summary>The rows on display: every word that satisfies all three column filters.</summary>
    public DataGridCollectionView Rows { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAny))]
    [NotifyPropertyChangedFor(nameof(CountSummary))]
    private int _totalCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountSummary))]
    private int _shownCount;

    [ObservableProperty]
    private string _wordFilter = string.Empty;

    [ObservableProperty]
    private string _resultFilter = string.Empty;

    [ObservableProperty]
    private string _readingFilter = string.Empty;

    public bool HasAny => TotalCount > 0;

    public string CountSummary => ShownCount == TotalCount
        ? $"{TotalCount} word(s)"
        : $"{ShownCount} of {TotalCount} word(s) match the filters";

    /// <summary>Replaces every row with <paramref name="words"/>, or clears the table for <see langword="null"/>.</summary>
    public void Load(IReadOnlyList<AssessmentWordResult>? words)
    {
        _all.Clear();
        if (words is not null) _all.AddRange(words.Select(word => new AssessWordRowViewModel(word)));
        TotalCount = _all.Count;
        Refresh();
    }

    partial void OnWordFilterChanged(string value) => Refresh();
    partial void OnResultFilterChanged(string value) => Refresh();
    partial void OnReadingFilterChanged(string value) => Refresh();

    private void Refresh()
    {
        Rows.Refresh();
        ShownCount = _all.Count(row => Matches(row));
    }

    private bool Matches(object item) =>
        item is AssessWordRowViewModel row
        && Contains(row.Word, WordFilter)
        && Contains(row.Result, ResultFilter)
        && Contains(row.ReadingText, ReadingFilter);

    private static bool Contains(string text, string filter) =>
        string.IsNullOrWhiteSpace(filter) || text.Contains(filter.Trim(), StringComparison.CurrentCultureIgnoreCase);
}

/// <summary>
/// One parsed word as the grid shows it: a short result, the readings as interlinear morphs, and plain text
/// for each column to sort and search on.
/// </summary>
public sealed class AssessWordRowViewModel
{
    public AssessWordRowViewModel(AssessmentWordResult word)
    {
        ArgumentNullException.ThrowIfNull(word);
        Word = word.Word;
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
        Readings = (word.Readings ?? Unresolved(word.Morphology))
            .Select((reading, index) => new ParserReadingViewModel(index + 1, reading))
            .ToArray();
        ReadingText = string.Join(" | ", Readings.Select(reading => reading.Text));
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
    public string Result { get; }
    public bool IsParsed { get; }
    public bool IsFailed { get; }
    public bool IsIncomplete { get; }
    public int ReadingCount { get; }
    public string Correctness { get; }
    public int? ElapsedMs { get; }
    public IReadOnlyList<ParserReadingViewModel> Readings { get; }
    public bool HasReadings => Readings.Count > 0;

    /// <summary>Every reading on one line, for sorting and searching by what the parser found.</summary>
    public string ReadingText { get; }

    public Uri? TryWordLink { get; }
    public bool HasTryWordLink => TryWordLink is not null;
    public IReadOnlyList<string> Unavailable { get; }
    public string Detail { get; }
}

/// <summary>One numbered reading of a word, as a row of interlinear morphs.</summary>
public sealed class ParserReadingViewModel
{
    public ParserReadingViewModel(int number, ParserReading reading)
    {
        ArgumentNullException.ThrowIfNull(reading);
        Number = number;
        Morphs = reading.Morphs.Select(morph => new ParserReadingMorphViewModel(morph)).ToArray();
        Text = string.Join(" + ", Morphs.Select(morph => morph.Form)) + " = " +
               string.Join(" + ", Morphs.Select(morph => morph.Gloss.Length == 0 ? "?" : morph.Gloss));
    }

    public int Number { get; }
    public IReadOnlyList<ParserReadingMorphViewModel> Morphs { get; }

    /// <summary>The reading as <c>forms = glosses</c>, for copying and searching.</summary>
    public string Text { get; }
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
    public string Category { get; }
    public bool Guessed { get; }
    public Uri? Link { get; }
    public bool HasLink => Link is not null;
    public bool HasNoLink => Link is null;
}
