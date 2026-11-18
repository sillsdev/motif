using SIL.ListCrdt;
using Xunit;

namespace SIL.ListCrdt.Tests;

static class Support
{
    public static string[] Live(this ListReplica r) => r.State.Elements().ToArray();
    public static string Text(this ListReplica r) => string.Concat(r.State.Elements());

    /// <summary>Deliver every operation to every other replica (any order is fine; this is in authoring order).</summary>
    public static void SyncAll(IReadOnlyList<ListReplica> replicas, IEnumerable<ListOp> ops)
    {
        var all = ops.ToArray();
        foreach (var r in replicas)
            foreach (var op in all) r.Receive(op);
    }

    public static void AssertSame(string label, params ListState[] states)
    {
        var first = states[0];
        foreach (var s in states.Skip(1))
        {
            Assert.True(first.Elements().SequenceEqual(s.Elements()), label + ": live order differs");
            Assert.True(first.AllElementsInOrder().SequenceEqual(s.AllElementsInOrder()), label + ": order with tombstones differs");
            Assert.True(first.NodeOrder().SequenceEqual(s.NodeOrder()), label + ": node order differs");
            Assert.Equal(first.ElementCount, s.ElementCount);
            Assert.Equal(first.NodeCount, s.NodeCount);
            Assert.Equal(0, s.PendingCount);
        }
        Assert.Equal(0, first.PendingCount);
    }

    public static void AssertConsistent(string label, ListState s)
    {
        var live = s.Elements().ToList();
        Assert.True(live.Count == live.Distinct().Count(), label + ": duplicate live element");
        var all = s.AllElementsInOrder().ToList();
        Assert.True(all.Count == all.Distinct().Count(), label + ": element appears twice");
        Assert.Equal(s.ElementCount, all.Count);
        var order = s.NodeOrder().ToList();
        Assert.Equal(s.NodeCount, order.Count);
        for (int i = 0; i < order.Count; i++)
        {
            var (_, right) = s.OriginsAfterNode(order[i]);
            Assert.True(i + 1 < order.Count ? right == order[i + 1] : right is null, label + ": successor mismatch at " + i);
        }
        Assert.Equal(order.Count == 0 ? null : order[0], s.OriginsAfterNode(null).Right);
    }

    public static List<T> Shuffle<T>(IEnumerable<T> items, Random rng)
    {
        var list = items.ToList();
        for (int i = list.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); (list[i], list[j]) = (list[j], list[i]); }
        return list;
    }
}
