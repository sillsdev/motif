using System.Globalization;
using Microsoft.Data.Sqlite;
using SIL.Motif.Contract.Projects;

namespace SIL.Motif.Host.Store;

/// <summary>Owns the single, current SQLite schema for Motif's paired project database.</summary>
/// <remarks>
/// Motif is pre-1.0: a database is either exactly this shape or it is refused outright. There is no
/// migration ladder here — an older database is not upgraded, it is rejected with an actionable error
/// telling the developer to delete the file and let Motif recreate it.
/// </remarks>
public static class MotifSchema
{
    /// <summary>SQLite application identifier written to Motif-owned databases.</summary>
    public const int ApplicationId = 0x4D4F5446;

    /// <summary>The schema generation this assembly creates and requires.</summary>
    public const int CurrentSchema = 52;

    /// <summary>The worker version an open at the given schema ceiling requires.</summary>
    internal static Version MinimumWorkerVersion(int schema) => schema is >= 1 and <= CurrentSchema
        ? new Version(0, 1)
        : throw new NotSupportedException($"Motif schema {schema} is not known to this worker.");

    /// <summary>Builds every table, index and the identity row for a brand-new database, in one step.</summary>
    internal static void Create(
        SqliteConnection connection, SqliteTransaction? transaction, ProjectLocator project,
        TimeProvider? timeProvider = null)
    {
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = MetadataDdl + CorpusDdl + ProposalWorkflowDdl + SelectionDdl + AssessmentDdl + JobDdl +
                BaselineDdl + BaselineTextWordsDdl + BaselineTextReadIndexDdl + RetainedInvocationDdl + ParsimonyBundleDdl + ParsimonyBundleLifetimeDdl +
                GrammarCheckDdl + PendingChangeFitDdl + ReviewMarkDdl +
                ReadOccurrenceDdl + ActiveParseDdl + ParserRefusalDdl + ProposalDifferenceDdl + PhonologyExampleDdl +
                PhonologyAssessmentDdl;
            command.ExecuteNonQuery();
        }

