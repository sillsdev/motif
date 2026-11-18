using SIL.ListCrdt;
using Xunit;

namespace SIL.ListCrdt.Tests;

public class IndexTests
{
    /// <summary>Independent oracle: live elements read off the Fugue tree, not the position index.</summary>
    static string[] TreeLive(ListState state) =>
        state.AllElementsInOrder().Where(e => !state.IsDeleted(e)).ToArray();

    static void AssertIndex(string label, ListState state)
    {
        var expected = TreeLive(state);
        Assert.True(expected.SequenceEqual(state.Elements()), label + ": Elements differs from the tree");
        Assert.Equal(expected.Length, state.LiveCount);
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.True(expected[i] == state.ElementAt(i), label + ": ElementAt " + i);
            Assert.True(i == state.IndexOf(expected[i]), label + ": IndexOf " + expected[i]);
        }
        foreach (var e in state.AllElementsInOrder().Where(state.IsDeleted)) Assert.Equal(-1, state.IndexOf(e));
        Assert.Equal(-1, state.IndexOf("never-inserted"));
        Assert.Throws<ArgumentOutOfRangeException>(() => state.ElementAt(expected.Length));
        Assert.Throws<ArgumentOutOfRangeException>(() => state.ElementAt(-1));
        Assert.Empty(state.ElementsFrom(expected.Length));
        if (expected.Length > 2) Assert.True(expected.Skip(2).SequenceEqual(state.ElementsFrom(2)));
    }

    [Theory]
    [InlineData(512)]
    [InlineData(4)]
    [InlineData(2)]
    public void IndexAgreesWithTheTreeAcrossRandomEditsAndShuffledDelivery(int splitThreshold)
    {
        for (int seed = 0; seed < 40; seed++)
        {
            var rng = new Random(seed);
            var replicas = Enumerable.Range(0, 3).Select(i => new ListReplica("r" + i, new ListState(null, splitThreshold))).ToArray();
            int fresh = 0;
            for (int round = 0; round < 12; round++)
            {
                var authored = new List<ListOp>();
                foreach (var r in replicas)
                    for (int step = rng.Next(1, 8); step > 0; step--)
                    {
                        int live = r.State.LiveCount;
                        switch (live == 0 ? 0 : rng.Next(5))
                        {
                            case 0 or 1: authored.Add(r.InsertAt(rng.Next(live + 1), "e" + fresh++)); break;
                            case 2 or 3:
                                {
                                    var op = r.MoveToIndex(r.State.ElementAt(rng.Next(live)), rng.Next(live));
                                    if (op is not null) authored.Add(op);
                                    break;
                                }
                            default: authored.Add(r.Delete(r.State.ElementAt(rng.Next(live)))); break;
                        }
                        AssertIndex($"seed {seed} round {round} local", r.State);
                    }
                foreach (var r in replicas)
                    foreach (var op in Support.Shuffle(authored, rng)) r.Receive(op);
                foreach (var r in replicas) AssertIndex($"seed {seed} round {round} merged", r.State);
                Support.AssertSame($"seed {seed} round {round}", replicas.Select(r => r.State).ToArray());
            }
        }
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    public void ConcurrentOutOfOrderMergeAcrossManyChunksAgrees(int splitThreshold)
    {
        for (int seed = 0; seed < 10; seed++)
        {
            var rng = new Random(seed);
            var replicas = Enumerable.Range(0, 3).Select(i => new ListReplica("r" + i, new ListState(null, splitThreshold))).ToArray();
            int fresh = 0;
            for (int round = 0; round < 6; round++)
            {
                var authored = new List<ListOp>();
                foreach (var r in replicas)
                    for (int step = 0; step < 40; step++)
                    {
                        int live = r.State.LiveCount;
                        switch (live < 3 ? 0 : rng.Next(6))
                        {
                            case 0 or 1 or 2: authored.Add(r.InsertAt(rng.Next(live + 1), "e" + fresh++)); break;
                            case 3 or 4:
                                if (r.MoveToIndex(r.State.ElementAt(rng.Next(live)), rng.Next(live)) is { } move) authored.Add(move);
                                break;
                            default: authored.Add(r.Delete(r.State.ElementAt(rng.Next(live)))); break;
                        }
                    }
                foreach (var r in replicas)
                    foreach (var op in Support.Shuffle(authored.Concat(authored.Take(10)), rng)) r.Receive(op);
                foreach (var r in replicas) AssertIndex($"seed {seed} round {round}", r.State);
                Support.AssertSame($"seed {seed} round {round}", replicas.Select(r => r.State).ToArray());
            }
        }
    }

    [Fact]
    public void IndexSpansManyChunksAndStaysConsistentUnderHeadMiddleAndTailEdits()
    {
        var rng = new Random(7);
        var replica = new ListReplica("w");
        for (int i = 0; i < 3000; i++)
        {
            int live = replica.State.LiveCount;
            replica.InsertAt(rng.Next(4) switch { 0 => 0, 1 => live, _ => rng.Next(live + 1) }, "e" + i);
        }
        AssertIndex("built", replica.State);
        for (int i = 0; i < 600; i++)
        {
            int live = replica.State.LiveCount;
            if (i % 3 == 0) replica.Delete(replica.State.ElementAt(rng.Next(live)));
            else replica.MoveToIndex(replica.State.ElementAt(rng.Next(live)), rng.Next(live));
        }
        AssertIndex("edited", replica.State);
        AssertIndex("restored", NodeRowEncoding.Restore(replica.State.ExportRows().Reverse()));
        AssertIndex("cloned", replica.State.Clone());
    }

    [Fact]
    public void OriginsAtIndexIgnoresTheMovedItemAndRejectsOutOfRangeIndexes()
    {
        var state = ListState.FromInitial(["a", "b", "c", "d"]);
        // Placing "b" at final index 2 means directly after "d" once "b" is out of the way.
        var (left, _) = state.OriginsAtIndex(3, ignore: "b");
        Assert.Equal(state.PositionOf("d"), left);
        Assert.Throws<ArgumentOutOfRangeException>(() => state.OriginsAtIndex(4, ignore: "b"));
        Assert.Throws<ArgumentOutOfRangeException>(() => state.OriginsAtIndex(5));
        Assert.Equal(state.PositionOf("d"), state.OriginsAtIndex(4).Left);
        Assert.Equal(state.PositionOf("a"), state.OriginsAtIndex(1, ignore: "c").Left);
        Assert.Equal(state.PositionOf("c"), state.OriginsAtIndex(2, ignore: "a").Left);
    }
}
