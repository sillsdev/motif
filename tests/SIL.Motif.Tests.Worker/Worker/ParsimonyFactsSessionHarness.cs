using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Parsimony;
using SIL.Motif.Host.Store;
using SIL.Motif.LiveHost.Baselines;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Parsimony;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Worker;

/// <summary>
/// Publishes a Baseline, runs the Parsimony job with a fake PanGloss that writes a real schema-v9 facts file,
/// then lets a test add facts rows and run a Measure over them in a session. Shared by the grammar-check tests.
/// </summary>
internal sealed class ParsimonyFactsSessionHarness : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pfs", Guid.NewGuid().ToString("N")[..8]);
    private readonly PristineProjectFixture _pristine;
    private readonly ProjectLocator _project;
    private readonly MotifDatabase _database;
    private readonly JobRepository _jobs;
    private readonly BaselineRepository _baselines;
    private readonly string _projectKey;
    private readonly BaselineToken _token;

    public ParsimonyFactsSessionHarness(PristineProjectFixture pristine)
    {
        _pristine = pristine;
        Directory.CreateDirectory(_root);
        _project = new ProjectLocator(Path.Combine(_root, "live", NewLangProjFixture.ProjectName + ".fwdata"),
            "live-project-identity");
        Directory.CreateDirectory(Path.GetDirectoryName(_project.FullFwDataPath)!);
        File.WriteAllText(_project.FullFwDataPath, "live project bytes stay untouched");
        _database = MotifDatabase.OpenOwned(Path.Combine(_root, "motif.db"), _project,
            MotifSchema.CurrentSchema, new Version(1, 0));
        _jobs = new JobRepository(_database);
        _baselines = new BaselineRepository(_database);
        _projectKey = ProjectWorkspaceKey.Compute(_project);
        _token = new BaselineToken("live-project-identity", "sha256:" + new string('1', 64), "1",
            "2026-10-05T00:00:00Z", "sha256:" + new string('2', 64));
    }

    public void Dispose()
    {
        _database.Dispose();
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>Marks the named sections complete and runs the SQL, all in one transaction.</summary>
    public static void EditFacts(string factsPath, string[] completeSections, Action<SqliteConnection> edit)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(factsPath),
            Pooling = false,
            ForeignKeys = true,
        }.ToString());
        connection.Open();
        using var transaction = connection.BeginTransaction();
        edit(connection);
        using var sections = connection.CreateCommand();
        sections.CommandText = "UPDATE artifact_section SET status='complete', reason_code=NULL WHERE section IN (" +
            string.Join(", ", completeSections.Select(name => "'" + name + "'")) + ");";
        sections.ExecuteNonQuery();
        transaction.Commit();
    }

    /// <summary>Runs one statement for each row; the statement takes named parameters.</summary>
    public static void Insert(SqliteConnection connection, string sql, params object[] values)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        for (var index = 0; index < values.Length; index++)
            command.Parameters.AddWithValue("$p" + index, values[index]);
        command.ExecuteNonQuery();
    }

    public static void WritingSystem(SqliteConnection connection, string tag = "qaa") =>
        Insert(connection, "INSERT OR IGNORE INTO writing_system(tag) VALUES ($p0);", tag);

    public static void Entry(SqliteConnection connection, string guid, int ordinal, string morphType = "stem") =>
        Insert(connection, "INSERT INTO lex_entry(guid, source_ordinal, lexeme_morph_type) VALUES ($p0, $p1, $p2);",
            guid, ordinal, morphType);

    public static void Msa(SqliteConnection connection, string guid, string entry, string kind) =>
        Insert(connection, "INSERT INTO msa(msa_guid, entry_guid, kind) VALUES ($p0, $p1, $p2);", guid, entry, kind);

    public static void MsaFeature(SqliteConnection connection, string msa, string feature, int ordinal = 0,
        string role = "required") =>
        Insert(connection, "INSERT INTO msa_exception_feature(msa_guid, role, ordinal, target_guid) " +
            "VALUES ($p0, $p1, $p2, $p3);", msa, role, ordinal, feature);

    public static void Feature(SqliteConnection connection, string guid, string name) =>
        Insert(connection, "INSERT INTO exception_feature(guid, name, abbreviation) VALUES ($p0, $p1, $p1);", guid, name);

    public static void Rule(SqliteConnection connection, string guid, string name, string feature,
        string polarity = "required")
    {
        Insert(connection, "INSERT INTO phonological_rule(guid, name, kind, direction, order_index) " +
            "VALUES ($p0, $p1, 'rewrite', 'leftToRight', 0);", guid, name);
        Insert(connection, "INSERT INTO rewrite_rhs(rule_guid, ordinal) VALUES ($p0, 0);", guid);
        Insert(connection, "INSERT INTO rewrite_rhs_rule_feature(rule_guid, rhs_ordinal, polarity, ordinal, " +
            "target_guid, target_kind) VALUES ($p0, 0, $p1, 0, $p2, 'exceptionFeature');", guid, polarity, feature);
    }

    public static void Allomorph(SqliteConnection connection, string guid, string entry, int ordinal,
        string morphType, string? form, params string[] environments)
    {
        Insert(connection, "INSERT INTO allomorph(guid, entry_guid, ordinal, morph_type, form_class, is_abstract) " +
            "VALUES ($p0, $p1, $p2, $p3, $p4, 0);", guid, entry, ordinal, morphType,
            morphType is "stem" or "root" ? "stem" : "affix");
        if (form is not null)
            Insert(connection, "INSERT INTO allomorph_form(allomorph_guid, ordinal, writing_system, form) " +
                "VALUES ($p0, 0, 'qaa', $p1);", guid, form);
        for (var index = 0; index < environments.Length; index++)
            Insert(connection, "INSERT INTO allomorph_environment(allomorph_guid, role, ordinal, environment_guid) " +
                "VALUES ($p0, 'phone', $p1, $p2);", guid, index, environments[index]);
    }

    public static void Environment(SqliteConnection connection, string guid, string representation) =>
        Insert(connection, "INSERT INTO environment(guid, name, representation, parse_status) " +
            "VALUES ($p0, '', $p1, 'valid');", guid, representation);

    /// <summary>Opens a session over the facts file once <paramref name="addFacts"/> has edited it.</summary>
    public async Task<T> RunAsync<T>(Action<string> addFacts, Func<ParsimonyQuerySession, string, T> use)
    {
        var publishedRoot = Path.Combine(_root, "published-baseline");
        var template = PublishedBaselineTemplate<SeededText>.Get("seeded-text", _pristine,
            cache => SeededProject.SeedText(cache, _pristine.Seed));
        var fwDataPath = template.CopyTo(publishedRoot);
        _ = fwDataPath;
        _baselines.Record(_projectKey, new BaselinePublication(publishedRoot, fwDataPath, _token),
            DateTimeOffset.Parse("2026-10-05T00:00:00Z"), DateTimeOffset.Parse("2026-10-05T00:00:00Z"),
            template.Projection, template.Summary);
        var parserPath = FakeParser.CopyRecordingInvocations(Path.Combine(_root, "parser"));
        FakeParser.WriteFactsFixture(parserPath, new { prohibitions = Array.Empty<object>() });
        using var invoker = new SIL.Motif.Host.PanGloss.PanGlossInvoker(parserPath);
        var input = JsonSerializer.Serialize(new ParsimonyJobInput("P-allo-duplicate-form",
            new ParsimonyScopeBinding(ParsimonyEvidenceScopeKind.ProjectApproved, null)), MotifJson.CreateOptions());
        var job = _jobs.Create(Guid.NewGuid().ToString("N"), _projectKey, ParsimonyJobHandler.JobKind, input,
            "2026-10-05T00:00:00Z");
        var claimed = new JobClaims(_database).Claim(_projectKey, "parsimony-facts-session",
            JobTimestamp.FormatUtc(DateTimeOffset.UtcNow), TimeSpan.FromMinutes(5));
        Assert.NotNull(claimed);
        var handler = RetainedFacts.Handler(_database, _baselines, _projectKey, _project,
            new RunnerOptions { Root = Path.Combine(_root, "worker"), ParserPath = parserPath }, invoker);
        var outcome = await handler.RunAsync(new ClaimedJob(_jobs, claimed), CancellationToken.None);
        Assert.Equal(JobStatus.Completed, outcome!.Status);

        using var result = JsonDocument.Parse(outcome.ResultJson!);
        var bundleId = result.RootElement.GetProperty("bundleId").GetString()!;
        var bundle = new ParsimonyBundleRepository(_database).Get(bundleId)!;
        addFacts(bundle.GrammarFactsPath);
        var inputs = new ParsimonyReportInputs(bundle.BundleId, _token, "baseline", null, bundle.ModelFingerprint,
            new ParsimonyArtifactDigest(bundle.GrammarFactsSchemaVersion,
                Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(bundle.GrammarFactsPath))).ToLowerInvariant()),
            new ParsimonyArtifactDigest(bundle.EvidenceSchemaVersion, bundle.EvidenceSha256),
            null, null, [], ParsimonyEvidenceScopeKind.ProjectApproved);
        using var session = new ParsimonyQuerySession(bundle.GrammarFactsPath, bundle.EvidencePath, inputs);
        return use(session, bundleId);
    }
}
