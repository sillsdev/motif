using Microsoft.Data.Sqlite;
using SIL.Motif.Contract.Ids;

namespace SIL.Motif.Worker.Store;

/// <summary>Removes one terminal Proposal and only the evidence no other store record retains.</summary>
internal static class ProposalDeletion
{
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
            if (status is null || !ProposalLifecycle.IsTerminal(status))
                throw new InvalidOperationException("Only terminal Proposals may be archived.");
            check.CommandText = "SELECT DraftName FROM Proposals WHERE ProposalId = $id;";
            if (check.ExecuteScalar() is string)
                throw new InvalidOperationException("A Proposal with an open Draft is kept until the Draft is finalized or discarded.");
            if (status == ProposalLifecycle.ToWire(ProposalStatus.Applied))
                throw new InvalidOperationException("An applied Proposal is final: it and its Receipts are kept.");
            check.CommandText = "SELECT COUNT(*) FROM Receipts WHERE ProposalId = $id;";
            if ((long)check.ExecuteScalar()! > 0)
                throw new InvalidOperationException("A Proposal with a Receipt is final: it and its Receipts are kept.");
        }

        RemoveEvidence(connection, transaction, proposalId.Value);
        using var proposal = connection.CreateCommand();
        proposal.Transaction = transaction;
        proposal.CommandText = "DELETE FROM Proposals WHERE ProposalId = $id;";
        proposal.Parameters.AddWithValue("$id", proposalId.Value);
        proposal.ExecuteNonQuery();
    }

    /// <summary>
    /// Removes the Reports, Differences, removable Assessments, grammar checks, revisions, Comments and pictures of a Proposal, or a Draft that
    /// was never finalized, on the caller's transaction, and throws when a result is still kept for another reason.
    /// </summary>
    /// <exception cref="ProposalRetainedByExampleSetException">A phonology Example Set cites the Proposal.</exception>
    /// <exception cref="ProposalEvidenceRetainedException">An Assessment of the Proposal is pinned or retained.</exception>
    internal static void RemoveEvidence(SqliteConnection connection, SqliteTransaction transaction, string proposalId)
    {
        RefuseIfRetainedByExampleSet(connection, transaction, proposalId);
        // Differences first: they cite Assessments by value only, and their rows hold the Proposal.
        foreach (var sql in new[] { "DELETE FROM Reports WHERE ProposalId = $id;",
            "DELETE FROM ProposalDifferences WHERE ProposalId = $id;",
            "DELETE FROM ReviewReports WHERE ProposalId = $id;" })
            Execute(connection, transaction, sql, proposalId);
        AssessmentRetention.SweepProposal(connection, transaction, proposalId, exceptAssessmentId: null);
        using (var check = connection.CreateCommand())
        {
            // Detaching would turn the Assessment into Baseline evidence, which is what a Proposal-less one means.
            check.Transaction = transaction;
            check.CommandText = "SELECT COUNT(*) FROM Assessments WHERE ProposalId = $id;";
            check.Parameters.AddWithValue("$id", proposalId);
            if ((long)check.ExecuteScalar()! > 0)
                throw new ProposalEvidenceRetainedException("This Proposal has Assessment results that are still " +
                    "pinned, retained, current, cited by another Proposal's Report, or replaced by a kept result, so it is kept.");
        }
        RefuseIfRetainedByExampleSet(connection, transaction, proposalId);
        ProposalDiscussionRepository.RemoveAll(connection, transaction, proposalId);
        foreach (var sql in new[] { "DELETE FROM GrammarChecks WHERE ProposalId = $id;",
            "DELETE FROM ProposalRevisions WHERE ProposalId = $id;" })
            Execute(connection, transaction, sql, proposalId);
    }

    private static void Execute(SqliteConnection connection, SqliteTransaction transaction, string sql, string proposalId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.Parameters.AddWithValue("$id", proposalId);
        command.ExecuteNonQuery();
    }

    private static void RefuseIfRetainedByExampleSet(SqliteConnection connection, SqliteTransaction transaction,
        string proposalId)
    {
        var count = AssessmentRetention.CountPhonologyExampleSetReferences(connection, transaction, proposalId);
        if (count > 0) throw new ProposalRetainedByExampleSetException(count);
    }
}

/// <summary>An Assessment of the Proposal is pinned, retained or cited, so the Proposal's evidence is kept.</summary>
internal sealed class ProposalEvidenceRetainedException(string message) : InvalidOperationException(message);
