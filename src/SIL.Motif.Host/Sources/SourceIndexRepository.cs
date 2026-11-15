using Microsoft.Data.Sqlite;
using SIL.Motif.Host.Store;

namespace SIL.Motif.Host.Sources;

/// <summary>The freshness and policy binding of one published Source generation.</summary>
public sealed record SourceIndexState(string SourceId, string Kind, string Generation, string SourceDigest,
    string IdentityJson, string Status, string TokenizerVersion, string AnchorVersion);

/// <summary>A Passage's locator and denominator counts; approval authority is unavailable.</summary>
public sealed record SourcePassage(string PassageId, string Locator, string ContentHash, int TotalPlaces, int EligiblePlaces);

/// <summary>A Source-local wordform identity, retaining a FieldWorks identity when available.</summary>
public sealed record SourceWordform(string WordformKey, string WritingSystem, string Normalized, string Display,
    string? FieldWorksId = null);

/// <summary>One ordered place and its optional current reading projection.</summary>
public sealed record SourceWordPlace(string PassageId, int Ordinal, string Anchor, string Verse, string Surface,
    string WordformKey, string? ReadingKey = null, string? Signature = null, string State = "unmeasured");

/// <summary>A place joined with its Source-local wordform.</summary>
public sealed record SourceIndexedPlace(SourceWordPlace Place, SourceWordform Wordform);

/// <summary>Publishes complete derived generations and reads bounded Passage pages.</summary>
public sealed class SourceIndexRepository(MotifDatabase database)
{
    /// <summary>Observes materialized page rows for bounded-read verification.</summary>
    public static readonly AsyncLocal<Action<int>?> RowsRead = new();
    /// <summary>Reads freshness without rebuilding an absent projection.</summary>
    public SourceIndexState? ReadState(string sourceId)
    {
        using var connection = database.OpenConnection();
        return ReadState(connection, null, sourceId);
    }

