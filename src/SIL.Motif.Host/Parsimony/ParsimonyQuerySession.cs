using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using MotifCanonicalJson = SIL.Motif.Contract.Canonicalization.CanonicalJson;
using SIL.Motif.Contract.HumanJudgments;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Projection.HumanJudgments;

namespace SIL.Motif.Host.Parsimony;

/// <summary>Opens one immutable evidence pair read-only and runs fixed Parsimony queries.</summary>
public sealed partial class ParsimonyQuerySession : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ParsimonyReportInputs _inputs;
    private readonly CancellationToken _leaseCancellationToken;
    private readonly Action<SqliteConnection>? _beforeCommand;
    private CancellationToken? _page;

    /// <summary>Validates and attaches the two frozen files for one Report input.</summary>
    public ParsimonyQuerySession(string grammarFactsPath, string evidencePath, ParsimonyReportInputs inputs,
        CancellationToken leaseCancellationToken = default)
        : this(grammarFactsPath, evidencePath, inputs, leaseCancellationToken, beforeCommand: null)
    {
    }

    /// <summary>Attaches like the public constructor and runs a test hook before each statement of a page.</summary>
    internal ParsimonyQuerySession(string grammarFactsPath, string evidencePath, ParsimonyReportInputs inputs,
        CancellationToken leaseCancellationToken, Action<SqliteConnection>? beforeCommand)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(grammarFactsPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(evidencePath);
        _inputs = inputs ?? throw new ArgumentNullException(nameof(inputs));
        _leaseCancellationToken = leaseCancellationToken;
        _beforeCommand = beforeCommand;
        if (_leaseCancellationToken.IsCancellationRequested) throw new ParsimonyLeaseLostException();
        ValidateDigest(grammarFactsPath, inputs.GrammarFacts);
        ValidateDigest(evidencePath, inputs.Evidence);
        var tokenJson = JsonSerializer.Serialize(inputs.BaselineToken,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        EvidenceWriter.ValidateIdentity(evidencePath);
        var sourceDigest = ReadEvidenceSourceDigest(evidencePath);
        if (sourceDigest != inputs.BaselineToken.BundleDigest)
            throw new InvalidDataException("The evidence artifact names a different Baseline source.");
        EvidenceWriter.Validate(evidencePath, tokenJson, sourceDigest, inputs.ModelFingerprint,
            inputs.InputKind, inputs.Candidate);
        _connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = ":memory:",
            Mode = SqliteOpenMode.Memory,
            Cache = SqliteCacheMode.Private,
            ForeignKeys = true,
            Pooling = false,
        }.ToString());
        try
        {
            _connection.Open();
            Attach("facts", grammarFactsPath);
            Attach("evidence", evidencePath);
            VerifyReadOnly("facts");
            VerifyReadOnly("evidence");
            InstallTemporaryViews();
            ValidateFacts(inputs);
            ValidateAssessmentBindings(inputs);
            Execute("PRAGMA query_only=ON;");
        }
        catch
        {
            _connection.Dispose();
            throw;
        }
    }

    /// <summary>The bundle identity recorded for both attached artifacts.</summary>
    public string BundleId => _inputs.BundleId;
    internal ParsimonyReportInputs Inputs => _inputs;
    public SIL.Motif.Contract.Baselines.BaselineToken BaselineToken => _inputs.BaselineToken;

    /// <summary>Reads the exact human-judgment revisions captured in this evidence artifact.</summary>
    public HumanJudgmentLineageProjection ReadHumanJudgments()
    {
        ThrowIfLeaseLost();
        string projectId;
        string capability;
        string capabilityMessage;
        string snapshotDigest;
        string projectionDigest;
        using (var metadata = NewCommand())
        {
            metadata.CommandText = "SELECT source_project_id, capability, capability_message, snapshot_digest, " +
                "projection_digest FROM evidence.human_judgment_metadata WHERE singleton=1;";
            using var reader = metadata.ExecuteReader();
            if (!reader.Read()) throw new InvalidDataException("The evidence has no human-judgment binding.");
            projectId = reader.GetString(0);
            capability = reader.GetString(1);
            capabilityMessage = reader.GetString(2);
            snapshotDigest = reader.GetString(3);
            projectionDigest = reader.GetString(4);
            if (reader.Read()) throw new InvalidDataException("The evidence has duplicate human-judgment bindings.");
        }

        var revisions = new List<HumanJudgmentRevisionProjection>();
        using (var command = NewCommand())
        {
            command.CommandText = "SELECT record_guid, judgment_id, revision_id, content_digest, format, version, " +
                "body_kind, physical_value, judgment_json, state FROM evidence.human_judgment_revisions " +
                "ORDER BY judgment_id, revision_id;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var recordId = reader.GetString(0);
                var judgment = HumanJudgmentCodec.ParseJson(reader.GetString(8), projectId, recordId);
                if (judgment.JudgmentId != reader.GetString(1) || judgment.RevisionId != reader.GetString(2) ||
                    HumanJudgmentCodec.LogicalDigest(judgment) != reader.GetString(3) ||
                    judgment.Format != reader.GetString(4) || judgment.Version != reader.GetInt32(5))
                    throw new InvalidDataException("A captured human-judgment revision does not match its normalized row.");
                revisions.Add(new HumanJudgmentRevisionProjection(recordId, judgment.JudgmentId,
                    judgment.RevisionId, reader.GetString(3), reader.GetString(4), reader.GetInt32(5),
                    reader.GetString(6), reader.GetString(7), judgment, reader.GetString(9)));
            }
        }

        var heads = new List<HumanJudgmentHeadProjection>();
        using (var command = NewCommand())
        {
            command.CommandText = "SELECT judgment_id, revision_id, record_guid, state, issue " +
                "FROM evidence.human_judgment_heads ORDER BY judgment_id, revision_id;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                heads.Add(new HumanJudgmentHeadProjection(reader.GetString(0), reader.GetString(1),
                    reader.GetString(2), reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4)));
        }

        var unavailable = new List<HumanJudgmentUnavailableProjection>();
        using (var command = NewCommand())
        {
            command.CommandText = "SELECT record_guid, judgment_id, reason, physical_digest " +
                "FROM evidence.human_judgment_unavailable ORDER BY record_guid, reason;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                unavailable.Add(new HumanJudgmentUnavailableProjection(reader.GetString(0),
                    reader.IsDBNull(1) ? null : reader.GetString(1), reader.GetString(2), reader.GetString(3)));
        }

        ThrowIfLeaseLost();
        return new HumanJudgmentLineageProjection(projectId, capability, capabilityMessage, snapshotDigest,
            projectionDigest, Array.AsReadOnly(revisions.ToArray()), Array.AsReadOnly(heads.ToArray()),
            Array.AsReadOnly(unavailable.ToArray()));
    }

    /// <summary>Returns flat authored ad hoc prohibitions and their known final loader state.</summary>
    public IReadOnlyList<AdhocProhibitionFact> ReadAdhocProhibitions()
    {
        RequireSections("adhoc_prohibitions");
        var rows = new Dictionary<string, ProhibitionBuilder>(StringComparer.Ordinal);
        using (var command = NewCommand())
        {
            command.CommandText = "SELECT p.prohibition_guid, p.kind, p.disabled, p.adjacency, p.primary_guid, " +
                "p.target_kind, o.ordinal, o.target_guid, o.target_kind " +
                "FROM facts.adhoc_prohibition AS p LEFT JOIN facts.adhoc_other AS o " +
                "ON o.prohibition_guid=p.prohibition_guid ORDER BY p.prohibition_guid, o.ordinal;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var guid = reader.GetString(0);
                if (!rows.TryGetValue(guid, out var item))
                {
                    item = new ProhibitionBuilder(guid, reader.GetString(1), reader.GetInt32(2) != 0,
                        reader.GetString(3), reader.GetString(4), reader.GetString(5));
                    rows.Add(guid, item);
                }
                if (!reader.IsDBNull(6))
                    item.Others.Add(new ProhibitionTarget(reader.GetInt32(6), reader.GetString(7), reader.GetString(8)));
            }
        }
        var loaderStates = ReadLoaderStates(rows.Values.Select(row => (row.Guid, row.Kind)));
        return rows.Values.Select(row => row.Build(loaderStates.GetValueOrDefault(row.Guid)))
            .OrderBy(item => item.Guid, StringComparer.Ordinal).ToArray();
    }

    /// <summary>Reports project and scope denominators without multiplying readings by tokens or forms.</summary>
    public ParsimonyJoinQuality ReadJoinQuality(ParsimonyEvidenceScopeKind scope)
    {
        var scopeId = ScopeId(scope);
        using var project = NewCommand();
        project.CommandText = """
            SELECT
                (SELECT COUNT(*) FROM evidence.wordforms),
                (SELECT COUNT(DISTINCT wordform_guid) FROM evidence.analyses
                 WHERE opinion IN ('approved', 'disapproved')),
                (SELECT COUNT(*) FROM evidence.analyses WHERE opinion='approved'),
                (SELECT COUNT(*) FROM evidence.analyses WHERE opinion='disapproved'),
                (SELECT COUNT(*) FROM evidence.analyses WHERE opinion='unknown'),
                (SELECT COUNT(DISTINCT m.entry_guid) FROM evidence.analysis_morphs AS m
                 JOIN evidence.analyses AS a USING (analysis_guid)
                 WHERE a.opinion='approved' AND m.entry_guid IS NOT NULL),
                (SELECT COUNT(*) FROM evidence.occurrences);
            """;
        using var projectReader = project.ExecuteReader();
        if (!projectReader.Read()) throw new InvalidDataException("The evidence denominators could not be read.");
        var projectWordforms = projectReader.GetInt64(0);
        var projectJudgedWordforms = projectReader.GetInt64(1);
        var projectApprovedReadings = projectReader.GetInt64(2);
        var projectDisapprovedReadings = projectReader.GetInt64(3);
        var projectCandidates = projectReader.GetInt64(4);
        var projectLexemes = projectReader.GetInt64(5);
        var projectOccurrences = projectReader.GetInt64(6);
        projectReader.Close();

        using var scoped = NewCommand();
        scoped.CommandText = """
            SELECT s.status,
                (SELECT COUNT(DISTINCT w.form) FROM evidence.scope_words AS w WHERE w.scope_id=s.scope_id),
                (SELECT COUNT(DISTINCT w.wordform_guid) FROM evidence.scope_words AS w
                 WHERE w.scope_id=s.scope_id AND w.wordform_guid IS NOT NULL),
                (SELECT COUNT(DISTINCT a.analysis_guid) FROM evidence.analyses AS a
                 JOIN evidence.scope_words AS w ON w.wordform_guid=a.wordform_guid
                 WHERE w.scope_id=s.scope_id AND a.opinion='approved'),
                (SELECT COUNT(DISTINCT m.entry_guid) FROM evidence.analysis_morphs AS m
                 JOIN evidence.analyses AS a USING (analysis_guid)
                 JOIN evidence.scope_words AS w ON w.wordform_guid=a.wordform_guid
                 WHERE w.scope_id=s.scope_id AND a.opinion='approved' AND m.entry_guid IS NOT NULL),
                (SELECT COUNT(*) FROM evidence.occurrences AS o
                 JOIN evidence.scope_texts AS t ON t.text_guid=o.text_guid
                 WHERE t.scope_id=s.scope_id)
            FROM evidence.scope_descriptor AS s WHERE s.scope_id=$scope;
            """;
        scoped.Parameters.AddWithValue("$scope", scopeId);
        using var scopedReader = scoped.ExecuteReader();
        if (!scopedReader.Read())
            return new ParsimonyJoinQuality(scopeId, false, projectWordforms, projectJudgedWordforms,
                projectApprovedReadings, projectDisapprovedReadings, projectCandidates, projectLexemes,
                projectOccurrences, null, null, null, null, null);
        if (scopedReader.GetString(0) != "complete")
            return new ParsimonyJoinQuality(scopeId, false, projectWordforms, projectJudgedWordforms,
                projectApprovedReadings, projectDisapprovedReadings, projectCandidates, projectLexemes,
                projectOccurrences, null, null, null, null, null);
        var textOccurrences = scope == ParsimonyEvidenceScopeKind.DefaultSelection
            ? scopedReader.GetInt64(5) : (long?)null;
        return new ParsimonyJoinQuality(scopeId, true, projectWordforms, projectJudgedWordforms,
            projectApprovedReadings, projectDisapprovedReadings, projectCandidates, projectLexemes,
            projectOccurrences, scopedReader.GetInt64(1), scopedReader.GetInt64(2), scopedReader.GetInt64(3),
            scopedReader.GetInt64(4), textOccurrences);
    }

    /// <summary>Whether grouped prohibition provenance is present in the validated facts file.</summary>
    public bool GroupedAdhocFactsAvailable => ReadSectionStatus("adhoc_groups") == "complete";

    /// <summary>Returns the published status of one section in the grammar-facts artifact.</summary>
    public string? SectionStatus(string section) => ReadSectionStatus(section);

    public void Dispose()
    {
        _connection.Dispose();
    }

    /// <summary>Stops a named query when its reader lease can no longer be renewed.</summary>
    public void ThrowIfLeaseLost()
    {
        if (_leaseCancellationToken.IsCancellationRequested) throw new ParsimonyLeaseLostException();
    }

    /// <summary>The reader lease's token, which each page links its deadline to.</summary>
    internal CancellationToken LeaseCancellationToken => _leaseCancellationToken;

    /// <summary>Makes a page's cancellation interrupt the running statement and gate each statement it starts.</summary>
    internal CancellationTokenRegistration BeginPage(CancellationToken pageToken)
    {
        _page = pageToken;
        return pageToken.Register(() =>
        {
            if (_connection.Handle is { } handle) SQLitePCL.raw.sqlite3_interrupt(handle);
        });
    }

    internal void EndPage(CancellationTokenRegistration registration)
    {
        registration.Dispose();
        _page = null;
    }

    /// <summary>Starts one statement, refusing it when the page or the lease has already stopped.</summary>
    private SqliteCommand NewCommand()
    {
        if (_page is { } page)
        {
            ThrowIfLeaseLost();
            page.ThrowIfCancellationRequested();
            _beforeCommand?.Invoke(_connection);
        }
        return _connection.CreateCommand();
    }

    private void Attach(string alias, string path)
    {
        using var command = NewCommand();
        command.CommandText = $"ATTACH DATABASE $path AS {alias};";
        command.Parameters.AddWithValue("$path", ParsimonySqlitePath.ReadOnlyUri(path));
        command.ExecuteNonQuery();
    }

    private void VerifyReadOnly(string alias)
    {
        using var transaction = _connection.BeginTransaction();
        using var command = NewCommand();
        command.Transaction = transaction;
        command.CommandText = $"CREATE TABLE {alias}.__motif_readonly_probe (value INTEGER);";
        try
        {
            command.ExecuteNonQuery();
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 8)
        {
            transaction.Rollback();
            return;
        }
        transaction.Rollback();
        throw new InvalidDataException($"The {alias} artifact did not open read-only.");
    }

    private void InstallTemporaryViews() => Execute("""
        CREATE TEMP VIEW approved_morphs AS
        SELECT m.analysis_guid, a.wordform_guid, m.morph_guid, m.msa_guid, m.ordinal
        FROM evidence.analysis_morphs AS m
        JOIN evidence.analyses AS a ON a.analysis_guid=m.analysis_guid
        WHERE a.opinion='approved';
        """);

    private void ValidateFacts(ParsimonyReportInputs inputs)
    {
        if (inputs.InputKind is not ("baseline" or "candidate") ||
            (inputs.InputKind == "candidate") != (inputs.Candidate is not null))
            throw new InvalidDataException("The Parsimony input kind and candidate identity do not agree.");
        if (ReadPragma("facts.application_id") != GrammarFactsReader.ApplicationId ||
            ReadPragma("facts.user_version") != GrammarFactsReader.SchemaVersion)
            throw new InvalidDataException("The grammar-facts artifact has an unsupported schema identity.");
        using (var command = NewCommand())
        {
            command.CommandText = "SELECT model_fingerprint, complete, compile_status, baseline_token_json, baseline_key, " +
                "input_kind, dry_run_digest " +
                "FROM facts.artifact_meta WHERE singleton=1;";
            using var reader = command.ExecuteReader();
            if (!reader.Read() || reader.GetString(0) != inputs.ModelFingerprint || reader.GetInt32(1) != 1 ||
                reader.GetString(2) != "completed" ||
                reader.GetString(3) != CanonicalTokenJson(inputs.BaselineToken) ||
                reader.GetString(4) != "sha256:" + Digest(MotifCanonicalJson.Canonicalize(reader.GetString(3))) ||
                (inputs.InputKind == "baseline" &&
                    (reader.GetString(5) != "baseline" || !reader.IsDBNull(6))) ||
                (inputs.InputKind == "candidate" &&
                    (reader.GetString(5) != "proposal-dry-run" || reader.IsDBNull(6))) || reader.Read())
                throw new InvalidDataException("The grammar-facts metadata does not match this Report input.");
        }
    }

    private void ValidateAssessmentBindings(ParsimonyReportInputs inputs)
    {
        using var command = NewCommand();
        command.CommandText = "SELECT assessment_id FROM evidence.parser_runs ORDER BY assessment_id;";
        using var reader = command.ExecuteReader();
        var artifactIds = new List<string>();
        while (reader.Read()) artifactIds.Add(reader.GetString(0));
        var reportIds = inputs.AssessmentIds.Order(StringComparer.Ordinal).ToArray();
        if (!artifactIds.SequenceEqual(reportIds, StringComparer.Ordinal))
            throw new InvalidDataException("The parser overlay does not match the Report's Assessment references.");
    }

    private Dictionary<string, bool?> ReadLoaderStates(IEnumerable<(string Guid, string Kind)> prohibitions)
    {
        var identities = prohibitions.Distinct().ToArray();
        var subjectKinds = identities.ToDictionary(identity => identity.Guid,
            identity => SubjectKind(identity.Kind), StringComparer.Ordinal);
        var values = new Dictionary<(string Guid, string Kind), List<(string Stage, bool? Loaded)>>();
        if (identities.Length == 0) return new Dictionary<string, bool?>(StringComparer.Ordinal);
        using var command = NewCommand();
        command.CommandText = "SELECT f.subject_guid, f.subject_kind, f.pipeline_stage, f.loaded " +
            "FROM facts.load_fact AS f JOIN facts.adhoc_prohibition AS p " +
            "ON p.prohibition_guid=f.subject_guid AND f.subject_kind=CASE p.kind " +
            "WHEN 'allomorph' THEN 'allomorphCoOccurrence' ELSE 'morphemeCoOccurrence' END " +
            "WHERE f.pipeline_stage IN ('compile','compact') " +
            "ORDER BY f.subject_guid, f.subject_kind, f.pipeline_stage;";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var guid = reader.GetString(0);
            var kind = reader.GetString(1);
            if (!subjectKinds.TryGetValue(guid, out var expectedKind) || expectedKind != kind) continue;
            var key = (guid, kind);
            if (!values.TryGetValue(key, out var rows)) values[key] = rows = [];
            rows.Add((reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetInt32(3) == 1));
        }
        var result = new Dictionary<string, bool?>(StringComparer.Ordinal);
        foreach (var identity in identities)
        {
            var key = (identity.Guid, SubjectKind(identity.Kind));
            if (!values.TryGetValue(key, out var rows))
            {
                result[identity.Guid] = null;
                continue;
            }
            var finalStage = rows.Any(row => row.Stage == "compact") ? "compact" : "compile";
            var decisions = rows.Where(row => row.Stage == finalStage).Select(row => row.Loaded).ToArray();
            result[identity.Guid] = decisions.Length != 0 && decisions.All(value => value.HasValue) &&
                         decisions.Select(value => value!.Value).Distinct().Count() == 1
                ? decisions[0]
                : null;
        }
        return result;
    }

    private static string SubjectKind(string prohibitionKind) => prohibitionKind switch
    {
        "allomorph" => "allomorphCoOccurrence",
        "morpheme" => "morphemeCoOccurrence",
        _ => string.Empty,
    };

    private string? ReadSectionStatus(string name)
    {
        using var command = NewCommand();
        command.CommandText = "SELECT status FROM facts.artifact_section WHERE section=$section;";
        command.Parameters.AddWithValue("$section", name);
        return command.ExecuteScalar() as string;
    }

    private long ReadPragma(string pragma)
    {
        using var command = NewCommand();
        command.CommandText = $"PRAGMA {pragma};";
        return Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private void Execute(string sql)
    {
        using var command = NewCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    /// <summary>Hashes a stream in bounded reads, so a large facts file is never held in memory whole.</summary>
    internal static string Sha256Hex(Stream stream) => Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();

    private static void ValidateDigest(string path, ParsimonyArtifactDigest expected)
    {
        // The SQLite checks ran once at publication; the digest alone catches any later byte change.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (!StringComparer.Ordinal.Equals(Sha256Hex(stream), expected.Sha256))
            throw new InvalidDataException("A Parsimony input file no longer matches its stored byte digest.");
    }

    private static string ReadEvidenceSourceDigest(string path)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = ParsimonySqlitePath.DataSource(path),
            Mode = SqliteOpenMode.ReadOnly,
            ForeignKeys = true,
            Pooling = false,
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT source_sha256 FROM artifact_metadata WHERE singleton=1;";
        return command.ExecuteScalar() as string
            ?? throw new InvalidDataException("The evidence artifact has no source identity.");
    }

    private static string CanonicalTokenJson(SIL.Motif.Contract.Baselines.BaselineToken token) =>
        MotifCanonicalJson.Canonicalize(JsonSerializer.Serialize(token,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));

    private static string Digest(string value) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private sealed class ProhibitionBuilder(string guid, string kind, bool disabled, string adjacency,
        string primaryGuid, string primaryTargetKind)
    {
        public string Guid { get; } = guid;
        public string Kind { get; } = kind;
        public List<ProhibitionTarget> Others { get; } = [];

        public AdhocProhibitionFact Build(bool? loaded) => new(Guid, Kind, disabled, adjacency, primaryGuid,
            primaryTargetKind, Others.OrderBy(item => item.Ordinal)
                .Select(item => new AdhocTargetFact(item.Guid, item.Kind)).ToArray(), loaded);
    }

    private sealed record ProhibitionTarget(int Ordinal, string Guid, string Kind);
}

/// <summary>One complete flat prohibition with its final known parser loader state.</summary>
public sealed record AdhocProhibitionFact(string Guid, string Kind, bool Disabled, string Adjacency,
    string PrimaryGuid, string PrimaryTargetKind, IReadOnlyList<AdhocTargetFact> Others, bool? Loaded);

/// <summary>One ordered conjunctive target of a prohibition.</summary>
public sealed record AdhocTargetFact(string Guid, string Kind);
