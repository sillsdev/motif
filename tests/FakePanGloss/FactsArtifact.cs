using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace SIL.Motif.FakePanGloss;

internal static class FactsArtifact
{
    internal const string Format = "pangloss-grammar-facts";
    internal const int SchemaVersion = 8;
    internal const int ApplicationId = 1_346_848_321;
    internal const string ContextFormat = "pangloss-facts-context";
    internal const int ContextVersion = 1;

    private static readonly string[] Sections =
    [
        "project", "source_census", "conversion_inventory", "categories", "entries", "msas",
        "adhoc_prohibitions", "load_accounting", "effective_grammar", "templates", "allomorphs",
        "environments", "features", "phonology", "patterns", "compound_rules", "affix_processes", "adhoc_groups",
        "compiled_mappings", "parser_config", "stats",
    ];

    private static readonly string[] InventoryStages =
        ["authored", "considered", "selected", "represented", "rejected", "synthesized"];

    private static readonly string[] PipelineStages = ["import", "snapshot", "compile", "compact"];

    internal static FactsResult Write(string snapshotPath, string contextPath, string outputPath, string mode,
        string fixturePath)
    {
        var snapshotBytes = File.ReadAllBytes(snapshotPath);
        var snapshot = ParseSnapshot(snapshotBytes);
        var context = ParseContext(File.ReadAllBytes(contextPath));
        var candidateFixture = Path.Combine(Path.GetDirectoryName(fixturePath)!,
            "_fake-pangloss-candidate-facts.json");
        var selectedFixture = context.InputKind == "proposal-dry-run" && File.Exists(candidateFixture)
            ? candidateFixture : fixturePath;
        var fixture = ReadFixture(selectedFixture);
        var sourceSha256 = Digest(snapshotBytes);
        var modelFingerprint = Digest(Encoding.UTF8.GetBytes(sourceSha256 + "fake-compiler-v1" +
            JsonSerializer.Serialize(fixture.Prohibitions) +
            (fixture.Groups is null ? "" : JsonSerializer.Serialize(fixture.Groups))));
        if (context.ExpectedModelFingerprint is not null && context.ExpectedModelFingerprint != modelFingerprint)
            throw new FactsArtifactException("model_mismatch", "the expected model fingerprint did not match");

        var databaseSourceSha256 = mode == "wrongFactsSource" ? Digest(Encoding.UTF8.GetBytes("different source")) : sourceSha256;
        var databaseSchemaVersion = mode switch
        {
            "wrongFactsSchema" => SchemaVersion + 1,
            "v7Facts" => 7,
            _ => SchemaVersion,
        };
        var compileStatus = mode == "compileRefused" ? "refused" : "completed";
        var sections = BuildSections(snapshot.SourceInventoryStatus, compileStatus == "completed",
            fixture.Groups is not null);
        var outputDirectory = Path.GetDirectoryName(Path.GetFullPath(outputPath))!;
        Directory.CreateDirectory(outputDirectory);
        var reserved = false;
        try
        {
            using (new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
            reserved = true;
            WriteDatabase(outputPath, snapshot, context, databaseSourceSha256, modelFingerprint,
                databaseSchemaVersion, compileStatus, sections, fixture);
            var bytes = File.ReadAllBytes(outputPath);
            return new FactsResult(
                ApplicationId,
                databaseSchemaVersion,
                Format,
                databaseSourceSha256,
                BareDigest(snapshotBytes),
                modelFingerprint,
                context.BaselineKey,
                context.InputKind,
                context.DryRunDigest,
                compileStatus,
                sections,
                bytes.LongLength,
                Digest(bytes));
        }
        catch
        {
            if (reserved)
            {
                try { File.Delete(outputPath); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            throw;
        }
    }

    private static SnapshotFacts ParseSnapshot(byte[] bytes)
    {
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            root.GetProperty("format").GetString() != "pangloss-project")
            throw new FactsArtifactException("unsupported_facts_source", "facts accepts only current pg-snapshot JSON input");
        var version = root.GetProperty("version").GetInt32();
        if (version != 1)
            throw new FactsArtifactException("unsupported_facts_source", "the Snapshot version is unsupported");
        var projectName = root.TryGetProperty("project", out var project) &&
            project.TryGetProperty("name", out var name) ? name.GetString() ?? "Fake project" : "Fake project";
        var provenanceSchemaVersion = 0;
        var sourceInventoryStatus = "unknown";
        if (root.TryGetProperty("conversionProvenance", out var provenance))
        {
            provenanceSchemaVersion = provenance.GetProperty("schemaVersion").GetInt32();
            sourceInventoryStatus = provenance.GetProperty("sourceInventoryStatus").GetString() ?? "unknown";
        }
        if (sourceInventoryStatus is not ("importedComplete" or "importedWithFatalIssues" or "synthetic" or "unknown"))
            throw new FactsArtifactException("invalid_snapshot", "the Snapshot has an invalid source inventory status");
        return new SnapshotFacts(projectName, version, provenanceSchemaVersion, sourceInventoryStatus);
    }

    private static FactsContext ParseContext(byte[] bytes)
    {
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new FactsArtifactException("invalid_context", "context must be a JSON object");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in root.EnumerateObject())
        {
            if (!names.Add(property.Name))
                throw new FactsArtifactException("invalid_context", $"duplicate context property {property.Name}");
            if (property.Name is not ("format" or "version" or "baselineToken" or "inputKind" or
                                      "dryRunDigest" or "expectedModelFingerprint"))
                throw new FactsArtifactException("invalid_context", $"unknown context property {property.Name}");
        }
        if (root.GetProperty("format").GetString() != ContextFormat)
            throw new FactsArtifactException("invalid_context", "the facts context format is unsupported");
        if (root.GetProperty("version").GetInt32() != ContextVersion)
            throw new FactsArtifactException("unsupported_context_version", "the facts context version is unsupported");
        if (!root.TryGetProperty("baselineToken", out var baselineToken))
            throw new FactsArtifactException("invalid_context", "baselineToken is required");
        var baselineTokenJson = CanonicalJson(baselineToken);
        var baselineKey = Digest(Encoding.UTF8.GetBytes(baselineTokenJson));
        var inputKind = root.GetProperty("inputKind").GetString();
        if (inputKind is not ("baseline" or "proposal-dry-run"))
            throw new FactsArtifactException("invalid_context", "inputKind must be baseline or proposal-dry-run");
        var dryRunDigestElement = root.GetProperty("dryRunDigest");
        var dryRunDigest = dryRunDigestElement.ValueKind == JsonValueKind.Null
            ? null
            : ValidateDigest(dryRunDigestElement.GetString(), "dryRunDigest");
        if ((inputKind == "baseline") != (dryRunDigest is null))
            throw new FactsArtifactException("invalid_context", "dryRunDigest does not match inputKind");
        var expectedModelFingerprint = root.TryGetProperty("expectedModelFingerprint", out var expected)
            ? ValidateDigest(expected.GetString(), "expectedModelFingerprint")
            : null;
        return new FactsContext(baselineTokenJson, baselineKey, inputKind, dryRunDigest, expectedModelFingerprint);
    }

    private static string ValidateDigest(string? value, string name)
    {
        if (value is null || !value.StartsWith("sha256:", StringComparison.Ordinal) || value.Length != 71 ||
            value.AsSpan(7).IndexOfAnyExcept("0123456789abcdefABCDEF") >= 0)
            throw new FactsArtifactException("invalid_context", $"{name} must use the sha256:<64 hex digits> form");
        return "sha256:" + value[7..].ToLowerInvariant();
    }

    private static string CanonicalJson(JsonElement value)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer)) WriteCanonicalValue(writer, value);
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static void WriteCanonicalValue(Utf8JsonWriter writer, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in value.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonicalValue(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var element in value.EnumerateArray()) WriteCanonicalValue(writer, element);
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(value.GetString());
                break;
            case JsonValueKind.Number:
                if (value.TryGetInt64(out var integer)) writer.WriteNumberValue(integer);
                else writer.WriteNumberValue(value.GetDouble());
                break;
            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;
            case JsonValueKind.Null:
                writer.WriteNullValue();
                break;
            default:
                throw new FactsArtifactException("invalid_context", "baselineToken contains an invalid JSON value");
        }
    }

    private static List<FactSection> BuildSections(string sourceStatus, bool compileCompleted,
        bool groupedFactsAvailable)
    {
        var unavailable = (string name) => new FactSection(name, "unavailable",
            "not emitted by the fake facts command", "not_supported_by_fake");
        var sections = new List<FactSection>
        {
            new("project", "complete", "project metadata in the supplied Snapshot", null),
            new("source_census", "partial",
                "source graph class counts from Snapshot provenance; per-object census is not exported",
                sourceStatus is "importedComplete" or "synthetic"
                    ? "source_object_details_not_available" : "source_inventory_unknown"),
            new("conversion_inventory", "complete", "Snapshot importer inventory and compiler final inventory", null),
            new("categories", "complete", "part-of-speech hierarchy in the supplied Snapshot", null),
            new("entries", "complete", "entry identities and senses in the supplied Snapshot", null),
            new("msas", "complete", "all MSA identities and category, slot, class, and exception references in the supplied Snapshot", null),
            new("adhoc_prohibitions", "complete", "flat allomorph and morpheme prohibitions in the supplied Snapshot", null),
            new("load_accounting", "partial", "typed compiler decisions; importer and uninstrumented decisions remain unknown", "reason_coverage_partial"),
            new("effective_grammar", compileCompleted ? "partial" : "unavailable",
                "production default compiler output", compileCompleted ? "compiled_outputs_not_exported" : "compile_refused"),
            new("templates", compileCompleted ? "complete" : "partial",
                "authored affix slots and templates with final compiled slot order from compiler decisions",
                compileCompleted ? null : "compile_refused"),
            new("allomorphs", "complete",
                "allomorph identity, entry order, morph type, abstract state, and every supplied form", null),
            unavailable("environments"),
            unavailable("features"), unavailable("phonology"), unavailable("patterns"), unavailable("compound_rules"),
            unavailable("affix_processes"),
            groupedFactsAvailable
                ? new FactSection("adhoc_groups", "complete", "typed group facts in the supplied fixture", null)
                : unavailable("adhoc_groups"),
            unavailable("compiled_mappings"),
            unavailable("parser_config"),
            new("stats", "not_requested", "no stats cache supplied", null),
        };
        return sections;
    }

    private static void WriteDatabase(string path, SnapshotFacts snapshot, FactsContext context,
        string sourceSha256, string modelFingerprint, int schemaVersion, string compileStatus,
        IReadOnlyList<FactSection> sections, FactsFixture fixture)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(path),
            Pooling = false,
        }.ToString());
        connection.Open();
        Execute(connection, "PRAGMA page_size=4096; PRAGMA encoding='UTF-8'; PRAGMA journal_mode=DELETE; " +
            $"PRAGMA synchronous=FULL; PRAGMA application_id={ApplicationId}; PRAGMA user_version={schemaVersion};");
        var schema = ReadSchema();
        Execute(connection, schema);
        using var transaction = connection.BeginTransaction();
        InsertMetadata(connection, transaction, snapshot, context, sourceSha256, modelFingerprint,
            schemaVersion, compileStatus);
        InsertSections(connection, transaction, sections);
        InsertSourceCensus(connection, transaction);
        InsertSyntheticInventoryFact(connection, transaction);
        InsertStageCounts(connection, transaction);
        InsertProhibitions(connection, transaction, fixture.Prohibitions);
        if (fixture.Groups is not null) InsertAdhocGroups(connection, transaction, fixture.Groups);
        using (var project = connection.CreateCommand())
        {
            project.Transaction = transaction;
            project.CommandText = "INSERT INTO project(singleton, name) VALUES (1, $name)";
            project.Parameters.AddWithValue("$name", snapshot.ProjectName);
            project.ExecuteNonQuery();
        }
        transaction.Commit();
        if (Scalar(connection, "PRAGMA integrity_check;") != "ok")
            throw new FactsArtifactException("facts_write_failed", "SQLite integrity_check failed");
        using var foreignKeys = connection.CreateCommand();
        foreignKeys.CommandText = "PRAGMA foreign_key_check;";
        using var rows = foreignKeys.ExecuteReader();
        if (rows.Read()) throw new FactsArtifactException("facts_write_failed", "SQLite foreign_key_check failed");
    }

    private static FactsFixture ReadFixture(string path)
    {
        if (!File.Exists(path)) return new FactsFixture([], null);
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("prohibitions", out var rows) ||
            rows.ValueKind != JsonValueKind.Array)
            throw new FactsArtifactException("facts_write_failed", "the fake facts fixture has no prohibition list");
        var result = new List<ProhibitionFixture>();
        foreach (var row in rows.EnumerateArray())
        {
            var prohibition = JsonSerializer.Deserialize<ProhibitionFixture>(row.GetRawText(), new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            }) ?? throw new FactsArtifactException("facts_write_failed", "the fake facts fixture has an empty prohibition");
            if (!Guid.TryParseExact(prohibition.ProhibitionGuid, "D", out var id) ||
                prohibition.ProhibitionGuid != id.ToString("D") ||
                !Guid.TryParseExact(prohibition.PrimaryGuid, "D", out var primary) ||
                prohibition.PrimaryGuid != primary.ToString("D") ||
                prohibition.Kind is not ("allomorph" or "morpheme") ||
                prohibition.TargetKind is not ("allomorph" or "msa") ||
                prohibition.Adjacency is not ("anywhere" or "somewhereToLeft" or "somewhereToRight" or
                    "adjacentToLeft" or "adjacentToRight") || prohibition.Others is null ||
                prohibition.Others.Any(target => target is null ||
                    !Guid.TryParseExact(target.TargetGuid, "D", out var targetGuid) ||
                    target.TargetGuid != targetGuid.ToString("D") || target.TargetKind is not ("allomorph" or "msa")))
                throw new FactsArtifactException("facts_write_failed", "the fake facts fixture has an invalid prohibition");
            result.Add(prohibition);
        }
        var groups = root.TryGetProperty("groups", out var groupRows)
            ? ReadAdhocGroups(groupRows, result)
            : null;
        return new FactsFixture(result, groups);
    }

    private static IReadOnlyList<AdhocGroupFixture> ReadAdhocGroups(JsonElement rows,
        IReadOnlyList<ProhibitionFixture> prohibitions)
    {
        if (rows.ValueKind != JsonValueKind.Array)
            throw new FactsArtifactException("facts_write_failed", "the fake facts fixture has an invalid group list");
        var memberIds = prohibitions.Select(item => item.ProhibitionGuid).ToHashSet(StringComparer.Ordinal);
        var result = new List<AdhocGroupFixture>();
        foreach (var row in rows.EnumerateArray())
        {
            var group = JsonSerializer.Deserialize<AdhocGroupFixture>(row.GetRawText(), new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            }) ?? throw new FactsArtifactException("facts_write_failed", "the fake facts fixture has an empty group");
            if (!Guid.TryParseExact(group.GroupGuid, "D", out var id) || group.GroupGuid != id.ToString("D") ||
                group.Texts is null || group.Members is null || group.Members.Length == 0 ||
                group.Texts.Any(text => text is null || text.Field is not ("name" or "description") ||
                    string.IsNullOrWhiteSpace(text.WritingSystem) || text.Text is null) ||
                group.Texts.Select(text => (text.Field, text.WritingSystem)).Distinct().Count() != group.Texts.Length ||
                group.Members.Any(member => !memberIds.Contains(member)) ||
                group.Members.Distinct(StringComparer.Ordinal).Count() != group.Members.Length ||
                result.Any(existing => existing.GroupGuid == group.GroupGuid))
                throw new FactsArtifactException("facts_write_failed", "the fake facts fixture has an invalid group");
            result.Add(group);
        }
        return result;
    }

    private static void InsertAdhocGroups(SqliteConnection connection, SqliteTransaction transaction,
        IReadOnlyList<AdhocGroupFixture> groups)
    {
        foreach (var writingSystem in groups.SelectMany(group => group.Texts)
                     .Select(text => text.WritingSystem).Distinct(StringComparer.Ordinal))
            Execute(connection, transaction, "INSERT OR IGNORE INTO writing_system(tag) VALUES ($tag);",
                ("$tag", writingSystem));
        foreach (var group in groups)
        {
            Execute(connection, transaction, "INSERT INTO adhoc_group(group_guid) VALUES ($guid);",
                ("$guid", group.GroupGuid));
            foreach (var text in group.Texts)
                Execute(connection, transaction,
                    "INSERT INTO adhoc_group_text(group_guid, field, writing_system, text) " +
                    "VALUES ($guid, $field, $writingSystem, $text);",
                    ("$guid", group.GroupGuid), ("$field", text.Field),
                    ("$writingSystem", text.WritingSystem), ("$text", text.Text));
            foreach (var member in group.Members)
                Execute(connection, transaction,
                    "INSERT INTO adhoc_group_member(group_guid, member_guid) VALUES ($group, $member);",
                    ("$group", group.GroupGuid), ("$member", member));
        }
    }

    private static void Execute(SqliteConnection connection, SqliteTransaction transaction, string sql,
        params (string Name, object Value)[] values)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in values) command.Parameters.AddWithValue(name, value);
        command.ExecuteNonQuery();
    }

    private static void InsertProhibitions(SqliteConnection connection, SqliteTransaction transaction,
        IReadOnlyList<ProhibitionFixture> prohibitions)
    {
        foreach (var prohibition in prohibitions)
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "INSERT INTO adhoc_prohibition " +
                    "(prohibition_guid, kind, disabled, adjacency, primary_guid, target_kind) " +
                    "VALUES ($guid, $kind, $disabled, $adjacency, $primary, $target_kind);";
                command.Parameters.AddWithValue("$guid", prohibition.ProhibitionGuid);
                command.Parameters.AddWithValue("$kind", prohibition.Kind);
                command.Parameters.AddWithValue("$disabled", prohibition.Disabled ? 1 : 0);
                command.Parameters.AddWithValue("$adjacency", prohibition.Adjacency);
                command.Parameters.AddWithValue("$primary", prohibition.PrimaryGuid);
                command.Parameters.AddWithValue("$target_kind", prohibition.TargetKind);
                command.ExecuteNonQuery();
            }
            for (var ordinal = 0; ordinal < prohibition.Others.Length; ordinal++)
            {
                using var other = connection.CreateCommand();
                other.Transaction = transaction;
                other.CommandText = "INSERT INTO adhoc_other " +
                    "(prohibition_guid, ordinal, target_guid, target_kind) " +
                    "VALUES ($guid, $ordinal, $target, $target_kind);";
                other.Parameters.AddWithValue("$guid", prohibition.ProhibitionGuid);
                other.Parameters.AddWithValue("$ordinal", ordinal);
                other.Parameters.AddWithValue("$target", prohibition.Others[ordinal].TargetGuid);
                other.Parameters.AddWithValue("$target_kind", prohibition.Others[ordinal].TargetKind);
                other.ExecuteNonQuery();
            }
            var subjectKind = prohibition.Kind switch
            {
                "allomorph" => "allomorphCoOccurrence",
                "morpheme" => "morphemeCoOccurrence",
                _ => throw new FactsArtifactException("facts_write_failed", "the fake facts fixture has an invalid prohibition kind"),
            };
            var subjectKey = JsonSerializer.Serialize(new
            {
                identity = new { guid = prohibition.ProhibitionGuid, kind = "object" },
                kind = subjectKind,
            });
            var loaded = prohibition.Disabled ? null : prohibition.Loaded;
            InsertProhibitionLoadFact(connection, transaction, subjectKind, subjectKey, prohibition.ProhibitionGuid,
                "compile", prohibition.Disabled ? "not_considered" : loaded switch
                {
                    true => "represented",
                    false => "rejected",
                    null => "unknown",
                }, loaded, prohibition.Disabled ? "disabled" : loaded switch
                {
                    true => "represented",
                    false => "fixture_rejected",
                    null => "decision_unrecorded",
                });
            if (prohibition.Compacted)
                InsertProhibitionLoadFact(connection, transaction, subjectKind, subjectKey,
                    prohibition.ProhibitionGuid, "compact", "compacted", false,
                    "grammar.cooccurrence.target-unreachable");
        }
    }

    private static void InsertProhibitionLoadFact(SqliteConnection connection, SqliteTransaction transaction,
        string subjectKind, string subjectKey, string prohibitionGuid, string stage, string disposition,
        bool? loaded, string reason)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO load_fact " +
            "(subject_kind, subject_key, pipeline_stage, decision_ordinal, subject_guid, context_key, " +
            "disposition, loaded, reason_code) VALUES " +
            "($kind, $key, $stage, 0, $guid, '', $disposition, $loaded, $reason);";
        command.Parameters.AddWithValue("$kind", subjectKind);
        command.Parameters.AddWithValue("$key", subjectKey);
        command.Parameters.AddWithValue("$stage", stage);
        command.Parameters.AddWithValue("$guid", prohibitionGuid);
        command.Parameters.AddWithValue("$disposition", disposition);
        command.Parameters.AddWithValue("$loaded", (object?)loaded switch
        {
            bool value => value ? 1 : 0,
            _ => DBNull.Value,
        });
        command.Parameters.AddWithValue("$reason", reason);
        command.ExecuteNonQuery();
    }

    private static void InsertMetadata(SqliteConnection connection, SqliteTransaction transaction,
        SnapshotFacts snapshot, FactsContext context, string sourceSha256, string modelFingerprint,
        int schemaVersion, string compileStatus)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO artifact_meta(singleton, application_id, schema_version, format, writer_version,
                compiler_version, source_revision, build_identity, snapshot_format, snapshot_version,
                provenance_schema_version, source_inventory_status, source_sha256, grammar_hash,
                model_fingerprint, baseline_token_json, baseline_key, input_kind, dry_run_digest,
                compile_options_json, compile_options_sha256, compile_status, complete, run_manifest_sha256)
            VALUES (1, $application_id, $schema_version, $format, 'fake-writer-v1', 'fake-compiler-v1',
                'fake-source', 'fake-build', 'pangloss-project', $snapshot_version,
                $provenance_schema_version, $source_inventory_status, $source_sha256, $grammar_hash,
                $model_fingerprint, $baseline_token_json, $baseline_key, $input_kind, $dry_run_digest,
                '{}', $compile_options_sha256, $compile_status, 1, NULL);
            """;
        command.Parameters.AddWithValue("$application_id", ApplicationId);
        command.Parameters.AddWithValue("$schema_version", schemaVersion);
        command.Parameters.AddWithValue("$format", Format);
        command.Parameters.AddWithValue("$snapshot_version", snapshot.Version);
        command.Parameters.AddWithValue("$provenance_schema_version", snapshot.ProvenanceSchemaVersion);
        command.Parameters.AddWithValue("$source_inventory_status", snapshot.SourceInventoryStatus);
        command.Parameters.AddWithValue("$source_sha256", sourceSha256);
        command.Parameters.AddWithValue("$grammar_hash", sourceSha256[7..]);
        command.Parameters.AddWithValue("$model_fingerprint", modelFingerprint);
        command.Parameters.AddWithValue("$baseline_token_json", context.BaselineTokenJson);
        command.Parameters.AddWithValue("$baseline_key", context.BaselineKey);
        command.Parameters.AddWithValue("$input_kind", context.InputKind);
        command.Parameters.AddWithValue("$dry_run_digest", (object?)context.DryRunDigest ?? DBNull.Value);
        command.Parameters.AddWithValue("$compile_options_sha256", Digest(Encoding.UTF8.GetBytes("{}")));
        command.Parameters.AddWithValue("$compile_status", compileStatus);
        command.ExecuteNonQuery();
    }

    private static void InsertSections(SqliteConnection connection, SqliteTransaction transaction,
        IReadOnlyList<FactSection> sections)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO artifact_section(section, status, source_scope, reason_code)
            VALUES ($section, $status, $source_scope, $reason_code);
            """;
        var name = command.Parameters.Add("$section", SqliteType.Text);
        var status = command.Parameters.Add("$status", SqliteType.Text);
        var sourceScope = command.Parameters.Add("$source_scope", SqliteType.Text);
        var reason = command.Parameters.Add("$reason_code", SqliteType.Text);
        command.Prepare();
        foreach (var section in sections)
        {
            name.Value = section.section;
            status.Value = section.status;
            sourceScope.Value = section.sourceScope;
            reason.Value = (object?)section.reasonCode ?? DBNull.Value;
            command.ExecuteNonQuery();
        }
    }

    private static void InsertSourceCensus(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO source_census(singleton, total_occurrences, ordered_header_sha256)
            VALUES (1, 0, $ordered_header_sha256);
            """;
        command.Parameters.AddWithValue("$ordered_header_sha256", BareDigest([]));
        command.ExecuteNonQuery();
    }

    private static void InsertSyntheticInventoryFact(SqliteConnection connection, SqliteTransaction transaction)
    {
        const string subjectKind = "featureDefinition";
        const string subjectKey = "{\"identity\":{\"key\":\"__pos__\",\"kind\":\"synthetic\"},\"kind\":\"featureDefinition\"}";
        // Synthetic compiler identities use a typed key and intentionally have no FieldWorks GUID.
        using var item = connection.CreateCommand();
        item.Transaction = transaction;
        item.CommandText = """
            INSERT INTO conversion_item(pipeline_stage, inventory_stage, subject_kind, subject_key, subject_guid)
            VALUES ('compile', 'synthesized', $kind, $key, NULL);
            """;
        item.Parameters.AddWithValue("$kind", subjectKind);
        item.Parameters.AddWithValue("$key", subjectKey);
        item.ExecuteNonQuery();

        using var fact = connection.CreateCommand();
        fact.Transaction = transaction;
        fact.CommandText = """
            INSERT INTO load_fact(subject_kind, subject_key, pipeline_stage, decision_ordinal, subject_guid,
                context_key, disposition, loaded, reason_code, effective_value_json, issue_key)
            VALUES ($kind, $key, 'compile', 0, NULL, '', 'synthesized', 1, 'synthesized', NULL, NULL);
            """;
        fact.Parameters.AddWithValue("$kind", subjectKind);
        fact.Parameters.AddWithValue("$key", subjectKey);
        fact.ExecuteNonQuery();
    }

    private static void InsertStageCounts(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO conversion_stage(pipeline_stage, inventory_stage, item_count)
            VALUES ($pipeline_stage, $inventory_stage, $item_count);
            """;
        var pipeline = command.Parameters.Add("$pipeline_stage", SqliteType.Text);
        var stage = command.Parameters.Add("$inventory_stage", SqliteType.Text);
        var count = command.Parameters.Add("$item_count", SqliteType.Integer);
        command.Prepare();
        foreach (var pipelineStage in PipelineStages)
        foreach (var inventoryStage in InventoryStages)
        {
            pipeline.Value = pipelineStage;
            stage.Value = inventoryStage;
            count.Value = pipelineStage == "compile" && inventoryStage == "synthesized" ? 1 : 0;
            command.ExecuteNonQuery();
        }
    }

    private static string ReadSchema()
    {
        var resource = typeof(FactsArtifact).Assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith(".facts-schema.sql", StringComparison.Ordinal));
        using var stream = typeof(FactsArtifact).Assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static string Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar()?.ToString() ?? string.Empty;
    }

    private static string Digest(byte[] bytes) => "sha256:" + BareDigest(bytes);

    private static string BareDigest(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    internal sealed record FactsResult(
        int applicationId,
        int schemaVersion,
        string format,
        string sourceSha256,
        string grammarHash,
        string modelFingerprint,
        string baselineKey,
        string inputKind,
        string? dryRunDigest,
        string compileStatus,
        IReadOnlyList<FactSection> sections,
        long outputBytes,
        string outputSha256);

    private sealed record ProhibitionFixture(
        string ProhibitionGuid,
        string Kind,
        bool Disabled,
        string Adjacency,
        string PrimaryGuid,
        string TargetKind,
        ProhibitionTargetFixture[] Others,
        bool? Loaded = true,
        bool Compacted = false);

    private sealed record ProhibitionTargetFixture(string TargetGuid, string TargetKind);

    private sealed record FactsFixture(IReadOnlyList<ProhibitionFixture> Prohibitions,
        IReadOnlyList<AdhocGroupFixture>? Groups);

    private sealed record AdhocGroupFixture(string GroupGuid, AdhocGroupTextFixture[] Texts, string[] Members);

    private sealed record AdhocGroupTextFixture(string Field, string WritingSystem, string Text);

    internal sealed record FactSection(string section, string status, string sourceScope, string? reasonCode);

    private sealed record SnapshotFacts(string ProjectName, int Version, int ProvenanceSchemaVersion,
        string SourceInventoryStatus);

    private sealed record FactsContext(string BaselineTokenJson, string BaselineKey, string InputKind,
        string? DryRunDigest, string? ExpectedModelFingerprint);
}

internal sealed class FactsArtifactException(string code, string message) : Exception(message)
{
    internal string Code { get; } = code;
}