    /// <summary>Reads freshness in the caller's snapshot.</summary>
    public static SourceIndexState? ReadState(SqliteConnection connection, SqliteTransaction? transaction, string sourceId)
    {
        using var command = Command(connection, transaction,
            "SELECT SourceId, Kind, Generation, SourceDigest, IdentityJson, Status, TokenizerVersion, AnchorVersion " +
            "FROM SourceIndexState WHERE SourceId=$source;", sourceId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
            reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7)) : null;
    }

    /// <summary>Marks an existing projection pending; absent projections remain explicitly absent.</summary>
    public void MarkPending(string sourceId)
    {
        using var connection = database.OpenConnection();
        using var command = Command(connection, null,
            "UPDATE SourceIndexState SET Status='pending', Generation=$generation WHERE SourceId=$source;", sourceId);
        command.Parameters.AddWithValue("$generation", Guid.NewGuid().ToString("N"));
        command.ExecuteNonQuery();
    }

    /// <summary>Invalidates all Source projections in the caller's write transaction.</summary>
    public static void MarkAllPending(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE SourceIndexState SET Status='pending', Generation=lower(hex(randomblob(16)));";
        command.ExecuteNonQuery();
    }

    /// <summary>Reads a Passage by identity without scanning its places.</summary>
    public SourcePassage? ReadPassage(string sourceId, string passageId)
    {
        using var connection = database.OpenConnection();
        return ReadPassage(connection, null, sourceId, passageId);
    }

    /// <summary>Reads a Passage in the caller's snapshot.</summary>
    public static SourcePassage? ReadPassage(SqliteConnection connection, SqliteTransaction? transaction,
        string sourceId, string passageId)
    {
        using var command = Command(connection, transaction,
            "SELECT PassageId, Locator, ContentHash, TotalPlaces, EligiblePlaces FROM SourcePassages " +
            "WHERE SourceId=$source AND PassageId=$passage;", sourceId);
        command.Parameters.AddWithValue("$passage", passageId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3), reader.GetInt32(4)) : null;
    }

    /// <summary>Reads at most the requested number of places after an ordinal.</summary>
    public IReadOnlyList<SourceIndexedPlace> ReadPage(string sourceId, string passageId, int afterOrdinal, int limit)
    {
        using var connection = database.OpenConnection();
        return ReadPage(connection, null, sourceId, passageId, afterOrdinal, limit);
    }

    /// <summary>Seeks a bounded page within the caller's snapshot.</summary>
    public static IReadOnlyList<SourceIndexedPlace> ReadPage(SqliteConnection connection, SqliteTransaction? transaction,
        string sourceId, string passageId, int afterOrdinal, int limit)
    {
        if (limit is < 1 or > 201) throw new ArgumentOutOfRangeException(nameof(limit));
        using var command = Command(connection, transaction,
            "SELECT p.PassageId,p.Ordinal,p.Anchor,p.Verse,p.Surface,p.WordformKey,p.ReadingKey,p.Signature,p.State," +
            "w.WritingSystem,w.Normalized,w.Display,w.FieldWorksId FROM SourceWordPlaces p " +
            "JOIN SourceWordforms w ON w.SourceId=p.SourceId AND w.WordformKey=p.WordformKey " +
            "WHERE p.SourceId=$source AND p.PassageId=$passage AND p.Ordinal>$after ORDER BY p.Ordinal LIMIT $limit;", sourceId);
        command.Parameters.AddWithValue("$passage", passageId);
        command.Parameters.AddWithValue("$after", afterOrdinal);
        command.Parameters.AddWithValue("$limit", limit);
        using var reader = command.ExecuteReader();
        var rows = new List<SourceIndexedPlace>();
        while (reader.Read())
            rows.Add(new(new(reader.GetString(0), reader.GetInt32(1), reader.GetString(2), reader.GetString(3),
                reader.GetString(4), reader.GetString(5), Nullable(reader, 6), Nullable(reader, 7), reader.GetString(8)),
                new(reader.GetString(5), reader.GetString(9), reader.GetString(10), reader.GetString(11), Nullable(reader, 12))));
        RowsRead.Value?.Invoke(rows.Count);
        return rows;
    }

    /// <summary>Stages rows off-path, then atomically replaces the Source projection.</summary>
    /// <remarks>A null expected generation is unconditional; an empty value requires an absent projection.</remarks>
    public bool Publish(SourceIndexState state, IEnumerable<SourcePassage> passages,
        IEnumerable<SourceWordform> wordforms, IEnumerable<SourceWordPlace> places, string? expectedGeneration = null)
    {
        using var stage = SourceIndexStage.Create(state, passages, wordforms, places);
        return Publish(stage, expectedGeneration);
    }

    /// <summary>Copies an already built stage under one short publication transaction.</summary>
    public bool Publish(SourceIndexStage stage, string? expectedGeneration = null)
    {
        using var connection = database.OpenConnection();
        using (var attach = connection.CreateCommand())
        {
            attach.CommandText = "ATTACH DATABASE $path AS source_stage;";
            attach.Parameters.AddWithValue("$path", stage.Path);
            attach.ExecuteNonQuery();
        }
        try
        {
            using var transaction = connection.BeginTransaction();
            var old = ReadState(connection, transaction, stage.State.SourceId);
            if (expectedGeneration is not null && (old?.Generation ?? "") != expectedGeneration) return false;
            foreach (var table in new[] { "SourceWordPlaces", "SourcePassages", "SourceWordforms", "SourceIndexState" })
            {
                using var delete = Command(connection, transaction, $"DELETE FROM {table} WHERE SourceId=$source;", stage.State.SourceId);
                delete.ExecuteNonQuery();
            }
            foreach (var table in new[] { "SourceIndexState", "SourcePassages", "SourceWordforms", "SourceWordPlaces" })
            {
                using var copy = connection.CreateCommand();
                copy.Transaction = transaction;
                copy.CommandText = $"INSERT INTO main.{table} SELECT * FROM source_stage.{table};";
                copy.ExecuteNonQuery();
            }
            transaction.Commit();
            return true;
        }
        finally
        {
            using var detach = connection.CreateCommand();
            detach.CommandText = "DETACH DATABASE source_stage;";
            detach.ExecuteNonQuery();
        }
    }

    private static string? Nullable(SqliteDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static SqliteCommand Command(SqliteConnection connection, SqliteTransaction? transaction, string sql, string sourceId)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.Parameters.AddWithValue("$source", sourceId.ToUpperInvariant());
        return command;
    }
}
