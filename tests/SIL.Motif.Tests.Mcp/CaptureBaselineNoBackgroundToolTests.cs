using System.Text.Json;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Store;
using SIL.Motif.Host;
using SIL.Motif.Mcp;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Projects;
using Xunit;

namespace SIL.Motif.Tests.Mcp;

/// <summary>Issue 296: <c>motif_capture_baseline</c> takes <c>no_background</c>, the same switch as the CLI's.</summary>
[Collection(LcmCacheParallelCollections.Group3)]
public sealed class CaptureBaselineNoBackgroundToolTests(PristineProjectFixture pristine)
{
    [Fact]
    public async Task NoBackgroundCapturesWithoutQueueingAnyJobAndTheDefaultQueuesTheSourceIndex()
    {
        var path = pristine.CopyProjectFile();
        using var privateRoot = PrivateManagedRoot.Use(Path.GetDirectoryName(path)!);
        KnownProjectTestSetup.AssignUniqueTag(RunnerOptions.ResolveRoot(), path, "nobg");
        var context = new ServerContext(path, "1.0", new NoRunnerLauncher(
            new JobRunnerLaunchOptions(RunnerOptions.ResolveRoot(), null)), new ActivityLog(), ToolProfile.Builtin,
            TextWriter.Null, advancedAiModeEnabled: true);
        var tools = MotifMcpServer.Expose(ToolProfile.Builtin);

        var foreground = await MotifMcpServer.CallAsync(context, tools, "motif_capture_baseline",
            new Dictionary<string, JsonElement>
            {
                ["project"] = JsonSerializer.SerializeToElement(path),
                ["no_background"] = JsonSerializer.SerializeToElement(true),
            }, CancellationToken.None);
        Assert.NotEqual(true, foreground.IsError);
        Assert.Empty(ActiveJobKinds(path));

        var background = await MotifMcpServer.CallAsync(context, tools, "motif_capture_baseline",
            new Dictionary<string, JsonElement> { ["project"] = JsonSerializer.SerializeToElement(path) },
            CancellationToken.None);
        Assert.NotEqual(true, background.IsError);
        Assert.Contains("source-index", ActiveJobKinds(path));
    }

    [Fact]
    public void NoBackgroundIsOneSharedFieldOnEveryToolThatTakesIt()
    {
        var tools = MotifMcpServer.Expose(ToolProfile.Builtin);
        foreach (var name in new[] { "motif_capture_baseline", "motif_assess", "motif_dry_run", "motif_trial" })
        {
            var tool = tools.Single(candidate => candidate.Name == name);
            Assert.True(tool.Tool.InputSchema["properties"]!.AsObject().ContainsKey("no_background"), name);
            Assert.DoesNotContain("NoBackground:", tool.Description, StringComparison.Ordinal);
        }
    }

    private static string[] ActiveJobKinds(string fwDataPath) =>
        ProjectStoreCommand.Run(fwDataPath, MotifProductVersion.CurrentText, (database, project) =>
            SIL.Motif.Contract.Commands.CommandOutcome<string[]>.Success(
                new JobRepository(database).ListActive(ProjectWorkspaceKey.Compute(project))
                    .Select(job => job.Kind).ToArray())).Value!;
}
