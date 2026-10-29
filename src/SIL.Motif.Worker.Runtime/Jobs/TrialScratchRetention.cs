using SIL.Motif.Contract.Jobs;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Worker.Jobs;

internal static class TrialScratchRetention
{
    internal static bool RetainRequested => Environment.GetEnvironmentVariable("MOTIF_RETAIN_TRIAL_SCRATCH") == "1";

    internal static WorkerMutexOwner Lock(string directory) => WorkerMutexOwner.ForRoot(LockName(directory));

    private static string LockName(string directory) =>
        "Global\\Motif.TrialScratch." + Path.GetFileName(Path.GetFullPath(directory))[..2];

    public static void Sweep(IEnumerable<(MotifDatabase Database, string WorkspaceKey)> projects) =>
        Sweep(projects, TrialScratchPath.Root);

    internal static void Sweep(IEnumerable<(MotifDatabase Database, string WorkspaceKey)> projects, string root,
        TimeSpan? maximumAge = null, DateTime? nowUtc = null)
    {
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        var active = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (database, workspaceKey) in projects)
            foreach (var job in new JobRepository(database).ListByProjectAndKind(workspaceKey, TrialJobHandler.TrialKind))
                if (!JobStateMachine.IsTerminal(job.Status))
                    active.Add(Path.GetFileName(TrialScratchPath.ForJob(job.JobId)));

        if (!Directory.Exists(root) || (File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) return;
        var age = maximumAge ?? MaximumAge();
        var cutoff = (nowUtc ?? DateTime.UtcNow) - age;
        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            var name = Path.GetFileName(directory);
            if (name.Length != 64 || name.Any(character => !Uri.IsHexDigit(character)) || active.Contains(name)) continue;
            try
            {
                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) continue;
                using var lease = Lock(directory);
                if (!lease.TryAcquire() || Directory.GetLastWriteTimeUtc(directory) >= cutoff) continue;
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static TimeSpan MaximumAge() =>
        int.TryParse(Environment.GetEnvironmentVariable("MOTIF_TRIAL_SCRATCH_MAX_AGE_HOURS"), out var hours) && hours is > 0 and <= 87600
            ? TimeSpan.FromHours(hours) : TimeSpan.FromHours(24);
}