        using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO MotifMetadata
                (Id, FullFwDataPath, FieldWorksProjectIdentity, MinimumWorkerVersion, CreatedUtc)
            VALUES (1, $path, $identity, $version, $created);
            """;
        insert.Parameters.AddWithValue("$path", project.FullFwDataPath);
        insert.Parameters.AddWithValue("$identity", project.FieldWorksProjectIdentity);
        insert.Parameters.AddWithValue("$version", MinimumWorkerVersion(CurrentSchema).ToString());
        insert.Parameters.AddWithValue("$created",
            (timeProvider?.GetUtcNow() ?? DateTimeOffset.UtcNow).ToString("O", CultureInfo.InvariantCulture));
        insert.ExecuteNonQuery();
    }

    /// <summary>Validates that an existing database has exactly the current schema's shape.</summary>
    internal static void ValidateSchema(SqliteConnection connection, SqliteTransaction? transaction = null)
    {
        var expectedTables = new HashSet<string>(StringComparer.Ordinal)
        {
            "MotifMetadata", "Corpora", "CorpusDocuments", "Assessments", "AssessedWords", "AssessmentWordPayloads", "AssessmentInvocations",
            "ParsedAnalyses", "AssessmentPins", "Proposals", "ProposalRevisions",
            "Receipts", "Reports", "AppliedIndex", "Jobs", "Baselines", "RetainedInvocations",
            "RetainedInvocationMembers", "NamedSelections", "DefaultSelection", "AssessmentObjectTimings",
            "BaselineSummaries", "BaselineTextWords", "BaselineTextWordforms", "BaselineTextReadIndex", "GrammarChecks", "PendingChangeFits", "ReviewMarks",
            "ReadOccurrences", "ActiveParse", "ParserRefusals", "ParsimonyBundles", "ParsimonyBundleReferences",
            "ParsimonyReaderLeases", "ParsimonyBuildClaims", "CurrentParsimonyBundles",
            "ProposalDifferences", "ProposalDifferenceCases", "ProposalDifferenceOccurrences", "ProposalDifferenceBuckets",
            "PhonologyExampleSets", "PhonologyExampleSetRevisions", "PhonologyExamples",
            "PhonologyAssessmentBindings", "PhonologyAssessmentRows", "PhonologyAssessmentDependencies"
        };
        var expectedIndexes = new HashSet<string>(StringComparer.Ordinal)
        {
            "IX_AssessedWords_Assessment", "IX_AssessedWords_Word", "IX_ParsedAnalyses_Word",
            "IX_Jobs_Lineage_Attempt", "IX_Jobs_Status_Updated", "IX_Jobs_Lease", "IX_Jobs_QueueOrder",
            "IX_Proposals_DraftName", "IX_Proposals_OpenRole", "IX_Assessments_Proposal", "IX_Assessments_Kind",
            "IX_RetainedInvocations_Project", "IX_RetainedInvocationMembers_Assessment",
            "IX_ParsimonyBundles_ProjectCreated", "IX_ParsimonyReaderLeases_BundleExpiry",
            "IX_ParsimonyBuildClaims_Project", "IX_GrammarChecks_Project", "IX_GrammarChecks_Proposal",
            "IX_ProposalDifferences_Proposal", "IX_ProposalDifferenceOccurrences_Order", "IX_ProposalDifferenceBuckets_Bucket",
            "IX_PhonologyExampleSets_Name", "IX_PhonologyExampleSetRevisions_SetRevision",
            "IX_PhonologyExamples_RevisionExampleId"
        };
        var expectedTriggers = new HashSet<string>(StringComparer.Ordinal)
        {
            "TR_PhonologyExampleSets_NameImmutable",
            "TR_PhonologyExampleSetRevisions_ImmutableUpdate",
            "TR_PhonologyExampleSetRevisions_ImmutableDelete",
            "TR_PhonologyExamples_Complete",
            "TR_PhonologyExamples_ImmutableUpdate",
            "TR_PhonologyExamples_ImmutableDelete"
        };

        using (var objects = connection.CreateCommand())
        {
            objects.Transaction = transaction;
            objects.CommandText = "SELECT type, name FROM sqlite_master " +
                "WHERE name NOT LIKE 'sqlite_%' AND type IN ('table', 'index', 'view', 'trigger');";
            using var reader = objects.ExecuteReader();
            while (reader.Read())
            {
                var type = reader.GetString(0);
                var name = reader.GetString(1);
                if ((type == "table" && expectedTables.Contains(name)) ||
                    (type == "index" && expectedIndexes.Contains(name)) ||
                    (type == "trigger" && expectedTriggers.Contains(name)))
                    continue;
                throw new InvalidDataException($"Motif schema contains unexpected {type} {name}.");
            }
        }

        foreach (var table in expectedTables)
            ValidateTable(connection, transaction, table, ColumnsFor(table), ForeignKeysFor(table));
        foreach (var index in expectedIndexes)
            ValidateIndex(connection, transaction, index, IndexColumnsFor(index));
        foreach (var trigger in expectedTriggers)
            ValidateTrigger(connection, transaction, trigger);
    }

    /// <summary>Lets a test fail one named store's metadata read as a storage outage does.</summary>
    private static void ThrowInjectedStorageFault(SqliteConnection connection)
    {
        var target = Environment.GetEnvironmentVariable("MOTIF_TEST_STORAGE_UNAVAILABLE_FOR");
        if (target is null) return;
        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (string.Equals(Path.GetFullPath(target), Path.GetFullPath(connection.DataSource), comparison))
            throw new SqliteException("disk I/O error", 10);
    }

    internal static (ProjectLocator Project, Version MinimumWorkerVersion) ReadMetadata(
        SqliteConnection connection,
        SqliteTransaction? transaction = null)
    {
        try
        {
            ThrowInjectedStorageFault(connection);
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "SELECT COUNT(*) FROM MotifMetadata;";
            if (Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture) != 1)
                throw new InvalidDataException("Motif database metadata must contain exactly one identity row.");

            command.CommandText = "SELECT FullFwDataPath, FieldWorksProjectIdentity, MinimumWorkerVersion FROM MotifMetadata WHERE Id = 1;";
            using var reader = command.ExecuteReader();
            if (!reader.Read())
                throw new InvalidDataException("Motif database metadata is missing its identity row.");

            var path = reader.GetString(0);
            var identity = reader.GetString(1);
            if (!Version.TryParse(reader.GetString(2), out var minimum))
                throw new InvalidDataException("Motif database metadata has an invalid minimum worker version.");
            return (new ProjectLocator(path, identity), minimum);
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (SqliteException exception) when (SqliteConnections.IsCorruption(exception))
        {
            throw new InvalidDataException("Motif database metadata is corrupt.", exception);
        }
        catch (SqliteException exception) when (SqliteConnections.IsStorageUnavailableCode(exception.SqliteErrorCode))
        {
            throw new IOException("Motif database metadata could not be read: storage is unavailable.", exception);
        }
        catch (Exception exception) when (
            exception is FormatException or ArgumentException or InvalidOperationException or
            InvalidCastException)
        {
            throw new InvalidDataException("Motif database metadata is corrupt.", exception);
        }
    }

    private static void ValidateTable(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string table,
        IReadOnlyList<ColumnShape> expectedColumns,
        IReadOnlyList<ForeignKeyShape> expectedForeignKeys)
    {
        var actual = ReadColumns(connection, transaction, table);
        if (!MatchesColumns(actual, expectedColumns))
            throw new InvalidDataException($"Motif table {table} does not match its registered schema.");

        ValidateForeignKeys(connection, transaction, table, expectedForeignKeys);
        ValidateTableInvariant(connection, transaction, table);
    }

    private static List<ColumnShape> ReadColumns(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string table)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT name, type, \"notnull\", dflt_value, pk " +
            "FROM pragma_table_info($table) ORDER BY cid;";
        command.Parameters.AddWithValue("$table", table);
        using var reader = command.ExecuteReader();
        var actual = new List<ColumnShape>();
        while (reader.Read())
        {
            actual.Add(new ColumnShape(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetInt32(2) != 0,
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetInt32(4)));
        }
        return actual;
    }

    private static bool MatchesColumns(IReadOnlyList<ColumnShape> actual, IReadOnlyList<ColumnShape> expected) =>
        actual.Count == expected.Count && !actual.Where((column, index) => !column.Matches(expected[index])).Any();

    private static void ValidateTableInvariant(SqliteConnection connection, SqliteTransaction? transaction, string table)
    {
        var requiredSql = table switch
        {
            "MotifMetadata" or "DefaultSelection" or "ActiveParse" => "CHECK (Id = 1)",
            "AssessedWords" => "AUTOINCREMENT",
            "RetainedInvocationMembers" => "UNIQUE (AssessmentId)",
            "ReadOccurrences" => "CHECK (WordIndex >= 0)",
            _ => null
        };
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT sql FROM sqlite_master WHERE type = 'table' AND name = $table;";
        command.Parameters.AddWithValue("$table", table);
        var sql = command.ExecuteScalar() as string;
        if (sql is null || (requiredSql is not null && sql.IndexOf(requiredSql, StringComparison.OrdinalIgnoreCase) < 0))
            throw new InvalidDataException($"Motif table {table} is missing a required invariant.");
        if (table == "Jobs")
        {
            foreach (var invariant in new[] { "CHECK (Attempt > 0)", "CHECK (CancellationRequested IN (0, 1))",
                "CHECK (Version >= 0)", "CHECK (DryRunPublished IN (0, 1))" })
                if (sql.IndexOf(invariant, StringComparison.OrdinalIgnoreCase) < 0)
                    throw new InvalidDataException($"Motif table {table} is missing a required invariant.");
        }
        if (table == "AssessedWords" && sql.IndexOf("CHECK (IsIncomplete IN (0, 1))", StringComparison.OrdinalIgnoreCase) < 0)
            throw new InvalidDataException($"Motif table {table} is missing a required invariant.");
        if (table == "Assessments")
        {
            var expected = AssessmentDdl[AssessmentDdl.IndexOf("CHECK ((SubjectKind", StringComparison.Ordinal)..];
            expected = expected[..expected.IndexOf(";", StringComparison.Ordinal)];
            if (!sql.Contains(expected.Trim(), StringComparison.Ordinal))
                throw new InvalidDataException("The Assessment subject branch constraints are invalid.");
        }
        var requiredInvariants = table switch
        {
            "PhonologyAssessmentBindings" => new[] { "UNIQUE (AssessmentId, RevisionId)" },
            "PhonologyAssessmentRows" => new[] { "CHECK (Ordinal >= 0)", "PRIMARY KEY (AssessmentId, Ordinal)",
                "UNIQUE (AssessmentId, ExampleId)", "FOREIGN KEY (AssessmentId, RevisionId)",
                "FOREIGN KEY (RevisionId, Ordinal, ExampleId)" },
            "PhonologyAssessmentDependencies" => new[] { "CHECK (Ordinal >= 0)", "UNIQUE (AssessmentId, Path)",
                "PRIMARY KEY (AssessmentId, Ordinal)" },
            "PhonologyExampleSets" => new[]
                { "FOREIGN KEY (SetId, CurrentRevisionId)", "DEFERRABLE INITIALLY DEFERRED" },
            "PhonologyExampleSetRevisions" => new[]
                { "CHECK (RevisionNumber > 0)", "CHECK (ExampleCount BETWEEN 1 AND 500)", "UNIQUE (SetId, RevisionId)" },
            "PhonologyExamples" => new[]
                { "CHECK (Ordinal >= 0)", "PRIMARY KEY (RevisionId, Ordinal)", "UNIQUE (RevisionId, ExampleId)", "UNIQUE (RevisionId, Ordinal, ExampleId)" },
            _ => []
        };
        foreach (var invariant in requiredInvariants)
            if (sql.IndexOf(invariant, StringComparison.OrdinalIgnoreCase) < 0)
                throw new InvalidDataException($"Motif table {table} is missing a required invariant.");
    }

    private static void ValidateTrigger(SqliteConnection connection, SqliteTransaction? transaction, string trigger)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT sql FROM sqlite_master WHERE type = 'trigger' AND name = $trigger;";
        command.Parameters.AddWithValue("$trigger", trigger);
        var sql = command.ExecuteScalar() as string;
        if (sql is null || !sql.Contains("RAISE(ABORT", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Motif trigger {trigger} is missing its immutability invariant.");
    }

    private static void ValidateIndex(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string index,
        IReadOnlyList<string> expectedColumns)
    {
        using var list = connection.CreateCommand();
        list.Transaction = transaction;
        list.CommandText = "SELECT \"unique\" FROM pragma_index_list($table) WHERE name = $index;";
        list.Parameters.AddWithValue("$table", IndexTableFor(index));
        list.Parameters.AddWithValue("$index", index);
        var unique = Convert.ToInt32(list.ExecuteScalar(), CultureInfo.InvariantCulture) != 0;
        if (unique != IsUniqueIndex(index))
            throw new InvalidDataException($"Motif index {index} has an unexpected uniqueness constraint.");

        using var columns = connection.CreateCommand();
        columns.Transaction = transaction;
        columns.CommandText = "SELECT name FROM pragma_index_info($index) ORDER BY seqno;";
        columns.Parameters.AddWithValue("$index", index);
        using var reader = columns.ExecuteReader();
        var actual = new List<string>();
        while (reader.Read()) actual.Add(reader.GetString(0));
        reader.Dispose();
        if (!actual.SequenceEqual(expectedColumns, StringComparer.Ordinal))
            throw new InvalidDataException($"Motif index {index} does not match its registered schema.");

        using var details = connection.CreateCommand();
        details.Transaction = transaction;
        details.CommandText = "SELECT origin, partial FROM pragma_index_list($table) WHERE name = $index;";
        details.Parameters.AddWithValue("$table", IndexTableFor(index));
        details.Parameters.AddWithValue("$index", index);
        using var detailReader = details.ExecuteReader();
        if (!detailReader.Read() || !StringComparer.Ordinal.Equals(detailReader.GetString(0), "c") ||
            (detailReader.GetInt32(1) != 0) != IsPartialIndex(index))
            throw new InvalidDataException($"Motif index {index} has unexpected registration details.");
    }

    private static void ValidateForeignKeys(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string table,
        IReadOnlyList<ForeignKeyShape> expected)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT \"table\", \"from\", \"to\", on_update, on_delete, match " +
            "FROM pragma_foreign_key_list($table) ORDER BY id, seq;";
        command.Parameters.AddWithValue("$table", table);
        using var reader = command.ExecuteReader();
        var actual = new List<ForeignKeyShape>();
        while (reader.Read())
            actual.Add(new ForeignKeyShape(
                reader.GetString(0), reader.GetString(1), reader.GetString(2),
                reader.GetString(3), reader.GetString(4), reader.GetString(5)));
        if (!actual.SequenceEqual(expected))
            throw new InvalidDataException($"Motif table {table} has an unexpected foreign-key shape.");
    }

    private static string IndexTableFor(string index) => index switch
    {
        "IX_AssessedWords_Assessment" or "IX_AssessedWords_Word" => "AssessedWords",
        "IX_ParsedAnalyses_Word" => "ParsedAnalyses",
        "IX_Jobs_Lineage_Attempt" or "IX_Jobs_Status_Updated" or "IX_Jobs_Lease" or "IX_Jobs_QueueOrder" => "Jobs",
        "IX_Proposals_DraftName" or "IX_Proposals_OpenRole" => "Proposals",
        "IX_Assessments_Proposal" or "IX_Assessments_Kind" => "Assessments",
        "IX_RetainedInvocations_Project" => "RetainedInvocations",
        "IX_RetainedInvocationMembers_Assessment" => "RetainedInvocationMembers",
        "IX_ParsimonyBundles_ProjectCreated" => "ParsimonyBundles",
        "IX_ParsimonyReaderLeases_BundleExpiry" => "ParsimonyReaderLeases",
        "IX_ParsimonyBuildClaims_Project" => "ParsimonyBuildClaims",
        "IX_GrammarChecks_Project" or "IX_GrammarChecks_Proposal" => "GrammarChecks",
        "IX_ProposalDifferences_Proposal" => "ProposalDifferences",
        "IX_ProposalDifferenceOccurrences_Order" => "ProposalDifferenceOccurrences",
        "IX_ProposalDifferenceBuckets_Bucket" => "ProposalDifferenceBuckets",
        "IX_PhonologyExampleSets_Name" => "PhonologyExampleSets",
        "IX_PhonologyExampleSetRevisions_SetRevision" => "PhonologyExampleSetRevisions",
        "IX_PhonologyExamples_RevisionExampleId" => "PhonologyExamples",
        _ => throw new InvalidDataException($"Motif index {index} is not registered.")
    };

    private static bool IsUniqueIndex(string index) => index is "IX_Jobs_Lineage_Attempt" or
        "IX_Proposals_DraftName" or "IX_Proposals_OpenRole" or "IX_GrammarChecks_Project" or "IX_GrammarChecks_Proposal" or
        "IX_PhonologyExampleSets_Name" or "IX_PhonologyExampleSetRevisions_SetRevision" or "IX_PhonologyExamples_RevisionExampleId";

    private static bool IsPartialIndex(string index) => index is "IX_Proposals_OpenRole" or "IX_GrammarChecks_Project" or
        "IX_GrammarChecks_Proposal";

    private static IReadOnlyList<string> IndexColumnsFor(string index) => index switch
    {
        "IX_AssessedWords_Assessment" => ["AssessmentId"],
        "IX_AssessedWords_Word" => ["AssessmentId", "Word"],
        "IX_ParsedAnalyses_Word" => ["AssessedWordId"],
        "IX_Jobs_Lineage_Attempt" => ["LineageId", "Attempt"],
        "IX_Jobs_Status_Updated" => ["Status", "UpdatedUtc"],
        "IX_Jobs_Lease" => ["Status", "LeaseUntilUtc"],
        "IX_Jobs_QueueOrder" => ["QueueOrder"],
        "IX_Proposals_DraftName" => ["DraftName"],
        "IX_Proposals_OpenRole" => ["Role"],
        "IX_Assessments_Proposal" => ["ProposalId"],
        "IX_Assessments_Kind" => ["Kind"],
        "IX_RetainedInvocations_Project" => ["ProjectKey"],
        "IX_RetainedInvocationMembers_Assessment" => ["AssessmentId"],
        "IX_ParsimonyBundles_ProjectCreated" => ["ProjectKey", "CreatedUtc"],
        "IX_ParsimonyReaderLeases_BundleExpiry" => ["BundleId", "ExpiresUtc"],
        "IX_ParsimonyBuildClaims_Project" => ["ProjectKey"],
        "IX_GrammarChecks_Project" => ["BaselineToken"],
        "IX_GrammarChecks_Proposal" => ["BaselineToken", "ProposalId", "IntentDigest"],
        "IX_ProposalDifferences_Proposal" => ["ProposalId", "CreatedUtc"],
        "IX_ProposalDifferenceOccurrences_Order" => ["DifferenceId", "TextId", "LineNumber", "OccurrenceIndex"],
        "IX_ProposalDifferenceBuckets_Bucket" => ["DifferenceId", "Bucket", "CaseKey"],
        "IX_PhonologyExampleSets_Name" => ["Name"],
        "IX_PhonologyExampleSetRevisions_SetRevision" => ["SetId", "RevisionNumber"],
        "IX_PhonologyExamples_RevisionExampleId" => ["RevisionId", "ExampleId"],
        _ => throw new InvalidDataException($"Motif index {index} is not registered.")
    };

    private static IReadOnlyList<ForeignKeyShape> ForeignKeysFor(string table) => table switch
    {
        "CorpusDocuments" => [new("Corpora", "CorpusId", "CorpusId", "NO ACTION", "NO ACTION", "NONE")],
        "PhonologyAssessmentBindings" => [new("PhonologyExampleSetRevisions", "RevisionId", "RevisionId", "NO ACTION", "NO ACTION", "NONE"),
            new("Assessments", "AssessmentId", "AssessmentId", "NO ACTION", "NO ACTION", "NONE")],
        "PhonologyAssessmentRows" => [new("PhonologyExamples", "RevisionId", "RevisionId", "NO ACTION", "NO ACTION", "NONE"),
            new("PhonologyExamples", "Ordinal", "Ordinal", "NO ACTION", "NO ACTION", "NONE"),
            new("PhonologyExamples", "ExampleId", "ExampleId", "NO ACTION", "NO ACTION", "NONE"),
            new("PhonologyAssessmentBindings", "AssessmentId", "AssessmentId", "NO ACTION", "NO ACTION", "NONE"),
            new("PhonologyAssessmentBindings", "RevisionId", "RevisionId", "NO ACTION", "NO ACTION", "NONE")],
        "PhonologyAssessmentDependencies" => [new("PhonologyAssessmentBindings", "AssessmentId", "AssessmentId", "NO ACTION", "NO ACTION", "NONE")],
        "Assessments" => [new("Assessments", "ReplacesAssessmentId", "AssessmentId", "NO ACTION", "NO ACTION", "NONE"),
            new("AssessmentInvocations", "InvocationId", "InvocationId", "NO ACTION", "NO ACTION", "NONE"),
            new("Proposals", "ProposalId", "ProposalId", "NO ACTION", "NO ACTION", "NONE")],
        "RetainedInvocations" => [new("AssessmentInvocations", "ArtifactInvocationId", "InvocationId", "NO ACTION", "NO ACTION", "NONE")],
        "RetainedInvocationMembers" => [new("Assessments", "AssessmentId", "AssessmentId", "NO ACTION", "NO ACTION", "NONE"),
            new("RetainedInvocations", "InvocationId", "InvocationId", "NO ACTION", "NO ACTION", "NONE")],
        "DefaultSelection" => [new("NamedSelections", "SelectionName", "SelectionName", "NO ACTION", "NO ACTION", "NONE")],
        "AssessmentObjectTimings" => [new("Assessments", "AssessmentId", "AssessmentId", "NO ACTION", "NO ACTION", "NONE")],
        "BaselineSummaries" => [new("Baselines", "ProjectKey", "ProjectKey", "NO ACTION", "NO ACTION", "NONE")],
        "BaselineTextWords" => [new("Baselines", "ProjectKey", "ProjectKey", "NO ACTION", "NO ACTION", "NONE")],
        "BaselineTextWordforms" => [new("Baselines", "ProjectKey", "ProjectKey", "NO ACTION", "NO ACTION", "NONE")],
        "BaselineTextReadIndex" => [new("Baselines", "ProjectKey", "ProjectKey", "NO ACTION", "NO ACTION", "NONE")],
        "AssessedWords" => [new("AssessmentWordPayloads", "MissedApprovedJson", "Hash", "NO ACTION", "NO ACTION", "NONE"),
            new("AssessmentWordPayloads", "AnalysisComparisonJson", "Hash", "NO ACTION", "NO ACTION", "NONE"),
            new("AssessmentWordPayloads", "ReadingGradesJson", "Hash", "NO ACTION", "NO ACTION", "NONE"),
            new("AssessmentWordPayloads", "CorrectnessJson", "Hash", "NO ACTION", "NO ACTION", "NONE"),
            new("AssessmentWordPayloads", "MorphologyJson", "Hash", "NO ACTION", "NO ACTION", "NONE"),
            new("Assessments", "AssessmentId", "AssessmentId", "NO ACTION", "NO ACTION", "NONE")],
        "ParsedAnalyses" => [new("AssessedWords", "AssessedWordId", "AssessedWordId", "NO ACTION", "NO ACTION", "NONE")],
        "AssessmentPins" => [new("Assessments", "AssessmentId", "AssessmentId", "NO ACTION", "NO ACTION", "NONE")],
        "ParsimonyBundleReferences" => [new("ParsimonyBundles", "BundleId", "BundleId", "NO ACTION", "CASCADE", "NONE")],
        "ParsimonyReaderLeases" => [new("ParsimonyBundles", "BundleId", "BundleId", "NO ACTION", "CASCADE", "NONE")],
        "CurrentParsimonyBundles" => [new("ParsimonyBundles", "BundleId", "BundleId", "NO ACTION", "CASCADE", "NONE")],
        "ProposalRevisions" => [new("Proposals", "ProposalId", "ProposalId", "NO ACTION", "NO ACTION", "NONE")],
        "ProposalDifferences" => [new("Proposals", "ProposalId", "ProposalId", "NO ACTION", "NO ACTION", "NONE")],
        "ProposalDifferenceCases" => [new("ProposalDifferences", "DifferenceId", "DifferenceId", "NO ACTION", "CASCADE", "NONE")],
        "ProposalDifferenceOccurrences" => [new("ProposalDifferences", "DifferenceId", "DifferenceId", "NO ACTION", "CASCADE", "NONE")],
        "ProposalDifferenceBuckets" => [new("ProposalDifferences", "DifferenceId", "DifferenceId", "NO ACTION", "CASCADE", "NONE")],
        "Receipts" => [new("Proposals", "ProposalId", "ProposalId", "NO ACTION", "NO ACTION", "NONE")],
        "Reports" => [new("Assessments", "AssessmentId", "AssessmentId", "NO ACTION", "NO ACTION", "NONE"),
            new("Proposals", "ProposalId", "ProposalId", "NO ACTION", "NO ACTION", "NONE")],
        "PhonologyExampleSets" => [new("PhonologyExampleSetRevisions", "SetId", "SetId", "NO ACTION", "NO ACTION", "NONE"),
            new("PhonologyExampleSetRevisions", "CurrentRevisionId", "RevisionId", "NO ACTION", "NO ACTION", "NONE")],
        "PhonologyExampleSetRevisions" => [new("Proposals", "ProposalId", "ProposalId", "NO ACTION", "NO ACTION", "NONE"),
            new("PhonologyExampleSets", "SetId", "SetId", "NO ACTION", "NO ACTION", "NONE")],
        "PhonologyExamples" => [new("PhonologyExampleSetRevisions", "RevisionId", "RevisionId", "NO ACTION", "NO ACTION", "NONE")],
        _ => []
    };

    private static IReadOnlyList<ColumnShape> ColumnsFor(string table) => table switch
    {
        "MotifMetadata" =>
        [C("Id", "INTEGER", false, 1), C("FullFwDataPath", "TEXT", true),
            C("FieldWorksProjectIdentity", "TEXT", true), C("MinimumWorkerVersion", "TEXT", true),
            C("CreatedUtc", "TEXT", true), C("CurrentAssessmentId", "TEXT"), C("SetupSkippedUtc", "TEXT")],
        "NamedSelections" =>
        [C("SelectionName", "TEXT", false, 1), C("TextIdsJson", "TEXT", true), C("AddedWordsJson", "TEXT", true),
            C("CreatedUtc", "TEXT", true), C("UpdatedUtc", "TEXT", true),
            C("TimeLimitMode", "TEXT", true), C("ExplicitPerWordLimitMs", "INTEGER"),
            C("PerWordStepLimit", "INTEGER"), C("Revision", "INTEGER", true)],
        "DefaultSelection" => [C("Id", "INTEGER", false, 1), C("SelectionName", "TEXT", true)],
        "ParserRefusals" => [C("BaselineToken", "TEXT", false, 1), C("RefusalJson", "TEXT", true)],
        "GrammarChecks" =>
        [C("BaselineToken", "TEXT", true), C("ProposalId", "TEXT"), C("IntentDigest", "TEXT"),
            C("SelectionSha256", "TEXT", true), C("ParserStamp", "TEXT"), C("ResponseGzip", "BLOB", true),
            C("CheckedUtc", "TEXT", true)],
        "PendingChangeFits" =>
        [C("DraftRevision", "TEXT", true, 1), C("ProjectLastWriteUtcTicks", "INTEGER", true, 2),
            C("BaselineIdentity", "TEXT", true, 3), C("FitSummaryJson", "TEXT", true)],
        "ReviewMarks" =>
        [C("ProposalId", "TEXT", true, 1), C("EntryKey", "TEXT", true, 2), C("ContentDigest", "TEXT", true),
            C("EvidenceDigest", "TEXT", true), C("LookedAtUtc", "TEXT", true)],
        "ActiveParse" =>
        [C("Id", "INTEGER", false, 1), C("Token", "TEXT", true), C("ProcessId", "INTEGER", true),
            C("ProcessStartIdentity", "TEXT", true)],
        "ReadOccurrences" =>
        [C("TextId", "TEXT", true, 1), C("ParagraphId", "TEXT", true, 2),
            C("SegmentId", "TEXT", true, 3), C("WordIndex", "INTEGER", true, 4),
            C("FingerprintJson", "TEXT", true)],
        "Corpora" => [C("CorpusId", "TEXT", false, 1), C("ProvenanceJson", "TEXT", true)],
        "CorpusDocuments" =>
        [C("CorpusId", "TEXT", true, 1), C("DocumentId", "TEXT", true, 2), C("OrdinalIndex", "INTEGER", true),
            C("Title", "TEXT", true), C("Source", "TEXT", true), C("Text", "TEXT", true),
            C("ContentSha256", "TEXT", true), C("IngestedUtc", "TEXT", true), C("Licence", "TEXT"),
            C("CapabilitiesJson", "TEXT"), C("AttributesJson", "TEXT")],
        "PhonologyAssessmentBindings" => [C("AssessmentId", "TEXT", true, 1), C("RevisionId", "TEXT", true),
            C("HeaderJson", "TEXT", true)],
        "PhonologyAssessmentRows" => [C("AssessmentId", "TEXT", true, 1), C("RevisionId", "TEXT", true),
            C("Ordinal", "INTEGER", true, 2), C("ExampleId", "TEXT", true), C("EvidenceJson", "TEXT", true),
            C("SummaryJson", "TEXT", true)],
        "PhonologyAssessmentDependencies" => [C("AssessmentId", "TEXT", true, 1), C("Ordinal", "INTEGER", true, 2),
            C("Path", "TEXT", true), C("Sha256", "TEXT", true)],
        "AssessmentInvocations" => [C("InvocationId", "TEXT", false, 1), C("EvidenceJson", "TEXT", true)],
        "Assessments" =>
        [C("AssessmentId", "TEXT", false, 1), C("SelectionName", "TEXT"), C("SelectionWordsJson", "TEXT"),
            C("SelectionSha256", "TEXT"), C("SelectionProvenanceJson", "TEXT"), C("OutcomeDigest", "TEXT"),
            C("SemanticDigest", "TEXT"), C("GrammarSourceSha256", "TEXT", true),
            C("ModelFingerprint", "TEXT"), C("Pipeline", "TEXT"), C("DiagnosticCount", "INTEGER"),
            C("SavedUtc", "TEXT", true), C("ProposalId", "TEXT"), C("ProposalIntentDigest", "TEXT"),
            C("Assessor", "TEXT", true), C("Kind", "TEXT", true), C("ScopeJson", "TEXT", true),
            C("ScopeDigest", "TEXT", true), C("TokeniserName", "TEXT"), C("TokeniserVersion", "TEXT"),
            C("BaselineToken", "TEXT", true), C("CachePath", "TEXT"), C("CacheDigest", "TEXT"), C("InvocationId", "TEXT"), C("ReplacesAssessmentId", "TEXT"), C("SubjectKind", "TEXT", true), C("SubjectJson", "TEXT")],
        "AssessmentWordPayloads" => [C("Hash", "BLOB", true, 1), C("Payload", "BLOB", true)],
        "AssessedWords" =>
        [C("AssessedWordId", "INTEGER", false, 1), C("AssessmentId", "TEXT", true), C("OrdinalIndex", "INTEGER", true),
            C("Word", "TEXT", true), C("Outcome", "TEXT", true), C("ElapsedMs", "INTEGER"), C("ElapsedNs", "INTEGER"),
            C("RawSignature", "TEXT"),
            C("MorphologyJson", "BLOB"), C("CorrectnessJson", "BLOB"), C("ProjectStanding", "TEXT"),
            C("OccurrenceCount", "INTEGER"), C("ReadingGradesJson", "BLOB"), C("AnalysisComparisonJson", "BLOB"), C("MissedApprovedCount", "INTEGER"),
            C("MissedApprovedJson", "BLOB"), C("IsIncomplete", "INTEGER", true, defaultValue: "0"),
            C("Attempts", "INTEGER"), C("Passes", "INTEGER"), C("WorkSteps", "INTEGER")],
        "AssessmentObjectTimings" => [C("AssessmentId", "TEXT", true, 1), C("Detail", "BLOB", true),
            C("Evidence", "BLOB")],
        "BaselineSummaries" => [C("ProjectKey", "TEXT", false, 1), C("SummaryJson", "TEXT", true)],
        "BaselineTextWords" => [C("ProjectKey", "TEXT", true, 1), C("TextId", "TEXT", true, 2),
            C("BundleDigest", "TEXT", true), C("TextJson", "TEXT", true)],
        "BaselineTextWordforms" => [C("ProjectKey", "TEXT", true, 1), C("WordformId", "TEXT", true, 2),
            C("BundleDigest", "TEXT", true), C("WordformJson", "TEXT", true)],
        "BaselineTextReadIndex" => [C("ProjectKey", "TEXT", true, 1), C("TextId", "TEXT", true, 2),
            C("BundleDigest", "TEXT", true), C("IndexJson", "TEXT", true)],
        "ParsimonyBundles" => [C("BundleId", "TEXT", false, 1), C("BaselineTokenJson", "TEXT", true),
            C("ProjectKey", "TEXT", true), C("BaselineDigest", "TEXT", true), C("MaterialKey", "TEXT", true),
            C("EvidenceDirectory", "TEXT", true), C("GrammarFactsPath", "TEXT", true), C("EvidencePath", "TEXT", true),
            C("BaselineRootDirectory", "TEXT", true), C("InputKind", "TEXT", true), C("CandidateIdentity", "TEXT"),
            C("ModelFingerprint", "TEXT", true), C("GrammarFactsSchemaVersion", "INTEGER", true),
            C("GrammarFactsSha256", "TEXT", true), C("EvidenceSchemaVersion", "INTEGER", true),
            C("EvidenceSha256", "TEXT", true), C("CreatedUtc", "TEXT", true),
            C("State", "TEXT", true, defaultValue: "'available'"), C("UnavailableReason", "TEXT")],
        "ParsimonyBundleReferences" => [C("BundleId", "TEXT", true, 1), C("ReferenceKind", "TEXT", true, 2),
            C("ReferenceId", "TEXT", true, 3), C("CreatedUtc", "TEXT", true)],
        "ParsimonyReaderLeases" => [C("LeaseId", "TEXT", false, 1), C("BundleId", "TEXT", true),
            C("OwnerId", "TEXT", true), C("CreatedUtc", "TEXT", true), C("HeartbeatUtc", "TEXT", true),
            C("ExpiresUtc", "TEXT", true)],
        "ParsimonyBuildClaims" => [C("BundleId", "TEXT", false, 1), C("ProjectKey", "TEXT", true),
            C("JobId", "TEXT", true), C("ClaimToken", "TEXT", true), C("BaselineRootDirectory", "TEXT", true),
            C("WorkingDirectory", "TEXT", true), C("StagingDirectory", "TEXT"), C("FinalDirectory", "TEXT"),
            C("CreatedUtc", "TEXT", true)],
        "CurrentParsimonyBundles" => [C("ProjectKey", "TEXT", true, 1), C("InputKind", "TEXT", true, 2),
            C("BundleId", "TEXT", true)],
        "ProposalDifferences" =>
        [C("DifferenceId", "TEXT", false, 1), C("ProposalId", "TEXT", true), C("TrialJobId", "TEXT", true),
            C("BeforeAssessmentId", "TEXT", true), C("AfterAssessmentId", "TEXT", true), C("TextId", "TEXT", true),
            C("BaselineToken", "TEXT", true), C("SummaryJson", "TEXT", true), C("SummaryDigest", "TEXT", true),
            C("CreatedUtc", "TEXT", true)],
        "ProposalDifferenceCases" =>
        [C("DifferenceId", "TEXT", true, 1), C("CaseKey", "TEXT", true, 2), C("Word", "TEXT", true),
            C("OccurrenceCount", "INTEGER", true), C("BeforeState", "TEXT", true), C("AfterState", "TEXT", true),
            C("CouldBeBad", "INTEGER", true), C("FactsJson", "TEXT", true), C("FactsDigest", "TEXT", true)],
        "ProposalDifferenceOccurrences" =>
        [C("DifferenceId", "TEXT", true, 1), C("TextId", "TEXT", true, 2), C("SegmentId", "TEXT", true, 3),
            C("OccurrenceIndex", "INTEGER", true, 4), C("LineNumber", "INTEGER", true), C("CaseKey", "TEXT", true)],
        "ProposalDifferenceBuckets" =>
        [C("DifferenceId", "TEXT", true, 1), C("CaseKey", "TEXT", true, 2), C("Bucket", "TEXT", true, 3)],
        "ParsedAnalyses" =>
        [C("AssessedWordId", "INTEGER", true), C("OrdinalIndex", "INTEGER", true), C("CategoryGuid", "TEXT"),
            C("MorphemeGuidsJson", "TEXT", true), C("RootIndex", "INTEGER", true), C("IdentityDigest", "TEXT", true)],
        "AssessmentPins" =>
        [C("AssessmentId", "TEXT", true, 1), C("PinnedBy", "TEXT", true, 2), C("PinnedUtc", "TEXT", true)],
        "Proposals" =>
        [C("ProposalId", "TEXT", false, 1), C("CurrentIntentDigest", "TEXT"), C("Status", "TEXT", true),
            C("Label", "TEXT"), C("Rationale", "TEXT"), C("RationaleIntentDigest", "TEXT"),
            C("RationaleUpdatedUtc", "TEXT"), C("Note", "TEXT"), C("NoteIntentDigest", "TEXT"), C("NoteUpdatedUtc", "TEXT"),
            C("SupersededBy", "TEXT"), C("AnchorJson", "TEXT"),
            C("ArchivedUtc", "TEXT"), C("DraftName", "TEXT"), C("DraftJson", "TEXT"), C("Role", "TEXT")],
        "ProposalRevisions" =>
        [C("ProposalId", "TEXT", true, 1), C("IntentDigest", "TEXT", true, 2),
            C("ProposalJson", "BLOB", true), C("CreatedUtc", "TEXT", true)],
        "Receipts" =>
        [C("ReceiptId", "TEXT", false, 1), C("ProposalId", "TEXT", true),
            C("IntentDigest", "TEXT", true), C("ReceiptJson", "TEXT", true), C("RecordedUtc", "TEXT", true)],
        "Reports" =>
        [C("ReportId", "TEXT", false, 1), C("ProposalId", "TEXT"), C("AssessmentId", "TEXT"),
            C("ReportJson", "TEXT", true), C("EvidenceJson", "TEXT"), C("CreatedUtc", "TEXT", true),
            C("Kind", "TEXT"), C("RenderedText", "TEXT")],
        "AppliedIndex" =>
        [C("ProposalId", "TEXT", false, 1), C("IntentDigest", "TEXT", true),
            C("AppliedUtc", "TEXT", true), C("RecordJson", "TEXT")],
        "Jobs" =>
        [C("JobId", "TEXT", false, 1), C("ProjectKey", "TEXT", true), C("Kind", "TEXT", true), C("Status", "TEXT", true),
            C("Attempt", "INTEGER", true, defaultValue: "1"), C("LineageId", "TEXT", true), C("InputJson", "TEXT", true),
            C("ResultJson", "TEXT"), C("ProgressJson", "TEXT"), C("CancellationRequested", "INTEGER", true, defaultValue: "0"),
            C("CreatedUtc", "TEXT", true), C("UpdatedUtc", "TEXT", true), C("Version", "INTEGER", true, defaultValue: "0"),
            C("DryRunPublished", "INTEGER", true, defaultValue: "0"), C("DryRunJson", "TEXT"),
            C("FailureCategory", "TEXT", true, defaultValue: "'none'"), C("NotBeforeUtc", "TEXT"), C("ArchivedUtc", "TEXT"),
            C("OwnerId", "TEXT"), C("ClaimToken", "TEXT"), C("LeaseUntilUtc", "TEXT"), C("HeartbeatUtc", "TEXT"),
            C("QueueOrder", "REAL", true, defaultValue: "CAST((julianday('now') - 2440587.5) * 86400000.0 AS REAL)")],
        "Baselines" =>
        [C("ProjectKey", "TEXT", false, 1), C("ProjectIdentity", "TEXT", true),
            C("SemanticSnapshotDigest", "TEXT", true), C("ProjectionVersion", "TEXT", true),
            C("CapturedUtc", "TEXT", true), C("BundleDigest", "TEXT", true),
            C("CapturedHostSessionId", "TEXT"), C("CapturedEditGeneration", "INTEGER"),
            C("RootDirectory", "TEXT", true), C("FwDataPath", "TEXT", true), C("PublishedUtc", "TEXT", true),
            C("SourceLastWriteUtc", "TEXT", true)],
        "RetainedInvocations" =>
        [C("InvocationId", "TEXT", false, 1), C("ProjectKey", "TEXT", true),
            C("BaselineToken", "TEXT", true), C("BaselineRootDirectory", "TEXT", true),
            C("BaselineFwDataPath", "TEXT", true), C("BaselineSourceLastWriteUtc", "TEXT", true),
            C("BaselinePublishedUtc", "TEXT", true), C("SavedUtc", "TEXT", true),
            C("SelectionDescriptorJson", "TEXT", true), C("SelectionDescriptorSha256", "TEXT", true),
            C("ExpectedKindsJson", "TEXT", true),
            C("Assessor", "TEXT", true),
            C("ScopeJson", "TEXT", true), C("ScopeDigest", "TEXT", true),
            C("ArtifactInvocationId", "TEXT", true)],
        "RetainedInvocationMembers" =>
        [C("InvocationId", "TEXT", true, 1), C("Kind", "TEXT", true, 2),
            C("AssessmentId", "TEXT", true)],
        "PhonologyExampleSets" =>
        [C("SetId", "TEXT", false, 1), C("Name", "TEXT", true), C("CurrentRevisionId", "TEXT", true)],
        "PhonologyExampleSetRevisions" =>
        [C("RevisionId", "TEXT", false, 1), C("SetId", "TEXT", true), C("RevisionNumber", "INTEGER", true),
            C("ExecutableSha256", "TEXT", true), C("CitedRevisionSha256", "TEXT", true),
            C("ExampleCount", "INTEGER", true), C("ProposalId", "TEXT"), C("SavedUtc", "TEXT", true)],
        "PhonologyExamples" =>
        [C("RevisionId", "TEXT", true, 1), C("Ordinal", "INTEGER", true, 2), C("ExampleId", "TEXT", true),
            C("Underlying", "TEXT", true), C("ExpectedJson", "TEXT", true), C("ContextJson", "TEXT", true),
            C("CitationJson", "TEXT")],
        _ => throw new InvalidDataException($"Motif table {table} is not registered.")
    };

    private static ColumnShape C(string name, string type, bool notNull = false, int primaryKey = 0,
        string? defaultValue = null) => new(name, type, notNull, defaultValue, primaryKey);

    private sealed record ColumnShape(
        string Name,
        string Type,
        bool NotNull,
        string? DefaultValue,
        int PrimaryKey)
    {
        public bool Matches(ColumnShape expected) =>
            StringComparer.OrdinalIgnoreCase.Equals(Name, expected.Name) &&
            StringComparer.OrdinalIgnoreCase.Equals(Type, expected.Type) &&
            NotNull == expected.NotNull &&
            DefaultValue == expected.DefaultValue &&
            PrimaryKey == expected.PrimaryKey;
    }

    private sealed record ForeignKeyShape(
        string Table,
        string From,
        string To,
        string OnUpdate,
        string OnDelete,
        string Match);

    private const string ParserRefusalDdl = """
        CREATE TABLE ParserRefusals (
            BaselineToken TEXT PRIMARY KEY,
            RefusalJson TEXT NOT NULL
        );
        """;

    private const string ActiveParseDdl = """
        CREATE TABLE ActiveParse (
            Id INTEGER PRIMARY KEY CHECK (Id = 1),
            Token TEXT NOT NULL,
            ProcessId INTEGER NOT NULL,
            ProcessStartIdentity TEXT NOT NULL
        );
        """;

    private const string MetadataDdl = """
        CREATE TABLE MotifMetadata (
            Id INTEGER PRIMARY KEY CHECK (Id = 1),
            FullFwDataPath TEXT NOT NULL,
            FieldWorksProjectIdentity TEXT NOT NULL,
            MinimumWorkerVersion TEXT NOT NULL,
            CreatedUtc TEXT NOT NULL,
            CurrentAssessmentId TEXT NULL,
            SetupSkippedUtc TEXT NULL
        );

