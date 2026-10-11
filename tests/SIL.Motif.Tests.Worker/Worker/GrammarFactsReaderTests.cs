using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Parsimony;
using Xunit;

namespace SIL.Motif.Tests.Worker;

public sealed class GrammarFactsReaderTests : IDisposable
{
    private static readonly string[] SectionNames =
    [
        "project", "source_census", "conversion_inventory", "categories", "entries", "msas",
        "adhoc_prohibitions", "load_accounting", "effective_grammar", "templates", "allomorphs",
        "environments", "features", "phonology", "patterns", "compound_rules", "affix_processes", "adhoc_groups",
        "compiled_mappings", "parser_config", "stats",
    ];

    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-facts-reader-" + Guid.NewGuid().ToString("N"));

    public GrammarFactsReaderTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }

    [Fact]
    public void AcceptsAValidVersionEightArtifactAndReportsItsSections()
    {
        var facts = new FactsFile(_root);

        var artifact = GrammarFactsReader.Read(facts.DatabasePath, facts.Response(), facts.SnapshotPath);

        Assert.Equal(GrammarFactsReader.ApplicationId, artifact.ApplicationId);
        Assert.Equal(8, artifact.SchemaVersion);
        Assert.Equal("pangloss-grammar-facts", artifact.Format);
        Assert.Equal(facts.SourceDigest, artifact.SourceSha256);
        Assert.Equal("baseline", artifact.InputKind);
        Assert.Null(artifact.DryRunDigest);
        Assert.Equal("completed", artifact.CompileStatus);
        Assert.Equal(new FileInfo(facts.DatabasePath).Length, artifact.OutputBytes);
        Assert.Equal(SectionNames, artifact.Sections.Select(section => section.Name));
    }

    [Fact]
    public void AcceptsAProposalDryRunArtifactThatNamesItsDigest()
    {
        var dryRun = "sha256:" + new string('c', 64);
        var facts = new FactsFile(_root, inputKind: "proposal-dry-run", dryRunDigest: dryRun);

        var artifact = GrammarFactsReader.Read(facts.DatabasePath, facts.Response(), facts.SnapshotPath);

        Assert.Equal("proposal-dry-run", artifact.InputKind);
        Assert.Equal(dryRun, artifact.DryRunDigest);
    }

    [Fact]
    public void RefusesADatabaseWithAWrongSqliteApplicationId()
    {
        var facts = new FactsFile(_root, applicationPragma: 12345);

        var failure = Refusal(facts);

        Assert.Contains("unsupported SQLite application id", failure.Message);
    }

    [Fact]
    public void RefusesAResponseWithAWrongApplicationIdOrFormat()
    {
        var facts = new FactsFile(_root);

        Assert.Contains("unsupported format or application id",
            Refusal(facts, response => response["applicationId"] = 99).Message);
        Assert.Contains("unsupported format or application id",
            Refusal(facts, response => response["format"] = "other-format").Message);
    }

    [Fact]
    public void RefusesAResponseOfAnotherSchemaVersionAndNamesThePinnedPanGloss()
    {
        var facts = new FactsFile(_root);

        var failure = Refusal(facts, response => response["schemaVersion"] = 7);

        Assert.Contains("schema version 7, not 8", failure.Message);
        Assert.Contains("PanGloss " + PanGlossInterfaceVersions.SourceTag, failure.Message);
    }

    [Fact]
    public void RefusesADatabaseOfAnotherSchemaVersionAndNamesThePinnedPanGloss()
    {
        var facts = new FactsFile(_root, userVersion: 7);

        var failure = Refusal(facts);

        Assert.Contains("not schema version 8", failure.Message);
        Assert.Contains("PanGloss " + PanGlossInterfaceVersions.SourceTag, failure.Message);
    }

    [Fact]
    public void RefusesFactsBuiltFromADifferentGrammarInput()
    {
        var facts = new FactsFile(_root);
        var other = Path.Combine(_root, "other-snapshot.json");
        File.WriteAllText(other, "{\"format\":\"pangloss-project\",\"different\":true}");

        var failure = Assert.Throws<InvalidDataException>(
            () => GrammarFactsReader.Read(facts.DatabasePath, facts.Response(), other));

        Assert.Contains("different grammar input", failure.Message);
    }

    [Fact]
    public void RefusesAResponseWhoseDigestOrByteCountDoesNotMatchTheDatabase()
    {
        var facts = new FactsFile(_root);

        Assert.Contains("digest does not match",
            Refusal(facts, response => response["outputSha256"] = "sha256:" + new string('0', 64)).Message);
        Assert.Contains("different output byte count",
            Refusal(facts, response => response["outputBytes"] = new FileInfo(facts.DatabasePath).Length + 1).Message);
    }

    [Fact]
    public void RefusesAResponseWithAMalformedDigest()
    {
        var facts = new FactsFile(_root);

        Assert.Contains("malformed SHA-256 digest",
            Refusal(facts, response => response["sourceSha256"] = "sha256:ABC").Message);
    }

    [Fact]
    public void RefusesAnInputKindThatDisagreesWithTheDryRunDigest()
    {
        var facts = new FactsFile(_root);

        Assert.Contains("invalid input kind or Dry Run digest",
            Refusal(facts, response => response["dryRunDigest"] = "sha256:" + new string('c', 64)).Message);
        Assert.Contains("invalid input kind or Dry Run digest",
            Refusal(facts, response => response["inputKind"] = "assessment").Message);
    }

    [Fact]
    public void RefusesAResponseWithADuplicateOrUnknownSection()
    {
        var facts = new FactsFile(_root);

        Assert.Contains("unknown or duplicate section", Refusal(facts, response =>
        {
            var sections = Sections(response);
            sections.Add(new Dictionary<string, object?>(sections[0]));
            response["sections"] = sections;
        }).Message);
        Assert.Contains("unknown or duplicate section", Refusal(facts, response =>
        {
            var sections = Sections(response);
            sections[0]["section"] = "invented_section";
            response["sections"] = sections;
        }).Message);
    }

    [Fact]
    public void RefusesAResponseThatOmitsASection()
    {
        var facts = new FactsFile(_root);

        var failure = Refusal(facts, response =>
        {
            var sections = Sections(response);
            sections.RemoveAt(sections.Count - 1);
            response["sections"] = sections;
        });

        Assert.Contains("does not contain every supported facts section", failure.Message);
    }

    [Fact]
    public void RefusesADatabaseWithADuplicateSectionRow()
    {
        var facts = new FactsFile(_root, database: connection =>
            Execute(connection, "INSERT INTO artifact_section SELECT * FROM artifact_section WHERE section = 'project';"));

        Assert.Contains("duplicate section rows", Refusal(facts).Message);
    }

    [Fact]
    public void RefusesADatabaseWithAMissingOrDisagreeingSectionRow()
    {
        var missing = new FactsFile(Path.Combine(_root, "missing"), database: connection =>
            Execute(connection, "DELETE FROM artifact_section WHERE section = 'stats';"));
        var disagreeing = new FactsFile(Path.Combine(_root, "disagreeing"), database: connection =>
            Execute(connection, "UPDATE artifact_section SET status = 'partial' WHERE section = 'project';"));

        Assert.Contains("section metadata does not match", Refusal(missing).Message);
        Assert.Contains("section metadata does not match", Refusal(disagreeing).Message);
    }

    [Fact]
    public void RefusesDatabaseMetadataThatDisagreesWithTheResponseOrIsIncomplete()
    {
        var disagreeing = new FactsFile(Path.Combine(_root, "disagreeing"), database: connection =>
            Execute(connection, "UPDATE artifact_meta SET baseline_key = 'sha256:other';"));
        var incomplete = new FactsFile(Path.Combine(_root, "incomplete"), database: connection =>
            Execute(connection, "UPDATE artifact_meta SET complete = 0;"));
        var absent = new FactsFile(Path.Combine(_root, "absent"), database: connection =>
            Execute(connection, "DELETE FROM artifact_meta;"));
        var duplicated = new FactsFile(Path.Combine(_root, "duplicated"), database: connection =>
            Execute(connection, "INSERT INTO artifact_meta SELECT * FROM artifact_meta;"));

        Assert.Contains("does not match its result or is incomplete", Refusal(disagreeing).Message);
        Assert.Contains("does not match its result or is incomplete", Refusal(incomplete).Message);
        Assert.Contains("no metadata row", Refusal(absent).Message);
        Assert.Contains("duplicate metadata rows", Refusal(duplicated).Message);
    }

    [Fact]
    public void RefusesAMissingDatabaseAndAResponseThatIsNotJson()
    {
        var facts = new FactsFile(_root);

        var missing = Assert.Throws<InvalidDataException>(() => GrammarFactsReader.Read(
            Path.Combine(_root, "absent.sqlite"), facts.Response(), facts.SnapshotPath));
        var malformed = Assert.Throws<InvalidDataException>(() => GrammarFactsReader.Read(
            facts.DatabasePath, "not json", facts.SnapshotPath));
        var notAnObject = Assert.Throws<InvalidDataException>(() => GrammarFactsReader.Read(
            facts.DatabasePath, "[]", facts.SnapshotPath));

        Assert.Contains("did not write the requested facts database", missing.Message);
        Assert.Contains("invalid", malformed.Message);
        Assert.Contains("not a JSON object", notAnObject.Message);
    }

    [Fact]
    public void ReportsACompileRefusalOnlyFromAResponseThatSaysRefused()
    {
        Assert.True(GrammarFactsReader.ReportsCompileRefusal("{\"compileStatus\":\"refused\"}"));
        Assert.False(GrammarFactsReader.ReportsCompileRefusal("{\"compileStatus\":\"completed\"}"));
        Assert.False(GrammarFactsReader.ReportsCompileRefusal("{\"compileStatus\":7}"));
        Assert.False(GrammarFactsReader.ReportsCompileRefusal("[]"));
        Assert.False(GrammarFactsReader.ReportsCompileRefusal("not json"));
    }

    private static InvalidDataException Refusal(FactsFile facts,
        Action<Dictionary<string, object?>>? mutateResponse = null) =>
        Assert.Throws<InvalidDataException>(() =>
            GrammarFactsReader.Read(facts.DatabasePath, facts.Response(mutateResponse), facts.SnapshotPath));

    private static List<Dictionary<string, object?>> Sections(Dictionary<string, object?> response) =>
        ((List<Dictionary<string, object?>>)response["sections"]!).Select(
            section => new Dictionary<string, object?>(section)).ToList();

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static string Digest(byte[] bytes) =>
        "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    // A facts database holding only what the reader checks, written with the pragmas and tables PanGloss uses.
    private sealed class FactsFile
    {
        private readonly Dictionary<string, object?> _response;

        public FactsFile(string directory, int applicationPragma = GrammarFactsReader.ApplicationId,
            int userVersion = GrammarFactsReader.SchemaVersion, string inputKind = "baseline",
            string? dryRunDigest = null, Action<SqliteConnection>? database = null)
        {
            Directory.CreateDirectory(directory);
            SnapshotPath = Path.Combine(directory, "snapshot.json");
            DatabasePath = Path.Combine(directory, "facts.sqlite");
            var snapshot = Encoding.UTF8.GetBytes("{\"format\":\"pangloss-project\",\"version\":1}");
            File.WriteAllBytes(SnapshotPath, snapshot);
            SourceDigest = Digest(snapshot);
            var modelFingerprint = "sha256:" + new string('b', 64);
            var sections = SectionNames.Select(name => new Dictionary<string, object?>
            {
                ["section"] = name, ["status"] = "complete", ["sourceScope"] = "the supplied Snapshot",
                ["reasonCode"] = null,
            }).ToList();

            using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
                   {
                       DataSource = DatabasePath, Pooling = false,
                   }.ToString()))
            {
                connection.Open();
                Execute(connection, $"PRAGMA application_id={applicationPragma}; PRAGMA user_version={userVersion}; " +
                    "PRAGMA journal_mode=DELETE;");
                Execute(connection, """
                    CREATE TABLE artifact_meta(singleton INTEGER, application_id INTEGER, schema_version INTEGER,
                        format TEXT, source_sha256 TEXT, model_fingerprint TEXT, baseline_key TEXT, input_kind TEXT,
                        dry_run_digest TEXT, compile_status TEXT, complete INTEGER);
                    CREATE TABLE artifact_section(section TEXT, status TEXT, source_scope TEXT, reason_code TEXT);
                    """);
                using (var meta = connection.CreateCommand())
                {
                    meta.CommandText = """
                        INSERT INTO artifact_meta VALUES (1, $app, $schema, 'pangloss-grammar-facts', $source,
                            $model, $baseline, $kind, $dry, 'completed', 1);
                        """;
                    meta.Parameters.AddWithValue("$app", GrammarFactsReader.ApplicationId);
                    meta.Parameters.AddWithValue("$schema", GrammarFactsReader.SchemaVersion);
                    meta.Parameters.AddWithValue("$source", SourceDigest);
                    meta.Parameters.AddWithValue("$model", modelFingerprint);
                    meta.Parameters.AddWithValue("$baseline", "sha256:" + new string('a', 64));
                    meta.Parameters.AddWithValue("$kind", inputKind);
                    meta.Parameters.AddWithValue("$dry", (object?)dryRunDigest ?? DBNull.Value);
                    meta.ExecuteNonQuery();
                }
                foreach (var section in sections)
                {
                    using var row = connection.CreateCommand();
                    row.CommandText = "INSERT INTO artifact_section VALUES ($name, 'complete', $scope, NULL);";
                    row.Parameters.AddWithValue("$name", section["section"]);
                    row.Parameters.AddWithValue("$scope", section["sourceScope"]);
                    row.ExecuteNonQuery();
                }
                database?.Invoke(connection);
            }

            var bytes = File.ReadAllBytes(DatabasePath);
            _response = new Dictionary<string, object?>
            {
                ["applicationId"] = GrammarFactsReader.ApplicationId,
                ["schemaVersion"] = GrammarFactsReader.SchemaVersion,
                ["format"] = "pangloss-grammar-facts",
                ["sourceSha256"] = SourceDigest,
                ["modelFingerprint"] = modelFingerprint,
                ["baselineKey"] = "sha256:" + new string('a', 64),
                ["inputKind"] = inputKind,
                ["dryRunDigest"] = dryRunDigest,
                ["compileStatus"] = "completed",
                ["sections"] = sections,
                ["outputBytes"] = bytes.LongLength,
                ["outputSha256"] = Digest(bytes),
            };
        }

        public string SnapshotPath { get; }
        public string DatabasePath { get; }
        public string SourceDigest { get; }

        public string Response(Action<Dictionary<string, object?>>? mutate = null)
        {
            var copy = new Dictionary<string, object?>(_response);
            mutate?.Invoke(copy);
            return JsonSerializer.Serialize(copy);
        }
    }
}
