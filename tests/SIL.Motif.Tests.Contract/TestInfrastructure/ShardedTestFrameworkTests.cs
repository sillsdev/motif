using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.TestInfrastructure;

public sealed class ShardedTestFrameworkTests
{
    [Fact]
    public void ShardsPartitionEveryClassExactlyOnce()
    {
        var classes = Enumerable.Range(0, 500).Select(i => $"SIL.Motif.Tests.Sample{i}Tests").ToArray();

        foreach (var count in new[] { 1, 2, 3, 4, 7 })
        {
            var shards = classes.GroupBy(name => ShardedTestFramework.ShardOf(name, count)).ToArray();
            Assert.All(shards, shard => Assert.InRange(shard.Key, 0, count - 1));
            Assert.Equal(classes.Length, shards.Sum(shard => shard.Count()));
            Assert.Equal(count, shards.Length);
        }
    }

    [Fact]
    public void AClassBelongsToTheSameShardInEveryProcess()
    {
        // A fixed expectation, not a recomputation: a per-process hash would pass a same-process comparison.
        Assert.Equal(3, ShardedTestFramework.ShardOf("SIL.Motif.Tests.Cli.PendingApplyArgvTests", 4));
        Assert.Equal(0, ShardedTestFramework.ShardOf("SIL.Motif.Tests.Cli.PendingApplyArgvTests", 1));
    }

    [Fact]
    public void WeightsDealTheHeaviestClassesApart()
    {
        var weights = new Dictionary<string, double> { ["A"] = 100, ["B"] = 90, ["C"] = 20, ["D"] = 10 };

        var assignment = ShardedTestFramework.Assign(["D", "C", "B", "A", "E"], 2, weights);

        Assert.NotEqual(assignment["A"], assignment["B"]);
        Assert.Equal(assignment["B"], assignment["C"]);
        Assert.Equal(assignment["A"], assignment["D"]);
        Assert.Equal(assignment["A"], assignment["E"]);
        Assert.Equal(assignment, ShardedTestFramework.Assign(["E", "A", "B", "C", "D"], 2, weights));
    }

    [Fact]
    public void WithoutWeightsAClassKeepsItsHashedShard() =>
        Assert.Equal(ShardedTestFramework.ShardOf("SIL.Motif.Tests.Cli.PendingApplyArgvTests", 4),
            ShardedTestFramework.Assign(["SIL.Motif.Tests.Cli.PendingApplyArgvTests"], 4, null)
                ["SIL.Motif.Tests.Cli.PendingApplyArgvTests"]);

    [Theory]
    [InlineData(null, null, null)]
    [InlineData("", null, null)]
    [InlineData("0/1", 0, 1)]
    [InlineData("3/4", 3, 4)]
    public void AShardValueParsesAsIndexOverCount(string? value, int? index, int? count)
    {
        var shard = ShardedTestFramework.ParseShard(value);
        Assert.Equal(index, shard?.Index);
        Assert.Equal(count, shard?.Count);
    }

    [Theory]
    [InlineData("4/4")]
    [InlineData("-1/4")]
    [InlineData("1/0")]
    [InlineData("1")]
    [InlineData("a/b")]
    public void AMalformedShardValueIsRefusedRatherThanRunningNothing(string value) =>
        Assert.Throws<InvalidOperationException>(() => ShardedTestFramework.ParseShard(value));
}
