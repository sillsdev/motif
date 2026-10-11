namespace SIL.Motif.Contract.Requests;

/// <summary>Which project's grammar to check; the grammar is read from its current Baseline.</summary>
/// <param name="ProjectPath">The recorded project path.</param>
/// <param name="ProposalId">The Proposal whose latest Trial grammar check to read, or null to check the project.</param>
/// <param name="StoredAnalyses">
/// How much of the stored-analysis comparison to run for this check, or null to use the per-user
/// Default proposal checks choice.
/// </param>
public sealed record GrammarCheckRequest(
    string ProjectPath, string? ProposalId = null, StoredAnalysisComparison? StoredAnalyses = null);
