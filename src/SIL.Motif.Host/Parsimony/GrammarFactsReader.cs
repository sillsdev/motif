using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace SIL.Motif.Host.Parsimony;

internal static class GrammarFactsReader
{
    internal const int ApplicationId = 1_346_848_321;
    internal const int SchemaVersion = PanGloss.PanGlossInterfaceVersions.FactsSchemaVersion;
    internal const string Format = "pangloss-grammar-facts";

    private static readonly ImmutableHashSet<string> SectionNames = new[]
    {
        "project", "source_census", "conversion_inventory", "categories", "entries", "msas",
        "adhoc_prohibitions", "load_accounting", "effective_grammar", "templates", "allomorphs",
        "environments", "features", "phonology", "patterns", "compound_rules", "affix_processes", "adhoc_groups",
        "compiled_mappings", "parser_config", "stats",
    }.ToImmutableHashSet(StringComparer.Ordinal);

    internal static bool ReportsCompileRefusal(string output)
    {
        try
        {
            using var document = JsonDocument.Parse(output);
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("compileStatus", out var status) &&
                status.ValueKind == JsonValueKind.String && status.GetString() == "refused";
        }
        catch (JsonException)
        {
            return false;
        }
    }

    internal static GrammarFactsArtifact Read(string path, string output, string snapshotPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotPath);
        if (!File.Exists(path)) throw new InvalidDataException("PanGloss did not write the requested facts database.");

