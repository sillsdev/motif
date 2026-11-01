namespace SIL.Motif.Contract.Requests;

/// <summary>The references a Difference can be read against.</summary>
public static class ProposalDifferenceReferences
{
    /// <summary>FieldWorks' recorded human decisions.</summary>
    public const string Human = "human";

    /// <summary>
    /// The decisions a named non-human agent staged from cited sources through Applied Proposals, never a person's.
    /// </summary>
    public const string SourceValidated = "source-validated";
}

/// <summary>Compares a completed Trial's parses with FieldWorks' recorded human decisions for one Text.</summary>
/// <param name="ProjectPath">The recorded project path.</param>
/// <param name="ProposalId">The Proposal whose Trial is compared.</param>
/// <param name="TextId">The GUID of the Text whose words and human decisions are compared.</param>
/// <param name="JobId">The completed Trial job; omitted takes the Proposal's latest completed Trial.</param>
public sealed record ProposalDifferenceProduceRequest(string ProjectPath, string ProposalId, string TextId,
    string? JobId = null);

/// <summary>Reads a saved Proposal Difference: its summary first, then one page of changed words.</summary>
/// <param name="ProjectPath">The recorded project path.</param>
/// <param name="ProposalId">
/// The Proposal whose newest Difference is read, narrowed by <paramref name="TextId"/> and
/// <paramref name="Reference"/>; ignored when a Difference id is given.
/// </param>
/// <param name="DifferenceId">The saved Difference to read.</param>
/// <param name="CouldBeBad">Whether to list only words filed under a could-be-bad category.</param>
/// <param name="Bucket">The stable category code that listed words must belong to.</param>
/// <param name="BeforeState">The approval state a listed word had before the Proposal.</param>
/// <param name="AfterState">The approval state a listed word has after the Proposal.</param>
/// <param name="Offset">How many matching words to skip.</param>
/// <param name="Limit">How many matching words to return.</param>
/// <param name="Limitation">The limitation code that either side of a listed word must carry, such as <c>capped</c>.</param>
/// <param name="Word">One exact word to list, whose Text occurrences are then returned a page at a time.</param>
/// <param name="OccurrenceOffset">How many of that word's Text occurrences to skip (default 0); only with a word.</param>
/// <param name="OccurrenceLimit">How many of that word's Text occurrences to return (default 25); only with a word.</param>
/// <param name="TextId">Read the Proposal's Difference for this Text GUID; only with a Proposal.</param>
/// <param name="Reference">Read the Proposal's Difference against <c>human</c> or <c>source-validated</c> decisions; only with a Proposal.</param>
public sealed record ProposalDifferenceShowRequest(string ProjectPath, string? ProposalId = null,
    string? DifferenceId = null, bool CouldBeBad = false, string? Bucket = null, string? BeforeState = null,
    string? AfterState = null, int Offset = 0, int Limit = 25, string? Limitation = null, string? Word = null,
    int? OccurrenceOffset = null, int? OccurrenceLimit = null, string? TextId = null, string? Reference = null);
