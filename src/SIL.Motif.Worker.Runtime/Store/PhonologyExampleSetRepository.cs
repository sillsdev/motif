using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Phonology;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Store;

namespace SIL.Motif.Worker.Store;

/// <summary>Raised when an Example Set write does not name its current revision.</summary>
public sealed class PhonologyExampleSetRevisionConflictException(string message) : InvalidOperationException(message);

/// <summary>Stores immutable, cited Example Set revisions in a project's Motif database.</summary>
public sealed class PhonologyExampleSetRepository
{
    private static readonly JsonSerializerOptions JsonOptions = PhonologyJson.CreateOptions();
    private readonly MotifDatabase _database;

    /// <summary>Creates a repository for one open project database.</summary>
    public PhonologyExampleSetRepository(MotifDatabase database) =>
        _database = database ?? throw new ArgumentNullException(nameof(database));

    /// <summary>Saves a new immutable revision and advances the named set's current revision atomically.</summary>
    public PhonologyExampleSetHeader Save(string name, IReadOnlyList<PhonologyExample> examples,
        string? expectedRevisionId, string? proposalId)
    {
        var validated = PhonologyExampleValidation.ValidateAndCopy(name, examples, expectedRevisionId, proposalId);
        var savedUtc = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction(deferred: false);

        var current = ReadCurrentSet(connection, transaction, validated.Name);
        if (current is null && expectedRevisionId is not null || current is not null &&
            !StringComparer.Ordinal.Equals(expectedRevisionId, current.RevisionId))
            throw new PhonologyExampleSetRevisionConflictException(
                "The Example Set changed before this revision could be saved.");
        if (validated.ProposalId is not null && !ProposalExists(connection, transaction, validated.ProposalId))
            throw new KeyNotFoundException("The supporting Proposal does not exist in this project store.");

        var setId = current?.SetId ?? CanonicalId.Mint("example-set/").Value;
        var revisionId = CanonicalId.Mint("example-revision/").Value;
        var revisionNumber = current is null ? 1 : checked(current.RevisionNumber + 1);
        if (current is null)
        {
            using var createSet = connection.CreateCommand();
            createSet.Transaction = transaction;
            createSet.CommandText = "INSERT INTO PhonologyExampleSets (SetId, Name, CurrentRevisionId) VALUES ($setId, $name, $revisionId);";
            createSet.Parameters.AddWithValue("$setId", setId);
            createSet.Parameters.AddWithValue("$name", validated.Name);
            createSet.Parameters.AddWithValue("$revisionId", revisionId);
            createSet.ExecuteNonQuery();
        }

        using (var createRevision = connection.CreateCommand())
        {
            createRevision.Transaction = transaction;
            createRevision.CommandText = """
                INSERT INTO PhonologyExampleSetRevisions
                    (RevisionId, SetId, RevisionNumber, ExecutableSha256, CitedRevisionSha256, ExampleCount, ProposalId, SavedUtc)
                VALUES ($revisionId, $setId, $revisionNumber, $executableSha256, $citedRevisionSha256, $exampleCount, $proposalId, $savedUtc);
                """;
            createRevision.Parameters.AddWithValue("$revisionId", revisionId);
            createRevision.Parameters.AddWithValue("$setId", setId);
            createRevision.Parameters.AddWithValue("$revisionNumber", revisionNumber);
            createRevision.Parameters.AddWithValue("$executableSha256", validated.ExecutableSha256);
            createRevision.Parameters.AddWithValue("$citedRevisionSha256", validated.CitedRevisionSha256);
            createRevision.Parameters.AddWithValue("$exampleCount", validated.Examples.Count);
            createRevision.Parameters.AddWithValue("$proposalId", (object?)validated.ProposalId ?? DBNull.Value);
            createRevision.Parameters.AddWithValue("$savedUtc", savedUtc);
            createRevision.ExecuteNonQuery();
        }

        InsertExamples(connection, transaction, revisionId, validated.Examples);

        if (current is not null)
        {
            using var advanceCurrent = connection.CreateCommand();
            advanceCurrent.Transaction = transaction;
            advanceCurrent.CommandText = "UPDATE PhonologyExampleSets SET CurrentRevisionId = $revisionId " +
                "WHERE SetId = $setId AND CurrentRevisionId = $expectedRevisionId;";
            advanceCurrent.Parameters.AddWithValue("$revisionId", revisionId);
            advanceCurrent.Parameters.AddWithValue("$setId", setId);
            advanceCurrent.Parameters.AddWithValue("$expectedRevisionId", current.RevisionId);
            if (advanceCurrent.ExecuteNonQuery() != 1)
                throw new PhonologyExampleSetRevisionConflictException(
                    "The Example Set changed before this revision could be saved.");
        }

        transaction.Commit();
        return Header(setId, revisionId, validated.Name, revisionNumber, validated.ExecutableSha256,
            validated.CitedRevisionSha256, validated.Examples.Count, validated.ProposalId, savedUtc);
    }

