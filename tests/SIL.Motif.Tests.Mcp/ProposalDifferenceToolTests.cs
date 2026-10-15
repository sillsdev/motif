using System.Text.Json;
using SIL.Motif.Commands;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Host.Store;
using SIL.Motif.Mcp;
using SIL.Motif.Worker;
using SIL.Motif.Worker.Projects;
using Xunit;

namespace SIL.Motif.Tests.Mcp;

[Trait("MotifTestLevel", "Unit")]
public sealed class ProposalDifferenceToolTests
{
    [Fact]
    public void TheReadToolAndTheProduceToolAreExposedInAdvancedAiMode()
    {
        var names = MotifMcpServer.Expose(ToolProfile.Builtin).Select(tool => tool.Name).ToArray();

        Assert.Contains("motif_proposal_difference", names);
        Assert.Contains("motif_difference_produce", names);
    }

    [Fact]
    public void EveryEnumeratedChoiceOfTheReadToolIsListedOnce()
    {
        var tool = MotifMcpServer.Expose(ToolProfile.Builtin).Single(tool => tool.Name == "motif_proposal_difference");
        var properties = (System.Text.Json.Nodes.JsonObject)tool.Tool.InputSchema["properties"]!;

        foreach (var name in new[] { "bucket", "before_state", "after_state" })
        {
            var choices = properties[name]!["enum"]!.AsArray().Select(node => node!.GetValue<string>()).ToArray();
            Assert.Equal(choices.Length, choices.Distinct(StringComparer.Ordinal).Count());
        }
    }

    [Fact]
    public async Task ReadingBeforeAnyComparisonIsSavedNamesTheProduceStep()
    {
        var directory = Path.Combine(Path.GetTempPath(), "motif-proposal-difference-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var project = Path.Combine(directory, "project.fwdata");
        File.WriteAllText(project, "");
        try
        {
            using (var machine = MachineDatabase.Open(RunnerOptions.ResolveRoot()))
                new KnownProjectRegistry(machine).Record(ProjectWorkspaceKey.Compute(
                    new ProjectLocator(project, "project")), project, DateTimeOffset.UtcNow);
            var context = new ServerContext(project, "1.0", new NoRunnerLauncher(
                new JobRunnerLaunchOptions(RunnerOptions.ResolveRoot(), null)), new ActivityLog(null),
                ToolProfile.Builtin, TextWriter.Null, advancedAiModeEnabled: true);
            var arguments = new Dictionary<string, JsonElement>
            {
                ["project"] = JsonSerializer.SerializeToElement(project),
                ["proposal"] = JsonSerializer.SerializeToElement("proposal/none"),
                ["could_be_bad"] = JsonSerializer.SerializeToElement(true),
            };

            var result = await MotifMcpServer.CallAsync(context, MotifMcpServer.Expose(ToolProfile.Builtin),
                "motif_proposal_difference", arguments, CancellationToken.None);

            Assert.True(result.IsError);
            Assert.Equal("difference.none", result.StructuredContent!.Value.GetProperty("code").GetString());
            Assert.Equal("NotFound", result.StructuredContent.Value.GetProperty("reason").GetString());
        }
        finally { Directory.Delete(directory, true); }
    }
}
