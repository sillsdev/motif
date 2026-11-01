namespace SIL.Motif.Contract.Responses;

/// <summary>
/// The Selection's words ranked by how often they occur in the chosen Texts and cut into fixed bands. The bands
/// share the words and places of the Selection between them, so each total here equals the Text Coverage total.
/// </summary>
/// <param name="ByText">
/// Whether the Selection is made of chosen Texts. Only then is this Text Coverage by frequency band; a word list or
/// corpus Selection is labelled by frequency band alone.
/// </param>
/// <param name="Bands">The bands that hold at least one word, most frequent first.</param>
public sealed record OverviewFrequencyBands(bool ByText, IReadOnlyList<OverviewFrequencyBand> Bands)
{
    /// <summary>
    /// Words the Selection's word list gave no count, which rank after every counted word and add nothing to a
    /// band's occurrences; always zero for a Selection of chosen Texts.
    /// </summary>
    public int NoCountWords { get; init; }
}

/// <summary>One band of ranked words and how much of it has a completed parse.</summary>
/// <param name="FirstRank">The band's first rank, counting the most frequent word as rank 1.</param>
/// <param name="LastRank">The band's last rank, or null for the band that holds every word beyond the others.</param>
/// <param name="Words">The Selection words in the band; fewer than the rank span when the Selection ends inside it.</param>
/// <param name="ParsedWords">Those words with a completed parse.</param>
/// <param name="Occurrences">Their places in the chosen Texts.</param>
/// <param name="ParsedOccurrences">The places of the words with a completed parse.</param>
public sealed record OverviewFrequencyBand(
    int FirstRank, int? LastRank, int Words, int ParsedWords, int Occurrences, int ParsedOccurrences);
