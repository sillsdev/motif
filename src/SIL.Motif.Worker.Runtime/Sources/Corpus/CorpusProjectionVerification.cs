using System.Text.Json;
using Microsoft.Data.Sqlite;
using SIL.Motif.Worker.Sources.Adapter;

namespace SIL.Motif.Worker.Sources;

internal static class CorpusProjectionVerification
{
    public static IReadOnlyList<CorpusDocumentVersion>? ReadDeltaBase(SqliteConnection connection, CorpusSourceSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        using var transaction = connection.BeginTransaction();
        var documents = snapshot.DocumentVersions(connection, transaction, cancellationToken);
        if (GenerationMatches(connection, transaction, snapshot, documents, cancellationToken)) return documents;

        Check(connection, transaction, snapshot, cancellationToken);
        transaction.Commit();
        return null;
    }

    public static bool Check(SqliteConnection connection, SqliteTransaction transaction, CorpusSourceSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var id = snapshot.Stamp.Id;
        var badShards = FindBadShardWeights(connection, transaction, id, cancellationToken);
        var badDigests = FindBadShardDigests(connection, transaction, """
            SELECT DISTINCT ShardId FROM IndexedCorpusDocuments WHERE Generation=$id
            """, cancellationToken, ("$id", id));
        if (snapshot.Binding.Private && badDigests.Count > 0)
        {
            // A replaced opaque key may also break witnesses in other shards that use the same FormId.
            badDigests.UnionWith(FindBadShardDigests(connection, transaction, """
                SELECT DISTINCT o.ShardId FROM IndexedCorpusOccurrences o
                JOIN (
                    SELECT DISTINCT FormId FROM IndexedCorpusOccurrences
                    WHERE ShardId IN (SELECT value FROM json_each($shards))
                ) f ON o.FormId=f.FormId
                WHERE o.ShardId NOT IN (SELECT ShardId FROM IndexedCorpusDocuments WHERE Generation=$id)
                """, cancellationToken, ("$id", id), ("$shards", JsonSerializer.Serialize(badDigests))));
        }
        badShards.UnionWith(badDigests);
        var (badForms, incompatibleShards) = CheckForms(connection, transaction, snapshot, cancellationToken);
        badShards.UnionWith(incompatibleShards);
        if (badShards.Count == 0 && badForms.Count == 0 && snapshot.Freshness != CorpusFreshness.InvalidProjection &&
            GenerationMatches(connection, transaction, snapshot,
                snapshot.DocumentVersions(connection, transaction, cancellationToken), cancellationToken))
            return true;

        Invalidate(connection, transaction, id, badShards, badForms);
        return false;
    }

    public static void Invalidate(SqliteConnection connection, SqliteTransaction transaction, string? generation,
        IReadOnlyCollection<long> badShards, IReadOnlyCollection<long> badForms)
    {
        using var invalidate = CorpusSql.Command(connection, transaction, """
            WITH badForms AS (
                SELECT value AS Id FROM json_each($forms)
            ), badShards AS (
                SELECT value AS Id FROM json_each($shards)
                UNION SELECT ShardId FROM IndexedCorpusOccurrences WHERE FormId IN badForms
                UNION SELECT ShardId FROM IndexedCorpusShardWeights WHERE FormId IN badForms
            )
            UPDATE IndexedCorpusGenerations SET ProjectionValid=0 WHERE Published=1 AND Id IN (
                SELECT $id
                UNION SELECT Generation FROM IndexedCorpusDocuments WHERE ShardId IN badShards
                UNION SELECT Generation FROM IndexedCorpusWeights WHERE FormId IN badForms
            );
            """, ("$id", generation), ("$shards", JsonSerializer.Serialize(badShards)),
            ("$forms", JsonSerializer.Serialize(badForms)));
        invalidate.ExecuteNonQuery();
        using var heads = CorpusSql.Command(connection, transaction, """
            UPDATE IndexedCorpusHeads SET Freshness=$invalid
            WHERE Freshness!=$invalid AND Generation IN (SELECT Id FROM IndexedCorpusGenerations WHERE ProjectionValid=0);
            """, ("$invalid", (int)CorpusFreshness.InvalidProjection));
        heads.ExecuteNonQuery();
    }

    private static bool GenerationMatches(SqliteConnection connection, SqliteTransaction transaction,
        CorpusSourceSnapshot snapshot, IReadOnlyList<CorpusDocumentVersion> documents, CancellationToken cancellationToken)
    {
        if (documents.Count != snapshot.DocumentCount ||
            CorpusSourceIndex.DocumentDigest(documents) != snapshot.Stamp.DocumentDigest ||
            documents.Select(document => document.ShardId).Distinct().Count() != documents.Count ||
            documents.Sum(document => document.Tokens) != snapshot.TokenCount) return false;

        cancellationToken.ThrowIfCancellationRequested();
        using var check = CorpusSql.Command(connection, transaction, """
            WITH combined AS (
                SELECT w.FormId,SUM(w.Frequency) AS Frequency
                FROM IndexedCorpusDocuments d JOIN IndexedCorpusShardWeights w ON w.ShardId=d.ShardId
                WHERE d.Generation=$id GROUP BY w.FormId
            ), stored AS (
                SELECT FormId,Frequency FROM IndexedCorpusWeights WHERE Generation=$id
            )
            SELECT (SELECT COUNT(*) FROM stored)=$types AND
                NOT EXISTS (SELECT * FROM combined EXCEPT SELECT * FROM stored) AND
                NOT EXISTS (SELECT * FROM stored EXCEPT SELECT * FROM combined) AND
                NOT EXISTS (
                    SELECT 1 FROM IndexedCorpusDocuments d
                    WHERE d.Generation=$id AND d.Tokens != COALESCE(
                        (SELECT SUM(Frequency) FROM IndexedCorpusShardWeights w WHERE w.ShardId=d.ShardId),0)
                );
            """, ("$id", snapshot.Stamp.Id), ("$types", snapshot.TypeCount));
        return (long)check.ExecuteScalar()! == 1;
    }

