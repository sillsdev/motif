using SIL.Motif.Commands.Catalog;
using Xunit;

namespace SIL.Motif.Tests.Commands;

public sealed class ProjectTagCommandCatalogTests
{
    [Fact]
    public void ReadingIsAgentReadOnlyMetadataAndSettingIsPersonOnly()
    {
        var read = Assert.Single(CommandCatalog.All, command => command.Name == "project tag");
        var set = Assert.Single(CommandCatalog.All, command => command.Name == "project tag --set");

        Assert.Equal(AgentClass.Read, read.Agent);
        Assert.False(read.IsAgentTool);
        Assert.Equal(AgentClass.HumanOnly, set.Agent);
        Assert.False(set.IsAgentTool);
    }
}
