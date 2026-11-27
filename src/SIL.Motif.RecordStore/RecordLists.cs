using System.Globalization;
using System.Text;
using SIL.Harmony.Core;
using SIL.Harmony.Entities;
using SIL.Harmony.Changes;
using SIL.Harmony;
using SIL.ListCrdt;

namespace SIL.Motif.RecordStore;

/// <summary>
/// The list package bounds the store passes. Element identities are a 36-character GUID plus an optional
/// operation suffix (at most about 90 characters); replica names are 36-character client GUIDs. The pending
/// bound is more than seven times the largest list in the Cebuano project (34,383 paragraphs).
/// </summary>
internal static class StoreListLimits
{
    public static readonly ListLimits Default = new() { MaxIdentifierLength = 128 };
}

/// <summary>A move that lost its position register. The store keeps it as a conflict copy and never judges concurrency.</summary>
public sealed record ListConflict(Guid Owner, int Field, Guid Item, string Author, long Counter, long Lamport,
    string WinnerAuthor, long WinnerCounter);

/// <summary>A checkpoint row of kind node, pending or conflict ; the checkpoint builder consumes these rows.</summary>
public sealed record ListCheckpointRow(string Kind, string Key, string Json);

/// <summary>
/// The ordered contents of one list field as list CRDT operations. Items imported in order are the initial
/// chain, held as bare GUIDs; every later operation is retained verbatim, including tombstones, abandoned
/// positions, losing moves and operations whose dependencies have not arrived.
/// </summary>
internal sealed class ListHistory
{
    public List<Guid> Initial { get; } = [];
    public List<ListOp> Ops { get; } = [];
    private Dictionary<Guid, int>? occurrences;
    private ListState? state;
    private bool shared;

    /// <summary>For each retained move, the highest counter of every replica its author had received.</summary>
    public Dictionary<NodeId, IReadOnlyDictionary<string, long>> Seen { get; } = [];

    public static string ElementOf(Guid item, int occurrence) =>
        occurrence == 0 ? item.ToString("D") : item.ToString("D") + "~i" + occurrence.ToString(CultureInfo.InvariantCulture);

    public static Guid ItemOf(string element)
    {
        if (element.Length < 36 || !Guid.TryParseExact(element.AsSpan(0, 36), "D", out var item) ||
            element.Length > 36 && element[36] != '~')
            throw new ArgumentException("A list element identity must begin with the item GUID.", nameof(element));
        return item;
    }

    /// <summary>Folds performed by <see cref="State"/> on this thread; a copy must not cause one.</summary>
    [ThreadStatic] internal static int StateBuilds;

    /// <summary>
    /// Copies share the folded state until one of them applies an operation, so a snapshot copy followed by
    /// one operation costs one state clone and never a fold from the initial chain.
    /// </summary>
    public ListHistory Clone()
    {
        var copy = new ListHistory();
        copy.Initial.AddRange(Initial);
        copy.Ops.AddRange(Ops);
        foreach (var pair in Seen) copy.Seen[pair.Key] = pair.Value;
        if (state is not null)
        {
            copy.state = state;
            copy.shared = true;
            shared = true;
        }
        return copy;
    }

    private Dictionary<Guid, int> Occurrences()
    {
        if (occurrences is not null) return occurrences;
        occurrences = [];
        foreach (var item in Initial) occurrences[item] = occurrences.GetValueOrDefault(item) + 1;
        return occurrences;
    }

    public ListState State()
    {
        if (state is not null) return state;
        var seen = new Dictionary<Guid, int>();
        var elements = Initial.Select(item =>
        {
            var n = seen.GetValueOrDefault(item);
            seen[item] = n + 1;
            return ElementOf(item, n);
        });
        StateBuilds++;
        var built = ListState.FromInitial(elements, StoreListLimits.Default);
        foreach (var op in Ops) built.Apply(op);
        return state = built;
    }

