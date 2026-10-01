using SIL.LCModel;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Host;

[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group1)]
public sealed class ConcurrentCacheStartupTests(PristineProjectFixture projects)
{
    [Fact]
    public async Task ConcurrentCacheOpensCompleteWithoutCorruptingStartup()
    {
        for (var round = 0; round < 5; round++)
        {
            var paths = Enumerable.Range(0, 8).Select(_ => projects.CopyProjectFile()).ToArray();
            using var start = new ManualResetEventSlim();
            var opens = paths.Select(path => Task.Factory.StartNew(() =>
            {
                start.Wait();
                using var cache = new FwDataProjectLoader().LoadScratchCache(path);
                return cache.LanguageProject.Guid;
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();

            start.Set();
            var identities = await Task.WhenAll(opens);
            Assert.All(identities, identity => Assert.NotEqual(Guid.Empty, identity));
        }
    }
}