    /// <summary>Lists current Example Set headers in immutable SetId order.</summary>
    public PhonologyExamplesListResponse List(int pageSize, PhonologySetListCursor? continuation)
    {
        PhonologyExampleValidation.ValidatePageSize(pageSize);
        using var connection = _database.OpenConnection();
        var projectIdentity = ReadProjectIdentity(connection);
        PhonologyExampleValidation.ValidateSetListCursor(continuation, projectIdentity);
        var rows = new List<PhonologyExampleSetHeader>(pageSize + 1);
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT s.SetId, s.Name, r.RevisionId, r.RevisionNumber, r.ExecutableSha256,
                    r.CitedRevisionSha256, r.ExampleCount, r.ProposalId, r.SavedUtc
                FROM PhonologyExampleSets s
                JOIN PhonologyExampleSetRevisions r
                    ON r.SetId = s.SetId AND r.RevisionId = s.CurrentRevisionId
                WHERE $afterSetId IS NULL OR s.SetId COLLATE BINARY > $afterSetId COLLATE BINARY
                ORDER BY s.SetId COLLATE BINARY
                LIMIT $limit;
                """;
            command.Parameters.AddWithValue("$afterSetId", (object?)continuation?.AfterSetId ?? DBNull.Value);
            command.Parameters.AddWithValue("$limit", pageSize + 1);
            using var reader = command.ExecuteReader();
            while (reader.Read()) rows.Add(ReadHeader(reader));
        }

        var items = rows.Take(pageSize).ToList();
        var hasMore = rows.Count > items.Count;
        while (true)
        {
            var cursor = hasMore && items.Count > 0
                ? new PhonologySetListCursor(projectIdentity, items[^1].Revision.SetId)
                : null;
            var response = new PhonologyExamplesListResponse(items.AsReadOnly(), cursor);
            if (SerializedSize(response) <= PhonologyExampleValidation.MaxSerializedPageUtf8Bytes)
                return response;
            if (items.Count == 0)
                throw new InvalidDataException("An Example Set list header exceeds the maximum page size.");
            items.RemoveAt(items.Count - 1);
            hasMore = true;
        }
    }

    /// <summary>Reads one exact historical revision and a bounded page of its authored examples.</summary>
    public PhonologyExamplesPageResponse Read(string revisionId, int pageSize, PhonologyPageCursor? continuation)
    {
        PhonologyExampleValidation.ValidateRevisionId(revisionId);
        PhonologyExampleValidation.ValidatePageSize(pageSize);
        PhonologyExampleValidation.ValidatePageCursor(continuation, revisionId);
        using var connection = _database.OpenConnection();
        var header = ReadHeader(connection, revisionId) ??
            throw new KeyNotFoundException("The requested Example Set revision does not exist.");
        using (var countCommand = connection.CreateCommand())
        {
            countCommand.CommandText = "SELECT COUNT(*) FROM PhonologyExamples WHERE RevisionId = $revisionId;";
            countCommand.Parameters.AddWithValue("$revisionId", revisionId);
            if (Convert.ToInt32(countCommand.ExecuteScalar(), CultureInfo.InvariantCulture) != header.Revision.ExampleCount)
                throw new InvalidDataException("The Example Set revision has an inconsistent example count.");
        }

        var examples = new List<(int Ordinal, PhonologyExample Example)>(pageSize + 1);
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT Ordinal, ExampleId, Underlying, ExpectedJson, ContextJson, CitationJson
                FROM PhonologyExamples
                WHERE RevisionId = $revisionId AND Ordinal > $afterOrdinal
                ORDER BY Ordinal
                LIMIT $limit;
                """;
            command.Parameters.AddWithValue("$revisionId", revisionId);
            command.Parameters.AddWithValue("$afterOrdinal", continuation?.AfterOrdinal ?? -1);
            command.Parameters.AddWithValue("$limit", pageSize + 1);
            using var reader = command.ExecuteReader();
            while (reader.Read()) examples.Add((reader.GetInt32(0), ReadExample(reader)));
        }

