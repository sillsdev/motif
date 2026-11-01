namespace SIL.Motif.Contract.Responses;

/// <summary>
/// The openings of the notes a saved Difference summary can carry in <see cref="ProposalDifferenceSummary.Warnings"/>.
/// The command line and tool list the whole English sentence; the window recognises a note by its opening and says it
/// in its own words, so the two share these constants and cannot drift apart.
/// </summary>
public static class ProposalDifferenceWarnings
{
    /// <summary>The two runs used different parsers.</summary>
    public const string ParserDiffers = "The before side was parsed by ";

    /// <summary>The two runs split the Text into words with different tokenisers.</summary>
    public const string TokeniserDiffers = "The before side was tokenised with ";

    /// <summary>Only one run retried words stopped at the per-word time limit under a longer limit.</summary>
    public const string RetryDiffers = "Only one side reran words stopped at the per-word time limit";
}
