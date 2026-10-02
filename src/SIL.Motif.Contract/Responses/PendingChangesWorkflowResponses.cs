using System.Text.Json.Serialization;

namespace SIL.Motif.Contract.Responses;

/// <summary>The outcome of applying the current pending changes, and the success document of
/// <c>apply --all-pending --json</c>.</summary>
/// <remarks>
/// Nothing pending is a success, not a refusal: it returns a summary and no <see cref="Receipt"/>.
/// <see cref="Applied"/> is true exactly when a receipt is present; inconsistent documents throw.
/// </remarks>
public sealed record ApplyPendingResult
{
    [JsonConstructor]
    private ApplyPendingResult(bool applied, ApplyProjection? receipt, string summary)
    {
        if (applied != receipt is not null)
            throw new ArgumentException("A pending Apply has a Receipt exactly when it applied something.",
                nameof(receipt));
        if (string.IsNullOrWhiteSpace(summary))
            throw new ArgumentException("A pending Apply requires a display summary.", nameof(summary));
        Applied = applied;
        Receipt = receipt;
        Summary = summary;
    }

    /// <summary>The result when there were no pending changes, so nothing was applied.</summary>
    public static ApplyPendingResult NothingPending { get; } = new(false, null, "Nothing to apply.");

    /// <summary>The result of an Apply that ran, recorded <paramref name="receipt"/>, and summarized its changes.</summary>
    public static ApplyPendingResult AppliedWith(ApplyProjection receipt, string summary) =>
        new(true, receipt ?? throw new ArgumentNullException(nameof(receipt)), summary);

    /// <summary>Always true, so a reader can tell this document from a failure envelope.</summary>
    [JsonPropertyOrder(0)] public bool Ok => true;

    /// <summary>Whether Motif applied a Proposal.</summary>
    [JsonPropertyOrder(1)] public bool Applied { get; }

    /// <summary>The recorded Receipt; null when nothing was pending.</summary>
    [JsonPropertyOrder(2)] public ApplyProjection? Receipt { get; }

    /// <summary>Short text a person can use to understand the pending changes that were applied.</summary>
    [JsonPropertyOrder(3)] public string Summary { get; }
}

/// <summary>The outcome of measuring one pending revision.</summary>
/// <param name="JobId">The Trial job that produced this outcome.</param>
/// <param name="Revision">The pending revision used for the Trial.</param>
/// <param name="Numbers">What the Trial did to the touched words' approved analyses.</param>
public sealed record MeasurePendingResult(string JobId, string Revision, ReviewNumbersResponse Numbers)
{
    /// <summary>Whether every requested word produced complete correctness evidence.</summary>
    public bool EvidenceComplete => Numbers.EvidenceComplete;
}

/// <summary>
/// Reports completed work while Motif measures words for one pending revision, including the worker's
/// latest sequential word evidence when parsing has begun.
/// </summary>
/// <param name="Completed">The number of words whose Trial work has finished.</param>
/// <param name="Total">The number of words requested.</param>
public sealed record MeasureProgress(int Completed, int Total)
{
    public SIL.Motif.Contract.Jobs.TrialWordProgress? WordProgress { get; init; }
}
