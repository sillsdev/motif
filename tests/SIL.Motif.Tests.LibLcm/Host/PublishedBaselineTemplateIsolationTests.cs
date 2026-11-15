using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.Texts;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Baselines;
using Xunit;

namespace SIL.Motif.Tests.Host;

[Collection(LcmCacheTestCollection.Name)]
public sealed class PublishedBaselineTemplateIsolationTests(PristineProjectFixture pristine) : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pti", Guid.NewGuid().ToString("N")[..8]);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CopiesAndEvidenceReadsRemainUsableWhileTheSharedMasterIsOpen(bool readEvidence)
    {
        var previous = Environment.GetEnvironmentVariable(FixtureDiskCache.Variable);
        var cacheRoot = Path.Combine(_root, "masters");
        Environment.SetEnvironmentVariable(FixtureDiskCache.Variable, cacheRoot);
        try
        {
            var template = PublishedBaselineTemplate<string>.Get("held-" + Guid.NewGuid().ToString("N")[..8],
                pristine, _ => "metadata");
            var masterPath = Path.Combine(Directory.GetDirectories(cacheRoot).Single(), "published",
                NewLangProjFixture.ProjectName + ".fwdata");
            using var master = new FwDataProjectLoader().LoadScratchCache(masterPath);
            Assert.True(File.Exists(masterPath + ".lock"));
            var before = File.ReadAllBytes(masterPath);

            if (readEvidence)
            {
                Assert.Equal(ProjectionJson.Serialize(ProjectSummaryReader.Read(master)),
                    ProjectionJson.Serialize(template.Summary));
                Assert.Equal(ProjectionJson.Serialize(TextWordsProjectionBuilder.Build(master, CancellationToken.None)),
                    ProjectionJson.Serialize(template.Projection));
            }
            else
            {
                var copy = template.CopyTo(Path.Combine(_root, "copy"));
                Assert.False(File.Exists(copy + ".lock"));
                using var reader = new FwDataProjectLoader().LoadScratchCache(copy);
                Assert.Equal(master.LangProject.Guid, reader.LangProject.Guid);
            }
            Assert.Equal(before, File.ReadAllBytes(masterPath));
        }
        finally
        {
            Environment.SetEnvironmentVariable(FixtureDiskCache.Variable, previous);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