        try
        {
            var bytes = File.ReadAllBytes(path);
            using var response = JsonDocument.Parse(output);
            var artifact = ParseResponse(path, response.RootElement);
            if (artifact.OutputBytes != bytes.LongLength)
                throw new InvalidDataException("The facts response reports a different output byte count.");
            if (artifact.OutputSha256 != Digest(bytes))
                throw new InvalidDataException("The facts response digest does not match the output database.");
            if (artifact.SourceSha256 != Digest(File.ReadAllBytes(snapshotPath)))
                throw new InvalidDataException("The facts database was built from a different Snapshot source.");

            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = ParsimonySqlitePath.DataSource(path),
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false,
            }.ToString());
            connection.Open();
            if (ReadPragma(connection, "application_id") != ApplicationId)
                throw new InvalidDataException("The facts database has an unsupported SQLite application id.");
            if (ReadPragma(connection, "user_version") != SchemaVersion)
                throw new InvalidDataException(
                    $"The facts database is not schema version {SchemaVersion}; rebuild the facts from the Snapshot.");
            ReadMetadata(connection, artifact);
            ReadSections(connection, artifact.Sections);
            return artifact;
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or SqliteException or InvalidOperationException or
                                          FormatException or OverflowException or ArgumentException)
        {
            throw new InvalidDataException("The PanGloss facts response or SQLite database is invalid.", exception);
        }
    }

    private static GrammarFactsArtifact ParseResponse(string path, JsonElement response)
    {
        if (response.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("The facts response is not a JSON object.");
        var applicationId = RequiredInt32(response, "applicationId");
        var schemaVersion = RequiredInt32(response, "schemaVersion");
        var format = RequiredString(response, "format");
        if (applicationId != ApplicationId || format != Format)
            throw new InvalidDataException("The facts response has an unsupported format or application id.");
        if (schemaVersion != SchemaVersion)
            throw new InvalidDataException(
                $"The facts response is schema version {schemaVersion}, not {SchemaVersion}; rebuild the facts from the Snapshot.");

        var sourceSha256 = RequiredString(response, "sourceSha256");
        var modelFingerprint = RequiredString(response, "modelFingerprint");
        var baselineKey = RequiredString(response, "baselineKey");
        var inputKind = RequiredString(response, "inputKind");
        var dryRunDigest = NullableString(response, "dryRunDigest");
        var compileStatus = RequiredString(response, "compileStatus");
        var outputSha256 = RequiredString(response, "outputSha256");
        var outputBytes = RequiredInt64(response, "outputBytes");
        if (!IsDigest(sourceSha256) || !IsDigest(modelFingerprint) || !IsDigest(outputSha256))
            throw new InvalidDataException("The facts response has a malformed SHA-256 digest.");
        if (inputKind is not ("baseline" or "proposal-dry-run") ||
            (inputKind == "baseline") != (dryRunDigest is null))
            throw new InvalidDataException("The facts response has an invalid input kind or Dry Run digest.");
        if (compileStatus is not ("completed" or "refused"))
            throw new InvalidDataException("The facts response has an unknown compile status.");
        if (outputBytes <= 0) throw new InvalidDataException("The facts response has an invalid output byte count.");

        var sections = ParseSections(RequiredProperty(response, "sections"));
        return new GrammarFactsArtifact(path, applicationId, schemaVersion, format, sourceSha256,
            modelFingerprint, baselineKey, inputKind, dryRunDigest, compileStatus, sections,
            outputBytes, outputSha256);
    }

    private static ImmutableArray<GrammarFactsSection> ParseSections(JsonElement sections)
    {
        if (sections.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("The facts response has no section list.");
        var result = ImmutableArray.CreateBuilder<GrammarFactsSection>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var section in sections.EnumerateArray())
        {
            var name = RequiredString(section, "section");
            var status = RequiredString(section, "status");
            var sourceScope = RequiredString(section, "sourceScope");
            var reasonCode = NullableString(section, "reasonCode");
            if (!SectionNames.Contains(name) || !names.Add(name))
                throw new InvalidDataException("The facts response has an unknown or duplicate section.");
            if (status is not ("complete" or "partial" or "unavailable" or "not_requested"))
                throw new InvalidDataException($"The facts response has an invalid status for section '{name}'.");
            result.Add(new GrammarFactsSection(name, status, sourceScope, reasonCode));
        }
        if (!names.SetEquals(SectionNames))
            throw new InvalidDataException("The facts response does not contain every supported facts section.");
        return result.ToImmutable();
    }

    private static void ReadMetadata(SqliteConnection connection, GrammarFactsArtifact artifact)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT application_id, schema_version, format, source_sha256, model_fingerprint,
                   baseline_key, input_kind, dry_run_digest, compile_status, complete
            FROM artifact_meta WHERE singleton = 1;
            """;
        using var reader = command.ExecuteReader();
        if (!reader.Read()) throw new InvalidDataException("The facts database has no metadata row.");
        var matches = reader.GetInt32(0) == artifact.ApplicationId &&
            reader.GetInt32(1) == artifact.SchemaVersion &&
            reader.GetString(2) == artifact.Format &&
            reader.GetString(3) == artifact.SourceSha256 &&
            reader.GetString(4) == artifact.ModelFingerprint &&
            reader.GetString(5) == artifact.BaselineKey &&
            reader.GetString(6) == artifact.InputKind &&
            (reader.IsDBNull(7) ? null : reader.GetString(7)) == artifact.DryRunDigest &&
            reader.GetString(8) == artifact.CompileStatus && reader.GetInt32(9) == 1;
        if (!matches) throw new InvalidDataException("The facts metadata does not match its result or is incomplete.");
        if (reader.Read()) throw new InvalidDataException("The facts database has duplicate metadata rows.");
    }

    private static void ReadSections(SqliteConnection connection, ImmutableArray<GrammarFactsSection> expected)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT section, status, source_scope, reason_code FROM artifact_section ORDER BY section";
        using var reader = command.ExecuteReader();
        var actual = new Dictionary<string, GrammarFactsSection>(StringComparer.Ordinal);
        while (reader.Read())
        {
            var section = new GrammarFactsSection(reader.GetString(0), reader.GetString(1), reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3));
            if (!actual.TryAdd(section.Name, section))
                throw new InvalidDataException("The facts database has duplicate section rows.");
        }
        if (actual.Count != expected.Length || expected.Any(section =>
                !actual.TryGetValue(section.Name, out var value) || value != section))
            throw new InvalidDataException("The facts section metadata does not match its result.");
    }

    private static long ReadPragma(SqliteConnection connection, string name)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA {name};";
        return (long)command.ExecuteScalar()!;
    }

    private static JsonElement RequiredProperty(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value))
            throw new InvalidDataException($"The facts response has no '{name}' property.");
        return value;
    }

    private static int RequiredInt32(JsonElement element, string name) =>
        RequiredProperty(element, name).TryGetInt32(out var value)
            ? value
            : throw new InvalidDataException($"The facts response has no valid '{name}' value.");

    private static long RequiredInt64(JsonElement element, string name) =>
        RequiredProperty(element, name).TryGetInt64(out var value)
            ? value
            : throw new InvalidDataException($"The facts response has no valid '{name}' value.");

    private static string RequiredString(JsonElement element, string name)
    {
        var value = RequiredProperty(element, name);
        if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
            throw new InvalidDataException($"The facts response has no valid '{name}' value.");
        return value.GetString()!;
    }

    private static string? NullableString(JsonElement element, string name)
    {
        var value = RequiredProperty(element, name);
        return value.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => value.GetString(),
            _ => throw new InvalidDataException($"The facts response has an invalid '{name}' value."),
        };
    }

    private static bool IsDigest(string value) => value.StartsWith("sha256:", StringComparison.Ordinal) &&
        value.Length == 71 && value.AsSpan(7).IndexOfAnyExcept("0123456789abcdef") < 0;

    private static string Digest(byte[] bytes) => "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}

internal sealed record GrammarFactsArtifact(
    string Path,
    int ApplicationId,
    int SchemaVersion,
    string Format,
    string SourceSha256,
    string ModelFingerprint,
    string BaselineKey,
    string InputKind,
    string? DryRunDigest,
    string CompileStatus,
    ImmutableArray<GrammarFactsSection> Sections,
    long OutputBytes,
    string OutputSha256);

internal sealed record GrammarFactsSection(string Name, string Status, string SourceScope, string? ReasonCode);
