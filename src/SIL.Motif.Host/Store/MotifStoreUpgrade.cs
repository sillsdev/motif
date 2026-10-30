using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Host.Store;

/// <summary>Copies authored project data into a new current-schema store.</summary>
public static class MotifStoreUpgrade
{
    private static readonly string[] Kept = ["Corpora", "CorpusDocuments", "Proposals", "ProposalRevisions",
        "Receipts", "AppliedIndex", "NamedSelections", "DefaultSelection", "ReviewMarks", "ReadOccurrences",
        "PhonologyExampleSets", "PhonologyExampleSetRevisions", "PhonologyExamples"];
    private static readonly HashSet<string> Derived = new(StringComparer.Ordinal)
    {
        "ActiveParse", "AssessedWords", "AssessmentInvocations", "AssessmentObjectTimings", "AssessmentPins",
        "AssessmentWordPayloads", "Assessments", "BaselineSummaries", "BaselineTextReadIndex", "BaselineTextWordforms",
        "BaselineTextWords", "Baselines", "CurrentParsimonyBundles", "GrammarChecks", "Jobs", "ParsedAnalyses",
        "ParserRefusals", "ParsimonyBuildClaims", "ParsimonyBundleReferences", "ParsimonyBundles", "ParsimonyReaderLeases",
        "PendingChangeFits", "PhonologyAssessmentBindings", "PhonologyAssessmentDependencies", "PhonologyAssessmentRows",
        "ProposalDifferenceBuckets", "ProposalDifferenceCases", "ProposalDifferenceOccurrences", "ProposalDifferences",
        "Reports", "RetainedInvocationMembers", "RetainedInvocations"
    };

    /// <summary>A revision that current code could not load; its bytes remain in the new store.</summary>
    public sealed record UnreadableRevision(string ProposalId, string IntentDigest, string Error);

    /// <summary>Counts and diagnostic information for one store upgrade.</summary>
    public sealed record Report(int SourceSchema, int TargetSchema, string? BackupPath,
        Dictionary<string, long> Carried, List<string> Dropped,
        List<UnreadableRevision> UnreadableRevisions);

    /// <summary>Copies an immutable source into a new output and writes a JSON report beside it.</summary>
    public static Report Copy(string source, string output) => Build(source, output, null, true, null);

