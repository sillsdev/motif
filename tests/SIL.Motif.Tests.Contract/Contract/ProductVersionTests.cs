using SIL.Motif.Contract;
using SIL.Motif.Contract.Runner;
using Xunit;

namespace SIL.Motif.Tests.Contract;

public sealed class ProductVersionTests
{
    [Theory]
    [InlineData("0.2.0")]
    [InlineData("0.2.0-beta001")]
    [InlineData("0.2.0-beta002")]
    [InlineData("0.2.0-beta.1+build.001")]
    public void RunnerMetadataPreservesTheFullSemanticVersion(string version)
    {
        var metadata = new RunnerBuildMetadata(version, 1);
        Assert.Equal(version, RunnerBuildMetadata.Parse(metadata.ToCanonicalJson()).ProductVersion);
        Assert.True(ProductVersionSyntax.TryParseCore(version, out var core));
        Assert.Equal(new Version(0, 2, 0), core);
    }

    [Theory]
    [InlineData("0.2.0-01")]
    [InlineData("0.2.0-beta..1")]
    [InlineData("0.2.0-")]
    [InlineData("0.2.0-beta001\n")]
    public void MalformedPrereleaseVersionsAreRefused(string version)
    {
        Assert.False(ProductVersionSyntax.TryParseCore(version, out _));
        Assert.Throws<ArgumentException>(() => new RunnerBuildMetadata(version, 1));
    }
}
