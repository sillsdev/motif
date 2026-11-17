using System.Globalization;
using Microsoft.Data.Sqlite;
using SIL.Motif.Contract.Projects;

namespace SIL.Motif.Host.Store;

/// <summary>What a Motif store records about the project it is registered to, read before it is pointed elsewhere.</summary>
/// <param name="Registered">The project path and name identity the store is registered to.</param>
/// <param name="BaselineProjectGuids">The LibLCM project GUIDs of the Baselines the store holds; empty when it holds none.</param>
public sealed record StoreRegistration(ProjectLocator Registered, IReadOnlyList<string> BaselineProjectGuids);

/// <summary>The path change <see cref="MotifDatabase.Relocate"/> made, or <c>Changed = false</c> when the store already pointed there.</summary>
public sealed record StoreRelocationResult(string OldFwDataPath, string NewFwDataPath, DateTimeOffset RelocatedUtc, bool Changed);

/// <summary>A relocation the store or the caller's check refused, with the refusal code to report.</summary>
public sealed class StoreRelocationRefusedException(string code, string message) : Exception(message)
{
    /// <summary>The refusal code the command reports.</summary>
    public string Code { get; } = code;
}

public sealed partial class MotifDatabase
{
    /// <summary>
    /// Points the store at <paramref name="newProject"/> and appends a <c>StoreRelocations</c> row naming the old path,
    /// the new path and the time, in one transaction under the creation lock and an exclusive use lease.
    /// </summary>
    /// <param name="path">The sibling Motif database path beside the new project.</param>
    /// <param name="newProject">The project the store should serve from now on.</param>
    /// <param name="approve">Throws <see cref="StoreRelocationRefusedException"/> when the store does not belong to that project.</param>
    /// <param name="ownershipPatience">Maximum wait for the creation lock; defaults to 30 seconds.</param>
    /// <param name="timeProvider">The clock for the recorded time, or <see langword="null"/> for system time.</param>
    /// <exception cref="MotifStoreLockException">Another Motif process holds the store open.</exception>
    public static StoreRelocationResult Relocate(string path, ProjectLocator newProject,
        Action<StoreRegistration> approve, TimeSpan? ownershipPatience = null, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(approve);
        var descriptor = Describe(path, newProject, MotifSchema.CurrentSchema, MotifProductVersion.Current);
        return MotifSqliteStore.WithExclusiveAccess(path, descriptor, ownershipPatience, (connection, transaction, schema) =>
        {
            var (stored, _) = MotifSchema.ReadMetadata(connection, transaction);
            approve(new StoreRegistration(stored, ReadBaselineGuids(connection, transaction)));
            var now = timeProvider?.GetUtcNow() ?? DateTimeOffset.UtcNow;
            if (StringComparer.Ordinal.Equals(stored.FullFwDataPath, newProject.FullFwDataPath))
                return new StoreRelocationResult(stored.FullFwDataPath, newProject.FullFwDataPath, now, false);

            if (schema < MotifSchema.CurrentSchema) Execute(connection, transaction, MotifSchema.StoreRelocationDdl);
            Execute(connection, transaction, "UPDATE MotifMetadata SET FullFwDataPath = $new WHERE Id = 1;",
                ("$new", newProject.FullFwDataPath));
            // A Baseline records the file it was captured from; none of them was captured from the new path.
            if (TableExists(connection, transaction, "Baselines"))
                Execute(connection, transaction, "UPDATE Baselines SET FwDataPath = $new WHERE FwDataPath = $old;",
                    ("$new", newProject.FullFwDataPath), ("$old", stored.FullFwDataPath));
            Execute(connection, transaction,
                "INSERT INTO StoreRelocations (OldFwDataPath, NewFwDataPath, RelocatedUtc) VALUES ($old, $new, $at);",
                ("$old", stored.FullFwDataPath), ("$new", newProject.FullFwDataPath),
                ("$at", now.ToString("O", CultureInfo.InvariantCulture)));
            return new StoreRelocationResult(stored.FullFwDataPath, newProject.FullFwDataPath, now, true);
        });
    }

    private static bool TableExists(SqliteConnection connection, SqliteTransaction transaction, string table)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name;";
        command.Parameters.AddWithValue("$name", table);
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture) != 0;
    }

    private static IReadOnlyList<string> ReadBaselineGuids(SqliteConnection connection, SqliteTransaction transaction)
    {
        if (!TableExists(connection, transaction, "Baselines")) return [];
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT DISTINCT ProjectIdentity FROM Baselines;";
        var guids = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read()) guids.Add(reader.GetString(0));
        return guids;
    }

    private static void Execute(SqliteConnection connection, SqliteTransaction transaction, string sql,
        params (string Name, object Value)[]? parameters)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in parameters ?? []) command.Parameters.AddWithValue(name, value);
        command.ExecuteNonQuery();
    }
}
