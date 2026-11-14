using SIL.Motif.Host.Config;
using Xunit;

namespace SIL.Motif.Tests.Config;

public sealed class ParsimonyBlockOnConfigurationTests
{
    [Fact]
    public void AFileWithoutTheTableIsAdvisory()
    {
        Assert.Empty(ProjectConfigurationFile.Parse("[regression]\ngate = true\n", "p.motif.toml").ParsimonyBlockOn);
        Assert.Empty(ProjectConfiguration.Defaults().ParsimonyBlockOn);
    }

    [Fact]
    public void TheTableOptsMeasuresInAndRoundTrips()
    {
        var parsed = ProjectConfigurationFile.Parse("[parsimony]\nblock-on = [\"P-variant-same-shape\"]\n", "p.motif.toml");
        Assert.Equal(["P-variant-same-shape"], parsed.ParsimonyBlockOn);
        var again = ProjectConfigurationFile.Parse(ProjectConfigurationFile.Render(parsed), "p.motif.toml");
        Assert.Equal(parsed.ParsimonyBlockOn, again.ParsimonyBlockOn);
    }

    [Fact]
    public void TheFoldRulesAreTheProjectsOwnAndRoundTrip()
    {
        Assert.Empty(ProjectConfiguration.Defaults().ParsimonyFold);
        var parsed = ProjectConfigurationFile.Parse("[parsimony]\nfold = [\"iy>y\", \"ng>n\"]\n", "p.motif.toml");
        Assert.Equal(["iy>y", "ng>n"], parsed.ParsimonyFold);
        Assert.Equal(parsed.ParsimonyFold, ProjectConfigurationFile.Parse(ProjectConfigurationFile.Render(parsed), "p.motif.toml").ParsimonyFold);
        Assert.Throws<ProjectConfigurationException>(() =>
            ProjectConfigurationFile.Parse("[parsimony]\nfold = [\"iy\"]\n", "p.motif.toml"));
    }

    [Theory]
    [InlineData("[parsimony]\nblock-on = [\"P-allo-duplicate-form\"]\n")]
    [InlineData("[parsimony]\nblock-on = P-variant-same-shape\n")]
    [InlineData("[parsimony]\nmode = \"hold\"\n")]
    public void ABadValueOrKeyNamesItsLine(string text)
    {
        var error = Assert.Throws<ProjectConfigurationException>(() => ProjectConfigurationFile.Parse(text, "p.motif.toml"));
        Assert.Contains("line 2", error.Message);
    }
}
