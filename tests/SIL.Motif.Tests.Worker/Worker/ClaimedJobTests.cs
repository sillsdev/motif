using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Jobs;
using System.Text.Json;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Ids;
using Xunit;

namespace SIL.Motif.Tests.Worker;

/// <summary>Covers the handle <see cref="JobRunnerLoop"/> hands a handler in place of a version number.</summary>
public sealed class ClaimedJobTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "motif-claimed-job-" + Guid.NewGuid().ToString("N"));
    private readonly MotifDatabase _database;
    private const string Project = "project-key";

    public ClaimedJobTests()
    {
        Directory.CreateDirectory(_root);
        var locator = new ProjectLocator(Path.Combine(_root, "project.fwdata"), "project");
        _database = MotifDatabase.OpenOwned(Path.Combine(_root, "project.motif.db"), locator,
            MotifSchema.CurrentSchema, new Version(1, 0));
    }

    [Fact]
    public void ATransitionCanFollowAnotherWithoutTheCallerEverSupplyingAVersion()
    {
        var jobs = new JobRepository(_database);
        jobs.Create("job-1", Project, "dry-run", "{}", Now());
        var claim = ClaimedJob.Of(jobs, "job-1");

        claim.Transition(JobStatus.WaitingForBaseline);
        claim.Transition(JobStatus.Queued);

        Assert.Equal(JobStatus.Queued, jobs.Get("job-1")!.Status);
    }

    [Fact]
    public void AHandleStillLosesToAnotherWriterEvenThoughItNeverAsksForAVersion()
    {
        var jobs = new JobRepository(_database);
        jobs.Create("job-1", Project, "dry-run", "{}", Now());
        var running = jobs.Transition(jobs.Get("job-1")!, JobStatus.Running);

        // The handle's own snapshot is the moment it was built; it never refreshes itself.
        var claim = ClaimedJob.Of(jobs, "job-1");
        Assert.Equal(running, claim.Job);

        // Another writer finishes the job while this handle still believes it is running.
        jobs.Transition("job-1", JobStatus.Cancelled, running.Version, JobFailureCategory.Cancellation);

        // The handle must still lose: it cannot publish a Dry Run over a job somebody else already finished.
        Assert.Throws<InvalidOperationException>(() => claim.PublishDryRun("{}"));
    }

    [Fact]
    public void AReclaimedJobRejectsStageProgressFromItsOriginalClaim()
    {
        var (jobs, original, successor) = ReclaimedTrial();
        var successorProgress = new JobStageProgress("measure", 2, 3, "successor phase");
        var successorToken = successor.Job.ClaimToken;
        var beforeMatching = jobs.Get(successor.JobId)!;

        successor.PublishStageProgress(successorProgress);

        var afterMatching = jobs.Get(successor.JobId)!;
        Assert.Equal(JsonSerializer.Serialize(successorProgress, MotifJson.CreateOptions()), afterMatching.ProgressJson);
        Assert.Equal(beforeMatching.Version + 1, afterMatching.Version);
        Assert.Equal(JobStatus.Running, afterMatching.Status);
        Assert.Equal(successorToken, afterMatching.ClaimToken);

        original.PublishStageProgress(new JobStageProgress("waiting-for-parser-slot", 1, 3, "stale phase"));

        Assert.Equal(afterMatching, jobs.Get(successor.JobId));
    }

    [Fact]
    public void AReclaimedJobRejectsTrialProgressFromItsOriginalClaim()
    {
        var (jobs, original, successor) = ReclaimedTrial();
        var successorProgress = new TrialWordProgress(1, 2, "dog");
        var successorToken = successor.Job.ClaimToken;
        var beforeMatching = jobs.Get(successor.JobId)!;

        successor.PublishTrialProgress(successorProgress);

        var afterMatching = jobs.Get(successor.JobId)!;
        Assert.Equal(JsonSerializer.Serialize(successorProgress, MotifJson.CreateOptions()), afterMatching.ProgressJson);
        Assert.Equal(beforeMatching.Version + 1, afterMatching.Version);
        Assert.Equal(JobStatus.Running, afterMatching.Status);
        Assert.Equal(successorToken, afterMatching.ClaimToken);

        Assert.Throws<InvalidOperationException>(() =>
            original.PublishTrialProgress(new TrialWordProgress(2, 2, null)));
        Assert.Equal(afterMatching, jobs.Get(successor.JobId));
    }

    private (JobRepository Jobs, ClaimedJob Original, ClaimedJob Successor) ReclaimedTrial()
    {
        var jobs = new JobRepository(_database);
        var claims = new JobClaims(_database);
        var jobId = CanonicalId.Mint("job/").Value;
        var input = JsonSerializer.Serialize(new TrialJobInput("{}", null, ["cat"]), MotifJson.CreateOptions());
        jobs.Create(jobId, Project, "trial", input, Now());

        var first = claims.Claim(Project, "runner-original", Now(), TimeSpan.FromSeconds(1));
        Assert.NotNull(first);
        Assert.Equal(jobId, first!.JobId);
        Assert.False(string.IsNullOrWhiteSpace(first.ClaimToken));
        var original = new ClaimedJob(jobs, first);

        var reclaimAt = DateTimeOffset.Parse(first.LeaseUntilUtc!).AddSeconds(1);
        var reclaimed = claims.Claim(Project, "runner-successor", Stamp(reclaimAt), Lease());
        Assert.NotNull(reclaimed);
        Assert.Equal(jobId, reclaimed!.JobId);
        Assert.NotEqual(first.ClaimToken, reclaimed.ClaimToken);

        return (jobs, original, new ClaimedJob(jobs, reclaimed));
    }

    private static string Now() =>
        JobTimestamp.FormatUtc(DateTimeOffset.UtcNow);

    private static string Stamp(DateTimeOffset moment) => JobTimestamp.FormatUtc(moment);

    private static TimeSpan Lease() => TimeSpan.FromMinutes(5);

    public void Dispose()
    {
        _database.Dispose();
        try { Directory.Delete(_root, true); }
        catch (DirectoryNotFoundException) { }
        catch (IOException) { }
    }
}
