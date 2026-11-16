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
    public SourceIndexState State { get; private set; }

    /// <summary>Enumerates and validates all rows into a private staging database.</summary>
    public static SourceIndexStage Create(SourceIndexState state, IEnumerable<SourcePassage> passages,
        IEnumerable<SourceWordform> wordforms, IEnumerable<SourceWordPlace> places)
    {
        using var builder = Begin(state);
        foreach (var row in passages) builder.AddPassage(row);
        foreach (var row in wordforms) builder.AddWordform(row);
        foreach (var row in places) builder.AddPlace(row);
        return builder.Complete(state.SourceDigest);
    }

    /// <summary>
    /// Opens a staging database that rows are appended to as they are produced, so a builder holds one Passage at a
    /// time. A previous published generation of the same project database may be named to copy unchanged Passages.
    /// </summary>
    public static Builder Begin(SourceIndexState state, string? previousDatabasePath = null) => new(state, previousDatabasePath);

    /// <summary>Appends rows to a staging database and seals it into a <see cref="SourceIndexStage"/>.</summary>
    public sealed class Builder : IDisposable
    {
        private readonly SourceIndexStage _stage;
        private readonly SqliteConnection _connection;
        private readonly SqliteTransaction _transaction;
        private readonly StageWriter _writer;
        private readonly bool _hasPrevious;
        private bool _completed;

        internal Builder(SourceIndexState state, string? previousDatabasePath)
        {
            state = state with { SourceId = state.SourceId.ToUpperInvariant() };
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "motif-source-stage-" + Guid.NewGuid().ToString("N") + ".sqlite");
            _stage = new SourceIndexStage(path, state);
            try
            {
                _connection = new SqliteConnection(new SqliteConnectionStringBuilder
                    { DataSource = path, Pooling = false, ForeignKeys = true }.ToString());
                _connection.Open();
                // The file is private and thrown away on any failure, so durability buys nothing here.
                Execute("PRAGMA journal_mode=OFF; PRAGMA synchronous=OFF;");
                if (previousDatabasePath is not null)
                {
                    using var attach = _connection.CreateCommand();
                    attach.CommandText = "ATTACH DATABASE $path AS previous;";
                    attach.Parameters.AddWithValue("$path", previousDatabasePath);
                    attach.ExecuteNonQuery();
                    _hasPrevious = true;
                }
                _transaction = _connection.BeginTransaction();
                using (var schema = _connection.CreateCommand())
                {
                    schema.Transaction = _transaction;
                    schema.CommandText = MotifSchema.SourceIndexDdl;
                    schema.ExecuteNonQuery();
                }
                _writer = new StageWriter(_connection, _transaction);
                _writer.Insert("SourceIndexState", [state.SourceId, state.Kind, state.Generation, state.SourceDigest,
                    state.IdentityJson, state.Status, state.TokenizerVersion, state.AnchorVersion]);
            }
            catch { Dispose(); throw; }
        }

        /// <summary>Appends a Passage row; its places must follow.</summary>
        public void AddPassage(SourcePassage row) =>
            _writer.Insert("SourcePassages", [_stage.State.SourceId, row.PassageId, row.Locator, row.ContentHash,
                row.TotalPlaces, row.EligiblePlaces]);

        /// <summary>Appends a wordform; a key already staged is kept as it is.</summary>
        public void AddWordform(SourceWordform row) =>
            _writer.Insert("SourceWordforms", [_stage.State.SourceId, row.WordformKey, row.WritingSystem,
                row.Normalized, row.Display, row.FieldWorksId], "INSERT OR IGNORE");

        /// <summary>Appends one place of an already appended Passage.</summary>
        public void AddPlace(SourceWordPlace row) =>
            _writer.Insert("SourceWordPlaces", [_stage.State.SourceId, row.PassageId, row.Ordinal, row.Anchor,
                row.Verse, row.Surface, row.WordformKey, row.ReadingKey, row.Signature, row.State]);

        /// <summary>
        /// Copies one Passage, its places and their wordforms from the previous generation, when that generation still
        /// holds exactly the expected passage and place count. Returns false and stages nothing otherwise.
        /// </summary>
        public bool TryCopyPassage(SourcePassage expected)
        {
            if (!_hasPrevious) return false;
            var source = _stage.State.SourceId;
            using (var count = Command("SELECT (SELECT COUNT(*) FROM previous.SourceWordPlaces WHERE SourceId=$s AND PassageId=$p)," +
                "(SELECT COUNT(*) FROM previous.SourcePassages WHERE SourceId=$s AND PassageId=$p AND ContentHash=$h " +
                "AND TotalPlaces=$t AND EligiblePlaces=$e);", source, expected))
            using (var reader = count.ExecuteReader())
            {
                if (!reader.Read() || reader.GetInt32(0) != expected.TotalPlaces || reader.GetInt32(1) != 1) return false;
            }
            Run("INSERT INTO SourcePassages SELECT * FROM previous.SourcePassages WHERE SourceId=$s AND PassageId=$p;", source, expected);
            Run("INSERT OR IGNORE INTO SourceWordforms SELECT * FROM previous.SourceWordforms WHERE SourceId=$s AND WordformKey IN " +
                "(SELECT WordformKey FROM previous.SourceWordPlaces WHERE SourceId=$s AND PassageId=$p);", source, expected);
            Run("INSERT INTO SourceWordPlaces SELECT * FROM previous.SourceWordPlaces WHERE SourceId=$s AND PassageId=$p;", source, expected);
            return true;
        }

        /// <summary>Validates counts, records the Source digest and seals the staging database.</summary>
        public SourceIndexStage Complete(string sourceDigest)
        {
            using (var update = _connection.CreateCommand())
            {
                update.Transaction = _transaction;
                update.CommandText = "UPDATE SourceIndexState SET SourceDigest=$digest;";
                update.Parameters.AddWithValue("$digest", sourceDigest);
                update.ExecuteNonQuery();
            }
            using (var validate = _connection.CreateCommand())
            {
                validate.Transaction = _transaction;
                validate.CommandText = "SELECT 1 FROM SourcePassages p LEFT JOIN " +
                    "(SELECT SourceId, PassageId, COUNT(*) AS Total FROM SourceWordPlaces GROUP BY SourceId, PassageId) w " +
                    "ON w.SourceId=p.SourceId AND w.PassageId=p.PassageId WHERE p.TotalPlaces<>COALESCE(w.Total,0) LIMIT 1;";
                if (validate.ExecuteScalar() is not null)
                    throw new InvalidDataException("A Source Passage count does not match its indexed places.");
            }
            _transaction.Commit();
            _stage.State = _stage.State with { SourceDigest = sourceDigest };
            _completed = true;
            Dispose();
            return _stage;
        }

        /// <summary>Abandons an unfinished staging database and removes its file.</summary>
        public void Dispose()
        {
            _writer?.Dispose();
            _transaction?.Dispose();
            _connection?.Dispose();
            if (!_completed) File.Delete(_stage.Path);
        }

        private void Execute(string sql)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        private SqliteCommand Command(string sql, string source, SourcePassage passage)
        {
            var command = _connection.CreateCommand();
            command.Transaction = _transaction;
            command.CommandText = sql;
            command.Parameters.AddWithValue("$s", source);
            command.Parameters.AddWithValue("$p", passage.PassageId);
            if (sql.Contains("$h", StringComparison.Ordinal))
            {
                command.Parameters.AddWithValue("$h", passage.ContentHash);
                command.Parameters.AddWithValue("$t", passage.TotalPlaces);
                command.Parameters.AddWithValue("$e", passage.EligiblePlaces);
            }
            return command;
        }

        private void Run(string sql, string source, SourcePassage passage)
        {
            using var command = Command(sql, source, passage);
            command.ExecuteNonQuery();
        }
    }

    private sealed class StageWriter(SqliteConnection connection, SqliteTransaction transaction) : IDisposable
    {
        private readonly Dictionary<string, SqliteCommand> _commands = new(StringComparer.Ordinal);

        public void Insert(string table, object?[] values, string verb = "INSERT")
        {
            var cacheKey = verb + " " + table;
            if (!_commands.TryGetValue(cacheKey, out var command))
            {
                command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = $"{verb} INTO {table} VALUES ({string.Join(",", Enumerable.Range(0, values.Length).Select(i => "$p" + i))});";
                for (var i = 0; i < values.Length; i++) command.Parameters.AddWithValue("$p" + i, DBNull.Value);
                command.Prepare();
                _commands.Add(cacheKey, command);
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
