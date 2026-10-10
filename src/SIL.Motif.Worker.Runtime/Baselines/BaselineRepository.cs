using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Host.Store;
using SIL.Motif.Host.Texts;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Worker.Baselines;

/// <summary>One project's recorded current Baseline: its identity, where its bundle was published, and when.</summary>
public sealed record BaselineRecord(
    string ProjectKey,
    BaselineToken Token,
    string RootDirectory,
    string FwDataPath,
    DateTimeOffset SourceLastWriteUtc,
    DateTimeOffset PublishedUtc);

/// <summary>A current Baseline and the project inventory captured from the same saved state.</summary>
public sealed record CurrentBaselineEvidence(BaselineRecord Baseline, ProjectSummarySnapshot Summary);

/// <summary>A current Baseline and the stored words of the Texts one read asked for.</summary>
public sealed record CurrentBaselineTextWords(BaselineRecord Baseline, TextWordsProjection Projection)
{
    public IReadOnlyList<SIL.Motif.Contract.Responses.WritingSystemDisplay> WritingSystems { get; init; } = [];
}

public sealed record CurrentBaselineTextRows(
    BaselineRecord Baseline,
    IReadOnlyList<TextWordsProjectedText> Texts,
    long Queries);

public sealed record CurrentBaselineWordformRows(
    BaselineRecord Baseline,
    IReadOnlyList<TextWordsProjectedWordform> Wordforms,
    long Queries);

public sealed record CurrentBaselineSelectionRead(
    BaselineRecord Baseline,
    ProjectSummarySnapshot Summary,
    int TextRows,
    int WordformRows,
    int TextLines,
    int TextTokens,
    int TextAnalyses,
    int WordformAnalyses,
    int Morphs,
    long Queries);

/// <summary>A current Baseline, project display settings and selected compact Text indexes from one read.</summary>
public sealed record CurrentBaselineSelectionIndexRead(
    BaselineRecord Baseline,
    ProjectSummarySnapshot Summary,
    IReadOnlyList<BaselineTextReadIndex> Texts,
    long Queries);

/// <summary>Reads and writes the project's single current-Baseline pointer and its recorded metadata.</summary>
public sealed class BaselineRepository
{
    private readonly MotifDatabase _database;

    public BaselineRepository(MotifDatabase database) =>
        _database = database ?? throw new ArgumentNullException(nameof(database));

    public BaselineRecord? GetCurrent(string projectKey)
    {
        RequireProjectKey(projectKey);
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = SelectSql + " WHERE ProjectKey = $project;";
        command.Parameters.AddWithValue("$project", projectKey);
        RepositoryReadCounters.QueryExecuted();
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        RepositoryReadCounters.RecordDeserialized();
        return Read(reader);
    }

    /// <summary>Reads the current Baseline and its saved project summary in one SQLite read.</summary>
    public CurrentBaselineEvidence? GetCurrentEvidence(string projectKey)
    {
        RequireProjectKey(projectKey);
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = EvidenceSelectSql + " WHERE b.ProjectKey = $project;";
        command.Parameters.AddWithValue("$project", projectKey);
        RepositoryReadCounters.QueryExecuted();
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        RepositoryReadCounters.RecordDeserialized();
        var baseline = Read(reader);
        if (reader.IsDBNull(12))
            throw new InvalidDataException("The current Baseline has no stored project summary.");
        return new CurrentBaselineEvidence(baseline, ReadSummary(reader.GetString(12)));
    }

    public CurrentBaselineSelectionRead? ReadCurrentSelectionRows(
        string projectKey,
        IReadOnlyCollection<Guid> textIds,
        Action<TextWordsProjectedText> onText,
        Action<TextWordsProjectedWordform> onWordform,
        CancellationToken cancellationToken = default)
    {
        RequireProjectKey(projectKey);
        ArgumentNullException.ThrowIfNull(textIds);
        ArgumentNullException.ThrowIfNull(onText);
        ArgumentNullException.ThrowIfNull(onWordform);
        cancellationToken.ThrowIfCancellationRequested();

        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction(deferred: true);
        BaselineRecord baseline;
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = SelectSql + " WHERE ProjectKey = $project;";
            command.Parameters.AddWithValue("$project", projectKey);
            using var reader = command.ExecuteReader();
            if (!reader.Read()) return null;
            baseline = Read(reader);
        }

