using System.Text.Json;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Mcp;
using Xunit;

namespace SIL.Motif.Tests.Mcp;

public sealed class RefusalMappingTests
{
    [Theory]
    [InlineData(FailureReason.InvalidArgument)]
    [InlineData(FailureReason.NotFound)]
    [InlineData(FailureReason.Refused)]
    [InlineData(FailureReason.Cancelled)]
    [InlineData(FailureReason.Busy)]
    [InlineData(FailureReason.StoreInconsistent)]
    public void EveryReasonBecomesAToolErrorWithItsCodeAndANextStep(FailureReason reason)
    {
        var refusal = new Refusal("some.unlisted-code", reason, "It did not work.",
            new Dictionary<string, string> { ["thing"] = "x" });

        var result = RefusalMapper.ToResult(refusal);

        Assert.True(result.IsError);
        var text = Assert.IsType<ModelContextProtocol.Protocol.TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.StartsWith("some.unlisted-code: It did not work.", text);
        Assert.Contains("thing: x", text);
        Assert.Contains("\nNext: ", text);
        var structured = result.StructuredContent!.Value;
        Assert.False(structured.GetProperty("ok").GetBoolean());
        Assert.Equal("some.unlisted-code", structured.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(structured.GetProperty("next").GetString()));
    }

    [Fact]
    public void AWaitThatTimedOutNamesTheJobInItsNextStep()
    {
        var refusal = new Refusal("job.wait-timeout", FailureReason.Busy, "Timed out.",
            new Dictionary<string, string> { ["jobId"] = "job/abc" });

        Assert.Contains("job set to job/abc", RefusalMapper.NextFor(refusal));
    }

    [Fact]
    public void AMissingBaselineSaysHowToTakeOne()
    {
        var refusal = new Refusal("baseline.missing", FailureReason.NotFound, "None yet.");

        Assert.Contains("motif_capture_baseline", RefusalMapper.NextFor(refusal));
    }

    [Fact]
    public async Task AnUnknownToolIsAnErrorTheModelCanReadNotAProtocolFailure()
    {
        var context = new ServerContext("absent.fwdata", "1.0", new SIL.Motif.Commands.NoRunnerLauncher(
            new SIL.Motif.Commands.JobRunnerLaunchOptions("root", null)), new ActivityLog(null), ToolProfile.Builtin, TextWriter.Null);
        var tools = MotifMcpServer.Expose(ToolProfile.Builtin);

        var result = await MotifMcpServer.CallAsync(context, tools, "motif_apply", null, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("tool.unknown", result.StructuredContent!.Value.GetProperty("code").GetString());
    }

    [Fact]
    public async Task AnArgumentTheToolDoesNotHaveIsRefusedWithAHint()
    {
        var context = new ServerContext("absent.fwdata", "1.0", new SIL.Motif.Commands.NoRunnerLauncher(
            new SIL.Motif.Commands.JobRunnerLaunchOptions("root", null)), new ActivityLog(null), ToolProfile.Builtin, TextWriter.Null);
        var tools = MotifMcpServer.Expose(ToolProfile.Builtin);
        var arguments = new Dictionary<string, JsonElement> { ["force"] = JsonSerializer.SerializeToElement(true) };

        var result = await MotifMcpServer.CallAsync(context, tools, "motif_overview", arguments, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("tool.unknown-argument", result.StructuredContent!.Value.GetProperty("code").GetString());
    }

    [Fact]
    public async Task AnUnknownProjectIsRefusedBeforeOpeningIt()
    {
        var context = new ServerContext(Path.Combine(Path.GetTempPath(), "no-such-motif-project.fwdata"), "1.0",
            new SIL.Motif.Commands.NoRunnerLauncher(new SIL.Motif.Commands.JobRunnerLaunchOptions("root", null)),
            new ActivityLog(null), ToolProfile.Builtin, TextWriter.Null);
        var tools = MotifMcpServer.Expose(ToolProfile.Builtin);

        var result = await MotifMcpServer.CallAsync(context, tools, "motif_proposals", new Dictionary<string, JsonElement>
            { ["project"] = JsonSerializer.SerializeToElement(context.ProjectPath) }, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("project.unknown", result.StructuredContent!.Value.GetProperty("code").GetString());
        Assert.Contains("Next:", ((ModelContextProtocol.Protocol.TextContentBlock)result.Content[0]).Text);
    }
}
