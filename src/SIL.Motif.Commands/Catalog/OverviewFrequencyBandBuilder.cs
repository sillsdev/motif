using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Commands.Catalog;

/// <summary>Cuts the Selection's words, ranked by their places in the chosen Texts or a word list's counts, into the Overview's frequency bands.</summary>
internal static class OverviewFrequencyBandBuilder
{
    /// <summary>
    /// The bands, or null when the Selection has no place counts to rank by. Every word falls in exactly one band,
    /// so the band totals equal the Selection totals; words tied on places are ranked by ordinal spelling.
    /// </summary>
    public static OverviewFrequencyBands? Build(IReadOnlyList<PlacedWord> words,
        IReadOnlyDictionary<string, int>? occurrences, bool byText)
    {
        if (occurrences is not { Count: > 0 } || words.Count == 0) return null;
        var forms = new string[words.Count];
        var counts = new long[words.Count];
        var unknown = new bool[words.Count];
        var noCount = 0;
        for (var index = 0; index < forms.Length; index++)
        {
            forms[index] = words[index].Form;
            counts[index] = occurrences.GetValueOrDefault(forms[index]);
            unknown[index] = !byText && !occurrences.ContainsKey(forms[index]);
            if (unknown[index]) noCount++;
        }
        if (counts.Sum() == 0) return null;

        var edges = OverviewMeasureLimits.BandEdges;
        var totals = new (int Words, int Parsed, long Occurrences, long ParsedOccurrences)[edges.Count + 1];
        var order = RankedWords.Order(forms, counts, unknown);
        var band = 0;
        for (var rank = 0; rank < order.Length; rank++)
        {
            while (band < edges.Count && rank >= edges[band]) band++;
            var index = order[rank];
            var parsed = words[index].Measured is not null &&
                words[index].Column is CompareColumnKind.Match or CompareColumnKind.NoMatch;
            var total = totals[band];
            totals[band] = (total.Words + 1, total.Parsed + (parsed ? 1 : 0), total.Occurrences + counts[index],
                total.ParsedOccurrences + (parsed ? counts[index] : 0));
        }

        var bands = new List<OverviewFrequencyBand>();
        for (var index = 0; index < totals.Length; index++)
        {
            if (totals[index].Words == 0) continue;
            bands.Add(new OverviewFrequencyBand(index == 0 ? 1 : edges[index - 1] + 1,
                index < edges.Count ? edges[index] : null, totals[index].Words, totals[index].Parsed,
                (int)Math.Min(totals[index].Occurrences, int.MaxValue),
                (int)Math.Min(totals[index].ParsedOccurrences, int.MaxValue)));
        }
        return new OverviewFrequencyBands(byText, bands) { NoCountWords = noCount };
    }
}
