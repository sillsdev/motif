namespace SIL.Motif.Contract.Responses;

/// <summary>The number of change entries in a Proposal's current Draft.</summary>
/// <param name="EntryCount">The count after grouping authored word changes and grammar operations.</param>
public sealed record ProposalReviewEntryCountResponse(int EntryCount);
