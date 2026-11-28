using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace SIL.Motif.RecordStore;

/// <summary>What one table of a store file currently is, from a cheap read; not a verification of its rows.</summary>
internal sealed record TableState(string Table, string State, string Detail = "");

/// <summary>The snapshot, index and content digests computed again from the stored rows.</summary>
internal sealed record RescannedDigests(string Snapshot, string Index, string Content);

public sealed partial class RecordStore
{
    /// <summary>Hand-projection tables; a status names them absent until they exist.</summary>
    private static readonly string[] DerivedTables = ["HandProjectionState", "HandFacts"];

    /// <summary>
    /// Names the state of every table through its own short read-only connection, so it is safe beside a live handle.
    /// It compares the stored revisions and flags; it does not decode snapshots or rescan history (that is <see cref="VerifyAsync"/>).
    /// </summary>
    internal static IReadOnlyList<TableState> InspectTables(string path)
    {
        using var connection = new SqliteConnection(RecordSqlite.ConnectionString(path, SqliteOpenMode.ReadOnly));
        connection.Open();
        object? Scalar(string sql)
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            return command.ExecuteScalar();
        }
        var existing = new HashSet<string>(StringComparer.Ordinal);
        using (var names = connection.CreateCommand())
        {
            names.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table'";
            using var reader = names.ExecuteReader();
            while (reader.Read()) existing.Add(reader.GetString(0));
        }
        var result = new List<TableState>();
        StoreState? state = null;
        if (!existing.Contains("RecordStoreState")) result.Add(new("RecordStoreState", "absent"));
        else
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT Published, Revision, ReplayDigest, SnapshotDigest, ContentDigest, IndexDigest, IndexLimitations FROM RecordStoreState LIMIT 1
                """;
            using var reader = command.ExecuteReader();
            if (reader.Read())
                state = new StoreState
                {
                    Published = reader.GetBoolean(0), Revision = reader.GetString(1), ReplayDigest = reader.GetString(2),
                    SnapshotDigest = reader.GetString(3), ContentDigest = reader.GetString(4), IndexDigest = reader.GetString(5),
                    IndexLimitations = reader.GetString(6)
                };
            result.Add(state is null ? new("RecordStoreState", "absent", "no state row")
                : new("RecordStoreState", state.Published ? "published" : "unpublished"));
        }
        string? firstCommit = null;
        string? revision = null;
        if (existing.Contains("Commits"))
        {
            var hashes = new List<string>();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT Hash, hex(Id) FROM Commits ORDER BY rowid";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (hashes.Count == 0) firstCommit = reader.GetString(1) + "/" + reader.GetString(0);
                hashes.Add(reader.GetString(0));
            }
            hashes.Sort(StringComparer.Ordinal);
            revision = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", hashes))));
            result.Add(new("Commits", state is null || state.Revision == revision ? "current" : "revision-mismatch", $"{hashes.Count} commits"));
        }
        else result.Add(new("Commits", "absent"));
        if (!existing.Contains("RecordStoreCertificate")) result.Add(new("RecordStoreCertificate", "absent"));
        else
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT Revision, ReplayDigest, SnapshotDigest, ContentDigest, IndexDigest, CommitRows, ChangeRows, Provenance, StoreId, WriteInProgress
                FROM RecordStoreCertificate LIMIT 1
                """;
            using var reader = command.ExecuteReader();
            if (!reader.Read()) result.Add(new("RecordStoreCertificate", "absent", "no certificate row"));
            else
            {
                var certificate = new StoreCertificate
                {
                    Revision = reader.GetString(0), ReplayDigest = reader.GetString(1), SnapshotDigest = reader.GetString(2),
                    ContentDigest = reader.GetString(3), IndexDigest = reader.GetString(4), CommitRows = reader.GetString(5),
                    ChangeRows = reader.GetString(6), Provenance = reader.GetString(7), StoreId = reader.GetString(8),
                    WriteInProgress = reader.GetBoolean(9)
                };
                var identity = firstCommit is null ? null
                    : Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("motif-record-store-identity/" + firstCommit)));
                var label = certificate.WriteInProgress ? "write-in-progress"
                    : identity is null || certificate.StoreId != identity ? "other-store"
                    : state is not null && Trusted(certificate, state, identity) ? "current" : "mismatch";
                result.Add(new("RecordStoreCertificate", label, "provenance " + certificate.Provenance));
            }
        }
        if (!existing.Contains("RecordContentIdentity")) result.Add(new("RecordContentIdentity", "absent"));
        else
        {
            var identities = Convert.ToInt64(Scalar("SELECT count(*) FROM RecordContentIdentity"));
            var rows = Convert.ToInt64(Scalar("SELECT count(*) FROM RecordContent"));
            result.Add(new("RecordContentIdentity", identities == rows ? "present" : "count-mismatch", $"{identities} identities, {rows} content rows"));
        }
        result.Add(existing.Contains("IncomingReferences")
            ? new("IncomingReferences", "present", Convert.ToInt64(Scalar("SELECT count(*) FROM IncomingReferences")) + " rows")
            : new("IncomingReferences", "absent"));
        foreach (var table in new[] { "RecordHeaders", "RecordChunks" })
            result.Add(existing.Contains(table)
                ? new(table, "present", Convert.ToInt64(Scalar($"SELECT count(*) FROM {table}")) + " rows")
                : new(table, "absent"));
        if (!existing.Contains("RecordPublicationBinding")) result.Add(new("RecordPublicationBinding", "absent"));
        else
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT Revision, SnapshotDigest, ReplayDigest, ContentDigest, IndexDigest FROM RecordPublicationBinding WHERE Id = 1";
            using var reader = command.ExecuteReader();
            result.Add(!reader.Read() ? new("RecordPublicationBinding", "absent", "no binding row")
                : state is not null && reader.GetString(0) == state.Revision && reader.GetString(1) == state.SnapshotDigest &&
                    reader.GetString(2) == state.ReplayDigest && reader.GetString(3) == state.ContentDigest && reader.GetString(4) == state.IndexDigest
                    ? new("RecordPublicationBinding", "current") : new("RecordPublicationBinding", "mismatch", "it does not copy RecordStoreState"));
        }
        foreach (var table in DerivedTables)
            result.Add(existing.Contains(table) ? new(table, "present") : new(table, "absent", "not built by this format"));
        return result;
    }

    /// <summary>Computes the snapshot digest, a reference index rebuilt from the snapshots, and the content digest from scratch on this handle's current point; writes nothing.</summary>
    internal Task<RescannedDigests> RescanDigestsAsync(CancellationToken ct = default) => ReadPublishedAsync(async state =>
    {
        string? snapshot = null, index = null;
        IReadOnlyList<GenericRecord>? decoded = null;
        await AcquireAsync(state, ct, (kind, digest) => { if (kind == "snapshot") snapshot = digest; }, records => decoded = records);
        // The index digest is rebuilt from the snapshots that acquisition decoded, not read back from the stored rows.
        index = ReferenceProjection.Build(decoded!).Digest();
        var fresh = new RecordContent();
        fresh.Load(await database.Set<ContentRow>().AsNoTracking().ToArrayAsync(ct), ct);
        return new RescannedDigests(snapshot!, index!, fresh.Digest(ct));
    }, ct);
}
