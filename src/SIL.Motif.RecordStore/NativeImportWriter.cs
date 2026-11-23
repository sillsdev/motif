using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SIL.Harmony;
using SIL.Harmony.Core;
using SIL.Harmony.Db;

namespace SIL.Motif.RecordStore;

internal sealed class NativeImportWriter(RecordDatabase database, RecordContent content)
{
    /// <summary>Test seam: receives the store path inside the open import transaction after each commit's rows.</summary>
    internal static Action<string>? BatchWritten;
    /// <summary>Test seam: receives the store path after the indexes are rebuilt, before the transaction commits.</summary>
    internal static Action<string>? IndexesRebuilt;

    public async Task WriteAsync(IReadOnlyList<GenericRecord> records, Guid clientId, IHybridDateTimeProvider clock)
    {
        var parent = "0000";
        var batches = records.Chunk(RecordStore.MaximumChangesPerCommit)
            .Select(records =>
            {
                var commit = CreateCommit(clientId, clock.GetDateTime(), parent);
                parent = commit.Hash;
                return (Records: records, Commit: commit);
            })
            .ToArray();
        database.AddRange(batches.Select(batch => batch.Commit));
        await database.SaveChangesAsync();
        // Each entity is created once, so its root snapshot suffices at every import commit boundary.
        await database.Database.ExecuteSqlRawAsync("UPDATE Commits SET IsSnapshotCheckpoint = 1");

        // Bulk rows go in with no secondary index to maintain; this transaction rebuilds each from sorted data.
        var indexes = Query("SELECT sql FROM sqlite_master WHERE type = 'index' AND tbl_name = 'Snapshots' AND sql IS NOT NULL");
        foreach (var index in Query("SELECT name FROM sqlite_master WHERE type = 'index' AND tbl_name = 'Snapshots' AND sql IS NOT NULL"))
            Execute($"DROP INDEX \"{index}\"");
        // One type name serves the whole model. Snapshot ids are time ordered so their index appends.
        var typeName = records.Count == 0 ? "" : new ObjectSnapshot(records[0], batches[0].Commit, isRoot: true).TypeName;
        var codec = new RecordCodec(content, retainValues: false);
        using var changes = Prepare("""
            INSERT INTO ChangeEntities ("Index", CommitId, EntityId, "Change", AuthoredModelVersion)
            VALUES ($index, $commit, $entity, $body, $version)
            """, ("$index", SqliteType.Integer), ("$commit", SqliteType.Blob), ("$entity", SqliteType.Blob),
            ("$body", SqliteType.Blob), ("$version", SqliteType.Integer));
        changes.Parameters["$version"].Value = RecordStore.CompleteRecordModelVersion;
        using var snapshots = Prepare("""
            INSERT INTO Snapshots (Id, TypeName, Entity, "References", EntityId, EntityIsDeleted, CommitId, IsRoot)
            VALUES ($id, $type, $body, '[]', $entity, 0, $commit, 1)
            """, ("$id", SqliteType.Blob), ("$type", SqliteType.Text), ("$body", SqliteType.Blob),
            ("$entity", SqliteType.Blob), ("$commit", SqliteType.Blob));
        using var values = Prepare("INSERT INTO RecordContent (Id, Data) VALUES ($id, $data)",
            ("$id", SqliteType.Integer), ("$data", SqliteType.Blob));
        foreach (var (batch, commit) in batches)
        {
            var commitId = NetworkGuidConverter.Write(commit.Id);
            changes.Parameters["$commit"].Value = commitId;
            snapshots.Parameters["$commit"].Value = commitId;
            for (var index = 0; index < batch.Length; index++)
            {
                var record = batch[index];
                var entityId = NetworkGuidConverter.Write(record.Id);
                var body = codec.Snapshot(record);
                snapshots.Parameters["$id"].Value = NetworkGuidConverter.Write(Guid.CreateVersion7());
                snapshots.Parameters["$type"].Value = typeName;
                snapshots.Parameters["$body"].Value = body;
                snapshots.Parameters["$entity"].Value = entityId;
                var changeBody = RecordCodec.ImportedChange(body);
                snapshots.ExecuteNonQuery();
                changes.Parameters["$index"].Value = index;
                changes.Parameters["$entity"].Value = entityId;
                changes.Parameters["$body"].Value = changeBody;
                changes.ExecuteNonQuery();
            }
            foreach (var value in content.Pending)
            {
                values.Parameters["$id"].Value = value.Id;
                values.Parameters["$data"].Value = value.Data;
                values.ExecuteNonQuery();
            }
            if (content.PersistIdentities)
                content.WriteIdentities((SqliteConnection)database.Database.GetDbConnection(),
                    (SqliteTransaction)database.Database.CurrentTransaction!.GetDbTransaction());
            content.Saved();
            BatchWritten?.Invoke(database.Database.GetDbConnection().DataSource);
        }
        foreach (var sql in indexes) Execute(sql);
        IndexesRebuilt?.Invoke(database.Database.GetDbConnection().DataSource);
        database.ChangeTracker.Clear();
    }

    private List<string> Query(string sql)
    {
        using var command = Command(sql);
        using var reader = command.ExecuteReader();
        var rows = new List<string>();
        while (reader.Read()) rows.Add(reader.GetString(0));
        return rows;
    }

    private void Execute(string sql)
    {
        using var command = Command(sql);
        command.ExecuteNonQuery();
    }

    private SqliteCommand Command(string sql)
    {
        var connection = (SqliteConnection)database.Database.GetDbConnection();
        var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)database.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText = sql;
        return command;
    }

    private static Commit CreateCommit(Guid clientId, HybridDateTime time, string parent)
    {
        // Harmony exposes the commit constructor through its JSON contract.
        var commit = JsonSerializer.Deserialize<Commit>(JsonSerializer.SerializeToUtf8Bytes(new
        {
            Id = Guid.NewGuid(), ClientId = clientId, HybridDateTime = time
        }))!;
        commit.SetParentHash(parent);
        return commit;
    }

    private SqliteCommand Prepare(string sql, params (string Name, SqliteType Type)[] parameters)
    {
        var connection = (SqliteConnection)database.Database.GetDbConnection();
        var command = connection.CreateCommand();
        try
        {
            command.Transaction = (SqliteTransaction)database.Database.CurrentTransaction!.GetDbTransaction();
            command.CommandText = sql;
            foreach (var (name, type) in parameters) command.Parameters.Add(name, type);
            command.Prepare();
            return command;
        }
        catch
        {
            command.Dispose();
            throw;
        }
    }
}
