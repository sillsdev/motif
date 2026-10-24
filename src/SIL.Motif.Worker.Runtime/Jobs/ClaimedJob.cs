using SIL.Motif.Contract.Jobs;
using System.Text.Json;
using SIL.Motif.Contract;

namespace SIL.Motif.Worker.Jobs;

/// <summary>
/// One claimed job row, handed to a <see cref="JobRunnerLoop.Handler"/> to drive its in-flight work.
/// </summary>
/// <remarks>
/// Progress writes use the claim token captured when the handle was created. A later read supplies a
/// compare-and-swap version and payload, never a replacement owner token.
/// </remarks>
public sealed class ClaimedJob
{
    private readonly JobRepository _jobs;
    private readonly string _jobId;
    private readonly string? _capturedClaimToken;
    private readonly object _progressGate = new();
    private long _progressGeneration;

    internal ClaimedJob(JobRepository jobs, JobRecord claimed)
    {
        _jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
        ArgumentNullException.ThrowIfNull(claimed);
        _jobId = claimed.JobId;
        _capturedClaimToken = claimed.ClaimToken;
        Job = claimed;
    }

    internal SIL.Motif.Host.Store.MotifDatabase Database => _jobs.Database;

    internal string? CapturedClaimToken => _capturedClaimToken;

    /// <summary>This job's id, stable for the life of the claim.</summary>
    public string JobId => _jobId;

    /// <summary>The row's state as of the last read this handle itself performed.</summary>
    public JobRecord Job { get; private set; }

    /// <summary>Moves this job to a non-terminal state, against whatever version the row now holds.</summary>
    public JobRecord Transition(JobStatus next, string? resultJson = null)
    {
        Job = _jobs.Transition(JobId, next, resultJson);
        return Job;
    }

    /// <summary>Moves this job to a terminal state carrying a failure category, against whatever version the row now holds.</summary>
    public JobRecord Transition(JobStatus next, JobFailureCategory category, string? resultJson = null)
    {
        var before = _jobs.Get(JobId) ?? throw new InvalidOperationException("The claimed job no longer exists.");
        Job = _jobs.Transition(JobId, next, before.Version, category, resultJson);
        return Job;
    }

    /// <summary>Records this job's Dry Run, against whatever version the row now holds.</summary>
    public JobRecord PublishDryRun(string dryRunJson)
    {
        var before = _jobs.Get(JobId) ?? throw new InvalidOperationException("The claimed job no longer exists.");
        Job = _jobs.PublishDryRun(JobId, dryRunJson, before.Version);
        return Job;
    }

    /// <summary>Records word progress while this handle still owns the active row.</summary>
    /// <remarks>Pinned by <c>AReclaimedJobRejectsTrialProgressFromItsOriginalClaim</c>.</remarks>
    public JobRecord PublishTrialProgress(TrialWordProgress progress)
    {
        lock (_progressGate)
        {
            _progressGeneration = checked(_progressGeneration + 1);
            return PublishProgressLocked(JsonSerializer.Serialize(progress, MotifJson.CreateOptions()));
        }
    }

    /// <summary>Records a stage while this handle still owns the active row.</summary>
    /// <remarks>Pinned by <c>AReclaimedJobRejectsStageProgressFromItsOriginalClaim</c>.</remarks>
    public void PublishStageProgress(JobStageProgress progress)
    {
        lock (_progressGate)
        {
            _progressGeneration = checked(_progressGeneration + 1);
            try
            {
                _ = PublishProgressLocked(JsonSerializer.Serialize(progress, MotifJson.CreateOptions()));
            }
            catch (Exception exception) when (exception is InvalidOperationException or OperationCanceledException)
            {
                // Progress is advisory when a row change prevents this write.
            }
        }
    }

