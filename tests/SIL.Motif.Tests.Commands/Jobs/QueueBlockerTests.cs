using SIL.Motif.Commands;
using SIL.Motif.Contract.Jobs;
using Xunit;

namespace SIL.Motif.Tests.Commands;

public sealed class QueueBlockerTests
{
    private static JobQueueEntryResponse Entry(string id, string kind, string project, JobStatus status = JobStatus.Queued) =>
        new(id, "key-" + project, "/projects/" + project + ".fwdata", kind, status, 1, "2026-11-20T10:00:00Z", 1.0);

    [Fact]
    public void AJobBehindAnotherProjectsJobNamesItAndTheRunner()
    {
        var queue = new[]
        {
            Entry("job-live", "source-index", "live", JobStatus.Running),
            Entry("job-other", "parsimony", "live"),
            Entry("job-scratch", "dry-run", "scratch"),
        };

        var blocker = QueueBlocker.For(queue, "job-scratch", 4242);
        var text = blocker.Describe();

        Assert.Equal(2, blocker.Ahead);
        Assert.Contains("2 jobs are ahead of it", text, StringComparison.Ordinal);
        Assert.Contains("first: job-live source-index (running) on '/projects/live.fwdata'", text, StringComparison.Ordinal);
        Assert.Contains("The runner is pid 4242", text, StringComparison.Ordinal);
        var facts = new Dictionary<string, string>();
        blocker.AddFacts(facts);
        Assert.Equal("job-live", facts["blockingJobId"]);
        Assert.Equal("2", facts["queueAhead"]);
        Assert.Equal("4242", facts["runnerPid"]);
    }

    [Fact]
    public void AJobAtTheFrontNamesNoBlockerAndSaysWhenNoRunnerIsRecorded()
    {
        var blocker = QueueBlocker.For([Entry("job-a", "dry-run", "a")], "job-a", null);

        var text = blocker.Describe();

        Assert.Null(blocker.First);
        Assert.DoesNotContain("ahead of it", text, StringComparison.Ordinal);
        Assert.Contains("No live runner is recorded", text, StringComparison.Ordinal);
    }
}
