using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SIL.Motif.Contract.Review;
using SIL.Motif.Host.Store;

namespace SIL.Motif.Worker.Store;

/// <summary>The frozen manifest, summary and estimated index cost for one report publication.</summary>
public sealed record ReviewReportPublication(string ProjectKey, string ProposalId, string TrialJobId,
    string ManifestDigest, ReportView Summary, IReadOnlyList<string> InputReferences, long EstimatedDetailBytes);

/// <summary>The stored summary and retained input references for a report.</summary>
public sealed record StoredReviewReport(ReportView Summary, bool DetailAvailable, IReadOnlyList<string> InputReferences);

/// <summary>Publishes immutable review summaries and complete detail indexes in one transaction.</summary>
public sealed class ReviewReportRepository(MotifDatabase database)
{
    private const long Budget = 100_000_000;
    private readonly MotifDatabase _database = database ?? throw new ArgumentNullException(nameof(database));

    /// <summary>Stores a complete streamed index or an explicitly unavailable detail summary.</summary>
    public async Task<ReviewReportId> PublishAsync(ReviewReportPublication publication,
        IAsyncEnumerable<ReviewCaseDelta> cases, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(publication);
        ArgumentNullException.ThrowIfNull(cases);
        ArgumentOutOfRangeException.ThrowIfNegative(publication.EstimatedDetailBytes);
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        using (var existing = Command(connection, transaction,
            "SELECT ReportId FROM ReviewReports WHERE ProjectKey=$project AND ManifestDigest=$digest;",
            ("$project", publication.ProjectKey), ("$digest", publication.ManifestDigest)))
        {
            if (existing.ExecuteScalar() is string id) return new ReviewReportId(id);
        }
        string status;
        using (var proposal = Command(connection, transaction, "SELECT Status FROM Proposals WHERE ProposalId=$id;",
            ("$id", publication.ProposalId)))
            status = proposal.ExecuteScalar() as string ?? throw new InvalidOperationException("Proposal does not exist.");
        var released = !ProposalDifferenceRetention.KeepsWordDetail(status);
        var reportId = new ReviewReportId("review-report/" + Guid.NewGuid().ToString("N"));
        var available = !released && publication.EstimatedDetailBytes <= Budget - Math.Min(Budget, AssessmentRetention.BytesInUse(connection, transaction));
        var summary = publication.Summary with { ReportId = reportId.Value, ReportDigest = publication.ManifestDigest };
        var summaryJson = JsonSerializer.Serialize(summary, ReviewJson.Options);
        Execute(connection, transaction, "INSERT INTO ReviewReports (ReportId,ProjectKey,ProposalId,TrialJobId,ManifestDigest,SummaryJson,SummaryDigest,InputReferencesJson,DetailAvailable,DetailBytes,CreatedUtc) VALUES ($id,$project,$proposal,$job,$manifest,$summary,$hash,$refs,$available,0,$created);",
            ("$id", reportId.Value), ("$project", publication.ProjectKey), ("$proposal", publication.ProposalId),
            ("$job", publication.TrialJobId), ("$manifest", publication.ManifestDigest), ("$summary", summaryJson),
            ("$hash", Hash(summaryJson)), ("$refs", JsonSerializer.Serialize(released ? [] : publication.InputReferences)),
            ("$available", available ? 1 : 0), ("$created", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)));
        long detailBytes = 0;
        if (available)
        {
            await foreach (var item in cases.WithCancellation(cancellationToken))
            {
                var facts = JsonSerializer.Serialize(new { item.NewNoParse, item.NewApprovedLoss, item.NewlyAcceptedNegative,
                    item.Flags, item.Limitations, item.Before, item.After }, ReviewJson.Options);
                var memberships = item.Memberships.Select(m => JsonSerializer.Serialize(m, ReviewJson.Options)).ToArray();
                var events = item.ReadingLosses.Select(e => JsonSerializer.Serialize(e, ReviewJson.Options)).ToArray();
                detailBytes = checked(detailBytes + Cost(item.CaseKey, item.WordKey, item.Word, item.WritingSystemId, facts)
                    + memberships.Sum(m => Cost(m)) + events.Sum(e => Cost(e)));
                if (detailBytes > publication.EstimatedDetailBytes ||
                    detailBytes > Budget - Math.Min(Budget, AssessmentRetention.BytesInUse(connection, transaction)))
                {
                    available = false;
                    break;
                }
                Execute(connection, transaction, "INSERT INTO ReviewReportCases VALUES ($id,$case,$wordkey,$word,$ws,$facts);",
                    ("$id", reportId.Value), ("$case", item.CaseKey), ("$wordkey", item.WordKey),
                    ("$word", item.Word), ("$ws", item.WritingSystemId), ("$facts", facts));
                for (var i = 0; i < memberships.Length; i++)
                    Execute(connection, transaction, "INSERT INTO ReviewReportMemberships VALUES ($id,$case,$ordinal,$set,$role,$facts);",
                        ("$id", reportId.Value), ("$case", item.CaseKey), ("$ordinal", i),
                        ("$set", item.Memberships[i].SetId), ("$role", item.Memberships[i].Role), ("$facts", memberships[i]));
                for (var i = 0; i < events.Length; i++)
                    Execute(connection, transaction, "INSERT INTO ReviewReportEvents VALUES ($id,$case,$ordinal,$facts);",
                        ("$id", reportId.Value), ("$case", item.CaseKey), ("$ordinal", i), ("$facts", events[i]));
            }
        }
        if (!available)
        {
            ReviewReportRetention.DeleteRows(connection, transaction, reportId.Value);
            summary = summary with { DetailAvailability = DetailAvailability.Unavailable, Page = null, Word = null, Trace = null,
                Limitations = summary.Limitations.Concat([new Limitation(released ? "detail-released" : "store-budget", [],
                    released ? "Report detail was released after the Proposal decision." : "Complete report detail exceeds the store budget.", [])]).ToArray() };
            summaryJson = JsonSerializer.Serialize(summary, ReviewJson.Options);
            Execute(connection, transaction, "UPDATE ReviewReports SET SummaryJson=$summary,SummaryDigest=$hash,DetailAvailable=0,DetailBytes=0 WHERE ReportId=$id;",
                ("$summary", summaryJson), ("$hash", Hash(summaryJson)), ("$id", reportId.Value));
        }
        else Execute(connection, transaction, "UPDATE ReviewReports SET DetailBytes=$bytes WHERE ReportId=$id;",
            ("$bytes", detailBytes), ("$id", reportId.Value));
        cancellationToken.ThrowIfCancellationRequested();
        transaction.Commit();
        return reportId;
    }

    /// <summary>Reads a frozen summary, rejecting damaged stored summary bytes.</summary>
    public StoredReviewReport? ReadManifest(ReviewReportId id)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT SummaryJson,SummaryDigest,DetailAvailable,InputReferencesJson FROM ReviewReports WHERE ReportId=$id;";
        command.Parameters.AddWithValue("$id", id.Value);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        var json = reader.GetString(0);
        if (Hash(json) != reader.GetString(1)) throw new InvalidDataException("Review summary digest does not match.");
        return new StoredReviewReport(JsonSerializer.Deserialize<ReportView>(json, ReviewJson.Options)!, reader.GetInt64(2) != 0,
            JsonSerializer.Deserialize<string[]>(reader.GetString(3))!);
    }

    private static long Cost(params string?[] values) => 512 + values.Sum(value => (long)Encoding.UTF8.GetByteCount(value ?? "") * 4);
    internal static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    internal static void Execute(SqliteConnection connection, SqliteTransaction transaction, string sql, params (string, object?)[] args)
    {
        using var command = Command(connection, transaction, sql, args);
        command.ExecuteNonQuery();
    }
    private static SqliteCommand Command(SqliteConnection connection, SqliteTransaction transaction, string sql, params (string, object?)[] args)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in args) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return command;
    }
}
