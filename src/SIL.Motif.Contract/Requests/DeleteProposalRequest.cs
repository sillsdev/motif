namespace SIL.Motif.Contract.Requests;

/// <summary>Deletes a declined, superseded or withdrawn Proposal that never applied, with its Trials.</summary>
/// <param name="ProjectPath">The FieldWorks project file.</param>
/// <param name="ProposalId">The Proposal to delete.</param>
public sealed record DeleteProposalRequest(string ProjectPath, string ProposalId);
