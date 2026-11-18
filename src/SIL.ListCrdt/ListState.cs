namespace SIL.ListCrdt;

public enum ApplyResult
{
    /// <summary>The operation changed the state (and possibly released buffered operations).</summary>
    Applied,
    /// <summary>The operation (or one with the same id) was already applied or is already buffered.</summary>
    Duplicate,
    /// <summary>A causal dependency (origin node or element) has not arrived; the operation is held.</summary>
    Buffered,
}

/// <summary>
/// State of one ordered list: a Fugue tree of position nodes (Weidner and Kleppmann) plus, per element,
/// a last-writer-wins register naming its current position node (Kleppmann, "Moving elements in list CRDTs").
/// <para>Applying the same set of operations in any order gives the same state, provided missing causal
/// dependencies are eventually delivered: operations that arrive before their origin nodes or element
/// are buffered, never dropped or reordered by timestamps.</para>
/// <para>No external dependencies. Not thread safe.</para>
/// </summary>
public sealed class ListState
{
    /// <summary>Replica name used by <see cref="FromInitial"/> for baseline nodes.</summary>
    public const string InitialReplica = "";

    sealed class Node(NodeId id, Node? parent, bool isRight, string? element)
    {
        public readonly NodeId Id = id;
        public readonly Node? Parent = parent;
        public readonly bool IsRight = isRight;
        public readonly int Depth = parent is null ? 0 : parent.Depth + 1;
        public readonly string? Element = element;
        public List<Node>? LeftChildren;
        public List<Node>? RightChildren;
    }

    sealed class Elem(Node current, long lamport, NodeId stamp)
    {
        public Node Current = current;
        public long Lamport = lamport;
        public NodeId Stamp = stamp;
        public bool Deleted;
    }

    readonly Node root = new(new NodeId("\u0000root", 0), null, false, null);
    readonly Dictionary<NodeId, Node> nodes = [];
    readonly Dictionary<string, Elem> elems = new(StringComparer.Ordinal);
    readonly HashSet<NodeId> appliedIds = [];
    readonly HashSet<NodeId> bufferedIds = [];
    readonly Dictionary<NodeId, List<ListOp>> waitingForNode = [];
    readonly Dictionary<string, List<ListOp>> waitingForElement = new(StringComparer.Ordinal);
    readonly List<ListOp> log = [];
    readonly Dictionary<NodeId, ListOp> received = [];
    readonly Dictionary<string, long> counters = new(StringComparer.Ordinal);

    public long MaximumLamport { get; private set; }
    public long MaximumCounter(string replica) => counters.GetValueOrDefault(replica);

    /// <summary>Includes missing-dependency operations, so persistence can resume causal buffering after restart.</summary>
    public IEnumerable<ListOp> ReceivedOperations => received.Values.OrderBy(op => op.Id);

    /// <summary>
    /// All applied moves whose position register lost, including sequentially superseded moves.
    /// Deleted items retain their winning and losing positions. This is not a concurrency detector.
    /// </summary>
    public IEnumerable<ListOp> LosingMoves => log.Where(op => op.Kind == ListOpKind.Move &&
        op.Id != elems[op.Element].Current.Id).OrderBy(op => op.Id);

    /// <summary>Operations applied so far, in application order.</summary>
    public IReadOnlyList<ListOp> AppliedOperations => log.AsReadOnly();
    /// <summary>Operations held back because a dependency has not arrived.</summary>
    public int PendingCount => bufferedIds.Count;
    /// <summary>Elements ever inserted, including deleted ones.</summary>
    public int ElementCount => elems.Count;
    /// <summary>Position nodes ever created (tombstones and abandoned move slots stay as anchors).</summary>
    public int NodeCount => nodes.Count;

    /// <summary>A state holding <paramref name="elements"/> in order, as a chain of inserts by <see cref="InitialReplica"/> with Lamport 0.</summary>
    public static ListState FromInitial(IEnumerable<string> elements)
    {
        var state = new ListState();
        NodeId? previous = null;
        long counter = 0;
        foreach (var element in elements)
        {
            var id = new NodeId(InitialReplica, ++counter);
            state.Apply(ListOp.Insert(id, 0, element, previous, null));
            previous = id;
        }
        return state;
    }

    /// <summary>Pure fold: the state after applying <paramref name="operations"/> in the order given, starting from <paramref name="start"/> or empty.</summary>
    public static ListState Fold(IEnumerable<ListOp> operations, ListState? start = null)
    {
        var state = start?.Clone() ?? new ListState();
        foreach (var op in operations) state.Apply(op);
        return state;
    }

