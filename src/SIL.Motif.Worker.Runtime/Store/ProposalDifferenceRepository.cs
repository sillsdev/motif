using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Store;

namespace SIL.Motif.Worker.Store;

/// <summary>One word of a Difference ready to store, with the Text positions it stands for.</summary>
public sealed record NewProposalDifferenceWord(
    string Word, ProposalDifferenceCaseFacts Facts, IReadOnlyList<ProposalDifferenceOccurrence> Occurrences);

/// <summary>One physical place in a Text where a compared word occurs.</summary>
public sealed record ProposalDifferenceOccurrence(string TextId, string SegmentId, int OccurrenceIndex, int LineNumber);

/// <summary>A complete Difference to store: its summary and every compared word.</summary>
public sealed record NewProposalDifference(ProposalDifferenceSummary Summary, IReadOnlyList<NewProposalDifferenceWord> Words);

/// <summary>Which stored words a read keeps; every set member must hold.</summary>
/// <param name="CouldBeBad">Keep only words filed under a could-be-bad category.</param>
/// <param name="Bucket">Keep only words in this category.</param>
/// <param name="BeforeState">Keep only words with this approval state before.</param>
/// <param name="AfterState">Keep only words with this approval state after.</param>
/// <param name="Limitation">Keep only words where either side was held back by this limitation.</param>
/// <param name="Word">Keep only this exact word, as stored (normalised to decomposed form).</param>
public sealed record ProposalDifferenceFilter(
    bool CouldBeBad = false, ProposalDifferenceBucket? Bucket = null, string? BeforeState = null, string? AfterState = null,
    HumanAgreementLimitation? Limitation = null, string? Word = null);

/// <summary>Which page of one word's Text places a read returns.</summary>
public sealed record ProposalDifferenceOccurrenceRequest(string Word, int Offset, int Limit);

/// <summary>A saved Difference's summary and whether its word detail is still stored.</summary>
public sealed record SavedProposalDifference(ProposalDifferenceSummary Summary, bool WordDetailKept);

/// <summary>The Differences a Proposal has saved, newest first, bounded to the most listed.</summary>
/// <param name="Items">The newest saved Differences, at most <see cref="ProposalDifferenceRepository.MostListed"/>.</param>
/// <param name="Total">How many the Proposal has saved, before the bound.</param>
/// <param name="Unreadable">How many of the newest ones could not be read, so they are not in <paramref name="Items"/>.</param>
public sealed record ProposalDifferenceList(IReadOnlyList<ProposalDifferenceSaved> Items, int Total, int Unreadable = 0);

/// <summary>The Proposal was decided before its Difference could be stored, so full word rows would outlive their purpose.</summary>
public sealed class ProposalDecidedException : InvalidOperationException
{
    public ProposalDecidedException() : base("The Proposal is decided, so a new Difference cannot be stored for it.") { }
}

/// <summary>A saved Difference's summary and one page of its words, all read from the same snapshot.</summary>
public sealed record ProposalDifferencePage(
    SavedProposalDifference Saved, int Matching, IReadOnlyList<ProposalDifferenceWord> Words,
    ProposalDifferenceOccurrencePage? Occurrences = null);

/// <summary>Stores a Proposal Difference in one transaction and reads it back by summary or page.</summary>
public sealed class ProposalDifferenceRepository
{
    // One options instance for all rows: fresh options per row rebuilt the converter metadata every time.
    private static readonly JsonSerializerOptions Json = MotifJson.CreateOptions();

    /// <summary>The most saved Differences one Proposal's list names; the rest are counted.</summary>
    public const int MostListed = 50;

    private readonly MotifDatabase _database;

    public ProposalDifferenceRepository(MotifDatabase database) =>
        _database = database ?? throw new ArgumentNullException(nameof(database));

