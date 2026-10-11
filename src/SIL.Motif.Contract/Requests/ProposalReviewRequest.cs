namespace SIL.Motif.Contract.Requests;

/// <summary>Reads one Proposal's changes as a single ordered list for a person to walk.</summary>
/// <param name="ProjectPath">The FieldWorks project whose Motif store holds the Proposal.</param>
/// <param name="ProposalId">The Proposal to read, or <see langword="null"/> for the pending Draft.</param>
public sealed record ProposalReviewRequest(string ProjectPath, string? ProposalId);
