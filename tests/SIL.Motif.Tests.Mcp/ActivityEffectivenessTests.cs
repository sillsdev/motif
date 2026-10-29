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
        var log = new ActivityLog(null);
        var context = Context(log);

        await MotifMcpServer.CallAsync(context, MotifMcpServer.Expose(ToolProfile.Builtin), "motif_add_phoneme",
            Args(("project", context.ProjectPath)), CancellationToken.None);

        var composers = Assert.Single(log.Entries).Composers;
        Assert.NotNull(composers);
        Assert.Equal(["compose-author-phoneme"], composers);
    }

    [Fact]
    public async Task ABatchCallRecordsTheDistinctVerbsOfItsSteps()
    {
        var log = new ActivityLog(null);
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
        var log = new ActivityLog(null);
        var context = Context(log);

        await MotifMcpServer.CallAsync(context, MotifMcpServer.Expose(ToolProfile.Builtin), "motif_overview",
            Args(("project", context.ProjectPath)), CancellationToken.None);

        Assert.Empty(Assert.Single(log.Entries).Composers!);
    }

    [Fact]
    public async Task ARefusalRecordsItsCodeAndTheFirstCallHasNoFollowedNext()
    {
        var log = new ActivityLog(null);
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
        var log = new ActivityLog(null);

        log.Record(Entry("motif_start_proposal"), "Add changes with motif_add_phoneme, then motif_dry_run.");
        log.Record(Entry("motif_add_phoneme"), "Now call motif_dry_run_extra.");
        log.Record(Entry("motif_dry_run"), null);
        log.Record(Entry("motif_overview"));

        Assert.Equal([null, true, false, false], log.Entries.Select(entry => entry.FollowedNext).ToArray());
    }

    [Fact]
    public void LinesWrittenBeforeTheFieldsExistStillRead()
    {
        var path = Path.Combine(Path.GetTempPath(), "activity-old-" + Guid.NewGuid().ToString("N") + ".jsonl");
        try
        {
            File.WriteAllText(path, "{\"at\":\"2026-01-01T00:00:00+00:00\",\"tool\":\"motif_overview\",\"args\":{}," +
                "\"isError\":false,\"code\":null,\"resultBytes\":1,\"durationMs\":1,\"jobId\":null,\"profile\":\"builtin\"}\n");

            var entry = Assert.Single(new ActivityLog(path).Entries);

            Assert.Null(entry.FollowedNext);
            Assert.True(entry.Composers is null or { Length: 0 });
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void TheLogLineCarriesComposersAndFollowedNextInCamelCase()
    {
        var path = Path.Combine(Path.GetTempPath(), "activity-new-" + Guid.NewGuid().ToString("N") + ".jsonl");
        try
        {
            var log = new ActivityLog(path);
            log.Record(Entry("motif_a") with { Composers = ["compose-author-phoneme"] }, "motif_b");
            log.Record(Entry("motif_b"));

            var second = JsonNode.Parse(File.ReadAllLines(path)[1])!;
            Assert.True(second["followedNext"]!.GetValue<bool>());
            Assert.Empty(second["composers"]!.AsArray());
            Assert.Equal("compose-author-phoneme", JsonNode.Parse(File.ReadAllLines(path)[0])!["composers"]![0]!.GetValue<string>());
        }
        finally { File.Delete(path); }
    }
}
