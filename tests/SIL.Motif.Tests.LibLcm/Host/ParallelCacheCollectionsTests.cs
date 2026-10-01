using SIL.Motif.Tests.Apply;
using SIL.Motif.Tests.Runner;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Host;

[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group2)]
public sealed class ParallelCacheCollectionsTests
{
    [Fact]
    public void DistinctCacheTestClassesCanUseDifferentCollections()
    {
        static string CollectionOf(Type type) =>
            (string)type.GetCustomAttributesData().Single(attribute =>
                attribute.AttributeType == typeof(CollectionAttribute)).ConstructorArguments[0].Value!;

        Assert.NotEqual(CollectionOf(typeof(ProposalApplierTests)),
            CollectionOf(typeof(GeneratedBasicFieldOperationsTests)));
    }

    [Fact]
    public void CollectionFixturesShareOneSeededMaster()
    {
        using var first = new PristineProjectFixture();
        using var second = new PristineProjectFixture();
        Assert.Equal(first.Seed.FirstSenseId, second.Seed.FirstSenseId);
    }
}
