using SIL.ListCrdt;
using Xunit;

namespace SIL.ListCrdt.Tests;

/// <summary>Randomised multi-replica runs: random ops, random reordered and duplicated delivery, then convergence checks.</summary>
public class FuzzTests
{
    sealed class Network(int n, Random rng)
    {
        public readonly List<ListReplica> Replicas = Enumerable.Range(0, n).Select(i => new ListReplica("r" + i)).ToList();
        readonly List<List<ListOp>> inbox = Enumerable.Range(0, n).Select(_ => new List<ListOp>()).ToList();
        public readonly List<ListOp> All = [];
        public readonly HashSet<string> Inserted = [];
        public readonly HashSet<string> Deleted = [];
        int fresh;

        void Broadcast(int from, params ListOp?[] ops)
        {
            foreach (var op in ops)
            {
                if (op is null) continue;
                All.Add(op);
                for (int i = 0; i < Replicas.Count; i++) if (i != from) inbox[i].Add(op);
            }
        }

        public string NewElement() => "e" + fresh++;

        public void RandomStep(bool allowStructure = true)
        {
            int who = rng.Next(Replicas.Count);
            var r = Replicas[who];
            var live = r.Live();
            switch (rng.Next(10))
            {
                case < 4:
                    {
                        var e = NewElement(); Inserted.Add(e);
                        Broadcast(who, r.InsertAt(rng.Next(live.Length + 1), e)); break;
                    }
                case < 6 when live.Length > 0:
                    Broadcast(who, r.MoveToIndex(live[rng.Next(live.Length)], rng.Next(live.Length))); break;
                case 6 when live.Length > 0:
                    {
                        var e = live[rng.Next(live.Length)]; Deleted.Add(e);
                        Broadcast(who, r.Delete(e)); break;
                    }
                case 7 when live.Length > 3:
                    {
                        int count = rng.Next(1, 4); int start = rng.Next(live.Length - count + 1);
                        var block = live.Skip(start).Take(count).ToList();
                        var rest = live.Where(e => !block.Contains(e)).ToList();
                        int at = rng.Next(rest.Count + 1);
                        Broadcast(who, r.MoveBlock(block, at == 0 ? null : rest[at - 1])); break;
                    }
                default:
                    DeliverSome(rng.Next(Replicas.Count), rng.Next(1, 6)); break;
            }
            if (rng.Next(4) == 0) DeliverSome(rng.Next(Replicas.Count), rng.Next(1, 8));
        }

        /// <summary>Deliver random pending messages to one replica: reordered, sometimes duplicated, sometimes kept for later.</summary>
        public void DeliverSome(int to, int count)
        {
            var box = inbox[to];
            for (int i = 0; i < count && box.Count > 0; i++)
            {
                int k = rng.Next(box.Count);
                var op = box[k];
                Replicas[to].Receive(op);
                if (rng.Next(5) == 0) Replicas[to].Receive(op);
                if (rng.Next(6) != 0) { box[k] = box[^1]; box.RemoveAt(box.Count - 1); }
            }
        }

        public void Quiesce()
        {
            for (int to = 0; to < Replicas.Count; to++)
            {
                foreach (var op in Support.Shuffle(inbox[to], rng)) Replicas[to].Receive(op);
                inbox[to].Clear();
            }
        }
    }