    /// <summary>Creates a parser-capacity scope that may temporarily replace this handle's progress.</summary>
    /// <param name="phase">The real stage and step count this invocation is performing.</param>
    /// <returns>A scope that writes only while this handle's progress generation remains current.</returns>
    internal ParserWaitProgress ObserveParserWait(JobStageProgress phase) => new(this, phase);

    /// <summary>Invalidates earlier scopes and returns the new local progress generation.</summary>
    /// <returns>The generation an invocation must retain for its later compare-and-swap writes.</returns>
    internal long BeginProgressScope()
    {
        lock (_progressGate) return _progressGeneration = checked(_progressGeneration + 1);
    }

    /// <summary>Writes a parser wait only if the local scope and captured database row still match.</summary>
    /// <param name="generation">The local generation captured by the invocation.</param>
    /// <param name="phase">The real stage and step count to display while waiting.</param>
    /// <returns>The exact saved Version, prior payload, and wait payload, or null when rejected.</returns>
    internal (long Version, string? PreviousProgressJson, string WaitProgressJson)? TryPublishParserWait(
        long generation, JobStageProgress phase)
    {
        lock (_progressGate)
        {
            if (generation != _progressGeneration) return null;
            var before = _jobs.Get(_jobId);
            if (before is null) return null;
            var detail = phase.Detail is null || StringComparer.Ordinal.Equals(phase.Detail, phase.Stage)
                ? phase.Stage : phase.Stage + ": " + phase.Detail;
            var wait = new JobStageProgress("waiting-for-parser-slot", phase.Step, phase.Steps, detail);
            var waitJson = JsonSerializer.Serialize(wait, MotifJson.CreateOptions());
            var changed = _jobs.TryUpdateClaimProgress(_jobId, _capturedClaimToken, before.Version,
                before.ProgressJson, waitJson);
            if (changed is null) return null;
            Job = changed;
            return (changed.Version, before.ProgressJson, waitJson);
        }
    }

    /// <summary>Restores a parser wait only at the exact row version written by that wait.</summary>
    /// <param name="generation">The local generation that published the wait.</param>
    /// <param name="waitVersion">The Version returned by the successful wait write.</param>
    /// <param name="waitProgressJson">The exact wait payload that was written.</param>
    /// <param name="previousProgressJson">The displaced payload, including SQL null.</param>
    /// <returns>True when the compare-and-swap restored the payload.</returns>
    internal bool TryRestoreParserWait(long generation, long waitVersion, string waitProgressJson,
        string? previousProgressJson)
    {
        lock (_progressGate)
        {
            if (generation != _progressGeneration) return false;
            var changed = _jobs.TryUpdateClaimProgress(_jobId, _capturedClaimToken, waitVersion,
                waitProgressJson, previousProgressJson);
            if (changed is null) return false;
            Job = changed;
            return true;
        }
    }

    private JobRecord PublishProgressLocked(string progressJson)
    {
        var before = _jobs.Get(_jobId) ?? throw new InvalidOperationException("The claimed job no longer exists.");
        var changed = _jobs.TryUpdateClaimProgress(_jobId, _capturedClaimToken, before.Version,
            before.ProgressJson, progressJson);
        if (changed is null)
        {
            var current = _jobs.Get(JobId);
            if (!string.IsNullOrWhiteSpace(_capturedClaimToken) && current?.CancellationRequested == true &&
                string.Equals(current.ClaimToken, _capturedClaimToken, StringComparison.Ordinal))
                throw new OperationCanceledException("The job was cancelled before progress could be published.");
            throw new InvalidOperationException("The claimed job no longer owns the active progress row.");
        }
        Job = changed;
        return changed;
    }

    /// <summary>Wraps the job named by <paramref name="jobId"/> as it reads right now.</summary>
    /// <remarks>For a caller that only has an id, not an already-read row — a real claim always has the row.</remarks>
    internal static ClaimedJob Of(JobRepository jobs, string jobId) =>
        new(jobs, jobs.Get(jobId) ?? throw new KeyNotFoundException($"Job '{jobId}' was not found."));
}
