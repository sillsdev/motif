using Microsoft.Data.Sqlite;

namespace SIL.Motif.Worker.Store;

/// <summary>
/// The one rule for which of a Proposal's Assessments may be removed: only those nothing else still needs.
/// </summary>
/// <remarks>
/// An Assessment is kept when a Report cites it, a pin or a retained invocation holds it, it is the project's
/// current Assessment, or a surviving Assessment names it as the measurement it replaces, transitively. Removal
/// never rewrites a surviving row: a Report keeps its frozen binding and a replacement keeps its lineage (ADR 0042),
/// and an Assessment is never detached from its Proposal, because one without a Proposal is Baseline evidence.
/// </remarks>
internal static class AssessmentRetention
{
    /// <summary>
    /// Removes the removable Assessments recorded against <paramref name="proposalId"/>, apart from
    /// <paramref name="exceptAssessmentId"/>, inside the caller's transaction, and returns how many it removed.
    /// </summary>
    public static int SweepProposal(SqliteConnection connection, SqliteTransaction transaction, string proposalId,
        string? exceptAssessmentId)
    {
        var removable = ReadIds(connection, transaction,
            "SELECT AssessmentId FROM Assessments WHERE ProposalId = $proposalId " +
            "AND ($exceptId IS NULL OR AssessmentId != $exceptId);",
            ("$proposalId", proposalId), ("$exceptId", exceptAssessmentId));
        if (removable.Count == 0) return 0;

        removable.ExceptWith(ReadIds(connection, transaction, """
            SELECT AssessmentId FROM Reports WHERE AssessmentId IS NOT NULL
            UNION SELECT AssessmentId FROM AssessmentPins
            UNION SELECT AssessmentId FROM RetainedInvocationMembers
            UNION SELECT CurrentAssessmentId FROM MotifMetadata WHERE CurrentAssessmentId IS NOT NULL;
            """));
        var replacements = ReadPairs(connection, transaction,
            "SELECT AssessmentId, ReplacesAssessmentId FROM Assessments WHERE ReplacesAssessmentId IS NOT NULL;");
        for (var kept = true; kept;)
        {
            kept = false;
            foreach (var (replacement, root) in replacements)
                if (!removable.Contains(replacement) && removable.Remove(root)) kept = true;
        }
        if (removable.Count == 0) return 0;

        Execute(connection, transaction, "CREATE TEMP TABLE IF NOT EXISTS RemovedAssessments (AssessmentId TEXT PRIMARY KEY);");
        Execute(connection, transaction, "DELETE FROM temp.RemovedAssessments;");
        foreach (var id in removable)
            Execute(connection, transaction, "INSERT INTO temp.RemovedAssessments (AssessmentId) VALUES ($id);", ("$id", id));
        foreach (var sql in new[]
        {
            "DELETE FROM AssessmentObjectTimings WHERE AssessmentId IN (SELECT AssessmentId FROM temp.RemovedAssessments);",
            """
            DELETE FROM ParsedAnalyses WHERE AssessedWordId IN (SELECT AssessedWordId FROM AssessedWords
                WHERE AssessmentId IN (SELECT AssessmentId FROM temp.RemovedAssessments));
            """,
            "DELETE FROM AssessedWords WHERE AssessmentId IN (SELECT AssessmentId FROM temp.RemovedAssessments);",
            "DELETE FROM Assessments WHERE AssessmentId IN (SELECT AssessmentId FROM temp.RemovedAssessments);",
            "DELETE FROM temp.RemovedAssessments;",
        })
            Execute(connection, transaction, sql);
        return removable.Count;
    }

    private static HashSet<string> ReadIds(SqliteConnection connection, SqliteTransaction transaction, string sql,
        params (string Name, string? Value)[] parameters)
    {
        using var command = Command(connection, transaction, sql, parameters);
        using var reader = command.ExecuteReader();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        while (reader.Read()) ids.Add(reader.GetString(0));
        return ids;
    }

    private static List<(string Replacement, string Root)> ReadPairs(SqliteConnection connection,
        SqliteTransaction transaction, string sql)
    {
        using var command = Command(connection, transaction, sql);
        using var reader = command.ExecuteReader();
        var pairs = new List<(string, string)>();
        while (reader.Read()) pairs.Add((reader.GetString(0), reader.GetString(1)));
        return pairs;
    }

    private static void Execute(SqliteConnection connection, SqliteTransaction transaction, string sql,
        params (string Name, string? Value)[] parameters)
    {
        using var command = Command(connection, transaction, sql, parameters);
        command.ExecuteNonQuery();
    }

    private static SqliteCommand Command(SqliteConnection connection, SqliteTransaction transaction, string sql,
        params (string Name, string? Value)[] parameters)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, (object?)value ?? DBNull.Value);
        return command;
    }
}
