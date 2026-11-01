using Microsoft.Data.Sqlite;

namespace SIL.Motif.Worker.Store;

/// <summary>Drops a Proposal Difference's word detail once its Proposal is decided, keeping the summary (ADR 0067).</summary>
internal static class ProposalDifferenceRetention
{
    /// <summary>Whether a Proposal that enters this status still wants the word detail of its Differences.</summary>
    internal static bool KeepsWordDetail(string status) => !ProposalLifecycle.IsTerminal(status);

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
