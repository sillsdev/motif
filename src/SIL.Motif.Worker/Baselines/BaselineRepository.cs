using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Host.Store;
using SIL.Motif.Host.Texts;

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

/// <summary>A current Baseline and its complete stored Text words projection.</summary>
public sealed record CurrentBaselineTextWords(BaselineRecord Baseline, TextWordsProjection Projection);

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
        using var reader = command.ExecuteReader();
        return reader.Read() ? Read(reader) : null;
    }

    /// <summary>Reads the current Baseline and its saved project summary in one SQLite read.</summary>
    public CurrentBaselineEvidence? GetCurrentEvidence(string projectKey)
    {
        RequireProjectKey(projectKey);
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = EvidenceSelectSql + " WHERE b.ProjectKey = $project;";
        command.Parameters.AddWithValue("$project", projectKey);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        var baseline = Read(reader);
        if (reader.IsDBNull(12))
            throw new InvalidDataException("The current Baseline has no stored project summary.");
        try
        {
            var summary = JsonSerializer.Deserialize<ProjectSummarySnapshot>(
                reader.GetString(12), MotifJson.CreateOptions());
            if (summary is null || summary.WordCount < 0 || summary.OccurrenceCount < 0 ||
                summary.WordformCount < 0 || summary.RuleCount < 0 || summary.LexemeCount < 0 ||
                summary.Wordforms is null || summary.Texts is null)
                throw new JsonException("The stored project summary has invalid counts or collections.");
            return new CurrentBaselineEvidence(baseline, summary);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The stored Baseline project summary is malformed.", exception);
        }
    }

    /// <summary>Reads the current Baseline and its matching Text words projection in one SQLite read.</summary>
    public CurrentBaselineTextWords? GetCurrentTextWords(string projectKey)
    {
        RequireProjectKey(projectKey);
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = TextWordsEvidenceSelectSql + " WHERE b.ProjectKey = $project;";
        command.Parameters.AddWithValue("$project", projectKey);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        var baseline = Read(reader);
        if (reader.IsDBNull(12))
            throw new InvalidDataException("The current Baseline has no matching Text words projection.");
        try
        {
            var projection = JsonSerializer.Deserialize<TextWordsProjection>(reader.GetString(12), MotifJson.CreateOptions());
            if (!IsValid(projection))
                throw new JsonException("The stored Text words projection has an invalid shape.");
            return new CurrentBaselineTextWords(baseline, projection!);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The stored Baseline Text words projection is malformed.", exception);
        }
    }

    // BaselinePublication is internal: every caller (BaselineCapturePublisher, BaselineRefresh) lives here too.
    internal BaselineRecord Record(
        string projectKey, BaselinePublication publication, DateTimeOffset publishedUtc,
        DateTimeOffset sourceLastWriteUtc, ProjectSummarySnapshot? projectSummary = null,
        TextWordsProjection? textWordsProjection = null)
    {
        RequireProjectKey(projectKey);
        ArgumentNullException.ThrowIfNull(publication);
        if (publishedUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("The publication time must be UTC.", nameof(publishedUtc));
        if (sourceLastWriteUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("The source last-write time must be UTC.", nameof(sourceLastWriteUtc));
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        using (var deleteProjection = connection.CreateCommand())
        {
            deleteProjection.Transaction = transaction;
            deleteProjection.CommandText = "DELETE FROM BaselineTextWords WHERE ProjectKey = $project;";
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
        if (textWordsProjection is not null)
        {
            using var textWordsCommand = connection.CreateCommand();
            textWordsCommand.Transaction = transaction;
            textWordsCommand.CommandText = """
                INSERT INTO BaselineTextWords (ProjectKey, BundleDigest, ProjectionJson)
                VALUES ($project, $bundle, $projection);
                """;
            textWordsCommand.Parameters.AddWithValue("$project", projectKey);
            textWordsCommand.Parameters.AddWithValue("$bundle", publication.Token.BundleDigest);
            textWordsCommand.Parameters.AddWithValue("$projection",
                JsonSerializer.Serialize(textWordsProjection, MotifJson.CreateOptions()));
            textWordsCommand.ExecuteNonQuery();
        }
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

    private static bool IsValid(TextWordsProjection? projection)
    {
        if (projection?.Texts is null || projection.Wordforms is null ||
            projection.Texts.Any(text => text is null || text.Title is null || text.Lines is null) ||
            projection.Texts.Select(text => text.TextId).Distinct().Count() != projection.Texts.Count ||
            projection.Wordforms.Any(wordform => wordform is null || wordform.CandidateCount < 0 ||
                wordform.Approved is null || wordform.Disapproved is null) ||
            projection.Wordforms.Select(wordform => wordform.WordformId).Distinct().Count() != projection.Wordforms.Count)
            return false;

        foreach (var text in projection.Texts)
        foreach (var line in text.Lines)
        {
            if (line is null || line.Number < 1 || line.Sentence is null || line.Tokens is null) return false;
            foreach (var token in line.Tokens)
                if (token is null || token.Text is null || token.Forms is null || token.Forms.Any(form => form is null) ||
                    token.Analysis is not null && !IsValid(token.Analysis))
                    return false;
        }

        return projection.Wordforms.SelectMany(wordform => wordform.Approved.Concat(wordform.Disapproved))
            .All(IsValid);
    }

    private static bool IsValid(TextWordsProjectedAnalysis analysis) =>
        !string.IsNullOrWhiteSpace(analysis.Key) && analysis.Morphs is not null &&
        analysis.Morphs.All(morph => morph is not null && morph.Form is not null && morph.Gloss is not null &&
            morph.Category is not null && (morph.LinkTarget is null || !string.IsNullOrWhiteSpace(morph.LinkTarget.Tool)));

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

    private const string TextWordsEvidenceSelectSql = "SELECT b.ProjectKey, b.ProjectIdentity, b.SemanticSnapshotDigest, " +
        "b.ProjectionVersion, b.CapturedUtc, b.BundleDigest, b.CapturedHostSessionId, b.CapturedEditGeneration, " +
        "b.RootDirectory, b.FwDataPath, b.PublishedUtc, b.SourceLastWriteUtc, p.ProjectionJson " +
        "FROM Baselines b LEFT JOIN BaselineTextWords p ON p.ProjectKey = b.ProjectKey " +
        "AND p.BundleDigest = b.BundleDigest";
}
