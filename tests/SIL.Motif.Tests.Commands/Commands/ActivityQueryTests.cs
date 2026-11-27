using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.Commands;

public sealed class ActivityQueryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-activity-" + Guid.NewGuid().ToString("N"));

    public ActivityQueryTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void SummarizeCountsCallsErrorsRefusalsByCodeAndFollowedAndIgnoredNext()
    {
        ActivityCall[] calls =
        [
            new("motif_overview", true, "baseline.missing", null),
            new("motif_capture_baseline", false, null, true),
            new("motif_add_phoneme", true, "tool.missing-argument", false),
            new("motif_add_phoneme", true, "tool.missing-argument", false),
        ];

        var summary = ActivityQuery.Summarize(calls);

        Assert.Equal(4, summary.Total);
        Assert.Equal(3, summary.Errors);
        Assert.Equal(1, summary.FollowedNext);
        Assert.Equal(2, summary.IgnoredNext);
        Assert.Equal(new[] { "motif_add_phoneme", "motif_capture_baseline", "motif_overview" }, summary.ByTool.Keys);
        Assert.Equal(2, summary.ByTool["motif_add_phoneme"]);
        Assert.Equal(new[] { "baseline.missing", "tool.missing-argument" }, summary.RefusalsByCode.Keys);
        Assert.Equal(2, summary.RefusalsByCode["tool.missing-argument"]);
    }

    [Fact]
    public void ReadSummarizesTheLinesOfACallLogAndSkipsLinesItCannotRead()
    {
        var path = Path.Combine(_root, "calls-2026-01-01.jsonl");
        File.WriteAllLines(path,
        [
            "{\"tool\":\"motif_overview\",\"outcome\":\"user_error\",\"code\":\"baseline.missing\"}",
            "not json",
            string.Empty,
            "{\"tool\":\"motif_capture_baseline\",\"outcome\":\"ok\",\"followedPrev\":true}",
        ]);

        var outcome = ActivityQuery.Read(new ActivityRequest(path));

        Assert.True(outcome.Succeeded);
        Assert.Equal(2, outcome.Value!.Total);
        Assert.Equal(1, outcome.Value.Errors);
        Assert.Equal(1, outcome.Value.FollowedNext);
        Assert.Equal(0, outcome.Value.IgnoredNext);
        Assert.Equal(1, outcome.Value.RefusalsByCode["baseline.missing"]);
    }

    [Fact]
    public void ReadAcceptsTheFolderOfDayFiles()
    {
        File.WriteAllText(Path.Combine(_root, "calls-2026-01-01.jsonl"), "{\"tool\":\"a\",\"outcome\":\"ok\"}\n");
        File.WriteAllText(Path.Combine(_root, "calls-2026-01-02.jsonl"), "{\"tool\":\"b\",\"outcome\":\"ok\"}\n");

        Assert.Equal(2, ActivityQuery.Read(new ActivityRequest(_root)).Value!.Total);
    }

    [Fact]
    public void ReadRefusesALogThatIsNotThere()
    {
        var outcome = ActivityQuery.Read(new ActivityRequest(Path.Combine(_root, "absent.jsonl")));

        Assert.False(outcome.Succeeded);
        Assert.Equal("activity.log-unreadable", outcome.Refusal!.Code);
        Assert.Equal(FailureReason.NotFound, outcome.Refusal.Reason);
    }
}
