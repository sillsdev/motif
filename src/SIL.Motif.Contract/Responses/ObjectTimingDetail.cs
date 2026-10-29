namespace SIL.Motif.Contract.Responses;

/// <summary>Whether object timing detail covers the words represented by a result.</summary>
public sealed record ObjectTimingDetail(bool IsComplete, string? Description = null)
{
    /// <summary>The number of costliest words with detail in each incomplete contributing run.</summary>
    public IReadOnlyList<int> DetailWordCounts { get; init; } = [];
    /// <summary>Whether one contributing run retried words after recording its timing facts.</summary>
    public bool HasRetriedWords { get; init; }
    /// <summary>Whether a contributing run had no object statistics cache.</summary>
    public bool StatisticsUnavailable { get; init; }
}
