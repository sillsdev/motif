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

        Assert.Contains("job set to job/abc", RefusalMapper.NextFor(refusal, MotifMcpServer.Expose(ToolProfile.Load("full"))));
    }

    [Theory]
    [InlineData("job.wait-timeout", FailureReason.Busy, "true", "cancelled", "complete", "start a new job")]
    [InlineData("job.wait-cancelled", FailureReason.Cancelled, "true", "cancelled", "incomplete", "check its cleanup")]
    [InlineData("job.wait-timeout", FailureReason.Busy, "false", "running", "incomplete", "check its status")]
    public void TerminalWaitRefusalExplainsTheObservedOutcomeInBothEnvelopeForms(string code,
        FailureReason reason, string terminal, string status, string cleanup, string advice)
    {
        var facts = new Dictionary<string, string>
        {
            ["jobId"] = "job/exact", ["terminal"] = terminal, ["status"] = status, ["cleanup"] = cleanup,
        };
        var result = RefusalMapper.ToResult(new Refusal(code, reason, "Wait ended.", facts));

        Assert.True(result.IsError);
        var body = result.StructuredContent!.Value;
        Assert.False(body.GetProperty("ok").GetBoolean());
        Assert.Equal(code, body.GetProperty("code").GetString());
        Assert.Equal(reason.ToString(), body.GetProperty("reason").GetString());
        Assert.Equal("Wait ended.", body.GetProperty("message").GetString());
        foreach (var (key, value) in facts) Assert.Equal(value, body.GetProperty("facts").GetProperty(key).GetString());
        var next = body.GetProperty("next").GetString()!;
        Assert.Contains(advice, next, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("repeat this call", next, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("call again", next, StringComparison.OrdinalIgnoreCase);
        var text = Assert.IsType<ModelContextProtocol.Protocol.TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.EndsWith("Next: " + next, text);
        Assert.True(Json.Schema.JsonSchema.FromText(TrialReviewTools.OutputSchema.ToJsonString()).Evaluate(body).IsValid);
    }

    [Fact]
    public void AMissingBaselineSaysHowToTakeOne()
    {
        var refusal = new Refusal("baseline.missing", FailureReason.NotFound, "None yet.");

        Assert.Contains("motif_capture_baseline", RefusalMapper.NextFor(refusal, MotifMcpServer.Expose(ToolProfile.Load("full"))));
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
            new SIL.Motif.Commands.JobRunnerLaunchOptions("root", null)), new ActivityLog(), ToolProfile.Load("full"), TextWriter.Null, advancedAiModeEnabled: true);
        var tools = MotifMcpServer.Expose(ToolProfile.Load("full"));

        var result = await MotifMcpServer.CallAsync(context, tools, "motif_apply", null, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("tool.unknown", result.StructuredContent!.Value.GetProperty("code").GetString());
    }

    [Fact]
    public async Task AnArgumentTheToolDoesNotHaveIsRefusedWithAHint()
    {
        var context = new ServerContext("absent.fwdata", "1.0", new SIL.Motif.Commands.NoRunnerLauncher(
            new SIL.Motif.Commands.JobRunnerLaunchOptions("root", null)), new ActivityLog(), ToolProfile.Load("full"), TextWriter.Null, advancedAiModeEnabled: true);
        var tools = MotifMcpServer.Expose(ToolProfile.Load("full"));
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
            new ActivityLog(), ToolProfile.Load("full"), TextWriter.Null, advancedAiModeEnabled: true);
        var tools = MotifMcpServer.Expose(ToolProfile.Load("full"));

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
        RefusalCodes.AssessParserMemoryLimit, RefusalCodes.AssessParserUnavailable,
        RefusalCodes.GrammarCheckParserUnavailable,
        RefusalCodes.ProjectWritingSystemMissing, HostRefusalCodes.ToolUnknownArgument,
        HostRefusalCodes.ToolMissingArgument, HostRefusalCodes.ToolFailed,
    ];

    [Fact]
    public void EveryRefusalTheMcpHintedNowCarriesAStructuredNext()
    {
        var help = SIL.Motif.Help.HelpCatalog.Load(System.Globalization.CultureInfo.InvariantCulture);
        var commands = SIL.Motif.Commands.Catalog.CommandCatalog.All.Select(command => command.Name).ToHashSet();
        var defaults = MotifMcpServer.Expose(ToolProfile.Load("full"));

        Assert.Equal(22, HintedCodes.Distinct().Count());
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
    public void UnderTheDefaultProfileAHiddenNextToolNamesTheCliVerbAndTheFullProfile()
    {
        var refusal = new Refusal(RefusalCodes.BaselineMissing, FailureReason.NotFound, "None yet.");

        var text = RefusalMapper.NextFor(refusal, MotifMcpServer.Expose(ToolProfile.Load("default")));

        Assert.Contains("Run `motif baseline capture", text);
        Assert.Contains("restart with `motif mcp --profile full` to call `motif_capture_baseline`", text);
        Assert.DoesNotContain("Ask the person", text);
    }

    [Fact]
    public void ANextStepToAToolHiddenInTheActiveProfileNamesTheCliVerbAndTheFullProfile()
    {
        var lean = new ToolProfile("lean", null, null, "concise", [new ToolSelection("motif_overview", null, null, null)]);
        var refusal = new Refusal(RefusalCodes.BaselineMissing, FailureReason.NotFound, "None yet.");

        var hidden = RefusalMapper.NextFor(refusal, MotifMcpServer.Expose(lean));
        var shown = RefusalMapper.NextFor(refusal, MotifMcpServer.Expose(ToolProfile.Load("full")));

        Assert.Contains("Run `motif baseline capture", hidden);
        Assert.Contains("--profile full", hidden);
        Assert.DoesNotContain("Ask the person", hidden);
        Assert.DoesNotContain("Call `motif_capture_baseline`", hidden);
        Assert.Contains("`motif_capture_baseline`", shown);
        Assert.DoesNotContain("Ask the person", shown);
    }

    [Fact]
    public void ACallersOwnProjectFlowsIntoTheRenderedNextStep()
    {
        var refusal = new Refusal(RefusalCodes.BaselineMissing, FailureReason.NotFound, "None yet.");
        var caller = new System.Text.Json.Nodes.JsonObject { ["project"] = "indonesian" };

        var text = RefusalMapper.NextFor(refusal, MotifMcpServer.Expose(ToolProfile.Load("full")), caller);

        Assert.Contains("project set to indonesian", text);
    }
}
