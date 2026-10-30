using Microsoft.Data.Sqlite;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Host.Config;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Store;
using SIL.Motif.Worker.Assess;
using SIL.Motif.Worker;

namespace SIL.Motif.Commands;

/// <summary>The project whose older unreferenced Assessment evidence should be removed.</summary>
public sealed record StorePruneRequest(string ProjectPath);

/// <summary>The number of removed Assessments and project-store file sizes around reclamation.</summary>
public sealed record StorePruneResponse(int AssessmentsRemoved, long BytesBefore, long BytesAfter, bool CheckpointComplete,
    int InvocationDirectoriesRemoved = 0, int UnownedDirectoriesKept = 0, long UnownedBytesKept = 0,
    bool UnownedSizeComplete = true, string? InvocationParentPath = null);

/// <summary>Removes older unreferenced standalone Assessments and reclaims their database pages.</summary>
public static class StorePruneCommand
{
    /// <summary>Applies the project's retention setting while no parse is running.</summary>
    public static CommandOutcome<StorePruneResponse> Run(StorePruneRequest request) =>
        ProjectStoreCommand.Run(request.ProjectPath, MotifProductVersion.CurrentText, (database, project) =>
        {
            using var lease = ProjectParseLease.TryAcquire(database);
            if (lease is null) return CommandOutcome<StorePruneResponse>.Refused(ProjectParseLease.BusyRefusal());
            try
            {
                var count = new ProjectConfigurationReader().Read(project).StandaloneAssessmentCount;
                return RunHeld(database, project, lease, count);
            }
            catch (ProjectConfigurationException exception)
            {
                return CommandOutcome<StorePruneResponse>.Refused(new Refusal(
                    "config.invalid", FailureReason.Refused, exception.Message));
            }
        });

    internal static CommandOutcome<StorePruneResponse> RunHeld(MotifDatabase database, ProjectLocator project,
        ProjectParseLease lease, int keepPerKind, string? managedRoot = null,
        CancellationToken cancellationToken = default, Action<string>? progress = null)
    {
        progress ??= line => ProjectDiagnosticLog.AppendLine(project.FullFwDataPath, line);
        using var connection = database.OpenConnection();
        using var timeout = connection.CreateCommand();
        timeout.CommandText = "PRAGMA busy_timeout;";
        var previousTimeout = Convert.ToInt64(timeout.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
        try
        {
            timeout.CommandText = "PRAGMA busy_timeout = 0;";
            timeout.ExecuteNonQuery();
            var beforeCheckpoint = Checkpoint(connection);
            var before = new FileInfo(database.FullPath).Length;
            timeout.CommandText = $"PRAGMA busy_timeout = {previousTimeout};";
            timeout.ExecuteNonQuery();
            int removed;
            IReadOnlyList<string> removedInvocations;
            using (var transaction = connection.BeginTransaction())
            {
                lease.EnsureHeld(connection, transaction);
                var current = EvidenceSets.ResolveCurrent(database, project, true, connection, transaction);
                if (!current.Succeeded) return CommandOutcome<StorePruneResponse>.Refused(current.Refusal!);
                var set = current.Value!.Set;
                var roots = set is null ? [] : new[] { set.RootAssessmentId }.OfType<string>()
                    .Concat(set.ReplacementAssessmentIds).Concat(set.MeasurementIds.Values).ToArray();
                removed = AssessmentRetention.SweepStandalone(connection, transaction, lease, keepPerKind, roots, out removedInvocations,
                    cancellationToken, progress);
                transaction.Commit();
            }
            // A stopped sweep committed whole removals; the rest wait for the next prune, so skip the slow compaction.
            var stopped = cancellationToken.IsCancellationRequested;
            var ownership = WorkspaceOwnership.Bootstrap(managedRoot ?? RunnerOptions.ResolveRoot());
            var cache = new StatsCacheStore(ownership, database.FullPath);
            var reclaimed = cache.Reclaim(database, lease, removedInvocations);
            if (stopped)
                return CommandOutcome<StorePruneResponse>.Success(new StorePruneResponse(removed, before,
                    new FileInfo(database.FullPath).Length, false, reclaimed.Pruned, reclaimed.UnownedCount,
                    reclaimed.UnownedBytes, reclaimed.SizeComplete, reclaimed.Parent));
            timeout.CommandText = "PRAGMA busy_timeout = 0;";
            timeout.ExecuteNonQuery();
            var first = Checkpoint(connection);
            var vacuumed = true;
            try
            {
                using var vacuum = connection.CreateCommand();
                vacuum.CommandText = "PRAGMA incremental_vacuum;";
                vacuum.ExecuteNonQuery();
            }
            catch (SqliteException exception) when (exception.SqliteErrorCode is 5 or 6)
            {
                vacuumed = false;
            }
            var last = Checkpoint(connection);
            var after = new FileInfo(database.FullPath).Length;
            return CommandOutcome<StorePruneResponse>.Success(new StorePruneResponse(removed, before, after,
                beforeCheckpoint && first && last && vacuumed, reclaimed.Pruned, reclaimed.UnownedCount,
                reclaimed.UnownedBytes, reclaimed.SizeComplete, reclaimed.Parent));
        }
        finally
        {
            timeout.CommandText = $"PRAGMA busy_timeout = {previousTimeout};";
            timeout.ExecuteNonQuery();
        }
    }

    private static bool Checkpoint(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
        using var reader = command.ExecuteReader();
        return reader.Read() && reader.GetInt64(0) == 0;
    }
}
