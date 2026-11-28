using System.Text.Json.Nodes;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Contract.Phonology;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Mcp;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker;
using Xunit;

namespace SIL.Motif.Tests.Mcp;

/// <summary>A generated tool that queues a durable job wakes the worker and returns the job's handle.</summary>
[Collection(LcmCacheParallelCollections.Group3)]
public sealed class GeneratedJobToolTests(PristineProjectFixture pristine)
{
    [Fact]
    public async Task AGeneratedJobToolWakesTheWorkerAndReturnsItsJobHandle()
    {
        var project = pristine.CopyProjectFile();
        var root = Path.Combine(Path.GetDirectoryName(project)!, "job-tool-root");
        var captured = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(project), root);
        Assert.True(captured.Succeeded, captured.Refusal?.Message);
        var saved = PhonologyExampleCommands.Save(new SavePhonologyExamplesRequest(project, "Set",
            [new PhonologyExample("front", "a-ku", ["e-ki"], new PhonologyGenerationContext.HcXmlEntry("entry-17"))]));
        Assert.True(saved.Succeeded, saved.Refusal?.Message);
        var runner = new RecordingRunner(root);
        var context = new ServerContext(project, "1.0", runner, new ActivityLog(null),
            ToolProfile.Builtin, TextWriter.Null, advancedAiModeEnabled: true);
        var tool = AgentTools.All.Single(tool => tool.Name == "motif_phonology_examples_check");
        var arguments = new JsonObject { ["revision_id"] = saved.Value!.ExampleSet.Revision.RevisionId };

        var result = await tool.Run(context, new ToolArgs(arguments, tool.ArgumentNames), CancellationToken.None);

        Assert.Null(result.Refusal);
        Assert.Equal(project, Assert.Single(runner.Projects));
        Assert.Equal(result.Value!["jobId"]!.GetValue<string>(), result.JobId);
    }

    private sealed class RecordingRunner(string root) : IJobRunnerLauncher
    {
        public JobRunnerLaunchOptions Options { get; } = new(root, null);
        public List<string> Projects { get; } = [];
        public void Start(string projectPath, Action<string>? reportWarning = null) => Projects.Add(projectPath);
    }
}
