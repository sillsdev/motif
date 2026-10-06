using SIL.Motif.Contract.Projects;
using SIL.Motif.Host.Store;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Baselines;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class RepositoryReadCounterScopeTests
{
    [Fact]
    public async Task ConcurrentObservationsCountOnlyTheirOwnRepositoryReads()
    {
        var root = Path.Combine(Path.GetTempPath(), "SIL.Motif.RepositoryReadCounterScopeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var first = Database(root, "first");
            using var second = Database(root, "second");
            using var barrier = new Barrier(2);

            var counts = await Task.WhenAll(Observe(first, barrier), Observe(second, barrier));

            Assert.All(counts, count => Assert.Equal(new ScaleRepositoryReadCount(1, 0), count));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task<ScaleRepositoryReadCount> Observe(MotifDatabase database, Barrier barrier)
    {
        using var observation = ScaleCountHarness.ObserveRepositoryReads();
        await Task.Run(() => barrier.SignalAndWait());
        await Task.Run(() => new BaselineRepository(database).GetCurrent("workspace"));
        return observation.Snapshot();
    }

    private static MotifDatabase Database(string root, string name)
    {
        var project = new ProjectLocator(Path.Combine(root, name + ".fwdata"), name);
        return MotifDatabase.OpenOwned(Path.Combine(root, name + ".motif.db"), project,
            MotifSchema.CurrentSchema, new Version(1, 0));
    }
}
