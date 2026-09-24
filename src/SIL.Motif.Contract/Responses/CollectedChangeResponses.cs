namespace SIL.Motif.Contract.Responses;

/// <summary>The saved Draft after one change was added, replaced, or removed.</summary>
public sealed record CollectedChangeResponse(string DraftName, string ProposalId, int OperationCount);

/// <summary>The fit of all collected changes in one Proposal.</summary>
public sealed record PreflightResponse(string ProposalId, IReadOnlyList<ChangeFitResult> Changes);

/// <summary>The fit of one operation against its authored wordform and analysis.</summary>
public sealed record ChangeFitResult(string OperationId, bool StillFits, string Reason, string BaselineToken);
