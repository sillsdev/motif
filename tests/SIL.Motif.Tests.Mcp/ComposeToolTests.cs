using SIL.Motif.Mcp;
using Xunit;

namespace SIL.Motif.Tests.Mcp;

[Trait("MotifTestLevel", "Unit")]
public sealed class ComposeToolTests
{
    private static AgentTool Tool => AgentTools.All.Single(tool => tool.Name == "motif_compose");

    private static string[] Verbs(AgentTool tool, string property) =>
        tool.InputSchema["properties"]![property]!["items"]!["oneOf"]!.AsArray()
            .Select(branch => branch!["properties"]!["verb"]!["enum"]![0]!.GetValue<string>())
            .Order(StringComparer.Ordinal).ToArray();

    [Fact]
    public void TheToolTakesProposalStepsAndRemoveAndIsOffUnlessAProfileSelectsIt()
    {
        Assert.Equal(["project", "proposal", "remove", "steps"], Tool.ArgumentNames.Order(StringComparer.Ordinal));
        Assert.False(Tool.OnByDefault);
        Assert.DoesNotContain(MotifMcpServer.Expose(ToolProfile.Builtin), exposed => exposed.Name == Tool.Name);
        var selected = new ToolProfile("compose", null, null, "concise", [new ToolSelection(Tool.Name, null, null, null)]);
        Assert.Contains(MotifMcpServer.Expose(selected), exposed => exposed.Name == Tool.Name);
        Assert.Equal("array", Tool.InputSchema["properties"]!["remove"]!["type"]!.GetValue<string>());
        Assert.Equal("string", Tool.InputSchema["properties"]!["remove"]!["items"]!["type"]!.GetValue<string>());
    }

    [Fact]
    public void ItsStepsOfferExactlyTheVerbsMotifComposeBatchOffers()
    {
        var batch = AgentTools.All.Single(tool => tool.Name == "motif_compose_batch");

        Assert.Equal(Verbs(batch, "intents"), Verbs(Tool, "steps"));
        var step = Tool.InputSchema["properties"]!["steps"]!["items"]!["oneOf"]![0]!["properties"]!.AsObject();
        Assert.Equal(["as", "cite", "intent", "verb"], step.Select(pair => pair.Key).Order(StringComparer.Ordinal));
    }
}
