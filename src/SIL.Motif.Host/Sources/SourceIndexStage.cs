using Microsoft.Data.Sqlite;
using SIL.Motif.Host.Store;

namespace SIL.Motif.Host.Sources;

/// <summary>A disposable SQLite generation built without holding the project database's write lock.</summary>
public sealed class SourceIndexStage : IDisposable
{
    private SourceIndexStage(string path, SourceIndexState state) { Path = path; State = state; }

    /// <summary>The private temporary database containing this generation.</summary>
    public string Path { get; }

    /// <summary>The generation binding copied at publication.</summary>
    public SourceIndexState State { get; }

    /// <summary>Enumerates and validates all rows into a private staging database.</summary>
    public static SourceIndexStage Create(SourceIndexState state, IEnumerable<SourcePassage> passages,
        IEnumerable<SourceWordform> wordforms, IEnumerable<SourceWordPlace> places)
    {
        state = state with { SourceId = state.SourceId.ToUpperInvariant() };
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "motif-source-stage-" + Guid.NewGuid().ToString("N") + ".sqlite");
        var stage = new SourceIndexStage(path, state);
        try
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
                { DataSource = path, Pooling = false, ForeignKeys = true }.ToString());
            connection.Open();
            using var transaction = connection.BeginTransaction();
            using (var schema = connection.CreateCommand())
            {
                schema.Transaction = transaction;
                schema.CommandText = MotifSchema.SourceIndexDdl;
                schema.ExecuteNonQuery();
            }
            using var writer = new StageWriter(connection, transaction);
            writer.Insert("SourceIndexState", [state.SourceId, state.Kind, state.Generation, state.SourceDigest,
                state.IdentityJson, state.Status, state.TokenizerVersion, state.AnchorVersion]);
            foreach (var row in passages)
                writer.Insert("SourcePassages", [state.SourceId, row.PassageId, row.Locator, row.ContentHash,
                    row.TotalPlaces, row.EligiblePlaces]);
            foreach (var row in wordforms)
                writer.Insert("SourceWordforms", [state.SourceId, row.WordformKey, row.WritingSystem,
                    row.Normalized, row.Display, row.FieldWorksId]);
            foreach (var row in places)
                writer.Insert("SourceWordPlaces", [state.SourceId, row.PassageId, row.Ordinal, row.Anchor,
                    row.Verse, row.Surface, row.WordformKey, row.ReadingKey, row.Signature, row.State]);
            using (var validate = connection.CreateCommand())
            {
                validate.Transaction = transaction;
                validate.CommandText = "SELECT 1 FROM SourcePassages p LEFT JOIN " +
                    "(SELECT SourceId, PassageId, COUNT(*) AS Total FROM SourceWordPlaces GROUP BY SourceId, PassageId) w " +
                    "ON w.SourceId=p.SourceId AND w.PassageId=p.PassageId WHERE p.TotalPlaces<>COALESCE(w.Total,0) LIMIT 1;";
                if (validate.ExecuteScalar() is not null)
                    throw new InvalidDataException("A Source Passage count does not match its indexed places.");
            }
            transaction.Commit();
            return stage;
        }
        catch { stage.Dispose(); throw; }
    }

    private sealed class StageWriter(SqliteConnection connection, SqliteTransaction transaction) : IDisposable
    {
        private readonly Dictionary<string, SqliteCommand> _commands = new(StringComparer.Ordinal);

        public void Insert(string table, object?[] values)
        {
            if (!_commands.TryGetValue(table, out var command))
            {
                command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = $"INSERT INTO {table} VALUES ({string.Join(",", Enumerable.Range(0, values.Length).Select(i => "$p" + i))});";
                for (var i = 0; i < values.Length; i++) command.Parameters.AddWithValue("$p" + i, DBNull.Value);
                command.Prepare();
                _commands.Add(table, command);
            }
            for (var i = 0; i < values.Length; i++) command.Parameters[i].Value = values[i] ?? DBNull.Value;
            command.ExecuteNonQuery();
        }

        public void Dispose()
        {
            foreach (var command in _commands.Values) command.Dispose();
        }
    }

    /// <summary>Removes the private staging file after publication or abandonment.</summary>
    public void Dispose() => File.Delete(Path);
}
