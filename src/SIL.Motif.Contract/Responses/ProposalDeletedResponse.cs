namespace SIL.Motif.Contract.Responses;

/// <summary>A deleted Proposal and how many of its Trials went with it.</summary>
/// <param name="ProposalId">The deleted Proposal's canonical id.</param>
/// <param name="TrialsDeleted">How many Trial records were removed with it.</param>
public sealed record ProposalDeletedResponse(string ProposalId, int TrialsDeleted);
