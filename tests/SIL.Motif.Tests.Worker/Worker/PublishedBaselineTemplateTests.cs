using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Worker;

[Collection(LcmCacheTestCollection.Name)]
public sealed class PublishedBaselineTemplateTests(PristineProjectFixture pristine)
{
    [Fact]
    public void CopiesAndEvidenceReadsDoNotCarryOrAcquireTheSharedTemplatesProjectLock()
    {
        var name = "lock-isolation-" + Guid.NewGuid().ToString("N");
        var template = PublishedBaselineTemplate<SeededText>.Get(name, pristine,
            cache => SeededProject.SeedText(cache, pristine.Seed));
        var cacheRoot = Assert.IsType<string>(Environment.GetEnvironmentVariable(FixtureDiskCache.Variable));
        var templateRoot = Assert.Single(Directory.GetDirectories(cacheRoot, "published-" + name + "-*"));
        var lockName = NewLangProjFixture.ProjectName + ".fwdata.lock";
        var copy = Path.Combine(Path.GetTempPath(), "baseline-copy-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var held = new FileStream(Path.Combine(templateRoot, "published", lockName),
                FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);

            Assert.True(File.Exists(template.CopyTo(copy)));
            Assert.False(File.Exists(Path.Combine(copy, lockName)));
            Assert.NotNull(template.Projection);
            Assert.NotNull(template.Summary);
            Assert.True(File.Exists(held.Name));
        }
        finally
        {
            if (Directory.Exists(copy)) Directory.Delete(copy, recursive: true);
            Directory.Delete(templateRoot, recursive: true);
        }
    }
}
