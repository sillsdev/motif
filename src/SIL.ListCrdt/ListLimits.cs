namespace SIL.ListCrdt;

/// <summary>
/// Bounds on remote input. Anything beyond a bound is refused with a <see cref="ListLimitException"/> before the
/// state changes; split oversize batches into pieces of at most <see cref="MaxBatchOperations"/> operations.
/// </summary>
public sealed record ListLimits
{
    /// <summary>Defaults sized for the 34,383-item text with generous headroom.</summary>
    public static ListLimits Default { get; } = new();

    /// <summary>Longest element identity or replica name, in UTF-16 code units.</summary>
    public int MaxIdentifierLength { get; init; } = 1024;

    /// <summary>Most operations held while a dependency is missing. A new operation that would be held past this is refused.</summary>
    public int MaxPendingOperations { get; init; } = 262_144;

    /// <summary>Largest accepted counter or Lamport time. A far-future value would stall the authoring clock for good (JSON-safe integer bound by default).</summary>
    public long MaxClock { get; init; } = 9_007_199_254_740_991;

    /// <summary>Longest accepted serialised operation or row, in UTF-16 code units, checked before parsing.</summary>
    public int MaxOperationJsonLength { get; init; } = 65_536;

    /// <summary>Most operations accepted in one serialised array.</summary>
    public int MaxBatchOperations { get; init; } = 10_000;

    /// <summary>Longest accepted serialised array, in UTF-16 code units, checked before parsing.</summary>
    public int MaxBatchJsonLength { get; init; } = 16 * 1024 * 1024;

    internal void Check(ListOp op)
    {
        if (op.Element.Length > MaxIdentifierLength)
            throw new ListLimitException(nameof(MaxIdentifierLength), MaxIdentifierLength,
                $"The element identity has {op.Element.Length} characters; the limit is {MaxIdentifierLength}.");
        CheckId(op.Id);
        if (op.LeftOrigin is { } left) CheckId(left);
        if (op.RightOrigin is { } right) CheckId(right);
        if (op.Lamport > MaxClock)
            throw new ListLimitException(nameof(MaxClock), MaxClock,
                $"The Lamport time {op.Lamport} is beyond the limit {MaxClock}; refusing a far-future operation.");
    }

    void CheckId(NodeId id)
    {
        if (id.Replica.Length > MaxIdentifierLength)
            throw new ListLimitException(nameof(MaxIdentifierLength), MaxIdentifierLength,
                $"A replica name has {id.Replica.Length} characters; the limit is {MaxIdentifierLength}.");
        if (id.Counter > MaxClock)
            throw new ListLimitException(nameof(MaxClock), MaxClock,
                $"The counter {id.Counter} is beyond the limit {MaxClock}; refusing a far-future operation.");
    }

    internal void CheckJson(string json, bool batch = false)
    {
        long max = batch ? MaxBatchJsonLength : MaxOperationJsonLength;
        if (json.Length > max)
            throw new ListLimitException(batch ? nameof(MaxBatchJsonLength) : nameof(MaxOperationJsonLength), max,
                $"The serialised {(batch ? "batch" : "operation")} has {json.Length} characters; the limit is {max}.");
    }
}

/// <summary>Remote input exceeded a <see cref="ListLimits"/> bound; the state was not changed.</summary>
public sealed class ListLimitException(string limitName, long limit, string message) : ArgumentException(message)
{
    /// <summary>The <see cref="ListLimits"/> property that was exceeded.</summary>
    public string LimitName { get; } = limitName;
    public long Limit { get; } = limit;
}
