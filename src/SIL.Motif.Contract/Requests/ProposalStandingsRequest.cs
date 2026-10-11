namespace SIL.Motif.Contract.Requests;

/// <summary>Lists every Proposal with its latest Trial's numbers beside today's.</summary>
/// <param name="ProjectPath">The FieldWorks project file.</param>
/// <param name="IncludeSetAside">Whether closed (applied, rejected, superseded, withdrawn) Proposals are listed too.</param>
public sealed record ProposalStandingsRequest(string ProjectPath, bool IncludeSetAside = false);
