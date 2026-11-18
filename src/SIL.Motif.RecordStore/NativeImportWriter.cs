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

        var codec = new RecordCodec(content);
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
                var snapshot = new ObjectSnapshot(record, commit, isRoot: true);
                var entityId = NetworkGuidConverter.Write(record.Id);
                var body = codec.Snapshot(record);
                snapshots.Parameters["$id"].Value = NetworkGuidConverter.Write(snapshot.Id);
                snapshots.Parameters["$type"].Value = snapshot.TypeName;
                snapshots.Parameters["$body"].Value = body;
                snapshots.Parameters["$entity"].Value = entityId;
                snapshots.ExecuteNonQuery();
                changes.Parameters["$index"].Value = index;
                changes.Parameters["$entity"].Value = entityId;
                changes.Parameters["$body"].Value = RecordCodec.ImportedChange(body);
                changes.ExecuteNonQuery();
            }
            foreach (var value in content.Pending)
            {
                values.Parameters["$id"].Value = value.Id;
                values.Parameters["$data"].Value = value.Data;
                values.ExecuteNonQuery();
            }
            content.Saved();
        }
        database.ChangeTracker.Clear();
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