        var page = examples.Take(pageSize).ToList();
        var hasMore = examples.Count > page.Count;
        while (true)
        {
            var cursor = hasMore && page.Count > 0
                ? new PhonologyPageCursor(revisionId, page[^1].Ordinal)
                : null;
            var response = new PhonologyExamplesPageResponse(header,
                page.Select(row => row.Example).ToArray(), cursor);
            if (SerializedSize(response) <= PhonologyExampleValidation.MaxSerializedPageUtf8Bytes)
                return response;
            if (page.Count == 0)
                throw new InvalidDataException("An Example Set example exceeds the maximum page size.");
            page.RemoveAt(page.Count - 1);
            hasMore = true;
        }
    }

    private static void InsertExamples(SqliteConnection connection, SqliteTransaction transaction,
        string revisionId, IReadOnlyList<PhonologyExample> examples)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO PhonologyExamples
                (RevisionId, Ordinal, ExampleId, Underlying, ExpectedJson, ContextJson, CitationJson)
            VALUES ($revisionId, $ordinal, $exampleId, $underlying, $expected, $context, $citation);
            """;
        var revisionParameter = command.Parameters.Add("$revisionId", SqliteType.Text);
        var ordinalParameter = command.Parameters.Add("$ordinal", SqliteType.Integer);
        var idParameter = command.Parameters.Add("$exampleId", SqliteType.Text);
        var underlyingParameter = command.Parameters.Add("$underlying", SqliteType.Text);
        var expectedParameter = command.Parameters.Add("$expected", SqliteType.Text);
        var contextParameter = command.Parameters.Add("$context", SqliteType.Text);
        var citationParameter = command.Parameters.Add("$citation", SqliteType.Text);
        for (var index = 0; index < examples.Count; index++)
        {
            var example = examples[index];
            revisionParameter.Value = revisionId;
            ordinalParameter.Value = index;
            idParameter.Value = example.Id;
            underlyingParameter.Value = example.Underlying;
            expectedParameter.Value = JsonSerializer.Serialize(example.Expected, JsonOptions);
            contextParameter.Value = JsonSerializer.Serialize(example.Context, JsonOptions);
            citationParameter.Value = example.Citation is null
                ? DBNull.Value
                : JsonSerializer.Serialize(example.Citation, JsonOptions);
            command.ExecuteNonQuery();
        }
    }

    private static CurrentSet? ReadCurrentSet(SqliteConnection connection, SqliteTransaction transaction, string name)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT s.SetId, s.CurrentRevisionId, r.RevisionNumber
            FROM PhonologyExampleSets s
            JOIN PhonologyExampleSetRevisions r
                ON r.SetId = s.SetId AND r.RevisionId = s.CurrentRevisionId
            WHERE s.Name = $name COLLATE BINARY;
            """;
        command.Parameters.AddWithValue("$name", name);
        using var reader = command.ExecuteReader();
        return reader.Read()
            ? new CurrentSet(reader.GetString(0), reader.GetString(1), reader.GetInt32(2))
            : null;
    }

    private static bool ProposalExists(SqliteConnection connection, SqliteTransaction transaction, string proposalId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT 1 FROM Proposals WHERE ProposalId = $proposalId;";
        command.Parameters.AddWithValue("$proposalId", proposalId);
        return command.ExecuteScalar() is not null;
    }

    private static string ReadProjectIdentity(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT FieldWorksProjectIdentity FROM MotifMetadata WHERE Id = 1;";
        return command.ExecuteScalar() as string ??
            throw new InvalidDataException("The project store has no project identity.");
    }

    private static PhonologyExampleSetHeader? ReadHeader(SqliteConnection connection, string revisionId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = HeaderQuery + " WHERE r.RevisionId = $revisionId;";
        command.Parameters.AddWithValue("$revisionId", revisionId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadHeader(reader) : null;
    }

    private static PhonologyExampleSetHeader ReadHeader(SqliteDataReader reader)
    {
        var setId = reader.GetString(0);
        var revisionId = reader.GetString(2);
        var revisionNumber = reader.GetInt32(3);
        var executableSha256 = reader.GetString(4);
        var citedRevisionSha256 = reader.GetString(5);
        var exampleCount = reader.GetInt32(6);
        var proposalId = reader.IsDBNull(7) ? null : reader.GetString(7);
        var savedUtc = reader.GetString(8);
        RequireStoredId(setId, "set");
        RequireStoredId(revisionId, "revision");
        if (proposalId is not null) RequireStoredId(proposalId, "Proposal");
        RequireStoredDigest(executableSha256);
        RequireStoredDigest(citedRevisionSha256);
        if (revisionNumber < 1 || exampleCount is < 1 or > PhonologyExampleValidation.MaxExamples)
            throw new InvalidDataException("The Example Set revision header is invalid.");
        return Header(setId, revisionId, reader.GetString(1), revisionNumber, executableSha256,
            citedRevisionSha256, exampleCount, proposalId, savedUtc);
    }

    private static string HeaderQuery => """
        SELECT s.SetId, s.Name, r.RevisionId, r.RevisionNumber, r.ExecutableSha256,
            r.CitedRevisionSha256, r.ExampleCount, r.ProposalId, r.SavedUtc
        FROM PhonologyExampleSetRevisions r
        JOIN PhonologyExampleSets s ON s.SetId = r.SetId
        """;

    private static PhonologyExample ReadExample(SqliteDataReader reader)
    {
        var ordinal = reader.GetInt32(0);
        var id = reader.GetString(1);
        try
        {
            var expected = JsonSerializer.Deserialize<string[]>(reader.GetString(3), JsonOptions) ??
                throw new JsonException("The stored expected forms are null.");
            var context = JsonSerializer.Deserialize<PhonologyGenerationContext>(reader.GetString(4), JsonOptions) ??
                throw new JsonException("The stored generation context is null.");
            var citation = reader.IsDBNull(5)
                ? null
                : JsonSerializer.Deserialize<CitationView>(reader.GetString(5), JsonOptions) ??
                    throw new JsonException("The stored Citation is null.");
            var stored = new PhonologyExample(id, reader.GetString(2), expected, context, citation);
            var validated = PhonologyExampleValidation.ValidateAndCopy("Stored", [stored]);
            if (ordinal < 0)
                throw new InvalidDataException("The stored example ordinal is negative.");
            return validated.Examples[0];
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or FormatException or InvalidOperationException)
        {
            throw new InvalidDataException("The Example Set revision contains an invalid stored example.", exception);
        }
    }

    private static PhonologyExampleSetHeader Header(string setId, string revisionId, string name, int revisionNumber,
        string executableSha256, string citedRevisionSha256, int exampleCount, string? proposalId, string savedUtc) =>
        new(new PhonologyRevisionReference(setId, revisionId, executableSha256, citedRevisionSha256, exampleCount),
            name, savedUtc, proposalId);

    private static int SerializedSize<T>(T response) =>
        JsonSerializer.SerializeToUtf8Bytes(response, JsonOptions).Length;

    private static void RequireStoredId(string value, string kind)
    {
        if (!CanonicalId.TryParse(value, out _))
            throw new InvalidDataException($"The stored Example Set {kind} id is not canonical.");
    }

    private static void RequireStoredDigest(string value)
    {
        if (value.Length != 71 || !value.StartsWith("sha256:", StringComparison.Ordinal) ||
            value.AsSpan(7).IndexOfAnyExcept("0123456789abcdef") >= 0)
            throw new InvalidDataException("The stored Example Set digest is not canonical.");
    }

    private sealed record CurrentSet(string SetId, string RevisionId, int RevisionNumber);
}
