using LoroCs;
using LoroCs.Extensions;
using SIL.ListCrdt;
using Xunit;
using Xunit.Abstractions;
namespace SIL.ListCrdt.LoroDifferential;

public class ResidualMoveDifferenceTests(ITestOutputHelper output)
{
    public record Step(int Round, int Peer, char Kind, string Name, string? Pred)
    {
        public override string ToString() => $"r{Round} p{Peer}: " + Kind switch { 'i' => $"insert {Name} after {Pred ?? "START"}", 'm' => $"move {Name} after {Pred ?? "START"}", _ => $"delete {Name}" };
    }
    sealed class Str(string s) : LoroValueLike { public LoroValue AsLoroValue() => new LoroValue.String(s); }
    static string[] Vals(LoroMovableList l) => l.ToVec().Select(v => ((LoroValue.String)v).Value).ToArray();

    public static (string, string, int, string)? Run(int peers, List<Step> steps)
    {
        var docs = Enumerable.Range(0, peers).Select(i => { var d = new LoroDoc(); d.SetPeerId((ulong)(i + 1)); return d; }).ToArray();
        var lists = docs.Select(d => d.GetMovableList("l".ToLoroContainerIdLike())).ToArray();
        var reps = Enumerable.Range(0, peers).Select(i => new ListReplica("r" + (i + 1))).ToArray();
        var log = new System.Text.StringBuilder();
        try
        {
            foreach (var grp in steps.GroupBy(o => o.Round).OrderBy(g => g.Key))
            {
                var sent = new List<ListOp>[peers]; for (int p = 0; p < peers; p++) sent[p] = [];
                foreach (var s in grp)
                {
                    int p = s.Peer; var live = reps[p].State.Elements().ToList();
                    int pi = s.Pred is null ? -1 : live.IndexOf(s.Pred);
                    if (s.Pred is not null && pi < 0) continue;
                    if (s.Kind == 'i') { if (reps[p].State.Contains(s.Name)) continue; int i = pi + 1; lists[p].Insert((uint)i, new Str(s.Name)); sent[p].Add(reps[p].InsertAt(i, s.Name)); log.AppendLine($"  {s}   [{string.Join(' ', live)}]"); }
                    else
                    {
                        int f = live.IndexOf(s.Name); if (f < 0) continue;
                        if (s.Kind == 'd') { lists[p].Delete((uint)f, 1); sent[p].Add(reps[p].Delete(s.Name)); log.AppendLine($"  {s}   [{string.Join(' ', live)}]"); continue; }
                        if (s.Pred == s.Name) continue;
                        var lp = live.Where(x => x != s.Name).ToList(); int t = s.Pred is null ? 0 : lp.IndexOf(s.Pred) + 1;
                        if (t == f) continue;
                        lists[p].Mov((uint)f, (uint)t); sent[p].Add(reps[p].MoveToIndex(s.Name, t)!); log.AppendLine($"  {s}   [{string.Join(' ', live)}]");
                    }
                }
                foreach (var d in docs) d.Commit();
                var all = sent.SelectMany(x => x).ToList();
                var moved = all.Where(o => o.Kind == ListOpKind.Move).Select(o => o.Element).ToHashSet();
                if (all.Any(o => o.Kind == ListOpKind.Delete && moved.Contains(o.Element))) return null;
                var snaps = docs.Select(d => d.ExportSnapshot()).ToArray();
                for (int p = 0; p < peers; p++) for (int q = 0; q < peers; q++) if (p != q) { docs[p].Import(snaps[q]); foreach (var o in sent[q]) reps[p].Receive(o); }
                var lo = string.Join(' ', Vals(lists[0])); var ou = string.Join(' ', reps[0].State.Elements());
                if (lo != ou) return (lo, ou, grp.Key, log.ToString());
                log.AppendLine($"  => {lo}");
            }
            return null;
        }
        finally { foreach (var d in docs) d.Dispose(); }
    }

    [Fact]
    public void ResidualDifference_MoveVersusInsertBesideAbandonedMoveSlot_IsPinned()
    {
        var r = Run(3,
        [
            new(0, 0, 'i', "a", null),
            new(1, 2, 'i', "6", "a"), new(1, 2, 'i', "7", "a"),
            new(2, 0, 'm', "6", "a"), new(2, 2, 'm', "6", "a"), new(2, 0, 'i', "8", null),
            new(3, 1, 'i', "x", "a"), new(3, 2, 'm', "8", "a"),
        ]);
        Assert.NotNull(r);
        Assert.Equal("a 8 x 6 7", r!.Value.Item1);
        Assert.Equal("a x 8 6 7", r.Value.Item2);
        output.WriteLine(r.Value.Item4);
    }
}
