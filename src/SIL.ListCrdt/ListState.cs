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
        // Visible-position index: full tree order as a doubly linked list cut into counted chunks.
        public Node? Prev, Next;
        public Chunk Chunk = null!;
        public bool Live;
    }

    /// <summary>A contiguous run of the full node order with its live count (order-statistic index).</summary>
    sealed class Chunk
    {
        public Node First = null!;
        public int Count, Live, Index;
    }

    const int DefaultChunkSplitThreshold = 512;
    readonly int chunkSplitThreshold = DefaultChunkSplitThreshold;

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
    readonly HashSet<string> insertedElements = new(StringComparer.Ordinal);
    readonly List<Chunk> chunks = [];
    Node? head;
    int liveCount;

    public ListState() : this(null) { }

    /// <summary>Test seam: a small split threshold makes chunk splits frequent.</summary>
    internal ListState(ListLimits? limits, int chunkSplitThreshold) : this(limits)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(chunkSplitThreshold, 2);
        this.chunkSplitThreshold = chunkSplitThreshold;
    }


    /// <summary>An empty state that enforces <paramref name="limits"/> on remote input (null means the defaults).</summary>
    public ListState(ListLimits? limits) => Limits = limits ?? ListLimits.Default;

    /// <summary>The ingestion limits enforced by <see cref="Apply"/>.</summary>
    public ListLimits Limits { get; }

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
    public static ListState FromInitial(IEnumerable<string> elements, ListLimits? limits = null)
    {
        var state = new ListState(limits);
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
        var copy = new ListState(Limits, chunkSplitThreshold);
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
        Limits.Check(op);
        if (op.Kind == ListOpKind.Insert && insertedElements.Contains(op.Element))
            throw new ArgumentException("Insert requires a fresh item identity.", nameof(op));
        if (bufferedIds.Count >= Limits.MaxPendingOperations && MissingDependency(op, out _, out _))
            throw new ListLimitException(nameof(ListLimits.MaxPendingOperations), Limits.MaxPendingOperations,
                $"The pending buffer already holds {bufferedIds.Count} operations awaiting dependencies; deliver the missing operations first or raise ListLimits.MaxPendingOperations.");
        received.Add(op.Id, op);
        if (op.Kind == ListOpKind.Insert) insertedElements.Add(op.Element);
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
        if (MissingDependency(op, out var missingNode, out var missingElement))
            return Buffer(op, external, missingNode, missingElement);

        if (op.Kind == ListOpKind.Delete)
        {
            var deleted = elems[op.Element];
            deleted.Deleted = true;
            SetLive(deleted.Current, false);
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
                SetLive(node, true);
            }
            else
            {
                var elem = elems[op.Element];
                if (op.Lamport > elem.Lamport || (op.Lamport == elem.Lamport && op.Id.CompareTo(elem.Stamp) > 0))
                {
                    SetLive(elem.Current, false);
                    elem.Current = node;
                    SetLive(node, !elem.Deleted);
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

    bool MissingDependency(ListOp op, out NodeId? node, out string? element)
    {
        node = null;
        element = null;
        if (op.Kind != ListOpKind.Insert && !elems.ContainsKey(op.Element)) { element = op.Element; return true; }
        if (op.Kind == ListOpKind.Delete) return false;
        if (op.LeftOrigin is { } left && !nodes.ContainsKey(left)) { node = left; return true; }
        if (op.RightOrigin is { } right && !nodes.ContainsKey(right)) { node = right; return true; }
        return false;
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
        int index = ~IndexOf(siblings, id);
        Node? predecessor;
        if (index > 0) predecessor = LastInSubtree(siblings[index - 1]);
        else if (isRight) predecessor = ReferenceEquals(parent, root) ? null : parent;
        else predecessor = (siblings.Count > 0 ? Leftmost(siblings[0]) : parent).Prev;
        siblings.Insert(index, node);
        nodes.Add(id, node);
        LinkAfter(predecessor, node);
        return node;
    }

    static Node LastInSubtree(Node n)
    {
        while (n.RightChildren is { Count: > 0 } r) n = r[^1];
        return n;
    }

    void LinkAfter(Node? predecessor, Node node)
    {
        var next = predecessor is null ? head : predecessor.Next;
        node.Prev = predecessor;
        node.Next = next;
        if (predecessor is null) head = node; else predecessor.Next = node;
        if (next is not null) next.Prev = node;
        Chunk chunk;
        if (predecessor is not null) chunk = predecessor.Chunk;
        else if (chunks.Count == 0) { chunk = new Chunk(); chunks.Add(chunk); chunk.First = node; }
        else { chunk = chunks[0]; chunk.First = node; }
        node.Chunk = chunk;
        if (++chunk.Count > chunkSplitThreshold) Split(chunk);
    }

    void Split(Chunk chunk)
    {
        int keep = chunk.Count / 2;
        var cursor = chunk.First;
        for (int i = 0; i < keep; i++) cursor = cursor.Next!;
        var tail = new Chunk { First = cursor, Count = chunk.Count - keep };
        for (var n = cursor; n is not null && ReferenceEquals(n.Chunk, chunk); n = n.Next)
        {
            n.Chunk = tail;
            if (n.Live) tail.Live++;
        }
        chunk.Count = keep;
        chunk.Live -= tail.Live;
        chunks.Insert(chunk.Index + 1, tail);
        for (int i = chunk.Index + 1; i < chunks.Count; i++) chunks[i].Index = i;
    }

    void SetLive(Node node, bool live)
    {
        if (node.Live == live) return;
        node.Live = live;
        int delta = live ? 1 : -1;
        node.Chunk.Live += delta;
        liveCount += delta;
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
    public IEnumerable<string> Elements() => LiveFrom(head);

    /// <summary>Number of live (not deleted) elements.</summary>
    public int LiveCount => liveCount;

    /// <summary>Zero-based live index of <paramref name="element"/>, or -1 when it is absent or deleted. Cost grows with the square root of the list size.</summary>
    public int IndexOf(string element)
    {
        if (!elems.TryGetValue(element, out var elem) || elem.Deleted) return -1;
        var node = elem.Current;
        int rank = 0;
        for (int i = 0; i < node.Chunk.Index; i++) rank += chunks[i].Live;
        for (var n = node.Chunk.First; !ReferenceEquals(n, node); n = n.Next!) if (n.Live) rank++;
        return rank;
    }

    /// <summary>The live element at zero-based <paramref name="index"/>.</summary>
    public string ElementAt(int index) => SelectLive(index).Element!;

    /// <summary>Live elements from zero-based live <paramref name="index"/> (equal to <see cref="LiveCount"/> yields nothing).</summary>
    public IEnumerable<string> ElementsFrom(int index)
    {
        if (index < 0 || index > liveCount) throw new ArgumentOutOfRangeException(nameof(index));
        return index == liveCount ? [] : LiveFrom(SelectLive(index));
    }

    static IEnumerable<string> LiveFrom(Node? start)
    {
        for (var n = start; n is not null; n = n.Next) if (n.Live) yield return n.Element!;
    }

    Node SelectLive(int index)
    {
        if (index < 0 || index >= liveCount) throw new ArgumentOutOfRangeException(nameof(index));
        int remaining = index;
        int c = 0;
        while (remaining >= chunks[c].Live) remaining -= chunks[c++].Live;
        var n = chunks[c].First;
        while (true)
        {
            if (n.Live && remaining-- == 0) return n;
            n = n.Next!;
        }
    }

    /// <summary>Origins for inserting or moving to just after <paramref name="afterElement"/> (null = list start): its current node and the node that follows it in the full order.</summary>
    public (NodeId? Left, NodeId? Right) OriginsAfterElement(string? afterElement) =>
        OriginsAfterNode(afterElement is null ? null : elems[afterElement].Current.Id);

    /// <summary>Origins for placing a node immediately after <paramref name="node"/> (null = list start).</summary>
    public (NodeId? Left, NodeId? Right) OriginsAfterNode(NodeId? node)
    {
        var next = node is { } id ? nodes[id].Next : head;
        return (node, next?.Id);
    }

    /// <summary>Origins for placing an item so that it becomes live index <paramref name="index"/>, ignoring <paramref name="ignore"/> (an element being moved).</summary>
    public (NodeId? Left, NodeId? Right) OriginsAtIndex(int index, string? ignore = null)
    {
        if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));
        if (index == 0) return OriginsAfterNode(null);
        int ignored = ignore is null ? -1 : IndexOf(ignore);
        if (index > liveCount - (ignored >= 0 ? 1 : 0)) throw new ArgumentOutOfRangeException(nameof(index));
        int predecessor = index - 1;
        if (ignored >= 0 && predecessor >= ignored) predecessor++;
        return OriginsAfterNode(SelectLive(predecessor).Id);
    }
}
