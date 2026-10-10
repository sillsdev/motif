using SIL.Motif.Contract.Jobs;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Parsimony;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Worker.Jobs;

/// <summary>Coordinates cleanup and durable recovery for one already-open project.</summary>
public sealed class WorkerRecoveryCoordinator
{
    private const int MaximumReportedFailures = 32;
    private readonly WorkerRecovery _recovery;
    private readonly WorkspaceCleaner _cleaner;
    private readonly EvidenceRetentionCleaner? _artifactCleaner;
    private readonly EvidenceArtifactPublisher? _artifactPublisher;

    public WorkerRecoveryCoordinator(WorkerRecovery recovery, WorkspaceCleaner cleaner,
        EvidenceRetentionCleaner? artifactCleaner = null, EvidenceArtifactPublisher? artifactPublisher = null)
    {
        _recovery = recovery ?? throw new ArgumentNullException(nameof(recovery));
        _cleaner = cleaner ?? throw new ArgumentNullException(nameof(cleaner));
        _artifactCleaner = artifactCleaner;
        _artifactPublisher = artifactPublisher;
    }

    public StartupRecoveryResult RecoverStartup(string projectKey, DateTimeOffset now)
    {
        var cleanup = _cleaner.CleanupStartup(ProjectWorkspaceKey.StorageSegment(projectKey));
        var recovery = _recovery.RecoverInterruptedJobs(now);
        var artifactCleanup = _artifactCleaner is null || _artifactPublisher is null
            ? null : _artifactCleaner.RecoverExpiredBuilds(projectKey, _artifactPublisher);
        var retention = _artifactCleaner?.Clean(projectKey);
        return new StartupRecoveryResult(recovery, Limit(cleanup), artifactCleanup, retention);
    }

    public WorkspaceCleanupResult CleanupTerminal(string projectKey, string jobId) =>
        Limit(_cleaner.CleanupJob(ProjectWorkspaceKey.StorageSegment(projectKey), jobId));

    private static WorkspaceCleanupResult Limit(WorkspaceCleanupResult result) =>
        result.Failures.Count <= MaximumReportedFailures
            ? result
            : result with { Failures = result.Failures.Take(MaximumReportedFailures).ToArray() };
}

/// <summary>Reports the cleanup diagnostics and durable recovery for one project startup.</summary>
public sealed record StartupRecoveryResult(RecoveryResult Recovery, WorkspaceCleanupResult Cleanup,
    WorkspaceCleanupResult? ArtifactCleanup = null, EvidenceRetentionResult? EvidenceRetention = null);
