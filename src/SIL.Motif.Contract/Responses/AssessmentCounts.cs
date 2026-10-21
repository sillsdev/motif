using System.Collections.Generic;
using System.Linq;

namespace SIL.Motif.Contract.Responses;

/// <summary>
/// How a run's words came out, so a caller learns the shape of a large list without reading its rows. The
/// outcome counts partition <see cref="Words"/>. <see cref="Accepted"/> and <see cref="GuessOnly"/> partition
/// <see cref="Analysed"/>: a word is accepted when at least one reading identifies every morph in the lexicon,
/// and guess-only when each reading contains a morph the parser guessed because it could not identify it.
/// </summary>
/// <param name="Words">Words measured.</param>
/// <param name="Analysed">Words the parser returned at least one reading for.</param>
/// <param name="NoAnalysis">Words whose search finished with no reading.</param>
/// <param name="TimedOut">Words whose search stopped at the time limit.</param>
/// <param name="Capped">Words whose search stopped at the step cap.</param>
/// <param name="Skipped">Words the parser refused to search.</param>
/// <param name="Accepted">Analysed words with a reading whose every morph the parser identified.</param>
/// <param name="GuessOnly">Analysed words whose every reading contains a guessed morph, or has no morph detail.</param>
public sealed record AssessmentCounts(
    int Words, int Analysed, int NoAnalysis, int TimedOut, int Capped, int Skipped, int Accepted, int GuessOnly)
{
    /// <summary>Counts these rows.</summary>
    public static AssessmentCounts Of(IEnumerable<AssessmentWordResult> words)
    {
        var rows = words.ToArray();
        int Outcome(string name) => rows.Count(row => row.Outcome == name);
        var analysed = rows.Where(row => row.Outcome == "analysed").ToArray();
        var accepted = analysed.Count(IsAccepted);
        return new AssessmentCounts(rows.Length, analysed.Length, Outcome("no-analysis"), Outcome("timed-out"),
            Outcome("capped"), Outcome("skipped"), accepted, analysed.Length - accepted);
    }

    /// <summary>Whether an analysed row has a reading with no guessed morph.</summary>
    public static bool IsAccepted(AssessmentWordResult row) =>
        row.Morphology?.Analyses.Any(analysis => analysis.Morphs.All(morph => morph.GuessedString is null)) == true;
}
