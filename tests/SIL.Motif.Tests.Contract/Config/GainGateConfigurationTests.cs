using SIL.Motif.Contract.Review;
using SIL.Motif.Host.Config;
using Xunit;

namespace SIL.Motif.Tests.Config;

public sealed class GainGateConfigurationTests
{
    [Fact]
    public void AFileWithoutTheTableGetsTheAdvisoryDefault()
    {
        Assert.Equal(GainGatePolicy.Default, ProjectConfigurationFile.Parse("[regression]\ngate = true\n", "p.motif.toml").GainGate);
        Assert.Equal(GainGatePolicy.Default, ProjectConfiguration.Defaults().GainGate);
    }

    [Fact]
    public void TheTableSetsTheModeAndThresholdAndRoundTrips()
    {
        var parsed = ProjectConfigurationFile.Parse("[gain-gate]\nmode = \"hold\"\nmin-attested-per-object = 2.5\n", "p.motif.toml");
        Assert.Equal(new GainGatePolicy(GainGateMode.Hold, 2.5), parsed.GainGate);
        var again = ProjectConfigurationFile.Parse(ProjectConfigurationFile.Render(parsed), "p.motif.toml");
        Assert.Equal(parsed.GainGate, again.GainGate);
    }

    [Theory]
    [InlineData("[gain-gate]\nmode = \"block\"\n")]
    [InlineData("[gain-gate]\nmode = hold\n")]
    [InlineData("[gain-gate]\nmin-attested-per-object = 0\n")]
    [InlineData("[gain-gate]\nmin-attested-per-object = -1\n")]
    [InlineData("[gain-gate]\nthreshold = 2\n")]
    public void ABadValueOrKeyNamesItsLine(string text)
    {
        var error = Assert.Throws<ProjectConfigurationException>(() => ProjectConfigurationFile.Parse(text, "p.motif.toml"));
        Assert.Contains("line 2", error.Message);
    }
}
