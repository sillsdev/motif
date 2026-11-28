using System.Text.Json;
using System.Text.Json.Nodes;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Mcp;
using Xunit;

namespace SIL.Motif.Tests.Mcp;

[Trait("MotifTestLevel", "Unit")]
public sealed class CallLogTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "motif-call-log-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    private static CallRecord Call(FailureReason? reason = null, string? code = null, params (string, string)[] args) =>
        new("mcp", "motif_overview", "builtin", args.ToDictionary(pair => pair.Item1, pair => pair.Item2),
            reason, code, false, null, 12, 345, null);

    private JsonObject OnlyLine()
    {
        var file = Assert.Single(Directory.GetFiles(_directory, "calls-*.jsonl"));
        return JsonNode.Parse(Assert.Single(File.ReadAllLines(file)))!.AsObject();
    }

    [Fact]
    public void ALineCarriesTheSchemaAndNeverTheArgumentValues()
    {
        var log = new CallLog(_directory, lane: "cli", agent: "claude", session: "s1",
            clock: () => new DateTimeOffset(2026, 11, 27, 10, 0, 0, TimeSpan.Zero));

        log.Record(Call(null, null, ("project", "/secret/grammar.fwdata"), ("proposal", "p-7")));

        var line = OnlyLine();
        Assert.Equal($"calls-2026-11-27-{Environment.ProcessId}.jsonl", Path.GetFileName(Directory.GetFiles(_directory).Single()));
        Assert.Equal(1, (int)line["v"]!);
        Assert.Equal("s1", (string)line["session"]!);
        Assert.Equal("cli", (string)line["lane"]!);
        Assert.Equal("claude", (string)line["agent"]!);
        Assert.Equal("mcp", (string)line["surface"]!);
        Assert.Equal("motif_overview", (string)line["tool"]!);
        Assert.Equal("ok", (string)line["outcome"]!);
        Assert.Equal(12, (int)line["durMs"]!);
        Assert.Equal(345, (int)line["resultBytes"]!);
        Assert.Equal("p-7", (string)line["proposalId"]!);
        Assert.Equal(["project", "proposal"], line["argKeys"]!.AsArray().Select(key => (string)key!));
        Assert.Equal("/secret/grammar.fwdata".Length, (int)line["argSizes"]!["project"]!);
        Assert.Equal(16, ((string)line["argHash"]!).Length);
        Assert.Null(line["args"]);
        Assert.DoesNotContain("secret", File.ReadAllText(Directory.GetFiles(_directory).Single()));
        Assert.True(line["parent"] is null);
    }

    [Fact]
    public void ASecondCallNamesTheFirstAsItsParent()
    {
        var log = new CallLog(_directory);
        log.Record(Call());
        log.Record(Call());

        var lines = File.ReadAllLines(Directory.GetFiles(_directory).Single()).Select(l => JsonNode.Parse(l)!).ToArray();
        Assert.Equal((string)lines[0]["id"]!, (string)lines[1]["parent"]!);
        Assert.Equal("unknown", (string)lines[0]["lane"]!);
    }

    [Fact]
    public async Task ConcurrentWritersLoseNoLine()
    {
        const int writers = 4, perWriter = 250;
        var logs = Enumerable.Range(0, writers).Select(_ => new CallLog(_directory)).ToArray();

        await Task.WhenAll(logs.Select(log => Task.Run(() =>
        {
            for (var i = 0; i < perWriter; i++) log.Record(Call());
        })));

        var lines = Directory.GetFiles(_directory).SelectMany(File.ReadAllLines).ToArray();
        Assert.Equal(writers * perWriter, lines.Length);
        Assert.All(lines, line => Assert.NotNull(JsonNode.Parse(line)));
    }

    [Fact]
    public void FullModeAlsoRecordsArgumentValues()
    {
        new CallLog(_directory, full: true).Record(Call(null, null, ("word", "kitaabu")));

        Assert.Equal("kitaabu", (string)OnlyLine()["args"]!["word"]!);
    }

    [Fact]
    public void OffWritesNothingAndADirectoryOrFullIsRead()
    {
        var off = CallLog.FromEnvironment(name => name == CallLog.Variable ? "OFF" : null, _directory);
        off.Record(Call());
        Assert.False(off.Enabled);
        Assert.False(Directory.Exists(_directory));

        var named = CallLog.FromEnvironment(name => name == CallLog.Variable ? _directory : null, "unused");
        named.Record(Call());
        Assert.Single(Directory.GetFiles(_directory));

        var root = Path.Combine(_directory, "root");
        var full = CallLog.FromEnvironment(name => name == CallLog.Variable ? "full" : null, root);
        full.Record(Call(null, null, ("k", "v")));
        Assert.Equal("v", (string)JsonNode.Parse(File.ReadAllLines(
            Directory.GetFiles(Path.Combine(root, "calls")).Single()).Single())!["args"]!["k"]!);
    }

    [Fact]
    public void ByDefaultTheLogIsOnUnderTheRootsCallsFolder()
    {
        var log = CallLog.FromEnvironment(_ => null, _directory);
        log.Record(Call());

        Assert.Single(Directory.GetFiles(Path.Combine(_directory, "calls")));
    }

    [Theory]
    [InlineData(null, null, "ok")]
    [InlineData(FailureReason.InvalidArgument, "tool.unknown", "user_error")]
    [InlineData(FailureReason.NotFound, "project.missing", "user_error")]
    [InlineData(FailureReason.Refused, "proposal.stale", "refused")]
    [InlineData(FailureReason.Refused, "trial.anything", "refused")]
    [InlineData(FailureReason.Refused, "apply.drift", "refused")]
    [InlineData(FailureReason.Refused, "command.advanced-ai-mode-required", "refused")]
    [InlineData(FailureReason.Refused, "tool.out-of-memory", "internal_error")]
    [InlineData(FailureReason.StoreInconsistent, "store.inconsistent", "internal_error")]
    [InlineData(FailureReason.Busy, "tool.failed", "internal_error")]
    [InlineData(FailureReason.Busy, "store.busy", "transient")]
    [InlineData(FailureReason.Cancelled, "tool.cancelled", "transient")]
    [InlineData(FailureReason.StoppedByBudget, "assess.budget", "transient")]
    public void TheOutcomeClassComesFromTheFailureReason(FailureReason? reason, string? code, string expected) =>
        Assert.Equal(expected, CallLog.Classify(reason, code));

    [Theory]
    [InlineData(0, "ok")]
    [InlineData(6, "ok")]
    [InlineData(1, "user_error")]
    [InlineData(3, "transient")]
    [InlineData(4, "internal_error")]
    public void ACliExitCodeWithoutARefusalIsClassifiedByItself(int exit, string expected) =>
        Assert.Equal(expected, CallLog.Classify(null, null, false, exit));

    [Fact]
    public void AnUnhandledExceptionIsAnInternalError() =>
        Assert.Equal("internal_error", CallLog.Classify(null, null, true));

    [Fact]
    public void FilesOlderThanThirtyDaysAreDeletedAndRecentOnesKept()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "calls-2026-10-01.jsonl"), "old\n");
        File.WriteAllText(Path.Combine(_directory, "calls-2026-11-10.jsonl"), "recent\n");
        File.WriteAllText(Path.Combine(_directory, "calls-notadate.jsonl"), "odd\n");

        new CallLog(_directory, clock: () => new DateTimeOffset(2026, 11, 27, 0, 0, 0, TimeSpan.Zero)).Record(Call());

        Assert.False(File.Exists(Path.Combine(_directory, "calls-2026-10-01.jsonl")));
        Assert.True(File.Exists(Path.Combine(_directory, "calls-2026-11-10.jsonl")));
        Assert.True(File.Exists(Path.Combine(_directory, "calls-notadate.jsonl")));
    }

    [Fact]
    public async Task AToolCallIsLoggedWithItsRefusalReasonAndAWriteFailureDoesNotBreakTheCall()
    {
        var context = new ServerContext(string.Empty, "1.0",
            new SIL.Motif.Commands.NoRunnerLauncher(new SIL.Motif.Commands.JobRunnerLaunchOptions("root", null)),
            new ActivityLog(new CallLog(_directory, lane: "grammar")), ToolProfile.Builtin, TextWriter.Null,
            advancedAiModeEnabled: true);
        var tools = MotifMcpServer.Expose(ToolProfile.Builtin);

        var result = await MotifMcpServer.CallAsync(context, tools, "motif_no_such_tool",
            new Dictionary<string, JsonElement> { ["x"] = JsonSerializer.SerializeToElement("y") }, CancellationToken.None);

        Assert.True(result.IsError);
        var line = OnlyLine();
        Assert.Equal("user_error", (string)line["outcome"]!);
        Assert.Equal("tool.unknown", (string)line["code"]!);
        Assert.Equal("InvalidArgument", (string)line["reason"]!);
        Assert.Equal("grammar", (string)line["lane"]!);
        Assert.Equal("unknown", (string)line["tool"]!);
        Assert.Empty(line["argKeys"]!.AsArray());

        // The "directory" is a file, so every write fails; the call must still answer.
        var blocked = Path.Combine(_directory, "blocked");
        File.WriteAllText(blocked, "not a directory");
        var broken = new ServerContext(string.Empty, "1.0",
            new SIL.Motif.Commands.NoRunnerLauncher(new SIL.Motif.Commands.JobRunnerLaunchOptions("root", null)),
            new ActivityLog(new CallLog(blocked)), ToolProfile.Builtin, TextWriter.Null, advancedAiModeEnabled: true);

        var survived = await MotifMcpServer.CallAsync(broken, tools, "motif_no_such_tool", null, CancellationToken.None);

        Assert.True(survived.IsError);
    }
}
