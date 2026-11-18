using SIL.ListCrdt;
using Xunit;

namespace SIL.ListCrdt.Tests;

public class IngestionTests
{
    [Fact]
    public void ReopenedAuthorContinuesCountersAndClockIncludingPendingOperations()
    {
        var first = new ListReplica("writer");
        var insert = first.InsertAt(0, "first");
        var second = new ListReplica("writer", NodeRowEncoding.Restore(first.State.ExportRows()));
        var next = second.InsertAt(1, "second");
        Assert.True(next.Id.Counter > insert.Id.Counter);
        Assert.True(next.Lamport > insert.Lamport);
        var pending = ListOp.Move(new NodeId("writer", 50), 100, "absent", null, null);
        Assert.Equal(ApplyResult.Buffered, second.Receive(pending));
        var reopened = new ListReplica("writer", second.State.Clone());
        Assert.Equal(1, reopened.State.PendingCount);
        var last = reopened.InsertAt(2, "third");
        Assert.Equal(51, last.Id.Counter);
        Assert.Equal(101, last.Lamport);
        Assert.Equal(["first", "second", "third"], reopened.State.Elements());
    }

    [Fact]
    public void ConflictingDuplicateIdsAreRejectedForAppliedAndPendingOperations()
    {
        var replica = new ListReplica("writer");
        var applied = replica.InsertAt(0, "first");
        Assert.Throws<ArgumentException>(() => replica.Receive(applied with { Element = "other" }));
        var pending = ListOp.Move(new NodeId("remote", 1), 4, "absent", null, null);
        Assert.Equal(ApplyResult.Buffered, replica.Receive(pending));
        Assert.Throws<ArgumentException>(() => replica.Receive(pending with { Lamport = 5 }));
        Assert.Equal(1, replica.State.PendingCount);
        Assert.Equal(["first"], replica.State.Elements());
    }

    [Fact]
    public void InvalidScalarOperationsLeaveStateAndClocksUnchanged()
    {
        var state = new ListState();
        var valid = ListOp.Insert(new NodeId("writer", 1), 1, "first", null, null);
        ListOp[] invalid =
        [
            valid with { Kind = (ListOpKind)99 },
            valid with { Lamport = -1 },
            valid with { Id = new NodeId("writer", 0) },
            valid with { Id = default },
            valid with { Element = "" },
            valid with { LeftOrigin = valid.Id },
            valid with { LeftOrigin = new NodeId("other", 1), RightOrigin = new NodeId("other", 1) },
            valid with { Kind = ListOpKind.Delete, LeftOrigin = new NodeId("other", 1) }
        ];
        foreach (var operation in invalid)
        {
            Assert.ThrowsAny<ArgumentException>(() => state.Apply(operation));
            Assert.Empty(state.ReceivedOperations);
            Assert.Equal(0, state.MaximumLamport);
            Assert.Equal(0, state.PendingCount);
            Assert.Equal(0, state.NodeCount);
        }
        Assert.Equal(ApplyResult.Applied, state.Apply(valid));
    }

    [Fact]
    public void SamePredecessorMoveIsNoOpAndInvalidAuthorEditsDoNotPartiallyApply()
    {
        var replica = new ListReplica("writer", ListState.FromInitial(["one", "two", "three"]));
        Assert.Null(replica.Move("two", "one"));
        Assert.Null(replica.MoveToIndex("three", 2));
        Assert.Null(replica.Move("one", null));
        int count = replica.State.AppliedOperations.Count;
        Assert.Throws<ArgumentOutOfRangeException>(() => replica.MoveToIndex("two", -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => replica.MoveToIndex("two", 3));
        Assert.Throws<ArgumentException>(() => replica.MoveToIndex("absent", 0));
        Assert.Throws<ArgumentException>(() => replica.MoveBlock(["one", "absent"], "three"));
        Assert.Throws<ArgumentException>(() => replica.MoveBlock(["one", "one"], "three"));
        Assert.Equal(count, replica.State.AppliedOperations.Count);
        Assert.Equal(["one", "two", "three"], replica.State.Elements());
    }

    [Fact]
    public void JsonRejectsDuplicateMembersAndInvalidIdentifierShapes()
    {
        var json = ListOp.Insert(new NodeId("writer", 1), 1, "first", null, null).ToJson();
        Assert.Throws<FormatException>(() => OpJson.FromJson(json.Replace("\"v\":1", "\"v\":1,\"v\":1")));
        Assert.Throws<FormatException>(() => OpJson.FromJson(json.Replace("[\"writer\",\"1\"]", "[1,\"1\"]")));
        Assert.Throws<FormatException>(() => OpJson.FromJson(json.Replace("[\"writer\",\"1\"]", "[\"writer\",\"0\"]")));
    }
}
