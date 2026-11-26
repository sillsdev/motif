using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Host.Store;

namespace SIL.Motif.Tests.Commands;

/// <summary>
/// Builds a small project store in the shape of an older schema, from code, so no binary is checked in. A current store is
/// created and seeded with one canonical dataset, then each step newer than the wanted schema is undone (the tables it added
/// are dropped, the columns it changed are put back). Schema 49, the oldest shape current code still reads, is the checked-in
/// <c>schema49.sql</c> instead. A new schema bump adds its undo here; <see cref="SchemaFixtureCoverageTests"/> fails until it does.
/// </summary>
internal static class StoreSchemaFixtures
{
    /// <summary>The schemas built from the current shape, each one the last before a step in <see cref="StoreUpgradeSteps"/>.</summary>
    public static readonly int[] Built = [55, 59, 61, 75, 76, 77, 78];

    /// <summary>Every schema with a fixture: the real 49 shape and the built ones.</summary>
    public static IEnumerable<int> All => new[] { 49 }.Concat(Built);

    /// <summary>The raw note a built schema below 62 holds, which the upgrade folds into a comment.</summary>
    public const string Note = "Please reconsider.\n\n- the second gloss\n- café 'quoted'";

    /// <summary>Whether the fixture at <paramref name="schema"/> has a table the steps added at <paramref name="step"/>.</summary>
    public static bool Has(int schema, int step) => schema >= step;

    /// <summary>Creates the fixture store at <paramref name="path"/>.</summary>
    public static void Create(string path, ProjectLocator project, int schema)
    {
        if (schema == 49) { CreateSchema49(path, project); return; }
        using (MotifDatabase.OpenOwned(path, project, MotifSchema.CurrentSchema, new Version(1, 0))) { }
        SqliteConnection.ClearAllPools();
        using var db = Open(path);
        Seed(db, schema);
        Revert(db, schema);
        Execute(db, $"PRAGMA user_version = {schema};");
        SqliteConnection.ClearAllPools();
    }

    private static void Seed(SqliteConnection db, int schema)
    {
        var differences = Has(schema, 56);
        Execute(db, """
            INSERT INTO Proposals (ProposalId, CurrentIntentDigest, Status, Label, Rationale, RationaleIntentDigest, RationaleUpdatedUtc)
                VALUES ('p-applied', 'sha256:a', 'applied', 'first', 'Why.', 'sha256:r', '2020-05-01T00:00:00.0000000+00:00'),
                       ('p-open', 'sha256:b', 'proposed', 'second', NULL, NULL, NULL);
            INSERT INTO ProposalRevisions (ProposalId, IntentDigest, ProposalJson, CreatedUtc)
                VALUES ('p-applied', 'sha256:a', X'00FF10EE', '2020-01-01'), ('p-open', 'sha256:b', X'01020304', '2020-01-02');
            INSERT INTO NamedSelections (SelectionName, TextIdsJson, AddedWordsJson, CreatedUtc, UpdatedUtc, TimeLimitMode, Revision,
                AssessBudgetSeconds, PerWordParserWorkLimit)
                VALUES ('sel', '["t1"]', '["kitabu"]', '2020', '2020', 'Estimated', 1, 3600, 5000);
            INSERT INTO Jobs (JobId, ProjectKey, Kind, Status, LineageId, InputJson, CreatedUtc, UpdatedUtc)
                VALUES ('j1', 'k', 'trial', 'done', 'l1', '{}', '2020', '2020');
            INSERT INTO ProgressState (Id, Revision, ContentRevision, JudgementRevision, SourceLastWriteUtc, TotalsJson, ExcludedJson)
                VALUES (1, 'r', 'c', 'j', '2020', '{}', '{}');
            INSERT INTO ProposalComments (ProposalId, Kind, AuthorLabel, PersonName, Text, IntentDigest, CreatedUtc, EditedUtc)
                VALUES ('p-open', 'standard', NULL, '', $note, 'sha256:d', '2020-05-01T00:00:00.0000000+00:00', NULL);
            """, ("$note", Note));
        if (differences)
            Execute(db, """
                INSERT INTO ProposalDifferences (DifferenceId, ProposalId, TrialJobId, BeforeAssessmentId, AfterAssessmentId, TextId, Reference,
                    BaselineToken, SummaryJson, SummaryDigest, CreatedUtc)
                    VALUES ('d-applied', 'p-applied', 'j1', 'b', 'a', 't1', 'human', 'tok', '{"Reference":"human"}', 'x', '2020-02-01'),
                           ('d-open', 'p-open', 'j1', 'b', 'a', 't1', 'human', 'tok', '{"Reference":"human"}', 'x', '2020-02-01');
                """);
        if (Has(schema, 77))
            Execute(db, "INSERT INTO GrammarCoverageMeasurements (StampId, Payload) VALUES ('stamp', '{}');");
        if (Has(schema, 79))
            Execute(db, """
                INSERT INTO WordTypeDispositions (FormKey, Form, Category, Evidence, Intended, RecordedBy, RecordedUtc)
                    VALUES ('nairobi', 'Nairobi', 'name', 'A city.', NULL, 'ann', '2020-01-01');
                """);
    }

