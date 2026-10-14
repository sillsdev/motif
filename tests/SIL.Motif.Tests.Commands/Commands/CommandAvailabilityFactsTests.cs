using SIL.Motif.Commands.Catalog;
using Xunit;

namespace SIL.Motif.Tests.Commands;

public sealed class CommandAvailabilityFactsTests
{
    [Fact]
    public void EveryCatalogEntryKeepsSurfaceAndAgentClassificationIndependent()
    {
        foreach (var command in CommandCatalog.All)
        {
            var facts = CommandAvailabilityFacts.For(command);
            Assert.Equal(command.Name, facts.Command);
            Assert.Equal(command.Surface, facts.Surface);
            Assert.Equal(command.Agent, facts.Agent);
            Assert.Equal(command.IsAgentTool, facts.AgentEligible);
            if (command.Agent == AgentClass.HumanOnly)
            {
                Assert.False(facts.AgentEligible);
                Assert.Contains("human-only", CommandAvailabilityText.Render(facts));
            }
            if (command.Surface == CommandSurface.Developer)
            {
                Assert.Equal("MOTIF_DEVELOPER_COMMANDS", facts.Setting);
                Assert.Contains("MOTIF_DEVELOPER_COMMANDS=1", facts.Remedy);
                Assert.False(facts.AgentEligible);
            }
            if (command.Surface == CommandSurface.AdvancedAi)
            {
                Assert.Equal("advanced-ai", facts.Setting);
                Assert.Contains("motif settings advanced-ai on", facts.Remedy);
                Assert.Equal("MOTIF_ADVANCED_AI_MODE_PATH", facts.RefusalFacts()["preferencePathEnvironmentVariable"]);
                Assert.Equal("true", facts.RefusalFacts()["reconnectRequired"]);
            }
        }
    }
}
