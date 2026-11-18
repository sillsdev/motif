using LoroCs;
using LoroCs.Extensions;
using SIL.ListCrdt;
using Xunit;
using Xunit.Abstractions;

namespace SIL.ListCrdt.LoroDifferential;

public class LoroDifferentialTests(ITestOutputHelper output)
{
    static LoroMovableList ListOf(LoroDoc doc) => doc.GetMovableList("l".ToLoroContainerIdLike());

    static string[] Values(LoroMovableList l) =>
        l.ToVec().Select(v => v is LoroValue.String s ? s.Value : throw new InvalidOperationException("non-string")).ToArray();

    static LoroDoc NewDoc(ulong peer) { var d = new LoroDoc(); d.SetPeerId(peer); return d; }

    [Fact]
    public void SingleReplicaOperations_AgreeExactlyWithLoroAndAPlainList()
    {
        for (int seed = 0; seed < 100; seed++)
        {
            var rng = new Random(seed);
            using var doc = NewDoc(1); var loro = ListOf(doc);
            var ours = new ListReplica("r1"); var model = new List<string>();
            int fresh = 0;
            for (int step = 0; step < 100; step++)
            {
                switch (rng.Next(4))
                {
                    case 0 or 1:
                        { var e = "e" + fresh++; int i = rng.Next(model.Count + 1); loro.Insert((uint)i, new Str(e)); ours.InsertAt(i, e); model.Insert(i, e); break; }
                    case 2 when model.Count > 1:
                        { int from = rng.Next(model.Count), to = rng.Next(model.Count); var e = model[from]; loro.Mov((uint)from, (uint)to); ours.MoveToIndex(e, to); model.RemoveAt(from); model.Insert(to, e); break; }
                    case 3 when model.Count > 0:
                        { int at = rng.Next(model.Count); var e = model[at]; loro.Delete((uint)at, 1); ours.Delete(e); model.RemoveAt(at); break; }
                }
                var l = Values(loro);
                Assert.True(model.SequenceEqual(l), $"seed {seed} step {step}: Loro differs from the model");
                Assert.True(model.SequenceEqual(Live(ours)), $"seed {seed} step {step}: ListCrdt differs from the model");
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConcurrentRounds_BothConverge_SameMembership_ReportOrderAgreement(bool withMoves)
    {
        int rounds = 0, sameOrder = 0, policyRounds = 0, policySetDiffs = 0, orderDiffs = 0;
        for (int seed = 0; seed < 60; seed++)
        {
            var rng = new Random(seed * 31 + (withMoves ? 1 : 0));
            int peers = rng.Next(2, 4);
            var docs = Enumerable.Range(0, peers).Select(i => NewDoc((ulong)(i + 1))).ToArray();
            var lists = docs.Select(ListOf).ToArray();
            var reps = Enumerable.Range(0, peers).Select(i => new ListReplica("r" + (i + 1))).ToArray();
            int fresh = 0;
            for (int round = 0; round < 8; round++)
            {
                var sent = new List<ListOp>[peers];
                for (int p = 0; p < peers; p++)
                {
                    sent[p] = [];
                    int n = rng.Next(1, 5);
                    for (int k = 0; k < n; k++)
                    {
                        var live = Live(reps[p]);
                        int choice = rng.Next(withMoves ? 6 : 4);
                        if (choice < 3 || live.Length < 2)
                        {
                            var e = $"p{p}-{fresh++}"; int i = rng.Next(live.Length + 1);
                            lists[p].Insert((uint)i, new Str(e)); sent[p].Add(reps[p].InsertAt(i, e));
                        }
                        else if (choice == 3)
                        {
                            int at = rng.Next(live.Length); lists[p].Delete((uint)at, 1); sent[p].Add(reps[p].Delete(live[at]));
                        }
                        else
                        {
                            int from = rng.Next(live.Length), to = rng.Next(live.Length); var e = live[from];
                            lists[p].Mov((uint)from, (uint)to); if (reps[p].MoveToIndex(e, to) is { } move) sent[p].Add(move);
                        }
                    }
                    docs[p].Commit();
                }
                var snapshots = docs.Select(d => d.ExportSnapshot()).ToArray();
                for (int p = 0; p < peers; p++)
                    for (int q = 0; q < peers; q++)
                        if (p != q) docs[p].Import(snapshots[q]);
                for (int p = 0; p < peers; p++)
                    for (int q = 0; q < peers; q++)
                        if (p != q) foreach (var op in Support(sent[q], rng)) reps[p].Receive(op);
                var loroOrders = lists.Select(Values).ToArray();
                var ourOrders = reps.Select(Live).ToArray();
                Assert.True(loroOrders.All(o => o.SequenceEqual(loroOrders[0])), $"seed {seed} round {round}: Loro replicas diverged");
                Assert.True(ourOrders.All(o => o.SequenceEqual(ourOrders[0])), $"seed {seed} round {round}: ListCrdt replicas diverged");
                Assert.Equal(ourOrders[0].Length, ourOrders[0].Distinct().Count());
                Assert.Equal(loroOrders[0].Length, loroOrders[0].Distinct().Count());
                rounds++;
                var moved = sent.SelectMany(x => x).Where(o => o.Kind == ListOpKind.Move).Select(o => o.Element).ToHashSet();
                var policyElements = sent.SelectMany(x => x)
                    .Where(op => op.Kind == ListOpKind.Delete && moved.Contains(op.Element))
                    .Select(op => op.Element).ToHashSet(StringComparer.Ordinal);
                bool policyRound = policyElements.Count > 0;
                if (policyRound)
                {
                    policyRounds++;
                    Assert.Equal(ourOrders[0].Where(item => !policyElements.Contains(item)).Order(StringComparer.Ordinal),
                        loroOrders[0].Where(item => !policyElements.Contains(item)).Order(StringComparer.Ordinal));
                    if (!loroOrders[0].OrderBy(x => x).SequenceEqual(ourOrders[0].OrderBy(x => x))) policySetDiffs++;
                    break;
                }
                if (!loroOrders[0].SequenceEqual(ourOrders[0]))
                {
                    orderDiffs++;
                    if (!withMoves) Assert.Fail($"seed {seed} round {round}: insert/delete order differs from Loro\nLoro: {string.Join(' ', loroOrders[0])}\nOurs: {string.Join(' ', ourOrders[0])}");
                    break;
                }
                sameOrder++;
            }
            foreach (var d in docs) d.Dispose();
        }
        output.WriteLine($"withMoves={withMoves}: rounds compared={rounds}, identical order={sameOrder}, order differences (moves, not a delete policy case)={orderDiffs}, move-vs-delete policy rounds={policyRounds} (live set differed in {policySetDiffs})");
        Assert.True(sameOrder > 200);
        Assert.Equal(0, orderDiffs);
        if (!withMoves) Assert.Equal(0, policyRounds);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConcurrentMoveDeleteKeepsUnaffectedMembershipAndOrder(bool moveToStart)
    {
        using var docA = NewDoc(1);
        using var docB = NewDoc(2);
        var nativeA = ListOf(docA);
        var nativeB = ListOf(docB);
        var a = new ListReplica("r1");
        var b = new ListReplica("r2");
        string[] initial = ["left", "victim", "right", "tail"];
        for (int i = 0; i < initial.Length; i++)
        {
            b.Receive(a.InsertAt(i, initial[i]));
            nativeA.Insert((uint)i, new Str(initial[i]));
        }
        docA.Commit();
        docB.Import(docA.ExportSnapshot());

        int destination = moveToStart ? 0 : 3;
        nativeA.Mov(1, (uint)destination);
        var move = a.MoveToIndex("victim", destination)!;
        nativeA.Insert(moveToStart ? 2u : 1u, new Str("aside"));
        var aside = a.Insert("aside", "left");
        nativeB.Delete(1, 1);
        var delete = b.Delete("victim");
        nativeB.Insert(2, new Str("new"));
        var insert = b.InsertAt(2, "new");
        docA.Commit();
        docB.Commit();
        var snapshotA = docA.ExportSnapshot();
        var snapshotB = docB.ExportSnapshot();
        docA.Import(snapshotB);
        docB.Import(snapshotA);
        a.Receive(insert);
        a.Receive(delete);
        b.Receive(aside);
        b.Receive(move);

        string[] expected = ["left", "aside", "right", "new", "tail"];
        Assert.Equal(expected, a.State.Elements());
        Assert.Equal(expected, b.State.Elements());
        Assert.True(a.State.IsDeleted("victim"));
        Assert.True(b.State.IsDeleted("victim"));
        Assert.Equal(a.State.NodeOrder(), b.State.NodeOrder());
        Assert.Equal(expected, Values(nativeA).Where(item => item != "victim"));
        Assert.Equal(expected, Values(nativeB).Where(item => item != "victim"));
        output.WriteLine($"moveToStart={moveToStart}: permanent-delete result={string.Join(' ', a.State.Elements())}; " +
            $"Loro result={string.Join(' ', Values(nativeA))}");
    }

    sealed class Str(string s) : LoroValueLike { public LoroValue AsLoroValue() => new LoroValue.String(s); }
    static string[] Live(ListReplica r) => r.State.Elements().ToArray();

    static IEnumerable<ListOp> Support(List<ListOp> ops, Random rng) => ops.OrderBy(_ => rng.Next());
}