        string summaryJson;
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "SELECT SummaryJson FROM BaselineSummaries WHERE ProjectKey = $project;";
            command.Parameters.AddWithValue("$project", projectKey);
            summaryJson = command.ExecuteScalar() as string
                ?? throw DamagedTextWords("The project summary is missing.");
        }
        var summary = ReadSummary(summaryJson);
        var requested = textIds.Distinct().ToArray();
        var wordformIds = new HashSet<Guid>();
        var textRows = 0;
        var textLines = 0;
        var textTokens = 0;
        var textAnalyses = 0;
        var wordformAnalyses = 0;
        var morphs = 0;
        long queries = 2;
        foreach (var textId in requested)
        {
            cancellationToken.ThrowIfCancellationRequested();
            queries++;
            VisitRows<TextWordsProjectedText>(connection, transaction,
                "SELECT TextId, BundleDigest, TextJson FROM BaselineTextWords", "TextId", projectKey,
                [textId], baseline.Token.BundleDigest, IsValid, row => row.TextId, cancellationToken, (id, text) =>
                {
                    textRows++;
                    textLines += text.Lines.Count;
                    textTokens += text.Lines.Sum(line => line.Tokens.Count);
                    textAnalyses += text.Analyses.Count;
                    morphs += text.Analyses.Sum(analysis => analysis.Morphs.Count);
                    foreach (var token in text.Lines.SelectMany(line => line.Tokens))
                        if (token.WordformId is { } wordformId) wordformIds.Add(wordformId);
                    onText(text);
                });
        }

        var missingWordforms = new HashSet<Guid>(wordformIds);
        foreach (var batch in wordformIds.OrderBy(id => id.ToString("D"), StringComparer.Ordinal).Chunk(256))
        {
            cancellationToken.ThrowIfCancellationRequested();
            queries++;
            VisitRows<TextWordsProjectedWordform>(connection, transaction,
                "SELECT WordformId, BundleDigest, WordformJson FROM BaselineTextWordforms", "WordformId",
                projectKey, batch, baseline.Token.BundleDigest, IsValid, row => row.WordformId,
                cancellationToken, (id, wordform) =>
                {
                    missingWordforms.Remove(id);
                    wordformAnalyses += wordform.Analyses.Count;
                    morphs += wordform.Analyses.Sum(analysis => analysis.Morphs.Count);
                    onWordform(wordform);
                });
        }
        if (missingWordforms.Count > 0)
            throw DamagedTextWords("A token names a wordform with no stored row.");

        return new CurrentBaselineSelectionRead(baseline, summary, textRows, wordformIds.Count,
            textLines, textTokens, textAnalyses, wordformAnalyses, morphs, queries);
    }

    /// <summary>Reads selected compact Text indexes without hydrating their Text or wordform detail rows.</summary>
    public CurrentBaselineSelectionIndexRead? ReadCurrentSelectionIndexes(
        string projectKey, IReadOnlyCollection<Guid> textIds, CancellationToken cancellationToken = default)
        => ReadCurrentSelectionIndexesInSnapshot(projectKey, textIds,
            (snapshot, _, _) => snapshot, cancellationToken);

    internal T? ReadCurrentSelectionIndexesInSnapshot<T>(
        string projectKey, IReadOnlyCollection<Guid> textIds,
        Func<CurrentBaselineSelectionIndexRead, SqliteConnection, SqliteTransaction, T> select,
        CancellationToken cancellationToken = default) where T : class
    {
        RequireProjectKey(projectKey);
        ArgumentNullException.ThrowIfNull(textIds);
        ArgumentNullException.ThrowIfNull(select);
        cancellationToken.ThrowIfCancellationRequested();
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction(deferred: true);
        BaselineRecord baseline;
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = SelectSql + " WHERE ProjectKey = $project;";
            command.Parameters.AddWithValue("$project", projectKey);
            RepositoryReadCounters.QueryExecuted();
            using var reader = command.ExecuteReader();
            if (!reader.Read()) return null;
            RepositoryReadCounters.RecordDeserialized();
            baseline = Read(reader);
        }

        ProjectSummarySnapshot summary;
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "SELECT SummaryJson FROM BaselineSummaries WHERE ProjectKey = $project;";
            command.Parameters.AddWithValue("$project", projectKey);
            RepositoryReadCounters.QueryExecuted();
            summary = ReadSummary(command.ExecuteScalar() as string
                ?? throw DamagedTextWords("The project summary is missing."));
        }

        var requested = textIds.Distinct().ToArray();
        var rows = ReadRows<BaselineTextReadIndex>(connection, transaction,
            "SELECT TextId, BundleDigest, IndexJson FROM BaselineTextReadIndex", "TextId", projectKey,
            requested, baseline.Token.BundleDigest, IsValid, index => index.TextId, cancellationToken);
        if (rows.Count != requested.Length)
            throw DamagedTextWords("A selected Text has no compact read-index row.");
        var ordered = requested.Select(id => rows[id]).ToArray();
        foreach (var text in ordered) ValidateIndexReferences(text);
        var snapshot = new CurrentBaselineSelectionIndexRead(baseline, summary, ordered,
            requested.Length == 0 ? 2 : 3);
        return select(snapshot, connection, transaction);
    }

    /// <summary>
    /// Reads the current Baseline, the stored words of those <paramref name="textIds"/> it holds, in the order first
    /// requested, and every wordform their tokens use, all in one read snapshot. A Text the Baseline does not hold is
    /// left out, as it always was; <see langword="null"/> means no Baseline has been recorded.
    /// </summary>
    /// <exception cref="InvalidDataException">
    /// A row read is malformed or belongs to another Baseline, or a token names a wordform or analysis that is not
    /// stored. Its message is written for the person using the window.
    /// </exception>
    /// <exception cref="OperationCanceledException">The read was canceled.</exception>
    public CurrentBaselineTextWords? GetCurrentTextWords(string projectKey, IReadOnlyCollection<Guid> textIds,
        CancellationToken cancellationToken = default)
    {
        RequireProjectKey(projectKey);
        ArgumentNullException.ThrowIfNull(textIds);
        cancellationToken.ThrowIfCancellationRequested();
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction(deferred: true);
        BaselineRecord baseline;
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = SelectSql + " WHERE ProjectKey = $project;";
            command.Parameters.AddWithValue("$project", projectKey);
            RepositoryReadCounters.QueryExecuted();
            using var reader = command.ExecuteReader();
            if (!reader.Read()) return null;
            RepositoryReadCounters.RecordDeserialized();
            baseline = Read(reader);
        }

        var requested = textIds.Distinct().ToArray();
        var digest = baseline.Token.BundleDigest;
        var texts = ReadRows<TextWordsProjectedText>(connection, transaction,
            "SELECT TextId, BundleDigest, TextJson FROM BaselineTextWords",
            "TextId", projectKey, requested, digest, IsValid, text => text.TextId, cancellationToken);
        using var summaryCommand = connection.CreateCommand();
        summaryCommand.Transaction = transaction;
        summaryCommand.CommandText = "SELECT SummaryJson FROM BaselineSummaries WHERE ProjectKey = $project;";
        summaryCommand.Parameters.AddWithValue("$project", projectKey);
        RepositoryReadCounters.QueryExecuted();
        var summaryJson = summaryCommand.ExecuteScalar() as string
            ?? throw DamagedTextWords("The project summary is missing.");
        var summary = ReadSummary(summaryJson);
        var ordered = requested.Where(texts.ContainsKey).Select(id => texts[id]).ToArray();
        var wordformIds = ordered.SelectMany(text => text.Lines).SelectMany(line => line.Tokens)
            .Select(token => token.WordformId).OfType<Guid>().Distinct().ToArray();
        var wordforms = ReadRows<TextWordsProjectedWordform>(connection, transaction,
            "SELECT WordformId, BundleDigest, WordformJson FROM BaselineTextWordforms",
            "WordformId", projectKey, wordformIds, digest, IsValid, wordform => wordform.WordformId, cancellationToken);
        if (wordforms.Count != wordformIds.Length)
            throw DamagedTextWords("A token names a wordform with no stored row.");
        return new CurrentBaselineTextWords(baseline, new TextWordsProjection(ordered, wordforms.Values
            .OrderBy(wordform => wordform.WordformId.ToString("D"), StringComparer.Ordinal).ToArray()))
            { WritingSystems = summary.WritingSystems };
    }

    public CurrentBaselineTextRows? GetCurrentTextRows(string projectKey, IReadOnlyCollection<Guid> textIds,
        BaselineToken? expectedBaseline = null, CancellationToken cancellationToken = default)
    {
        RequireProjectKey(projectKey);
        ArgumentNullException.ThrowIfNull(textIds);
        cancellationToken.ThrowIfCancellationRequested();
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction(deferred: true);
        BaselineRecord baseline;
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = SelectSql + " WHERE ProjectKey = $project;";
            command.Parameters.AddWithValue("$project", projectKey);
            using var reader = command.ExecuteReader();
            if (!reader.Read()) return null;
            baseline = Read(reader);
        }
        if (expectedBaseline is not null && baseline.Token != expectedBaseline) return null;
        var requested = textIds.Distinct().ToArray();
        var rows = ReadRows<TextWordsProjectedText>(connection, transaction,
            "SELECT TextId, BundleDigest, TextJson FROM BaselineTextWords", "TextId", projectKey,
            requested, baseline.Token.BundleDigest, IsValid, text => text.TextId, cancellationToken);
        return new CurrentBaselineTextRows(baseline,
            requested.Where(rows.ContainsKey).Select(id => rows[id]).ToArray(), requested.Length == 0 ? 1 : 2);
    }

    public CurrentBaselineWordformRows? GetCurrentWordformRows(string projectKey, BaselineToken expectedBaseline,
        IReadOnlyCollection<Guid> wordformIds, CancellationToken cancellationToken = default)
    {
        RequireProjectKey(projectKey);
        ArgumentNullException.ThrowIfNull(expectedBaseline);
        ArgumentNullException.ThrowIfNull(wordformIds);
        cancellationToken.ThrowIfCancellationRequested();
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction(deferred: true);
        BaselineRecord baseline;
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = SelectSql + " WHERE ProjectKey = $project;";
            command.Parameters.AddWithValue("$project", projectKey);
            using var reader = command.ExecuteReader();
            if (!reader.Read()) return null;
            baseline = Read(reader);
        }
        if (baseline.Token != expectedBaseline) return null;
        var requested = wordformIds.Distinct().ToArray();
        var rows = new Dictionary<Guid, TextWordsProjectedWordform>();
        long queries = 1;
        foreach (var batch in requested.Chunk(256))
        {
            cancellationToken.ThrowIfCancellationRequested();
            queries++;
            VisitRows<TextWordsProjectedWordform>(connection, transaction,
                "SELECT WordformId, BundleDigest, WordformJson FROM BaselineTextWordforms", "WordformId",
                projectKey, batch, expectedBaseline.BundleDigest, IsValid, wordform => wordform.WordformId,
                cancellationToken, (id, wordform) => rows.Add(id, wordform));
        }
        if (rows.Count != requested.Length)
            throw DamagedTextWords("A selected Text token names a missing wordform row.");
        return new CurrentBaselineWordformRows(baseline,
            requested.OrderBy(id => id.ToString("D"), StringComparer.Ordinal).Select(id => rows[id]).ToArray(), queries);
    }

    private static ProjectSummarySnapshot ReadSummary(string json)
    {
        try
        {
            var summary = JsonSerializer.Deserialize<ProjectSummarySnapshot>(json, MotifJson.CreateOptions());
            if (summary is null || summary.WordCount < 0 || summary.OccurrenceCount < 0 ||
                summary.WordformCount < 0 || summary.RuleCount < 0 || summary.LexemeCount < 0 ||
                summary.Wordforms is null || summary.Texts is null || summary.WordWritingSystems is null ||
                summary.Texts.Any(text => text is null || text.WordCount < 0 || text.OccurrenceCount < 0 ||
                    text.InterlinearizedWordCount < 0 || text.InterlinearizedWordCount > text.WordCount ||
                    text.InterlinearizedOccurrenceCount < 0 ||
                    text.InterlinearizedOccurrenceCount > text.OccurrenceCount) ||
                summary.WritingSystems is null || summary.WritingSystems.Any(ws => ws is null ||
                    string.IsNullOrEmpty(ws.Id) || ws.Name is null || ws.Abbreviation is null ||
                    ws.FontFamily is null || ws.FontFeatures is null || !Enum.IsDefined(ws.Kind) ||
                    ws.Position < 0 || ws.StyleSizes is null ||
                    ws.StyleFonts is null || !ws.StyleSizes.Keys.Order().SequenceEqual(ws.StyleFonts.Keys.Order()) ||
                    ws.StyleFonts.Values.Any(font => font is null || font.FontFamily is null || font.FontFeatures is null) ||
                    ws.StyleSizes.Values.Any(size => !double.IsFinite(size) || size <= 0) ||
                    SIL.Motif.Host.WritingSystems.WritingSystemDisplayReader.Styles.Any(style =>
                        !ws.StyleSizes.TryGetValue(style, out var size) || !double.IsFinite(size) || size <= 0)))
                throw new JsonException("The stored project summary has invalid counts or display settings.");
            RepositoryReadCounters.RecordDeserialized(System.Text.Encoding.UTF8.GetByteCount(json));
            return summary;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The stored Baseline project summary is malformed. Refresh the project to rebuild it.",
                exception);
        }
    }

    // Filters by json_each so any number of ids binds as one parameter; each row must match its Baseline and key.
    private static Dictionary<Guid, T> ReadRows<T>(SqliteConnection connection, SqliteTransaction transaction,
        string select, string idColumn, string projectKey, IReadOnlyCollection<Guid> ids, string digest,
        Func<T?, bool> isValid, Func<T, Guid> idOf, CancellationToken cancellationToken) where T : class
    {
        var rows = new Dictionary<Guid, T>();
        VisitRows(connection, transaction, select, idColumn, projectKey, ids, digest, isValid, idOf,
            cancellationToken, (id, row) => rows.Add(id, row));
        return rows;
    }

    private static int VisitRows<T>(SqliteConnection connection, SqliteTransaction transaction,
        string select, string idColumn, string projectKey, IReadOnlyCollection<Guid> ids, string digest,
        Func<T?, bool> isValid, Func<T, Guid> idOf, CancellationToken cancellationToken,
        Action<Guid, T> visit) where T : class
    {
        if (ids.Count == 0) return 0;
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = select +
            $" WHERE ProjectKey = $project AND {idColumn} IN (SELECT value FROM json_each($ids));";
        command.Parameters.AddWithValue("$project", projectKey);
        command.Parameters.AddWithValue("$ids", JsonSerializer.Serialize(ids.Select(id => id.ToString("D"))));
        RepositoryReadCounters.QueryExecuted();
        using var reader = command.ExecuteReader();
        var count = 0;
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!string.Equals(reader.GetString(1), digest, StringComparison.Ordinal))
                throw DamagedTextWords("A stored row belongs to another Baseline.");
            var json = reader.GetString(2);
            T? row;
            try
            {
                row = JsonSerializer.Deserialize<T>(json, TextWordsJson);
            }
            catch (JsonException exception)
            {
                throw DamagedTextWords("A stored row is not valid JSON.", exception);
            }
            if (!isValid(row) || !Guid.TryParse(reader.GetString(0), out var key) || idOf(row!) != key)
                throw DamagedTextWords("A stored row has an invalid shape.");
            var occurrences = row switch
            {
                BaselineTextReadIndex index => index.Lines.Sum(line => line.Tokens.Count(token => token.WordformId is not null)),
                TextWordsProjectedText text => text.Lines.Sum(line => line.Tokens.Count(token => token.WordformId is not null)),
                _ => 0,
            };
            RepositoryReadCounters.RecordDeserialized(System.Text.Encoding.UTF8.GetByteCount(json), occurrences);
            visit(key, row!);
            count++;
        }
        return count;
    }

    private static InvalidDataException DamagedTextWords(string detail, Exception? cause = null) =>
        new("Motif's stored words for this Baseline are missing or damaged. Delete the refused store and let Motif recreate it.",
            new InvalidDataException(detail, cause));

    // BaselinePublication is internal: every caller (BaselineCapturePublisher, BaselineRefresh) lives here too.
    internal BaselineRecord Record(
        string projectKey, BaselinePublication publication, DateTimeOffset publishedUtc,
        DateTimeOffset sourceLastWriteUtc, TextWordsProjection textWordsProjection,
        ProjectSummarySnapshot? projectSummary = null)
    {
        RequireProjectKey(projectKey);
        ArgumentNullException.ThrowIfNull(publication);
        ArgumentNullException.ThrowIfNull(textWordsProjection);
        if (publishedUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("The publication time must be UTC.", nameof(publishedUtc));
        if (sourceLastWriteUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("The source last-write time must be UTC.", nameof(sourceLastWriteUtc));
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        using (var deleteProjection = connection.CreateCommand())
        {
            deleteProjection.Transaction = transaction;
            deleteProjection.CommandText = "DELETE FROM BaselineTextReadIndex WHERE ProjectKey = $project; " +
                "DELETE FROM BaselineTextWords WHERE ProjectKey = $project; " +
                "DELETE FROM BaselineTextWordforms WHERE ProjectKey = $project;";
            deleteProjection.Parameters.AddWithValue("$project", projectKey);
            deleteProjection.ExecuteNonQuery();
        }
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO Baselines
                (ProjectKey, ProjectIdentity, SemanticSnapshotDigest, ProjectionVersion, CapturedUtc,
                 BundleDigest, CapturedHostSessionId, CapturedEditGeneration, RootDirectory, FwDataPath, PublishedUtc,
                 SourceLastWriteUtc)
            VALUES
                ($project, $identity, $semantic, $projection, $captured, $bundle, $hostSession,
                 $editGeneration, $root, $fwdata, $published, $sourceLastWrite)
            ON CONFLICT(ProjectKey) DO UPDATE SET
                ProjectIdentity = excluded.ProjectIdentity,
                SemanticSnapshotDigest = excluded.SemanticSnapshotDigest,
                ProjectionVersion = excluded.ProjectionVersion,
                CapturedUtc = excluded.CapturedUtc,
                BundleDigest = excluded.BundleDigest,
                CapturedHostSessionId = excluded.CapturedHostSessionId,
                CapturedEditGeneration = excluded.CapturedEditGeneration,
                RootDirectory = excluded.RootDirectory,
                FwDataPath = excluded.FwDataPath,
                PublishedUtc = excluded.PublishedUtc,
                SourceLastWriteUtc = excluded.SourceLastWriteUtc
            WHERE Baselines.BundleDigest <> excluded.BundleDigest;
            """;
        AddParameters(command, projectKey, publication, publishedUtc, sourceLastWriteUtc);
        command.ExecuteNonQuery();
        using (var summaryCommand = connection.CreateCommand())
        {
            summaryCommand.Transaction = transaction;
            summaryCommand.CommandText = """
                INSERT INTO BaselineSummaries (ProjectKey, SummaryJson) VALUES ($project, $summary)
                ON CONFLICT(ProjectKey) DO UPDATE SET SummaryJson = excluded.SummaryJson;
                """;
            summaryCommand.Parameters.AddWithValue("$project", projectKey);
            summaryCommand.Parameters.AddWithValue("$summary", JsonSerializer.Serialize(
                projectSummary ?? ProjectSummarySnapshot.Empty, MotifJson.CreateOptions()));
            summaryCommand.ExecuteNonQuery();
        }
        InsertRows(connection, transaction,
            "INSERT INTO BaselineTextWords (ProjectKey, TextId, BundleDigest, TextJson) VALUES ($project, $id, $bundle, $json);",
            projectKey, publication.Token.BundleDigest, textWordsProjection.Texts, text => text.TextId);
        InsertRows(connection, transaction,
            "INSERT INTO BaselineTextWordforms (ProjectKey, WordformId, BundleDigest, WordformJson) " +
            "VALUES ($project, $id, $bundle, $json);",
            projectKey, publication.Token.BundleDigest, textWordsProjection.Wordforms, wordform => wordform.WordformId);
        InsertRows(connection, transaction,
            "INSERT INTO BaselineTextReadIndex (ProjectKey, TextId, BundleDigest, IndexJson) " +
            "VALUES ($project, $id, $bundle, $json);",
            projectKey, publication.Token.BundleDigest,
            BaselineTextReadIndexBuilder.Build(textWordsProjection), text => text.TextId);
        command.CommandText = SelectSql + " WHERE ProjectKey = $project;";
        using var reader = command.ExecuteReader();
        if (!reader.Read()) throw new InvalidOperationException("The Baseline publication was not recorded.");
        var result = Read(reader);
        reader.Close();
        transaction.Commit();
        return result;
    }

    private static void AddParameters(SqliteCommand command, string projectKey,
        BaselinePublication publication, DateTimeOffset publishedUtc, DateTimeOffset sourceLastWriteUtc)
    {
        var token = publication.Token;
        command.Parameters.AddWithValue("$project", projectKey);
        command.Parameters.AddWithValue("$identity", token.ProjectIdentity);
        command.Parameters.AddWithValue("$semantic", token.SemanticSnapshotDigest);
        command.Parameters.AddWithValue("$projection", token.ProjectionVersion);
        command.Parameters.AddWithValue("$captured", token.CapturedUtc);
        command.Parameters.AddWithValue("$bundle", token.BundleDigest);
        command.Parameters.AddWithValue("$hostSession", (object?)token.CapturedHostSessionId ?? DBNull.Value);
        command.Parameters.AddWithValue("$editGeneration", (object?)token.CapturedEditGeneration ?? DBNull.Value);
        command.Parameters.AddWithValue("$root", Path.GetFullPath(publication.RootDirectory));
        command.Parameters.AddWithValue("$fwdata", Path.GetFullPath(publication.FwDataPath));
        command.Parameters.AddWithValue("$published", publishedUtc.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$sourceLastWrite",
            sourceLastWriteUtc.ToString("O", CultureInfo.InvariantCulture));
    }

    private static BaselineRecord Read(SqliteDataReader reader)
    {
        try
        {
            var token = new BaselineToken(reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.GetString(4), reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetInt64(7));
            var published = DateTimeOffset.ParseExact(reader.GetString(10), "O", CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind);
            if (published.Offset != TimeSpan.Zero)
                throw new InvalidDataException("The persisted Baseline publication time is not UTC.");
            var sourceLastWrite = DateTimeOffset.ParseExact(reader.GetString(11), "O", CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind);
            if (sourceLastWrite.Offset != TimeSpan.Zero)
                throw new InvalidDataException("The persisted Baseline source last-write time is not UTC.");
            return new BaselineRecord(reader.GetString(0), token, Path.GetFullPath(reader.GetString(8)),
                Path.GetFullPath(reader.GetString(9)), sourceLastWrite, published);
        }
        catch (InvalidDataException) { throw; }
        catch (Exception exception) when (exception is ArgumentException or FormatException or InvalidCastException or
            InvalidOperationException or OverflowException)
        {
            throw new InvalidDataException("The persisted Baseline row is malformed.", exception);
        }
    }

    private static void InsertRows<T>(SqliteConnection connection, SqliteTransaction transaction, string insert,
        string projectKey, string digest, IEnumerable<T> rows, Func<T, Guid> idOf)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = insert;
        command.Parameters.AddWithValue("$project", projectKey);
        command.Parameters.AddWithValue("$bundle", digest);
        var id = command.Parameters.Add("$id", SqliteType.Text);
        var json = command.Parameters.Add("$json", SqliteType.Text);
        foreach (var row in rows)
        {
            id.Value = idOf(row).ToString("D");
            json.Value = JsonSerializer.Serialize(row, TextWordsJson);
            command.ExecuteNonQuery();
        }
    }

    private static bool IsValid(TextWordsProjectedText? text)
    {
        if (text?.Title is null || text.Lines is null || text.Analyses is null) return false;
        var keys = new HashSet<string>(StringComparer.Ordinal);
        if (!text.Analyses.All(analysis => analysis is not null && IsValid(analysis) && keys.Add(analysis.Key)))
            return false;
        foreach (var line in text.Lines)
        {
            if (line is null || line.Number < 1 || line.Sentence is null || string.IsNullOrEmpty(line.SentenceStyle) || line.Tokens is null ||
                line.ParagraphId == Guid.Empty || line.SegmentId == Guid.Empty) return false;
            foreach (var token in line.Tokens)
                if (token is null || token.Text is null || token.Forms is null || token.Forms.Any(form => form?.Text is null || string.IsNullOrEmpty(form.WritingSystem)) ||
                    (token.WordformId is null) != (token.Status is null) ||
                    token.OccurrenceIndex < 0 ||
                    (token.AnalysisId is null) != (token.AnalysisKey is null) ||
                    token.AnalysisKey is not null && !keys.Contains(token.AnalysisKey))
                    return false;
        }
        return true;
    }

    private static bool IsValid(BaselineTextReadIndex? text)
    {
        if (text is null || text.TextId == Guid.Empty || text.Title is null || text.Lines is null ||
            text.Wordforms is null || text.Lines.Any(line => line is null || line.Number < 1 ||
                line.ParagraphId == Guid.Empty || line.SegmentId == Guid.Empty || line.Tokens is null ||
                line.Tokens.Any(token => token is null || token.OccurrenceIndex < 0 || token.Forms is null ||
                    token.Forms.Any(form => form is null || form.Text is null ||
                        string.IsNullOrEmpty(form.WritingSystem)) ||
                    (token.WordformId is null) != (token.Status is null) ||
                    (token.AnalysisId is null) != (token.AnalysisKey is null))) ||
            text.Wordforms.Any(wordform => wordform is null || wordform.WordformId == Guid.Empty ||
                wordform.ApprovedCount < 0 || wordform.CandidateCount < 0 || wordform.DisapprovedCount < 0 ||
                wordform.Analyses is null || wordform.Analyses.Any(analysis => analysis is null ||
                    string.IsNullOrWhiteSpace(analysis.Key) || analysis.AnalysisId == Guid.Empty ||
                    analysis.Opinion is not ("approved" or "disapproved" or "unknown") ||
                    analysis.Identity is not { Morphs: not null } ||
                    analysis.Identity.SourceAnalysisId != CanonicalId.FromGuid(analysis.AnalysisId).Value ||
                    analysis.Identity.Morphs.Any(morph => morph is null || morph.Forms is null ||
                        morph.Forms.Any(form => form is null)))) ||
            text.Wordforms.Select(wordform => wordform.WordformId).Distinct().Count() != text.Wordforms.Count ||
            text.Lines.Select(line => line.Number).Distinct().Count() != text.Lines.Count)
            return false;
        foreach (var wordform in text.Wordforms)
        {
            var analyses = wordform.Analyses;
            if (analyses.Select(analysis => analysis.Key).Distinct(StringComparer.Ordinal).Count() != analyses.Count ||
                analyses.Select(analysis => analysis.AnalysisId).Distinct().Count() != analyses.Count ||
                wordform.ApprovedCount != analyses.Count(analysis => analysis.Opinion == "approved") ||
                wordform.CandidateCount != analyses.Count(analysis => analysis.Opinion == "unknown") ||
                wordform.DisapprovedCount != analyses.Count(analysis => analysis.Opinion == "disapproved"))
                return false;
        }

        var byWordform = text.Wordforms.ToDictionary(wordform => wordform.WordformId);
        return text.Lines.SelectMany(line => line.Tokens).All(token =>
        {
            if (token.AnalysisKey is null) return true;
            if (token.WordformId is not { } wordformId || !byWordform.TryGetValue(wordformId, out var wordform))
                return false;
            return wordform.Analyses.Any(analysis => analysis.Key == token.AnalysisKey &&
                analysis.AnalysisId == token.AnalysisId);
        });
    }

    private static void ValidateIndexReferences(BaselineTextReadIndex text)
    {
        var wordforms = text.Wordforms.Select(wordform => wordform.WordformId).ToHashSet();
        if (text.Lines.SelectMany(line => line.Tokens).Any(token =>
                token.WordformId is { } id && !wordforms.Contains(id)))
            throw DamagedTextWords("A compact Text index references an absent wordform identity.");
    }

    private static bool IsValid(TextWordsProjectedWordform? wordform)
    {
        if (wordform is not { Approved: not null, Disapproved: not null, Analyses: not null, CandidateCount: >= 0 })
            return false;
        if (wordform.Analyses.Any(analysis => analysis is null || !IsValid(analysis)) ||
            wordform.Analyses.Select(analysis => analysis.AnalysisId).Distinct().Count() != wordform.Analyses.Count)
            return false;
        var byId = wordform.Analyses.ToDictionary(analysis => analysis.AnalysisId);
        return wordform.Approved.All(analysis => IsValid(analysis) &&
                   byId.TryGetValue(analysis.AnalysisId, out var stored) && stored.Opinion == "approved") &&
               wordform.Disapproved.All(analysis => IsValid(analysis) &&
                   byId.TryGetValue(analysis.AnalysisId, out var stored) && stored.Opinion == "disapproved") &&
               wordform.CandidateCount == wordform.Analyses.Count(analysis => analysis.Opinion == "unknown") &&
               wordform.Approved.Count == wordform.Analyses.Count(analysis => analysis.Opinion == "approved") &&
               wordform.Disapproved.Count == wordform.Analyses.Count(analysis => analysis.Opinion == "disapproved");
    }

    private static bool IsValid(TextWordsProjectedAnalysis analysis) =>
        !string.IsNullOrWhiteSpace(analysis.Key) && analysis.AnalysisId != Guid.Empty &&
        analysis.Opinion is "approved" or "disapproved" or "unknown" && analysis.Identity is { Morphs: not null } &&
        analysis.Identity.SourceAnalysisId == CanonicalId.FromGuid(analysis.AnalysisId).Value &&
        analysis.Identity.Morphs.All(morph => morph is not null && morph.Forms is not null &&
            morph.Forms.All(form => form is not null)) && analysis.Morphs is not null &&
        analysis.Morphs.All(morph => morph is not null && morph.Form is not null && morph.Gloss is not null &&
            morph.Category is not null && (morph.LinkTarget is null || !string.IsNullOrWhiteSpace(morph.LinkTarget.Tool)));

    // Unescaped language text remains readable; explicit null tags preserve unknown or composed text.
    private static readonly JsonSerializerOptions TextWordsJson = new(MotifJson.CreateOptions())
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static void RequireProjectKey(string projectKey)
    {
        if (string.IsNullOrWhiteSpace(projectKey))
            throw new ArgumentException("A project workspace key is required.", nameof(projectKey));
    }

    private const string SelectSql = "SELECT ProjectKey, ProjectIdentity, SemanticSnapshotDigest, " +
        "ProjectionVersion, CapturedUtc, BundleDigest, CapturedHostSessionId, CapturedEditGeneration, " +
        "RootDirectory, FwDataPath, PublishedUtc, SourceLastWriteUtc FROM Baselines";

    private const string EvidenceSelectSql = "SELECT b.ProjectKey, b.ProjectIdentity, b.SemanticSnapshotDigest, " +
        "b.ProjectionVersion, b.CapturedUtc, b.BundleDigest, b.CapturedHostSessionId, b.CapturedEditGeneration, " +
        "b.RootDirectory, b.FwDataPath, b.PublishedUtc, b.SourceLastWriteUtc, s.SummaryJson " +
        "FROM Baselines b LEFT JOIN BaselineSummaries s ON s.ProjectKey = b.ProjectKey";
}
