using System.Globalization;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.TestFixtures;

/// <summary>
/// Waits on a durable job by whether it is still making progress, not by how long a loaded machine takes.
/// </summary>
/// <remarks>
/// A fixed wall-clock deadline fails a job that is merely slow: under a full suite a Baseline capture or a
/// Trial can take minutes while its runner renews the lease every second. What a test must catch instead is
/// a job nobody is working on. That is a row left queued for too long in one stretch, a running row whose
/// heartbeat has gone stale, or a job that never finishes inside a generous cap. Each failure names the
/// row's status, owner, attempt and heartbeat. The row is read from the project's database directly rather
/// than by starting a <c>motif jobs show</c> process for every poll.
/// </remarks>
internal sealed class JobProgress
{
    /// <summary>How long one uninterrupted stretch at <c>queued</c> may last before no runner is taking it.</summary>
    internal static readonly TimeSpan ClaimBound = TimeSpan.FromSeconds(60);

    /// <summary>How old a running row's heartbeat may be; its runner renews it every second.</summary>
    internal static readonly TimeSpan StaleHeartbeat = TimeSpan.FromSeconds(30);

    /// <summary>The overall bound, so that even a job that keeps renewing cannot hold a test forever.</summary>
    internal static readonly TimeSpan Cap = TimeSpan.FromMinutes(5);

    private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(50);

    private readonly DateTime _started = DateTime.UtcNow;
    private JobStatus? _status;
    private DateTime _statusSince;

    /// <summary>
    /// Polls <paramref name="jobId"/> until <paramref name="reached"/> holds, failing the test, named by
    /// <paramref name="step"/>, as soon as the job stops making progress.
    /// </summary>
    /// <param name="observe">Sees every row read, for a test that asserts on a state it passes through.</param>
    internal static JobRecord WaitUntil(string projectPath, string jobId, Func<JobRecord, bool> reached,
        string step, Action<JobRecord>? observe = null)
    {
        var progress = new JobProgress();
        while (true)
        {
            var job = Read(projectPath, jobId);
            observe?.Invoke(job);
            if (reached(job)) return job;
            if (progress.Stalled(job) is { } stalled) Assert.Fail(step + ": " + stalled + " (" + Describe(job) + ").");
            Thread.Sleep(Poll);
        }
    }

    /// <summary>Polls <paramref name="jobId"/> until it reaches a terminal status; see <see cref="WaitUntil"/>.</summary>
    internal static JobRecord WaitUntilFinished(string projectPath, string jobId, string step) =>
        WaitUntil(projectPath, jobId, job => JobStateMachine.IsTerminal(job.Status), step);

    /// <summary>
    /// Why <paramref name="job"/> is no longer making progress, or <see langword="null"/> while it still is.
    /// Call it with every read of the same job, in order, from one tracker.
    /// </summary>
    internal string? Stalled(JobRecord job)
    {
        var now = DateTime.UtcNow;
        if (job.Status != _status)
        {
            _status = job.Status;
            _statusSince = now;
        }
        if (job.Status == JobStatus.Queued && now - _statusSince > ClaimBound)
            return "no runner claimed the job within " + ClaimBound;
        if (job.Status == JobStatus.Running && HeartbeatAge(job, now) > StaleHeartbeat)
            return "the runner that claimed the job stopped renewing it";
        if (now - _started > Cap) return "the job did not finish within " + Cap;
        return null;
    }

    /// <summary>Reads one job's row from the database paired with <paramref name="projectPath"/>.</summary>
    internal static JobRecord Read(string projectPath, string jobId)
    {
        using var database = ProjectMotifDatabase.Open(projectPath);
        return new JobRepository(database).Get(jobId) ??
            throw new InvalidOperationException("Job '" + jobId + "' is not in the project's database.");
    }

    /// <summary>The most recently created job of <paramref name="kind"/>, or <see langword="null"/> when there is none.</summary>
    internal static string? LatestJobId(string projectPath, string kind)
    {
        using var database = ProjectMotifDatabase.Open(projectPath);
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT JobId FROM Jobs WHERE Kind = $kind ORDER BY rowid DESC LIMIT 1;";
        command.Parameters.AddWithValue("$kind", kind);
        return command.ExecuteScalar() as string;
    }

    /// <summary>The row's status, owner, attempt, creation and heartbeat, for a failure message.</summary>
    internal static string Describe(JobRecord job) =>
        job.JobId + " " + JobStatusJson.ToWire(job.Status) + ", owner " + (job.OwnerId ?? "none") +
        ", attempt " + job.Attempt + ", created " + job.CreatedUtc + ", heartbeat " + (job.HeartbeatUtc ?? "never");

    private static TimeSpan HeartbeatAge(JobRecord job, DateTime now) => job.HeartbeatUtc is { } beat
        ? now - DateTime.Parse(beat, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal)
        : TimeSpan.Zero;
}
