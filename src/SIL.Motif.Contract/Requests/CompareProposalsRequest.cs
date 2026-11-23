namespace SIL.Motif.Contract.Requests;

/// <summary>Compares two to eight Proposals by their Trial before/after evidence and the approved analyses each keeps and loses; it measures nothing and changes nothing.</summary>
/// <param name="ProjectPath">The known project whose paired store retains the Trial evidence.</param>
/// <param name="ProposalIds">Two to eight distinct Proposal IDs. Each uses its newest completed Trial unless <paramref name="Jobs"/> pins one.</param>
/// <param name="At">The Baseline digest every Trial must have been measured on. Omit to use the one they share.</param>
/// <param name="Snapshot">The snapshot token from an earlier summary. Page calls pass it so every page reads the same Trials.</param>
/// <param name="Page">The one-based page of form rows to read. Omit for the compact summary.</param>
/// <param name="Jobs">Trial job IDs that pin a Proposal's column to that exact completed Trial. At most one per Proposal, each naming one of the Proposals; the rest use their newest.</param>
public sealed record CompareProposalsRequest(string ProjectPath, IReadOnlyList<string> ProposalIds,
    string? At = null, int? Page = null, string? Snapshot = null, IReadOnlyList<string>? Jobs = null);
