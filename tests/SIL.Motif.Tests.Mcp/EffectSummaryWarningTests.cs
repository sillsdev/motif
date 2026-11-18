using System.Text.Json;
using System.Text.Json.Nodes;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Mcp;
using SIL.Motif.Projection.Rendering;
using Xunit;

namespace SIL.Motif.Tests.Mcp;

[Trait("MotifTestLevel", "Unit")]
public sealed class EffectSummaryWarningTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ThousandsOfLongWarningsStayBoundedInDefaultTextAndMcp(bool mcp)
    {
        var warnings = Enumerable.Range(0, 2000).Select(index => $"warning {index}: " + new string('w', 1000)).ToArray();
        var effects = warnings.Select(warning => new EffectView("target", "dryRunWarning", [],
            JsonSerializer.SerializeToElement(new { warning }))).ToArray();
        var projection = new DryRunProjection("proposal", "intent", "Baseline", effects, "digest", "footprint")
        {
            Warnings = warnings,
            Operations = Enumerable.Range(0, 2000).Select(index =>
                new ProposalOperationView("operation/" + index, "kind", null, null, [], null)).ToArray(),
        };
        var value = ProjectionJson.ToNode(projection)!.AsObject();
        Assert.Equal(2000, value["effects"]!.AsArray().Count);
        Assert.Equal(2000, value["operations"]!.AsArray().Count);
        Assert.Equal(warnings[^1], value["effects"]![1999]!["preview"]!["warning"]!.GetValue<string>());
        value.Remove("effects");
        value.Remove("operations");
        var context = new ServerContext("unused.fwdata", "1.0", new NoRunnerLauncher(
            new JobRunnerLaunchOptions("root", null)), new ActivityLog(null), ToolProfile.Builtin,
            TextWriter.Null, advancedAiModeEnabled: true);
        var tool = new AgentTool("motif_dry_run", "", AgentClass.Read,
            new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() }, true, true,
            true, false, (_, _, _) => Task.FromResult(ToolOutcome.Ok(value))) { NeedsProject = false };
        var exposed = new ExposedTool(tool, tool.Name, tool.Description, "concise");
        var response = await MotifMcpServer.CallAsync(context, [exposed], exposed.Name, null, CancellationToken.None);
        Assert.NotEqual(true, response.IsError);
        var output = mcp ? response.StructuredContent!.Value.GetRawText() : CommandTextRenderer.Render(projection);

        Assert.InRange(output.Length, 1, 6000);
        Assert.DoesNotContain(new string('w', 241), output);
        Assert.DoesNotContain("warning 1999:", output);
        if (mcp)
        {
            var summary = response.StructuredContent!.Value.GetProperty("result").GetProperty("effectsSummary");
            Assert.Equal(2000, summary.GetProperty("warningCount").GetInt32());
            Assert.Equal(1990, summary.GetProperty("warningsOmitted").GetInt32());
            Assert.Equal(10, summary.GetProperty("warnings").GetArrayLength());
        }
        else
            Assert.Contains("warnings: 2000 (1990 omitted)", output);
    }
}
