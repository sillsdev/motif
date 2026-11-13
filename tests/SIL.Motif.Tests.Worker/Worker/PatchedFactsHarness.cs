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
/// Runs the Parsimony facts pipeline over a seeded project with a stand-in parser, patches the grammar facts it
/// wrote, and hands a query session over the patched file to the test.
/// </summary>
internal sealed class PatchedFactsHarness : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pfh", Guid.NewGuid().ToString("N")[..8]);
    private readonly PristineProjectFixture _pristine;
    private readonly ProjectLocator _project;
    private readonly MotifDatabase _database;
    private readonly JobRepository _jobs;
    private readonly BaselineRepository _baselines;
    private readonly string _projectKey;
    private readonly BaselineToken _token;

    public PatchedFactsHarness(PristineProjectFixture pristine)
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

    /// <summary>Marks these facts sections complete after the patch, as a full run would have left them.</summary>
    public static readonly string[] CompleteSections =
        ["allomorphs", "environments", "patterns", "phonology", "entries"];

    /// <summary>Applies the patch inside one transaction with foreign keys on, then measures the patched file.</summary>
    public async Task RunAsync(string measureId, Action<SqliteConnection, SqliteTransaction> patch,
        Action<ParsimonyQuerySession, string> assert)
    {
        var template = PublishedBaselineTemplate<SeededText>.Get("seeded-text", _pristine,
            cache => SeededProject.SeedText(cache, _pristine.Seed));
        var publishedRoot = Path.Combine(_root, "published-baseline");
        var fwDataPath = template.CopyTo(publishedRoot);
        _baselines.Record(_projectKey, new BaselinePublication(publishedRoot, fwDataPath, _token),
            DateTimeOffset.Parse("2026-10-05T00:00:00Z"), DateTimeOffset.Parse("2026-10-05T00:00:00Z"),
            template.Projection, template.Summary);
        var parserPath = FakeParser.CopyRecordingInvocations(Path.Combine(_root, "parser"));
        FakeParser.WriteFactsFixture(parserPath, new { prohibitions = Array.Empty<object>() });
        using var invoker = new SIL.Motif.Host.PanGloss.PanGlossInvoker(parserPath);
        var input = JsonSerializer.Serialize(new ParsimonyJobInput(measureId,
            new ParsimonyScopeBinding(ParsimonyEvidenceScopeKind.ProjectApproved, null)), MotifJson.CreateOptions());
        var job = _jobs.Create(Guid.NewGuid().ToString("N"), _projectKey, ParsimonyJobHandler.JobKind, input,
            "2026-10-05T00:00:00Z");
        var handler = RetainedFacts.Handler(_database, _baselines, _projectKey, _project,
            new RunnerOptions { Root = Path.Combine(_root, "worker"), ParserPath = parserPath }, invoker);
        var claimed = new JobClaims(_database).Claim(_projectKey, "patched-facts-test",
            JobTimestamp.FormatUtc(DateTimeOffset.UtcNow), TimeSpan.FromMinutes(5));
        Assert.NotNull(claimed);
        var outcome = await handler.RunAsync(new ClaimedJob(_jobs, claimed), CancellationToken.None);
        Assert.True(outcome!.Status == JobStatus.Completed, outcome.ResultJson);
        using var result = JsonDocument.Parse(outcome.ResultJson!);
        var bundleId = result.RootElement.GetProperty("bundleId").GetString()!;
        var bundle = new ParsimonyBundleRepository(_database).Get(bundleId)!;
        Patch(bundle.GrammarFactsPath, patch);
        var inputs = new ParsimonyReportInputs(bundle.BundleId, _token, "baseline", null, bundle.ModelFingerprint,
            new ParsimonyArtifactDigest(bundle.GrammarFactsSchemaVersion, Sha256Of(bundle.GrammarFactsPath)),
            new ParsimonyArtifactDigest(bundle.EvidenceSchemaVersion, bundle.EvidenceSha256),
            null, null, [], ParsimonyEvidenceScopeKind.ProjectApproved);
        using var session = new ParsimonyQuerySession(bundle.GrammarFactsPath, bundle.EvidencePath, inputs);
        assert(session, bundleId);
    }

    private static void Patch(string factsPath, Action<SqliteConnection, SqliteTransaction> patch)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(factsPath),
            Pooling = false,
            ForeignKeys = true,
        }.ToString());
        connection.Open();
        using var transaction = connection.BeginTransaction();
        patch(connection, transaction);
        using var sections = connection.CreateCommand();
        sections.Transaction = transaction;
        sections.CommandText = "UPDATE artifact_section SET status='complete', reason_code=NULL WHERE section IN (" +
            string.Join(", ", CompleteSections.Select(name => $"'{name}'")) + ");";
        sections.ExecuteNonQuery();
        transaction.Commit();
    }

    private static string Sha256Of(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
}

/// <summary>Writes grammar-facts rows through parameterised commands, so a loop of thousands stays one transaction.</summary>
internal sealed class FactsWriter(SqliteConnection connection, SqliteTransaction transaction)
{
    private long _nextRoot = 1_000_000;
    private long _nextNode = 1_000_000;

