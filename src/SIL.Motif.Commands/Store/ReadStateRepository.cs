using Microsoft.Data.Sqlite;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Host.Store;

namespace SIL.Motif.Commands.Store;

public sealed record ReadOccurrenceRecord(OccurrenceAnchor Occurrence, string FingerprintJson);

public sealed class ReadStateRepository(MotifDatabase database)
{
    private readonly MotifDatabase _database = database ?? throw new ArgumentNullException(nameof(database));

    public ReadOccurrenceRecord? Get(OccurrenceAnchor occurrence)
    {
        ValidateOccurrence(occurrence);
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
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
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT TextId, ParagraphId, SegmentId, WordIndex, FingerprintJson
            FROM ReadOccurrences WHERE TextId = $textId
            ORDER BY ParagraphId, SegmentId, WordIndex;
            """;
        command.Parameters.AddWithValue("$textId", textId.ToString("D"));
        using var reader = command.ExecuteReader();
        var records = new List<ReadOccurrenceRecord>();
        while (reader.Read())
        {
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
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
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
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
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
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
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
}
