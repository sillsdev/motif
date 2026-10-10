namespace SIL.Motif.Contract.Requests;

/// <summary>Reads the latest Trial recorded for one Proposal.</summary>
/// <param name="ProjectPath">The FieldWorks project whose Motif store holds the Trial.</param>
/// <param name="ProposalId">The Proposal whose latest Trial is requested.</param>
/// <param name="IncludeWordResults">Whether to include the potentially large root Assessment word rows.</param>
public sealed record ReadTrialRequest(string ProjectPath, string ProposalId, bool IncludeWordResults = false);
