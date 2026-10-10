using SIL.Motif.Contract.Jobs;
using SIL.Motif.Host.Store;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Worker.Jobs;

internal static class TrialScratchRetention
{
    public static void DeleteEarlier(MotifDatabase database, string projectKey, string proposalId, string keepJobId)
    {
        var jobs = new JobRepository(database).ListByProjectAndKind(projectKey, TrialJobHandler.TrialKind);
        var keep = jobs.FirstOrDefault(job => job.JobId == keepJobId);
        if (keep is null) return;

        foreach (var job in jobs)
        {
            if (job.JobId == keepJobId || !IsEarlier(job, keep) ||
                !TryProposalId(job, out var candidateProposalId) || candidateProposalId != proposalId)
                continue;
            DeleteDirectory(TrialScratchPath.ForJob(job.JobId));
        }
    }

    public static void Sweep(IEnumerable<(MotifDatabase Database, string WorkspaceKey)> projects)
        => Sweep(projects, TrialScratchPath.Root);

    internal static void Sweep(IEnumerable<(MotifDatabase Database, string WorkspaceKey)> projects, string root)
    {
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        var retainedPaths = new HashSet<string>(StringComparer.Ordinal);
        var ownedPaths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (database, workspaceKey) in projects)
        {
            var jobs = new JobRepository(database).ListByProjectAndKind(workspaceKey, TrialJobHandler.TrialKind);
            foreach (var job in jobs) ownedPaths.Add(ScratchPath(root, job.JobId));
            var candidates = new List<(JobRecord Job, string ProposalId)>();
            foreach (var job in jobs)
            {
                if (TryProposalId(job, out var proposalId)) candidates.Add((job, proposalId));
                else if (!JobStateMachine.IsTerminal(job.Status))
                    retainedPaths.Add(Path.GetFullPath(TrialScratchPath.ForJob(job.JobId)));
            }

            foreach (var group in candidates.GroupBy(candidate => candidate.ProposalId, StringComparer.Ordinal))
            {
                var ordered = group.OrderByDescending(candidate => candidate.Job.CreatedUtc, StringComparer.Ordinal)
                    .ThenByDescending(candidate => candidate.Job.JobId, StringComparer.Ordinal).ToArray();
                var latestCompleted = ordered.FirstOrDefault(candidate => candidate.Job.Status == JobStatus.Completed);
                if (latestCompleted.Job is not null)
                    retainedPaths.Add(ScratchPath(root, latestCompleted.Job.JobId));
                foreach (var active in ordered.Where(candidate => !JobStateMachine.IsTerminal(candidate.Job.Status)))
                    retainedPaths.Add(ScratchPath(root, active.Job.JobId));
            }
        }

        if (!Directory.Exists(root)) return;
        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            // A folder no swept job names may be another installation's live copy, so it is never ours to delete.
            var full = Path.GetFullPath(directory);
            if (ownedPaths.Contains(full) && !retainedPaths.Contains(full)) DeleteDirectory(directory);
        }
        try { Directory.Delete(root, recursive: false); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static bool IsEarlier(JobRecord candidate, JobRecord reference) =>
        string.Compare(candidate.CreatedUtc, reference.CreatedUtc, StringComparison.Ordinal) < 0 ||
        candidate.CreatedUtc == reference.CreatedUtc &&
        string.Compare(candidate.JobId, reference.JobId, StringComparison.Ordinal) < 0;

    private static string ScratchPath(string root, string jobId) =>
        Path.GetFullPath(Path.Combine(root, Path.GetFileName(TrialScratchPath.ForJob(jobId))));

    private static bool TryProposalId(JobRecord job, out string proposalId)
    {
        try
        {
            proposalId = ProposalJsonParser.Parse(TrialJobInput.Parse(job.InputJson).ProposalJson)
                .ProposalId.Value;
            return true;
        }
        catch (Exception exception) when (exception is System.Text.Json.JsonException or InvalidDataException or
            ArgumentException or InvalidOperationException)
        {
            proposalId = string.Empty;
            return false;
        }
    }

    private static void DeleteDirectory(string path)
    {
        try { Directory.Delete(path, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