    /// <summary>Copy including buffered operations and retained position anchors.</summary>
    public ListState Clone()
    {
        var copy = new ListState();
        foreach (var op in received.Values) copy.Apply(op);
        return copy;
    }

    public ApplyResult Apply(ListOp op)
    {
        ArgumentNullException.ThrowIfNull(op);
        op.Validate();
        if (received.TryGetValue(op.Id, out var known))
        {
            if (known != op) throw new ArgumentException("An operation ID was reused with different content.", nameof(op));
            return ApplyResult.Duplicate;
        }
        if (op.Kind == ListOpKind.Insert && elems.ContainsKey(op.Element))
            throw new ArgumentException("Insert requires a fresh item identity.", nameof(op));
        received.Add(op.Id, op);
        MaximumLamport = Math.Max(MaximumLamport, op.Lamport);
        counters[op.Id.Replica] = Math.Max(MaximumCounter(op.Id.Replica), op.Id.Counter);
        var work = new Stack<ListOp>();
        work.Push(op);
        bool first = true;
        ApplyResult result = ApplyResult.Duplicate;
        while (work.Count > 0)
        {
            var current = work.Pop();
            var outcome = TryApply(current, first);
            if (first) { result = outcome; first = false; }
            if (outcome != ApplyResult.Applied) continue;
            if (current.Kind != ListOpKind.Delete && waitingForNode.Remove(current.Id, out var byNode))
                foreach (var w in byNode) { bufferedIds.Remove(w.Id); work.Push(w); }
            if (current.Kind == ListOpKind.Insert && waitingForElement.Remove(current.Element, out var byElement))
                foreach (var w in byElement) { bufferedIds.Remove(w.Id); work.Push(w); }
        }
        return result;
    }

    ApplyResult TryApply(ListOp op, bool external)
    {
        if (appliedIds.Contains(op.Id)) return ApplyResult.Duplicate;
        if (op.Kind != ListOpKind.Insert && !elems.ContainsKey(op.Element)) return Buffer(op, external, element: op.Element);
        if (op.Kind != ListOpKind.Delete)
        {
            if (op.LeftOrigin is { } left && !nodes.ContainsKey(left)) return Buffer(op, external, node: left);
            if (op.RightOrigin is { } right && !nodes.ContainsKey(right)) return Buffer(op, external, node: right);
        }

        if (op.Kind == ListOpKind.Delete)
        {
            elems[op.Element].Deleted = true;
        }
        else
        {
            if (op.Kind == ListOpKind.Insert && elems.ContainsKey(op.Element))
                throw new InvalidOperationException($"Element {op.Element} was already inserted; moves keep identity, inserts must use fresh ids");
            var left = op.LeftOrigin is { } l ? nodes[l] : root;
            var right = op.RightOrigin is { } r ? nodes[r] : null;
            var node = Attach(op.Id, left, right, op.Element);
            if (op.Kind == ListOpKind.Insert)
            {
                elems.Add(op.Element, new Elem(node, op.Lamport, op.Id));
            }
            else
            {
                var elem = elems[op.Element];
                if (op.Lamport > elem.Lamport || (op.Lamport == elem.Lamport && op.Id.CompareTo(elem.Stamp) > 0))
                {
                    elem.Current = node;
                    elem.Lamport = op.Lamport;
                    elem.Stamp = op.Id;
                }
            }
        }
        appliedIds.Add(op.Id);
        bufferedIds.Remove(op.Id);
        log.Add(op);
        return ApplyResult.Applied;
    }

    ApplyResult Buffer(ListOp op, bool external, NodeId? node = null, string? element = null)
    {
        if (external) { if (!bufferedIds.Add(op.Id)) return ApplyResult.Duplicate; }
        else bufferedIds.Add(op.Id);
        if (node is { } n)
        {
            if (!waitingForNode.TryGetValue(n, out var list)) waitingForNode[n] = list = [];
            list.Add(op);
        }
        else
        {
            if (!waitingForElement.TryGetValue(element!, out var list)) waitingForElement[element!] = list = [];
            list.Add(op);
        }
        return ApplyResult.Buffered;
    }

