using System.Text.Json;
using Microsoft.Data.Sqlite;
using SIL.Motif.Host.Store;
using SIL.Motif.Scripture;
using SIL.Motif.Worker.Sources.Adapter;

namespace SIL.Motif.Worker.Sources;

internal sealed record CorpusCursor(CorpusStamp Stamp, string Query, long After);

internal sealed record CorpusPage<T>(IReadOnlyList<T> Items, CorpusCursor? Next);

internal enum CorpusSourceFailure
{
    BucketUnavailable,
    ContextBasisMismatch,
    ContextUnavailable,
    DocumentUnavailable,
    EmptySelection,
    InvalidCursor,
    InvalidProjection,
    PrivateContext,
    PrivateSelection,
    SelectionBasisMismatch,
    SourceChanged,
    SourceUnavailable,
}

internal sealed class CorpusSourceRefusal(CorpusSourceFailure reason, string message) : InvalidOperationException(message)
{
    public CorpusSourceFailure Reason { get; } = reason;
}

internal sealed class CorpusSourceSnapshot(MotifDatabase database, CorpusStamp stamp, CorpusSourceBinding binding,
    CorpusFileVersion[] inventory, int documentCount, long typeCount, long tokenCount, DateTimeOffset lastCheckedUtc,
    CorpusFreshness freshness, bool exactGeneration)
{
    public CorpusStamp Stamp { get; } = stamp;
    public CorpusSourceBinding Binding { get; } = binding;
    public IReadOnlyList<CorpusFileVersion> Inventory { get; } = Array.AsReadOnly(inventory);
    public int DocumentCount { get; } = documentCount;
    public long TypeCount { get; } = typeCount;
    public long TokenCount { get; } = tokenCount;
    public DateTimeOffset LastCheckedUtc { get; } = lastCheckedUtc;
    public CorpusFreshness Freshness { get; } = freshness;

    private const string DocumentColumns = "DocumentId,Container,Position,RawHash,TextHash,Metadata,ShardId,Tokens";

    public CorpusPage<CorpusDocumentVersion> Documents(int limit = 50, CorpusCursor? cursor = null,
        CancellationToken cancellationToken = default)
    {
        const string query = "documents";
        var after = ValidatePage(query, limit, cursor, cancellationToken);
        using var connection = database.OpenConnection();
        using var transaction = connection.BeginTransaction(deferred: true);
        using var status = StatusCommand(connection, transaction);
        ReadProjectionStatus(status);
        using var command = CorpusSql.Command(connection, transaction,
            $"SELECT {DocumentColumns},Ordinal FROM IndexedCorpusDocuments " +
            "WHERE Generation=$id AND Ordinal>$after ORDER BY Ordinal LIMIT $limit;",
            ("$id", Stamp.Id), ("$after", after), ("$limit", limit + 1));
        using var reader = command.ExecuteReader();
        var entries = new List<CorpusDocumentVersion>();
        var last = ReadPage(reader, entries, limit, 8, ReadDocument, cancellationToken);
        return new(entries, last is null ? null : new(Stamp, query, last.Value));
    }

    internal IReadOnlyList<CorpusDocumentVersion> DocumentVersions(SqliteConnection connection, SqliteTransaction? transaction,
        CancellationToken cancellationToken)
    {
        using var command = CorpusSql.Command(connection, transaction,
            $"SELECT {DocumentColumns} FROM IndexedCorpusDocuments WHERE Generation=$id ORDER BY Ordinal;",
            ("$id", Stamp.Id));
        using var reader = command.ExecuteReader();
        var documents = new List<CorpusDocumentVersion>();
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            documents.Add(ReadDocument(reader));
        }
        return documents;
    }

    public CorpusPage<CorpusOccurrence> Occurrences(string documentId, string? formId = null,
        int limit = 50, CorpusCursor? cursor = null, CancellationToken cancellationToken = default)
    {
        var query = CorpusContent.Digest(new[] { "occurrences", documentId, formId });
        var after = ValidatePage(query, limit, cursor, cancellationToken);
        using var connection = database.OpenConnection();
        using var transaction = connection.BeginTransaction(deferred: true);
        using var status = StatusCommand(connection, transaction);
        EnsureSourceAvailable(status);
        var document = FindDocument(connection, transaction, documentId);
        var filter = formId is null ? "" :
            "AND o.FormId=(SELECT Id FROM IndexedCorpusForms WHERE FormKey=$form)";
        using var command = CorpusSql.Command(connection, transaction, $"""
            SELECT o.Ordinal,o.Segment,o.Word,o.ByteOffset,o.ByteLength,o.Verse,COALESCE(o.Surface,f.FormNfd),
                f.FormKey,f.WritingSystem,f.FormNfd
            FROM IndexedCorpusOccurrences o JOIN IndexedCorpusForms f ON f.Id=o.FormId
            WHERE o.ShardId=$shard AND o.Ordinal>$after {filter} ORDER BY o.Ordinal LIMIT $limit;
            """, ("$shard", document.ShardId), ("$after", after), ("$form", formId), ("$limit", limit + 1));
        using var reader = command.ExecuteReader();
        var entries = new List<CorpusOccurrence>();
        var key = new DocumentKey(Stamp.Source, documentId);
        var last = ReadPage(reader, entries, limit, 0, row => ReadOccurrence(key, document.TextHash, row), cancellationToken);
        return new(entries, last is null ? null : new(Stamp, query, last.Value));
    }

    public CorpusResolvedSelection ResolveSelection(IReadOnlyCollection<string>? documentIds = null, string? bucket = null,
        CancellationToken cancellationToken = default, string? expectedMembershipDigest = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var connection = database.OpenConnection();
        using var transaction = connection.BeginTransaction(deferred: true);
        using var status = StatusCommand(connection, transaction);
        var currentStatus = EnsureSourceAvailable(status);
        if (bucket is not null)
            throw new CorpusSourceRefusal(CorpusSourceFailure.BucketUnavailable, "This Source generation has no measured bucket membership.");
        if (Binding.Private)
            throw new CorpusSourceRefusal(CorpusSourceFailure.PrivateSelection, "This Source requires a private parser stream.");
        if (currentStatus == CorpusFreshness.Historical && (!exactGeneration || expectedMembershipDigest is null))
            throw new CorpusSourceRefusal(CorpusSourceFailure.SelectionBasisMismatch,
                "Historical Selection requires an exact generation and a frozen Document membership digest.");
        var membership = documentIds?.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var documents = ReadMembership(connection, transaction, membership, cancellationToken);
        if (membership is not null && documents.Count != membership.Length)
            throw new CorpusSourceRefusal(CorpusSourceFailure.DocumentUnavailable, "A requested Document is absent from the pinned Source.");
        var digest = CorpusContent.Digest(new
        {
            Stamp.SegmentationId,
            Documents = documents.OrderBy(document => document.Id, StringComparer.Ordinal),
        });
        if (expectedMembershipDigest is not null && expectedMembershipDigest != digest)
            throw new CorpusSourceRefusal(CorpusSourceFailure.SelectionBasisMismatch,
                "The Selection does not match the frozen Document membership.");
        var weightFilter = documents.Count == DocumentCount ? null : JsonSerializer.Serialize(membership);
        var (types, tokens) = weightFilter is null ? (TypeCount, TokenCount) :
            (CountSelection(connection, transaction, weightFilter), documents.Sum(document => document.Tokens));
        if (types == 0)
            throw new CorpusSourceRefusal(CorpusSourceFailure.EmptySelection, "The pinned scope contains no eligible Wordforms.");
        var scope = new CorpusSelectionScope(Stamp, digest, types, tokens, membership is null
            ? null : Array.AsReadOnly(membership));
        return new(scope, ct => ReadWeights(weightFilter, ct));
    }

    private sealed record SelectionDocument(string Id, string TextHash, long Tokens);

    private List<SelectionDocument> ReadMembership(SqliteConnection connection, SqliteTransaction transaction,
        string[]? membership, CancellationToken cancellationToken)
    {
        var filter = membership is null ? "" : "AND DocumentId IN (SELECT value FROM json_each($documents))";
        using var command = CorpusSql.Command(connection, transaction, $"""
            SELECT DocumentId,TextHash,Tokens FROM IndexedCorpusDocuments
            WHERE Generation=$id {filter};
            """, ("$id", Stamp.Id), ("$documents", JsonSerializer.Serialize(membership)));
        using var reader = command.ExecuteReader();
        var documents = new List<SelectionDocument>();
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            documents.Add(new(reader.GetString(0), reader.GetString(1), reader.GetInt64(2)));
        }
        return documents;
    }

    private const string ScopeWeights = """
        SELECT w.FormId,SUM(w.Frequency) Weight
        FROM IndexedCorpusDocuments d JOIN IndexedCorpusShardWeights w ON w.ShardId=d.ShardId
        WHERE d.Generation=$id AND d.DocumentId IN (SELECT value FROM json_each($documents))
        GROUP BY w.FormId
        """;

    private long CountSelection(SqliteConnection connection, SqliteTransaction transaction, string membership)
    {
        using var command = CorpusSql.Command(connection, transaction, $"SELECT COUNT(*) FROM ({ScopeWeights});",
            ("$id", Stamp.Id), ("$documents", membership));
        return (long)command.ExecuteScalar()!;
    }

    private IEnumerable<WeightedWordform> ReadWeights(string? membership, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var connection = database.OpenConnection();
        using var transaction = connection.BeginTransaction(deferred: true);
        using var status = StatusCommand(connection, transaction);
        EnsureSourceAvailable(status);
        using var liveConnection = database.OpenConnection();
        using var liveStatus = StatusCommand(liveConnection, null);
        liveStatus.Prepare();
        var selected = membership is null ? "w.Frequency" : "s.Weight";
        var join = membership is null ? "" : $"JOIN ({ScopeWeights}) s ON s.FormId=w.FormId";
        using var command = CorpusSql.Command(connection, transaction, $"""
            SELECT f.FormKey,f.WritingSystem,f.FormNfd,w.Frequency,{selected}
            FROM IndexedCorpusWeights w
            JOIN IndexedCorpusForms f ON f.Id=w.FormId
            {join}
            WHERE w.Generation=$id ORDER BY w.FormId;
            """, ("$id", Stamp.Id), ("$documents", membership));
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureSourceAvailable(liveStatus);
            yield return new(reader.GetString(0), new(reader.GetString(1), reader.GetString(2)),
                reader.GetInt64(3), reader.GetInt64(4));
        }
        cancellationToken.ThrowIfCancellationRequested();
        EnsureSourceAvailable(liveStatus);
    }

    public CorpusSegment ReadContext(CorpusSourceDeclaration declaration, OccurrenceKey occurrence,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var connection = database.OpenConnection();
        using var transaction = connection.BeginTransaction(deferred: true);
        using var status = StatusCommand(connection, transaction);
        ReadProjectionStatus(status);
        if (Binding.Private)
            throw new CorpusSourceRefusal(CorpusSourceFailure.PrivateContext, "The private Source does not expose context text.");
        if (occurrence.Document.Source != Stamp.Source || occurrence.SegmentationId != Stamp.SegmentationId ||
            declaration.SourceId != Stamp.Source.SourceId)
            throw new CorpusSourceRefusal(CorpusSourceFailure.ContextBasisMismatch, "The context request belongs to another Source basis.");
        var document = FindDocument(connection, transaction, occurrence.Document.DocumentId);
        if (document.TextHash != occurrence.DocumentHash)
            throw new CorpusSourceRefusal(CorpusSourceFailure.ContextBasisMismatch, "The context request names another Document version.");
        EnsureAnchor(connection, transaction, document.ShardId, occurrence.Anchor);
        transaction.Commit();
        var input = new CorpusSourceInput(declaration, Stamp.Source.ProjectId);
        if (input.Binding != Binding)
            throw new CorpusSourceRefusal(CorpusSourceFailure.ContextBasisMismatch, "The context decoder differs from the pinned Source.");
        try
        {
            var stylesheet = input.CaptureStylesheet(cancellationToken);
            if (stylesheet is not null && !Inventory.Contains(stylesheet.Version)) throw ContextUnavailable();
            var file = input.Capture(document.Container, cancellationToken);
            int? position = Inventory.Contains(file.Version) ? document.Position : null;
            var styles = Binding.Format == CorpusSourceFormat.Usfm ? ParatextPassages.ReadStylesheet(stylesheet?.Bytes) : null;
            var decoded = input.Read(file, styles, cancellationToken, position)
                .Where(row => row.Version.Id == document.Id).Take(2).ToArray();
            var segment = decoded.Length == 1 && decoded[0].Version.TextHash == document.TextHash
                ? decoded[0].Segments.FirstOrDefault(row => row.Number == occurrence.Anchor.Segment) : null;
            if (segment is null || !input.Matches(file, cancellationToken) ||
                stylesheet is not null && !input.Matches(stylesheet, cancellationToken)) throw ContextUnavailable();
            input.EnsureFilesUnchanged(stylesheet is null ? [file.Observation] : [file.Observation, stylesheet.Observation],
                cancellationToken);
            using var liveStatus = StatusCommand(connection, null);
            ReadProjectionStatus(liveStatus);
            return segment;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            throw ContextUnavailable();
        }
    }

    private static CorpusSourceRefusal ContextUnavailable() =>
        new(CorpusSourceFailure.ContextUnavailable, "The original context for the pinned Document is unavailable.");

    private void EnsureAnchor(SqliteConnection connection, SqliteTransaction transaction, long shard, CorpusAnchor anchor)
    {
        using var command = CorpusSql.Command(connection, transaction, """
            SELECT Segment,Word,ByteOffset,ByteLength,Verse FROM IndexedCorpusOccurrences
            WHERE ShardId=$shard AND Segment=$segment AND Word=$word;
            """, ("$shard", shard), ("$segment", anchor.Segment), ("$word", anchor.Word));
        using var row = command.ExecuteReader();
        if (anchor.Offset is <= 0 || !row.Read() || ReadAnchor(row, 0) != anchor)
            throw new CorpusSourceRefusal(CorpusSourceFailure.ContextBasisMismatch,
                "The anchor does not identify an Occurrence in the pinned Document.");
    }

    private CorpusDocumentVersion FindDocument(SqliteConnection connection, SqliteTransaction transaction, string id)
    {
        using var command = CorpusSql.Command(connection, transaction,
            $"SELECT {DocumentColumns} FROM IndexedCorpusDocuments WHERE Generation=$generation AND DocumentId=$document;",
            ("$generation", Stamp.Id), ("$document", id));
        using var row = command.ExecuteReader();
        if (!row.Read())
            throw new CorpusSourceRefusal(CorpusSourceFailure.DocumentUnavailable, "The Document is absent from the pinned Source.");
        return ReadDocument(row);
    }

    private static CorpusDocumentVersion ReadDocument(SqliteDataReader row) =>
        new(row.GetString(0), row.GetString(1), row.GetInt32(2), row.GetString(3), row.GetString(4),
            row.OptionalString(5), row.GetInt64(6), row.GetInt64(7));

    private CorpusOccurrence ReadOccurrence(DocumentKey document, string contentHash, SqliteDataReader row)
        => new(new(document, contentHash, Stamp.SegmentationId, ReadAnchor(row, 1)), row.GetInt64(0), row.GetString(7),
            row.IsDBNull(9) ? null : new(row.GetString(8), row.GetString(9)), row.OptionalString(6));

    private CorpusAnchor ReadAnchor(SqliteDataReader row, int start)
    {
        var segment = row.GetInt32(start);
        var word = row.GetInt32(start + 1);
        return Binding.Format switch
        {
            CorpusSourceFormat.Sikor => new CorpusAnchor("token-index", segment, word, RawWordIndex: segment, PartIndex: word),
            CorpusSourceFormat.Usfm => new CorpusAnchor("verse", segment, word, Verse: row.OptionalString(start + 4)),
            _ => new CorpusAnchor("utf8-byte", segment, word, row.GetInt64(start + 2), row.GetInt32(start + 3)),
        };
    }

    private SqliteCommand StatusCommand(SqliteConnection connection, SqliteTransaction? transaction) =>
        CorpusSql.Command(connection, transaction, """
            SELECT g.ProjectionValid,h.Freshness FROM IndexedCorpusGenerations g
            LEFT JOIN IndexedCorpusHeads h ON h.SourceId=g.SourceId AND h.Generation=g.Id
            WHERE g.Id=$id AND g.SourceId=$source AND g.Published=1;
            """, ("$id", Stamp.Id), ("$source", Stamp.Source.SourceId));

    private static CorpusFreshness ReadProjectionStatus(SqliteCommand command)
    {
        using var row = command.ExecuteReader();
        if (!row.Read()) throw InvalidProjection();
        var status = ReadStoredStatus(row, 0, 1);
        return status == CorpusFreshness.InvalidProjection ? throw InvalidProjection() : status;
    }

    internal static CorpusFreshness ReadStoredStatus(SqliteDataReader row, int projectionColumn, int freshnessColumn)
    {
        if (row.GetValue(projectionColumn) is not long valid || valid is not (0 or 1)) throw InvalidProjection();
        var status = row.GetValue(freshnessColumn) switch
        {
            DBNull => CorpusFreshness.Historical,
            (long)CorpusFreshness.Verified => CorpusFreshness.Verified,
            (long)CorpusFreshness.Changed => CorpusFreshness.Changed,
            (long)CorpusFreshness.InvalidProjection => CorpusFreshness.InvalidProjection,
            (long)CorpusFreshness.Unavailable => CorpusFreshness.Unavailable,
            _ => throw InvalidProjection(),
        };
        return valid == 0 ? CorpusFreshness.InvalidProjection : status;
    }

    private static CorpusSourceRefusal InvalidProjection() =>
        new(CorpusSourceFailure.InvalidProjection, "The pinned Corpus projection failed verification.");

    private static CorpusFreshness EnsureSourceAvailable(SqliteCommand command) => ReadProjectionStatus(command) switch
    {
        CorpusFreshness.Changed => throw new CorpusSourceRefusal(CorpusSourceFailure.SourceChanged,
            "The Corpus Source inputs changed. Refresh the Source before reading its current projection."),
        CorpusFreshness.Unavailable => throw new CorpusSourceRefusal(CorpusSourceFailure.SourceUnavailable,
            "The Corpus Source inputs are unavailable."),
        var current => current,
    };
    private long ValidatePage(string query, int limit, CorpusCursor? cursor, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, 200);
        if (cursor is not null && (cursor.Stamp != Stamp || cursor.Query != query || cursor.After < 1))
            throw new CorpusSourceRefusal(CorpusSourceFailure.InvalidCursor, "The cursor belongs to another pinned query.");
        return cursor?.After ?? 0;
    }

    private static long? ReadPage<T>(SqliteDataReader reader, List<T> entries, int limit, int ordinalColumn,
        Func<SqliteDataReader, T> materialize, CancellationToken cancellationToken)
    {
        long last = 0;
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entries.Count == limit) return last;
            entries.Add(materialize(reader));
            last = reader.GetInt64(ordinalColumn);
        }
        return null;
    }
}

internal sealed record CorpusResolvedSelection(CorpusSelectionScope Scope,
    Func<CancellationToken, IEnumerable<WeightedWordform>> Stream);