    internal static Report Build(string source, string output, ProjectLocator? expected, bool immutable, string? backup,
        Version? worker = null, Action<SqliteConnection, bool>? onOpen = null)
    {
        source = Path.GetFullPath(source);
        output = Path.GetFullPath(output);
        if (!File.Exists(source)) throw new FileNotFoundException(StoreUpgradeMessages.Get("SourceMissing"), source);
        if (File.Exists(output) || File.Exists(output + ".upgrade.json"))
            throw new IOException(StoreUpgradeMessages.Get("OutputExists"));
        var sourceUri = new Uri(source).AbsoluteUri + (immutable ? "?mode=ro&immutable=1" : "?mode=ro");
        using var input = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = sourceUri, Mode = SqliteOpenMode.ReadOnly, Pooling = false
        }.ToString());
        input.Open();
        onOpen?.Invoke(input, true);
        var application = Scalar(input, "PRAGMA application_id;");
        var version = checked((int)Scalar(input, "PRAGMA user_version;"));
        if (application != MotifSchema.ApplicationId || version < 1 || version >= MotifSchema.CurrentSchema)
            throw new InvalidDataException(StoreUpgradeMessages.Get("SourceVersion"));
        if (Scalar(input, "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='MotifMetadata';") != 1)
            throw new InvalidDataException(StoreUpgradeMessages.Get("MissingMetadata"));
        var metadata = MotifSchema.ReadMetadata(input);
        if ((worker ?? MotifProductVersion.Current) < metadata.MinimumWorkerVersion)
            throw new NotSupportedException(StoreUpgradeMessages.Get("NewerWorker"));
        if (expected is not null &&
            (!StringComparer.OrdinalIgnoreCase.Equals(expected.FullFwDataPath, metadata.Project.FullFwDataPath) ||
             !StringComparer.Ordinal.Equals(expected.FieldWorksProjectIdentity, metadata.Project.FieldWorksProjectIdentity)))
            throw new InvalidDataException(StoreUpgradeMessages.Get("ProjectMismatch"));
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var created = false;
        var temporaryReport = output + ".upgrade.json.tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (File.Open(output, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
            created = true;
            using var destination = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = new Uri(output).AbsoluteUri, Mode = SqliteOpenMode.ReadWrite, Pooling = false
            }.ToString());
            destination.Open();
            onOpen?.Invoke(destination, false);
            Execute(destination, "PRAGMA auto_vacuum = INCREMENTAL;");
            using (var attach = destination.CreateCommand())
            {
                attach.CommandText = "ATTACH DATABASE $source AS old;";
                attach.Parameters.AddWithValue("$source", sourceUri);
                attach.ExecuteNonQuery();
            }
            Execute(destination, $"PRAGMA application_id = {MotifSchema.ApplicationId}; PRAGMA user_version = {MotifSchema.CurrentSchema};");
            using var transaction = destination.BeginTransaction();
            MotifSchema.Create(destination, transaction, metadata.Project);
            Execute(destination, "PRAGMA defer_foreign_keys = ON;", transaction);
            var tables = Names(destination, "SELECT name FROM old.sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%';", transaction);
            var unknown = tables.Except(Kept, StringComparer.Ordinal).Where(name => name != "MotifMetadata" && !Derived.Contains(name)).ToArray();
            if (unknown.Length != 0) throw new InvalidDataException(StoreUpgradeMessages.Format("UnknownTable", string.Join(", ", unknown)));
            var carried = Kept.ToDictionary(table => table, _ => 0L, StringComparer.Ordinal);
            carried["MotifMetadata"] = 1;
            foreach (var table in Kept)
            {
                if (!tables.Contains(table)) continue;
                var oldColumns = Columns(destination, "old", table, transaction);
                var newColumns = Columns(destination, "main", table, transaction);
                var matching = newColumns.Where(oldColumns.Contains).ToArray();
                if (matching.Length == 0) throw new InvalidDataException(StoreUpgradeMessages.Format("NoColumns", table));
                var names = matching.Select(Quote).ToList();
                var values = matching.Select(name => table == "Proposals" && name == "AnchorJson" ? "NULL" : Quote(name)).ToList();
                AddRequiredDefaults(destination, transaction, table, oldColumns, names, values);
                Execute(destination, $"INSERT INTO main.{Quote(table)} ({string.Join(",", names)}) SELECT {string.Join(",", values)} FROM old.{Quote(table)};", transaction);
                carried[table] = Scalar(destination, $"SELECT COUNT(*) FROM main.{Quote(table)};", transaction);
                if (carried[table] != Scalar(destination, $"SELECT COUNT(*) FROM old.{Quote(table)};", transaction))
                    throw new InvalidDataException(StoreUpgradeMessages.Format("CountChanged", table));
            }
            var metadataColumns = Columns(destination, "old", "MotifMetadata", transaction);
            foreach (var column in new[] { "CreatedUtc", "SetupSkippedUtc" })
                if (metadataColumns.Contains(column))
                    Execute(destination, $"UPDATE MotifMetadata SET {Quote(column)} = (SELECT {Quote(column)} FROM old.MotifMetadata WHERE Id=1);", transaction);
            var dropped = tables.Where(Derived.Contains).Order(StringComparer.Ordinal).ToList();
            var unreadable = CheckRevisions(destination, transaction);
            using (var check = destination.CreateCommand())
            {
                check.Transaction = transaction;
                check.CommandText = "PRAGMA foreign_key_check;";
                using var rows = check.ExecuteReader();
                if (rows.Read()) throw new InvalidDataException(StoreUpgradeMessages.Get("ReferencesInvalid"));
            }
            MotifSchema.ValidateSchema(destination, transaction);
            using (var integrity = destination.CreateCommand())
            {
                integrity.Transaction = transaction;
                integrity.CommandText = "PRAGMA main.quick_check;";
                if (!string.Equals(integrity.ExecuteScalar() as string, "ok", StringComparison.Ordinal))
                    throw new InvalidDataException(StoreUpgradeMessages.Get("IntegrityFailed"));
            }
            transaction.Commit();
            var report = new Report(version, MotifSchema.CurrentSchema, backup, carried, dropped, unreadable);
            File.WriteAllText(temporaryReport, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporaryReport, output + ".upgrade.json");
            return report;
        }
        catch
        {
            if (created && File.Exists(output)) File.Delete(output);
            throw;
        }
        finally
        {
            if (File.Exists(temporaryReport)) File.Delete(temporaryReport);
        }
    }

    private static void AddRequiredDefaults(SqliteConnection db, SqliteTransaction transaction, string table,
        HashSet<string> oldColumns, List<string> names, List<string> values)
    {
        using var command = db.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"PRAGMA main.table_info({Quote(table)});";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var name = reader.GetString(1);
            if (oldColumns.Contains(name)) continue;
            if (reader.GetInt32(5) != 0) throw new InvalidDataException(StoreUpgradeMessages.Format("MissingIdentity", table + "." + name));
            if (reader.GetInt32(3) == 0 || !reader.IsDBNull(4)) continue;
            var type = reader.GetString(2).ToUpperInvariant();
            var value = (table, name) switch
            {
                ("NamedSelections", "Revision") => "1",
                ("NamedSelections", "TimeLimitMode") => "'Estimated'",
                ("NamedSelections", "TextIdsJson" or "AddedWordsJson") => "'[]'",
                _ when type.Contains("INT", StringComparison.Ordinal) => "0",
                _ when type.Contains("REAL", StringComparison.Ordinal) || type.Contains("FLOAT", StringComparison.Ordinal) => "0.0",
                _ when type.Contains("BLOB", StringComparison.Ordinal) => "X''",
                _ => "''"
            };
            names.Add(Quote(name));
            values.Add(value);
        }
    }

    private static List<UnreadableRevision> CheckRevisions(SqliteConnection db, SqliteTransaction transaction)
    {
        OperationRegistryBootstrap.Initialize();
        var errors = new List<UnreadableRevision>();
        using var command = db.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT ProposalId, IntentDigest, ProposalJson FROM ProposalRevisions;";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            try
            {
                var value = reader.GetValue(2);
                var json = value is byte[] bytes ? new UTF8Encoding(false, true).GetString(bytes) : (string)value;
                ProposalJsonParser.Parse(json.TrimStart('\uFEFF'));
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                errors.Add(new UnreadableRevision(reader.GetString(0), reader.GetString(1), ex.Message));
            }
        }
        return errors;
    }

    private static HashSet<string> Columns(SqliteConnection db, string schema, string table, SqliteTransaction tx) =>
        Names(db, $"SELECT name FROM pragma_table_info('{table.Replace("'", "''")}','{schema}');", tx);
    private static HashSet<string> Names(SqliteConnection db, string sql, SqliteTransaction tx)
    {
        using var command = db.CreateCommand();
        command.Transaction = tx;
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        var result = new HashSet<string>(StringComparer.Ordinal);
        while (reader.Read()) result.Add(reader.GetString(0));
        return result;
    }
    private static string Quote(string name) => "\"" + name.Replace("\"", "\"\"") + "\"";
    private static long Scalar(SqliteConnection db, string sql, SqliteTransaction? tx = null)
    {
        using var command = db.CreateCommand();
        command.Transaction = tx;
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar());
    }
    private static void Execute(SqliteConnection db, string sql, SqliteTransaction? tx = null)
    {
        using var command = db.CreateCommand();
        command.Transaction = tx;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