    Node Attach(NodeId id, Node left, Node? right, string element)
    {
        Node parent;
        bool isRight;
        if (right is not null && right.Depth > left.Depth && IsAncestor(left, right)) { parent = right; isRight = false; }
        else { parent = left; isRight = true; }
        var node = new Node(id, parent, isRight, element);
        ref var siblings = ref (isRight ? ref parent.RightChildren : ref parent.LeftChildren);
        siblings ??= [];
        siblings.Insert(~IndexOf(siblings, id), node);
        nodes.Add(id, node);
        return node;
    }

    static bool IsAncestor(Node ancestor, Node descendant)
    {
        var cur = descendant;
        for (int steps = descendant.Depth - ancestor.Depth; steps > 0; steps--) cur = cur!.Parent;
        return ReferenceEquals(cur, ancestor);
    }

    static int IndexOf(List<Node> siblings, NodeId id)
    {
        int lo = 0, hi = siblings.Count - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >>> 1;
            int c = siblings[mid].Id.CompareTo(id);
            if (c == 0) return mid;
            if (c < 0) lo = mid + 1; else hi = mid - 1;
        }
        return ~lo;
    }

    static Node Leftmost(Node n)
    {
        while (n.LeftChildren is { Count: > 0 } l) n = l[0];
        return n;
    }

    static Node? Next(Node n)
    {
        if (n.RightChildren is { Count: > 0 } r) return Leftmost(r[0]);
        var cur = n;
        while (cur.Parent is { } p)
        {
            var siblings = cur.IsRight ? p.RightChildren! : p.LeftChildren!;
            int i = IndexOf(siblings, cur.Id);
            if (i + 1 < siblings.Count) return Leftmost(siblings[i + 1]);
            if (!cur.IsRight) return p;
            cur = p;
        }
        return null;
    }

    IEnumerable<Node> InOrder()
    {
        var stack = new Stack<(Node node, int index)>();
        stack.Push((root, 0));
        while (stack.Count > 0)
        {
            var (n, i) = stack.Pop();
            int l = n.LeftChildren?.Count ?? 0, r = n.RightChildren?.Count ?? 0;
            if (i < l) { stack.Push((n, i + 1)); stack.Push((n.LeftChildren![i], 0)); }
            else if (i == l) { stack.Push((n, i + 1)); if (!ReferenceEquals(n, root)) yield return n; }
            else if (i - l - 1 < r) { stack.Push((n, i + 1)); stack.Push((n.RightChildren![i - l - 1], 0)); }
        }
    }


    public bool Contains(string element) => elems.ContainsKey(element);
    public bool IsDeleted(string element) => elems[element].Deleted;
    /// <summary>The position node currently holding <paramref name="element"/>.</summary>
    public NodeId PositionOf(string element) => elems[element].Current.Id;

    /// <summary>All position nodes in tree order, including tombstoned and abandoned ones.</summary>
    public IEnumerable<NodeId> NodeOrder() => InOrder().Select(n => n.Id);

    /// <summary>Every element (deleted ones too) at its current position, in order.</summary>
    public IEnumerable<string> AllElementsInOrder() =>
        InOrder().Where(n => n.Element is not null && ReferenceEquals(elems[n.Element].Current, n)).Select(n => n.Element!);

    /// <summary>Live elements in list order.</summary>
    public IEnumerable<string> Elements() => AllElementsInOrder().Where(e => !elems[e].Deleted);

    /// <summary>Origins for inserting or moving to just after <paramref name="afterElement"/> (null = list start): its current node and the node that follows it in the full order.</summary>
    public (NodeId? Left, NodeId? Right) OriginsAfterElement(string? afterElement) =>
        OriginsAfterNode(afterElement is null ? null : elems[afterElement].Current.Id);

    /// <summary>Origins for placing a node immediately after <paramref name="node"/> (null = list start).</summary>
    public (NodeId? Left, NodeId? Right) OriginsAfterNode(NodeId? node)
    {
        var left = node is { } id ? nodes[id] : root;
        var next = Next(left);
        return (node, next?.Id);
    }

    /// <summary>Origins for placing an item so that it becomes live index <paramref name="index"/>, ignoring <paramref name="ignore"/> (an element being moved).</summary>
    public (NodeId? Left, NodeId? Right) OriginsAtIndex(int index, string? ignore = null)
    {
        if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));
        if (index == 0) return OriginsAfterNode(null);
        string? predecessor = null;
        int seen = 0;
        foreach (var e in Elements())
        {
            if (e == ignore) continue;
            if (++seen == index) { predecessor = e; break; }
        }
        if (predecessor is null) throw new ArgumentOutOfRangeException(nameof(index));
        return OriginsAfterElement(predecessor);
    }
}
