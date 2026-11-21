using System.Globalization;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Worker;
using SIL.Motif.Worker.Jobs;

namespace SIL.Motif.Commands;

/// <summary>What stands between a queued job and a runner: the jobs ahead of it and the runner that serves them.</summary>
/// <param name="Ahead">How many active jobs, in any Known project, are ahead of the job.</param>
/// <param name="First">The job at the front of the queue, or <see langword="null"/> when the job is itself first.</param>
/// <param name="RunnerPid">The live runner the worker root records, or <see langword="null"/> when there is none.</param>
internal sealed record QueueBlocker(int Ahead, JobQueueEntryResponse? First, int? RunnerPid)
{
    /// <summary>Reads the global queue for <paramref name="jobId"/>; <see langword="null"/> when it cannot be read.</summary>
    public static QueueBlocker? Find(string jobId, string productVersion)
    {
        try
        {
            var queue = JobCommands.ListAll(new ListActiveJobsRequest(productVersion));
            if (!queue.Succeeded) return null;
            return For(queue.Value!.Jobs, jobId, RunnerIdentity.ReadLive(RunnerOptions.ResolveRoot())?.Pid);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            Microsoft.Data.Sqlite.SqliteException or InvalidDataException)
        {
            return null;
        }
    }

    /// <summary>The blocker of <paramref name="jobId"/> in a queue already listed in claim order.</summary>
    internal static QueueBlocker For(IReadOnlyList<JobQueueEntryResponse> jobs, string jobId, int? runnerPid)
    {
        var index = -1;
        for (var position = 0; position < jobs.Count && index < 0; position++)
            if (string.Equals(jobs[position].JobId, jobId, StringComparison.Ordinal)) index = position;
        return index <= 0 ? new QueueBlocker(0, null, runnerPid) : new QueueBlocker(index, jobs[0], runnerPid);
    }

    /// <summary>Sentences naming the job in front and the runner, for a wait that has stalled.</summary>
    public string Describe()
    {
        var text = string.Empty;
        if (First is { } first)
            text += " " + Ahead.ToString(CultureInfo.InvariantCulture) + (Ahead == 1 ? " job is" : " jobs are") +
                " ahead of it in the queue, which one runner works through across every project in this worker " +
                "root; first: " + first.JobId + " " + first.Kind + " (" + JobStatusJson.ToWire(first.Status) + ") on '" +
                first.ProjectPath + "'.";
        text += RunnerPid is { } pid
            ? " The runner is pid " + pid.ToString(CultureInfo.InvariantCulture) + "; `motif worker stop` ends it."
            : " No live runner is recorded for this worker root.";
        return text;
    }

    /// <summary>Adds the blocker's values to a refusal's facts.</summary>
    public void AddFacts(IDictionary<string, string> facts)
    {
        facts["queueAhead"] = Ahead.ToString(CultureInfo.InvariantCulture);
        if (First is { } first)
        {
            facts["blockingJobId"] = first.JobId;
            facts["blockingJobKind"] = first.Kind;
            facts["blockingProjectPath"] = first.ProjectPath;
        }
        if (RunnerPid is { } pid) facts["runnerPid"] = pid.ToString(CultureInfo.InvariantCulture);
    }
}
