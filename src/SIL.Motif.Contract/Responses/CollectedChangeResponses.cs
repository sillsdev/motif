namespace SIL.Motif.Contract.Responses;

/// <summary>The fit of all collected changes in one Proposal.</summary>
public sealed record PreflightResponse(string ProposalId, IReadOnlyList<ChangeFitResult> Changes);

/// <summary>The fit of one operation against its authored wordform and analysis.</summary>
public sealed record ChangeFitResult(string OperationId, bool StillFits, string Reason, string BaselineToken)
{
    /// <summary>The authored pending change that produced this operation, when it was collected as one.</summary>
    public string? ChangeId { get; init; }

    /// <summary>The machine-readable fit state: fits, uncertain, or no-longer-fits.</summary>
    public string Status { get; init; } = StillFits ? "fits" : "no-longer-fits";

    /// <summary>The sentence-token context to inspect when this operation is uncertain.</summary>
    public ChangeUncertainty? Uncertainty { get; init; }
}
