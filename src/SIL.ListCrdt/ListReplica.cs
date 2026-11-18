namespace SIL.ListCrdt;

/// <summary>
/// Authors operations for one exclusively owned replica name. Reopen only after loading all previously authored
/// operations, or use a fresh name. The state and this helper require serialized access.
/// </summary>
public sealed class ListReplica
{
    public ListReplica(string replicaId, ListState? state = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(replicaId);
        if (replicaId.Contains('\0')) throw new ArgumentException("Replica names cannot contain NUL.", nameof(replicaId));
        ReplicaId = replicaId;
        State = state ?? new ListState();
    }

    public string ReplicaId { get; }
    public ListState State { get; }

    NodeId NextId() => new(ReplicaId, checked(State.MaximumCounter(ReplicaId) + 1));
    long NextTime() => checked(State.MaximumLamport + 1);

    ListOp Record(ListOp op)
    {
        if (State.Apply(op) != ApplyResult.Applied)
            throw new InvalidOperationException("Locally authored operation was not applied.");
        return op;
    }

    void RequireLive(string element)
    {
        if (!State.Contains(element) || State.IsDeleted(element))
            throw new ArgumentException("An authored edit requires a live item.", nameof(element));
    }

    /// <summary>Use a fresh item identity; a tombstoned identity cannot be inserted again.</summary>
    public ListOp Insert(string element, string? afterElement)
    {
        var (left, right) = State.OriginsAfterElement(afterElement);
        return Record(ListOp.Insert(NextId(), NextTime(), element, left, right));
    }

    public ListOp InsertAt(int index, string element)
    {
        var (left, right) = State.OriginsAtIndex(index);
        return Record(ListOp.Insert(NextId(), NextTime(), element, left, right));
    }

    /// <summary>Returns null when the item already follows the requested live predecessor.</summary>
    public ListOp? Move(string element, string? afterElement)
    {
        RequireLive(element);
        if (afterElement == element) throw new ArgumentException("Cannot move an item after itself.", nameof(afterElement));
        if (afterElement is not null) RequireLive(afterElement);
        string? previous = null;
        foreach (var current in State.Elements())
        {
            if (current == element)
            {
                if (previous == afterElement) return null;
                break;
            }
            previous = current;
        }
        var (left, right) = State.OriginsAfterElement(afterElement);
        return Record(ListOp.Move(NextId(), NextTime(), element, left, right));
    }

    /// <summary>
    /// The target is the final zero-based live index after removing the item from its previous place.
    /// Returns null without advancing clocks or creating a position when the index is unchanged.
    /// </summary>
    public ListOp? MoveToIndex(string element, int index)
    {
        RequireLive(element);
        int currentIndex = 0;
        foreach (var current in State.Elements())
        {
            if (current == element) break;
            currentIndex++;
        }
        var (left, right) = State.OriginsAtIndex(index, ignore: element);
        if (currentIndex == index) return null;
        return Record(ListOp.Move(NextId(), NextTime(), element, left, right));
    }

    /// <summary>
    /// Authors a batch of individual moves with one Lamport time. An unchanged contiguous live placement
    /// returns an empty array before allocating stamps. This is not an atomic range move: concurrent inserts
    /// stay attached to their original position anchors.
    /// </summary>
    public ListOp[] MoveBlock(IReadOnlyList<string> elements, string? afterElement)
    {
        ArgumentNullException.ThrowIfNull(elements);
        if (elements.Distinct(StringComparer.Ordinal).Count() != elements.Count ||
            (afterElement is not null && elements.Contains(afterElement)))
            throw new ArgumentException("A block requires distinct items and an external predecessor.", nameof(elements));
        foreach (var element in elements) RequireLive(element);
        if (afterElement is not null) RequireLive(afterElement);
        if (elements.Count == 0) return [];
        var live = State.Elements();
        if (afterElement is not null)
            live = live.SkipWhile(element => element != afterElement).Skip(1);
        if (live.Take(elements.Count).SequenceEqual(elements, StringComparer.Ordinal)) return [];
        long time = NextTime();
        var ops = new ListOp[elements.Count];
        var (left, right) = State.OriginsAfterElement(afterElement);
        for (int i = 0; i < elements.Count; i++)
        {
            ops[i] = Record(ListOp.Move(NextId(), time, elements[i], left, right));
            (left, right) = State.OriginsAfterNode(ops[i].Id);
        }
        return ops;
    }

    public ListOp Delete(string element)
    {
        RequireLive(element);
        return Record(ListOp.Delete(NextId(), NextTime(), element));
    }

    public ApplyResult Receive(ListOp op) => State.Apply(op);
}