        """;

    private const string CorpusDdl = """
        CREATE TABLE IF NOT EXISTS Corpora (
            CorpusId TEXT PRIMARY KEY,
            ProvenanceJson TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS CorpusDocuments (
            CorpusId TEXT NOT NULL REFERENCES Corpora(CorpusId),
            DocumentId TEXT NOT NULL,
            OrdinalIndex INTEGER NOT NULL,
            Title TEXT NOT NULL,
            Source TEXT NOT NULL,
            Text TEXT NOT NULL,
            ContentSha256 TEXT NOT NULL,
            IngestedUtc TEXT NOT NULL,
            Licence TEXT NULL,
            CapabilitiesJson TEXT NULL,
            AttributesJson TEXT NULL,
            PRIMARY KEY (CorpusId, DocumentId)
        );

        """;

    private const string ProposalWorkflowDdl = """
        CREATE TABLE Proposals (
            ProposalId TEXT PRIMARY KEY,
            CurrentIntentDigest TEXT NULL,
            Status TEXT NOT NULL,
            Label TEXT NULL,
            Rationale TEXT NULL,
            RationaleIntentDigest TEXT NULL,
            RationaleUpdatedUtc TEXT NULL,
            Note TEXT NULL,
            NoteIntentDigest TEXT NULL,
            NoteUpdatedUtc TEXT NULL,
            SupersededBy TEXT NULL,
            AnchorJson TEXT NULL,
            ArchivedUtc TEXT NULL,
            DraftName TEXT NULL,
            DraftJson TEXT NULL,
            Role TEXT NULL CHECK (Role IS NULL OR Role = 'pending-changes')
        );
        CREATE UNIQUE INDEX IX_Proposals_DraftName ON Proposals(DraftName);
        CREATE UNIQUE INDEX IX_Proposals_OpenRole ON Proposals(Role) WHERE DraftName IS NOT NULL;

        CREATE TABLE ProposalRevisions (
            ProposalId TEXT NOT NULL REFERENCES Proposals(ProposalId),
            IntentDigest TEXT NOT NULL,
            ProposalJson BLOB NOT NULL,
            CreatedUtc TEXT NOT NULL,
            PRIMARY KEY (ProposalId, IntentDigest)
        );

        CREATE TABLE Receipts (
            ReceiptId TEXT PRIMARY KEY,
            ProposalId TEXT NOT NULL REFERENCES Proposals(ProposalId),
            IntentDigest TEXT NOT NULL,
            ReceiptJson TEXT NOT NULL,
            RecordedUtc TEXT NOT NULL
        );

        CREATE TABLE AppliedIndex (
            ProposalId TEXT PRIMARY KEY,
            IntentDigest TEXT NOT NULL,
            AppliedUtc TEXT NOT NULL,
            RecordJson TEXT NULL
        );

        """;

    private const string SelectionDdl = """
        CREATE TABLE NamedSelections (
            SelectionName TEXT PRIMARY KEY,
            TextIdsJson TEXT NOT NULL,
            AddedWordsJson TEXT NOT NULL,
            CreatedUtc TEXT NOT NULL,
            UpdatedUtc TEXT NOT NULL,
            TimeLimitMode TEXT NOT NULL CHECK (TimeLimitMode IN ('Estimated', 'Explicit')),
            ExplicitPerWordLimitMs INTEGER NULL CHECK (ExplicitPerWordLimitMs IS NULL OR ExplicitPerWordLimitMs > 0),
            PerWordStepLimit INTEGER NULL CHECK (PerWordStepLimit IS NULL OR PerWordStepLimit > 0),
            Revision INTEGER NOT NULL CHECK (Revision > 0)
        );

        CREATE TABLE DefaultSelection (
            Id INTEGER PRIMARY KEY CHECK (Id = 1),
            SelectionName TEXT NOT NULL REFERENCES NamedSelections(SelectionName)
        );

        """;

    private const string AssessmentDdl = """
        CREATE TABLE AssessmentInvocations (
            InvocationId TEXT PRIMARY KEY,
            EvidenceJson TEXT NOT NULL
        );
        CREATE TABLE Assessments (
            AssessmentId TEXT PRIMARY KEY,
            SelectionName TEXT NULL,
            SelectionWordsJson TEXT NULL,
            SelectionSha256 TEXT NULL,
            SelectionProvenanceJson TEXT NULL,
            OutcomeDigest TEXT NULL,
            SemanticDigest TEXT NULL,
            GrammarSourceSha256 TEXT NOT NULL,
            ModelFingerprint TEXT NULL,
            Pipeline TEXT NULL,
            DiagnosticCount INTEGER NULL,
            SavedUtc TEXT NOT NULL,
            ProposalId TEXT NULL REFERENCES Proposals(ProposalId),
            ProposalIntentDigest TEXT NULL,
            Assessor TEXT NOT NULL,
            Kind TEXT NOT NULL,
            ScopeJson TEXT NOT NULL,
            ScopeDigest TEXT NOT NULL,
            TokeniserName TEXT NULL,
            TokeniserVersion TEXT NULL,
            BaselineToken TEXT NOT NULL,
            CachePath TEXT NULL,
            CacheDigest TEXT NULL,
            InvocationId TEXT NULL REFERENCES AssessmentInvocations(InvocationId),
            ReplacesAssessmentId TEXT NULL REFERENCES Assessments(AssessmentId),
            SubjectKind TEXT NOT NULL CHECK (SubjectKind IN ('words', 'phonologyExamples')),
            SubjectJson TEXT NULL,
            CHECK ((SubjectKind = 'words' AND Kind != 'PhonologyExamples' AND SubjectJson IS NULL
                AND SelectionName IS NOT NULL AND SelectionWordsJson IS NOT NULL AND SelectionSha256 IS NOT NULL
                AND TokeniserName IS NOT NULL AND TokeniserVersion IS NOT NULL)
                OR (SubjectKind = 'phonologyExamples' AND Kind = 'PhonologyExamples' AND SubjectJson IS NOT NULL
                AND SelectionName IS NULL AND SelectionWordsJson IS NULL AND SelectionSha256 IS NULL
                AND SelectionProvenanceJson IS NULL AND TokeniserName IS NULL AND TokeniserVersion IS NULL
                AND CachePath IS NULL AND CacheDigest IS NULL AND InvocationId IS NULL AND ReplacesAssessmentId IS NULL
                AND ProposalIntentDigest IS NULL AND OutcomeDigest IS NULL AND SemanticDigest IS NULL
                AND ModelFingerprint IS NULL AND Pipeline IS NULL AND DiagnosticCount IS NULL))
        );
        CREATE INDEX IX_Assessments_Proposal ON Assessments(ProposalId);
        CREATE INDEX IX_Assessments_Kind ON Assessments(Kind);

        CREATE TABLE AssessmentWordPayloads (
            Hash BLOB NOT NULL PRIMARY KEY,
            Payload BLOB NOT NULL
        ) WITHOUT ROWID;

        CREATE TABLE AssessedWords (
            AssessedWordId INTEGER PRIMARY KEY AUTOINCREMENT,
            AssessmentId TEXT NOT NULL REFERENCES Assessments(AssessmentId),
            OrdinalIndex INTEGER NOT NULL,
            Word TEXT NOT NULL,
            Outcome TEXT NOT NULL,
            ElapsedMs INTEGER NULL,
            ElapsedNs INTEGER NULL,
            RawSignature TEXT NULL,
            MorphologyJson BLOB NULL REFERENCES AssessmentWordPayloads(Hash),
            CorrectnessJson BLOB NULL REFERENCES AssessmentWordPayloads(Hash),
            ProjectStanding TEXT NULL,
            OccurrenceCount INTEGER NULL,
            ReadingGradesJson BLOB NULL REFERENCES AssessmentWordPayloads(Hash),
            AnalysisComparisonJson BLOB NULL REFERENCES AssessmentWordPayloads(Hash),
            MissedApprovedCount INTEGER NULL,
            MissedApprovedJson BLOB NULL REFERENCES AssessmentWordPayloads(Hash),
            IsIncomplete INTEGER NOT NULL DEFAULT 0 CHECK (IsIncomplete IN (0, 1)),
            Attempts INTEGER NULL,
            Passes INTEGER NULL,
            WorkSteps INTEGER NULL
        );
        CREATE INDEX IX_AssessedWords_Assessment ON AssessedWords(AssessmentId);
        CREATE INDEX IX_AssessedWords_Word ON AssessedWords(AssessmentId, Word);

        CREATE TABLE ParsedAnalyses (
            AssessedWordId INTEGER NOT NULL REFERENCES AssessedWords(AssessedWordId),
            OrdinalIndex INTEGER NOT NULL,
            CategoryGuid TEXT NULL,
            MorphemeGuidsJson TEXT NOT NULL,
            RootIndex INTEGER NOT NULL,
            IdentityDigest TEXT NOT NULL
        );
        CREATE INDEX IX_ParsedAnalyses_Word ON ParsedAnalyses(AssessedWordId);

        CREATE TABLE AssessmentObjectTimings (
            AssessmentId TEXT NOT NULL PRIMARY KEY REFERENCES Assessments(AssessmentId),
            Detail BLOB NOT NULL,
            Evidence BLOB NULL
        ) WITHOUT ROWID;

        CREATE TABLE AssessmentPins (
            AssessmentId TEXT NOT NULL REFERENCES Assessments(AssessmentId),
            PinnedBy TEXT NOT NULL,
            PinnedUtc TEXT NOT NULL,
            PRIMARY KEY (AssessmentId, PinnedBy)
        );

        CREATE TABLE Reports (
            ReportId TEXT PRIMARY KEY,
            ProposalId TEXT NULL REFERENCES Proposals(ProposalId),
            AssessmentId TEXT NULL REFERENCES Assessments(AssessmentId),
            ReportJson TEXT NOT NULL,
            EvidenceJson TEXT NULL,
            CreatedUtc TEXT NOT NULL,
            Kind TEXT NULL,
            RenderedText TEXT NULL
        );

        """;

    private const string JobDdl = """
        CREATE TABLE Jobs (
            JobId TEXT PRIMARY KEY,
            ProjectKey TEXT NOT NULL,
            Kind TEXT NOT NULL,
            Status TEXT NOT NULL,
            Attempt INTEGER NOT NULL DEFAULT 1 CHECK (Attempt > 0),
            LineageId TEXT NOT NULL,
            InputJson TEXT NOT NULL,
            ResultJson TEXT NULL,
            ProgressJson TEXT NULL,
            CancellationRequested INTEGER NOT NULL DEFAULT 0 CHECK (CancellationRequested IN (0, 1)),
            CreatedUtc TEXT NOT NULL,
            UpdatedUtc TEXT NOT NULL,
            Version INTEGER NOT NULL DEFAULT 0 CHECK (Version >= 0),
            DryRunPublished INTEGER NOT NULL DEFAULT 0 CHECK (DryRunPublished IN (0, 1)),
            DryRunJson TEXT NULL,
            FailureCategory TEXT NOT NULL DEFAULT 'none',
            NotBeforeUtc TEXT NULL,
            ArchivedUtc TEXT NULL,
            OwnerId TEXT NULL,
            ClaimToken TEXT NULL,
            LeaseUntilUtc TEXT NULL,
            HeartbeatUtc TEXT NULL,
            QueueOrder REAL NOT NULL DEFAULT (CAST((julianday('now') - 2440587.5) * 86400000.0 AS REAL))
        );
        CREATE UNIQUE INDEX IX_Jobs_Lineage_Attempt ON Jobs(LineageId, Attempt);
        CREATE INDEX IX_Jobs_Status_Updated ON Jobs(Status, UpdatedUtc);
        CREATE INDEX IX_Jobs_Lease ON Jobs(Status, LeaseUntilUtc);
        CREATE INDEX IX_Jobs_QueueOrder ON Jobs(QueueOrder);

        """;

    private const string BaselineDdl = """
        CREATE TABLE Baselines (
            ProjectKey TEXT PRIMARY KEY,
            ProjectIdentity TEXT NOT NULL,
            SemanticSnapshotDigest TEXT NOT NULL,
            ProjectionVersion TEXT NOT NULL,
            CapturedUtc TEXT NOT NULL,
            BundleDigest TEXT NOT NULL,
            CapturedHostSessionId TEXT NULL,
            CapturedEditGeneration INTEGER NULL,
            RootDirectory TEXT NOT NULL,
            FwDataPath TEXT NOT NULL,
            PublishedUtc TEXT NOT NULL,
            SourceLastWriteUtc TEXT NOT NULL,
            CHECK ((CapturedHostSessionId IS NULL) = (CapturedEditGeneration IS NULL)),
            CHECK (CapturedEditGeneration IS NULL OR CapturedEditGeneration >= 0)
        );

        CREATE TABLE BaselineSummaries (
            ProjectKey TEXT PRIMARY KEY REFERENCES Baselines(ProjectKey),
            SummaryJson TEXT NOT NULL
        );
        """;

    private const string BaselineTextWordsDdl = """
        CREATE TABLE BaselineTextWords (
            ProjectKey TEXT NOT NULL REFERENCES Baselines(ProjectKey),
            TextId TEXT NOT NULL,
            BundleDigest TEXT NOT NULL,
            TextJson TEXT NOT NULL,
            PRIMARY KEY (ProjectKey, TextId)
        );

        CREATE TABLE BaselineTextWordforms (
            ProjectKey TEXT NOT NULL REFERENCES Baselines(ProjectKey),
            WordformId TEXT NOT NULL,
            BundleDigest TEXT NOT NULL,
            WordformJson TEXT NOT NULL,
            PRIMARY KEY (ProjectKey, WordformId)
        );
        """;

    private const string BaselineTextReadIndexDdl = """
        CREATE TABLE BaselineTextReadIndex (
            ProjectKey TEXT NOT NULL REFERENCES Baselines(ProjectKey),
            TextId TEXT NOT NULL,
            BundleDigest TEXT NOT NULL,
            IndexJson TEXT NOT NULL,
            PRIMARY KEY (ProjectKey, TextId)
        );
        """;

    private const string RetainedInvocationDdl = """
        CREATE TABLE RetainedInvocations (
            InvocationId TEXT PRIMARY KEY,
            ProjectKey TEXT NOT NULL,
            BaselineToken TEXT NOT NULL,
            BaselineRootDirectory TEXT NOT NULL,
            BaselineFwDataPath TEXT NOT NULL,
            BaselineSourceLastWriteUtc TEXT NOT NULL,
            BaselinePublishedUtc TEXT NOT NULL,
            SavedUtc TEXT NOT NULL,
            SelectionDescriptorJson TEXT NOT NULL,
            SelectionDescriptorSha256 TEXT NOT NULL,
            ExpectedKindsJson TEXT NOT NULL,
            Assessor TEXT NOT NULL,
            ScopeJson TEXT NOT NULL,
            ScopeDigest TEXT NOT NULL,
            ArtifactInvocationId TEXT NOT NULL REFERENCES AssessmentInvocations(InvocationId)
        );
        CREATE INDEX IX_RetainedInvocations_Project ON RetainedInvocations(ProjectKey);

        CREATE TABLE RetainedInvocationMembers (
            InvocationId TEXT NOT NULL REFERENCES RetainedInvocations(InvocationId),
            Kind TEXT NOT NULL,
            AssessmentId TEXT NOT NULL REFERENCES Assessments(AssessmentId),
            PRIMARY KEY (InvocationId, Kind),
            UNIQUE (AssessmentId)
        );
        CREATE INDEX IX_RetainedInvocationMembers_Assessment ON RetainedInvocationMembers(AssessmentId);
        """;

    private const string GrammarCheckDdl = """
        CREATE TABLE GrammarChecks (
            BaselineToken TEXT NOT NULL,
            ProposalId TEXT NULL,
            IntentDigest TEXT NULL,
            SelectionSha256 TEXT NOT NULL,
            ParserStamp TEXT NULL,
            ResponseGzip BLOB NOT NULL,
            CheckedUtc TEXT NOT NULL
        );
        CREATE UNIQUE INDEX IX_GrammarChecks_Project ON GrammarChecks(BaselineToken) WHERE ProposalId IS NULL;
        CREATE UNIQUE INDEX IX_GrammarChecks_Proposal
            ON GrammarChecks(BaselineToken, ProposalId, IntentDigest) WHERE ProposalId IS NOT NULL;
        """;

    private const string PendingChangeFitDdl = """
        CREATE TABLE PendingChangeFits (
            DraftRevision TEXT NOT NULL,
            ProjectLastWriteUtcTicks INTEGER NOT NULL,
            BaselineIdentity TEXT NOT NULL,
            FitSummaryJson TEXT NOT NULL,
            PRIMARY KEY (DraftRevision, ProjectLastWriteUtcTicks, BaselineIdentity)
        );
        """;

    private const string ProposalDifferenceDdl = """
        CREATE TABLE ProposalDifferences (
            DifferenceId TEXT PRIMARY KEY,
            ProposalId TEXT NOT NULL REFERENCES Proposals(ProposalId),
            TrialJobId TEXT NOT NULL,
            BeforeAssessmentId TEXT NOT NULL,
            AfterAssessmentId TEXT NOT NULL,
            TextId TEXT NOT NULL,
            BaselineToken TEXT NOT NULL,
            SummaryJson TEXT NOT NULL,
            SummaryDigest TEXT NOT NULL,
            CreatedUtc TEXT NOT NULL
        );
        CREATE INDEX IX_ProposalDifferences_Proposal ON ProposalDifferences(ProposalId, CreatedUtc);

        CREATE TABLE ProposalDifferenceCases (
            DifferenceId TEXT NOT NULL REFERENCES ProposalDifferences(DifferenceId) ON DELETE CASCADE,
            CaseKey TEXT NOT NULL,
            Word TEXT NOT NULL,
            OccurrenceCount INTEGER NOT NULL CHECK (OccurrenceCount >= 0),
            BeforeState TEXT NOT NULL,
            AfterState TEXT NOT NULL,
            CouldBeBad INTEGER NOT NULL CHECK (CouldBeBad IN (0, 1)),
            FactsJson TEXT NOT NULL,
            FactsDigest TEXT NOT NULL,
            PRIMARY KEY (DifferenceId, CaseKey)
        );

        CREATE TABLE ProposalDifferenceOccurrences (
            DifferenceId TEXT NOT NULL REFERENCES ProposalDifferences(DifferenceId) ON DELETE CASCADE,
            TextId TEXT NOT NULL,
            SegmentId TEXT NOT NULL,
            OccurrenceIndex INTEGER NOT NULL CHECK (OccurrenceIndex >= 0),
            LineNumber INTEGER NOT NULL,
            CaseKey TEXT NOT NULL,
            PRIMARY KEY (DifferenceId, TextId, SegmentId, OccurrenceIndex)
        );
        CREATE INDEX IX_ProposalDifferenceOccurrences_Order
            ON ProposalDifferenceOccurrences(DifferenceId, TextId, LineNumber, OccurrenceIndex);

        CREATE TABLE ProposalDifferenceBuckets (
            DifferenceId TEXT NOT NULL REFERENCES ProposalDifferences(DifferenceId) ON DELETE CASCADE,
            CaseKey TEXT NOT NULL,
            Bucket TEXT NOT NULL,
            PRIMARY KEY (DifferenceId, CaseKey, Bucket)
        );
        CREATE INDEX IX_ProposalDifferenceBuckets_Bucket ON ProposalDifferenceBuckets(DifferenceId, Bucket, CaseKey);
        """;

    private const string ReviewMarkDdl = """
        CREATE TABLE ReviewMarks (
            ProposalId TEXT NOT NULL,
            EntryKey TEXT NOT NULL,
            ContentDigest TEXT NOT NULL,
            EvidenceDigest TEXT NOT NULL,
            LookedAtUtc TEXT NOT NULL,
            PRIMARY KEY (ProposalId, EntryKey)
        );
        """;

    private const string ReadOccurrenceDdl = """
        CREATE TABLE ReadOccurrences (
            TextId TEXT NOT NULL,
            ParagraphId TEXT NOT NULL,
            SegmentId TEXT NOT NULL,
            WordIndex INTEGER NOT NULL CHECK (WordIndex >= 0),
            FingerprintJson TEXT NOT NULL,
            PRIMARY KEY (TextId, ParagraphId, SegmentId, WordIndex)
        );
        """;

    private const string ParsimonyBundleDdl = """
        CREATE TABLE ParsimonyBundles (
            BundleId TEXT PRIMARY KEY,
            BaselineTokenJson TEXT NOT NULL,
            ProjectKey TEXT NOT NULL,
            BaselineDigest TEXT NOT NULL,
            MaterialKey TEXT NOT NULL,
            EvidenceDirectory TEXT NOT NULL,
            GrammarFactsPath TEXT NOT NULL,
            EvidencePath TEXT NOT NULL,
            BaselineRootDirectory TEXT NOT NULL,
            InputKind TEXT NOT NULL CHECK (InputKind IN ('baseline','candidate')),
            CandidateIdentity TEXT NULL,
            ModelFingerprint TEXT NOT NULL,
            GrammarFactsSchemaVersion INTEGER NOT NULL CHECK (GrammarFactsSchemaVersion > 0),
            GrammarFactsSha256 TEXT NOT NULL,
            EvidenceSchemaVersion INTEGER NOT NULL CHECK (EvidenceSchemaVersion > 0),
            EvidenceSha256 TEXT NOT NULL,
            CreatedUtc TEXT NOT NULL,
            State TEXT NOT NULL CHECK (State IN ('available','deleting','missing','corrupt')) DEFAULT 'available',
            UnavailableReason TEXT NULL,
            CHECK ((InputKind = 'candidate') = (CandidateIdentity IS NOT NULL))
        );
        CREATE INDEX IX_ParsimonyBundles_ProjectCreated ON ParsimonyBundles(ProjectKey, CreatedUtc);
        """;

    private const string ParsimonyBundleLifetimeDdl = """
        CREATE TABLE ParsimonyBundleReferences (
            BundleId TEXT NOT NULL REFERENCES ParsimonyBundles(BundleId) ON DELETE CASCADE,
            ReferenceKind TEXT NOT NULL,
            ReferenceId TEXT NOT NULL,
            CreatedUtc TEXT NOT NULL,
            PRIMARY KEY (BundleId, ReferenceKind, ReferenceId)
        );

        CREATE TABLE ParsimonyReaderLeases (
            LeaseId TEXT PRIMARY KEY,
            BundleId TEXT NOT NULL REFERENCES ParsimonyBundles(BundleId) ON DELETE CASCADE,
            OwnerId TEXT NOT NULL,
            CreatedUtc TEXT NOT NULL,
            HeartbeatUtc TEXT NOT NULL,
            ExpiresUtc TEXT NOT NULL
        );
        CREATE INDEX IX_ParsimonyReaderLeases_BundleExpiry ON ParsimonyReaderLeases(BundleId, ExpiresUtc);

        CREATE TABLE ParsimonyBuildClaims (
            BundleId TEXT PRIMARY KEY,
            ProjectKey TEXT NOT NULL,
            JobId TEXT NOT NULL,
            ClaimToken TEXT NOT NULL,
            BaselineRootDirectory TEXT NOT NULL,
            WorkingDirectory TEXT NOT NULL,
            StagingDirectory TEXT NULL,
            FinalDirectory TEXT NULL,
            CreatedUtc TEXT NOT NULL
        );
        CREATE INDEX IX_ParsimonyBuildClaims_Project ON ParsimonyBuildClaims(ProjectKey);

        CREATE TABLE CurrentParsimonyBundles (
            ProjectKey TEXT NOT NULL,
            InputKind TEXT NOT NULL CHECK (InputKind IN ('baseline','candidate')),
            BundleId TEXT NOT NULL REFERENCES ParsimonyBundles(BundleId) ON DELETE CASCADE,
            PRIMARY KEY (ProjectKey, InputKind)
        );
        """;

    private const string PhonologyExampleDdl = """
        CREATE TABLE PhonologyExampleSets (
            SetId TEXT PRIMARY KEY,
            Name TEXT NOT NULL,
            CurrentRevisionId TEXT NOT NULL,
            FOREIGN KEY (SetId, CurrentRevisionId)
                REFERENCES PhonologyExampleSetRevisions(SetId, RevisionId) DEFERRABLE INITIALLY DEFERRED
        );
        CREATE UNIQUE INDEX IX_PhonologyExampleSets_Name ON PhonologyExampleSets(Name);

        CREATE TABLE PhonologyExampleSetRevisions (
            RevisionId TEXT PRIMARY KEY,
            SetId TEXT NOT NULL REFERENCES PhonologyExampleSets(SetId),
            RevisionNumber INTEGER NOT NULL CHECK (RevisionNumber > 0),
            ExecutableSha256 TEXT NOT NULL,
            CitedRevisionSha256 TEXT NOT NULL,
            ExampleCount INTEGER NOT NULL CHECK (ExampleCount BETWEEN 1 AND 500),
            ProposalId TEXT NULL REFERENCES Proposals(ProposalId),
            SavedUtc TEXT NOT NULL,
            UNIQUE (SetId, RevisionId)
        );
        CREATE UNIQUE INDEX IX_PhonologyExampleSetRevisions_SetRevision
            ON PhonologyExampleSetRevisions(SetId, RevisionNumber);

        CREATE TABLE PhonologyExamples (
            RevisionId TEXT NOT NULL REFERENCES PhonologyExampleSetRevisions(RevisionId),
            Ordinal INTEGER NOT NULL CHECK (Ordinal >= 0),
            ExampleId TEXT NOT NULL,
            Underlying TEXT NOT NULL,
            ExpectedJson TEXT NOT NULL,
            ContextJson TEXT NOT NULL,
            CitationJson TEXT NULL,
            PRIMARY KEY (RevisionId, Ordinal),
            UNIQUE (RevisionId, ExampleId),
            UNIQUE (RevisionId, Ordinal, ExampleId)
        );
        CREATE UNIQUE INDEX IX_PhonologyExamples_RevisionExampleId ON PhonologyExamples(RevisionId, ExampleId);

        CREATE TRIGGER TR_PhonologyExamples_Complete
        BEFORE INSERT ON PhonologyExamples
        WHEN (SELECT COUNT(*) FROM PhonologyExamples WHERE RevisionId = NEW.RevisionId) >=
             (SELECT ExampleCount FROM PhonologyExampleSetRevisions WHERE RevisionId = NEW.RevisionId)
        BEGIN
            SELECT RAISE(ABORT, 'Example Set revision is complete');
        END;

        CREATE TRIGGER TR_PhonologyExampleSets_NameImmutable
        BEFORE UPDATE OF Name ON PhonologyExampleSets
        BEGIN
            SELECT RAISE(ABORT, 'Example Set names are immutable');
        END;

        CREATE TRIGGER TR_PhonologyExampleSetRevisions_ImmutableUpdate
        BEFORE UPDATE ON PhonologyExampleSetRevisions
        BEGIN
            SELECT RAISE(ABORT, 'Example Set revisions are immutable');
        END;

        CREATE TRIGGER TR_PhonologyExampleSetRevisions_ImmutableDelete
        BEFORE DELETE ON PhonologyExampleSetRevisions
        BEGIN
            SELECT RAISE(ABORT, 'Example Set revisions are immutable');
        END;

        CREATE TRIGGER TR_PhonologyExamples_ImmutableUpdate
        BEFORE UPDATE ON PhonologyExamples
        BEGIN
            SELECT RAISE(ABORT, 'Example Set examples are immutable');
        END;

        CREATE TRIGGER TR_PhonologyExamples_ImmutableDelete
        BEFORE DELETE ON PhonologyExamples
        BEGIN
            SELECT RAISE(ABORT, 'Example Set examples are immutable');
        END;
        """;

    private const string PhonologyAssessmentDdl = """
        CREATE TABLE PhonologyAssessmentBindings (
            AssessmentId TEXT NOT NULL PRIMARY KEY REFERENCES Assessments(AssessmentId),
            RevisionId TEXT NOT NULL REFERENCES PhonologyExampleSetRevisions(RevisionId),
            HeaderJson TEXT NOT NULL,
            UNIQUE (AssessmentId, RevisionId)
        );
        CREATE TABLE PhonologyAssessmentRows (
            AssessmentId TEXT NOT NULL,
            RevisionId TEXT NOT NULL,
            Ordinal INTEGER NOT NULL CHECK (Ordinal >= 0),
            ExampleId TEXT NOT NULL,
            EvidenceJson TEXT NOT NULL,
            SummaryJson TEXT NOT NULL,
            PRIMARY KEY (AssessmentId, Ordinal),
            UNIQUE (AssessmentId, ExampleId),
            FOREIGN KEY (AssessmentId, RevisionId) REFERENCES PhonologyAssessmentBindings(AssessmentId, RevisionId),
            FOREIGN KEY (RevisionId, Ordinal, ExampleId) REFERENCES PhonologyExamples(RevisionId, Ordinal, ExampleId)
        );
        CREATE TABLE PhonologyAssessmentDependencies (
            AssessmentId TEXT NOT NULL REFERENCES PhonologyAssessmentBindings(AssessmentId),
            Ordinal INTEGER NOT NULL CHECK (Ordinal >= 0),
            Path TEXT NOT NULL,
            Sha256 TEXT NOT NULL,
            PRIMARY KEY (AssessmentId, Ordinal),
            UNIQUE (AssessmentId, Path)
        );
        """;
}
