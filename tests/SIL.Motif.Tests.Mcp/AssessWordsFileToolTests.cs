using System.Text.Json;
using System.Text.Json.Nodes;
using SIL.Motif.Commands;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Host.Store;
using SIL.Motif.Mcp;
using SIL.Motif.Worker;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Mcp;

[Trait("MotifTestLevel", "Unit")]
public sealed class AssessWordsFileToolTests
{
    [Theory]
    [InlineData("motif_assess")]
    [InlineData("motif_trial")]
    public void TheToolTakesAWordsFileInCamelCase(string tool)
    {
        var exposed = MotifMcpServer.Expose(ToolProfile.Builtin).Single(entry => entry.Name == tool);

        var properties = exposed.Tool.InputSchema["properties"]!.AsObject();
        Assert.Equal("string", properties["wordsFile"]!["type"]!.GetValue<string>());
    }

    [Fact]
    public void TheAssessToolPagesAStoredAssessmentWithOffsetAndAssessment()
    {
        var exposed = MotifMcpServer.Expose(ToolProfile.Builtin).Single(entry => entry.Name == "motif_assess");

        var properties = exposed.Tool.InputSchema["properties"]!.AsObject();
        Assert.True(properties.ContainsKey("assessment"));
        Assert.True(properties.ContainsKey("offset"));
    }

    [Theory]
    [InlineData("motif_assess", "words", "assess.words-ambiguous")]
    [InlineData("motif_trial", "words", "assess.words-ambiguous")]
    [InlineData("motif_assess", null, "assess.words-file-unreadable")]
    [InlineData("motif_trial", null, "assess.words-file-unreadable")]
    public async Task AWordsFileThatCannotBeUsedIsRefusedWithARegisteredCode(string tool, string? inline, string code)
    {
        var directory = Path.Combine(Path.GetTempPath(), "motif-words-file-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var project = Path.Combine(directory, "project.fwdata");
        File.WriteAllText(project, "");
        var wordsFile = inline is null ? Path.Combine(directory, "missing.txt") : Path.Combine(directory, "words.txt");
        File.WriteAllText(Path.Combine(directory, "words.txt"), "kaon\n");
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
                ["wordsFile"] = JsonSerializer.SerializeToElement(wordsFile),
            };
            if (inline is not null) arguments["words"] = JsonSerializer.SerializeToElement(new[] { "baga" });
            if (tool == "motif_trial") arguments["proposal"] = JsonSerializer.SerializeToElement("proposal/x");

            var result = await MotifMcpServer.CallAsync(context, MotifMcpServer.Expose(ToolProfile.Builtin),
                tool, arguments, CancellationToken.None);

            Assert.True(result.IsError);
            Assert.Equal(code, result.StructuredContent!.Value.GetProperty("code").GetString());
        }
        finally { Directory.Delete(directory, true); }
    }
}
