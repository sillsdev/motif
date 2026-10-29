using System.Text.Json;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Worker.Jobs;

internal static class TrialScratchRetention
{
    private const string RemoveMarker = ".motif-discard-scratch";
    private const string RetainMarker = ".motif-retain-scratch";
    internal static bool RetainRequested => Environment.GetEnvironmentVariable("MOTIF_RETAIN_TRIAL_SCRATCH") == "1";

    internal static WorkerMutexOwner Lock(string directory) => WorkerMutexOwner.ForRoot(LockName(directory));

    private static string LockName(string directory) =>
        "Global\\Motif.TrialScratch." + Path.GetFileName(Path.GetFullPath(directory))[..2];

    internal static void PruneProject(MotifDatabase database, string workspaceKey, string currentJobId)
    {
        var jobs = new JobRepository(database).ListByProjectAndKind(workspaceKey, TrialJobHandler.TrialKind);
        var proposals = ProposalStatuses(database);
        var candidates = jobs.Where(job => job.JobId == currentJobId || JobStateMachine.IsTerminal(job.Status))
            .Where(job => Directory.Exists(TrialScratchPath.ForJob(job.JobId)))
            .Where(job => !IsRetained(TrialScratchPath.ForJob(job.JobId)))
            .OrderByDescending(job => job.CreatedUtc, StringComparer.Ordinal)
            .ThenByDescending(job => job.JobId, StringComparer.Ordinal).ToArray();
        foreach (var job in candidates.Where(job => IsMarkedForDeletion(TrialScratchPath.ForJob(job.JobId)) || !CanKeep(job, proposals)))
            TryDelete(TrialScratchPath.ForJob(job.JobId));
        foreach (var job in candidates.Where(job => Directory.Exists(TrialScratchPath.ForJob(job.JobId)) &&
                     !IsMarkedForDeletion(TrialScratchPath.ForJob(job.JobId)) && CanKeep(job, proposals)).Skip(MaximumCount()))
            TryDelete(TrialScratchPath.ForJob(job.JobId));
    }

