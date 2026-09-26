using System.Text.Json.Serialization;

namespace SIL.Motif.Contract.Responses;

/// <summary>The outcome of applying the current pending changes, and the success document of
/// <c>apply --all-pending --json</c>.</summary>
/// <remarks>
/// Nothing pending is a success, not a refusal: it serializes as <c>{"ok":true,"applied":false}</c>. An Apply
/// that ran serializes as <c>{"ok":true,"applied":true,"receipt":{…}}</c>. <see cref="Applied"/> is true
/// exactly when <see cref="Receipt"/> is present; the factories are the only way to build one, and
/// deserializing a document that breaks that rule throws.
/// </remarks>
public sealed record ApplyPendingResult
{
    [JsonConstructor]
    private ApplyPendingResult(bool applied, ApplyProjection? receipt)
    {
        if (applied != receipt is not null)
            throw new ArgumentException("A pending Apply has a Receipt exactly when it applied something.",
                nameof(receipt));
        Applied = applied;
        Receipt = receipt;
    }

    /// <summary>The result when there were no pending changes, so nothing was applied.</summary>
    public static ApplyPendingResult NothingPending { get; } = new(false, null);

    /// <summary>The result of an Apply that ran and recorded <paramref name="receipt"/>.</summary>
    public static ApplyPendingResult AppliedWith(ApplyProjection receipt) =>
        new(true, receipt ?? throw new ArgumentNullException(nameof(receipt)));

    /// <summary>Always true, so a reader can tell this document from a failure envelope.</summary>
    [JsonPropertyOrder(0)] public bool Ok => true;

    /// <summary>Whether Motif applied a Proposal.</summary>
    [JsonPropertyOrder(1)] public bool Applied { get; }

    /// <summary>The recorded apply result, or <see langword="null"/> when nothing was pending.</summary>
    [JsonPropertyOrder(2)] public ApplyProjection? Receipt { get; }
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
/// Reports completed work while Motif measures words for one pending revision. It names no current word:
/// words may be parsed in parallel, so no one word is the one being checked.
/// </summary>
/// <param name="Completed">The number of words whose Trial work has finished.</param>
/// <param name="Total">The number of words requested.</param>
public sealed record MeasureProgress(int Completed, int Total);