    [Theory]
    [InlineData(2, 100, 400)]
    [InlineData(3, 100, 400)]
    [InlineData(5, 100, 600)]
    public void RandomOpsWithRandomDelivery_Converge_NoDuplicates_IdentityPreserved(int replicas, int seeds, int steps)
    {
        long totalOps = 0;
        for (int seed = 0; seed < seeds; seed++)
        {
            var rng = new Random(seed * 7919 + replicas);
            var net = new Network(replicas, rng);
            for (int step = 0; step < steps; step++) net.RandomStep();
            net.Quiesce();
            var label = $"replicas {replicas} seed {seed}";
            Support.AssertSame(label, net.Replicas.Select(r => r.State).ToArray());
            var state = net.Replicas[0].State;
            Support.AssertConsistent(label, state);
            Assert.True(state.AllElementsInOrder().Order().SequenceEqual(net.Inserted.Order()), label + ": element set changed");
            Assert.True(state.Elements().Order().SequenceEqual(net.Inserted.Except(net.Deleted).Order()), label + ": live set differs from inserted minus deleted");
            foreach (var order in new[] { net.All.AsEnumerable().Reverse().ToList(), Support.Shuffle(net.All, rng), Support.Shuffle(net.All, rng) })
            {
                var fresh = ListState.Fold(order);
                Support.AssertSame(label + " refold", state, fresh);
            }
            totalOps += net.All.Count;
        }
        Assert.True(totalOps > seeds * steps / 4);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void ConcurrentForwardRuns_NeverInterleave(int replicas)
    {
        int runsChecked = 0;
        for (int seed = 0; seed < 150; seed++)
        {
            var rng = new Random(seed + 1000 * replicas);
            var rs = Enumerable.Range(0, replicas).Select(i => new ListReplica("r" + i)).ToList();
            int fresh = 0;
            var setup = new List<ListOp>();
            for (int i = 0; i < rng.Next(0, 6); i++) setup.Add(rs[0].InsertAt(rng.Next(rs[0].Live().Length + 1), "s" + fresh++));
            Support.SyncAll(rs.Skip(1).ToList(), setup);
            for (int round = 0; round < 6; round++)
            {
                var live = rs[0].Live();
                int shared = rng.Next(live.Length + 1);
                var runs = new List<(List<string> ids, List<ListOp> ops)>();
                foreach (var r in rs)
                {
                    int at = rng.Next(3) == 0 ? shared : rng.Next(live.Length + 1);
                    string? prev = at == 0 ? null : live[at - 1];
                    var ids = new List<string>(); var ops = new List<ListOp>();
                    int length = rng.Next(1, 8);
                    for (int i = 0; i < length; i++)
                    {
                        var e = $"{r.ReplicaId}-{fresh++}"; ops.Add(r.Insert(e, prev)); ids.Add(e); prev = e;
                    }
                    runs.Add((ids, ops));
                }
                foreach (var r in rs)
                    foreach (var (_, ops) in Support.Shuffle(runs, rng).Where(x => !x.ids[0].StartsWith(r.ReplicaId + "-")))
                        foreach (var op in ops) r.Receive(op);
                for (int k = 1; k < rs.Count; k++) Assert.True(rs[0].Live().SequenceEqual(rs[k].Live()), $"seed {seed} round {round}: diverged");
                var final = rs[0].Live().ToList();
                foreach (var (ids, _) in runs)
                {
                    int start = final.IndexOf(ids[0]);
                    Assert.True(final.Skip(start).Take(ids.Count).SequenceEqual(ids), $"seed {seed} round {round}: a concurrent run was interleaved");
                    runsChecked++;
                }
            }
        }
        Assert.Equal(150 * 6 * replicas, runsChecked);
    }

    [Fact]
    public void ConcurrentMovesOfOneElement_AlwaysLeaveExactlyOneCopyAtTheTimestampWinner()
    {
        for (int seed = 0; seed < 200; seed++)
        {
            var rng = new Random(seed);
            int n = rng.Next(2, 6);
            var rs = Enumerable.Range(0, n).Select(i => new ListReplica("r" + i)).ToList();
            var setup = new List<ListOp>(); string? prev = null;
            for (int i = 0; i < 8; i++) { setup.Add(rs[0].Insert("x" + i, prev)); prev = "x" + i; }
            Support.SyncAll(rs.Skip(1).ToList(), setup);
            var moves = new List<ListOp>();
            foreach (var r in rs) moves.Add(r.Move("x3", $"x{(new[] { 0, 1, 5, 6, 7 })[rng.Next(5)]}")!);
            foreach (var r in rs) foreach (var m in Support.Shuffle(moves, rng)) r.Receive(m);
            var winner = moves.OrderBy(m => m.Lamport).ThenBy(m => m.Id).Last();
            foreach (var r in rs)
            {
                Assert.Equal(1, r.Live().Count(e => e == "x3"));
                Assert.Equal(winner.Id, r.State.PositionOf("x3"));
                Assert.Equal(8, r.Live().Length);
            }
        }
    }
}
