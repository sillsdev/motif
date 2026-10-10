using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.ViewModels;

/// <summary>The four things FieldWorks can hold for one analysis, in the order the window shows them.</summary>
public enum WordDispositionKind
{
    /// <summary>FieldWorks holds the analysis as Approved.</summary>
    Approved,

    /// <summary>FieldWorks holds the analysis without an opinion.</summary>
    Unknown,

    /// <summary>FieldWorks holds the analysis as Disapproved.</summary>
    Disapproved,

    /// <summary>FieldWorks does not hold the analysis.</summary>
    Absent,
}

/// <summary>One tile of a disposition button's menu: a letter the person can choose for one analysis.</summary>
/// <param name="Kind">What FieldWorks would hold.</param>
/// <param name="IsCurrent">Whether FieldWorks holds this now.</param>
/// <param name="IsStaged">Whether a pending change makes FieldWorks hold this after Apply.</param>
/// <param name="Choice">The staged change this tile makes, or <see langword="null"/> when it changes nothing.</param>
public sealed record WordDispositionTile(
    WordDispositionKind Kind, bool IsCurrent, bool IsStaged, AnalysisMarkingChoice? Choice, WordMarkingChoice? Action = null)
{
    /// <summary>The tile's letter: A, U, D, or a dash for an analysis FieldWorks does not hold.</summary>
    public string Letter => WordDisposition.LetterOf(Kind);

    /// <summary>Whether choosing the tile does anything: it stages a change, or it is what FieldWorks already holds.</summary>
    public bool IsAvailable => Action?.IsAvailable ?? (Choice is not null || IsCurrent);

    /// <summary>The hover text: the full phrase and, for the word's main analysis, its key.</summary>
    public string Tip => (IsCurrent ? "Now: " : string.Empty) + WordDisposition.PhraseOf(Kind);

    /// <summary>Whether the tile is Approved, for its style class.</summary>
    public bool IsApproved => Kind == WordDispositionKind.Approved;

    /// <summary>Whether the tile is Unknown, for its style class.</summary>
    public bool IsUnknown => Kind == WordDispositionKind.Unknown;

    /// <summary>Whether the tile is Disapproved, for its style class.</summary>
    public bool IsDisapproved => Kind == WordDispositionKind.Disapproved;

    /// <summary>Whether the tile is Not in FieldWorks, for its style class.</summary>
    public bool IsAbsent => Kind == WordDispositionKind.Absent;
}

/// <summary>One analysis in a disposition menu with its tiles.</summary>
/// <param name="Number">The analysis's number in its list, starting at 1.</param>
/// <param name="Morphs">The analysis's morpheme forms, hyphenated as FieldWorks writes them.</param>
/// <param name="Glosses">The morphemes' glosses in the same order.</param>
/// <param name="Note">Where the analysis comes from, or how it compares, in the window's words.</param>
/// <param name="Tiles">The tiles the person can choose for this analysis.</param>
public sealed record WordDispositionRow(
    int Number, string Morphs, string Glosses, string Note, IReadOnlyList<WordDispositionTile> Tiles)
{
    /// <summary>Whether a pending change chooses one of this row's tiles.</summary>
    public bool IsStaged => Tiles.Any(tile => tile.IsStaged);
}

/// <summary>
/// The two buttons a word offers: its opinion mark, which sets what FieldWorks holds for the stored analyses, and a
/// count of PanGloss readings FieldWorks does not hold, which adds one. Both present the word's existing Fix choices
/// and stage them through the same commands, so Review, Apply and undo are unchanged.
/// </summary>
public sealed class WordDisposition
{
    private WordDisposition(IReadOnlyList<WordDispositionRow> stored, IReadOnlyList<WordDispositionRow> readings,
        AnalysisMarkingChoice? keep, AnalysisMarkingChoice? addAll, bool spellingStaged, Func<AnalysisMarkingChoice, WordMarkingChoice>? bind)
    {
        StoredRows = stored;
        Readings = readings;
        KeepFieldWorks = keep;
        AddAllAsUnknown = addAll;
        IsSpellingStaged = spellingStaged;
        KeepFieldWorksAction = keep is null ? null : bind?.Invoke(keep);
        AddAllAsUnknownAction = addAll is null ? null : bind?.Invoke(addAll);
    }

    /// <summary>Each analysis FieldWorks stores, with tiles for what it should hold.</summary>
    public IReadOnlyList<WordDispositionRow> StoredRows { get; }

    /// <summary>Each PanGloss reading FieldWorks does not hold, numbered, with tiles for adding it.</summary>
    public IReadOnlyList<WordDispositionRow> Readings { get; }

    /// <summary>The choice that keeps FieldWorks as it is and marks the word read, when the word offers it.</summary>
    public AnalysisMarkingChoice? KeepFieldWorks { get; }

    /// <summary>The keep choice bound to the captured word and evidence that supplied the menu.</summary>
    public WordMarkingChoice? KeepFieldWorksAction { get; }

