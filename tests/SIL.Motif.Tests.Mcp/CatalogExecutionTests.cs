using System.Text.Json.Nodes;
using SIL.Motif.Commands;
using SIL.Motif.Mcp;
using Xunit;

namespace SIL.Motif.Tests.Mcp;

public sealed class CatalogExecutionTests
{
    [Fact]
    public async Task AGeneratedJobToolWakesTheWorkerAndReturnsItsJobHandle()
    {
        var root = Path.Combine(Path.GetTempPath(), "motif-catalog-job-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var project = Path.Combine(root, "project.fwdata");
        File.WriteAllText(project, "");
        try
        {
            var runner = new RecordingRunner(root);
            var context = new ServerContext(project, "1.0", runner, new ActivityLog(null),
                ToolProfile.Builtin, TextWriter.Null);
            var tool = AgentTools.All.Single(tool => tool.Name == "motif_baseline_refresh");

            var result = await tool.Run(context, new ToolArgs(new JsonObject(), tool.ArgumentNames), CancellationToken.None);

            Assert.Null(result.Refusal);
            Assert.Equal(project, Assert.Single(runner.Projects));
            Assert.Equal(result.Value!["jobId"]!.GetValue<string>(), result.JobId);
        }
        finally { Directory.Delete(root, true); }
    }

    private sealed class RecordingRunner(string root) : IJobRunnerLauncher
    {
        public JobRunnerLaunchOptions Options { get; } = new(root, null);
        public List<string> Projects { get; } = [];
        public void Start(string projectPath, Action<string>? reportWarning = null) => Projects.Add(projectPath);
    }
}
