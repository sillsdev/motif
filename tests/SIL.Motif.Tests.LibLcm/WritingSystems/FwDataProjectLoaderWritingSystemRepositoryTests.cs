using SIL.LCModel.Core.WritingSystems;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.PanGloss;
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

        var repository = Assert.IsAssignableFrom<CoreGlobalWritingSystemRepository>(
            ProcessWritingSystemRepository.CurrentOutsideScratchLoads());
        Assert.Equal(
            CoreGlobalWritingSystemRepository.CurrentVersionPath(ProcessWritingSystemRepository.BasePath),
            repository.PathToWritingSystems);
    }

    [Fact]
    public void RepositoryInitializationUsesTheLockPathInheritedByChildren()
    {
        if (OperatingSystem.IsWindows()) return;

        var repository = Assert.IsType<SerializedGlobalWritingSystemRepository>(
            ProcessWritingSystemRepository.CurrentOutsideScratchLoads());
        var lockName = SerializedGlobalWritingSystemRepository.GetRepositoryLockName(repository.PathToWritingSystems);
        var inheritedPath = WorkerLockPaths.GetLockPath(lockName, machineWide: false);
        var unscopedPath = Path.Combine(Path.GetTempPath(), Path.GetFileName(inheritedPath));

        Assert.True(unscopedPath == inheritedPath || !File.Exists(unscopedPath),
            $"The test host initialized the repository lock outside its namespace: {unscopedPath}");
        Assert.True(File.Exists(inheritedPath),
            $"The test host did not initialize the lock its children inherit: {inheritedPath}");
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
    public void InitKeepsSldrLookupsOffTheNetworkByDefault()
    {
        Assert.True(string.IsNullOrWhiteSpace(
            Environment.GetEnvironmentVariable(FwDataProjectLoader.SldrOnlineVariable)));
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