    public void Sql(string sql, params (string Name, object? Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    public void WritingSystem(string tag = "qaa") =>
        Sql("INSERT OR IGNORE INTO writing_system(tag) VALUES ($tag);", ("$tag", tag));

    public void Entry(string guid) =>
        Sql("INSERT INTO lex_entry(guid, source_ordinal, lexeme_morph_type) VALUES ($g, 0, 'stem');", ("$g", guid));

    private int _allomorphOrdinal;

    /// <summary>One non-abstract stem allomorph with one form that names the given environments in order.</summary>
    public void Allomorph(string guid, string entryGuid, string form, params (string Role, string Environment)[] uses)
    {
        Sql("INSERT INTO allomorph(guid, entry_guid, ordinal, morph_type, form_class, is_abstract) " +
            "VALUES ($g, $e, $n, 'stem', 'stem', 0);", ("$g", guid), ("$e", entryGuid), ("$n", _allomorphOrdinal++));
        Sql("INSERT INTO allomorph_form(allomorph_guid, ordinal, writing_system, form) VALUES ($g, 0, 'qaa', $f);",
            ("$g", guid), ("$f", form));
        for (var ordinal = 0; ordinal < uses.Length; ordinal++)
            Sql("INSERT INTO allomorph_environment(allomorph_guid, role, ordinal, environment_guid) " +
                "VALUES ($g, $r, $o, $e);", ("$g", guid), ("$r", uses[ordinal].Role), ("$o", ordinal),
                ("$e", uses[ordinal].Environment));
    }

    /// <summary>An environment with the canonical keys of its sides; pass a null key for a side with no fact.</summary>
    public void Environment(string guid, string representation, string? leftKey, string? rightKey,
        string status = "valid")
    {
        Sql("INSERT INTO environment(guid, name, representation, parse_status, parse_error_code, parse_error_text) " +
            "VALUES ($g, '', $r, $s, $c, $t);", ("$g", guid), ("$r", representation), ("$s", status),
            ("$c", status == "invalid" ? "bad" : null), ("$t", status == "invalid" ? "bad" : null));
        foreach (var (side, key) in new[] { ("left", leftKey), ("right", rightKey) })
            if (key is not null)
                Sql("INSERT INTO environment_side(environment_guid, side, canonical_key, shape) " +
                    "VALUES ($g, $s, $k, 'complex');", ("$g", guid), ("$s", side), ("$k", key));
    }

    public void Phoneme(string guid, string name) =>
        Sql("INSERT INTO phoneme(guid, name) VALUES ($g, $n);", ("$g", guid), ("$n", name));

    /// <summary>One pattern root; each node names its parent by index, or -1 for a top-level node.</summary>
    public long PatternRoot(string ownerKind, string ownerGuid, string role, int ordinal, params Node[] nodes)
    {
        var root = _nextRoot++;
        Sql("INSERT INTO pattern_root(root_id, owner_kind, owner_guid, role, ordinal, source_kind) " +
            "VALUES ($r, $k, $g, $role, $o, $s);", ("$r", root), ("$k", ownerKind), ("$g", ownerGuid),
            ("$role", role), ("$o", ordinal),
            ("$s", ownerKind == "environment" ? "resolved_environment" : "authored"));
        var ids = new long[nodes.Length];
        var siblings = new Dictionary<long, int>();
        for (var index = 0; index < nodes.Length; index++)
        {
            ids[index] = _nextNode++;
            long? parent = nodes[index].Parent < 0 ? null : ids[nodes[index].Parent];
            var position = siblings[parent ?? -1] = siblings.GetValueOrDefault(parent ?? -1, -1) + 1;
            Sql("INSERT INTO pattern_node(node_id, root_id, parent_node_id, ordinal, kind, phoneme_guid, token_text) " +
                "VALUES ($n, $r, $p, $o, $k, $ph, $t);", ("$n", ids[index]), ("$r", root), ("$p", parent),
                ("$o", position), ("$k", nodes[index].Kind), ("$ph", nodes[index].PhonemeGuid),
                ("$t", nodes[index].Text));
        }
        return root;
    }

    /// <summary>A rewrite rule with its left and right context roots linked from one right-hand side.</summary>
    public void RewriteRule(string guid, string name, long? leftRoot, long? rightRoot)
    {
        Sql("INSERT INTO phonological_rule(guid, name, kind, direction, order_index) " +
            "VALUES ($g, $n, 'rewrite', 'leftToRight', 0);", ("$g", guid), ("$n", name));
        Sql("INSERT INTO rewrite_rhs(rule_guid, ordinal, left_context_root_id, right_context_root_id) " +
            "VALUES ($g, 0, $l, $r);", ("$g", guid), ("$l", leftRoot), ("$r", rightRoot));
    }
}

/// <summary>One pattern node to write: its kind, readable token, parent index, and phoneme.</summary>
internal sealed record Node(string Kind, string? Text = null, int Parent = -1, string? PhonemeGuid = null);
