namespace SIL.Motif.Contract.Requests;

/// <summary>Declines a Proposal: a person says no, and it is kept with its Trials as history.</summary>
/// <param name="ProjectPath">The FieldWorks project file.</param>
/// <param name="ProposalId">The Proposal to decline.</param>
/// <param name="Reason">An optional Comment saved as the Proposal is declined.</param>
public sealed record DeclineProposalRequest(string ProjectPath, string ProposalId, string? Reason = null);

/// <summary>Opens a declined or withdrawn Proposal again as a Draft under the same id.</summary>
/// <param name="ProjectPath">The FieldWorks project file.</param>
/// <param name="ProposalId">The Proposal to reopen.</param>
public sealed record ReopenProposalRequest(string ProjectPath, string ProposalId);

/// <summary>Withdraws a Proposal: its author takes it back before review. A person may reopen it.</summary>
/// <param name="ProjectPath">The FieldWorks project file.</param>
/// <param name="ProposalId">The Proposal to withdraw.</param>
public sealed record WithdrawProposalRequest(string ProjectPath, string ProposalId);
