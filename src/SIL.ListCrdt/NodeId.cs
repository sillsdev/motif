namespace SIL.ListCrdt;

/// <summary>
/// Identifier of a position node (an insert or a move creates one) and of every operation.
/// Ordered by replica (ordinal) then counter; that order breaks ties between concurrent
/// siblings (Fugue) and between concurrent moves (Kleppmann LWW register).
/// </summary>
public readonly record struct NodeId(string Replica, long Counter) : IComparable<NodeId>
{
    public int CompareTo(NodeId other)
    {
        int c = string.CompareOrdinal(Replica, other.Replica);
        return c != 0 ? c : Counter.CompareTo(other.Counter);
    }

    public override string ToString() => Replica + "@" + Counter;
}