    /// <summary>
    /// Stores the summary, words, Text positions and categories together, or none of them, replacing any Difference
    /// the Proposal already has for the same Text and reference, and no other. The Proposal's status is read inside
    /// that transaction.
    /// </summary>
    /// <param name="difference">The summary and its words.</param>
    /// <param name="cancellationToken">Checked once more just before the commit; a cancel rolls everything back.</param>
    /// <exception cref="ProposalDecidedException">The Proposal is decided, so nothing is stored.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled before the commit, so nothing is stored.</exception>
    public void Publish(NewProposalDifference difference, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(difference);
        var summary = difference.Summary;
        var summaryJson = JsonSerializer.Serialize(summary, Json);
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        using (var status = connection.CreateCommand())
        {
            status.Transaction = transaction;
            status.CommandText = "SELECT Status FROM Proposals WHERE ProposalId = $proposal;";
            status.Parameters.AddWithValue("$proposal", summary.ProposalId);
            if (status.ExecuteScalar() is string current && ProposalLifecycle.IsTerminal(current))
                throw new ProposalDecidedException();
        }
        // The child rows go with it: their foreign keys cascade.
        Execute(connection, transaction,
            "DELETE FROM ProposalDifferences WHERE ProposalId = $proposal AND TextId = $text AND Reference = $reference;",
            ("$proposal", summary.ProposalId), ("$text", summary.TextId), ("$reference", summary.Reference));
        Execute(connection, transaction,
            "INSERT INTO ProposalDifferences (DifferenceId, ProposalId, TrialJobId, BeforeAssessmentId, " +
            "AfterAssessmentId, TextId, Reference, BaselineToken, SummaryJson, SummaryDigest, CreatedUtc) VALUES " +
            "($id, $proposal, $job, $before, $after, $text, $reference, $baseline, $json, $digest, $created);",
            ("$id", summary.DifferenceId), ("$proposal", summary.ProposalId), ("$job", summary.TrialJobId),
            ("$before", summary.BeforeAssessmentId), ("$after", summary.AfterAssessmentId), ("$text", summary.TextId),
            ("$reference", summary.Reference), ("$baseline", summary.BaselineToken), ("$json", summaryJson), ("$digest", Sha(summaryJson)),
            ("$created", summary.CreatedUtc));
        foreach (var word in difference.Words)
        {
            var factsJson = JsonSerializer.Serialize(word.Facts, Json);
            Execute(connection, transaction,
                "INSERT INTO ProposalDifferenceCases (DifferenceId, CaseKey, Word, OccurrenceCount, BeforeState, " +
                "AfterState, CouldBeBad, FactsJson, FactsDigest) VALUES ($id, $key, $word, $count, $before, $after, " +
                "$bad, $json, $digest);",
                ("$id", summary.DifferenceId), ("$key", word.Word), ("$word", word.Word),
                ("$count", word.Occurrences.Count), ("$before", StateName(word.Facts.Before)),
                ("$after", StateName(word.Facts.After)), ("$bad", word.Facts.CouldBeBad ? 1 : 0),
                ("$json", factsJson), ("$digest", Sha(factsJson)));
            foreach (var occurrence in word.Occurrences)
                Execute(connection, transaction,
                    "INSERT INTO ProposalDifferenceOccurrences (DifferenceId, TextId, SegmentId, OccurrenceIndex, " +
                    "LineNumber, CaseKey) VALUES ($id, $text, $segment, $index, $line, $key);",
                    ("$id", summary.DifferenceId), ("$text", occurrence.TextId), ("$segment", occurrence.SegmentId),
                    ("$index", occurrence.OccurrenceIndex), ("$line", occurrence.LineNumber), ("$key", word.Word));
            foreach (var bucket in word.Facts.Buckets.Select(item => item.Bucket).Distinct())
                Execute(connection, transaction,
                    "INSERT INTO ProposalDifferenceBuckets (DifferenceId, CaseKey, Bucket) VALUES ($id, $key, $bucket);",
                    ("$id", summary.DifferenceId), ("$key", word.Word), ("$bucket", BucketName(bucket)));
        }
        cancellationToken.ThrowIfCancellationRequested();
        transaction.Commit();
    }

    /// <summary>The wire name of an approval state, or "unavailable" when no state could be established.</summary>
    public static string StateName(HumanAgreementFacts facts) => facts.ApprovedState is { } state
        ? ApprovalStateName(state) : "unavailable";

    /// <summary>The wire name of an approval state.</summary>
    public static string ApprovalStateName(HumanAgreementApprovalState state) =>
        WireName(state, new StrictStringEnumConverter<HumanAgreementApprovalState>());

    /// <summary>The stable wire code of a category.</summary>
    public static string BucketName(ProposalDifferenceBucket bucket) =>
        WireName(bucket, new StrictStringEnumConverter<ProposalDifferenceBucket>());

