using System.Text.Json;
using System.Text.Json.Nodes;
using SIL.Motif.Commands;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Host;
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
    private static void RecordBaseline(SIL.Motif.Host.Store.MotifDatabase database, SIL.Motif.Contract.Projects.ProjectLocator project, string fwdata)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Baselines (ProjectKey, ProjectIdentity, SemanticSnapshotDigest, ProjectionVersion, CapturedUtc,
                BundleDigest, RootDirectory, FwDataPath, PublishedUtc, SourceLastWriteUtc)
            VALUES ($project, 'project', $digest, 'test', '2026-01-01T00:00:00Z', $digest, $root, $fwdata,
                '2026-01-01T00:00:00.0000000+00:00', '2026-01-01T00:00:00.0000000+00:00');
            """;
        command.Parameters.AddWithValue("$project", ProjectWorkspaceKey.Compute(project));
        command.Parameters.AddWithValue("$digest", ProposalCompareExports.BaselineDigest('a'));
        command.Parameters.AddWithValue("$root", Path.GetDirectoryName(fwdata)!);
        command.Parameters.AddWithValue("$fwdata", fwdata);
        command.ExecuteNonQuery();
    }

    [Fact]
    public async Task JobsPinTheSameTrialsAsTheCliAndAnUnknownJobRefusesWithTheTrialMissingCode()
    {
        using var fixture = new ReviewAcceptanceFixture();
        using var privateRoot = PrivateManagedRoot.Use(Path.GetDirectoryName(fixture.Project)!);
        KnownProjectTestSetup.AssignUniqueTag(RunnerOptions.ResolveRoot(), fixture.Project, "compare-jobs");
        string[] ids = [];
        string[] jobs = [];
        var staged = ProjectStoreCommand.Run<string>(fixture.Project, MotifProductVersion.CurrentText, (database, project) =>
        {
            ids = [ProposalCompareExports.AddTrial(database, project, [new("f1", [1], [1, 2])]),
                ProposalCompareExports.AddTrial(database, project, [new("f1", [1], [1])])];
            jobs = ids.Select(id => new JobRepository(database)
                .ListByProjectAndKind(ProjectWorkspaceKey.Compute(project), "trial")
                .Single(job => job.InputJson.Contains(id, StringComparison.Ordinal)).JobId).ToArray();
            RecordBaseline(database, project, fixture.Project);
            return CommandOutcome<string>.Success("");
        });
        Assert.True(staged.Succeeded, staged.Refusal?.Message);
        var context = new ServerContext(fixture.Project, "1.0", new NoRunnerLauncher(
            new JobRunnerLaunchOptions(RunnerOptions.ResolveRoot(), null)), new ActivityLog(), ToolProfile.Builtin,
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
        Assert.Equal(plain.Content.ToString(), pinned.Content.ToString());

        var unknown = await Call(new JsonObject { ["jobs"] = new JsonArray("job/nonexistent") });
        Assert.True(unknown.Error);
        Assert.Contains("proposal.compare-trial-missing", unknown.Content.ToString());
    }
}