    /// <summary>
    /// Applies one operation. Refused input (malformed identity, beyond a limit, a reused item identity or operation
    /// identity) changes nothing and reports <see cref="ListOutcome.Refused"/>; it never throws.
    /// </summary>
    public ListOutcome TryApply(ListOp op, IReadOnlyDictionary<string, long>? seen = null)
    {
        try
        {
            op.Validate();
            ItemOf(op.Element);
            if (Ops.Count == 0 && IsInitialAppend(op))
            {
                var item = ItemOf(op.Element);
                Initial.Add(item);
                Occurrences()[item] = Occurrences().GetValueOrDefault(item) + 1;
                state = null;
                return ListOutcome.Applied;
            }
            var folded = State();
            if (shared) { folded = state = folded.Clone(); shared = false; }
            if (folded.Apply(op) == ApplyResult.Duplicate) return ListOutcome.Duplicate;
            Ops.Add(op);
            if (op.Kind == ListOpKind.Move && seen is { Count: > 0 }) Seen[op.Id] = seen;
            return ListOutcome.Applied;
        }
        catch (ArgumentException) { return ListOutcome.Refused; }
        catch (InvalidOperationException) { return ListOutcome.Refused; }
    }

    /// <summary>
    /// Losing moves whose winner's author had not received them: a true conflict. A sequential supersession (the
    /// winner's author had already seen the loser) is not reported. A move without a recorded view is reported.
    /// </summary>
    public IEnumerable<ListOp> ConcurrentLosingMoves()
    {
        if (Ops.Count == 0) yield break;
        var folded = State();
        var byId = Ops.ToDictionary(op => op.Id);
        foreach (var loser in folded.LosingMoves)
        {
            var winner = byId.GetValueOrDefault(folded.PositionOf(loser.Element));
            if (winner is null || winner.Kind != ListOpKind.Move) continue;
            if (Seen.TryGetValue(winner.Id, out var view) && view.GetValueOrDefault(loser.Id.Replica) >= loser.Id.Counter) continue;
            yield return loser;
        }
    }

    private bool IsInitialAppend(ListOp op)
    {
        if (op.Kind != ListOpKind.Insert || op.Lamport != 0 || op.Id.Replica != ListState.InitialReplica ||
            op.Id.Counter != Initial.Count + 1 || op.RightOrigin is not null) return false;
        var previous = Initial.Count == 0 ? (NodeId?)null : new NodeId(ListState.InitialReplica, Initial.Count);
        if (op.LeftOrigin != previous) return false;
        var item = ItemOf(op.Element);
        return op.Element == ElementOf(item, Occurrences().GetValueOrDefault(item));
    }

    public ListOp NextInitial(Guid item) => ListOp.Insert(new NodeId(ListState.InitialReplica, Initial.Count + 1), 0,
        ElementOf(item, Occurrences().GetValueOrDefault(item)),
        Initial.Count == 0 ? null : new NodeId(ListState.InitialReplica, Initial.Count), null);

    /// <summary>Collections are sets: a concurrent add of the same target shows once.</summary>
    public List<Guid> Visible(bool distinct)
    {
        if (Ops.Count == 0) return Initial;
        var items = State().Elements().Select(ItemOf);
        return (distinct ? items.Distinct() : items).ToList();
    }
}

internal enum ListOutcome { Applied, Duplicate, Refused }

/// <summary>One list operation addressed to an owner record and field.</summary>
internal sealed class ListRecordOp(Guid entityId, int field, ListOp operation, IReadOnlyDictionary<string, long>? seen = null)
    : EditChange<GenericRecord>(entityId), ISelfNamedType<ListRecordOp>
{
    public int Field { get; } = field;
    public ListOp Operation { get; } = operation;
    public IReadOnlyDictionary<string, long> Seen { get; } = seen ?? new Dictionary<string, long>();

    /// <summary>A refused operation changes nothing on any replica; commit-time checks reject it first.</summary>
    public override ValueTask ApplyChange(GenericRecord entity, IChangeContext context)
    {
        entity.ApplyListOp(Field, Operation, Seen);
        return ValueTask.CompletedTask;
    }
}

