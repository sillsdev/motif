using System.Text.Json;
using ModelContextProtocol.Protocol;
using SIL.Motif.Commands;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Host.Store;
using SIL.Motif.Mcp;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker;
using SIL.Motif.Worker.Projects;
using Xunit;

namespace SIL.Motif.Tests.Mcp;

/// <summary>A tool that opens a project whose writing system LibLCM cannot load reports a setup problem.</summary>
[Collection(LcmCacheParallelCollections.Group3)]
public sealed class WritingSystemMissingToolTests(PristineProjectFixture pristine)
{
    private const string SeamVariable = "MOTIF_TEST_FAIL_WRITING_SYSTEM_FOR";

    [Fact]
    public async Task ACaptureOfAProjectWithAMissingWritingSystemIsASetupProblemNotDamage()
    {
        var copy = pristine.CopyProjectFile();
        var path = Path.Combine(Path.GetDirectoryName(copy)!, "Wsfail" + Guid.NewGuid().ToString("N") + ".fwdata");
        File.Move(copy, path);
        using (var machine = MachineDatabase.Open(RunnerOptions.ResolveRoot()))
            new KnownProjectRegistry(machine).Record(ProjectWorkspaceKey.Compute(
                new ProjectLocator(path, "wsfail")), path, DateTimeOffset.UtcNow);
        var context = new ServerContext(path, "1.0", new NoRunnerLauncher(
            new JobRunnerLaunchOptions(RunnerOptions.ResolveRoot(), null)), new ActivityLog(null), ToolProfile.Builtin,
            TextWriter.Null, advancedAiModeEnabled: true);

        CallToolResult result;
        Environment.SetEnvironmentVariable(SeamVariable, Path.GetFileName(path));
        try
        {
            result = await MotifMcpServer.CallAsync(context, MotifMcpServer.Expose(ToolProfile.Builtin),
                "motif_capture_baseline", new Dictionary<string, JsonElement> { ["project"] = JsonSerializer.SerializeToElement(path) }, CancellationToken.None);
        }
        finally { Environment.SetEnvironmentVariable(SeamVariable, null); }

        Assert.True(result.IsError);
        var structured = result.StructuredContent!.Value;
        Assert.Equal("project.writing-system-missing", structured.GetProperty("code").GetString());
        var text = structured.GetRawText();
        Assert.Contains("setup problem", structured.GetProperty("next").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain("damaged", structured.GetProperty("message").GetString()!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(Path.Combine(Path.GetDirectoryName(path)!, "WritingSystemStore"), text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AGrammarReadNamesTheProjectsOwnStoreNeverTheSavedCopy()
    {
        var copy = pristine.CopyProjectFile();
        var path = Path.Combine(Path.GetDirectoryName(copy)!, "Wsfail" + Guid.NewGuid().ToString("N") + ".fwdata");
        File.Move(copy, path);
        using (var machine = MachineDatabase.Open(RunnerOptions.ResolveRoot()))
            new KnownProjectRegistry(machine).Record(ProjectWorkspaceKey.Compute(
                new ProjectLocator(path, "wsfail")), path, DateTimeOffset.UtcNow);
        var context = new ServerContext(path, "1.0", new NoRunnerLauncher(
            new JobRunnerLaunchOptions(RunnerOptions.ResolveRoot(), null)), new ActivityLog(null), ToolProfile.Builtin,
            TextWriter.Null, advancedAiModeEnabled: true);
        var tools = MotifMcpServer.Expose(ToolProfile.Builtin);
        var project = new Dictionary<string, JsonElement> { ["project"] = JsonSerializer.SerializeToElement(path) };
        var captured = await MotifMcpServer.CallAsync(context, tools, "motif_capture_baseline", project, CancellationToken.None);
        Assert.NotEqual(true, captured.IsError);

        CallToolResult result;
        Environment.SetEnvironmentVariable(SeamVariable, Path.GetFileName(path));
        try
        {
            result = await MotifMcpServer.CallAsync(context, tools, "motif_grammar", project, CancellationToken.None);
        }
        finally { Environment.SetEnvironmentVariable(SeamVariable, null); }

        Assert.True(result.IsError);
        var structured = result.StructuredContent!.Value;
        Assert.True(structured.GetProperty("code").GetString() == "project.writing-system-missing", structured.GetRawText());
        var store = Path.Combine(Path.GetDirectoryName(path)!, "WritingSystemStore");
        Assert.Contains(store, structured.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Equal(store, structured.GetProperty("facts").GetProperty("projectWritingSystemStore").GetString());
        Assert.DoesNotContain("SIL.Motif.BaselineRead", structured.GetRawText(), StringComparison.Ordinal);
    }
}
