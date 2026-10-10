namespace SIL.Motif.Contract.Requests;

/// <summary>Reads the latest Overview matching the current Baseline and resolved default Selection.</summary>
/// <param name="ProjectPath">The recorded project path.</param>
/// <param name="ProposalId">The Proposal whose latest Trial supplies the evidence, or null for the project.</param>
public sealed record OverviewRequest(string ProjectPath, string? ProposalId = null);
