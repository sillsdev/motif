using System.Text.Json;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Mcp;
using Xunit;

namespace SIL.Motif.Tests.Mcp;

[Trait("MotifTestLevel", "Unit")]
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
    public void AServerContextMustBeToldWhetherAdvancedAiModeIsOn()
    {
        var parameter = typeof(MotifMcpServer).Assembly.GetType("SIL.Motif.Mcp.ServerContext", throwOnError: true)!
            .GetConstructors().Single().GetParameters().Single(p => p.Name == "advancedAiModeEnabled");

        Assert.False(parameter.HasDefaultValue, "A context built without the user's choice must not serve tools.");
    }

    [Fact]
    public async Task AnUnknownToolIsAnErrorTheModelCanReadNotAProtocolFailure()
    {
        var context = new ServerContext("absent.fwdata", "1.0", new SIL.Motif.Commands.NoRunnerLauncher(
            new SIL.Motif.Commands.JobRunnerLaunchOptions("root", null)), new ActivityLog(null), ToolProfile.Builtin, TextWriter.Null, advancedAiModeEnabled: true);
        var tools = MotifMcpServer.Expose(ToolProfile.Builtin);

        var result = await MotifMcpServer.CallAsync(context, tools, "motif_apply", null, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("tool.unknown", result.StructuredContent!.Value.GetProperty("code").GetString());
    }

    [Fact]
    public async Task AnArgumentTheToolDoesNotHaveIsRefusedWithAHint()
    {
        var context = new ServerContext("absent.fwdata", "1.0", new SIL.Motif.Commands.NoRunnerLauncher(
            new SIL.Motif.Commands.JobRunnerLaunchOptions("root", null)), new ActivityLog(null), ToolProfile.Builtin, TextWriter.Null, advancedAiModeEnabled: true);
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
            new ActivityLog(null), ToolProfile.Builtin, TextWriter.Null, advancedAiModeEnabled: true);
        var tools = MotifMcpServer.Expose(ToolProfile.Builtin);

        var result = await MotifMcpServer.CallAsync(context, tools, "motif_proposals", new Dictionary<string, JsonElement>
            { ["project"] = JsonSerializer.SerializeToElement(context.ProjectPath) }, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("project.unknown", result.StructuredContent!.Value.GetProperty("code").GetString());
        Assert.Contains("Next:", ((ModelContextProtocol.Protocol.TextContentBlock)result.Content[0]).Text);
    }
}

[Trait("MotifTestLevel", "Unit")]
public sealed class RefusalNextStepTests
{
    private static readonly string[] HintedCodes =
    [
        RefusalCodes.BaselineMissing, HostRefusalCodes.BaselineUnavailable, RefusalCodes.ProjectBusy,
        RefusalCodes.ProjectInUse, HostRefusalCodes.ProjectRegistrationFailed, RefusalCodes.ChangeProjectSaving,
        RefusalCodes.ProjectNotFound, RefusalCodes.JobWaitTimeout, RefusalCodes.JobWaitCancelled,
        RefusalCodes.ProposalNotFound, RefusalCodes.ProposalInvalidId, RefusalCodes.DraftRevisionConflict,
        RefusalCodes.JobDryRunIncomplete, RefusalCodes.StoreOtherVersion, RefusalCodes.ParseAlreadyRunning,
        RefusalCodes.AssessParserUnavailable, RefusalCodes.GrammarCheckParserUnavailable,
        RefusalCodes.ProjectWritingSystemMissing, HostRefusalCodes.ToolUnknownArgument,
        HostRefusalCodes.ToolMissingArgument, HostRefusalCodes.ToolFailed,
    ];

    [Fact]
    public void EveryRefusalTheMcpHintedNowCarriesAStructuredNext()
    {
        var help = SIL.Motif.Help.HelpCatalog.Load(System.Globalization.CultureInfo.InvariantCulture);
        var commands = SIL.Motif.Commands.Catalog.CommandCatalog.All.Select(command => command.Name).ToHashSet();
        var defaults = MotifMcpServer.Expose(ToolProfile.Builtin);

        Assert.Equal(21, HintedCodes.Distinct().Count());
        foreach (var code in HintedCodes)
        {
            var spec = RefusalCatalog.Specs[code];
            var next = Assert.IsType<NextStep>(spec.Next);
            Assert.True(next.Command is NextStep.RepeatCall or NextStep.AskPerson || commands.Contains(next.Command),
                code + " names '" + next.Command + "', which is not a catalog command.");
            Assert.False(string.IsNullOrWhiteSpace(help.RefusalWhy(code)), code + " has no why in Help.");
            if (commands.Contains(next.Command))
            {
                var request = SIL.Motif.Commands.Catalog.CommandCatalog.All.Single(c => c.Name == next.Command).RequestType;
                var tool = defaults.Single(t => t.Tool.Requests.Contains(request));
                Assert.All(next.Args.Keys, arg => Assert.Contains(arg, tool.Tool.ArgumentNames));
            }
        }
    }

    [Fact]
    public void ANextStepToAToolHiddenInTheActiveProfileIsAPersonsStep()
    {
        var lean = new ToolProfile("lean", null, null, "concise", [new ToolSelection("motif_overview", null, null, null)]);
        var refusal = new Refusal(RefusalCodes.BaselineMissing, FailureReason.NotFound, "None yet.");

        var hidden = RefusalMapper.NextFor(refusal, MotifMcpServer.Expose(lean));
        var shown = RefusalMapper.NextFor(refusal, MotifMcpServer.Expose(ToolProfile.Builtin));

        Assert.Contains("Ask the person to run `motif baseline capture", hidden);
        Assert.DoesNotContain("motif_capture_baseline", hidden);
        Assert.Contains("`motif_capture_baseline`", shown);
        Assert.DoesNotContain("Ask the person", shown);
    }

    [Fact]
    public void ACallersOwnProjectFlowsIntoTheRenderedNextStep()
    {
        var refusal = new Refusal(RefusalCodes.BaselineMissing, FailureReason.NotFound, "None yet.");
        var caller = new System.Text.Json.Nodes.JsonObject { ["project"] = "indonesian" };

        var text = RefusalMapper.NextFor(refusal, MotifMcpServer.Expose(ToolProfile.Builtin), caller);

        Assert.Contains("project set to indonesian", text);
    }
}
