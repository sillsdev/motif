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
public sealed record ProposalDifferenceFilter(
    bool CouldBeBad = false, ProposalDifferenceBucket? Bucket = null, string? BeforeState = null, string? AfterState = null);

/// <summary>Stores a Proposal Difference in one transaction and reads it back by summary or page.</summary>
public sealed class ProposalDifferenceRepository
{
    private readonly MotifDatabase _database;

    public ProposalDifferenceRepository(MotifDatabase database) =>
        _database = database ?? throw new ArgumentNullException(nameof(database));

    /// <summary>Stores the summary, words, Text positions and categories together, or none of them.</summary>
    public void Publish(NewProposalDifference difference)
    {
        ArgumentNullException.ThrowIfNull(difference);
        var summary = difference.Summary;
        var summaryJson = JsonSerializer.Serialize(summary, MotifJson.CreateOptions());
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        Execute(connection, transaction,
            "INSERT INTO ProposalDifferences (DifferenceId, ProposalId, TrialJobId, BeforeAssessmentId, " +
            "AfterAssessmentId, TextId, BaselineToken, SummaryJson, SummaryDigest, CreatedUtc) VALUES " +
            "($id, $proposal, $job, $before, $after, $text, $baseline, $json, $digest, $created);",
            ("$id", summary.DifferenceId), ("$proposal", summary.ProposalId), ("$job", summary.TrialJobId),
            ("$before", summary.BeforeAssessmentId), ("$after", summary.AfterAssessmentId), ("$text", summary.TextId),
            ("$baseline", summary.BaselineToken), ("$json", summaryJson), ("$digest", Sha(summaryJson)),
            ("$created", summary.CreatedUtc));
        foreach (var word in difference.Words)
        {
            var factsJson = JsonSerializer.Serialize(word.Facts, MotifJson.CreateOptions());
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

    private static string WireName<T>(T value, System.Text.Json.Serialization.JsonConverter<T> converter)
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(converter);
        return JsonSerializer.Serialize(value, options).Trim('"');
    }

    /// <summary>The newest Difference id saved for a Proposal, or null when it has none.</summary>
    public string? LatestFor(string proposalId)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT DifferenceId FROM ProposalDifferences WHERE ProposalId = $proposal " +
            "ORDER BY CreatedUtc DESC, DifferenceId DESC LIMIT 1;";
        command.Parameters.AddWithValue("$proposal", proposalId);
        return command.ExecuteScalar() as string;
    }

    /// <summary>The stored summary, refused when its bytes no longer match the digest saved with it.</summary>
    public ProposalDifferenceSummary? GetSummary(string differenceId)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT SummaryJson, SummaryDigest FROM ProposalDifferences WHERE DifferenceId = $id;";
        command.Parameters.AddWithValue("$id", differenceId);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        var json = reader.GetString(0);
        if (!string.Equals(Sha(json), reader.GetString(1), StringComparison.Ordinal))
            throw new InvalidDataException("The saved Difference summary does not match its digest. Delete it and compare again.");
        return Read<ProposalDifferenceSummary>(json, "summary");
    }

    /// <summary>One page of matching words ordered by word, and how many words match before paging.</summary>
    public (int Matching, IReadOnlyList<ProposalDifferenceWord> Words) ReadWords(
        string differenceId, ProposalDifferenceFilter filter, int offset, int limit)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(limit);
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction(deferred: true);
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
                "FROM ProposalDifferenceCases c WHERE " + where + " ORDER BY c.Word LIMIT $limit OFFSET $offset;";
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
        return (matching, words);
    }

    private static T Read<T>(string json, string what) where T : class
    {
        try { return JsonSerializer.Deserialize<T>(json, MotifJson.CreateOptions()) ?? throw new JsonException("null"); }
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
