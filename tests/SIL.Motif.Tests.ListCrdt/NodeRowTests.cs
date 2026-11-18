using SIL.ListCrdt;
using Xunit;

namespace SIL.ListCrdt.Tests;

public class NodeRowTests
{
    [Fact]
    public void LargeSequenceUsesOneRowPerItemAndOneSmallRowPerEdit()
    {
        var ids = Enumerable.Range(0, 34383).Select(i => "paragraph-" + i).ToArray();
        var replica = new ListReplica("writer", ListState.FromInitial(ids));
        var rows = replica.State.ExportRows().ToDictionary(row => row.Key);
        Assert.Equal(ids.Length, rows.Count);
        Assert.All(rows.Values, row => Assert.True(row.Json.Length < 512));
        var move = replica.MoveToIndex(ids[0], ids.Length - 1)!;
        var patch = NodeRowEncoding.Encode(move);
        Assert.True(patch.Json.Length < 512);
        Assert.True(rows.TryAdd(patch.Key, patch));
        Assert.Equal(ids.Length + 1, rows.Count);
        var restored = NodeRowEncoding.Restore(rows.Values.Reverse());
        Assert.Equal(ids.Skip(1).Append(ids[0]), restored.Elements());
        Assert.Equal(replica.State.NodeOrder(), restored.NodeOrder());
    }

    [Fact]
    public void RowsPreservePendingDeletesLosingMovesAndAbandonedAnchors()
    {
        var baseline = ListState.FromInitial(["one", "two", "three"]);
        var a = new ListReplica("a", baseline.Clone());
        var b = new ListReplica("b", baseline.Clone());
        var lose = a.MoveToIndex("one", 1)!;
        var win = b.MoveToIndex("one", 2)!;
        a.Receive(win);
        a.Delete("two");
        var missing = ListOp.Insert(new NodeId("later", 1), 1, "missing", null, null);
        var pending = ListOp.Insert(new NodeId("later", 2), 2, "pending", missing.Id, null);
        Assert.Equal(ApplyResult.Buffered, a.Receive(pending));
        var rows = a.State.ExportRows().ToArray();
        var restored = NodeRowEncoding.Restore(rows.Reverse());
        Assert.Equal(1, restored.PendingCount);
        Assert.True(restored.IsDeleted("two"));
        Assert.Equal([lose], restored.LosingMoves);
        Assert.Equal(a.State.NodeOrder(), restored.NodeOrder());
        Assert.Equal(ApplyResult.Duplicate, restored.Apply(pending));
        Assert.Equal(ApplyResult.Applied, restored.Apply(missing));
        a.Receive(missing);
        Assert.Equal(0, restored.PendingCount);
        Assert.Equal(a.State.Elements(), restored.Elements());
        Assert.Equal(rows.OrderBy(row => row.Key).Select(row => row.Json),
            NodeRowEncoding.Restore(rows).ExportRows().OrderBy(row => row.Key).Select(row => row.Json));
    }

    [Theory]
    [InlineData("right,item,left", 3, 2, 0)]
    [InlineData("item,left,right", 1, 1, 0)]
    [InlineData("item,right,left", 1, 2, 0)]
    [InlineData("left,item,right", 3, 1, 0)]
    [InlineData("left,right,item", 3, 4, 0)]
    [InlineData("right,left,item", 3, 4, 0)]
    public void RowsPreserveMissingElementQueuesAcrossDependencyOrders(string arrivalOrder, int first, int second, int third)
    {
        var author = new ListReplica("writer");
        var item = author.Insert("e", null);
        var left = author.Insert("l", "e");
        var right = author.Insert("r", "l");
        var move = author.Move("e", "l")!;
        var delete = author.Delete("e");
        ListOp[] complete = [item, left, right, move, delete];
        var state = ListState.Fold([move, delete]);
        Assert.Equal(2, state.PendingCount);
        state = NodeRowEncoding.Restore(state.ExportRows().Reverse());
        Assert.Equal(2, state.PendingCount);
        Assert.Equal(5, state.MaximumLamport);
        Assert.Equal(5, state.MaximumCounter("writer"));
        Assert.Equal(ApplyResult.Duplicate, state.Apply(move));
        Assert.Equal(ApplyResult.Duplicate, state.Apply(delete));

        var dependencies = new Dictionary<string, ListOp> { ["item"] = item, ["left"] = left, ["right"] = right };
        int[] pendingCounts = [first, second, third];
        var delivered = new List<ListOp> { move, delete };
        var order = arrivalOrder.Split(',');
        for (int i = 0; i < order.Length; i++)
        {
            var dependency = dependencies[order[i]];
            state.Apply(dependency);
            delivered.Add(dependency);
            Assert.Equal(pendingCounts[i], state.PendingCount);
            state = NodeRowEncoding.Restore(state.ExportRows().Reverse());
            Assert.Equal(pendingCounts[i], state.PendingCount);
            Assert.Equal(5, state.MaximumLamport);
            Assert.Equal(5, state.MaximumCounter("writer"));
            foreach (var operation in delivered) Assert.Equal(ApplyResult.Duplicate, state.Apply(operation));
            Assert.Equal(pendingCounts[i], state.PendingCount);
            if (state.Contains("e")) Assert.True(state.IsDeleted("e"));
        }

        Assert.Equal(["l", "r"], state.Elements());
        Assert.True(state.IsDeleted("e"));
        Assert.Equal(move.Id, state.PositionOf("e"));
        Assert.Equal([item.Id, left.Id, move.Id, right.Id], state.NodeOrder());
        Assert.Equal(4, state.NodeCount);
        Assert.Equal(3, state.ElementCount);
        Support.AssertSame("restored missing-element queues " + arrivalOrder, author.State, state);
        foreach (var operation in complete) Assert.Equal(ApplyResult.Duplicate, state.Apply(operation));
        var reopened = new ListReplica("writer", state);
        var continuation = reopened.Insert("continued", "r");
        Assert.Equal(new NodeId("writer", 6), continuation.Id);
        Assert.Equal(6, continuation.Lamport);
        Assert.Equal(author.Insert("continued", "r"), continuation);
        Support.AssertSame("continued author " + arrivalOrder, author.State, reopened.State);
        Assert.True(reopened.State.IsDeleted("e"));
    }

    [Fact]
    public void RowKeysSeparateReplicaNamesAndRejectMismatchedPayloads()
    {
        var first = NodeRowEncoding.Encode(ListOp.Insert(new NodeId("a@1", 2), 1, "one", null, null));
        var second = NodeRowEncoding.Encode(ListOp.Insert(new NodeId("a", 12), 1, "two", null, null));
        Assert.NotEqual(first.Key, second.Key);
        Assert.Throws<FormatException>(() => NodeRowEncoding.Decode(first with { Key = second.Key }));
        Assert.Throws<FormatException>(() => NodeRowEncoding.Decode(first with { Version = 2 }));
    }
}
