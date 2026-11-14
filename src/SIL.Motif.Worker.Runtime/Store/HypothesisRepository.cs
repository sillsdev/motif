using SIL.Motif.Projection.Store;
using System.Globalization;
using Microsoft.Data.Sqlite;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Host.Store;

namespace SIL.Motif.Worker.Store;

/// <summary>The states a hypothesis is in.</summary>
public static class HypothesisStatuses
{
    /// <summary>Stated, and no verdict has been decided on it.</summary>
    public const string Proposed = "proposed";

    /// <summary>Its test passed and the rule stands.</summary>
    public const string Passed = "passed";

    /// <summary>Its test failed, or an attested loss stands; a list for this shape is now allowed.</summary>
    public const string Failed = "failed";

    /// <summary>A later hypothesis replaces it.</summary>
    public const string Superseded = "superseded";

    /// <summary>Every status a hypothesis can have.</summary>
    public static IReadOnlyList<string> All { get; } = [Proposed, Passed, Failed, Superseded];
}

/// <summary>The verdicts <c>rule-hypothesis-test</c> returns.</summary>
public static class HypothesisVerdicts
{
    /// <summary>Rule hits clear every control by the margin and the Trial lost nothing attested.</summary>
    public const string Pass = "PASS";

    /// <summary>A control matches or beats the rule, or an attested loss stands.</summary>
    public const string Fail = "FAIL";

    /// <summary>Below the minimum N or inside the margin.</summary>
    public const string Undecided = "UNDECIDED";

    /// <summary>Every verdict.</summary>
    public static IReadOnlyList<string> All { get; } = [Pass, Fail, Undecided];
}

/// <summary>One question for a speaker, with the answer its author privately predicts.</summary>
/// <param name="Question">A word judgement or minimal pair.</param>
/// <param name="Predicted">The answer the hypothesis predicts; never shown to a speaker.</param>
/// <param name="Answer">The speaker's answer, or null while unanswered.</param>
public sealed record HypothesisQuestion(string Question, string? Predicted, string? Answer);

/// <summary>One hypothesis row.</summary>
public sealed record HypothesisRecord(
    string HypothesisId, int Seq, string? Pattern, string Statement, string Mechanism, string ChangeText,
    string Environment, string HostClass, string Slot, string? Neighbour, IReadOnlyList<string> Predictions,
    string? Falsifier, string? Exceptions, int? ObjectsAdded, int? AttestedTypesGained,
    IReadOnlyList<HypothesisQuestion> Questions, string Status, string? Decision, string? SupersededBy,
    string? GdrId, string? GdrSection, string? GdrNone, string? SealedVerdict, string? VerdictSource,
    int? WordQuestionsAsked, int? ObjectsBefore, int? TypesBefore, int? ObjectsAfter, int? TypesAfter,
    double? AgentHours, double? AlternativeHours, string CreatedUtc, string UpdatedUtc);

/// <summary>One recorded test of a hypothesis.</summary>
public sealed record HypothesisTestRecord(
    string HypothesisId, int TestNumber, string Verdict, int PredictedForms, int RuleHits,
    IReadOnlyDictionary<string, int> Controls, int Margin, int? TrialAttestedLosses, int? TrialNewReadings,
    int? TrialWorkSteps, string? Command, string RecordedUtc);

/// <summary>
/// The hypothesis ledger of one store: rule, prediction, test and decision. It is authored data, so a store upgrade keeps
/// it. This class only reads and writes rows; the rules about what may be recorded when live in <c>HypothesisCommands</c>.
/// </summary>
public sealed class HypothesisRepository
{
    private readonly MotifDatabase _database;
    private readonly IJobClock _clock;

