using System.Text.Json;
using System.Text.Json.Nodes;
using SIL.Motif.Mcp;
using Xunit;

namespace SIL.Motif.Tests.Mcp;

[Trait("MotifTestLevel", "Unit")]
public sealed class ActivityEffectivenessTests
{
    private static ServerContext Context(ActivityLog log) => new(
        Path.Combine(Path.GetTempPath(), "no-such-motif-project.fwdata"), "1.0",
        new SIL.Motif.Commands.NoRunnerLauncher(new SIL.Motif.Commands.JobRunnerLaunchOptions("root", null)),
        log, ToolProfile.Builtin, TextWriter.Null, advancedAiModeEnabled: true);

    private static Dictionary<string, JsonElement> Args(params (string Name, object Value)[] arguments) =>
        arguments.ToDictionary(pair => pair.Name, pair => JsonSerializer.SerializeToElement(pair.Value));

    private static ActivityEntry Entry(string tool) => new(DateTimeOffset.UtcNow, tool, new JsonObject(), false, null, 0, 0,
        null, "builtin");

    [Fact]
    public async Task AnAddToolCallRecordsTheComposerVerbItAddressed()
    {
        var log = new ActivityLog();
        var context = Context(log);

        await MotifMcpServer.CallAsync(context, MotifMcpServer.Expose(ToolProfile.Load("full")), "motif_add_phoneme",
            Args(("project", context.ProjectPath)), CancellationToken.None);

        var composers = Assert.Single(log.Entries).Composers;
        Assert.NotNull(composers);
        Assert.Equal(["compose-author-phoneme"], composers);
    }

    [Fact]
    public async Task ABatchCallRecordsTheDistinctVerbsOfItsSteps()
    {
        var log = new ActivityLog();
        var context = Context(log);
        var intents = new[]
        {
            new { verb = "compose-author-phoneme", intent = new { } },
            new { verb = "compose-author-natural-class", intent = new { } },
            new { verb = "compose-author-phoneme", intent = new { } },
        };

        await MotifMcpServer.CallAsync(context, MotifMcpServer.Expose(ToolProfile.Builtin), "motif_compose_batch",
            Args(("project", context.ProjectPath), ("draft", "d"), ("intents", intents)), CancellationToken.None);

        var composers = Assert.Single(log.Entries).Composers;
        Assert.NotNull(composers);
        Assert.Equal(["compose-author-phoneme", "compose-author-natural-class"], composers);
    }

    [Fact]
    public async Task AToolThatIsNoComposerRecordsNoComposers()
    {
        var log = new ActivityLog();
        var context = Context(log);

        await MotifMcpServer.CallAsync(context, MotifMcpServer.Expose(ToolProfile.Builtin), "motif_overview",
            Args(("project", context.ProjectPath)), CancellationToken.None);

        Assert.Empty(Assert.Single(log.Entries).Composers!);
    }

    [Fact]
    public async Task ARefusalRecordsItsCodeAndTheFirstCallHasNoFollowedNext()
    {
        var log = new ActivityLog();
        var context = Context(log);
        var tools = MotifMcpServer.Expose(ToolProfile.Builtin);

        await MotifMcpServer.CallAsync(context, tools, "motif_overview", Args(("project", context.ProjectPath)),
            CancellationToken.None);
        await MotifMcpServer.CallAsync(context, tools, "motif_overview", Args(("project", context.ProjectPath)),
            CancellationToken.None);

        var entries = log.Entries;
        Assert.Equal("project.unknown", entries[0].Code);
        Assert.Null(entries[0].FollowedNext);
        Assert.False(entries[1].FollowedNext);
    }

    [Fact]
    public void ACallIsFollowedNextOnlyWhenThePreviousAdviceNamesItsToolExactly()
    {
        var log = new ActivityLog();

        log.Record(Entry("motif_start_proposal"), "Add changes with motif_add_phoneme, then motif_dry_run.");
        log.Record(Entry("motif_add_phoneme"), "Now call motif_dry_run_extra.");
        log.Record(Entry("motif_dry_run"), null);
        log.Record(Entry("motif_overview"));

        Assert.Equal([null, true, false, false], log.Entries.Select(entry => entry.FollowedNext).ToArray());
    }

    [Fact]
    public void TheCallLogLineCarriesComposersAndFollowedPrev()
    {
        var directory = Path.Combine(Path.GetTempPath(), "activity-new-" + Guid.NewGuid().ToString("N"));
        try
        {
            var log = new ActivityLog(new SIL.Motif.Host.CallLog(directory));
            log.Record(Entry("motif_a") with { Composers = ["compose-author-phoneme"] }, "motif_b");
            log.Record(Entry("motif_b"));

            var lines = File.ReadAllLines(Directory.GetFiles(directory).Single());
            var second = JsonNode.Parse(lines[1])!;
            Assert.True(second["followedPrev"]!.GetValue<bool>());
            Assert.Null(second["composers"]);
            Assert.Equal("motif_b", JsonNode.Parse(lines[0])!["nextNamed"]!.GetValue<string>());
            Assert.Equal("compose-author-phoneme", JsonNode.Parse(lines[0])!["composers"]![0]!.GetValue<string>());
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