/// <summary>Authors list operations from a record's current list state for one client (replica).</summary>
internal static class ListAuthoring
{
    public static ListRecordOp Insert(GenericRecord record, int field, Guid client, Guid item, int index)
    {
        var (replica, state) = Begin(record, field, client);
        var element = item.ToString("D") + "~" + replica.ReplicaId + "." +
            (state.MaximumCounter(replica.ReplicaId) + 1).ToString(CultureInfo.InvariantCulture);
        return new ListRecordOp(record.Id, field, replica.InsertAt(index, element));
    }

    /// <summary>Returns null when the item is already at the requested final index.</summary>
    public static ListRecordOp? Move(GenericRecord record, int field, Guid client, int fromIndex, int toIndex)
    {
        var (replica, state) = Begin(record, field, client);
        var seen = View(state);
        var op = replica.MoveToIndex(state.ElementAt(fromIndex), toIndex);
        return op is null ? null : new ListRecordOp(record.Id, field, op, seen);
    }

    public static ListRecordOp Delete(GenericRecord record, int field, Guid client, int index)
    {
        var (replica, state) = Begin(record, field, client);
        return new ListRecordOp(record.Id, field, replica.Delete(state.ElementAt(index)));
    }

    /// <summary>Per replica, the longest fully applied counter prefix 1..n (a causal prefix).</summary>
    private static Dictionary<string, long> View(ListState state)
    {
        var view = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var group in state.AppliedOperations.Where(o => o.Id.Replica != ListState.InitialReplica).GroupBy(o => o.Id.Replica))
        {
            var applied = group.Select(o => o.Id.Counter).ToHashSet();
            long prefix = 0;
            while (applied.Contains(prefix + 1)) prefix++;
            if (prefix > 0) view[group.Key] = prefix;
        }
        return view;
    }

    private static (ListReplica, ListState) Begin(GenericRecord record, int field, Guid client)
    {
        var state = record.HistoryFor(field).State().Clone();
        return (new ListReplica(client.ToString("D"), state), state);
    }
}

/// <summary>The CRDT-backed list seam; <see cref="GenericRecord.Values"/> keeps the visible order for readers.</summary>
internal sealed class CrdtRecordList : IRecordList
{
    public IReadOnlyList<Guid> Read(GenericRecord record, int field) =>
        record.Values.TryGetValue(field.ToString(CultureInfo.InvariantCulture), out var value)
            ? value.EnumerateArray().Select(x => x.GetGuid()).ToArray() : [];

    public IReadOnlyList<ListConflict> Conflicts(GenericRecord record, int field)
    {
        if (!record.Lists.TryGetValue(field, out var history) || history.Ops.Count == 0) return [];
        var state = history.State();
        return history.ConcurrentLosingMoves().Select(op =>
        {
            var winner = state.PositionOf(op.Element);
            return new ListConflict(record.Id, field, ListHistory.ItemOf(op.Element), op.Id.Replica, op.Id.Counter,
                op.Lamport, winner.Replica, winner.Counter);
        }).ToArray();
    }

    public IReadOnlyList<ListCheckpointRow> CheckpointRows(GenericRecord record, int field)
    {
        var history = record.HistoryFor(field);
        var state = history.State();
        var prefix = record.Id.ToString("N") + field.ToString("x8", CultureInfo.InvariantCulture);
        var applied = state.AppliedOperations.Select(op => op.Id).ToHashSet();
        var rows = new List<ListCheckpointRow>();
        foreach (var row in state.ExportRows())
        {
            var op = NodeRowEncoding.Decode(row, state.Limits);
            var key = prefix + Convert.ToHexString(Encoding.UTF8.GetBytes(row.Key)).ToLowerInvariant();
            rows.Add(new(applied.Contains(op.Id) ? "node" : "pending", key, row.Json));
        }
        foreach (var loser in history.ConcurrentLosingMoves())
        {
            var row = NodeRowEncoding.Encode(loser);
            rows.Add(new("conflict", prefix + Convert.ToHexString(Encoding.UTF8.GetBytes(row.Key)).ToLowerInvariant(), row.Json));
        }
        return rows.OrderBy(r => r.Kind, StringComparer.Ordinal).ThenBy(r => r.Key, StringComparer.Ordinal).ToArray();
    }
}
