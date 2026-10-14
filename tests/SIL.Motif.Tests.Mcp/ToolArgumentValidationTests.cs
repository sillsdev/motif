using System.IO;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using SIL.Motif.Commands;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Host.Store;
using SIL.Motif.Mcp;
using SIL.Motif.Worker;
using SIL.Motif.Worker.Projects;
using Xunit;

namespace SIL.Motif.Tests.Mcp;

[Trait("MotifTestLevel", "Unit")]
public sealed class ToolArgumentValidationTests
{
    [Fact]
    public async Task InvalidProposalLimitIsRefusedAndRecordedBeforeTheToolRuns()
    {
        using var project = new RegisteredProject();
        var activity = new ActivityLog(Path.Combine(project.Directory, "activity.jsonl"));
        var context = project.Context(activity);
        var arguments = new Dictionary<string, JsonElement>
        {
            ["project"] = JsonSerializer.SerializeToElement(project.Path),
            ["limit"] = JsonSerializer.SerializeToElement("x"),
        };
        CallToolResult? result = null;

        var exception = await Record.ExceptionAsync(async () =>
            result = await MotifMcpServer.CallAsync(context, MotifMcpServer.Expose(ToolProfile.Builtin),
                "motif_proposals", arguments, CancellationToken.None));

        Assert.Null(exception);
        Assert.True(result!.IsError);
        Assert.Equal("tool.invalid-argument", result.StructuredContent!.Value.GetProperty("code").GetString());
        Assert.Contains(activity.Entries, entry => entry.Tool == "motif_proposals" && entry.IsError &&
            entry.Code == "tool.invalid-argument");
    }

    [Fact]
    public async Task UndeclaredDetailIsRefusedAndRecordedBeforeTheToolRuns()
    {
        using var project = new RegisteredProject();
        var activity = new ActivityLog(Path.Combine(project.Directory, "activity.jsonl"));
        var context = project.Context(activity);
        var arguments = new Dictionary<string, JsonElement>
        {
            ["project"] = JsonSerializer.SerializeToElement(project.Path),
            ["detail"] = JsonSerializer.SerializeToElement("verbose"),
        };

        var result = await MotifMcpServer.CallAsync(context, MotifMcpServer.Expose(ToolProfile.Builtin),
            "motif_proposals", arguments, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("tool.invalid-argument", result.StructuredContent!.Value.GetProperty("code").GetString());
        Assert.Contains(activity.Entries, entry => entry.Tool == "motif_proposals" && entry.IsError &&
            entry.Code == "tool.invalid-argument");
    }

    [Fact]
    public async Task MissingRequiredSlotsIsReportedBeforeComposingTheDraft()
    {
        using var project = new RegisteredProject();
        var arguments = new Dictionary<string, JsonElement>
        {
            ["project"] = JsonSerializer.SerializeToElement(project.Path),
            ["draft"] = JsonSerializer.SerializeToElement("slots-edit"),
            ["target"] = JsonSerializer.SerializeToElement("msa/target"),
            ["expected_slots"] = JsonSerializer.SerializeToElement(Array.Empty<string>()),
        };
        var profile = ToolProfile.Load("parsimony-experimental");

        var result = await MotifMcpServer.CallAsync(project.Context(new ActivityLog(null), profile),
            MotifMcpServer.Expose(profile), "motif_edit_inflectional_affix", arguments,
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("tool.missing-argument", result.StructuredContent!.Value.GetProperty("code").GetString());
        Assert.Contains("slots", result.StructuredContent.Value.GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AStringInsteadOfTrialWordsIsAnInvalidArgument()
    {
        using var project = new RegisteredProject();
        var arguments = new Dictionary<string, JsonElement>
        {
            ["project"] = JsonSerializer.SerializeToElement(project.Path),
            ["proposal"] = JsonSerializer.SerializeToElement("proposal/not-found"),
            ["words"] = JsonSerializer.SerializeToElement("foo"),
        };

        var result = await MotifMcpServer.CallAsync(project.Context(new ActivityLog(null)),
            MotifMcpServer.Expose(ToolProfile.Builtin), "motif_trial", arguments, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("tool.invalid-argument", result.StructuredContent!.Value.GetProperty("code").GetString());
    }

    [Fact]
    public void PresentNonIntegerFlagsAndListsAreInvalidArguments()
    {
        var args = new ToolArgs(new System.Text.Json.Nodes.JsonObject
        {
            ["limit"] = "x",
            ["left_out"] = "false",
            ["words"] = "foo",
            ["required"] = "not-an-array",
        }, ["limit", "left_out", "words", "required"]);

        Assert.Equal("tool.invalid-argument", Assert.Throws<ToolArgumentException>(() => args.Int("limit", 25)).Code);
        Assert.Equal("tool.invalid-argument", Assert.Throws<ToolArgumentException>(() => args.Flag("left_out")).Code);
        Assert.Equal("tool.invalid-argument", Assert.Throws<ToolArgumentException>(() => args.List("words")).Code);
        Assert.Equal("tool.invalid-argument", Assert.Throws<ToolArgumentException>(() => args.RequiredList("required")).Code);
        Assert.Empty(new ToolArgs(new System.Text.Json.Nodes.JsonObject(), []).List("optional"));
    }

    private sealed class RegisteredProject : IDisposable
    {
        public RegisteredProject()
        {
            Directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "motif-mcp-arguments-" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(Directory);
            Path = System.IO.Path.Combine(Directory, "project.fwdata");
            File.WriteAllText(Path, "");
            using var machine = MachineDatabase.Open(RunnerOptions.ResolveRoot());
            new KnownProjectRegistry(machine).Record(ProjectWorkspaceKey.Compute(
                new ProjectLocator(Path, "project")), Path, DateTimeOffset.UtcNow);
        }

        public string Directory { get; }

        public string Path { get; }

        public ServerContext Context(ActivityLog activity, ToolProfile? profile = null) => new(Path, "1.0", new NoRunnerLauncher(
            new JobRunnerLaunchOptions(RunnerOptions.ResolveRoot(), null)), activity, profile ?? ToolProfile.Builtin,
            TextWriter.Null, advancedAiModeEnabled: true);

        public void Dispose()
        {
            if (System.IO.Directory.Exists(Directory)) System.IO.Directory.Delete(Directory, true);
        }
    }
}
