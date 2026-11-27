using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;
using SIL.Motif.Commands;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Projects;
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
    public void TrialReadAdvertisesTheSameRequestAndResultContractsAsTheCli()
    {
        var entry = Assert.Single(SIL.Motif.Commands.Catalog.CommandCatalog.All, entry => entry.Name == "proposal trial");
        Assert.Equal(typeof(ReviewReadRequest), entry.RequestType);
        Assert.Equal(typeof(TrialReviewResult), entry.ResponseType);
        var tool = Assert.Single(AgentTools.All, tool => tool.Name == "motif_trial");
        Assert.Contains(typeof(ReviewReadRequest), tool.Requests);
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

    [Theory]
    [InlineData("motif_trial", "matches")]
    [InlineData("motif_dry_run", "matches")]
    [InlineData("motif_trial", "kind")]
    [InlineData("motif_trial", "proposal")]
    [InlineData("motif_trial", "project")]
    [InlineData("motif_dry_run", "kind")]
    [InlineData("motif_dry_run", "proposal")]
    [InlineData("motif_dry_run", "project")]
    public async Task ResumedWaitRefusesMismatchedJobsAndReportsOnlyTheValidatedJobsCancellation(string tool, string mismatch)
    {
        var root = Path.Combine(Path.GetTempPath(), nameof(TrialReviewToolTests), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "project.fwdata");
        try
        {
            File.WriteAllText(path, "");
            var version = SIL.Motif.Host.MotifProductVersion.CurrentText;
            var runner = new CountingRunner(root);
            var context = new ServerContext(path, version, runner, new ActivityLog(), ToolProfile.Builtin,
                TextWriter.Null, advancedAiModeEnabled: true);
            var proposal = CanonicalId.Mint("proposal/").Value;
            const string jobId = "job/resume";
            var expectedKind = tool == "motif_trial" ? "trial" : "dry-run";
            var kind = mismatch == "kind" ? "baseline-refresh" : expectedKind;
            var ownedProposal = mismatch == "proposal" ? CanonicalId.Mint("proposal/").Value : proposal;
            var proposalJson = JsonSerializer.Serialize(new { proposalId = ownedProposal, contractVersions = new JsonObject(), operations = Array.Empty<object>() });
            var input = kind switch
            {
                "trial" => JsonSerializer.Serialize(new TrialJobInput(proposalJson, null), MotifJson.CreateOptions()),
                "dry-run" => JsonSerializer.Serialize(new DryRunJobInput(DryRunJobInput.CurrentSchemaVersion,
                    FrozenProposalRevision.Create(proposalJson), [], null), MotifJson.CreateOptions()),
                _ => "{}",
            };
            Assert.True(ProjectStoreCommand.Run(path, version, (database, project) =>
            {
                var key = mismatch == "project" ? "a-different-project" : ProjectWorkspaceKey.Compute(project);
                new JobRepository(database).Create(jobId, key, kind, input, JobTimestamp.FormatUtc(DateTimeOffset.UtcNow));
                return CommandOutcome<string>.Success(jobId);
            }).Succeeded);
            var args = new JsonObject { ["proposal"] = proposal, ["job"] = jobId,
                ["wait_terminal"] = true, ["wait_seconds"] = 1 };
            var entry = AgentTools.All.Single(entry => entry.Name == tool);

            var outcome = await entry.Run(context, new ToolArgs(args, entry.ArgumentNames), CancellationToken.None);

            var status = ProjectStoreCommand.ReadExisting(path, CommandOutcome<string>.Success("missing"),
                (database, _) => CommandOutcome<string>.Success(new JobRepository(database).Get(jobId)!.Status.ToString()));
            if (mismatch != "matches")
            {
                Assert.Equal("job.wait-identity-mismatch", outcome.Refusal?.Code);
                Assert.Equal(0, runner.Starts);
                Assert.Equal(JobStatus.Queued.ToString(), status.Value);
                return;
            }

            Assert.Equal(JobStatus.Cancelled.ToString(), status.Value);
            Assert.Equal("job.wait-timeout", outcome.Refusal?.Code);
            var refusal = RefusalMapper.ToResult(outcome.Refusal!);
            Assert.True(refusal.IsError);
            var envelope = refusal.StructuredContent!.Value;
            Assert.Equal("Busy", envelope.GetProperty("reason").GetString());
            var facts = envelope.GetProperty("facts");
            Assert.Equal(jobId, facts.GetProperty("jobId").GetString());
            Assert.Equal("cancelled", facts.GetProperty("status").GetString());
            Assert.Equal("true", facts.GetProperty("terminal").GetString());
            Assert.Equal("complete", facts.GetProperty("cleanup").GetString());
            var next = envelope.GetProperty("next").GetString()!;
            Assert.Contains("Start a new job", next);
            Assert.DoesNotContain("call again", next, StringComparison.OrdinalIgnoreCase);
            Assert.EndsWith("Next: " + next,
                Assert.IsType<ModelContextProtocol.Protocol.TextContentBlock>(Assert.Single(refusal.Content)).Text);
            Assert.True(JsonSchema.FromText(TrialReviewTools.OutputSchema.ToJsonString()).Evaluate(envelope).IsValid);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private sealed class CountingRunner(string root) : IJobRunnerLauncher
    {
        public JobRunnerLaunchOptions Options { get; } = new(root, null);
        public int Starts { get; private set; }
        public void Start(string projectPath, Action<string>? reportWarning = null) => Starts++;
    }

    [Fact]
    public void RefreshRequiresAnExplicitStoredIdentity()
    {
        var tool = AgentTools.All.Single(item => item.Name == "motif_trial");
        var args = new ToolArgs(new JsonObject { ["proposal"] = "proposal/one" }, tool.ArgumentNames);
        var outcome = TrialReviewTools.Refresh(null!, "proposal/one", args);
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
