using SIL.Motif.Tests.Apply;
using SIL.Motif.Tests.Runner;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Host;

[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group2)]
public sealed class ParallelCacheCollectionsTests
{
    [Fact]
    public void TestsThatPersistWritingSystemsRunInTheSerializedCollection()
    {
        var repositoryUsers = new[]
        {
            typeof(FootprintPlanAgreementTests),
            typeof(global::SIL.Motif.Tests.Composers.AuthorFeatureStructureEndToEndTests),
            typeof(global::SIL.Motif.Tests.Composers.AuthorFeatureValueEndToEndTests),
            typeof(global::SIL.Motif.Tests.Composers.AuthorLexemeFormEndToEndTests),
            typeof(global::SIL.Motif.Tests.ProjectLoadTests),
            typeof(global::SIL.Motif.Tests.Parser.PanGlossCandidateExportTests),
            typeof(global::SIL.Motif.Tests.Samples.TagalogFeasibilityTests),
        };

        Assert.All(repositoryUsers, type =>
            Assert.Equal(LcmCacheTestCollection.Name, CollectionOf(type)));
    }

    [Fact]
    public void DistinctCacheTestClassesCanUseDifferentCollections()
    {
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

    private static string CollectionOf(Type type) =>
        (string)type.GetCustomAttributesData().Single(attribute =>
            attribute.AttributeType == typeof(CollectionAttribute)).ConstructorArguments[0].Value!;
}
