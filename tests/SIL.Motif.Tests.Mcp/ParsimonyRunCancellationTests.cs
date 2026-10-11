using System.Text.Json.Nodes;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Host.Parsimony;
using SIL.Motif.Host.Store;
using SIL.Motif.Mcp;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Mcp;

public sealed class ParsimonyRunCancellationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), nameof(ParsimonyRunCancellationTests),
        Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("P-adhoc-duplicate")]
    [InlineData(ParsimonyAdmission.AllMeasures)]
    public async Task CancellingTheCallCancelsTheJobItStartedInsteadOfWaitingOutTheTimeout(string measureId)
    {
        var fwDataPath = CreateProject();
        var tool = ParsimonyTools.All.Single(item => item.Name == "motif_run_parsimony_measure");
        var context = new ServerContext(fwDataPath, "1.0",
            new NoRunnerLauncher(new JobRunnerLaunchOptions(_root, null)), new ActivityLog(null),
            ToolProfile.Builtin, TextWriter.Null);
        var args = new ToolArgs(new JsonObject { ["measure_id"] = measureId, ["wait_seconds"] = 300 },
            tool.ArgumentNames);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var started = System.Diagnostics.Stopwatch.StartNew();
        var outcome = await tool.Run(context, args, cancellation.Token);

        Assert.True(started.Elapsed < TimeSpan.FromSeconds(30), "the call took " + started.Elapsed);
        Assert.Equal("job.wait-cancelled", outcome.Refusal?.Code);
        var jobId = outcome.Refusal!.Facts!["jobId"];
        var job = JobCommands.Show(new ShowJobRequest(fwDataPath, jobId, "1.0"));
        Assert.Equal(JobStatus.Cancelled, job.Value!.Status);
    }

    [Fact]
    public void TheRunToolOffersEveryMeasureAndAllOfThem()
    {
        var tool = ParsimonyTools.All.Single(item => item.Name == "motif_run_parsimony_measure");

        var offered = ((JsonObject)tool.InputSchema["properties"]!)["measure_id"]!["enum"]!.AsArray()
            .Select(item => item!.GetValue<string>()).ToArray();

        Assert.Contains(ParsimonyAdmission.AllMeasures, offered);
        Assert.Equal(ParsimonyAdmission.SupportedMeasureIds().Append(ParsimonyAdmission.AllMeasures), offered);
    }

    private string CreateProject()
    {
        Directory.CreateDirectory(_root);
        var fwDataPath = Path.Combine(_root, "project.fwdata");
        File.WriteAllText(fwDataPath, "project marker");
        var project = new ProjectLocator(fwDataPath, "project");
        using (MotifDatabase.OpenOwned(ProjectDatabaseCatalog.DatabasePathFor(project), project,
                   MotifSchema.CurrentSchema, new Version(1, 0))) { }
        return fwDataPath;
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }
}
