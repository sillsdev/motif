using System.Globalization;

namespace SIL.ListCrdt;

/// <summary>
/// One independently stored position or delete-tombstone operation. Scope the key by list identity in the
/// enclosing store. Neither this key nor the JSON contains the other items in the sequence.
/// </summary>
public sealed record ListNodeRow(int Version, string Key, string Json);

/// <summary>
/// Append-only rows retaining anchors, losing moves, tombstones and missing-dependency operations.
/// A baseline has one row per item; each later insert, move or delete adds one row. This encoding retains
/// history without compaction. Store rows and their enclosing commit atomically before acknowledging edits.
/// </summary>
public static class NodeRowEncoding
{
    public const int Version = 1;

    static string KeyOf(NodeId id) => CanonicalJson.Write(new object?[]
    {
        id.Replica, id.Counter.ToString(CultureInfo.InvariantCulture)
    });

    public static ListNodeRow Encode(ListOp operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        operation.Validate();
        return new ListNodeRow(Version, KeyOf(operation.Id), operation.ToJson());
    }

    public static ListOp Decode(ListNodeRow row, ListLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (row.Version != Version) throw new FormatException("Unsupported list row version.");
        limits ??= ListLimits.Default;
        limits.CheckJson(row.Key);
        var operation = OpJson.FromJson(row.Json, limits);
        operation.Validate();
        if (!string.Equals(row.Key, KeyOf(operation.Id), StringComparison.Ordinal))
            throw new FormatException("The list row key does not match its operation identity.");
        return operation;
    }

    /// <summary>
    /// Reconstructs a list from rows in any order, retaining operations with absent dependencies. Rows are
    /// streamed one at a time and each is checked against <paramref name="limits"/> before it is applied.
    /// </summary>
    public static ListState Restore(IEnumerable<ListNodeRow> rows, ListLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var state = new ListState(limits);
        foreach (var row in rows) state.Apply(Decode(row, state.Limits));
        return state;
    }

    public static IEnumerable<ListNodeRow> ExportRows(this ListState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state.ReceivedOperations.Select(Encode);
    }
}
