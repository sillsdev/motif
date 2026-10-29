using SIL.Motif.Commands;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Mcp;
using Xunit;

namespace SIL.Motif.Tests.Mcp;

[Trait("MotifTestLevel", "Unit")]
public sealed class ComposeToolTests
{
    [Fact]
    public void ComposerHintsNameTheOperationCountWithoutRepeatingAnExistingCount()
    {
        var result = CommandOutcome<ComposedOperationsResponse>.Success(new("draft", "composer", [], 3));

        Assert.Equal("Draft now has 3 operations.", ToolOutcome.From(result).Next);
        Assert.Equal("Draft now has 3 operations. Try it.", ToolOutcome.From(result, _ => "Try it.").Next);
        Assert.Equal("Draft now has 3 changes. Try it.",
            ToolOutcome.From(result, _ => "Draft now has 3 changes. Try it.").Next);
    }

    private static AgentTool Tool => AgentTools.All.Single(tool => tool.Name == "motif_compose");

    private static string[] Verbs(AgentTool tool, string property) =>
        tool.InputSchema["properties"]![property]!["items"]!["oneOf"]!.AsArray()
            .Select(branch => branch!["properties"]!["verb"]!["enum"]![0]!.GetValue<string>())
            .Order(StringComparer.Ordinal).ToArray();

    [Fact]
    public void TheToolTakesProposalStepsAndRemoveAndIsOffUnlessAProfileSelectsIt()
    {
        Assert.Equal(["project", "proposal", "remove", "steps"], Tool.ArgumentNames.Order(StringComparer.Ordinal));
        Assert.DoesNotContain(MotifMcpServer.Expose(ToolProfile.Builtin), exposed => exposed.Name == Tool.Name);
        var selected = new ToolProfile("compose", null, null, "concise", [new ToolSelection(Tool.Name, null, null, null)]);
        Assert.Contains(MotifMcpServer.Expose(selected), exposed => exposed.Name == Tool.Name);
        Assert.Equal("array", Tool.InputSchema["properties"]!["remove"]!["type"]!.GetValue<string>());
        Assert.Equal("string", Tool.InputSchema["properties"]!["remove"]!["items"]!["type"]!.GetValue<string>());
    }

    [Fact]
    public void ItsStepVerbsAreEveryComposerAnAgentIsOfferedAndMotifComposeBatchOffersThoseButThreeNamedOnes()
    {
        var batch = AgentTools.All.Single(tool => tool.Name == "motif_compose_batch");
        var offeredBySteps = Verbs(Tool, "steps");
        var offeredByBatch = Verbs(batch, "intents");
        var cliFirst = ProposalCommands.ComposerDescriptors.Where(composer => !composer.IsAgentTool)
            .Select(composer => composer.Verb).ToHashSet(StringComparer.Ordinal);
        var registry = ProposalCommands.ComposerVerbs.Where(verb => !cliFirst.Contains(verb))
            .Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(registry, offeredBySteps);
        string[] batchless = ["retire-allomorph", "retire-redundant-zero-affix", "compose-record-parsimony-disposition"];
        Assert.Equal(registry.Except(batchless), offeredByBatch);
        var step = Tool.InputSchema["properties"]!["steps"]!["items"]!["oneOf"]![0]!["properties"]!.AsObject();
        Assert.Equal(["as", "cite", "intent", "verb"], step.Select(pair => pair.Key).Order(StringComparer.Ordinal));
    }
}
