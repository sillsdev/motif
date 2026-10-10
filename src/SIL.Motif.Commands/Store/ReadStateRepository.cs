using Microsoft.Data.Sqlite;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Commands.Store;

public sealed record ReadOccurrenceRecord(OccurrenceAnchor Occurrence, string FingerprintJson);

public sealed class ReadStateRepository
{
    private readonly MotifDatabase _database;
    private SqliteConnection? _transactionConnection;
    private SqliteTransaction? _transaction;

    public ReadStateRepository(MotifDatabase database) =>
        _database = database ?? throw new ArgumentNullException(nameof(database));

    public ReadStateWriteTransaction BeginTransaction()
    {
        if (_transaction is not null) throw new InvalidOperationException("A Read-state transaction is already open.");
        _transactionConnection = _database.OpenConnection();
        _transaction = _transactionConnection.BeginTransaction();
        return new ReadStateWriteTransaction(this, _transactionConnection, _transaction);
    }

    public bool IsCurrentContext(string projectKey, ExpectedContext expectedContext)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectKey);
        ArgumentNullException.ThrowIfNull(expectedContext);
        if (_transactionConnection is null || _transaction is null)
            throw new InvalidOperationException("Context validation requires an open Read-state transaction.");
        return ExpectedContextRepository.IsCurrentContext(
            _database, _transactionConnection, _transaction, projectKey, expectedContext);
    }

    public ReadOccurrenceRecord? Get(OccurrenceAnchor occurrence)
    {
        ValidateOccurrence(occurrence);
        using var ownedConnection = _transactionConnection is null ? _database.OpenConnection() : null;
        var connection = _transactionConnection ?? ownedConnection!;
        using var command = connection.CreateCommand();
        command.Transaction = _transaction;
        command.CommandText = """
            SELECT FingerprintJson FROM ReadOccurrences
            WHERE TextId = $textId AND ParagraphId = $paragraphId
                AND SegmentId = $segmentId AND WordIndex = $wordIndex;
            """;
        AddAnchorParameters(command, occurrence);
        var fingerprint = command.ExecuteScalar() as string;
        return fingerprint is null ? null : new ReadOccurrenceRecord(occurrence, fingerprint);
    }

    public IReadOnlyList<ReadOccurrenceRecord> GetForText(Guid textId)
    {
        if (textId == Guid.Empty) throw new ArgumentException("A Text identity is required.", nameof(textId));
        using var ownedConnection = _transactionConnection is null ? _database.OpenConnection() : null;
        var connection = _transactionConnection ?? ownedConnection!;
        using var command = connection.CreateCommand();
        command.Transaction = _transaction;
        command.CommandText = """
            SELECT TextId, ParagraphId, SegmentId, WordIndex, FingerprintJson
            FROM ReadOccurrences WHERE TextId = $textId
            ORDER BY ParagraphId, SegmentId, WordIndex;
            """;
        command.Parameters.AddWithValue("$textId", textId.ToString("D"));
        RepositoryReadCounters.QueryExecuted();
        using var reader = command.ExecuteReader();
        var records = new List<ReadOccurrenceRecord>();
        while (reader.Read())
        {
            RepositoryReadCounters.RecordDeserialized();
            var occurrence = new OccurrenceAnchor(ParseGuid(reader.GetString(0)), ParseGuid(reader.GetString(1)),
                ParseGuid(reader.GetString(2)), reader.GetInt32(3));
            ValidateOccurrence(occurrence);
            records.Add(new ReadOccurrenceRecord(occurrence, reader.GetString(4)));
        }
        return records;
    }

    public IReadOnlyList<ReadOccurrenceRecord> GetForTexts(IReadOnlyCollection<Guid> textIds)
    {
        ArgumentNullException.ThrowIfNull(textIds);
        if (textIds.Any(id => id == Guid.Empty))
            throw new ArgumentException("Read-state Text identities must be non-empty.", nameof(textIds));
        if (textIds.Count == 0) return [];
        using var ownedConnection = _transactionConnection is null ? _database.OpenConnection() : null;
        var connection = _transactionConnection ?? ownedConnection!;
        using var command = connection.CreateCommand();
        command.Transaction = _transaction;
        command.CommandText = """
            SELECT TextId, ParagraphId, SegmentId, WordIndex, FingerprintJson
            FROM ReadOccurrences WHERE TextId IN (SELECT value FROM json_each($textIds))
            ORDER BY TextId, ParagraphId, SegmentId, WordIndex;
            """;
        command.Parameters.AddWithValue("$textIds", System.Text.Json.JsonSerializer.Serialize(
            textIds.Select(id => id.ToString("D"))));
        RepositoryReadCounters.QueryExecuted();
        using var reader = command.ExecuteReader();
        var records = new List<ReadOccurrenceRecord>();
        while (reader.Read())
        {
            RepositoryReadCounters.RecordDeserialized();
            var occurrence = new OccurrenceAnchor(ParseGuid(reader.GetString(0)), ParseGuid(reader.GetString(1)),
                ParseGuid(reader.GetString(2)), reader.GetInt32(3));
            ValidateOccurrence(occurrence);
            records.Add(new ReadOccurrenceRecord(occurrence, reader.GetString(4)));
        }
        return records;
    }

    public void Upsert(ReadOccurrenceRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        ValidateOccurrence(record.Occurrence);
        ArgumentException.ThrowIfNullOrWhiteSpace(record.FingerprintJson);
        using var ownedConnection = _transactionConnection is null ? _database.OpenConnection() : null;
        var connection = _transactionConnection ?? ownedConnection!;
        using var command = connection.CreateCommand();
        command.Transaction = _transaction;
        command.CommandText = """
            INSERT INTO ReadOccurrences (TextId, ParagraphId, SegmentId, WordIndex, FingerprintJson)
            VALUES ($textId, $paragraphId, $segmentId, $wordIndex, $fingerprint)
            ON CONFLICT (TextId, ParagraphId, SegmentId, WordIndex) DO UPDATE SET
                FingerprintJson = excluded.FingerprintJson;
            """;
        AddAnchorParameters(command, record.Occurrence);
        command.Parameters.AddWithValue("$fingerprint", record.FingerprintJson);
        command.ExecuteNonQuery();
    }

    public void Delete(OccurrenceAnchor occurrence)
    {
        ValidateOccurrence(occurrence);
        using var ownedConnection = _transactionConnection is null ? _database.OpenConnection() : null;
        var connection = _transactionConnection ?? ownedConnection!;
        using var command = connection.CreateCommand();
        command.Transaction = _transaction;
        command.CommandText = """
            DELETE FROM ReadOccurrences
            WHERE TextId = $textId AND ParagraphId = $paragraphId
                AND SegmentId = $segmentId AND WordIndex = $wordIndex;
            """;
        AddAnchorParameters(command, occurrence);
        command.ExecuteNonQuery();
    }

    public void DeleteForText(Guid textId)
    {
        if (textId == Guid.Empty) throw new ArgumentException("A Text identity is required.", nameof(textId));
        using var ownedConnection = _transactionConnection is null ? _database.OpenConnection() : null;
        var connection = _transactionConnection ?? ownedConnection!;
        using var command = connection.CreateCommand();
        command.Transaction = _transaction;
        command.CommandText = "DELETE FROM ReadOccurrences WHERE TextId = $textId;";
        command.Parameters.AddWithValue("$textId", textId.ToString("D"));
        command.ExecuteNonQuery();
    }

    private static void AddAnchorParameters(SqliteCommand command, OccurrenceAnchor occurrence)
    {
        command.Parameters.AddWithValue("$textId", occurrence.TextId.ToString("D"));
        command.Parameters.AddWithValue("$paragraphId", occurrence.ParagraphId.ToString("D"));
        command.Parameters.AddWithValue("$segmentId", occurrence.SegmentId.ToString("D"));
        command.Parameters.AddWithValue("$wordIndex", occurrence.Index);
    }

    private static Guid ParseGuid(string value) => Guid.TryParseExact(value, "D", out var id) && id != Guid.Empty
        ? id
        : throw new InvalidDataException("Stored Read occurrence has an invalid identity.");

    private static void ValidateOccurrence(OccurrenceAnchor occurrence)
    {
        if (occurrence.TextId == Guid.Empty || occurrence.ParagraphId == Guid.Empty ||
            occurrence.SegmentId == Guid.Empty || occurrence.Index < 0)
            throw new ArgumentException("A Read occurrence requires non-empty identities and a non-negative word index.",
                nameof(occurrence));
    }

    private void EndTransaction(SqliteConnection connection, SqliteTransaction transaction)
    {
        _transaction = null;
        _transactionConnection = null;
        transaction.Dispose();
        connection.Dispose();
    }

    public sealed class ReadStateWriteTransaction : IDisposable
    {
        private ReadStateRepository? _owner;
        private readonly SqliteConnection _connection;
        private readonly SqliteTransaction _transaction;

        internal ReadStateWriteTransaction(ReadStateRepository owner, SqliteConnection connection,
            SqliteTransaction transaction)
        {
            _owner = owner;
            _connection = connection;
            _transaction = transaction;
        }

        public void Commit()
        {
            ObjectDisposedException.ThrowIf(_owner is null, this);
            _transaction.Commit();
        }

        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            if (owner is not null) owner.EndTransaction(_connection, _transaction);
        }
    }
}
