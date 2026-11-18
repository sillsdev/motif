using SIL.ListCrdt;
using Xunit;

namespace SIL.ListCrdt.Tests;

public class BlockMoveTests
{
    [Theory]
    [InlineData(false, 1, 0)]
    [InlineData(false, 2, 0)]
    [InlineData(true, 1, 0)]
    [InlineData(true, 2, 0)]
    [InlineData(false, 1, 1)]
    [InlineData(false, 2, 1)]
    [InlineData(true, 1, 1)]
    [InlineData(true, 2, 1)]
    [InlineData(false, 1, 2)]
    [InlineData(false, 2, 2)]
    [InlineData(true, 1, 2)]
    [InlineData(true, 2, 2)]
    public void UnchangedLiveBlockKeepsClocksNodesAndRows(bool middle, int count, int retainedPositions)
    {
        var ids = new List<string>();
        if (middle) ids.Add("before");
        if (retainedPositions > 0) ids.Add("gap");
        ids.Add("first");
        if (retainedPositions > 0) ids.Add("between");
        ids.AddRange(["second", "tail"]);
        var setup = new ListReplica("setup", ListState.FromInitial(ids));
        if (retainedPositions == 1)
        {
            setup.Delete("gap");
            setup.Delete("between");
        }
        else if (retainedPositions == 2)
        {
            setup.Move("gap", "tail");
            setup.Move("between", "gap");
        }
        var replica = new ListReplica("writer", setup.State);
        var state = replica.State;
        var rows = state.ExportRows().ToArray();
        var nodes = state.NodeOrder().ToArray();
        var live = state.Elements().ToArray();
        int operations = state.AppliedOperations.Count;
        long time = state.MaximumLamport;
        long counter = state.MaximumCounter(replica.ReplicaId);

        Assert.Empty(replica.MoveBlock(count == 1 ? ["first"] : ["first", "second"], middle ? "before" : null));

        Assert.Equal(time, state.MaximumLamport);
        Assert.Equal(counter, state.MaximumCounter(replica.ReplicaId));
        Assert.Equal(operations, state.AppliedOperations.Count);
        Assert.Equal(nodes.Length, state.NodeCount);
        Assert.Equal(nodes, state.NodeOrder());
        Assert.Equal(live, state.Elements());
        Assert.Equal(rows, state.ExportRows());
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    public void UnchangedBlockDoesNotCompeteWithConcurrentInsert(bool middle, int count)
    {
        string[] initial = middle ? ["before", "first", "second", "tail"] : ["first", "second", "tail"];
        var a = new ListReplica("a", ListState.FromInitial(initial));
        var b = new ListReplica("b", a.State.Clone());
        var insert = b.InsertAt(middle ? 1 : 0, "new");

        var moves = a.MoveBlock(count == 1 ? ["first"] : ["first", "second"], middle ? "before" : null);
        a.Receive(insert);
        foreach (var move in moves) b.Receive(move);

        string[] expected = middle ? ["before", "new", "first", "second", "tail"] : ["new", "first", "second", "tail"];
        Assert.Equal(expected, a.State.Elements());
        Assert.Equal(expected, b.State.Elements());
        Assert.Equal(a.State.NodeOrder(), b.State.NodeOrder());
        Assert.Empty(moves);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void UnchangedBlockDoesNotAllocateExhaustedIdsOrTimestamps(bool exhaustedClock)
    {
        var state = ListState.FromInitial(["first", "second"]);
        var pending = ListOp.Delete(new NodeId("writer", exhaustedClock ? 1 : long.MaxValue),
            exhaustedClock ? long.MaxValue : 1, "absent");
        state.Apply(pending);
        var rows = state.ExportRows().ToArray();
        var replica = new ListReplica("writer", state);

        Assert.Empty(replica.MoveBlock(["first", "second"], null));

        Assert.Equal(1, state.PendingCount);
        Assert.Equal(rows, state.ExportRows());
    }

    [Theory]
    [InlineData("gapped")]
    [InlineData("reversed")]
    [InlineData("relocated")]
    public void ActualBlockReorderKeepsOneSharedTimeAndDistinctIds(string placement)
    {
        string[] initial = placement == "gapped" ? ["head", "one", "middle", "two", "tail"] : ["head", "one", "two", "tail"];
        var replica = new ListReplica("writer", ListState.FromInitial(initial));
        string[] block = placement == "reversed" ? ["two", "one"] : ["one", "two"];

        var moves = replica.MoveBlock(block, placement == "relocated" ? "tail" : "head");

        string[] expected = placement switch
        {
            "gapped" => ["head", "one", "two", "middle", "tail"],
            "reversed" => ["head", "two", "one", "tail"],
            _ => ["head", "tail", "one", "two"]
        };
        Assert.Equal(expected, replica.State.Elements());
        Assert.Equal(2, moves.Length);
        Assert.Equal([new NodeId("writer", 1), new NodeId("writer", 2)], moves.Select(move => move.Id));
        Assert.All(moves, move => Assert.Equal(1, move.Lamport));
        Assert.Equal(initial.Length + 2, replica.State.NodeCount);
    }
}
