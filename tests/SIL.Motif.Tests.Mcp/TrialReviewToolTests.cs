using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;
using SIL.Motif.Contract.Review;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Mcp;
using Xunit;

namespace SIL.Motif.Tests.Mcp;

[Trait("MotifTestLevel", "Unit")]
public sealed class TrialReviewToolTests
{
    [Fact]
    public void ToolsListDeclaresOutputSchemaAndReportDetailDefaultsToSummary()
    {
        var exposed = MotifMcpServer.Expose(ToolProfile.Builtin).Single(item => item.Tool.Name == "motif_trial");
        Assert.Equal("summary", exposed.DefaultDetail);
        var schema = exposed.Describe().OutputSchema;
        Assert.NotNull(schema);
        Assert.True(schema.Value.GetProperty("$defs").TryGetProperty("TrialReviewResult", out _));
        Assert.True(schema.Value.TryGetProperty("oneOf", out _));
        Assert.False(exposed.Tool.HasDetail);
    }

    [Fact]
    public void OutputSchemaAcceptsSuccessAndRefusalEnvelopes()
    {
        var schema = JsonSchema.FromText(TrialReviewTools.OutputSchema.ToJsonString());
        var empty = new NoTrialResult("proposal/one", []);
        var success = new JsonObject { ["ok"] = true,
            ["result"] = JsonSerializer.SerializeToNode<TrialReviewResult>(empty, ReviewJson.Options) };
        using var successDocument = JsonDocument.Parse(success.ToJsonString());
        Assert.True(schema.Evaluate(successDocument.RootElement).IsValid);
        var refusal = RefusalMapper.ToResult(new Refusal("tool.invalid-argument", FailureReason.InvalidArgument, "Bad selector."));
        Assert.True(schema.Evaluate(refusal.StructuredContent!.Value).IsValid);
    }

    [Fact]
    public void BadPerToolDetailNamesTheTool()
    {
        var profile = new ToolProfile("test", null, null, "concise", [new ToolSelection("motif_trial", null, null, "detailed")]);
        var exception = Assert.Throws<ProfileException>(() => MotifMcpServer.Expose(profile));
        Assert.Contains("motif_trial", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResumeRejectsNewScopeInputsBeforeStartingRunner()
    {
        var tool = AgentTools.All.Single(item => item.Name == "motif_trial");
        var args = new ToolArgs(new JsonObject { ["proposal"] = "proposal/one", ["job"] = "job/one",
            ["words"] = new JsonArray("word") }, tool.ArgumentNames);
        var outcome = await tool.Run(null!, args, CancellationToken.None);
        Assert.Equal("tool.invalid-argument", outcome.Refusal!.Code);
    }

    [Fact]
    public void RefreshRequiresAnExplicitStoredIdentity()
    {
        var tool = AgentTools.All.Single(item => item.Name == "motif_trial");
        var args = new ToolArgs(new JsonObject { ["proposal"] = "proposal/one" }, tool.ArgumentNames);
        var outcome = TrialReviewTools.Read(null!, "proposal/one", args, refresh: true);
        Assert.Equal("tool.invalid-argument", outcome.Refusal!.Code);
    }

    [Fact]
    public void TrialSchemaDoesNotExposeApply()
    {
        var tool = AgentTools.All.Single(item => item.Name == "motif_trial");
        var schema = tool.InputSchema.ToJsonString();
        Assert.DoesNotContain("apply", schema, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("refresh-report", schema, StringComparison.Ordinal);
    }
}
