using Microsoft.Data.Sqlite;
using SIL.Motif.Contract.Ids;

namespace SIL.Motif.Worker.Store;

/// <summary>Removes one terminal Proposal and only the evidence no other store record retains.</summary>
internal static class ProposalDeletion
{
    /// <summary>Whether a Proposal status is terminal.</summary>
    /// <param name="status">The stored workflow status.</param>
    /// <returns><see langword="true"/> when the Proposal has been closed.</returns>
    internal static bool IsTerminal(string status) =>
        status is "applied" or "rejected" or "superseded" or "withdrawn";

    /// <summary>Deletes a terminal Proposal and its removable evidence inside the caller's transaction.</summary>
    /// <param name="connection">The open connection that owns <paramref name="transaction"/>.</param>
    /// <param name="transaction">The transaction that includes this Proposal deletion.</param>
    /// <param name="proposalId">The id of the Proposal to delete.</param>
    internal static void Delete(SqliteConnection connection, SqliteTransaction transaction, CanonicalId proposalId)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);

        using (var check = connection.CreateCommand())
        {
            check.Transaction = transaction;
            check.CommandText = "SELECT Status FROM Proposals WHERE ProposalId = $id;";
            check.Parameters.AddWithValue("$id", proposalId.Value);
            var status = check.ExecuteScalar() as string;
            if (status is null || !IsTerminal(status))
                throw new InvalidOperationException("Only terminal Proposals may be archived.");
            check.CommandText = "SELECT DraftName FROM Proposals WHERE ProposalId = $id;";
            if (check.ExecuteScalar() is string)
                throw new InvalidOperationException("A Proposal with an open Draft is kept until the Draft is finalized or discarded.");
            if (status == "applied")
                throw new InvalidOperationException("An applied Proposal is final: it and its Receipts are kept.");
            check.CommandText = "SELECT COUNT(*) FROM Receipts WHERE ProposalId = $id;";
            if ((long)check.ExecuteScalar()! > 0)
                throw new InvalidOperationException("A Proposal with a Receipt is final: it and its Receipts are kept.");
        }

        RefuseIfRetainedByExampleSet(connection, transaction, proposalId.Value);
        using (var reports = connection.CreateCommand())
        {
            reports.Transaction = transaction;
            reports.CommandText = "DELETE FROM Reports WHERE ProposalId = $id;";
            reports.Parameters.AddWithValue("$id", proposalId.Value);
            reports.ExecuteNonQuery();
        }
        AssessmentRetention.SweepProposal(connection, transaction, proposalId.Value, exceptAssessmentId: null);
        using (var check = connection.CreateCommand())
        {
            // Detaching would turn the Assessment into Baseline evidence, which is what a Proposal-less one means.
            check.Transaction = transaction;
            check.CommandText = "SELECT COUNT(*) FROM Assessments WHERE ProposalId = $id;";
            check.Parameters.AddWithValue("$id", proposalId.Value);
            if ((long)check.ExecuteScalar()! > 0)
                throw new InvalidOperationException("This Proposal has Assessment results that are still pinned, " +
                    "retained, current, cited by another Proposal's Report, or replaced by a kept result, so it is kept.");
        }
        RefuseIfRetainedByExampleSet(connection, transaction, proposalId.Value);
        foreach (var sql in new[] { "DELETE FROM ProposalDifferences WHERE ProposalId = $id;",
            "DELETE FROM GrammarChecks WHERE ProposalId = $id;",
            "DELETE FROM ProposalRevisions WHERE ProposalId = $id;" })
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            command.Parameters.AddWithValue("$id", proposalId.Value);
            command.ExecuteNonQuery();
        }
        using var proposal = connection.CreateCommand();
        proposal.Transaction = transaction;
        proposal.CommandText = "DELETE FROM Proposals WHERE ProposalId = $id;";
        proposal.Parameters.AddWithValue("$id", proposalId.Value);
        proposal.ExecuteNonQuery();
    }

    private static void RefuseIfRetainedByExampleSet(SqliteConnection connection, SqliteTransaction transaction,
        string proposalId)
    {
        var count = AssessmentRetention.CountPhonologyExampleSetReferences(connection, transaction, proposalId);
        if (count > 0) throw new ProposalRetainedByExampleSetException(count);
    }
}
