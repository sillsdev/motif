namespace SIL.Motif.Contract.Requests;

/// <summary>Compares a completed Trial's parses with FieldWorks' recorded human decisions for one Text.</summary>
/// <param name="ProjectPath">The recorded project path.</param>
/// <param name="ProposalId">The Proposal whose Trial is compared.</param>
/// <param name="TextId">The GUID of the Text whose words and human decisions are compared.</param>
/// <param name="JobId">The completed Trial job; omitted takes the Proposal's latest completed Trial.</param>
public sealed record ProposalDifferenceProduceRequest(string ProjectPath, string ProposalId, string TextId,
    string? JobId = null);

/// <summary>Reads a saved Proposal Difference: its summary first, then one page of changed words.</summary>
/// <param name="ProjectPath">The recorded project path.</param>
/// <param name="ProposalId">The Proposal whose newest Difference is read; ignored when a Difference id is given.</param>
/// <param name="DifferenceId">The saved Difference to read.</param>
/// <param name="CouldBeBad">Whether to list only words filed under a could-be-bad category.</param>
/// <param name="Bucket">The stable category code that listed words must belong to.</param>
/// <param name="BeforeState">The approval state a listed word had before the Proposal.</param>
/// <param name="AfterState">The approval state a listed word has after the Proposal.</param>
/// <param name="Offset">How many matching words to skip.</param>
/// <param name="Limit">How many matching words to return.</param>
public sealed record ProposalDifferenceShowRequest(string ProjectPath, string? ProposalId = null,
    string? DifferenceId = null, bool CouldBeBad = false, string? Bucket = null, string? BeforeState = null,
    string? AfterState = null, int Offset = 0, int Limit = 25);
