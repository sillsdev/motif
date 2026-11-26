using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Host.Store;

namespace SIL.Motif.Tests.Commands;

/// <summary>
/// Builds a small project store in the shape of an older schema. The shape is <c>schemaN.sql</c>, the store DDL as the git
/// history had it when <c>user_version</c> was N (its header names the commit); schema 49 is the same kind of file. A canonical
/// dataset is then seeded with plain INSERTs against that DDL, so a table or column the step table forgot shows up as an upgrade
/// that fails or loses data. A new schema bump needs the previous schema's file; <see cref="StoreUpgradeFixtureTests"/> fails until it exists.
/// </summary>
internal static class StoreSchemaFixtures
{
    /// <summary>Every schema with a fixture file.</summary>
    public static readonly int[] All = [49, 55, 59, 61, 75, 76, 77, 78];

    /// <summary>The raw note a store below schema 62 holds, which the upgrade folds into a comment.</summary>
    public const string Note = "Please reconsider.\n\n- the second gloss\n- café 'quoted'";

    /// <summary>Creates the fixture store at <paramref name="path"/>.</summary>
    public static void Create(string path, ProjectLocator project, int schema)
    {
        using var db = Open(path);
        using var stream = typeof(StoreSchemaFixtures).Assembly.GetManifestResourceStream($"SIL.Motif.Tests.Commands.Store.schema{schema}.sql")
            ?? throw new InvalidOperationException($"No schema{schema}.sql is embedded.");
        using var reader = new StreamReader(stream);
        Execute(db, reader.ReadToEnd() + $"PRAGMA application_id = {MotifSchema.ApplicationId}; PRAGMA user_version = {schema};");
        Execute(db, "INSERT INTO MotifMetadata (Id, FullFwDataPath, FieldWorksProjectIdentity, MinimumWorkerVersion, CreatedUtc) " +
            "VALUES (1, $path, $identity, '0.1', '2020');", ("$path", project.FullFwDataPath), ("$identity", project.FieldWorksProjectIdentity));
        Execute(db, """
            INSERT INTO Proposals (ProposalId, CurrentIntentDigest, Status, AnchorJson)
                VALUES ('p-applied', 'sha256:a', 'applied', 'computed'), ('p-open', 'sha256:b', 'proposed', 'computed');
            INSERT INTO ProposalRevisions (ProposalId, IntentDigest, ProposalJson, CreatedUtc)
                VALUES ('p-applied', 'sha256:a', X'00FF10EE', '2020-01-01'), ('p-open', 'sha256:b', X'01020304', '2020-01-02');
            """);
        if (schema == 49)
        {
            Execute(db, "CREATE TABLE Jobs (Id INTEGER NOT NULL); INSERT INTO Jobs VALUES (1);");
            return;
        }
        Execute(db, """
            INSERT INTO Jobs (JobId, ProjectKey, Kind, Status, LineageId, InputJson, CreatedUtc, UpdatedUtc)
                VALUES ('j1', 'k', 'trial', 'done', 'l1', '{}', '2020', '2020');
            """);
        // Before schema 60 the limit counted analysis attempts, a hundredth of the parser work it counts now.
        var limit = Has(db, "NamedSelections", "PerWordStepLimit") ? "PerWordStepLimit" : "PerWordParserWorkLimit";
        Execute(db, $"INSERT INTO NamedSelections (SelectionName, TextIdsJson, AddedWordsJson, CreatedUtc, UpdatedUtc, TimeLimitMode, Revision, " +
            $"AssessBudgetSeconds, {limit}) VALUES ('sel', '[\"t1\"]', '[\"kitabu\"]', '2020', '2020', 'Estimated', 1, 3600, " +
            $"{(limit == "PerWordStepLimit" ? 50 : 5000)});");
        if (Has(db, "Proposals", "Note"))
            Execute(db, "UPDATE Proposals SET Note = $note, NoteIntentDigest = 'sha256:d', NoteUpdatedUtc = '2020-05-01T00:00:00.0000000+00:00' " +
                "WHERE ProposalId = 'p-open';", ("$note", Note));
        else
            Execute(db, "INSERT INTO ProposalComments (ProposalId, Kind, AuthorLabel, PersonName, Text, IntentDigest, CreatedUtc, EditedUtc) " +
                "VALUES ('p-open', 'standard', NULL, '', $note, 'sha256:d', '2020-05-01T00:00:00.0000000+00:00', NULL);", ("$note", Note));
        // A schema before 56 keeps an older Difference table that current code cannot read.
        var reference = Has(db, "ProposalDifferences", "Reference");
        Execute(db, $"INSERT INTO ProposalDifferences (DifferenceId, ProposalId, TrialJobId, BeforeAssessmentId, AfterAssessmentId, TextId, " +
            $"{(reference ? "Reference, " : "")}BaselineToken, SummaryJson, SummaryDigest, CreatedUtc) VALUES " +
            $"('d-applied', 'p-applied', 'j1', 'b', 'a', 't1', {(reference ? "'human', " : "")}'tok', '{{\"Reference\":\"human\"}}', 'x', '2020-02-01'), " +
            $"('d-open', 'p-open', 'j1', 'b', 'a', 't1', {(reference ? "'human', " : "")}'tok', '{{\"Reference\":\"human\"}}', 'x', '2020-02-01');");
        if (Has(db, "ProgressState", "Id"))
            Execute(db, "INSERT INTO ProgressState (Id, Revision, ContentRevision, JudgementRevision, SourceLastWriteUtc, TotalsJson, ExcludedJson) " +
                "VALUES (1, 'r', 'c', 'j', '2020', '{}', '{}');");
        if (Has(db, "GrammarCoverageMeasurements", "StampId"))
            Execute(db, "INSERT INTO GrammarCoverageMeasurements (StampId, Payload) VALUES ('stamp', '{}');");
    }

