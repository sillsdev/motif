using System.Text.Json;
using System.Text.Json.Nodes;
using SIL.Motif.Commands;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Mcp;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Projects;
using Xunit;

namespace SIL.Motif.Tests.Mcp;

[Collection(LcmCacheParallelCollections.Group3)]
public sealed class ProposalCompareJobsTests
{
    [Fact]
    public async Task JobsPinTheSameTrialsAsTheCliAndAnUnknownJobRefusesWithTheTrialMissingCode()
    {
        using var fixture = new ReviewAcceptanceFixture();
        string[] ids = [];
        string[] jobs = [];
        var staged = ProjectStoreCommand.Run<string>(fixture.Project, MotifProductVersion.CurrentText, (database, project) =>
        {
            ids = [ProposalCompareExports.AddTrial(database, project, [new("f1", [1], [1, 2])]),
                ProposalCompareExports.AddTrial(database, project, [new("f1", [1], [1])])];
            jobs = ids.Select(id => new JobRepository(database)
                .ListByProjectAndKind(ProjectWorkspaceKey.Compute(project), "trial")
                .Single(job => job.InputJson.Contains(id, StringComparison.Ordinal)).JobId).ToArray();
            return CommandOutcome<string>.Success("");
        });
        Assert.True(staged.Succeeded, staged.Refusal?.Message);
        var context = new ServerContext(fixture.Project, "1.0", new NoRunnerLauncher(
            new JobRunnerLaunchOptions(RunnerOptions.ResolveRoot(), null)), new ActivityLog(null), ToolProfile.Builtin,
            TextWriter.Null, advancedAiModeEnabled: true);
        var tools = MotifMcpServer.Expose(ToolProfile.Builtin);
        async Task<(bool Error, JsonElement Content)> Call(JsonObject arguments)
        {
            arguments["project"] = fixture.Project;
            arguments["proposalIds"] = new JsonArray(ids.Select(id => (JsonNode)id).ToArray());
            var result = await MotifMcpServer.CallAsync(context, tools, "motif_proposal_compare",
                arguments.ToDictionary(pair => pair.Key, pair => JsonSerializer.SerializeToElement(pair.Value)), CancellationToken.None);
            return (result.IsError == true, result.StructuredContent!.Value);
        }

        var plain = await Call(new JsonObject());
        var pinned = await Call(new JsonObject { ["jobs"] = new JsonArray(jobs.Select(job => (JsonNode)job).ToArray()) });
        Assert.False(plain.Error, plain.Content.ToString());
        Assert.False(pinned.Error, pinned.Content.ToString());
        Assert.Equal(plain.Content.GetProperty("snapshot").GetString(), pinned.Content.GetProperty("snapshot").GetString());

        var unknown = await Call(new JsonObject { ["jobs"] = new JsonArray("job/nonexistent") });
        Assert.True(unknown.Error);
        Assert.Equal("proposal.compare-trial-missing", unknown.Content.GetProperty("code").GetString());
    }
}