    /// <summary>The stable wire code of a limitation.</summary>
    public static string LimitationName(HumanAgreementLimitation limitation) =>
        WireName(limitation, new StrictStringEnumConverter<HumanAgreementLimitation>());

    private static string WireName<T>(T value, System.Text.Json.Serialization.JsonConverter<T> converter)
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(converter);
        return JsonSerializer.Serialize(value, options).Trim('"');
    }

    /// <summary>
    /// The newest Difference id saved for a Proposal, narrowed to a Text and a reference when given, or null when
    /// it has none.
    /// </summary>
    public string? LatestFor(string proposalId, string? textId = null, string? reference = null)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT DifferenceId FROM ProposalDifferences WHERE ProposalId = $proposal " +
            "AND ($text IS NULL OR TextId = $text) AND ($reference IS NULL OR Reference = $reference) " +
            "ORDER BY CreatedUtc DESC, DifferenceId DESC LIMIT 1;";
        command.Parameters.AddWithValue("$proposal", proposalId);
        command.Parameters.AddWithValue("$text", (object?)textId ?? DBNull.Value);
        command.Parameters.AddWithValue("$reference", (object?)reference ?? DBNull.Value);
        return command.ExecuteScalar() as string;
    }

    /// <summary>
    /// The Differences a Proposal has saved, one per Text and reference, newest first, so a caller can say which
    /// Text and reference each belongs to. Only the first <see cref="MostListed"/> are named; the total counts all.
    /// A summary that no longer matches its digest or cannot be read is left out and counted, not thrown.
    /// </summary>
    public ProposalDifferenceList ListFor(string proposalId)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT SummaryJson, SummaryDigest, WordDetailDroppedUtc, COUNT(*) OVER () " +
            "FROM ProposalDifferences WHERE ProposalId = $proposal " +
            "ORDER BY CreatedUtc DESC, DifferenceId DESC LIMIT $limit;";
        command.Parameters.AddWithValue("$proposal", proposalId);
        command.Parameters.AddWithValue("$limit", MostListed);
        var items = new List<ProposalDifferenceSaved>();
        var total = 0;
        var unreadable = 0;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            total = reader.GetInt32(3);
            var json = reader.GetString(0);
            ProposalDifferenceSummary summary;
            try
            {
                summary = string.Equals(Sha(json), reader.GetString(1), StringComparison.Ordinal)
                    ? Read<ProposalDifferenceSummary>(json, "summary") : throw new InvalidDataException("Digest mismatch.");
            }
            catch (InvalidDataException)
            {
                unreadable++;
                continue;
            }
            items.Add(new ProposalDifferenceSaved(summary.DifferenceId, summary.TextId, summary.TextTitle,
                summary.Reference, summary.CreatedUtc, reader.IsDBNull(2)));
        }
        return new ProposalDifferenceList(items, total, unreadable);
    }

    /// <summary>The stored summary, refused when its bytes no longer match the digest saved with it.</summary>
    public ProposalDifferenceSummary? GetSummary(string differenceId) => GetSaved(differenceId)?.Summary;

    /// <summary>The stored summary and whether word detail is still kept, or null when there is no such Difference.</summary>
    public SavedProposalDifference? GetSaved(string differenceId)
    {
        using var connection = _database.OpenConnection();
        return ReadSaved(connection, null, differenceId);
    }

    private static SavedProposalDifference? ReadSaved(SqliteConnection connection, SqliteTransaction? transaction,
        string differenceId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT SummaryJson, SummaryDigest, WordDetailDroppedUtc FROM ProposalDifferences " +
            "WHERE DifferenceId = $id;";
        command.Parameters.AddWithValue("$id", differenceId);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        var json = reader.GetString(0);
        if (!string.Equals(Sha(json), reader.GetString(1), StringComparison.Ordinal))
            throw new InvalidDataException("The saved Difference summary does not match its digest. Delete it and compare again.");
        return new SavedProposalDifference(Read<ProposalDifferenceSummary>(json, "summary"), reader.IsDBNull(2));
    }

    /// <summary>The saved Difference of a Proposal for one Text and reference, or null when it has none.</summary>
    public SavedProposalDifference? FindFor(string proposalId, string textId, string reference)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT DifferenceId FROM ProposalDifferences " +
            "WHERE ProposalId = $proposal AND TextId = $text AND Reference = $reference;";
        command.Parameters.AddWithValue("$proposal", proposalId);
        command.Parameters.AddWithValue("$text", textId);
        command.Parameters.AddWithValue("$reference", reference);
        return command.ExecuteScalar() is string id ? GetSaved(id) : null;
    }

    /// <summary>Whether the Proposal has left the undecided states, so its word detail is no longer kept.</summary>
    public bool ProposalIsDecided(string proposalId)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Status FROM Proposals WHERE ProposalId = $proposal;";
        command.Parameters.AddWithValue("$proposal", proposalId);
        return command.ExecuteScalar() is string status && ProposalLifecycle.IsTerminal(status);
    }

    /// <summary>
    /// The summary, whether word detail is kept, and one page of matching words ordered by word, read together so a
    /// decision made during the read cannot show a kept flag that disagrees with the words. Null when there is none.
    /// </summary>
    /// <exception cref="OperationCanceledException">The token was cancelled, which interrupts a query in flight.</exception>
    public ProposalDifferencePage? ReadPage(string differenceId, ProposalDifferenceFilter filter, int offset, int limit,
        ProposalDifferenceOccurrenceRequest? occurrences = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(limit);
        if (occurrences is not null)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(occurrences.Offset);
            ArgumentOutOfRangeException.ThrowIfNegative(occurrences.Limit);
        }
        cancellationToken.ThrowIfCancellationRequested();
        using var connection = _database.OpenConnection();
        // A cancelled read interrupts its running query; the registration is disposed before the connection.
        using var interrupt = cancellationToken.Register(
            static state => SQLitePCL.raw.sqlite3_interrupt(((SqliteConnection)state!).Handle), connection);
        try
        {
            return ReadPage(connection, differenceId, filter, offset, limit, occurrences);
        }
        catch (SqliteException exception) when (cancellationToken.IsCancellationRequested &&
            exception.SqliteErrorCode == SQLitePCL.raw.SQLITE_INTERRUPT)
        {
            throw new OperationCanceledException(cancellationToken);
        }
    }

    private static ProposalDifferencePage? ReadPage(SqliteConnection connection, string differenceId,
        ProposalDifferenceFilter filter, int offset, int limit, ProposalDifferenceOccurrenceRequest? occurrences)
    {
        using var transaction = connection.BeginTransaction(deferred: true);
        if (ReadSaved(connection, transaction, differenceId) is not { } saved) return null;
        var where = new StringBuilder("c.DifferenceId = $id");
        var parameters = new List<(string, object)> { ("$id", differenceId) };
        if (filter.CouldBeBad) where.Append(" AND c.CouldBeBad = 1");
        if (filter.Bucket is { } bucket)
        {
            where.Append(" AND EXISTS (SELECT 1 FROM ProposalDifferenceBuckets b WHERE b.DifferenceId = c.DifferenceId " +
                "AND b.CaseKey = c.CaseKey AND b.Bucket = $bucket)");
            parameters.Add(("$bucket", BucketName(bucket)));
        }
        if (filter.BeforeState is { } before) { where.Append(" AND c.BeforeState = $before"); parameters.Add(("$before", before)); }
        if (filter.AfterState is { } after) { where.Append(" AND c.AfterState = $after"); parameters.Add(("$after", after)); }
        if (filter.Word is { } exact) { where.Append(" AND c.CaseKey = $word"); parameters.Add(("$word", exact)); }
        if (filter.Limitation is { } limitation)
        {
            // A word counts once for a limitation that touched either checkpoint, as the summary's counts do.
            where.Append(" AND (EXISTS (SELECT 1 FROM json_each(c.FactsJson, '$.Before.Limitations') l WHERE l.value = $limitation) " +
                "OR EXISTS (SELECT 1 FROM json_each(c.FactsJson, '$.After.Limitations') l WHERE l.value = $limitation))");
            parameters.Add(("$limitation", LimitationName(limitation)));
        }

        int matching;
        using (var count = connection.CreateCommand())
        {
            count.Transaction = transaction;
            count.CommandText = "SELECT COUNT(*) FROM ProposalDifferenceCases c WHERE " + where;
            foreach (var (name, value) in parameters) count.Parameters.AddWithValue(name, value);
            matching = Convert.ToInt32(count.ExecuteScalar(), CultureInfo.InvariantCulture);
        }

        var words = new List<ProposalDifferenceWord>();
        using (var page = connection.CreateCommand())
        {
            page.Transaction = transaction;
            page.CommandText = "SELECT c.Word, c.OccurrenceCount, c.BeforeState, c.AfterState, c.FactsJson, c.FactsDigest " +
                "FROM ProposalDifferenceCases c WHERE " + where + " ORDER BY c.CaseKey LIMIT $limit OFFSET $offset;";
            foreach (var (name, value) in parameters) page.Parameters.AddWithValue(name, value);
            page.Parameters.AddWithValue("$limit", limit);
            page.Parameters.AddWithValue("$offset", offset);
            using var reader = page.ExecuteReader();
            while (reader.Read())
            {
                var factsJson = reader.GetString(4);
                if (!string.Equals(Sha(factsJson), reader.GetString(5), StringComparison.Ordinal))
                    throw new InvalidDataException("A saved Difference word does not match its digest. Delete the Difference and compare again.");
                var facts = Read<ProposalDifferenceCaseFacts>(factsJson, "word");
                words.Add(new ProposalDifferenceWord(reader.GetString(0), reader.GetInt32(1), reader.GetString(2),
                    reader.GetString(3), facts.Changes, facts.Buckets.Select(item => item.Bucket).Distinct().ToArray(), facts));
            }
        }
        return new ProposalDifferencePage(saved, matching, words,
            saved.WordDetailKept ? ReadPlaces(connection, transaction, differenceId, saved.Summary.TextId, occurrences) : null);
    }

    // The places are the large part of word detail, so they exist only while the Proposal is undecided.
    private static ProposalDifferenceOccurrencePage? ReadPlaces(SqliteConnection connection, SqliteTransaction transaction,
        string differenceId, string textId, ProposalDifferenceOccurrenceRequest? request)
    {
        if (request is null) return null;
        int total;
        using (var count = connection.CreateCommand())
        {
            count.Transaction = transaction;
            count.CommandText = "SELECT COUNT(*) FROM ProposalDifferenceOccurrences WHERE DifferenceId = $id AND TextId = $text AND CaseKey = $word;";
            count.Parameters.AddWithValue("$id", differenceId);
            count.Parameters.AddWithValue("$text", textId);
            count.Parameters.AddWithValue("$word", request.Word);
            total = Convert.ToInt32(count.ExecuteScalar(), CultureInfo.InvariantCulture);
        }
        var places = new List<ProposalDifferenceOccurrencePlace>();
        using (var page = connection.CreateCommand())
        {
            page.Transaction = transaction;
            page.CommandText = "SELECT TextId, SegmentId, OccurrenceIndex, LineNumber FROM ProposalDifferenceOccurrences " +
                "WHERE DifferenceId = $id AND TextId = $text AND CaseKey = $word ORDER BY TextId, LineNumber, OccurrenceIndex, SegmentId " +
                "LIMIT $limit OFFSET $offset;";
            page.Parameters.AddWithValue("$id", differenceId);
            page.Parameters.AddWithValue("$text", textId);
            page.Parameters.AddWithValue("$word", request.Word);
            page.Parameters.AddWithValue("$limit", request.Limit);
            page.Parameters.AddWithValue("$offset", request.Offset);
            using var reader = page.ExecuteReader();
            while (reader.Read())
                places.Add(new ProposalDifferenceOccurrencePlace(reader.GetString(0), reader.GetString(1),
                    reader.GetInt32(2), reader.GetInt32(3)));
        }
        var next = request.Offset + places.Count < total ? request.Offset + places.Count : (int?)null;
        return new ProposalDifferenceOccurrencePage(request.Word, places, total, next);
    }

    private static T Read<T>(string json, string what) where T : class
    {
        try { return JsonSerializer.Deserialize<T>(json, Json) ?? throw new JsonException("null"); }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"The saved Difference {what} is malformed. Delete the Difference and compare again.", exception);
        }
    }

    private static string Sha(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    private static void Execute(SqliteConnection connection, SqliteTransaction transaction, string sql,
        params (string Name, object Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        command.ExecuteNonQuery();
    }
}
