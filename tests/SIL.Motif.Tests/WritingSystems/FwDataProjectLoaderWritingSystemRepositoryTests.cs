using SIL.LCModel.Core.WritingSystems;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Tests.TestFixtures;
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

        var repository = Assert.IsType<CoreGlobalWritingSystemRepository>(ProcessWritingSystemRepository.Current);
        Assert.Equal(
            CoreGlobalWritingSystemRepository.CurrentVersionPath(ProcessWritingSystemRepository.BasePath),
            repository.PathToWritingSystems);
    }
}
