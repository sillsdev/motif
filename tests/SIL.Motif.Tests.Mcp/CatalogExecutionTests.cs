using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Store;
using SIL.Motif.Mcp;
using SIL.Motif.Worker;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;
using Xunit;
using Xunit.Abstractions;

namespace SIL.Motif.Tests.Mcp;

[Trait("MotifTestLevel", "Unit")]
public sealed class CatalogExecutionTests
{
    private readonly ITestOutputHelper _output;

    public CatalogExecutionTests(ITestOutputHelper output) => _output = output;

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
            false, false, (_, _, _) => Task.FromException<ToolOutcome>(
                new ArgumentException("The handler rejected this value."))) { NeedsProject = false };
        var exposed = new ExposedTool(agent, agent.Name, agent.Description, "detailed");

        var result = await MotifMcpServer.CallAsync(context, [exposed], exposed.Name, null, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("tool.failed", result.StructuredContent!.Value.GetProperty("code").GetString());
        Assert.Equal("Busy", result.StructuredContent.Value.GetProperty("reason").GetString());
        Assert.Contains("The handler rejected this value.",
            result.StructuredContent.Value.GetProperty("message").GetString(), StringComparison.Ordinal);
        var next = result.StructuredContent.Value.GetProperty("next").GetString()!;
        Assert.Contains("Repeat this call", next, StringComparison.Ordinal);
        Assert.DoesNotContain("damaged", next, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CatalogJobResponsesUseProjectionJsonEnumAndNullConventions()
    {
        var root = Path.Combine(Path.GetTempPath(), "motif-catalog-json-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var projectPath = Path.Combine(root, "project.fwdata");
        File.WriteAllText(projectPath, "");
        try
        {
            var locator = new ProjectLocator(projectPath, "project");
            var workspaceKey = ProjectWorkspaceKey.Compute(locator);
            var catalog = new ProjectDatabaseCatalog(MotifSchema.CurrentSchema, new Version(1, 0));
            using (var database = catalog.OpenOwned(locator))
                new JobRepository(database).Create("job-1", workspaceKey, "baseline-refresh", "{}",
                    JobTimestamp.FormatUtc(DateTimeOffset.UtcNow));
            using (var machine = MachineDatabase.Open(RunnerOptions.ResolveRoot()))
                new KnownProjectRegistry(machine).Record(workspaceKey, projectPath, DateTimeOffset.UtcNow);

            var expected = JobCommands.Show(new ShowJobRequest(projectPath, "job-1", "1.0"));
            Assert.True(expected.Succeeded);
            var expectedJson = JsonNode.Parse(ProjectionJson.Serialize(expected.Value!));
            var context = new ServerContext(projectPath, "1.0", new NoRunnerLauncher(
                new JobRunnerLaunchOptions(RunnerOptions.ResolveRoot(), null)), new ActivityLog(null),
                ToolProfile.Builtin, TextWriter.Null, advancedAiModeEnabled: true);
            var arguments = new JsonObject { ["jobId"] = "job-1" };
            var command = CommandCatalog.All.Single(item => item.Name == "jobs show");
            var invoke = typeof(CatalogTools).GetMethod("Invoke", BindingFlags.Static | BindingFlags.NonPublic)!;
            var task = Assert.IsAssignableFrom<Task<ToolOutcome>>(
                invoke.Invoke(null, [command, context, new ToolArgs(arguments, ["jobId"]), CancellationToken.None]));
            var outcome = await task;

            Assert.Null(outcome.Refusal);
            var actualJson = outcome.Value!;
            var actualEnvelope = new JsonObject { ["ok"] = true, ["result"] = actualJson.DeepClone() };
            var projectedEnvelope = new JsonObject { ["ok"] = true, ["result"] = expectedJson!.DeepClone() };
            _output.WriteLine("Catalog response bytes before: " + Encoding.UTF8.GetByteCount(actualEnvelope.ToJsonString()));
            _output.WriteLine("ProjectionJson response bytes after: " + Encoding.UTF8.GetByteCount(projectedEnvelope.ToJsonString()));
            Assert.True(JsonNode.DeepEquals(expectedJson, actualJson),
                "Catalog MCP JSON should match the project contract's projection JSON. Actual: " +
                actualJson.ToJsonString() + " Expected: " + expectedJson.ToJsonString());
        }
        finally { Directory.Delete(root, true); }
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
