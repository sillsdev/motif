using System.Text.Json.Nodes;
using SIL.Motif.Commands;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Mcp;
using Xunit;

namespace SIL.Motif.Tests.Mcp;

[Trait("MotifTestLevel", "Unit")]
public sealed class FindComposerToolTests
{
    private static AgentTool Tool => AgentTools.All.Single(tool => tool.Name == "motif_find_composer");

    [Fact]
    public void TheToolReadsOnlyNeedsNoProjectAndIsOffUnlessAProfileSelectsIt()
    {
        Assert.True(Tool.ReadOnly);
        Assert.False(Tool.NeedsProject);
        Assert.False(Tool.OnByDefault);
        Assert.DoesNotContain(MotifMcpServer.Expose(ToolProfile.Builtin), exposed => exposed.Name == Tool.Name);
        var selected = new ToolProfile("find", null, null, "concise", [new ToolSelection(Tool.Name, null, null, null)]);
        Assert.Contains(MotifMcpServer.Expose(selected), exposed => exposed.Name == Tool.Name);
    }

    [Fact]
    public void TheToolTakesCamelCaseGoalSubjectAndADetailChoice()
    {
        Assert.Equal(["detail", "goal", "subject"], Tool.ArgumentNames.Order(StringComparer.Ordinal));
        var detail = Tool.InputSchema["properties"]!["detail"]!["enum"]!.AsArray().Select(node => (string)node!);
        Assert.Equal(["concise", "detailed"], detail);
    }

    [Fact]
    public async Task ACallRanksComposersAndRefusesAGoalWithASubject()
    {
        var context = new ServerContext("", "1.0", new NoRunnerLauncher(new JobRunnerLaunchOptions("root", null)),
            new ActivityLog(null), ToolProfile.Builtin, TextWriter.Null, advancedAiModeEnabled: true);

        var found = await Tool.Run(context, Args(("goal", "add a missing consonant")), CancellationToken.None);
        Assert.Null(found.Refusal);
        Assert.Equal("compose-author-phoneme", found.Value!["results"]![0]!["verb"]!.GetValue<string>());

        var both = await Tool.Run(context, Args(("goal", "x"), ("subject", "sound-inventory")), CancellationToken.None);
        Assert.Equal("composer.goal-or-subject", both.Refusal!.Code);
    }

    [Fact]
    public async Task ANoMatchCallLogsAFindComposerMissWithTheGoalAndNearestSubjectAndMatchesLogNothing()
    {
        var path = Path.Combine(Path.GetTempPath(), "find-composer-miss-" + Guid.NewGuid().ToString("N") + ".jsonl");
        try
        {
            var log = new ActivityLog(path);
            var context = new ServerContext("", "1.0", new NoRunnerLauncher(new JobRunnerLaunchOptions("root", null)),
                log, ToolProfile.Builtin, TextWriter.Null, advancedAiModeEnabled: true);

            await Tool.Run(context, Args(("goal", "add a missing consonant")), CancellationToken.None);
            Assert.DoesNotContain(log.Entries, entry => entry.Tool == "findComposerMiss");

            var miss = await Tool.Run(context, Args(("goal", "Export the dictionary to a printable file")),
                CancellationToken.None);
            Assert.True(miss.Value!["noMatch"]!.GetValue<bool>());
            Assert.Contains(miss.Value["missing"]!.GetValue<string>(), miss.Next!);

            var entry = Assert.Single(log.Entries, e => e.Tool == "findComposerMiss");
            Assert.Equal("Export the dictionary to a printable file", entry.Args["goal"]!.GetValue<string>());
            Assert.Equal(miss.Value["nearestSubjects"]![0]!["id"]!.GetValue<string>(),
                entry.Args["nearestSubject"]!.GetValue<string>());
            Assert.Contains("findComposerMiss", File.ReadAllText(path));
        }
        finally { File.Delete(path); }
    }

    private static ToolArgs Args(params (string Name, string Value)[] values)
    {
        var json = new JsonObject();
        foreach (var (name, value) in values) json[name] = value;
        return new ToolArgs(json, Tool.ArgumentNames);
    }
}
