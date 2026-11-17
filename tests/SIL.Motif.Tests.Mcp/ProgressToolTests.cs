using System.Text.Json;
using System.Text.Json.Nodes;
using System.Runtime.CompilerServices;
using Json.Schema;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Mcp;
using Xunit;

namespace SIL.Motif.Tests.Mcp;

[Trait("MotifTestLevel", "Unit")]
public sealed class ProgressToolTests
{
    [Fact]
    public void DefaultProfileExposesReadOnlyProgressWithClosedSchemas()
    {
        var exposed = MotifMcpServer.Expose(ToolProfile.Builtin).Single(item => item.Tool.Name == "motif_progress");
        Assert.True(exposed.Tool.ReadOnly);
        Assert.True(exposed.Tool.Idempotent);
        Assert.Equal("summary", exposed.DefaultDetail);
        Assert.False(exposed.Tool.HasDetail);
        Assert.NotNull(exposed.Describe().OutputSchema);
        Assert.False(exposed.Tool.InputSchema["additionalProperties"]!.GetValue<bool>());
        Assert.False(exposed.Tool.InputSchema["properties"]!["scope"]!["additionalProperties"]!.GetValue<bool>());
        var texts = exposed.Tool.InputSchema["properties"]!["scope"]!["properties"]!["texts"]!;
        Assert.Equal(200, texts["maxItems"]!.GetValue<int>());
        Assert.Equal("uuid", texts["items"]!["format"]!.GetValue<string>());
    }

    [Fact]
    public void OutputSchemaAcceptsSuccessAndRefusal()
    {
        var bucket = new ProgressBuckets(1, 0, 0, 1, new Dictionary<string, int> { ["parser-evidence-unavailable"] = 1 });
        var response = new ProgressResponse("progress/a", "sha256:" + new string('a', 64),
            new ProgressCoverage("fieldworks-segments-v1", "ws-valid-characters-v1", "least-complete-place-v1",
                false, 0, new Dictionary<string, int>()), new ProgressFreshness("content/a", "judgement/a", true),
            new ProgressCounts(1, bucket, bucket), [], null,
            ["checked-unavailable", "changed-since-unavailable", "parser-evidence-unavailable"], new ProgressChangedSince(null));
        var success = new JsonObject { ["ok"] = true, ["result"] = JsonNode.Parse(ProjectionJson.Serialize(response)) };
        var validator = JsonSchema.FromText(ProgressToolSchema.Output.ToJsonString());
        Assert.True(validator.Evaluate(JsonSerializer.SerializeToElement(success)).IsValid);
        var source = new SourceProgressResponse("WEB", "generation", 5, 2,
            [new SourceProgressPassage("MAT 1", "MAT 1", 5, 2)], null);
        var sourceSuccess = new JsonObject { ["ok"] = true, ["result"] = JsonNode.Parse(ProjectionJson.Serialize(source)) };
        Assert.True(validator.Evaluate(JsonSerializer.SerializeToElement(sourceSuccess)).IsValid);
        var refusal = RefusalMapper.ToResult(new Refusal("progress.index-not-ready", FailureReason.Refused, "Refresh."));
        Assert.True(validator.Evaluate(refusal.StructuredContent!.Value).IsValid);
    }

    [Fact]
    public void DefaultProfileOutputSchemaMatchesGolden()
    {
        var tool = MotifMcpServer.Expose(ToolProfile.Load("default"))
            .Single(item => item.Tool.Name == "motif_progress").Describe();
        var actual = JsonNode.Parse(tool.OutputSchema!.Value.GetRawText())!
            .ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n";
        var path = OutputGoldenPath();
        if (Environment.GetEnvironmentVariable("MOTIF_UPDATE_GOLDEN") == "1") File.WriteAllText(path, actual);
        Assert.Equal(File.ReadAllText(path).ReplaceLineEndings("\n"), actual.ReplaceLineEndings("\n"));
    }

    private static string OutputGoldenPath([CallerFilePath] string source = "") =>
        Path.Combine(Path.GetDirectoryName(source)!, "Golden", "progress-output.default.json");
}