    /// <summary>Opens the ledger of one store.</summary>
    public HypothesisRepository(MotifDatabase database, IJobClock? clock = null)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _clock = clock ?? new SystemJobClock();
    }

    private const string Columns =
        "HypothesisId, Seq, Pattern, Statement, Mechanism, ChangeText, Environment, HostClass, Slot, Neighbour, " +
        "PredictionsJson, Falsifier, Exceptions, ObjectsAdded, AttestedTypesGained, QuestionsJson, Status, Decision, " +
        "SupersededBy, GdrId, GdrSection, GdrNone, SealedVerdict, VerdictSource, WordQuestionsAsked, ObjectsBefore, " +
        "TypesBefore, ObjectsAfter, TypesAfter, AgentHours, AlternativeHours, CreatedUtc, UpdatedUtc";

    private static readonly System.Text.Json.JsonSerializerOptions Json = new(System.Text.Json.JsonSerializerDefaults.Web);

    /// <summary>The fields a new hypothesis is born with.</summary>
    public sealed record NewHypothesis(
        string? Pattern, string Statement, string Mechanism, string ChangeText, string Environment, string HostClass,
        string Slot, string? Neighbour, IReadOnlyList<string> Predictions, string? Falsifier, string? Exceptions,
        int? ObjectsAdded, int? AttestedTypesGained, IReadOnlyList<HypothesisQuestion> Questions);

    /// <summary>Stores a new proposed hypothesis under the next permanent <c>H-n</c> id.</summary>
    public HypothesisRecord Add(NewHypothesis hypothesis)
    {
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var next = connection.CreateCommand();
        next.Transaction = transaction;
        next.CommandText = "SELECT COALESCE(MAX(Seq), 0) + 1 FROM Hypotheses;";
        var seq = Convert.ToInt32(next.ExecuteScalar(), CultureInfo.InvariantCulture);
        var id = "H-" + seq.ToString(CultureInfo.InvariantCulture);
        using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO Hypotheses (HypothesisId, Seq, Pattern, Statement, Mechanism, ChangeText, Environment, HostClass,
                Slot, Neighbour, PredictionsJson, Falsifier, Exceptions, ObjectsAdded, AttestedTypesGained, QuestionsJson,
                Status, CreatedUtc, UpdatedUtc)
            VALUES ($id, $seq, $pattern, $statement, $mechanism, $change, $env, $host, $slot, $neighbour, $predictions,
                $falsifier, $exceptions, $added, $gained, $questions, 'proposed', $now, $now);
            """;
        var p = insert.Parameters;
        p.AddWithValue("$id", id);
        p.AddWithValue("$seq", seq);
        p.AddWithValue("$pattern", Db(hypothesis.Pattern));
        p.AddWithValue("$statement", hypothesis.Statement);
        p.AddWithValue("$mechanism", hypothesis.Mechanism);
        p.AddWithValue("$change", hypothesis.ChangeText);
        p.AddWithValue("$env", hypothesis.Environment);
        p.AddWithValue("$host", hypothesis.HostClass);
        p.AddWithValue("$slot", hypothesis.Slot);
        p.AddWithValue("$neighbour", Db(hypothesis.Neighbour));
        p.AddWithValue("$predictions", System.Text.Json.JsonSerializer.Serialize(hypothesis.Predictions, Json));
        p.AddWithValue("$falsifier", Db(hypothesis.Falsifier));
        p.AddWithValue("$exceptions", Db(hypothesis.Exceptions));
        p.AddWithValue("$added", Db(hypothesis.ObjectsAdded));
        p.AddWithValue("$gained", Db(hypothesis.AttestedTypesGained));
        p.AddWithValue("$questions", System.Text.Json.JsonSerializer.Serialize(hypothesis.Questions, Json));
        p.AddWithValue("$now", Now());
        insert.ExecuteNonQuery();
        transaction.Commit();
        return Get(id)!;
    }

    /// <summary>The hypothesis with this id (any case), or null. O(log n).</summary>
    public HypothesisRecord? Get(string hypothesisId)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        // Ids are stored as H-n, so an upper-cased lookup seeks the primary key (O(log n)); NOCASE would scan.
        command.CommandText = $"SELECT {Columns} FROM Hypotheses WHERE HypothesisId = $id;";
        command.Parameters.AddWithValue("$id", hypothesisId.Trim().ToUpperInvariant());
        using var reader = command.ExecuteReader();
        return reader.Read() ? Read(reader) : null;
    }

    /// <summary>
    /// The GDR link of every passed hypothesis that has one, for the GDR sync check (ADR 0119): one scan of the passed rows,
    /// O(h) for h hypotheses and never the GDR text, which stays in the project.
    /// </summary>
    public IReadOnlyList<(string HypothesisId, string GdrId, string? GdrSection)> PassedGdrLinks()
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT HypothesisId, GdrId, GdrSection FROM Hypotheses " +
            "WHERE Status = 'passed' AND GdrId IS NOT NULL ORDER BY Seq;";
        using var reader = command.ExecuteReader();
        var links = new List<(string, string, string?)>();
        while (reader.Read()) links.Add((reader.GetString(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2)));
        return links;
    }

    /// <summary>One page of hypotheses in id order, and how many match; <paramref name="status"/> null means all.</summary>
    public (IReadOnlyList<HypothesisRecord> Page, int Total) List(string? status, int offset, int limit)
    {
        using var connection = _database.OpenConnection();
        var filter = status is null ? "1 = 1" : "Status = $status";
        // Total counts an index (O(n) in entries); a page is a seek plus limit rows. Neither is on a check path.
        using var count = connection.CreateCommand();
        count.CommandText = $"SELECT COUNT(*) FROM Hypotheses WHERE {filter};";
        if (status is not null) count.Parameters.AddWithValue("$status", status);
        var total = Convert.ToInt32(count.ExecuteScalar(), CultureInfo.InvariantCulture);
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {Columns} FROM Hypotheses WHERE {filter} ORDER BY Seq LIMIT $limit OFFSET $offset;";
        if (status is not null) command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue("$limit", limit);
        command.Parameters.AddWithValue("$offset", offset);
        using var reader = command.ExecuteReader();
        var page = new List<HypothesisRecord>();
        while (reader.Read()) page.Add(Read(reader));
        return (page, total);
    }

    /// <summary>
    /// Hypotheses on the same shape: the same change (ignoring case), and the same environment, host class and slot for
    /// each of those given. A blank condition matches any value, unless <paramref name="exact"/> (then only a blank). A seek on the shape index: O(log n + k) for k matches.
    /// </summary>
    public IReadOnlyList<HypothesisRecord> Neighbours(
        string? change, string? environment, string? hostClass, string? slot, bool exact = false)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        var filters = new List<string>();
        void Equal(string column, string parameter, string? value)
        {
            if (string.IsNullOrWhiteSpace(value) && !exact) return;
            // Same collation as the index, or the planner cannot seek it.
            filters.Add($"{column} = {parameter} COLLATE NOCASE");
            command.Parameters.AddWithValue(parameter, value ?? "");
        }
        Equal("ChangeText", "$change", change);
        Equal("Environment", "$env", environment);
        Equal("HostClass", "$host", hostClass);
        Equal("Slot", "$slot", slot);
        command.CommandText = $"SELECT {Columns} FROM Hypotheses WHERE {(filters.Count == 0 ? "1 = 0" : string.Join(" AND ", filters))} ORDER BY Seq;";
        using var reader = command.ExecuteReader();
        var found = new List<HypothesisRecord>();
        while (reader.Read()) found.Add(Read(reader));
        return found;
    }

    /// <summary>Records a test under the next test number of its hypothesis.</summary>
    public HypothesisTestRecord AddTest(string hypothesisId, string verdict, int predictedForms, int ruleHits,
        IReadOnlyDictionary<string, int> controls, int margin, int? losses, int? newReadings, int? workSteps, string? command)
    {
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var next = connection.CreateCommand();
        next.Transaction = transaction;
        next.CommandText = "SELECT COALESCE(MAX(TestNumber), 0) + 1 FROM HypothesisTests WHERE HypothesisId = $id;";
        next.Parameters.AddWithValue("$id", hypothesisId);
        var number = Convert.ToInt32(next.ExecuteScalar(), CultureInfo.InvariantCulture);
        var recorded = Now();
        using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO HypothesisTests (HypothesisId, TestNumber, Verdict, PredictedForms, RuleHits, ControlHitsJson, Margin,
                TrialAttestedLosses, TrialNewReadings, TrialWorkSteps, Command, RecordedUtc)
            VALUES ($id, $number, $verdict, $predicted, $hits, $controls, $margin, $losses, $readings, $steps, $command, $now);
            """;
        var p = insert.Parameters;
        p.AddWithValue("$id", hypothesisId);
        p.AddWithValue("$number", number);
        p.AddWithValue("$verdict", verdict);
        p.AddWithValue("$predicted", predictedForms);
        p.AddWithValue("$hits", ruleHits);
        p.AddWithValue("$controls", System.Text.Json.JsonSerializer.Serialize(controls, Json));
        p.AddWithValue("$margin", margin);
        p.AddWithValue("$losses", Db(losses));
        p.AddWithValue("$readings", Db(newReadings));
        p.AddWithValue("$steps", Db(workSteps));
        p.AddWithValue("$command", Db(command));
        p.AddWithValue("$now", recorded);
        insert.ExecuteNonQuery();
        Touch(connection, transaction, hypothesisId);
        transaction.Commit();
        return new HypothesisTestRecord(hypothesisId, number, verdict, predictedForms, ruleHits, controls, margin, losses,
            newReadings, workSteps, command, recorded);
    }

    /// <summary>Every test of a hypothesis, oldest first.</summary>
    public IReadOnlyList<HypothesisTestRecord> Tests(string hypothesisId)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT HypothesisId, TestNumber, Verdict, PredictedForms, RuleHits, ControlHitsJson, Margin, TrialAttestedLosses,
                   TrialNewReadings, TrialWorkSteps, Command, RecordedUtc
            FROM HypothesisTests WHERE HypothesisId = $id ORDER BY TestNumber;
            """;
        command.Parameters.AddWithValue("$id", hypothesisId);
        using var reader = command.ExecuteReader();
        var tests = new List<HypothesisTestRecord>();
        while (reader.Read())
            tests.Add(new HypothesisTestRecord(reader.GetString(0), reader.GetInt32(1), reader.GetString(2), reader.GetInt32(3),
                reader.GetInt32(4),
                System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, int>>(reader.GetString(5), Json) ?? [],
                reader.GetInt32(6), Int(reader, 7), Int(reader, 8), Int(reader, 9), Str(reader, 10), reader.GetString(11)));
        return tests;
    }

    /// <summary>Links Proposals to a hypothesis; a Proposal already linked stays as it is.</summary>
    public void LinkProposals(string hypothesisId, IEnumerable<string> proposalIds)
    {
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        foreach (var proposalId in proposalIds)
        {
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = "INSERT OR IGNORE INTO HypothesisProposals (HypothesisId, ProposalId, LinkedUtc) VALUES ($h, $p, $now);";
            insert.Parameters.AddWithValue("$h", hypothesisId);
            insert.Parameters.AddWithValue("$p", proposalId);
            insert.Parameters.AddWithValue("$now", Now());
            insert.ExecuteNonQuery();
        }
        Touch(connection, transaction, hypothesisId);
        transaction.Commit();
    }

    /// <summary>The Proposals linked to a hypothesis, oldest link first.</summary>
    public IReadOnlyList<string> ProposalsOf(string hypothesisId)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT ProposalId FROM HypothesisProposals WHERE HypothesisId = $id ORDER BY LinkedUtc, ProposalId;";
        command.Parameters.AddWithValue("$id", hypothesisId);
        using var reader = command.ExecuteReader();
        var ids = new List<string>();
        while (reader.Read()) ids.Add(reader.GetString(0));
        return ids;
    }

    /// <summary>The hypotheses a Proposal is linked to.</summary>
    public IReadOnlyList<string> HypothesesOf(string proposalId)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT HypothesisId FROM HypothesisProposals WHERE ProposalId = $id ORDER BY HypothesisId;";
        command.Parameters.AddWithValue("$id", proposalId);
        using var reader = command.ExecuteReader();
        var ids = new List<string>();
        while (reader.Read()) ids.Add(reader.GetString(0));
        return ids;
    }

    /// <summary>
    /// Sets the named columns of a hypothesis in one step, with <paramref name="alsoSuperseded"/> marked superseded by it.
    /// Column names come from callers in this assembly's own code, never from input.
    /// </summary>
    public void Set(string hypothesisId, IReadOnlyDictionary<string, object?> columns,
        IReadOnlyList<string>? alsoSuperseded = null, string? alsoDecision = null)
    {
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        if (columns.Count > 0)
        {
            using var update = connection.CreateCommand();
            update.Transaction = transaction;
            var sets = columns.Keys.Select((name, i) => $"{name} = $v{i}").ToList();
            update.CommandText = $"UPDATE Hypotheses SET {string.Join(", ", sets)}, UpdatedUtc = $now WHERE HypothesisId = $id;";
            var index = 0;
            foreach (var value in columns.Values) update.Parameters.AddWithValue($"$v{index++}", Db(value));
            update.Parameters.AddWithValue("$now", Now());
            update.Parameters.AddWithValue("$id", hypothesisId);
            update.ExecuteNonQuery();
        }
        foreach (var older in alsoSuperseded ?? [])
        {
            using var supersede = connection.CreateCommand();
            supersede.Transaction = transaction;
            supersede.CommandText = """
                UPDATE Hypotheses SET Status = 'superseded', SupersededBy = $by, UpdatedUtc = $now,
                    Decision = COALESCE(Decision, '') || CASE WHEN COALESCE(Decision, '') = '' THEN '' ELSE ' ' END || $note
                WHERE HypothesisId = $id;
                """;
            supersede.Parameters.AddWithValue("$by", hypothesisId);
            supersede.Parameters.AddWithValue("$now", Now());
            supersede.Parameters.AddWithValue("$note", alsoDecision ?? $"Superseded by {hypothesisId}.");
            supersede.Parameters.AddWithValue("$id", older);
            supersede.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    /// <summary>Replaces the question list of a hypothesis.</summary>
    public void SetQuestions(string hypothesisId, IReadOnlyList<HypothesisQuestion> questions) =>
        Set(hypothesisId, new Dictionary<string, object?>
        {
            ["QuestionsJson"] = System.Text.Json.JsonSerializer.Serialize(questions, Json),
        });

    private void Touch(SqliteConnection connection, SqliteTransaction transaction, string hypothesisId)
    {
        using var touch = connection.CreateCommand();
        touch.Transaction = transaction;
        touch.CommandText = "UPDATE Hypotheses SET UpdatedUtc = $now WHERE HypothesisId = $id;";
        touch.Parameters.AddWithValue("$now", Now());
        touch.Parameters.AddWithValue("$id", hypothesisId);
        touch.ExecuteNonQuery();
    }

    private static object Db(object? value) => value ?? DBNull.Value;

    private static string? Str(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);

    private static int? Int(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetInt32(i);

    private static double? Real(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetDouble(i);

    private static HypothesisRecord Read(SqliteDataReader r) => new(
        r.GetString(0), r.GetInt32(1), Str(r, 2), r.GetString(3), r.GetString(4), r.GetString(5), r.GetString(6),
        r.GetString(7), r.GetString(8), Str(r, 9),
        System.Text.Json.JsonSerializer.Deserialize<List<string>>(r.GetString(10), Json) ?? [], Str(r, 11), Str(r, 12),
        Int(r, 13), Int(r, 14),
        System.Text.Json.JsonSerializer.Deserialize<List<HypothesisQuestion>>(r.GetString(15), Json) ?? [], r.GetString(16),
        Str(r, 17), Str(r, 18), Str(r, 19), Str(r, 20), Str(r, 21), Str(r, 22), Str(r, 23), Int(r, 24), Int(r, 25),
        Int(r, 26), Int(r, 27), Int(r, 28), Real(r, 29), Real(r, 30), r.GetString(31), r.GetString(32));

    private string Now() => _clock.UtcNow.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
}
