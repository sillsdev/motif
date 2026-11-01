namespace SIL.Motif.Contract.Responses;

/// <summary>
/// The Selection's words with no completed parse, most often used first, grouped only by what the parser recorded:
/// a refusal code, the limit that stopped the search, or no analysis. Words the project marks as an incorrect
/// spelling are counted apart and not listed.
/// </summary>
/// <param name="Words">How many words are unparsed and listed, whatever their reason.</param>
/// <param name="Occurrences">Their places in the chosen Texts; zero when the Selection has no place counts.</param>
/// <param name="RankedByOccurrences">
/// Whether <paramref name="Top"/> is ordered by places in the chosen Texts. When false the Selection has no place
/// counts and the words are ordered by spelling.
/// </param>
/// <param name="Top">At most <see cref="OverviewMeasureLimits.TopWords"/> words, ties broken by spelling.</param>
/// <param name="TopOccurrences">The places the listed words account for.</param>
/// <param name="Groups">At most <see cref="OverviewMeasureLimits.TopWords"/> reasons, largest first.</param>
/// <param name="GroupCount">How many distinct reasons there are, which can exceed <paramref name="Groups"/>.</param>
/// <param name="IncorrectSpellingWords">Unparsed words the project marks as an incorrect spelling, not listed.</param>
public sealed record OverviewUnparsedWords(
    int Words,
    int Occurrences,
    bool RankedByOccurrences,
    IReadOnlyList<OverviewUnparsedWord> Top,
    int TopOccurrences,
    IReadOnlyList<OverviewUnparsedGroup> Groups,
    int GroupCount,
    int IncorrectSpellingWords)
{
    /// <summary>Whether the Selection is made of chosen Texts, so the places are places in those Texts.</summary>
    public bool ByText { get; init; }
}

/// <summary>One unparsed word, with its places in the chosen Texts and the recorded reason it has no parse.</summary>
/// <param name="Word">The word as written in the Selection.</param>
/// <param name="Occurrences">Its places in the chosen Texts; zero when the Selection has no place counts.</param>
/// <param name="Kind">One of <see cref="UnparsedKind"/>.</param>
/// <param name="Code">The parser's refusal code when <paramref name="Kind"/> is <see cref="UnparsedKind.Refused"/>.</param>
public sealed record OverviewUnparsedWord(string Word, int Occurrences, string Kind, string? Code);

/// <summary>The unparsed words that share one recorded reason.</summary>
/// <param name="Kind">One of <see cref="UnparsedKind"/>.</param>
/// <param name="Code">The parser's refusal code when <paramref name="Kind"/> is <see cref="UnparsedKind.Refused"/>.</param>
/// <param name="Words">How many words share the reason.</param>
/// <param name="Occurrences">Their places in the chosen Texts.</param>
public sealed record OverviewUnparsedGroup(string Kind, string? Code, int Words, int Occurrences);

/// <summary>The reasons an unparsed word can carry, each one a fact the parser stored for the word.</summary>
public static class UnparsedKind
{
    /// <summary>The search finished and built no analysis; the parser stored no finer reason.</summary>
    public const string NoAnalysis = "no-analysis";

    /// <summary>The parser refused the word and stored a refusal code.</summary>
    public const string Refused = "refused";

    /// <summary>The search stopped at the work limit.</summary>
    public const string WorkLimit = StopLimit.Work;

    /// <summary>The search stopped at the time safety net.</summary>
    public const string TimeLimit = StopLimit.Time;

    /// <summary>The work limit and the time safety net both fired.</summary>
    public const string BothLimits = StopLimit.Both;

    /// <summary>The search did not finish, and the stored result does not say which limit ended it.</summary>
    public const string Stopped = "stopped";
}

/// <summary>The sizes of the lists the Overview carries; each is a top list beside its total.</summary>
public static class OverviewMeasureLimits
{
    /// <summary>How many words any Overview list names.</summary>
    public const int TopWords = 10;

    /// <summary>The last rank of each frequency band but the final one, which holds every word beyond them.</summary>
    public static readonly IReadOnlyList<int> BandEdges = [100, 1_000, 10_000];
}
