using SIL.ListCrdt;
using Xunit;

namespace SIL.ListCrdt.Tests;

/// <summary>Single-replica behaviour against a plain List model, so authoring (origin choice) is right before concurrency is tested.</summary>
public class ModelTests
{
    [Fact]
    public void RandomIndexOperations_MatchAPlainListModel()
    {
        int checks = 0;
        for (int seed = 0; seed < 300; seed++)
        {
            var rng = new Random(seed);
            var r = new ListReplica("a");
            var model = new List<string>();
            int next = 0;
            for (int step = 0; step < 120; step++)
            {
                switch (rng.Next(5))
                {
                    case 0 or 1:
                        {
                            var e = "e" + next++; int i = rng.Next(model.Count + 1);
                            r.InsertAt(i, e); model.Insert(i, e); break;
                        }
                    case 2 when model.Count > 0:
                        {
                            var e = model[rng.Next(model.Count)]; model.Remove(e);
                            int i = rng.Next(model.Count + 1);
                            r.MoveToIndex(e, i); model.Insert(i, e); break;
                        }
                    case 3 when model.Count > 0:
                        {
                            var e = model[rng.Next(model.Count)];
                            r.Delete(e); model.Remove(e); break;
                        }
                    case 4 when model.Count > 2:
                        {
                            int n = rng.Next(1, Math.Min(4, model.Count));
                            int start = rng.Next(model.Count - n + 1);
                            var block = model.GetRange(start, n); model.RemoveRange(start, n);
                            int i = rng.Next(model.Count + 1);
                            r.MoveBlock(block, i == 0 ? null : model[i - 1]); model.InsertRange(i, block); break;
                        }
                }
                Assert.True(model.SequenceEqual(r.Live()), $"seed {seed} step {step}");
                checks++;
            }
            Support.AssertConsistent("seed " + seed, r.State);
        }
        Assert.Equal(300 * 120, checks);
    }

    [Fact]
    public void FromInitial_BuildsAChainInOrder_AndSupportsLargeLists()
    {
        var ids = Enumerable.Range(0, 40000).Select(i => "id" + i).ToArray();
        var s = ListState.FromInitial(ids);
        Assert.True(ids.SequenceEqual(s.Elements()));
        var r = new ListReplica("a", s);
        r.Move("id10", "id39999");
        r.Insert("new", "id5");
        Assert.Equal("id11", r.Live()[11]);
        Assert.Equal("id10", r.Live()[^1]);
        Assert.Equal("new", r.Live()[6]);
    }
}