    private static Dictionary<string, string> ProposalStatuses(MotifDatabase database)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT ProposalId, Status FROM Proposals;";
        using var reader = command.ExecuteReader();
        while (reader.Read()) result.Add(reader.GetString(0), reader.GetString(1));
        return result;
    }

    private static bool CanKeep(JobRecord job, IReadOnlyDictionary<string, string> proposals)
    {
        try
        {
            var id = ProposalJsonParser.Parse(TrialJobInput.Parse(job.InputJson).ProposalJson).ProposalId.Value;
            return proposals.TryGetValue(id, out var status) && status != ProposalLifecycle.ToWire(ProposalStatus.Applied);
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or
            ArgumentException or InvalidOperationException) { return true; }
    }

    internal static IReadOnlyList<string> JobsForProposal(MotifDatabase database, CanonicalId proposalId)
    {
        var jobs = new List<string>();
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT JobId, InputJson FROM Jobs WHERE Kind = $kind;";
        command.Parameters.AddWithValue("$kind", TrialJobHandler.TrialKind);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            try
            {
                var input = TrialJobInput.Parse(reader.GetString(1));
                if (ProposalJsonParser.Parse(input.ProposalJson).ProposalId == proposalId) jobs.Add(reader.GetString(0));
            }
            catch (Exception exception) when (exception is JsonException or InvalidDataException or
                ArgumentException or InvalidOperationException) { }
        }
        return jobs;
    }

    internal static void DeleteJobs(IEnumerable<string> jobIds)
    {
        if (RetainRequested) return;
        foreach (var jobId in jobIds) TryDelete(TrialScratchPath.ForJob(jobId), markIfBusy: true);
    }

    internal static void DeleteProposal(MotifDatabase database, CanonicalId proposalId)
    {
        if (RetainRequested) return;
        try { DeleteJobs(JobsForProposal(database, proposalId)); }
        catch (Microsoft.Data.Sqlite.SqliteException) { }
    }

    internal static bool IsMarkedForDeletion(string directory) => File.Exists(Path.Combine(directory, RemoveMarker));
    internal static void MarkRetained(string directory) => File.WriteAllText(Path.Combine(directory, RetainMarker), string.Empty);
    private static bool IsRetained(string directory) => File.Exists(Path.Combine(directory, RetainMarker));

    internal static void TryDelete(string directory, bool markIfBusy = false)
    {
        try
        {
            var parent = Path.GetDirectoryName(Path.GetFullPath(directory));
            if (parent is null || (File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0 ||
                !Directory.Exists(directory) || (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0 ||
                IsRetained(directory)) return;
            using var lease = Lock(directory);
            if (lease.TryAcquire()) Directory.Delete(directory, recursive: true);
            else if (markIfBusy)
            {
                File.WriteAllText(Path.Combine(directory, RemoveMarker), string.Empty);
                if (lease.TryAcquire()) Directory.Delete(directory, recursive: true);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public static void Sweep(IEnumerable<(MotifDatabase Database, string WorkspaceKey)> projects) =>
        Sweep(projects, TrialScratchPath.Root);

    internal static void Sweep(IEnumerable<(MotifDatabase Database, string WorkspaceKey)> projects, string root,
        TimeSpan? maximumAge = null, DateTime? nowUtc = null)
    {
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        if (RetainRequested || !Directory.Exists(root) || (File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) return;
        var active = new HashSet<string>(StringComparer.Ordinal);
        var known = new HashSet<string>(StringComparer.Ordinal);
        var kept = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (database, workspaceKey) in projects)
        {
            var jobs = new JobRepository(database).ListByProjectAndKind(workspaceKey, TrialJobHandler.TrialKind);
            var proposals = ProposalStatuses(database);
            foreach (var job in jobs)
            {
                var name = Path.GetFileName(TrialScratchPath.ForJob(job.JobId));
                known.Add(name);
                if (!JobStateMachine.IsTerminal(job.Status)) active.Add(name);
            }
            foreach (var job in jobs.Where(job => JobStateMachine.IsTerminal(job.Status) && CanKeep(job, proposals))
                         .Where(job => Directory.Exists(Path.Combine(root, Path.GetFileName(TrialScratchPath.ForJob(job.JobId)))))
                         .Where(job => !IsRetained(Path.Combine(root, Path.GetFileName(TrialScratchPath.ForJob(job.JobId)))))
                         .OrderByDescending(job => job.CreatedUtc, StringComparer.Ordinal)
                         .ThenByDescending(job => job.JobId, StringComparer.Ordinal).Take(MaximumCount()))
                kept.Add(Path.GetFileName(TrialScratchPath.ForJob(job.JobId)));
        }

        var cutoff = (nowUtc ?? DateTime.UtcNow) - (maximumAge ?? MaximumAge());
        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            var name = Path.GetFileName(directory);
            if (name.Length != 64 || name.Any(character => !Uri.IsHexDigit(character)) || active.Contains(name)) continue;
            try
            {
                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) continue;
                if (IsRetained(directory)) continue;
                if (IsMarkedForDeletion(directory) || (!kept.Contains(name) &&
                    (known.Contains(name) || Directory.GetLastWriteTimeUtc(directory) < cutoff))) TryDelete(directory);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static int MaximumCount() =>
        int.TryParse(Environment.GetEnvironmentVariable("MOTIF_TRIAL_SCRATCH_MAX_COUNT"), out var maximum) && maximum > 0
            ? maximum : 3;

    private static TimeSpan MaximumAge() =>
        int.TryParse(Environment.GetEnvironmentVariable("MOTIF_TRIAL_SCRATCH_MAX_AGE_HOURS"), out var hours) && hours is > 0 and <= 87600
            ? TimeSpan.FromHours(hours) : TimeSpan.FromHours(24);
}
