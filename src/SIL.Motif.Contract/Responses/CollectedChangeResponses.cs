using System.Text.Json.Serialization;

namespace SIL.Motif.Contract.Responses;

/// <summary>The fit of all collected changes in one Proposal.</summary>
public sealed record PreflightResponse(string ProposalId, IReadOnlyList<ChangeFitResult> Changes);

/// <summary>The fit of one operation against its authored wordform and analysis.</summary>
public sealed record ChangeFitResult
{
    [JsonConstructor]
    public ChangeFitResult(string operationId, string status, string reason, string baselineToken)
    {
        OperationId = operationId;
        Status = status;
        Reason = reason;
        BaselineToken = baselineToken;
    }

    public ChangeFitResult(string operationId, bool stillFits, string reason, string baselineToken)
        : this(operationId, stillFits ? ChangeFitStatus.Fits : ChangeFitStatus.NoLongerFits,
            reason, baselineToken) { }

    public string OperationId { get; init; }

    /// <summary>Whether the result is in the <see cref="ChangeFitStatus.Fits"/> state.</summary>
    public bool StillFits => Status == ChangeFitStatus.Fits;

    public string Reason { get; init; }

    public string BaselineToken { get; init; }

    /// <summary>The authored pending change that produced this operation, when it was collected as one.</summary>
    public string? ChangeId { get; init; }

    /// <summary>The machine-readable fit state: fits, uncertain, or no-longer-fits.</summary>
    public string Status { get; init; }

    /// <summary>The sentence-token context to inspect when this operation is uncertain.</summary>
    public ChangeUncertainty? Uncertainty { get; init; }
}
