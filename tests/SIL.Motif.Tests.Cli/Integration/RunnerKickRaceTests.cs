using System.Diagnostics;
using System.Globalization;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Integration;

/// <summary>
/// Covers the kick ADR 0041 decision 5 requires: the CLI spawns a runner unconditionally after
/// enqueueing, and the race that makes that not a one-liner — a runner the CLI kicks can lose the
/// ownership mutex to one that is alive but about to exit, and must retry rather than give up, or the job
/// it just queued is stranded until the next command happens to wake one.
/// </summary>
[Collection(LcmCacheTestCollection.Name)]
public sealed class RunnerKickRaceTests : IDisposable
{
    private readonly PristineProjectFixture _projects;
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "motif-kick-" + Guid.NewGuid().ToString("N"));
    private readonly string _ownerNamespace = "motif-kick-" + Guid.NewGuid().ToString("N");

    public RunnerKickRaceTests(PristineProjectFixture projects)
    {
        _projects = projects;
        Directory.CreateDirectory(_root);
    }

    /// <summary>
    /// Stands in for "a live runner inside its final idle tick" by holding the same ownership mutex
    /// directly, rather than racing a real process's own idle timer — which cannot be made to hit a
    /// precise instant without a sleep-and-hope test. The kicked runner is a real process throughout.
    /// </summary>
    [Fact]
    public void AnEnqueueThatLandsWhileTheOwnerIsAboutToExitStillEndsWithTheJobRun()
    {
        var project = _projects.CopyProjectFile();
        using var occupying = JobRunnerHost.ForNamespace(_ownerNamespace);
        Assert.True(occupying.TryAcquireOwnership());

        // Enqueues and kicks a real runner; its first acquisition attempt is guaranteed to fail here.
        var jobId = Cli($"baseline-refresh --project \"{project}\"").Output.Trim();
        Assert.False(string.IsNullOrWhiteSpace(jobId));

        // Comfortably inside the runner's own retry window (docs/adr/0041-the-database-is-the-only-store.md).
        Thread.Sleep(500);
        occupying.Dispose();

        // Bounded by progress, not by how long a loaded machine takes to capture a Baseline.
        var claimDeadline = DateTime.UtcNow + ClaimBound;
        var finishDeadline = DateTime.UtcNow + FinishBound;
        JobRecord job;
        while (!JobStateMachine.IsTerminal((job = JobOf(project, jobId)).Status))
        {
            var now = DateTime.UtcNow;
            Assert.False(job.Status == JobStatus.Queued && now > claimDeadline,
                "No runner claimed the job within " + ClaimBound + ": " + Describe(job));
            Assert.False(job.Status == JobStatus.Running && HeartbeatAge(job, now) > StaleHeartbeat,
                "The runner that claimed the job stopped renewing it: " + Describe(job));
            Assert.True(now < finishDeadline, "The job did not finish within " + FinishBound + ": " + Describe(job));
            Thread.Sleep(50);
        }
    }

    private static readonly TimeSpan ClaimBound = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan FinishBound = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan StaleHeartbeat = TimeSpan.FromSeconds(30);

    private static JobRecord JobOf(string project, string jobId)
    {
        using var database = ProjectMotifDatabase.Open(project);
        return new JobRepository(database).Get(jobId) ?? throw new InvalidOperationException(jobId + " is gone.");
    }

    private static TimeSpan HeartbeatAge(JobRecord job, DateTime now) => job.HeartbeatUtc is { } beat
        ? now - DateTime.Parse(beat, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal)
        : TimeSpan.MaxValue;

    private static string Describe(JobRecord job) =>
        JobStatusJson.ToWire(job.Status) + ", owner " + (job.OwnerId ?? "none") + ", attempt " + job.Attempt +
        ", created " + job.CreatedUtc + ", heartbeat " + (job.HeartbeatUtc ?? "never");

    [Fact]
    public void ACapturingCallerGetsEndOfFileWithoutWaitingForTheRunnerItKicked()
    {
        var project = _projects.CopyProjectFile();
        var elapsed = Stopwatch.StartNew();

        var run = Cli($"baseline-refresh --project \"{project}\"", idleSeconds: 12);

        Assert.Equal(0, run.ExitCode);
        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(8),
            $"Reading the CLI's output took {elapsed.Elapsed}: the kicked runner held its standard handles.");
    }

    /// Runs the real CLI with the kick enabled, sharing this test's isolated root and runner namespace.
    private CliRun Cli(string arguments, int idleSeconds = 2)
    {
        var executable = BuildOutput.Cli;
        var start = new ProcessStartInfo(executable)
        {
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.Environment[RunnerOptions.RootVariable] = _root;
        start.Environment[RunnerOptions.NamespaceVariable] = _ownerNamespace;
        // The runner this kicks is nobody's to wait on, so bound how long it outlives the test.
        start.Environment[RunnerOptions.IdleVariable] = idleSeconds.ToString(CultureInfo.InvariantCulture);
        using var process = Process.Start(start)!;
        // Both pipes drain concurrently: a sequential read deadlocks past the pipe buffer.
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        // One bound over exit and both drains: a handle held open blocks a drain as surely as a hung exit.
        if (!Task.WhenAll(outputTask, errorTask, process.WaitForExitAsync()).Wait(CliBound))
        {
            try { process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            Assert.Fail("'motif " + arguments + "' did not exit and close its output within " + CliBound + ".");
        }
        return new CliRun(process.ExitCode, outputTask.Result, errorTask.Result);
    }

    private static readonly TimeSpan CliBound = TimeSpan.FromMinutes(2);

    private sealed record CliRun(int ExitCode, string Output, string Error);

    public void Dispose()
    {
        // The kicked runner is still sweeping this root; give its bounded idle timeout time to expire.
        for (var attempt = 0; attempt < 20; attempt++)
        {
            try { Directory.Delete(_root, true); return; }
            catch (DirectoryNotFoundException) { return; }
            catch (IOException) { Thread.Sleep(250); }
            catch (UnauthorizedAccessException) { Thread.Sleep(250); }
        }
    }
}