    // Undoes each step newer than the schema, newest first.
    private static void Revert(SqliteConnection db, int schema)
    {
        if (schema < 79) Execute(db, "DROP TABLE WordTypeDispositions;");
        if (schema < 77)
            Execute(db, "DROP TABLE GrammarCoverageNoAnalysisRank; DROP TABLE GrammarCoverageSegmentations; DROP TABLE GrammarCoverageMeasurements;");
        if (schema < 62)
        {
            Execute(db, """
                ALTER TABLE Proposals ADD COLUMN Note TEXT;
                ALTER TABLE Proposals ADD COLUMN NoteIntentDigest TEXT;
                ALTER TABLE Proposals ADD COLUMN NoteUpdatedUtc TEXT;
                UPDATE Proposals SET Note = (SELECT Text FROM ProposalComments c WHERE c.ProposalId = Proposals.ProposalId),
                    NoteIntentDigest = (SELECT IntentDigest FROM ProposalComments c WHERE c.ProposalId = Proposals.ProposalId),
                    NoteUpdatedUtc = (SELECT CreatedUtc FROM ProposalComments c WHERE c.ProposalId = Proposals.ProposalId);
                DROP TABLE ProposalComments;
                """);
        }
        if (schema < 60)
            Execute(db, "ALTER TABLE NamedSelections RENAME COLUMN PerWordParserWorkLimit TO PerWordStepLimit; " +
                "UPDATE NamedSelections SET PerWordStepLimit = PerWordStepLimit / 100;");
        if (schema < 56) Execute(db, "DROP TABLE ProposalDifferences;");
    }

    private static void CreateSchema49(string path, ProjectLocator project)
    {
        using var connection = Open(path);
        using var stream = typeof(StoreSchemaFixtures).Assembly.GetManifestResourceStream("SIL.Motif.Tests.Commands.Store.schema49.sql")!;
        using var reader = new StreamReader(stream);
        Execute(connection, reader.ReadToEnd() + """
            PRAGMA application_id = 1297044550;
            PRAGMA user_version = 49;
            CREATE TABLE Jobs (Id INTEGER NOT NULL);
            INSERT INTO Jobs VALUES (1);
            INSERT INTO MotifMetadata (Id, FullFwDataPath, FieldWorksProjectIdentity, MinimumWorkerVersion, CreatedUtc)
                VALUES (1, $path, $identity, '0.1', '2020');
            INSERT INTO Proposals (ProposalId, CurrentIntentDigest, Status, AnchorJson)
                VALUES ('p-applied', 'sha256:a', 'applied', 'computed'), ('p-open', 'sha256:b', 'open', NULL);
            INSERT INTO ProposalRevisions VALUES ('p-applied', 'sha256:a', X'00FF10EE', '2020-01-01'), ('p-open', 'sha256:b', X'01020304', '2020-01-02');
            """, ("$path", project.FullFwDataPath), ("$identity", project.FieldWorksProjectIdentity));
        SqliteConnection.ClearAllPools();
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
