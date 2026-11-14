using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Assess;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Worker.Jobs;

/// <summary>
/// Releases the Assessments, run directories and source copies of Trials a newer completed Trial of the same
/// Proposal has replaced (ADR 0113). The latest Trial is never touched, so an un-applied Proposal keeps what its
/// verdict, Apply and the gates read.
/// </summary>
internal static class SupersededTrialRetention
{
    /// <summary>One hour covers clock and publish skew for a source another writer has not yet recorded.</summary>
    internal static readonly TimeSpan SourceAge = TimeSpan.FromHours(1);

    /// <summary>
    /// Runs while the caller holds the project's parse lease, after a Trial committed. Rows go in one transaction;
    /// run directories go after it, so a failure leaves the store consistent and the next Trial or prune retries.
    /// </summary>
    internal static (int Assessments, int Runs) Run(MotifDatabase database, ProjectParseLease lease, string workerRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(lease);
        int removed;
        IReadOnlyList<string> invocations;
        using (var connection = database.OpenConnection())
        using (var transaction = connection.BeginTransaction())
        {
            removed = AssessmentRetention.SweepSupersededTrials(connection, transaction, lease, out invocations,
                cancellationToken);
            transaction.Commit();
        }
        if (removed == 0) return (0, 0);
        var cache = new StatsCacheStore(WorkspaceOwnership.Bootstrap(workerRoot), database.FullPath);
        return (removed, cache.Reclaim(database, lease, invocations).Pruned);
    }

    /// <summary>
    /// Removes source copies nothing references. It reads every Known project's store, so the caller runs it after
    /// releasing the project's parse lease.
    /// </summary>
    internal static void SweepSources(MotifDatabase database, string workerRoot, CancellationToken cancellationToken) =>
        UserDataSweep.SweepAfterRelease(workerRoot, database.FullPath, SourceAge, cancellationToken);
}