    /// <summary>The choice that adds every PanGloss reading FieldWorks lacks as Unknown, when offered.</summary>
    public AnalysisMarkingChoice? AddAllAsUnknown { get; }

    /// <summary>The add-all choice bound to the captured word and evidence that supplied the menu.</summary>
    public WordMarkingChoice? AddAllAsUnknownAction { get; }

    /// <summary>Whether a pending change marks the word as an incorrect spelling.</summary>
    public bool IsSpellingStaged { get; }

    /// <summary>What the opinion mark shows: the first stored analysis after any pending change, or a dash.</summary>
    public WordDispositionKind Mark => StoredRows.Count == 0
        ? WordDispositionKind.Absent
        : (StoredRows[0].Tiles.FirstOrDefault(tile => tile.IsStaged) ??
           StoredRows[0].Tiles.FirstOrDefault(tile => tile.IsCurrent))?.Kind ?? WordDispositionKind.Unknown;

    /// <summary>The opinion mark's letter.</summary>
    public string MarkLetter => LetterOf(Mark);

    /// <summary>Whether the mark shows Approved, for its style class.</summary>
    public bool IsMarkApproved => Mark == WordDispositionKind.Approved;

    /// <summary>Whether the mark shows Unknown, for its style class.</summary>
    public bool IsMarkUnknown => Mark == WordDispositionKind.Unknown;

    /// <summary>Whether the mark shows Disapproved, for its style class.</summary>
    public bool IsMarkDisapproved => Mark == WordDispositionKind.Disapproved;

    /// <summary>Whether the mark shows Not in FieldWorks, for its style class.</summary>
    public bool IsMarkAbsent => Mark == WordDispositionKind.Absent;

    /// <summary>Whether a reading is chosen and added as Approved, for the add button's style class.</summary>
    public bool IsChosenApproved => ChosenKind == WordDispositionKind.Approved;

    /// <summary>Whether a reading is chosen and added as Unknown, for the add button's style class.</summary>
    public bool IsChosenUnknown => ChosenKind == WordDispositionKind.Unknown;

    /// <summary>Whether the person has changed what FieldWorks holds for this word, so the mark shows solid.</summary>
    public bool IsChanged => IsSpellingStaged || StoredRows.Any(row => row.IsStaged);

    /// <summary>The reading whose addition is pending, or <see langword="null"/>.</summary>
    public WordDispositionRow? ChosenReading => Readings.FirstOrDefault(row => row.IsStaged);

    /// <summary>What the chosen reading is added as, for the add button's colour.</summary>
    public WordDispositionKind? ChosenKind => ChosenReading?.Tiles.First(tile => tile.IsStaged).Kind;

    /// <summary>Whether the add button shows: PanGloss has readings FieldWorks does not hold.</summary>
    public bool HasReadings => Readings.Count > 0;


    /// <summary>Whether FieldWorks holds any analysis of this word for the tiles to act on.</summary>
    public bool HasStoredRows => StoredRows.Count > 0;

    /// <summary>The add button's text: "+n" for n readings, or "✓k" once reading k is chosen.</summary>
    public string AddLabel => ChosenReading is { } chosen ? "✓" + chosen.Number : "+" + Readings.Count;

    /// <summary>The add button's hover text.</summary>
    public string AddTip => ChosenReading is { } chosen
        ? $"Adding analysis {chosen.Number} ({chosen.Morphs}) as {PhraseWordOf(ChosenKind!.Value)}"
        : Readings.Count == 1 ? "Add the PanGloss analysis to FieldWorks" : $"Add one of {Readings.Count} PanGloss analyses";

    /// <summary>The mark button's hover text.</summary>
    public string MarkTip => IsChanged
        ? $"Changed: FieldWorks will hold it as {PhraseWordOf(Mark)}. Ctrl+Z undoes"
        : "Change what FieldWorks holds";

    /// <summary>Builds both buttons from a word's marking.</summary>
    public static WordDisposition From(AnalysisMarkingState marking, Func<AnalysisMarkingChoice, WordMarkingChoice>? bind = null)
    {
        ArgumentNullException.ThrowIfNull(marking);
        var staged = marking.StagedTransitions;
        var stored = marking.FieldWorksAnalyses.Select((analysis, index) => StoredRow(analysis, index, marking, staged, bind))
            .ToArray();

        var readings = new List<WordDispositionRow>();
        var readingIndexes = marking.FixChoices.Where(choice => choice.Kind == AnalysisMarkingActionKind.Add &&
                choice.ReadingIndex is not null)
            .Select(choice => choice.ReadingIndex!.Value).Distinct().ToArray();
        foreach (var index in readingIndexes)
        {
            var reading = index < marking.PanGlossReadings.Count ? marking.PanGlossReadings[index] : null;
            var morphs = reading?.Display?.Morphs ?? [];
            var tiles = new List<WordDispositionTile>();
            foreach (var kind in new[] { WordDispositionKind.Approved, WordDispositionKind.Unknown })
            {
                var choice = marking.FixChoices.FirstOrDefault(candidate =>
                    candidate.Kind == AnalysisMarkingActionKind.Add && candidate.ReadingIndex == index &&
                    candidate.AfterApply == PhraseWordOf(kind));
                if (choice is null) continue;
                tiles.Add(new WordDispositionTile(kind, false,
                    staged.Any(change => change.ReadingIndex == index && change.AfterApply == choice.AfterApply), choice, bind?.Invoke(choice)));
            }
            readings.Add(new WordDispositionRow(readings.Count + 1, MorphText(morphs), GlossText(morphs),
                "Different from FieldWorks", tiles));
        }

        var keep = marking.FixChoices.FirstOrDefault(choice => choice.Kind == AnalysisMarkingActionKind.KeepFieldWorks);
        var addAll = marking.FixChoices.FirstOrDefault(choice => choice.Kind == AnalysisMarkingActionKind.AcceptNewSet);
        var spelling = staged.Any(change => change.AfterApply == "Incorrect");
        return new WordDisposition(stored, readings, keep, addAll, spelling, bind);
    }

