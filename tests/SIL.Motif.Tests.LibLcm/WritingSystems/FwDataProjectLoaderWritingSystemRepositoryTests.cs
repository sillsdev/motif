using SIL.LCModel.Core.WritingSystems;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Tests.TestFixtures;
using SIL.WritingSystems;
using Xunit;

namespace SIL.Motif.Tests.WritingSystems;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class WritingSystemRepositoryInitializationCollection
{
    public const string Name = "Writing-system repository initialization";
}

[Collection(WritingSystemRepositoryInitializationCollection.Name)]
public sealed class FwDataProjectLoaderWritingSystemRepositoryTests
{
    [Fact]
    public void InitInstallsTheRepositorySelectedByTheEnvironment()
    {
        FwDataProjectLoader.Init();

        var repository = Assert.IsType<CoreGlobalWritingSystemRepository>(
            ProcessWritingSystemRepository.CurrentOutsideScratchLoads());
        Assert.Equal(
            CoreGlobalWritingSystemRepository.CurrentVersionPath(ProcessWritingSystemRepository.BasePath),
            repository.PathToWritingSystems);
    }

    [Fact]
    public void InitPointsTheDefaultWritingSystemRepositoryAtTheConfiguredPath()
    {
        FwDataProjectLoader.Init();

        Assert.Equal(Path.GetFullPath(ProcessWritingSystemRepository.BasePath),
            GlobalWritingSystemRepository<WritingSystemDefinition>.DefaultBasePath);
        Assert.Equal(Path.GetFullPath(ProcessWritingSystemRepository.BasePath),
            GlobalWritingSystemRepository<CoreWritingSystemDefinition>.DefaultBasePath);
        using var defaultRepository = new CoreGlobalWritingSystemRepository();
        Assert.Equal(CoreGlobalWritingSystemRepository.CurrentVersionPath(ProcessWritingSystemRepository.BasePath),
            defaultRepository.PathToWritingSystems);
    }

    [Fact]
    public void InitReadsTheSldrCacheTheEnvironmentNames()
    {
        FwDataProjectLoader.Init();

        Assert.Equal(
            Path.GetFullPath(Environment.GetEnvironmentVariable(FwDataProjectLoader.SldrCachePathVariable)!),
            Sldr.SldrCachePath);
        Assert.Equal(Path.GetFullPath(ProcessWritingSystemRepository.SldrCachePath), Sldr.SldrCachePath);
    }

    [Fact]
    public void InitKeepsSldrLookupsOffTheNetworkWhenTheEnvironmentAsks()
    {
        Assert.False(string.IsNullOrWhiteSpace(
            Environment.GetEnvironmentVariable(FwDataProjectLoader.SldrOfflineVariable)));
        FwDataProjectLoader.Init();

        var destination = Path.Combine(Path.GetTempPath(), "motif-sldr-offline-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(destination);
        try
        {
            // Only a lookup that reached the SLDR service reports FromSldr; offline ones read the cache or miss.
            Assert.NotEqual(SldrStatus.FromSldr, Sldr.GetLdmlFile(destination, "en", [], out _));
        }
        finally
        {
            Directory.Delete(destination, recursive: true);
        }
    }
}
