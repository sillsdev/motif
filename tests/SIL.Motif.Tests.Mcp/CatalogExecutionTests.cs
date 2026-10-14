using System.Reflection;
using System.Text.Json.Nodes;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Mcp;
using Xunit;

namespace SIL.Motif.Tests.Mcp;

[Trait("MotifTestLevel", "Unit")]
public sealed class CatalogExecutionTests
{
    [Fact]
    public async Task CatalogHandlerArgumentExceptionKeepsItsOriginalMessage()
    {
        var context = new ServerContext("", "1.0", new NoRunnerLauncher(
            new JobRunnerLaunchOptions("root", null)), new ActivityLog(null), ToolProfile.Builtin,
            TextWriter.Null, advancedAiModeEnabled: true);
        var command = CommandCatalog.All.Single(item => item.Name == "setup skip");
        var arguments = new ToolArgs(new JsonObject(), []);
        var invoke = typeof(CatalogTools).GetMethod("Invoke", BindingFlags.Static | BindingFlags.NonPublic)!;

        var task = Assert.IsAssignableFrom<Task<ToolOutcome>>(
            invoke.Invoke(null, [command, context, arguments, CancellationToken.None]));
        var exception = await Assert.ThrowsAsync<ArgumentException>(async () => await task);

        Assert.Contains("ProjectPath", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUnexpectedExceptionInsideAToolIsAToolFailureNotTheCallersFault()
    {
        var context = new ServerContext("unused.fwdata", "1.0", new NoRunnerLauncher(
            new JobRunnerLaunchOptions("root", null)), new ActivityLog(null), ToolProfile.Builtin,
            TextWriter.Null, advancedAiModeEnabled: true);
        var agent = new AgentTool("motif_throwing_catalog_handler", "", AgentClass.Read,
            new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() }, true, true,
            true, false, false, (_, _, _) => Task.FromException<ToolOutcome>(
                new ArgumentException("The handler rejected this value."))) { NeedsProject = false };
        var exposed = new ExposedTool(agent, agent.Name, agent.Description, "detailed");

        var result = await MotifMcpServer.CallAsync(context, [exposed], exposed.Name, null, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("tool.failed", result.StructuredContent!.Value.GetProperty("code").GetString());
        Assert.Equal("Busy", result.StructuredContent.Value.GetProperty("reason").GetString());
        Assert.Contains("The handler rejected this value.",
            result.StructuredContent.Value.GetProperty("message").GetString(), StringComparison.Ordinal);
        var next = result.StructuredContent.Value.GetProperty("next").GetString()!;
        Assert.Contains("Repeat the call once", next, StringComparison.Ordinal);
        Assert.DoesNotContain("damaged", next, StringComparison.OrdinalIgnoreCase);
    }

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
                ToolProfile.Builtin, TextWriter.Null, advancedAiModeEnabled: true);
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
