using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.WritingSystems;

/// <summary>
/// Pins that caches opened by this test process save their shared writing systems into the process's
/// own repository and never into the machine-wide one, which concurrent shards would contend on.
/// </summary>
[Collection(LcmCacheTestCollection.Name)]
public sealed class ProcessWritingSystemRepositoryTests
{
    private readonly PristineProjectFixture _pristine;

    public ProcessWritingSystemRepositoryTests(PristineProjectFixture pristine)
    {
        _pristine = pristine;
    }

    [Fact]
    public void TheProcessRepositoryIsStillTheOneLibLcmHandsOutAfterACacheOpenAndDispose()
    {
        using (_pristine.NewScratch()) { }

        Assert.NotNull(ProcessWritingSystemRepository.Installed);
        Assert.Same(ProcessWritingSystemRepository.Installed, ProcessWritingSystemRepository.Current);
    }

    [Fact]
    public void TheSeededMastersWritingSystemsWereSavedIntoTheProcessRepository()
    {
        var saved = Directory.EnumerateFiles(ProcessWritingSystemRepository.BasePath, "*.ldml", SearchOption.AllDirectories)
            .Select(Path.GetFileNameWithoutExtension)
            .ToArray();

        Assert.Contains(NewLangProjFixture.VernacularTag, saved);
    }
}
