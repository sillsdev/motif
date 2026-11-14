using System.Text.Json;
using Microsoft.Data.Sqlite;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Host.Store;
using SIL.Motif.Host.Texts;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Projects;
using Xunit;

namespace SIL.Motif.Tests.Commands;

public sealed class MotifStoreUpgradeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-upgrade", Guid.NewGuid().ToString("N"));
    private string Source => Path.Combine(_root, "project.motif.db");
    private ProjectLocator Project => new(Path.Combine(_root, "project.fwdata"), "fixture-project");

    public MotifStoreUpgradeTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void UpgradeNeverReadsDroppedSourceTables()
    {
        CreateCurrentSchemaAsOld();
        var reads = new HashSet<string>(StringComparer.Ordinal);
        void Observe(SqliteConnection connection, bool source)
        {
            SQLitePCL.strdelegate_authorizer authorizer = (_, action, table, _, database, _) =>
            {
                if (action == SQLitePCL.raw.SQLITE_READ && database == (source ? "main" : "old"))
                    reads.Add(table);
                return SQLitePCL.raw.SQLITE_OK;
            };
            Assert.Equal(SQLitePCL.raw.SQLITE_OK, SQLitePCL.raw.sqlite3_set_authorizer(connection.Handle, authorizer, null));
        }

        var report = MotifStoreUpgrade.Build(Source, Path.Combine(_root, "copy.db"), Project, true, null, onOpen: Observe);
        Assert.NotEmpty(report.Dropped);
        Assert.Contains("Proposals", reads);
        Assert.Empty(report.Dropped.Intersect(reads, StringComparer.Ordinal));
    }

    [Fact]
    public void EveryCurrentSchemaTableIsClassified()
    {
        CreateCurrentSchemaAsOld();
        using var connection = Connect(Source);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%';";
        using var rows = command.ExecuteReader();
        var tables = new HashSet<string>(StringComparer.Ordinal);
        while (rows.Read()) tables.Add(rows.GetString(0));
        var report = MotifStoreUpgrade.Copy(Source, Path.Combine(_root, "copy.db"));
        Assert.Empty(tables.Except(report.Carried.Keys.Concat(report.Dropped), StringComparer.Ordinal));
    }

    [Fact]
    public void Schema64UpgradeKeepsAuthoredHistoryAndDropsComputedEvidence()
    {
        CreateCurrentSchemaAsOld();
        using (var old = Connect(Source))
            Execute(old, """
                PRAGMA user_version = 64;
                DROP TABLE AssessmentWordSetBindings;
                INSERT INTO Proposals (ProposalId, CurrentIntentDigest, Status) VALUES ('p', 'digest', 'applied');
                INSERT INTO ProposalComments (ProposalId, PersonName, Text, CreatedUtc) VALUES ('p', 'person', 'comment', '2026');
                INSERT INTO Receipts VALUES ('receipt', 'p', 'digest', '{}', '2026');
                INSERT INTO AssessmentWordPayloads VALUES (x'01', x'02');
                """);
        var output = Path.Combine(_root, "copy.db");
        var report = MotifStoreUpgrade.Copy(Source, output);
        using var copy = Connect(output);
        Assert.Equal(64, report.SourceSchema);
        Assert.Contains("AssessmentWordPayloads", report.Dropped);
        Assert.Equal("applied", Text(copy, "SELECT Status FROM Proposals WHERE ProposalId = 'p';"));
        Assert.Equal("comment", Text(copy, "SELECT Text FROM ProposalComments WHERE ProposalId = 'p';"));
        Assert.Equal(1, Count(copy, "Receipts"));
        Assert.Equal(0, Count(copy, "AssessmentWordPayloads"));
    }

    [Fact]
    public void UpgradeCarriesTheParsimonySelectionPointerBesideTheDefaultOne()
    {
        CreateCurrentSchemaAsOld();
        using (var old = Connect(Source))
        {
            Execute(old, "INSERT INTO NamedSelections VALUES ('parsimony', '[]', '[\"kitabu\"]', '2020', '2020', " +
                "'Estimated', NULL, NULL, 1, 3600, NULL); INSERT INTO ParsimonySelection VALUES (1, 'parsimony');");
        }

        var output = Path.Combine(_root, "copy.db");
        var report = MotifStoreUpgrade.Copy(Source, output);

        Assert.Equal(1, report.Carried["ParsimonySelection"]);
        using var copy = Connect(output);
        Assert.Equal("parsimony", Text(copy, "SELECT SelectionName FROM ParsimonySelection WHERE Id = 1;"));
        Assert.Equal(0, Count(copy, "DefaultSelection"));
    }

    [Theory]
    [InlineData("200000", 20_000_000L)]
    [InlineData("1", 100L)]
    [InlineData("92233720368547759", long.MaxValue)]
    [InlineData("NULL", null)]
    public void UpgradeReadsAnOldNamedSelectionAttemptLimitAsOneHundredTimesItsValue(string oldValue, long? expected)
    {
        CreateCurrentSchemaAsOld();
        using (var old = Connect(Source))
        {
            Execute(old, "ALTER TABLE NamedSelections RENAME COLUMN PerWordParserWorkLimit TO PerWordStepLimit;" +
                "INSERT INTO NamedSelections (SelectionName, TextIdsJson, AddedWordsJson, CreatedUtc, UpdatedUtc, TimeLimitMode, " +
                $"Revision, AssessBudgetSeconds, PerWordStepLimit) VALUES ('old', '[]', '[\"kitabu\"]', '2020', '2020', 'Estimated', 1, 3600, {oldValue});");
        }

        var output = Path.Combine(_root, "copy.db");
        var report = MotifStoreUpgrade.Copy(Source, output);

        Assert.Equal(1, report.Carried["NamedSelections"]);
        using var copy = Connect(output);
        using var check = copy.CreateCommand();
        check.CommandText = "SELECT PerWordParserWorkLimit FROM NamedSelections WHERE SelectionName = 'old';";
        var stored = check.ExecuteScalar();
        if (expected is null) Assert.True(stored is null or DBNull);
        else Assert.Equal(expected.Value, Convert.ToInt64(stored));
    }

    [Fact]
    public void Schema61FoldsEachNoteIntoAFirstCommentByThePersonAndKeepsEveryProposal()
    {
        CreateCurrentSchemaAsOld();
        const string multiLine = "Please reconsider.\n\n- the second gloss\n- café 'quoted'";
        using (var old = Connect(Source))
        {
            Execute(old, """
                DROP TABLE ProposalPictures; DROP TABLE ProposalComments; DROP TABLE ProposalRationaleRevisions;
                ALTER TABLE Proposals ADD COLUMN Note TEXT;
                ALTER TABLE Proposals ADD COLUMN NoteIntentDigest TEXT;
                ALTER TABLE Proposals ADD COLUMN NoteUpdatedUtc TEXT;
                INSERT INTO Proposals (ProposalId, Status) VALUES ('plain', 'proposed');
                INSERT INTO Proposals (ProposalId, Status, Note) VALUES ('closed', 'rejected', 'Kept though decided.');
                PRAGMA user_version = 61;
                """);
            using var insert = old.CreateCommand();
            insert.CommandText = "INSERT INTO Proposals (ProposalId, Status, Rationale, Note, NoteIntentDigest, NoteUpdatedUtc) " +
                "VALUES ('open', 'proposed', 'Why.', $note, 'sha256:d', '2020-05-01T00:00:00.0000000+00:00');";
            insert.Parameters.AddWithValue("$note", multiLine);
            insert.ExecuteNonQuery();
        }

        var report = MotifStoreUpgrade.Copy(Source, Path.Combine(_root, "copy.db"));

        Assert.Equal(61, report.SourceSchema);
        Assert.Equal(3, report.Carried["Proposals"]);
        Assert.Equal(2, report.Carried["ProposalComments"]);
        using var upgraded = Connect(Path.Combine(_root, "copy.db"));
        Assert.Equal(3, Count(upgraded, "Proposals"));
        Assert.Equal(2, Count(upgraded, "ProposalComments"));
        Assert.Equal(multiLine, Text(upgraded, "SELECT Text FROM ProposalComments WHERE ProposalId = 'open';"));
        Assert.Equal("sha256:d", Text(upgraded, "SELECT IntentDigest FROM ProposalComments WHERE ProposalId = 'open';"));
        Assert.Equal("2020-05-01T00:00:00.0000000+00:00", Text(upgraded, "SELECT CreatedUtc FROM ProposalComments WHERE ProposalId = 'open';"));
        Assert.Equal("Kept though decided.", Text(upgraded, "SELECT Text FROM ProposalComments WHERE ProposalId = 'closed';"));
        Assert.Equal(1, Scalar(upgraded, "SELECT COUNT(*) FROM ProposalComments WHERE AuthorLabel IS NULL AND PersonName = '' AND " +
            "Kind = 'standard' AND EditedUtc IS NULL AND ProposalId = 'open';"));
        Assert.Equal(0, Scalar(upgraded, "SELECT COUNT(*) FROM pragma_table_info('Proposals') WHERE name LIKE 'Note%';"));
        Assert.Equal(0, Count(upgraded, "ProposalRationaleRevisions"));
        Assert.Equal(0, Count(upgraded, "ProposalPictures"));
    }

    [Fact]
    public void UpgradeFromSchema68KeepsRequestsAndDatabaseHeldCorpusText()
    {
        CreateCurrentSchemaAsOld();
        using (var old = Connect(Source))
            Execute(old, """
                ALTER TABLE Corpora DROP COLUMN FileBacked;
                ALTER TABLE CorpusDocuments DROP COLUMN FileBacked;
                PRAGMA user_version = 68;
                INSERT INTO Requests (RequestId, TargetKind, TargetId, TargetLabel, Status, CreatedUtc)
                    VALUES ('request/1', 'word', 'word', 'word', 'open', '2026-01-01T00:00:00Z');
                INSERT INTO Proposals (ProposalId, Status, RequestId) VALUES ('open', 'proposed', 'request/1');
                INSERT INTO Corpora (CorpusId, ProvenanceJson) VALUES ('corpus', '{}');
                INSERT INTO CorpusDocuments (CorpusId, DocumentId, OrdinalIndex, Title, Source, Text, ContentSha256, IngestedUtc)
                    VALUES ('corpus', 'doc', 0, 'Document', 'paste:', 'original text', 'hash', '2026-01-01T00:00:00Z');
                """);
        var output = Path.Combine(_root, "copy.db");
        var report = MotifStoreUpgrade.Copy(Source, output);
        Assert.Equal(68, report.SourceSchema);
        Assert.Equal(MotifSchema.CurrentSchema, report.TargetSchema);
        using var upgraded = Connect(output);
        Assert.Equal("request/1", Text(upgraded, "SELECT RequestId FROM Proposals WHERE ProposalId = 'open';"));
        Assert.Equal(1, Count(upgraded, "Requests"));
        Assert.Equal("original text", Text(upgraded, "SELECT Text FROM CorpusDocuments WHERE DocumentId = 'doc';"));
        Assert.Equal(0, Scalar(upgraded, "SELECT FileBacked FROM Corpora WHERE CorpusId = 'corpus';"));
        Assert.Equal(0, Scalar(upgraded, "SELECT FileBacked FROM CorpusDocuments WHERE DocumentId = 'doc';"));
    }

    [Fact]
    public void UpgradeKeepsRequestsAndTheLinkFromAProposalToItsRequest()
    {
        CreateCurrentSchemaAsOld();
        using (var old = Connect(Source))
            Execute(old, """
                INSERT INTO Requests (RequestId, TargetKind, TargetId, TargetLabel, Note, Status, CreatedUtc)
                    VALUES ('request/1', 'word', 'kita', 'kita', 'Please look.', 'open', '2026-01-01T00:00:00Z');
                INSERT INTO Proposals (ProposalId, Status, RequestId) VALUES ('open', 'proposed', 'request/1');
                """);

        var report = MotifStoreUpgrade.Copy(Source, Path.Combine(_root, "copy.db"));

        Assert.Equal(1, report.Carried["Requests"]);
        using var upgraded = Connect(Path.Combine(_root, "copy.db"));
        Assert.Equal("Please look.", Text(upgraded, "SELECT Note FROM Requests WHERE RequestId = 'request/1';"));
        Assert.Equal("request/1", Text(upgraded, "SELECT RequestId FROM Proposals WHERE ProposalId = 'open';"));
    }

    [Fact]
    public void UpgradeKeepsTheHypothesisLedgerWithItsProposalLinksAndTests()
    {
        CreateCurrentSchemaAsOld();
        using (var old = Connect(Source))
            Execute(old, """
                INSERT INTO Hypotheses (HypothesisId, Seq, Statement, Mechanism, ChangeText, CreatedUtc, UpdatedUtc)
                    VALUES ('H-1', 1, 'd becomes r', 'environment', 'd>r', '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z');
                INSERT INTO HypothesisProposals (HypothesisId, ProposalId, LinkedUtc) VALUES ('H-1', 'p/1', '2026-01-01T00:00:00Z');
                INSERT INTO HypothesisTests (HypothesisId, TestNumber, Verdict, PredictedForms, RuleHits, ControlHitsJson, Margin, RecordedUtc)
                    VALUES ('H-1', 1, 'PASS', 25, 60, '{}', 40, '2026-01-01T00:00:00Z');
                """);

        var report = MotifStoreUpgrade.Copy(Source, Path.Combine(_root, "copy.db"));

        Assert.Equal(1, report.Carried["Hypotheses"]);
        Assert.Equal(1, report.Carried["HypothesisProposals"]);
        Assert.Equal(1, report.Carried["HypothesisTests"]);
    }

    [Fact]
    public void UpgradeCarriesProposalPicturesWithTheirBytes()
    {
        CreateCurrentSchemaAsOld();
        var bytes = new byte[5000];
        new Random(11).NextBytes(bytes);
        using (var old = Connect(Source))
        {
            Execute(old, "INSERT INTO Proposals (ProposalId, Status) VALUES ('open', 'proposed');");
            using var insert = old.CreateCommand();
            insert.CommandText = "INSERT INTO ProposalPictures (ProposalId, CommentId, MediaType, Width, Height, Bytes, CreatedUtc) " +
                "VALUES ('open', NULL, 'image/png', 40, 30, $bytes, '2026-01-01T00:00:00Z');";
            insert.Parameters.AddWithValue("$bytes", bytes);
            insert.ExecuteNonQuery();
        }

        var report = MotifStoreUpgrade.Copy(Source, Path.Combine(_root, "copy.db"));

        Assert.Equal(1, report.Carried["ProposalPictures"]);
        using var upgraded = Connect(Path.Combine(_root, "copy.db"));
        using var read = upgraded.CreateCommand();
        read.CommandText = "SELECT Bytes FROM ProposalPictures WHERE ProposalId = 'open';";
        Assert.Equal(bytes, (byte[])read.ExecuteScalar()!);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Schema61WithNoNoteWorthKeepingMakesNoComments(bool withProposals)
    {
        CreateCurrentSchemaAsOld();
        using (var old = Connect(Source))
        {
            Execute(old, """
                DROP TABLE ProposalPictures; DROP TABLE ProposalComments; DROP TABLE ProposalRationaleRevisions;
                ALTER TABLE Proposals ADD COLUMN Note TEXT;
                ALTER TABLE Proposals ADD COLUMN NoteIntentDigest TEXT;
                ALTER TABLE Proposals ADD COLUMN NoteUpdatedUtc TEXT;
                PRAGMA user_version = 61;
                """);
            if (withProposals)
                Execute(old, "INSERT INTO Proposals (ProposalId, Status, Note) VALUES ('none', 'proposed', NULL), " +
                    "('empty', 'proposed', ''), ('blank', 'rejected', '   ');");
        }

        var report = MotifStoreUpgrade.Copy(Source, Path.Combine(_root, "copy.db"));

        Assert.Equal(0, report.Carried["ProposalComments"]);
        using var upgraded = Connect(Path.Combine(_root, "copy.db"));
        Assert.Equal(withProposals ? 3 : 0, Count(upgraded, "Proposals"));
        Assert.Equal(0, Count(upgraded, "ProposalComments"));
    }

    [Fact]
    public void UpgradeCarriesRationaleRevisionsCommentsAndPicturesWithTheirNumbers()
    {
        CreateCurrentSchemaAsOld();
        using (var old = Connect(Source))
        {
            Execute(old, """
                INSERT INTO Proposals (ProposalId, Status, Rationale) VALUES ('p', 'proposed', 'Two.');
                INSERT INTO ProposalRationaleRevisions (ProposalId, Text, AuthorLabel, PersonName, CreatedUtc)
                    VALUES ('p', 'One.', 'Claude (Opus 5.5)', 'John', '2020-01-01'), ('p', 'Two.', NULL, 'John', '2020-01-02');
                INSERT INTO ProposalComments (CommentId, ProposalId, AuthorLabel, PersonName, Text, CreatedUtc, EditedUtc)
                    VALUES (7, 'p', 'Claude', 'John', 'Agent.', '2020-01-03', NULL), (9, 'p', NULL, 'John', 'Person.', '2020-01-04', '2020-01-05');
                INSERT INTO ProposalPictures (ProposalId, CommentId, MediaType, Width, Height, Bytes, CreatedUtc)
                    VALUES ('p', 9, 'image/png', 2, 3, X'0102', '2020-01-06'), ('p', NULL, 'image/png', 1, 1, X'03', '2020-01-07');
                """);
        }

        var report = MotifStoreUpgrade.Copy(Source, Path.Combine(_root, "copy.db"));

        Assert.Equal(2, report.Carried["ProposalRationaleRevisions"]);
        Assert.Equal(2, report.Carried["ProposalComments"]);
        Assert.Equal(2, report.Carried["ProposalPictures"]);
        using var upgraded = Connect(Path.Combine(_root, "copy.db"));
        Assert.Equal("Person.", Text(upgraded, "SELECT Text FROM ProposalComments WHERE CommentId = 9;"));
        Assert.Equal("2020-01-05", Text(upgraded, "SELECT EditedUtc FROM ProposalComments WHERE CommentId = 9;"));
        Assert.Equal("Claude (Opus 5.5)", Text(upgraded, "SELECT AuthorLabel FROM ProposalRationaleRevisions WHERE Text = 'One.';"));
        Assert.Equal(1, Scalar(upgraded, "SELECT COUNT(*) FROM ProposalPictures WHERE CommentId = 9 AND Width = 2 AND length(Bytes) = 2;"));
    }

    [Fact]
    public void Schema49CarriesRevisionsAndReportsUnreadableAndDroppedData()
    {
        CreateSchema49();
        var output = Path.Combine(_root, "copy.db");
        var report = MotifStoreUpgrade.Copy(Source, output);
        Assert.Equal(49, report.SourceSchema);
        Assert.Equal(MotifSchema.CurrentSchema, report.TargetSchema);
        Assert.Equal(1, report.Carried["Proposals"]);
        Assert.Equal(1, report.Carried["ProposalRevisions"]);
        Assert.Single(report.UnreadableRevisions);
        Assert.Contains("Jobs", report.Dropped);
        Assert.True(File.Exists(output + ".upgrade.json"));
        using var db = MotifDatabase.OpenOwned(output, Project, MotifSchema.CurrentSchema, new Version(1, 0));
        using var connection = db.OpenConnection();
        Assert.Equal(1, Count(connection, "Proposals"));
        Assert.Equal(1, Count(connection, "ProposalRevisions"));
        Assert.Equal(2L, Scalar(connection, "PRAGMA auto_vacuum;"));
        using var check = connection.CreateCommand();
        check.CommandText = "SELECT AnchorJson, CurrentAssessmentId, SetupSkippedUtc FROM Proposals, MotifMetadata;";
        using var row = check.ExecuteReader();
        Assert.True(row.Read());
        Assert.True(row.IsDBNull(0));
        Assert.True(row.IsDBNull(1));
        Assert.Equal("2020", row.GetString(2));
    }

    [Fact]
    public void Schema60UpgradeDropsBundlesReportsAndKeptViewsAndKeepsProposals()
    {
        CreateCurrentSchemaAsOld();
        using (var old = Connect(Source))
        {
            Execute(old, "INSERT INTO Proposals (ProposalId, Status) VALUES ('proposal', 'proposed');" +
                "INSERT INTO Reports (ReportId, ProposalId, ReportJson, CreatedUtc) VALUES ('report', 'proposal', '{}', '2020');" +
                "INSERT INTO ParsimonyBundles VALUES ('bundle', '{}', 'key', 'sha256:a', 'material', 'dir', " +
                "'dir/grammar-facts.sqlite', 'dir/evidence.sqlite', 'root', 'baseline', NULL, 'model', 1, 'f', 1, 'e', " +
                "'2020', 'available', NULL);" +
                "INSERT INTO ParsimonyFrozenViews VALUES ('bundle', 'allomorph-context', 'digest', 1, 'items', 0, '{}');");
        }

        var output = Path.Combine(_root, "copy.db");
        var report = MotifStoreUpgrade.Copy(Source, output);

        Assert.Equal(MotifSchema.CurrentSchema, report.TargetSchema);
        Assert.Equal(1, report.Carried["Proposals"]);
        foreach (var table in new[] { "ParsimonyBundles", "ParsimonyFrozenViews", "Reports" })
            Assert.Contains(table, report.Dropped);
        using var copy = Connect(output);
        Assert.Equal(1, Count(copy, "Proposals"));
        Assert.Equal(0, Count(copy, "ParsimonyBundles"));
        Assert.Equal(0, Count(copy, "ParsimonyFrozenViews"));
        Assert.Equal(0, Count(copy, "Reports"));
    }

    [Fact]
    public async Task CliCopiesAnImmutableSourceAndPrintsTheReport()
    {
        CreateSchema49();
        var before = File.ReadAllBytes(Source);
        var output = Path.Combine(_root, "cli.db");
        var result = await CliProcess.RunAsync(_root, null, false, "store", "upgrade", "--source", Source,
            "--out", output, "--json");
        Assert.True(result.ExitCode == 0, result.Error);
        var report = JsonSerializer.Deserialize<MotifStoreUpgrade.Report>(result.Output)!;
        Assert.Equal(1, report.Carried["ProposalRevisions"]);
        Assert.Contains("Jobs", report.Dropped);
        Assert.Equal(before, File.ReadAllBytes(Source));
        Assert.True(File.Exists(output + ".upgrade.json"));
    }

    [Fact]
    public void UpgradeKeepsADecidedProposalsDifferenceSummaryAndDropsItsWordsFromTheSchemaSummariesStart()
    {
        SeedDifferences(MotifSchema.CurrentSchema - 1);

        var report = MotifStoreUpgrade.Build(Source, Path.Combine(_root, "copy.db"), null, true, null,
            firstSummarySchema: MotifSchema.CurrentSchema - 1);

        Assert.Equal(2, report.Carried["ProposalDifferences"]);
        Assert.Contains("ProposalDifferenceCases", report.Dropped);
        Assert.DoesNotContain("ProposalDifferences", report.Dropped);
        using var upgraded = Connect(Path.Combine(_root, "copy.db"));
        Assert.Equal("newer", Text(upgraded, "SELECT SummaryJson FROM ProposalDifferences WHERE TextId = 't1' AND ProposalId = 'rej';"));
        Assert.Equal(1, Count(upgraded, "ProposalDifferences WHERE TextId = 't2'"));
        Assert.Equal(0, Count(upgraded, "ProposalDifferences WHERE ProposalId = 'open'"));
        Assert.Equal(2, Count(upgraded, "ProposalDifferences WHERE WordDetailDroppedUtc IS NOT NULL"));
        Assert.Equal(0, Count(upgraded, "ProposalDifferenceCases"));
    }

    [Fact]
    public void UpgradeFromBeforeSummariesStartCarriesNoDifferenceSummary()
    {
        SeedDifferences(MotifStoreUpgrade.FirstSummarySchema - 1);

        var report = MotifStoreUpgrade.Copy(Source, Path.Combine(_root, "copy.db"));

        Assert.Contains("ProposalDifferences", report.Dropped);
        Assert.False(report.Carried.ContainsKey("ProposalDifferences") && report.Carried["ProposalDifferences"] != 0);
        using var upgraded = Connect(Path.Combine(_root, "copy.db"));
        Assert.Equal(0, Count(upgraded, "ProposalDifferences"));
        Assert.Equal(2, Count(upgraded, "Proposals"));
    }

    [Fact]
    public void UpgradeFromSchema56KeepsProposalsAndTheirDifferenceSummariesAndDropsTheBaselineAndItsSummary()
    {
        // Schema 57 changed the words a Baseline's summary lists, so no older summary may survive.
        var key = ProjectWorkspaceKey.Compute(Project);
        using (var database = MotifDatabase.OpenOwned(Source, Project, MotifSchema.CurrentSchema, new Version(1, 0)))
        {
            var token = new BaselineToken("project-id", "sha256:" + new string('1', 64), "projection-v1",
                "2026-09-24T10:00:00Z", "sha256:" + new string('a', 64));
            var saved = DateTimeOffset.Parse("2026-09-24T10:00:00Z");
            new BaselineRepository(database).Record(key,
                new BaselinePublication(Path.Combine(_root, "baseline"), Path.Combine(_root, "baseline", "b.fwdata"), token),
                saved, saved, TestTextWords.Empty, new ProjectSummarySnapshot(1, 1, 1, 0, 0, ["1990"], []));
            using var seed = database.OpenConnection();
            Execute(seed, """
                INSERT INTO Proposals (ProposalId, Status) VALUES ('rej', 'rejected'), ('open', 'proposed');
                INSERT INTO ProposalDifferences VALUES
                    ('d-rej', 'rej', 'j', 'b', 'a', 't1', 'human', 'tok', 'summary', 'x', '2020-02-01', NULL),
                    ('d-open', 'open', 'j', 'b', 'a', 't1', 'human', 'tok', 'undecided', 'x', '2020-01-01', NULL);
                """);
        }
        using (var old = Connect(Source)) Execute(old, "PRAGMA user_version = 56;");

        var report = MotifStoreUpgrade.Copy(Source, Path.Combine(_root, "copy.db"));

        Assert.Equal(56, report.SourceSchema);
        Assert.Equal(MotifSchema.CurrentSchema, report.TargetSchema);
        Assert.Contains("BaselineSummaries", report.Dropped);
        Assert.Contains("Baselines", report.Dropped);
        Assert.Equal(2, report.Carried["Proposals"]);
        Assert.Equal(1, report.Carried["ProposalDifferences"]);
        using var upgraded = Connect(Path.Combine(_root, "copy.db"));
        Assert.Equal(0, Count(upgraded, "Baselines"));
        Assert.Equal(0, Count(upgraded, "BaselineSummaries"));
        Assert.Equal(2, Count(upgraded, "Proposals"));
        Assert.Equal("summary", Text(upgraded, "SELECT SummaryJson FROM ProposalDifferences WHERE ProposalId = 'rej';"));
        Assert.Equal(1, Count(upgraded, "ProposalDifferences WHERE WordDetailDroppedUtc IS NOT NULL"));
    }

    [Fact]
    public void UpgradeFromSchema57KeepsDecidedAndDeferredSummariesWithTheReferenceEachNamesAndDropsTheRest()
    {
        SeedDifferences(MotifSchema.CurrentSchema - 2);
        using (var old = Connect(Source))
            Execute(old, """
                INSERT INTO Proposals (ProposalId, Status) VALUES ('def', 'deferred');
                INSERT INTO ProposalDifferences VALUES
                    ('d-def', 'def', 'j', 'b', 'a', 't1', 'tok', '{"Reference":"source-validated"}', 'x', '2020-03-01'),
                    ('d-def2', 'def', 'j', 'b', 'a', 't2', 'tok', '{"DifferenceId":"d-def2"}', 'x', '2020-03-01');
                INSERT INTO ProposalDifferenceCases VALUES ('d-def', 'w', 'w', 1, 'a', 'b', 0, '{}', 'x');
                """);

        var report = MotifStoreUpgrade.Build(Source, Path.Combine(_root, "copy.db"), null, true, null,
            firstSummarySchema: MotifSchema.CurrentSchema - 2);

        Assert.Equal(MotifSchema.CurrentSchema - 2, report.SourceSchema);
        Assert.Equal(4, report.Carried["ProposalDifferences"]);
        using var upgraded = Connect(Path.Combine(_root, "copy.db"));
        Assert.Equal("source-validated", Text(upgraded, "SELECT Reference FROM ProposalDifferences WHERE DifferenceId = 'd-def';"));
        Assert.Equal("human", Text(upgraded, "SELECT Reference FROM ProposalDifferences WHERE DifferenceId = 'd-def2';"));
        Assert.Equal("human", Text(upgraded, "SELECT Reference FROM ProposalDifferences WHERE DifferenceId = 'd-new';"));
        Assert.Equal(0, Count(upgraded, "ProposalDifferences WHERE ProposalId = 'open'"));
        Assert.Equal(4, Count(upgraded, "ProposalDifferences WHERE WordDetailDroppedUtc IS NOT NULL"));
        Assert.Equal(0, Count(upgraded, "ProposalDifferenceCases"));
    }

    [Fact]
    public void OpenUpgradesAndKeepsTheOriginalBackup()
    {
        CreateSchema49();
        using (var db = MotifDatabase.OpenOwned(Source, Project, MotifSchema.CurrentSchema, new Version(1, 0)))
        using (var connection = db.OpenConnection())
            Assert.Equal(1, Count(connection, "ProposalRevisions"));
        var backup = Source + ".schema49.bak";
        Assert.True(File.Exists(backup));
        using var original = Connect(backup);
        Assert.Equal(49L, Scalar(original, "PRAGMA user_version;"));
        Assert.Equal(1, Count(original, "ProposalRevisions"));
        Assert.True(File.Exists(Source + ".upgrade.json"));
    }

    [Fact]
    public void SuppressedRequestDoesNotUpgrade()
    {
        CreateSchema49();
        using (StoreUpgradeContext.Suppress())
            Assert.Throws<MotifStoreVersionException>(() =>
                MotifDatabase.OpenOwned(Source, Project, MotifSchema.CurrentSchema, new Version(1, 0)));
        Assert.False(File.Exists(Source + ".schema49.bak"));
    }

    [Fact]
    public void InterruptedRenameCompletesFromVerifiedOutput()
    {
        CreateSchema49();
        var pending = Source + ".upgrading";
        var backup = Source + ".schema49.bak";
        var report = MotifStoreUpgrade.Copy(Source, pending) with { BackupPath = backup };
        File.WriteAllText(pending + ".upgrade.json", JsonSerializer.Serialize(report));
        File.Move(Source, backup);
        using var db = MotifDatabase.OpenOwned(Source, Project, MotifSchema.CurrentSchema, new Version(1, 0));
        using var connection = db.OpenConnection();
        Assert.Equal(1, Count(connection, "ProposalRevisions"));
        Assert.True(File.Exists(backup));
        Assert.False(File.Exists(pending));
    }

    [Fact]
    public void StaleOutputAndItsJournalAreDiscardedBeforeRetry()
    {
        CreateSchema49();
        File.WriteAllText(Source + ".upgrading", "interrupted");
        File.WriteAllText(Source + ".upgrading-journal", "interrupted");
        using var db = MotifDatabase.OpenOwned(Source, Project, MotifSchema.CurrentSchema, new Version(1, 0));
        using var connection = db.OpenConnection();
        Assert.Equal(1, Count(connection, "ProposalRevisions"));
        Assert.False(File.Exists(Source + ".upgrading-journal"));
        Assert.True(File.Exists(Source + ".schema49.bak"));
    }

    [Fact]
    public void ExistingBackupIsNeverOverwritten()
    {
        CreateSchema49();
        var backup = Source + ".schema49.bak";
        File.WriteAllText(backup, "previous backup");
        Assert.Throws<IOException>(() => MotifDatabase.OpenOwned(Source, Project, MotifSchema.CurrentSchema, new Version(1, 0)));
        Assert.Equal("previous backup", File.ReadAllText(backup));
        using var original = Connect(Source);
        Assert.Equal(49L, Scalar(original, "PRAGMA user_version;"));
    }

    [Fact]
    public void UnknownTablesAreNotSilentlyDiscarded()
    {
        CreateSchema49();
        using (var connection = Connect(Source))
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "CREATE TABLE Unclassified (Value TEXT);";
            command.ExecuteNonQuery();
        }
        var output = Path.Combine(_root, "copy.db");
        Assert.Throws<InvalidDataException>(() => MotifStoreUpgrade.Copy(Source, output));
        Assert.False(File.Exists(output));
        Assert.True(File.Exists(Source));
    }

    [Fact]
    public void MissingRequiredSelectionColumnsUseCurrentCodeDefaults()
    {
        CreateSchema49();
        using (var connection = Connect(Source))
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                CREATE TABLE NamedSelections (SelectionName TEXT PRIMARY KEY, TextIdsJson TEXT NOT NULL,
                    AddedWordsJson TEXT NOT NULL, CreatedUtc TEXT NOT NULL, UpdatedUtc TEXT NOT NULL);
                INSERT INTO NamedSelections VALUES ('example', $empty, $empty, '2020', '2020');
                """;
            command.Parameters.AddWithValue("$empty", "[]");
            command.ExecuteNonQuery();
        }
        var output = Path.Combine(_root, "copy.db");
        MotifStoreUpgrade.Copy(Source, output);
        using var copy = Connect(output);
        using var check = copy.CreateCommand();
        check.CommandText = "SELECT TimeLimitMode, Revision, AssessBudgetSeconds FROM NamedSelections;";
        using var row = check.ExecuteReader();
        Assert.True(row.Read());
        Assert.Equal("Estimated", row.GetString(0));
        Assert.Equal(1, row.GetInt32(1));
        Assert.Equal(3600, row.GetInt32(2));
    }

    [Fact]
    public void MissingIdentityColumnIsNotInvented()
    {
        CreateSchema49();
        using (var connection = Connect(Source))
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "CREATE TABLE NamedSelections (TextIdsJson TEXT NOT NULL);";
            command.ExecuteNonQuery();
        }
        var output = Path.Combine(_root, "copy.db");
        Assert.Throws<InvalidDataException>(() => MotifStoreUpgrade.Copy(Source, output));
        Assert.False(File.Exists(output));
    }

    [Fact]
    public void ProjectMismatchDoesNotReplaceOrBackUpTheSource()
    {
        CreateSchema49();
        Assert.Throws<InvalidDataException>(() => MotifDatabase.OpenOwned(Source,
            new ProjectLocator(Project.FullFwDataPath, "other"), MotifSchema.CurrentSchema, new Version(1, 0)));
        Assert.True(File.Exists(Source));
        Assert.False(File.Exists(Source + ".schema49.bak"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private void CreateCurrentSchemaAsOld()
    {
        using (MotifDatabase.OpenOwned(Source, Project, MotifSchema.CurrentSchema, new Version(1, 0))) { }
        using var connection = Connect(Source);
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA user_version = {MotifSchema.CurrentSchema - 1};";
        command.ExecuteNonQuery();
    }

    // A store one generation behind with an old-shape Difference table, two Texts' summaries and one word row.
    private void SeedDifferences(int schema)
    {
        CreateCurrentSchemaAsOld();
        using var old = Connect(Source);
        Execute(old, $$"""
            DROP INDEX IX_ProposalDifferences_ProposalText;
            ALTER TABLE ProposalDifferences DROP COLUMN WordDetailDroppedUtc;
            ALTER TABLE ProposalDifferences DROP COLUMN Reference;
            CREATE INDEX IX_ProposalDifferences_ProposalText ON ProposalDifferences(ProposalId, TextId);
            INSERT INTO Proposals (ProposalId, Status) VALUES ('rej', 'rejected'), ('open', 'proposed');
            INSERT INTO ProposalDifferences VALUES
                ('d-old', 'rej', 'j', 'b', 'a', 't1', 'tok', 'older', 'x', '2020-01-01'),
                ('d-new', 'rej', 'j', 'b', 'a', 't1', 'tok', 'newer', 'x', '2020-02-01'),
                ('d-two', 'rej', 'j', 'b', 'a', 't2', 'tok', 'second', 'x', '2020-02-01'),
                ('d-open', 'open', 'j', 'b', 'a', 't1', 'tok', 'undecided', 'x', '2020-01-01');
            INSERT INTO ProposalDifferenceCases VALUES ('d-new', 'w', 'w', 1, 'a', 'b', 0, '{}', 'x');
            PRAGMA user_version = {{schema}};
            """);
    }

    private void CreateSchema49()
    {
        using var connection = Connect(Source);
        using var stream = typeof(MotifStoreUpgradeTests).Assembly.GetManifestResourceStream(
            "SIL.Motif.Tests.Commands.Store.schema49.sql")!;
        using var reader = new StreamReader(stream);
        using var command = connection.CreateCommand();
        command.CommandText = reader.ReadToEnd() + """
            PRAGMA application_id = 1297044550;
            PRAGMA user_version = 49;
            CREATE TABLE Jobs (Id INTEGER NOT NULL);
            INSERT INTO Jobs VALUES (1);
            INSERT INTO MotifMetadata (Id, FullFwDataPath, FieldWorksProjectIdentity, MinimumWorkerVersion, CreatedUtc)
                VALUES (1, $path, $identity, '0.1', '2020');
            UPDATE MotifMetadata SET CurrentAssessmentId = 'old', SetupSkippedUtc = '2020';
            INSERT INTO Proposals (ProposalId, CurrentIntentDigest, Status, AnchorJson)
                VALUES ('fixture', 'digest', 'open', 'computed');
            INSERT INTO ProposalRevisions VALUES ('fixture', 'digest', $json, '2020');
            """;
        command.Parameters.AddWithValue("$path", Project.FullFwDataPath);
        command.Parameters.AddWithValue("$identity", Project.FieldWorksProjectIdentity);
        command.Parameters.AddWithValue("$json", new byte[] { 0xff });
        command.ExecuteNonQuery();
    }

    private static SqliteConnection Connect(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        connection.Open();
        return connection;
    }
    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
    private static string Text(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (string)command.ExecuteScalar()!;
    }
    private static long Count(SqliteConnection connection, string table) => Scalar(connection, $"SELECT COUNT(*) FROM {table};");
    private static long Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (long)command.ExecuteScalar()!;
    }
}