    private static HashSet<long> FindBadShardWeights(SqliteConnection connection, SqliteTransaction transaction,
        string generation, CancellationToken cancellationToken)
    {
        using var check = CorpusSql.Command(connection, transaction, """
            WITH actual AS (
                SELECT o.ShardId,o.FormId,COUNT(*) AS Frequency
                FROM IndexedCorpusOccurrences o
                WHERE o.ShardId IN (SELECT ShardId FROM IndexedCorpusDocuments WHERE Generation=$id)
                GROUP BY o.ShardId,o.FormId
            ), stored AS (
                SELECT w.ShardId,w.FormId,w.Frequency FROM IndexedCorpusShardWeights w
                WHERE w.ShardId IN (SELECT ShardId FROM IndexedCorpusDocuments WHERE Generation=$id)
            ), missing AS (
                SELECT * FROM actual EXCEPT SELECT * FROM stored
            ), unexpected AS (
                SELECT * FROM stored EXCEPT SELECT * FROM actual
            )
            SELECT ShardId FROM missing UNION SELECT ShardId FROM unexpected;
            """, ("$id", generation));
        using var rows = check.ExecuteReader();
        var badShards = new HashSet<long>();
        while (rows.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            badShards.Add(rows.GetInt64(0));
        }
        return badShards;
    }

    private static (HashSet<long> BadForms, HashSet<long> IncompatibleShards) CheckForms(SqliteConnection connection,
        SqliteTransaction transaction, CorpusSourceSnapshot snapshot, CancellationToken cancellationToken)
    {
        using var forms = CorpusSql.Command(connection, transaction, """
            WITH scopedForms AS (
                SELECT w.ShardId,w.FormId FROM IndexedCorpusShardWeights w
                WHERE w.ShardId IN (SELECT ShardId FROM IndexedCorpusDocuments WHERE Generation=$id)
                UNION
                SELECT d.ShardId,o.FormId FROM IndexedCorpusDocuments d
                JOIN IndexedCorpusOccurrences o ON o.ShardId=d.ShardId WHERE d.Generation=$id
            )
            SELECT w.ShardId,w.FormId,f.FormKey,f.WritingSystem,f.FormNfd FROM scopedForms w
            LEFT JOIN IndexedCorpusForms f ON f.Id=w.FormId ORDER BY w.FormId;
            """, ("$id", snapshot.Stamp.Id));
        using var rows = forms.ExecuteReader();
        var badForms = new HashSet<long>();
        var incompatibleShards = new HashSet<long>();
        long? form = null;
        var compatible = true;
        while (rows.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var id = rows.GetInt64(1);
            if (id != form)
            {
                form = id;
                var key = rows.OptionalString(2);
                var ws = rows.OptionalString(3);
                var nfd = rows.OptionalString(4);
                if (key is null || ws is null ||
                    (nfd is null ? !CorpusContent.IsHash(key) : CorpusContent.Digest(new WordformKey(ws, nfd)) != key))
                    badForms.Add(id);
                compatible = ws == snapshot.Binding.WritingSystem && snapshot.Binding.Private == (nfd is null);
            }
            if (!compatible) incompatibleShards.Add(rows.GetInt64(0));
        }
        return (badForms, incompatibleShards);
    }

    private static HashSet<long> FindBadShardDigests(SqliteConnection connection, SqliteTransaction transaction,
        string shardScope, CancellationToken cancellationToken, params (string Name, object? Value)[] parameters)
    {
        using var command = CorpusSql.Command(connection, transaction, $"""
            WITH scope AS ({shardScope})
            SELECT d.ShardId,s.Digest,o.Ordinal,o.FormId,o.Segment,o.Word,o.ByteOffset,o.ByteLength,o.Verse,o.Surface,f.FormKey
            FROM scope d LEFT JOIN IndexedCorpusShards s ON s.Id=d.ShardId
            LEFT JOIN IndexedCorpusOccurrences o ON o.ShardId=d.ShardId
            LEFT JOIN IndexedCorpusForms f ON f.Id=o.FormId
            ORDER BY d.ShardId,o.Ordinal;
            """, parameters);
        using var rows = command.ExecuteReader();
        using var digest = new CorpusShardDigest();
        var badShards = new HashSet<long>();
        long? shard = null;
        string? expected = null;
        while (rows.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var id = rows.GetInt64(0);
            if (id != shard)
            {
                if (shard is { } prior && digest.Complete() != expected) badShards.Add(prior);
                shard = id;
                expected = rows.OptionalString(1);
            }
            if (!rows.IsDBNull(2))
                digest.Append(rows.GetInt64(2), rows.GetInt64(3), rows.OptionalString(10), rows.GetInt32(4), rows.GetInt32(5),
                    rows.IsDBNull(6) ? null : rows.GetInt64(6), rows.IsDBNull(7) ? null : rows.GetInt32(7),
                    rows.OptionalString(8), rows.OptionalString(9));
        }
        if (shard is { } last && digest.Complete() != expected) badShards.Add(last);
        return badShards;
    }
}
