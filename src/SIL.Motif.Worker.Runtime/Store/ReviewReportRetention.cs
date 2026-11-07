using System.Text.Json;
using Microsoft.Data.Sqlite;
using SIL.Motif.Contract.Review;
using SIL.Motif.Host.Store;

namespace SIL.Motif.Worker.Store;

internal static class ReviewReportRetention
{
    internal static void DropDetail(SqliteConnection connection, SqliteTransaction transaction, string proposalId, string droppedUtc)
    {
        var reports = new List<(string Id, string Json, string Digest)>();
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "SELECT ReportId,SummaryJson,SummaryDigest FROM ReviewReports WHERE ProposalId=$proposal AND (DetailAvailable=1 OR InputReferencesJson != '[]');";
            command.Parameters.AddWithValue("$proposal", proposalId);
            using var reader = command.ExecuteReader();
            while (reader.Read()) reports.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        }
        foreach (var (id, json, digest) in reports)
        {
            if (ReviewReportRepository.Hash(json) != digest) throw new InvalidDataException("Review summary digest does not match.");
            var summary = JsonSerializer.Deserialize<ReportView>(json, ReviewJson.Options) ?? throw new InvalidDataException("Invalid review summary.");
            summary = summary with { DetailAvailability = DetailAvailability.Unavailable, Page = null, Word = null, Trace = null,
                Limitations = summary.Limitations.Concat([new Limitation("detail-released", [], "Report detail was released after the Proposal decision.", [])]).ToArray() };
            var updated = JsonSerializer.Serialize(summary, ReviewJson.Options);
            DeleteRows(connection, transaction, id);
            ReviewReportRepository.Execute(connection, transaction, "UPDATE ReviewReports SET SummaryJson=$json,SummaryDigest=$hash,InputReferencesJson='[]',DetailAvailable=0,DetailBytes=0 WHERE ReportId=$id;",
                ("$json", updated), ("$hash", ReviewReportRepository.Hash(updated)), ("$id", id));
        }
    }

    internal static void DeleteRows(SqliteConnection connection, SqliteTransaction transaction, string id)
    {
        foreach (var table in new[] { "ReviewReportCases", "ReviewReportMemberships", "ReviewReportEvents" })
            ReviewReportRepository.Execute(connection, transaction, $"DELETE FROM {table} WHERE ReportId=$id;", ("$id", id));
    }

    internal static HashSet<string> ProtectedTrialJobs(MotifDatabase database)
    {
        var jobs = new HashSet<string>(StringComparer.Ordinal);
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT TrialJobId FROM ReviewReports WHERE DetailAvailable=1 OR InputReferencesJson != '[]';";
        using var reader = command.ExecuteReader();
        while (reader.Read()) jobs.Add(reader.GetString(0));
        return jobs;
    }
}
