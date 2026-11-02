using Microsoft.Data.Sqlite;

namespace SIL.Motif.Worker.Store;

/// <summary>
/// Drops a Proposal Difference's word detail once its Proposal is decided or deferred, keeping the summary (ADR 0067).
/// </summary>
internal static class ProposalDifferenceRetention
{
    /// <summary>
    /// Whether a Proposal that enters this status still wants the word detail of its Differences. A deferred Proposal
    /// is set aside, so it does not; making the Difference again there is an explicit request and keeps its words
    /// until the Proposal next changes status.
    /// </summary>
    internal static bool KeepsWordDetail(string status) =>
        !ProposalLifecycle.IsTerminal(status) && ProposalLifecycle.Parse(status) is not ProposalStatus.Deferred;

    /// <summary>
    /// Deletes the word, Text-position and category rows of every Difference of the Proposal and stamps when it
    /// happened, on the caller's transaction so the status change and the drop commit together.
    /// </summary>
    internal static void DropWordDetail(SqliteConnection connection, SqliteTransaction transaction, string proposalId,
        string nowUtc)
    {
        foreach (var table in new[] { "ProposalDifferenceBuckets", "ProposalDifferenceOccurrences", "ProposalDifferenceCases" })
            Run(connection, transaction, $"DELETE FROM {table} WHERE DifferenceId IN " +
                "(SELECT DifferenceId FROM ProposalDifferences WHERE ProposalId = $proposal);", proposalId, nowUtc);
        Run(connection, transaction, "UPDATE ProposalDifferences SET WordDetailDroppedUtc = $now " +
            "WHERE ProposalId = $proposal AND WordDetailDroppedUtc IS NULL;", proposalId, nowUtc);
    }

    /// <summary>
    /// Drops the word detail still held for any Proposal that is applied, rejected, withdrawn or superseded, which
    /// only a store written before the drop was made on decision, or a failed drop, can leave behind.
    /// </summary>
    internal static void DropForClosedProposals(SqliteConnection connection, SqliteTransaction transaction)
    {
        var closed = new List<string>();
        using (var find = connection.CreateCommand())
        {
            find.Transaction = transaction;
            find.CommandText = """
                SELECT DISTINCT d.ProposalId FROM ProposalDifferences d JOIN Proposals p ON p.ProposalId = d.ProposalId
                WHERE d.WordDetailDroppedUtc IS NULL AND p.Status IN ('applied', 'rejected', 'withdrawn', 'superseded');
                """;
            using var reader = find.ExecuteReader();
            while (reader.Read()) closed.Add(reader.GetString(0));
        }
        var now = DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        foreach (var proposalId in closed) DropWordDetail(connection, transaction, proposalId, now);
    }

    private static void Run(SqliteConnection connection, SqliteTransaction transaction, string sql, string proposalId,
        string nowUtc)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.Parameters.AddWithValue("$proposal", proposalId);
        command.Parameters.AddWithValue("$now", nowUtc);
        command.ExecuteNonQuery();
    }
}
