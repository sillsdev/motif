using SIL.Motif.Commands;
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

    // Composers a person reaches through the CLI only, which no agent batch or step may name.
    private static readonly string[] CliOnlyComposers =
        ["retire-allomorph", "retire-redundant-zero-affix", "compose-record-parsimony-disposition"];

    [Fact]
    public void ItsStepVerbsAreTheRegistrysAgentBatchableVerbsAndMotifComposeBatchOffersTheSame()
    {
        var batch = AgentTools.All.Single(tool => tool.Name == "motif_compose_batch");
        var offeredBySteps = Verbs(Tool, "steps");
        var offeredByBatch = Verbs(batch, "intents");
        var excluded = ProposalCommands.ComposerDescriptors
            .Where(composer => !composer.IsAgentTool || !composer.InBatch).Select(composer => composer.Verb)
            .Concat(CliOnlyComposers).ToHashSet(StringComparer.Ordinal);
        Assert.All(CliOnlyComposers, verb => Assert.Contains(verb, ProposalCommands.ComposerVerbs));
        var registry = ProposalCommands.ComposerVerbs.Where(verb => !excluded.Contains(verb))
            .Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(registry, offeredBySteps);
        Assert.Equal(offeredByBatch, offeredBySteps);
        var step = Tool.InputSchema["properties"]!["steps"]!["items"]!["oneOf"]![0]!["properties"]!.AsObject();
        Assert.Equal(["as", "cite", "intent", "verb"], step.Select(pair => pair.Key).Order(StringComparer.Ordinal));
    }
}
