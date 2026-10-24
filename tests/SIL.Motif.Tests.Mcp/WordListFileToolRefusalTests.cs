using System.Text.Json;
using SIL.Motif.Commands;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Host.Store;
using SIL.Motif.Mcp;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Mcp;

public sealed class WordListFileToolRefusalTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-word-file-tools-" + Guid.NewGuid().ToString("N"));

    public WordListFileToolRefusalTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Theory]
    [InlineData("motif_assess", "large", "assess.words-file-too-large")]
    [InlineData("motif_trial", "large", "assess.words-file-too-large")]
    [InlineData("motif_assess", "malformed", "assess.words-file-not-text")]
    [InlineData("motif_trial", "malformed", "assess.words-file-not-text")]
    [InlineData("motif_assess", "truncated", "assess.words-file-not-text")]
    [InlineData("motif_trial", "truncated", "assess.words-file-not-text")]
    [InlineData("motif_assess", "nul", "assess.words-file-not-text")]
    [InlineData("motif_trial", "nul", "assess.words-file-not-text")]
    public async Task InvalidWordFilesKeepTypedFactsAndGuidanceBeforeAnyRunnerOrJob(string tool, string kind, string code)
    {
        var project = Path.Combine(_root, "project.fwdata");
        File.WriteAllText(project, "");
        var words = Path.Combine(_root, "words.txt");
        byte[] bytes = kind switch
        {
            "large" => Enumerable.Repeat((byte)'a', 8 * 1024 * 1024 + 1).ToArray(),
            "malformed" => [0x61, 0xff],
            "truncated" => [0x61, 0xc3],
            _ => [0x61, 0x00, 0x62],
        };
        File.WriteAllBytes(words, bytes);
        var parser = FakeParser.CopyRecordingInvocations(Path.Combine(_root, "parser"));
        var runner = new RefuseRunner(Path.Combine(_root, "worker"), parser);
        using (var machine = MachineDatabase.Open(RunnerOptions.ResolveRoot()))
            new KnownProjectRegistry(machine).Record(ProjectWorkspaceKey.Compute(
                new ProjectLocator(project, "project")), project, DateTimeOffset.UtcNow);
        var context = new ServerContext(project, "1.0", runner, new ActivityLog(null), ToolProfile.Builtin,
            TextWriter.Null, advancedAiModeEnabled: true);
        var arguments = new Dictionary<string, JsonElement>
        {
            ["project"] = JsonSerializer.SerializeToElement(project),
            ["wordsFile"] = JsonSerializer.SerializeToElement(words),
        };
        if (tool == "motif_trial") arguments["proposal"] = JsonSerializer.SerializeToElement("proposal/unused");

        var result = await MotifMcpServer.CallAsync(context, MotifMcpServer.Expose(ToolProfile.Builtin),
            tool, arguments, CancellationToken.None);

        Assert.True(result.IsError);
        var refusal = result.StructuredContent!.Value;
        Assert.False(refusal.GetProperty("ok").GetBoolean());
        Assert.Equal(code, refusal.GetProperty("code").GetString());
        Assert.Equal("InvalidArgument", refusal.GetProperty("reason").GetString());
        Assert.Equal(words, refusal.GetProperty("facts").GetProperty("wordsFile").GetString());
        Assert.Contains("repeat", refusal.GetProperty("next").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, runner.Starts);
        Assert.Empty(FakeParser.Invocations(parser));
        var database = ProjectDatabaseCatalog.DatabasePathFor(new ProjectLocator(project, "project"));
        Assert.False(File.Exists(database), "No project store or job should be created for an unusable word file.");
    }

    private sealed class RefuseRunner(string root, string parser) : IJobRunnerLauncher
    {
        public JobRunnerLaunchOptions Options { get; } = new(root, parser);
        public int Starts { get; private set; }

        public void Start(string projectPath, Action<string>? reportWarning = null)
        {
            Starts++;
            throw new InvalidOperationException("An unusable word file must not launch the runner.");
        }
    }
}