    /// <summary>Whether the store has the table with the column.</summary>
    public static bool Has(SqliteConnection db, string table, string column)
    {
        using var command = db.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name = '{column}';";
        return Convert.ToInt64(command.ExecuteScalar()) > 0;
    }

    /// <summary>The schema text of a store, whitespace-normalised, so two stores compare by what they define.</summary>
    public static string[] NormalisedSchema(SqliteConnection db)
    {
        using var command = db.CreateCommand();
        command.CommandText = "SELECT type, name, tbl_name, COALESCE(sql, '') FROM sqlite_master WHERE name NOT LIKE 'sqlite_%' ORDER BY type, name;";
        using var rows = command.ExecuteReader();
        var result = new List<string>();
        while (rows.Read())
            result.Add(string.Join("|", rows.GetString(0), rows.GetString(1), rows.GetString(2),
                System.Text.RegularExpressions.Regex.Replace(rows.GetString(3), @"\s+", " ").Replace("( ", "(").Replace(" )", ")").Trim()));
        return [.. result];
    }

    /// <summary>A digest of every row of <paramref name="table"/> in <paramref name="columns"/>, typed, so a changed byte changes it.</summary>
    public static string Digest(SqliteConnection db, string table, IReadOnlyList<string> columns)
    {
        using var command = db.CreateCommand();
        command.CommandText = $"SELECT {string.Join(",", columns.Select(MotifStoreUpgrade.Quote))} FROM {MotifStoreUpgrade.Quote(table)} ORDER BY rowid;";
        using var reader = command.ExecuteReader();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        while (reader.Read())
            for (var i = 0; i < reader.FieldCount; i++)
            {
                var value = reader.GetValue(i);
                var bytes = value switch
                {
                    DBNull => "N:"u8.ToArray(),
                    byte[] blob => [.. "B:"u8, .. blob],
                    _ => System.Text.Encoding.UTF8.GetBytes(value.GetType().Name + ":" + Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)),
                };
                hash.AppendData(BitConverter.GetBytes(bytes.Length));
                hash.AppendData(bytes);
            }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    public static SqliteConnection Open(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        connection.Open();
        return connection;
    }

    public static void Execute(SqliteConnection db, string sql, params (string Name, object Value)[] parameters)
    {
        using var command = db.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        command.ExecuteNonQuery();
    }
}
