namespace SIL.Motif.Contract.Responses;

/// <summary>
/// How many analyses the parsed words have. Words the parser refused, stopped or built nothing for are left out,
/// and so are parsed words whose analyses could not all be read, because their count would be too low.
/// </summary>
/// <param name="ParsedWords">Selection words with a completed parse.</param>
/// <param name="CountedWords">Those parsed words whose analyses were all read; the mean is over these.</param>
/// <param name="MeanAnalyses">Analyses per counted word.</param>
/// <param name="AmbiguousWords">Counted words with more than one analysis.</param>
/// <param name="Top">
/// At most <see cref="OverviewMeasureLimits.TopWords"/> words with more than one analysis, most analyses first,
/// then most places in the chosen Texts, then spelling.
/// </param>
public sealed record OverviewAmbiguity(
    int ParsedWords, int CountedWords, double MeanAnalyses, int AmbiguousWords, IReadOnlyList<OverviewAmbiguousWord> Top);

/// <summary>A parsed word with more than one analysis.</summary>
/// <param name="Word">The word as written in the Selection.</param>
/// <param name="Analyses">How many analyses the parser built for it.</param>
/// <param name="Occurrences">Its places in the chosen Texts; zero when the Selection has no place counts.</param>
public sealed record OverviewAmbiguousWord(string Word, int Analyses, int Occurrences);
