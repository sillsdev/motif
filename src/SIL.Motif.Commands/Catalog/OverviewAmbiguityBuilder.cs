using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Commands.Catalog;

/// <summary>Counts the analyses of the Selection's parsed words from the stored parse.</summary>
internal static class OverviewAmbiguityBuilder
{
    /// <summary>
    /// The ambiguity of the parsed words, or null when none has a readable analysis count. A word the parser refused
    /// or stopped is not parsed; one whose analyses could not all be read is parsed but not counted.
    /// </summary>
    public static OverviewAmbiguity? Build(IReadOnlyList<PlacedWord> words, IReadOnlyDictionary<string, int>? occurrences)
    {
        var parsed = 0;
        var counted = 0;
        long analyses = 0;
        var ambiguous = new List<(string Word, int Analyses, int Occurrences)>();
        foreach (var placed in words)
        {
            if (placed.Measured is not { } word ||
                placed.Column is not (CompareColumnKind.Match or CompareColumnKind.NoMatch)) continue;
            parsed++;
            if (word.Morphology is not { Unavailable.Count: 0 } morphology) continue;
            counted++;
            analyses += morphology.Analyses.Count;
            if (morphology.Analyses.Count > 1)
                ambiguous.Add((placed.Form, morphology.Analyses.Count, occurrences?.GetValueOrDefault(placed.Form) ?? 0));
        }
        if (counted == 0) return null;

        var top = ambiguous.OrderByDescending(word => word.Analyses).ThenByDescending(word => word.Occurrences)
            .ThenBy(word => word.Word, StringComparer.Ordinal).Take(OverviewMeasureLimits.TopWords)
            .Select(word => new OverviewAmbiguousWord(word.Word, word.Analyses, word.Occurrences)).ToArray();
        return new OverviewAmbiguity(parsed, counted, (double)analyses / counted, ambiguous.Count, top);
    }
}