    private static WordDispositionRow StoredRow(FieldWorksAnalysisMarking analysis, int index,
        AnalysisMarkingState marking, IReadOnlyList<StagedMarkingTransition> staged, Func<AnalysisMarkingChoice, WordMarkingChoice>? bind)
    {
        var current = KindOfGrade(analysis.Opinion);
        var pending = staged.FirstOrDefault(change => change.StoredAnalysisId == analysis.StoredAnalysisId);
        var tiles = new[] { WordDispositionKind.Approved, WordDispositionKind.Unknown, WordDispositionKind.Disapproved,
                WordDispositionKind.Absent }
            .Select(kind =>
            {
                var choice = marking.FixChoices.FirstOrDefault(candidate =>
                    candidate.StoredAnalysisId == analysis.StoredAnalysisId && candidate.Kind == ActionFor(kind));
                return new WordDispositionTile(kind, kind == current,
                    pending is not null && KindOfAfterApply(pending.AfterApply) == kind, choice, choice is null ? null : bind?.Invoke(choice));
            }).ToArray();
        var matching = marking.PanGlossReadings.Any(reading => reading.MatchingAnalysisIds.Contains(analysis.StoredAnalysisId));
        return new WordDispositionRow(index + 1, MorphText(analysis.Morphs), GlossText(analysis.Morphs),
            matching ? "In FieldWorks · PanGloss built the same" : "In FieldWorks", tiles);
    }

    private static AnalysisMarkingActionKind ActionFor(WordDispositionKind kind) => kind switch
    {
        WordDispositionKind.Approved => AnalysisMarkingActionKind.Approve,
        WordDispositionKind.Unknown => AnalysisMarkingActionKind.MakeUnknown,
        WordDispositionKind.Disapproved => AnalysisMarkingActionKind.Disapprove,
        _ => AnalysisMarkingActionKind.RemoveAnalysis,
    };

    private static WordDispositionKind KindOfGrade(string opinion) => opinion switch
    {
        ReadingGrade.Approved => WordDispositionKind.Approved,
        ReadingGrade.Disapproved => WordDispositionKind.Disapproved,
        _ => WordDispositionKind.Unknown,
    };

    private static WordDispositionKind? KindOfAfterApply(string afterApply) => afterApply switch
    {
        "Approved" => WordDispositionKind.Approved,
        "Unknown" => WordDispositionKind.Unknown,
        "Disapproved" => WordDispositionKind.Disapproved,
        "Removed" => WordDispositionKind.Absent,
        _ => null,
    };

    /// <summary>The window's letter for <paramref name="kind"/>.</summary>
    public static string LetterOf(WordDispositionKind kind) => kind switch
    {
        WordDispositionKind.Approved => "A",
        WordDispositionKind.Unknown => "U",
        WordDispositionKind.Disapproved => "D",
        _ => "–",
    };

    /// <summary>The full hover phrase for <paramref name="kind"/>.</summary>
    public static string PhraseOf(WordDispositionKind kind) => kind switch
    {
        WordDispositionKind.Approved => "Approve: FieldWorks marks this analysis correct",
        WordDispositionKind.Unknown => "Unknown: FieldWorks holds it without an opinion",
        WordDispositionKind.Disapproved => "Disapprove: FieldWorks marks this analysis wrong",
        _ => "Not in FieldWorks: remove it",
    };

    private static string PhraseWordOf(WordDispositionKind kind) => kind switch
    {
        WordDispositionKind.Approved => "Approved",
        WordDispositionKind.Unknown => "Unknown",
        WordDispositionKind.Disapproved => "Disapproved",
        _ => "Not in FieldWorks",
    };

    private static string MorphText(IReadOnlyList<ParserReadingMorph> morphs) =>
        string.Join(" ", morphs.Select(morph => morph.Form));

    private static string GlossText(IReadOnlyList<ParserReadingMorph> morphs) =>
        string.Join(" ", morphs.Select(morph => morph.Gloss));
}
