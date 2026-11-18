namespace SIL.ListCrdt;

public enum ListOpKind { Insert, Move, Delete }

/// <summary>
/// One immutable list operation.
/// <para>Insert: creates <see cref="Element"/> and its first position node <see cref="Id"/>, placed between
/// <see cref="LeftOrigin"/> and <see cref="RightOrigin"/> (null = start / end of the full tree order).</para>
/// <para>Move: creates a new position node <see cref="Id"/> for an existing element (same identity) and
/// proposes it for the element's last-writer-wins position register at (<see cref="Lamport"/>, <see cref="Id"/>).</para>
/// <para>Delete: tombstones the element. Position nodes stay as anchors. <see cref="Id"/> only names the operation.</para>
/// Every <see cref="Id"/> must be unique across all operations of all replicas.
/// </summary>
public sealed record ListOp(
    ListOpKind Kind,
    NodeId Id,
    long Lamport,
    string Element,
    NodeId? LeftOrigin = null,
    NodeId? RightOrigin = null)
{
    /// <summary>Validates the scalar wire contract; dependency graph validity requires the surrounding operation stream.</summary>
    public void Validate()
    {
        if (!Enum.IsDefined(Kind)) throw new ArgumentException("Unknown operation kind.");
        ArgumentException.ThrowIfNullOrEmpty(Element);
        ValidateId(Id);
        if (Lamport < 0) throw new ArgumentOutOfRangeException(nameof(Lamport));
        if (LeftOrigin is { } left) ValidateId(left);
        if (RightOrigin is { } right) ValidateId(right);
        if (LeftOrigin == Id || RightOrigin == Id ||
            (LeftOrigin is not null && LeftOrigin == RightOrigin))
            throw new ArgumentException("Origins must be distinct and cannot reference the operation itself.");
        if (Kind == ListOpKind.Delete && (LeftOrigin is not null || RightOrigin is not null))
            throw new ArgumentException("Delete operations cannot have position origins.");
    }

    static void ValidateId(NodeId id)
    {
        if (id.Replica is null || id.Replica.Contains('\0') || id.Counter <= 0)
            throw new ArgumentException("IDs require a non-null replica without NUL and a positive counter.");
    }

    public static ListOp Insert(NodeId id, long lamport, string element, NodeId? left, NodeId? right) =>
        new(ListOpKind.Insert, id, lamport, element, left, right);

    public static ListOp Move(NodeId id, long lamport, string element, NodeId? left, NodeId? right) =>
        new(ListOpKind.Move, id, lamport, element, left, right);

    public static ListOp Delete(NodeId id, long lamport, string element) =>
        new(ListOpKind.Delete, id, lamport, element);
}
