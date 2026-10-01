using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Commands.Queries;

/// <summary>What the page showing a word knows about it that its Assessment result does not.</summary>
/// <param name="Places">How many places in the chosen Texts the word occurs, when the page has counted them.</param>
/// <param name="IsUnread">Whether some place the word occurs is unread, when the page has read the marks.</param>
public sealed record WordRowFacts(int? Places = null, bool? IsUnread = null);

/// <summary>
/// The one projection from a word's Assessment result to the <see cref="WordRow"/> every page shows, so a word
/// reads the same wherever the window reaches it.
/// </summary>
public static class WordRowProjection
{
    /// <summary>The row for <paramref name="word"/>, placed by <see cref="CompareSemantics.Place"/>.</summary>
    public static WordRow Of(AssessmentWordResult word, WordRowFacts? facts = null)
    {
        ArgumentNullException.ThrowIfNull(word);
        var placement = CompareSemantics.Place(new CompareWordFacts(word.ProjectStanding, word.Outcome,
            word.IsIncomplete, word.Morphology, word.ReadingGrades, word.MissedApproved?.Count ?? 0));
        return Of(word, placement.Column, facts);
    }

    /// <summary>
    /// The row for <paramref name="word"/> in the Matrix column <paramref name="column"/>: the window passes the
    /// column its Matrix placed the word in, so the row never contradicts the cell the word sits in.
    /// </summary>
    public static WordRow Of(AssessmentWordResult word, CompareColumnKind column, WordRowFacts? facts = null)
    {
        ArgumentNullException.ThrowIfNull(word);
        var (meaning, family) = CompareSemantics.MeaningOf(word.ProjectStanding, column);
        var fieldWorks = FieldWorksAnalysisOf(word);
        var fieldWorksMorphs = fieldWorks?.Morphs ?? [];
        var outcome = OutcomeOf(column);
        var panGloss = outcome == WordRowOutcome.Different ? ClosestReading(fieldWorksMorphs, word.Readings ?? []) : null;
        var panGlossMorphs = panGloss?.Morphs ?? [];
        return new WordRow(word.Word, outcome, meaning, ToneOf(family))
        {
            Gloss = string.Join(" ", fieldWorksMorphs.Select(morph => morph.Gloss.Length == 0 ? "?" : morph.Gloss)),
            Opinion = word.ProjectStanding,
            FieldWorksAnalysisId = fieldWorks?.StoredAnalysisId,
            FieldWorksMorphemes = fieldWorksMorphs,
            PanGlossMorphemes = panGlossMorphs,
            DifferingPositions = fieldWorksMorphs.Count == 0 ? [] : DifferingPositions(fieldWorksMorphs, panGlossMorphs),
            PanGlossReadingCount = word.Morphology?.Analyses.Count ?? word.Readings?.Count ?? 0,
            Places = facts?.Places ?? word.OccurrenceCount,
            ElapsedMs = word.Outcome == "skipped" ? null : word.ElapsedMs,
            IsUnread = facts?.IsUnread,
            WordAnalysesLink = word.TryWordLink,
        };
    }

    /// <summary>The outcome a Matrix column stands for.</summary>
    public static WordRowOutcome OutcomeOf(CompareColumnKind column) => column switch
    {
        CompareColumnKind.Match => WordRowOutcome.Same,
        CompareColumnKind.NoMatch => WordRowOutcome.Different,
        CompareColumnKind.NoParse => WordRowOutcome.NoParse,
        CompareColumnKind.Timeout => WordRowOutcome.Stopped,
        _ => WordRowOutcome.NotParsed,
    };

    /// <summary>The tone each of the Matrix's families of meaning takes.</summary>
    public static WordRowTone ToneOf(CompareFamilyKind family) => family switch
    {
        CompareFamilyKind.Good or CompareFamilyKind.Fine => WordRowTone.Fine,
        CompareFamilyKind.Review or CompareFamilyKind.New => WordRowTone.Look,
        CompareFamilyKind.Violation => WordRowTone.Problem,
        _ => WordRowTone.Neutral,
    };

    /// <summary>
    /// The positions in <paramref name="panGloss"/>, counting from one, outside the longest run of morphemes the two
    /// share in order. Two morphemes are the same only when both name the same allomorph and grammatical info.
    /// </summary>
    public static IReadOnlyList<int> DifferingPositions(
        IReadOnlyList<ParserReadingMorph> fieldWorks, IReadOnlyList<ParserReadingMorph> panGloss)
    {
        ArgumentNullException.ThrowIfNull(fieldWorks);
        ArgumentNullException.ThrowIfNull(panGloss);
        var shared = SharedPositions(fieldWorks, panGloss);
        return Enumerable.Range(1, panGloss.Count).Where(position => !shared.Contains(position - 1)).ToArray();
    }

    // The approved analysis, else the only one FieldWorks holds, the same choice the run makes for its expectation.
    private static ParserReading? FieldWorksAnalysisOf(AssessmentWordResult word) =>
        word.ExpectedAnalysis
        ?? word.StoredAnalyses.FirstOrDefault(reading => reading.StoredAnalysisOpinion == ReadingGrade.Approved)
        ?? (word.StoredAnalyses.Count == 1 ? word.StoredAnalyses[0] : null);

    // The reading sharing most morphemes with FieldWorks' analysis, the parser's first on a tie.
    private static ParserReading? ClosestReading(IReadOnlyList<ParserReadingMorph> fieldWorks,
        IReadOnlyList<ParserReading> readings) =>
        readings.Select((reading, index) => (reading, index, shared: SharedPositions(fieldWorks, reading.Morphs).Count))
            .OrderByDescending(item => item.shared).ThenBy(item => item.index)
            .Select(item => item.reading).FirstOrDefault();

    // The positions in panGloss of a longest common subsequence with fieldWorks, compared by identity.
    private static HashSet<int> SharedPositions(
        IReadOnlyList<ParserReadingMorph> fieldWorks, IReadOnlyList<ParserReadingMorph> panGloss)
    {
        var lengths = new int[fieldWorks.Count + 1, panGloss.Count + 1];
        for (var i = fieldWorks.Count - 1; i >= 0; i--)
            for (var j = panGloss.Count - 1; j >= 0; j--)
                lengths[i, j] = SameMorpheme(fieldWorks[i], panGloss[j])
                    ? lengths[i + 1, j + 1] + 1 : Math.Max(lengths[i + 1, j], lengths[i, j + 1]);
        var shared = new HashSet<int>();
        for (int i = 0, j = 0; i < fieldWorks.Count && j < panGloss.Count;)
        {
            if (SameMorpheme(fieldWorks[i], panGloss[j])) { shared.Add(j); i++; j++; }
            else if (lengths[i + 1, j] >= lengths[i, j + 1]) i++;
            else j++;
        }
        return shared;
    }

    private static bool SameMorpheme(ParserReadingMorph left, ParserReadingMorph right) =>
        left.AllomorphId is { } allomorph && left.GrammaticalInfoId is { } grammaticalInfo &&
        StringComparer.OrdinalIgnoreCase.Equals(allomorph, right.AllomorphId) &&
        StringComparer.OrdinalIgnoreCase.Equals(grammaticalInfo, right.GrammaticalInfoId);
}
