using SIL.Motif.Host.Config;
using Xunit;

namespace SIL.Motif.Tests.Config;

public sealed class DeclaredGateConfigurationTests
{
    private const string Sets = "[[review-word-set]]\nid = \"gold\"\nname = \"Gold\"\nrole = \"regression\"\nselection = \"Gold words\"\n";

    [Fact]
    public void DeclaredGateRoundTripsAlongsideExistingGainAndParsimonyPolicies()
    {
        var text = Sets + "[gate]\nword-sets = [\"gold\"]\n[gain-gate]\nmode = \"hold\"\nmin-attested-per-object = 2.5\n" +
            "[parsimony]\nblock-on = [\"R-word-negative-accepted\"]\n";
        var configuration = ProjectConfigurationFile.Parse(text, "project.motif.toml");
        var rendered = ProjectConfigurationFile.Render(configuration);
        Assert.Contains("[gate]", rendered);
        Assert.Contains("word-sets = [\"gold\"]", rendered);
        Assert.Contains("min-attested-per-object = 2.5", rendered);
        Assert.Contains("block-on = [\"R-word-negative-accepted\"]", rendered);
        Assert.Equal(rendered, ProjectConfigurationFile.Render(ProjectConfigurationFile.Parse(rendered, "project.motif.toml")));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("[\"missing\"]")]
    [InlineData("[\"gold\", \"gold\"]")]
    public void DeclaredGateRequiresDistinctExistingWordSets(string value) =>
        Assert.Throws<ProjectConfigurationException>(() => ProjectConfigurationFile.Parse(
            Sets + "[gate]\nword-sets = " + value, "project.motif.toml"));

    [Fact]
    public void EmptyGateTableDoesNotSilentlyGrantAuthority() =>
        Assert.Throws<ProjectConfigurationException>(() => ProjectConfigurationFile.Parse(Sets + "[gate]", "project.motif.toml"));
}
