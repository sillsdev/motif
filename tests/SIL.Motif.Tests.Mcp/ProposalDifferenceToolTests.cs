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

        foreach (var name in new[] { "bucket", "before_state", "after_state", "limitation" })
        {
            var choices = properties[name]!["enum"]!.AsArray().Select(node => node!.GetValue<string>()).ToArray();
            Assert.Equal(choices.Length, choices.Distinct(StringComparer.Ordinal).Count());
        }
    }

    [Fact]
    public void TheReadToolOffersTheSameLimitationsAndPlacePagingAsTheCommand()
    {
        var tool = MotifMcpServer.Expose(ToolProfile.Builtin).Single(tool => tool.Name == "motif_proposal_difference");
        var properties = (System.Text.Json.Nodes.JsonObject)tool.Tool.InputSchema["properties"]!;

        var choices = properties["limitation"]!["enum"]!.AsArray().Select(node => node!.GetValue<string>()).ToArray();
        Assert.Equal(["capped", "timed-out", "projection-unavailable", "parser-refused", "invalid-shape",
            "malformed-evidence", "invalid-expected-morphology"], choices);
        Assert.NotNull(properties["word"]);
        Assert.NotNull(properties["occurrence_offset"]);
        Assert.NotNull(properties["occurrence_limit"]);
    }

    [Fact]
    public async Task TheNewArgumentsReachTheSharedCommandAndAreRefusedWithItsCodes()
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
                ["word"] = JsonSerializer.SerializeToElement("sita"),
                ["limitation"] = JsonSerializer.SerializeToElement("nonsense"),
            };

            var result = await MotifMcpServer.CallAsync(context, MotifMcpServer.Expose(ToolProfile.Builtin),
                "motif_proposal_difference", arguments, CancellationToken.None);

            Assert.True(result.IsError);
            Assert.Equal("difference.invalid-limitation", result.StructuredContent!.Value.GetProperty("code").GetString());
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData("occurrence_offset")]
    [InlineData("occurrence_limit")]
    public async Task PlaceArgumentsWithoutAWordAreRefusedInsteadOfIgnored(string argument)
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
                [argument] = JsonSerializer.SerializeToElement(1),
            };

            var result = await MotifMcpServer.CallAsync(context, MotifMcpServer.Expose(ToolProfile.Builtin),
                "motif_proposal_difference", arguments, CancellationToken.None);

            Assert.True(result.IsError);
            Assert.Equal("difference.occurrence-needs-word",
                result.StructuredContent!.Value.GetProperty("code").GetString());
        }
        finally { Directory.Delete(directory, true); }
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

    private static string SourceValidatedProfile() =>
        Path.Combine(AppContext.BaseDirectory, "profiles", "source-validated.json");

    [Fact]
    public void TheDefaultProduceToolOffersNoReferenceAndTheSourceValidatedOneIsOptIn()
    {
        var defaults = MotifMcpServer.Expose(ToolProfile.Builtin);
        var optIn = MotifMcpServer.Expose(ToolProfile.Load(SourceValidatedProfile()));

        var produce = defaults.Single(tool => tool.Name == "motif_difference_produce");
        var properties = (System.Text.Json.Nodes.JsonObject)produce.Tool.InputSchema["properties"]!;
        Assert.Null(properties["reference"]);
        Assert.DoesNotContain(defaults, tool => tool.Name == "motif_difference_produce_source_validated");
        Assert.Contains(optIn, tool => tool.Name == "motif_difference_produce_source_validated");
        Assert.Contains(optIn, tool => tool.Name == "motif_proposal_difference");
    }

    [Fact]
    public async Task TheSourceValidatedProduceToolAsksForTheSourceValidatedReference()
    {
        var directory = Path.Combine(Path.GetTempPath(), "motif-proposal-difference-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var project = Path.Combine(directory, "project.fwdata");
        File.WriteAllText(project, "");
        var seen = new System.Collections.Concurrent.ConcurrentDictionary<string, string>();
        var original = AgentTools.ProduceSourceValidatedDifference;
        AgentTools.ProduceSourceValidatedDifference = (request, reference) =>
        {
            seen[request.ProposalId] = reference;
            return original(request, reference);
        };
        try
        {
            using (var machine = MachineDatabase.Open(RunnerOptions.ResolveRoot()))
                new KnownProjectRegistry(machine).Record(ProjectWorkspaceKey.Compute(
                    new ProjectLocator(project, "project")), project, DateTimeOffset.UtcNow);
            var profile = ToolProfile.Load(SourceValidatedProfile());
            var context = new ServerContext(project, "1.0", new NoRunnerLauncher(
                new JobRunnerLaunchOptions(RunnerOptions.ResolveRoot(), null)), new ActivityLog(null),
                profile, TextWriter.Null, advancedAiModeEnabled: true);
            var arguments = new Dictionary<string, JsonElement>
            {
                ["project"] = JsonSerializer.SerializeToElement(project),
                ["proposal"] = JsonSerializer.SerializeToElement("proposal/none-for-reference"),
                ["text"] = JsonSerializer.SerializeToElement(Guid.NewGuid().ToString()),
            };

            var result = await MotifMcpServer.CallAsync(context, MotifMcpServer.Expose(profile),
                "motif_difference_produce_source_validated", arguments, CancellationToken.None);

            Assert.True(result.IsError);
            Assert.Equal("difference.invalid-proposal", result.StructuredContent!.Value.GetProperty("code").GetString());
            Assert.Equal("source-validated", seen["proposal/none-for-reference"]);
        }
        finally
        {
            AgentTools.ProduceSourceValidatedDifference = original;
            Directory.Delete(directory, true);
        }
    }
}
