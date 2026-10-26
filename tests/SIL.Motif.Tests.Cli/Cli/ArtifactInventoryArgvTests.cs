using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Store;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Cli;

public sealed class ArtifactInventoryArgvTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-artifact-inventory-" + Guid.NewGuid().ToString("N"));
    private readonly string _workerRoot = Path.Combine(Path.GetTempPath(), "motif-artifact-inventory-machine-" + Guid.NewGuid().ToString("N"));
    private readonly string _project;
    private readonly string _store;

    public ArtifactInventoryArgvTests()
    {
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(_workerRoot);
        WorkspaceOwnership.Bootstrap(_root);
        _project = Path.Combine(_root, "project.fwdata");
        _store = Path.Combine(_root, "project.motif.db");
        File.WriteAllText(_project, "synthetic project locator");
        using (MotifDatabase.OpenOwned(_store, new ProjectLocator(_project, "inventory-test"),
                   MotifSchema.CurrentSchema, MotifProductVersion.Current))
        {
        }

        var run = Path.Combine(_root, "assessment-runs", "inv-test");
        Directory.CreateDirectory(run);
        File.WriteAllText(Path.Combine(run, "source.fwdata"), "synthetic staged source");
        var sources = Path.Combine(_root, "assessment-sources");
        Directory.CreateDirectory(sources);
        File.WriteAllText(Path.Combine(sources, new string('a', 64) + ".fwdata"), "synthetic shared source");
        var baseline = Path.Combine(_root, "baselines", new string('b', 64));
        Directory.CreateDirectory(baseline);
        File.WriteAllText(Path.Combine(baseline, "source.fwdata"), "synthetic Baseline source");

        File.WriteAllText(StoresManifest, JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            referenceDomainComplete = true,
            stores = new[]
            {
                new { databasePath = _store, projectPath = _project, projectIdentity = "inventory-test" }
            }
        }));
    }

    private string StoresManifest => Path.Combine(_root, "stores.json");

    [Fact]
    public void InventoryReadsCommittedWalWithoutShmAndLeavesEverySourceEntryUnchanged()
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
               {
                   DataSource = _store,
                   Mode = SqliteOpenMode.ReadWrite,
                   Pooling = false,
               }.ToString());
        connection.Open();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA journal_mode=WAL; PRAGMA wal_autocheckpoint=0; " +
                                  "UPDATE MotifMetadata SET CreatedUtc = '2026-10-24T00:00:00.0000000+00:00' WHERE Id = 1;";
            command.ExecuteNonQuery();
        }
        Assert.True(File.Exists(_store + "-wal"));
        if (OperatingSystem.IsLinux()) File.Delete(_store + "-shm");

        var before = SnapshotTree(_root);
        var result = RunInventory();

        Assert.Equal(0, result.ExitCode);
        using var inventory = JsonDocument.Parse(result.Output);
        Assert.Equal(Path.GetFullPath(_root), inventory.RootElement.GetProperty("rootPath").GetString());
        Assert.Equal(3, inventory.RootElement.GetProperty("artifacts").GetArrayLength());
        Assert.True(inventory.RootElement.GetProperty("inventoryComplete").GetBoolean());
        Assert.True(inventory.RootElement.GetProperty("logicalBytes").GetInt64() > 0);
        Assert.Equal(before, SnapshotTree(_root));
        Assert.Empty(Directory.EnumerateFileSystemEntries(_workerRoot));
    }

    [Fact]
    public void DeveloperInventoryIsNotExposedAsAnAgentTool()
    {
        var descriptor = Assert.Single(CommandCatalog.All, command => command.Name == "store artifacts");
        Assert.Equal(CommandSurface.Developer, descriptor.Surface);
        Assert.Equal(AgentClass.HumanOnly, descriptor.Agent);
        Assert.False(descriptor.ExposeAsAgentTool);

        var result = RunInventory(developerCommands: false);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("MOTIF_DEVELOPER_COMMANDS=1", result.Error, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_workerRoot));
    }

    [Fact]
    public void MissingStoreLockAndMalformedSchemaRemainUnknownWithoutCreatingLocks()
    {
        var lockPath = _store + ".use.lock";
        File.Delete(lockPath);
        var before = SnapshotTree(_root);

        var missingLock = RunInventory();

        Assert.Equal(0, missingLock.ExitCode);
        using (var missingDocument = JsonDocument.Parse(missingLock.Output))
        {
            Assert.False(missingDocument.RootElement.GetProperty("referenceDomainComplete").GetBoolean());
            Assert.Contains(missingDocument.RootElement.GetProperty("diagnostics").EnumerateArray(),
                item => item.GetProperty("code").GetString() == "missing-use-lock");
            Assert.All(missingDocument.RootElement.GetProperty("artifacts").EnumerateArray(), item =>
                Assert.Equal("Unknown", item.GetProperty("disposition").GetString()));
        }
        Assert.False(File.Exists(lockPath));
        Assert.Equal(before, SnapshotTree(_root));

        File.WriteAllText(lockPath, string.Empty);
        File.WriteAllText(_store, "not a SQLite database");
        WriteManifest(referenceDomainComplete: false);
        before = SnapshotTree(_root);

        var malformed = RunInventory();

        Assert.Equal(0, malformed.ExitCode);
        using var malformedDocument = JsonDocument.Parse(malformed.Output);
        Assert.False(malformedDocument.RootElement.GetProperty("referenceDomainComplete").GetBoolean());
        Assert.Contains(malformedDocument.RootElement.GetProperty("stores").EnumerateArray(),
            item => item.GetProperty("reasonCode").GetString() == "snapshot-database-invalid");
        Assert.Contains(malformedDocument.RootElement.GetProperty("diagnostics").EnumerateArray(),
            item => item.GetProperty("code").GetString() == "reference-domain-not-declared-complete");
        Assert.All(malformedDocument.RootElement.GetProperty("artifacts").EnumerateArray(), item =>
            Assert.Equal("Unknown", item.GetProperty("disposition").GetString()));
        Assert.Equal(before, SnapshotTree(_root));
    }

    [Fact]
    public void SharedSourcePathIsReportedOnceWithEveryOwner()
    {
        var sourcePath = Path.Combine(_root, "assessment-sources", new string('a', 64) + ".fwdata");
        var secondProject = Path.Combine(_root, "second.fwdata");
        var secondStore = Path.Combine(_root, "second.motif.db");
        File.WriteAllText(secondProject, "second synthetic project locator");
        using (MotifDatabase.OpenOwned(secondStore, new ProjectLocator(secondProject, "inventory-test-2"),
                   MotifSchema.CurrentSchema, MotifProductVersion.Current))
        {
        }
        RecordInvocation(_store, _project, "inventory-test", "inv-shared-one", sourcePath);
        RecordInvocation(secondStore, secondProject, "inventory-test-2", "inv-shared-two", sourcePath);
        WriteManifest(referenceDomainComplete: true,
            (_store, _project, "inventory-test"), (secondStore, secondProject, "inventory-test-2"));

        var result = RunInventory();

        Assert.Equal(0, result.ExitCode);
        using var inventory = JsonDocument.Parse(result.Output);
        var shared = Assert.Single(inventory.RootElement.GetProperty("artifacts").EnumerateArray(), item =>
            item.GetProperty("path").GetString() == sourcePath);
        Assert.Equal(FileInfoLength(sourcePath), shared.GetProperty("logicalBytes").GetInt64());
        Assert.Equal(new[] { "inv-shared-one", "inv-shared-two" },
            shared.GetProperty("ownerInvocationIds").EnumerateArray().Select(item => item.GetString()).Order(StringComparer.Ordinal));
        var expectedBytes = Directory.EnumerateFiles(Path.Combine(_root, "assessment-runs"), "*", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(Path.Combine(_root, "assessment-sources"), "*", SearchOption.AllDirectories))
            .Concat(Directory.EnumerateFiles(Path.Combine(_root, "baselines"), "*", SearchOption.AllDirectories))
            .Sum(FileInfoLength);
        Assert.Equal(expectedBytes, inventory.RootElement.GetProperty("logicalBytes").GetInt64());
    }

    [Fact]
    public void CurrentHistoryPinsReportsAndDifferenceReferencesRetainRunEvidence()
    {
        var invocationId = "inv-cited";
        var sourcePath = Path.Combine(_root, "assessment-sources", new string('a', 64) + ".fwdata");
        RecordInvocation(_store, _project, "inventory-test", invocationId, sourcePath);
        var runPath = Path.Combine(_root, "assessment-runs", invocationId, "source.fwdata");
        Directory.CreateDirectory(Path.GetDirectoryName(runPath)!);
        File.WriteAllText(runPath, "cited run data");
        AddCitations(invocationId);

        var result = RunInventory();

        Assert.Equal(0, result.ExitCode);
        using var inventory = JsonDocument.Parse(result.Output);
        var run = Assert.Single(inventory.RootElement.GetProperty("artifacts").EnumerateArray(), item =>
            item.GetProperty("path").GetString() == runPath);
        Assert.Equal("Retained", run.GetProperty("disposition").GetString());
        Assert.Equal("current", run.GetProperty("reasonCode").GetString());
        var retainedIds = run.GetProperty("retainingRecordIds").EnumerateArray()
            .Select(item => item.GetString()).ToHashSet(StringComparer.Ordinal);
        Assert.Contains("assessment-cited", retainedIds);
        Assert.Contains("report-cited", retainedIds);
        Assert.Contains("difference-cited", retainedIds);
        Assert.Contains("report-frozen", retainedIds);
        Assert.Contains(inventory.RootElement.GetProperty("diagnostics").EnumerateArray(), item =>
            item.GetProperty("code").GetString() == "frozen-report-baseline-unresolved");
    }

    [Fact]
    public void MissingFrozenParsimonyEvidenceKeepsKnownCitationAndWithholdsAggregateBytes()
    {
        const string invocationId = "inv-missing-report-evidence";
        const string assessmentId = "assessment-report-evidence";
        const string reportId = "report-missing-evidence";
        var sourcePath = Path.Combine(_root, "assessment-runs", invocationId, "source.fwdata");
        Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
        File.WriteAllText(sourcePath, "cited synthetic run");
        RecordInvocation(_store, _project, "inventory-test", invocationId, sourcePath);
        RecordAssessment(assessmentId, invocationId);
        var report = CreateParsimonyReport(reportId, [assessmentId], []);
        RecordReport(reportId, "parsimony", null,
            JsonSerializer.Serialize(report, new JsonSerializerOptions(JsonSerializerDefaults.Web)), null);

        var result = RunInventory();

        Assert.Equal(0, result.ExitCode);
        using var inventory = JsonDocument.Parse(result.Output);
        Assert.False(inventory.RootElement.GetProperty("referenceDomainComplete").GetBoolean());
        Assert.False(inventory.RootElement.TryGetProperty("logicalBytes", out _));
        Assert.Contains(inventory.RootElement.GetProperty("diagnostics").EnumerateArray(), item =>
            item.GetProperty("code").GetString() == "frozen-report-reference-unresolved");
        var artifact = Assert.Single(inventory.RootElement.GetProperty("artifacts").EnumerateArray(), item =>
            item.GetProperty("path").GetString() == sourcePath);
        Assert.Equal("Retained", artifact.GetProperty("disposition").GetString());
        Assert.Contains(reportId, artifact.GetProperty("retainingRecordIds").EnumerateArray()
            .Select(item => item.GetString()));
    }

    [Fact]
    public void ParsimonyReportCitationsFromResponseInputsAndEvidenceRetainTheirRuns()
    {
        const string reportId = "report-all-citation-shapes";
        var reportAssessment = RecordInventoryCitation("response-citation");
        var inputAssessment = RecordInventoryCitation("input-citation");
        var evidenceAssessment = RecordInventoryCitation("evidence-citation");
        var report = CreateParsimonyReport(reportId, [reportAssessment.AssessmentId],
            [inputAssessment.AssessmentId]);
        RecordReport(reportId, "parsimony", null,
            JsonSerializer.Serialize(report, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            CreateFrozenReportEvidence([evidenceAssessment.AssessmentId]));

        var result = RunInventory();

        Assert.Equal(0, result.ExitCode);
        using var inventory = JsonDocument.Parse(result.Output);
        Assert.Contains("frozen-parser-run-references-unresolved",
            inventory.RootElement.GetProperty("diagnostics").EnumerateArray()
                .Select(item => item.GetProperty("code").GetString()));
        Assert.DoesNotContain("frozen-report-reference-unresolved",
            inventory.RootElement.GetProperty("diagnostics").EnumerateArray()
                .Select(item => item.GetProperty("code").GetString()));
        foreach (var citation in new[] { reportAssessment, inputAssessment, evidenceAssessment })
        {
            var artifact = Assert.Single(inventory.RootElement.GetProperty("artifacts").EnumerateArray(), item =>
                item.GetProperty("path").GetString() == citation.SourcePath);
            Assert.Contains(reportId, artifact.GetProperty("retainingRecordIds").EnumerateArray()
                .Select(item => item.GetString()));
            Assert.Equal("Retained", artifact.GetProperty("disposition").GetString());
        }
    }

    [Theory]
    [InlineData("missing-required", "frozen-report-reference-unresolved")]
    [InlineData("missing-evidence-field", "frozen-report-reference-unresolved")]
    [InlineData("explicit-null", "frozen-report-reference-unresolved")]
    [InlineData("nested-malformed", "frozen-report-reference-unresolved")]
    [InlineData("unsupported-kind", "unknown-report-kind")]
    [InlineData("unresolved-id", "unknown-frozen-report-assessment")]
    public void InvalidOrUnresolvedReportCitationsKeepInventoryUnknown(string shape, string diagnosticCode)
    {
        const string reportId = "report-invalid-citation-shape";
        var report = CreateParsimonyReport(reportId,
            shape == "unresolved-id" ? ["assessment-not-in-store"] : [], []);
        var reportJson = JsonSerializer.Serialize(report, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var evidenceJson = CreateFrozenReportEvidence(
            shape == "unresolved-id" ? ["assessment-not-in-store"] : []);
        var kind = "parsimony";
        switch (shape)
        {
            case "missing-required":
                reportJson = "{\"reportId\":\"report-invalid-citation-shape\",\"findings\":[]," +
                             "\"text\":\"missing inputs\",\"notes\":[]}";
                evidenceJson = null!;
                break;
            case "missing-evidence-field":
                evidenceJson = RemoveJsonProperty(evidenceJson, "joinQuality");
                break;
            case "explicit-null":
                reportJson = SetJsonProperty(reportJson, "assessmentIds", null);
                break;
            case "nested-malformed":
                evidenceJson = SetJsonProperty(evidenceJson, "unknownInputField", JsonValue.Create("unknown"), "inputs");
                break;
            case "unsupported-kind":
                kind = "future-parsimony";
                break;
        }
        RecordReport(reportId, kind, null, reportJson, evidenceJson);

        var result = RunInventory();

        Assert.Equal(0, result.ExitCode);
        using var inventory = JsonDocument.Parse(result.Output);
        Assert.False(inventory.RootElement.GetProperty("referenceDomainComplete").GetBoolean());
        Assert.False(inventory.RootElement.TryGetProperty("logicalBytes", out _));
        Assert.Contains(inventory.RootElement.GetProperty("diagnostics").EnumerateArray(), item =>
            item.GetProperty("code").GetString() == diagnosticCode);
        Assert.All(inventory.RootElement.GetProperty("artifacts").EnumerateArray(), item =>
            Assert.Equal("Unknown", item.GetProperty("disposition").GetString()));
    }

    [Fact]
    public void AssessmentRepositoryProducerInvocationEvidenceWithOptionalRetryIsRecognized()
    {
        var token = CreateBaselineToken();
        RecordBaseline(token);
        var withoutRetry = RecordProducedInvocation("producer-no-retry", token, withRetry: false);
        var withRetry = RecordProducedInvocation("producer-with-retry", token, withRetry: true);

        var result = RunInventory();

        Assert.Equal(0, result.ExitCode);
        using var inventory = JsonDocument.Parse(result.Output);
        Assert.True(inventory.RootElement.GetProperty("referenceDomainComplete").GetBoolean());
        Assert.True(inventory.RootElement.TryGetProperty("logicalBytes", out _));
        Assert.DoesNotContain(inventory.RootElement.GetProperty("diagnostics").EnumerateArray(), item =>
            item.GetProperty("code").GetString() == "malformed-invocation-evidence");
        foreach (var produced in new[] { withoutRetry, withRetry })
        {
            var source = Assert.Single(inventory.RootElement.GetProperty("artifacts").EnumerateArray(), item =>
                item.GetProperty("path").GetString() == produced.SourcePath);
            Assert.Contains(produced.InvocationId, source.GetProperty("ownerInvocationIds").EnumerateArray()
                .Select(item => item.GetString()));
            Assert.Equal("Retained", source.GetProperty("disposition").GetString());
        }
    }

    [Fact]
    public void MalformedProducerRetryMemberLeavesItsSourceUnknown()
    {
        var token = CreateBaselineToken();
        RecordBaseline(token);
        var produced = RecordProducedInvocation("producer-malformed-retry", token, withRetry: true);
        var evidenceJson = ReadInvocationEvidence(produced.InvocationId);
        var evidence = JsonNode.Parse(evidenceJson)!.AsObject();
        evidence["StoppedWordRetry"]!.AsObject()["FutureRetryMember"] = "unknown";
        WriteInvocationEvidence(produced.InvocationId, evidence.ToJsonString());

        var result = RunInventory();

        Assert.Equal(0, result.ExitCode);
        using var inventory = JsonDocument.Parse(result.Output);
        Assert.False(inventory.RootElement.GetProperty("referenceDomainComplete").GetBoolean());
        Assert.False(inventory.RootElement.TryGetProperty("logicalBytes", out _));
        Assert.Contains(inventory.RootElement.GetProperty("diagnostics").EnumerateArray(), item =>
            item.GetProperty("code").GetString() == "malformed-invocation-evidence");
        var source = Assert.Single(inventory.RootElement.GetProperty("artifacts").EnumerateArray(), item =>
            item.GetProperty("path").GetString() == produced.SourcePath);
        Assert.Empty(source.GetProperty("ownerInvocationIds").EnumerateArray());
        Assert.Equal("Unknown", source.GetProperty("disposition").GetString());
    }

    [Fact]
    public void UnknownInvocationEvidenceFieldsLeaveOwnedPathsUncertain()
    {
        var invocationId = "inv-unknown-shape";
        var sourcePath = Path.Combine(_root, "assessment-sources", new string('a', 64) + ".fwdata");
        RecordInvocation(_store, _project, "inventory-test", invocationId, sourcePath);
        using (var database = MotifDatabase.OpenOwned(_store,
                   new ProjectLocator(_project, "inventory-test"), MotifSchema.CurrentSchema,
                   MotifProductVersion.Current))
        using (var connection = database.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            var evidence = JsonNode.Parse(JsonSerializer.Serialize(CreateEvidence(invocationId, sourcePath)))!
                .AsObject();
            evidence["FuturePath"] = "/unknown";
            command.CommandText = "UPDATE AssessmentInvocations SET EvidenceJson = $evidence WHERE InvocationId = $id;";
            command.Parameters.AddWithValue("$evidence", evidence.ToJsonString());
            command.Parameters.AddWithValue("$id", invocationId);
            command.ExecuteNonQuery();
        }
        var before = SnapshotTree(_root);

        var result = RunInventory();

        Assert.Equal(0, result.ExitCode);
        using var inventory = JsonDocument.Parse(result.Output);
        Assert.False(inventory.RootElement.GetProperty("referenceDomainComplete").GetBoolean());
        Assert.Contains(inventory.RootElement.GetProperty("diagnostics").EnumerateArray(), item =>
            item.GetProperty("code").GetString() == "malformed-invocation-evidence");
        var source = Assert.Single(inventory.RootElement.GetProperty("artifacts").EnumerateArray(), item =>
            item.GetProperty("path").GetString() == sourcePath);
        Assert.Equal("Unknown", source.GetProperty("disposition").GetString());
        Assert.Empty(source.GetProperty("ownerInvocationIds").EnumerateArray());
        Assert.Equal(before, SnapshotTree(_root));
    }

    [Fact]
    public void UnresolvedAssessmentBaselineTokenMakesBaselineFamilyUncertain()
    {
        var token = CreateBaselineToken();
        RecordAssessmentBaselineToken("assessment-token-only", JsonSerializer.Serialize(token,
            MotifJson.CreateOptions()));

        var result = RunInventory();

        Assert.Equal(0, result.ExitCode);
        using var inventory = JsonDocument.Parse(result.Output);
        Assert.False(inventory.RootElement.GetProperty("referenceDomainComplete").GetBoolean());
        Assert.Contains(inventory.RootElement.GetProperty("diagnostics").EnumerateArray(), item =>
            item.GetProperty("code").GetString() == "unresolved-assessment-baseline-token");
        var baseline = Assert.Single(inventory.RootElement.GetProperty("artifacts").EnumerateArray(), item =>
            item.GetProperty("family").GetString() == "baselines");
        Assert.Equal("Unknown", baseline.GetProperty("disposition").GetString());
        Assert.Equal("reference-domain-incomplete", baseline.GetProperty("reasonCode").GetString());
    }

    [Fact]
    public void AssessmentBaselineTokenResolvesToTheMatchingRecordedBaseline()
    {
        var token = CreateBaselineToken();
        RecordAssessmentBaselineToken("assessment-baseline-citation", JsonSerializer.Serialize(token,
            MotifJson.CreateOptions()));
        RecordBaseline(token);

        var result = RunInventory();

        Assert.Equal(0, result.ExitCode);
        using var inventory = JsonDocument.Parse(result.Output);
        Assert.True(inventory.RootElement.GetProperty("referenceDomainComplete").GetBoolean());
        var baselinePath = Path.Combine(_root, "baselines", new string('b', 64), "source.fwdata");
        var baseline = Assert.Single(inventory.RootElement.GetProperty("artifacts").EnumerateArray(), item =>
            item.GetProperty("path").GetString() == baselinePath);
        Assert.Equal("Retained", baseline.GetProperty("disposition").GetString());
        Assert.Equal("assessment-history", baseline.GetProperty("reasonCode").GetString());
        Assert.Contains("assessment-baseline-citation", baseline.GetProperty("retainingRecordIds")
            .EnumerateArray().Select(item => item.GetString()));
    }

    [Fact]
    public void MalformedAssessmentBaselineTokenMakesBaselineFamilyUncertain()
    {
        RecordAssessmentBaselineToken("assessment-malformed-token", "not-json");
        RecordAssessmentBaselineToken("assessment-unknown-token-shape", """
            {"ProjectIdentity":"inventory-test","SemanticSnapshotDigest":"sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb","ProjectionVersion":"projection-v1","CapturedUtc":"2026-10-24T00:00:00Z","BundleDigest":"sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb","FuturePath":"/unknown"}
            """);

        var result = RunInventory();

        Assert.Equal(0, result.ExitCode);
        using var inventory = JsonDocument.Parse(result.Output);
        Assert.False(inventory.RootElement.GetProperty("referenceDomainComplete").GetBoolean());
        Assert.Contains(inventory.RootElement.GetProperty("diagnostics").EnumerateArray(), item =>
            item.GetProperty("code").GetString() == "malformed-assessment-baseline-token");
        var baseline = Assert.Single(inventory.RootElement.GetProperty("artifacts").EnumerateArray(), item =>
            item.GetProperty("family").GetString() == "baselines");
        Assert.Equal("Unknown", baseline.GetProperty("disposition").GetString());
    }

    [RequiresLinuxHardLinkIdentityFact]
    public void HardlinkedDatabaseAliasesListedTogetherMakeTheStoreDomainUncertain()
    {
        var alias = Path.Combine(_root, "project-alias.motif.db");
        CreateHardLink(_store, alias);
        File.WriteAllBytes(alias + ".use.lock", []);
        WriteManifest(referenceDomainComplete: true,
            (_store, _project, "inventory-test"), (alias, _project, "inventory-test"));
        var before = SnapshotTree(_root);

        var result = RunInventory();

        Assert.Equal(0, result.ExitCode);
        using var inventory = JsonDocument.Parse(result.Output);
        Assert.False(inventory.RootElement.GetProperty("referenceDomainComplete").GetBoolean());
        var stores = inventory.RootElement.GetProperty("stores").EnumerateArray().ToArray();
        Assert.Equal(2, stores.Length);
        Assert.All(stores, store =>
        {
            Assert.Equal("incomplete", store.GetProperty("status").GetString());
            Assert.Equal("physical-alias-refused", store.GetProperty("reasonCode").GetString());
            Assert.False(store.TryGetProperty("snapshotSha256", out _));
        });
        Assert.Contains(inventory.RootElement.GetProperty("diagnostics").EnumerateArray(), item =>
            item.GetProperty("code").GetString() == "store-physical-alias-refused");
        Assert.Equal(before, SnapshotTree(_root));
    }

    [RequiresLinuxHardLinkIdentityFact]
    public void AliasOnlyManifestCannotReadPastOriginalWriterLeaseAndCommittedWal()
    {
        var alias = Path.Combine(_root, "project-alias-only.motif.db");
        CreateHardLink(_store, alias);
        File.WriteAllBytes(alias + ".use.lock", []);
        WriteManifest(referenceDomainComplete: true, (alias, _project, "inventory-test"));
        using var writer = MotifDatabase.OpenOwned(_store, new ProjectLocator(_project, "inventory-test"),
            MotifSchema.CurrentSchema, MotifProductVersion.Current);
        using var connection = writer.OpenConnection();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA journal_mode=WAL; PRAGMA wal_autocheckpoint=0; " +
                                  "UPDATE MotifMetadata SET CreatedUtc = '2026-10-24T00:00:00.0000000+00:00' WHERE Id = 1;";
            command.ExecuteNonQuery();
        }
        Assert.True(File.Exists(_store + "-wal"));
        Assert.False(File.Exists(alias + "-wal"));
        var before = SnapshotTree(_root);

        var result = RunInventory();

        Assert.Equal(0, result.ExitCode);
        using var inventory = JsonDocument.Parse(result.Output);
        Assert.False(inventory.RootElement.GetProperty("referenceDomainComplete").GetBoolean());
        var store = Assert.Single(inventory.RootElement.GetProperty("stores").EnumerateArray());
        Assert.Equal("incomplete", store.GetProperty("status").GetString());
        Assert.Equal("physical-alias-refused", store.GetProperty("reasonCode").GetString());
        Assert.False(store.TryGetProperty("snapshotSha256", out _));
        Assert.Equal(before, SnapshotTree(_root));
    }

    [RequiresLinuxHardLinkIdentityFact]
    public void HardlinkedArtifactNamesAreUnknownWithoutDuplicatedByteAccounting()
    {
        var first = Path.Combine(_root, "assessment-sources", new string('a', 64) + ".fwdata");
        var alias = Path.Combine(_root, "assessment-sources", "hard-link-alias.fwdata");
        CreateHardLink(first, alias);
        var before = SnapshotTree(_root);

        var result = RunInventory();

        Assert.Equal(0, result.ExitCode);
        using var inventory = JsonDocument.Parse(result.Output);
        Assert.False(inventory.RootElement.GetProperty("inventoryComplete").GetBoolean());
        Assert.False(inventory.RootElement.TryGetProperty("logicalBytes", out _));
        var artifacts = inventory.RootElement.GetProperty("artifacts").EnumerateArray()
            .Where(item => item.GetProperty("path").GetString() is var path && (path == first || path == alias))
            .ToArray();
        Assert.Equal(2, artifacts.Length);
        Assert.All(artifacts, artifact =>
        {
            Assert.Equal("Unknown", artifact.GetProperty("disposition").GetString());
            Assert.Equal("physical-alias-refused", artifact.GetProperty("reasonCode").GetString());
            Assert.False(artifact.TryGetProperty("logicalBytes", out _));
        });
        Assert.Equal(before, SnapshotTree(_root));
    }

    [Fact]
    public void SymbolicLinksAreListedAsRefusedWithoutFollowingTheirTargets()
    {
        if (OperatingSystem.IsWindows()) return;
        var target = Path.Combine(_workerRoot, "outside.fwdata");
        var link = Path.Combine(_root, "assessment-sources", "linked.fwdata");
        File.WriteAllText(target, "outside target");
        File.CreateSymbolicLink(link, target);

        var result = RunInventory();

        Assert.Equal(0, result.ExitCode);
        using var inventory = JsonDocument.Parse(result.Output);
        var refused = Assert.Single(inventory.RootElement.GetProperty("artifacts").EnumerateArray(), item =>
            item.GetProperty("path").GetString() == link);
        Assert.Equal("Unknown", refused.GetProperty("disposition").GetString());
        Assert.Equal("link-refused", refused.GetProperty("reasonCode").GetString());
        Assert.False(refused.TryGetProperty("logicalBytes", out _));
        Assert.False(inventory.RootElement.GetProperty("inventoryComplete").GetBoolean());
        Assert.False(inventory.RootElement.TryGetProperty("logicalBytes", out _));
        Assert.Equal("outside target", File.ReadAllText(target));
    }

    private void WriteManifest(bool referenceDomainComplete,
        params (string DatabasePath, string ProjectPath, string ProjectIdentity)[] stores)
    {
        var entries = stores.Length == 0
            ? new[] { (DatabasePath: _store, ProjectPath: _project, ProjectIdentity: "inventory-test") }
            : stores;
        File.WriteAllText(StoresManifest, JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            referenceDomainComplete,
            stores = entries.Select(store => new
            {
                databasePath = store.DatabasePath,
                projectPath = store.ProjectPath,
                projectIdentity = store.ProjectIdentity,
            }).ToArray(),
        }));
    }

    private static void RecordInvocation(string storePath, string projectPath, string projectIdentity,
        string invocationId, string sourcePath)
    {
        using var database = MotifDatabase.OpenOwned(storePath,
            new ProjectLocator(projectPath, projectIdentity), MotifSchema.CurrentSchema, MotifProductVersion.Current);
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO AssessmentInvocations (InvocationId, EvidenceJson) VALUES ($id, $evidence);";
        command.Parameters.AddWithValue("$id", invocationId);
        command.Parameters.AddWithValue("$evidence", JsonSerializer.Serialize(CreateEvidence(invocationId, sourcePath)));
        command.ExecuteNonQuery();
    }

    private void RecordAssessmentBaselineToken(string assessmentId, string baselineToken)
    {
        using var database = MotifDatabase.OpenOwned(_store,
            new ProjectLocator(_project, "inventory-test"), MotifSchema.CurrentSchema, MotifProductVersion.Current);
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Assessments (AssessmentId, SelectionName, SelectionWordsJson, SelectionSha256,
                GrammarSourceSha256, SavedUtc, Assessor, Kind, ScopeJson, ScopeDigest, TokeniserName,
                TokeniserVersion, BaselineToken, SubjectKind)
            VALUES ($id, 'Default', '[]', 'selection', 'grammar', '2026-10-24T00:00:00Z',
                'PanGloss', 'ParseTime', '{}', 'scope', 'tokeniser', '1', $baseline, 'words');
            """;
        command.Parameters.AddWithValue("$id", assessmentId);
        command.Parameters.AddWithValue("$baseline", baselineToken);
        command.ExecuteNonQuery();
    }

    private void RecordAssessment(string assessmentId, string invocationId)
    {
        using var database = MotifDatabase.OpenOwned(_store,
            new ProjectLocator(_project, "inventory-test"), MotifSchema.CurrentSchema, MotifProductVersion.Current);
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Assessments (AssessmentId, SelectionName, SelectionWordsJson, SelectionSha256,
                GrammarSourceSha256, SavedUtc, Assessor, Kind, ScopeJson, ScopeDigest, TokeniserName,
                TokeniserVersion, BaselineToken, InvocationId, SubjectKind)
            VALUES ($id, 'Default', '[]', 'selection', 'grammar', '2026-10-24T00:00:00Z',
                'PanGloss', 'ParseTime', '{}', 'scope', 'tokeniser', '1', 'baseline', $invocation, 'words');
            """;
        command.Parameters.AddWithValue("$id", assessmentId);
        command.Parameters.AddWithValue("$invocation", invocationId);
        command.ExecuteNonQuery();
    }

    private void RecordReport(string reportId, string? kind, string? assessmentId, string reportJson,
        string? evidenceJson)
    {
        using var database = MotifDatabase.OpenOwned(_store,
            new ProjectLocator(_project, "inventory-test"), MotifSchema.CurrentSchema, MotifProductVersion.Current);
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Reports (ReportId, AssessmentId, ReportJson, EvidenceJson, Kind, CreatedUtc)
            VALUES ($id, $assessment, $report, $evidence, $kind, '2026-10-24T00:00:00Z');
            """;
        command.Parameters.AddWithValue("$id", reportId);
        command.Parameters.AddWithValue("$assessment", (object?)assessmentId ?? DBNull.Value);
        command.Parameters.AddWithValue("$report", reportJson);
        command.Parameters.AddWithValue("$evidence", (object?)evidenceJson ?? DBNull.Value);
        command.Parameters.AddWithValue("$kind", (object?)kind ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    private ProducerInvocation RecordProducedInvocation(string suffix, BaselineToken token, bool withRetry)
    {
        const string digest = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        var invocationId = "inv-" + suffix;
        var assessmentId = "assessment-" + suffix;
        var sourcePath = Path.Combine(_root, "assessment-sources", suffix + ".fwdata");
        var runPath = Path.Combine(_root, "assessment-runs", invocationId);
        Directory.CreateDirectory(runPath);
        File.WriteAllText(sourcePath, suffix);
        var wordsPath = Path.Combine(runPath, "words.txt");
        var tsvPath = Path.Combine(runPath, "results.tsv");
        var stderrPath = Path.Combine(runPath, "stderr.txt");
        File.WriteAllText(wordsPath, "words");
        File.WriteAllText(tsvPath, "results");
        File.WriteAllText(stderrPath, string.Empty);
        var invocation = new BatchInvocationEvidence(invocationId, sourcePath, digest, digest,
            wordsPath, digest, tsvPath, digest, stderrPath, digest, 1000, new StepCap(100), 1, false)
        {
            StoppedWordRetry = withRetry
                ? new StoppedWordRetry(10_000, 1, 1, 1, 0, "retry-" + invocationId,
                    digest, digest, digest, digest, "0\tword\t1000\t500\trecovered")
                : null,
        };
        var record = new NewAssessmentRecord(assessmentId, null, null, "PanGloss", "ParseTime", "{}", digest,
            new AssessmentSubject.Words(Selection.Create("producer-selection", ["word"]), "whitespace", "1"),
            JsonSerializer.Serialize(token, MotifJson.CreateOptions()), null, null, digest, null, null, null,
            new AssessmentRecordMaterial.Words([], null, null, [], invocation));
        using var database = MotifDatabase.OpenOwned(_store,
            new ProjectLocator(_project, "inventory-test"), MotifSchema.CurrentSchema, MotifProductVersion.Current);
        new AssessmentRepository(database).Record(record);
        return new ProducerInvocation(assessmentId, invocationId, sourcePath);
    }

    private string ReadInvocationEvidence(string invocationId)
    {
        using var database = MotifDatabase.OpenOwned(_store,
            new ProjectLocator(_project, "inventory-test"), MotifSchema.CurrentSchema, MotifProductVersion.Current);
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT EvidenceJson FROM AssessmentInvocations WHERE InvocationId = $id;";
        command.Parameters.AddWithValue("$id", invocationId);
        return Assert.IsType<string>(command.ExecuteScalar());
    }

    private void WriteInvocationEvidence(string invocationId, string evidenceJson)
    {
        using var database = MotifDatabase.OpenOwned(_store,
            new ProjectLocator(_project, "inventory-test"), MotifSchema.CurrentSchema, MotifProductVersion.Current);
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE AssessmentInvocations SET EvidenceJson = $evidence WHERE InvocationId = $id;";
        command.Parameters.AddWithValue("$evidence", evidenceJson);
        command.Parameters.AddWithValue("$id", invocationId);
        command.ExecuteNonQuery();
    }

    private (string AssessmentId, string SourcePath) RecordInventoryCitation(string suffix)
    {
        var invocationId = "inv-" + suffix;
        var assessmentId = "assessment-" + suffix;
        var sourcePath = Path.Combine(_root, "assessment-runs", invocationId, "source.fwdata");
        Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
        File.WriteAllText(sourcePath, suffix);
        RecordInvocation(_store, _project, "inventory-test", invocationId, sourcePath);
        RecordAssessment(assessmentId, invocationId);
        return (assessmentId, sourcePath);
    }

    private static string SetJsonProperty(string json, string name, JsonNode? value, string? parent = null)
    {
        var root = JsonNode.Parse(json)!;
        var target = parent is null ? root : root[parent]!;
        target[name] = value?.DeepClone();
        return root.ToJsonString(new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    private static string RemoveJsonProperty(string json, string name)
    {
        var root = JsonNode.Parse(json)!.AsObject();
        Assert.True(root.Remove(name));
        return root.ToJsonString(new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    private static ParsimonyReportResponse CreateParsimonyReport(string reportId,
        IReadOnlyList<string> reportAssessmentIds, IReadOnlyList<string> inputAssessmentIds)
    {
        const string digest = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        var inputs = new ParsimonyReportInputs("inventory-bundle",
            new BaselineToken("inventory-test", "sha256:" + digest, "projection-v1",
                "2026-10-24T00:00:00Z", "sha256:" + digest),
            "baseline", null, "model-fingerprint", new ParsimonyArtifactDigest(1, digest),
            new ParsimonyArtifactDigest(1, digest), null, null, inputAssessmentIds);
        return new ParsimonyReportResponse(reportId, inputs, [], "Synthetic Parsimony report.")
        {
            AssessmentIds = reportAssessmentIds,
            Notes = [],
        };
    }

    private void RecordBaseline(BaselineToken token)
    {
        var baselineRoot = Path.Combine(_root, "baselines", new string('b', 64));
        using var database = MotifDatabase.OpenOwned(_store,
            new ProjectLocator(_project, "inventory-test"), MotifSchema.CurrentSchema, MotifProductVersion.Current);
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Baselines (ProjectKey, ProjectIdentity, SemanticSnapshotDigest, ProjectionVersion,
                CapturedUtc, BundleDigest, CapturedHostSessionId, CapturedEditGeneration, RootDirectory,
                FwDataPath, PublishedUtc, SourceLastWriteUtc)
            VALUES ('inventory-project-key', $identity, $semantic, $projection, $captured, $bundle,
                NULL, NULL, $root, $fwdata, '2026-10-24T00:00:00.0000000+00:00',
                '2026-10-24T00:00:00.0000000+00:00');
            """;
        command.Parameters.AddWithValue("$identity", token.ProjectIdentity);
        command.Parameters.AddWithValue("$semantic", token.SemanticSnapshotDigest);
        command.Parameters.AddWithValue("$projection", token.ProjectionVersion);
        command.Parameters.AddWithValue("$captured", token.CapturedUtc);
        command.Parameters.AddWithValue("$bundle", token.BundleDigest);
        command.Parameters.AddWithValue("$root", baselineRoot);
        command.Parameters.AddWithValue("$fwdata", Path.Combine(baselineRoot, "source.fwdata"));
        command.ExecuteNonQuery();
    }

    private static BaselineToken CreateBaselineToken() => new("inventory-test",
        "sha256:" + new string('b', 64), "projection-v1", "2026-10-24T00:00:00Z",
        "sha256:" + new string('b', 64));

    private static void CreateHardLink(string existingPath, string linkPath)
    {
        Assert.True(OperatingSystem.IsLinux());
        if (CreateLinuxHardLink(existingPath, linkPath) != 0)
        {
            var error = Marshal.GetLastPInvokeError();
            throw new IOException("Could not create the synthetic hard link.", new Win32Exception(error));
        }
    }

    [DllImport("libc", EntryPoint = "link", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern int CreateLinuxHardLink(string existingPath, string linkPath);

    private static BatchInvocationEvidence CreateEvidence(string invocationId, string sourcePath)
    {
        const string digest = "sha256:dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd";
        var runDirectory = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(sourcePath)!)!,
            "assessment-runs", invocationId);
        return new BatchInvocationEvidence(invocationId, sourcePath, BatchInvocationEvidence.DigestFile(sourcePath),
            digest, Path.Combine(runDirectory, "words.txt"), digest, Path.Combine(runDirectory, "results.tsv"), digest,
            Path.Combine(runDirectory, "stderr.txt"), digest, 100, new StepCap(1), 1, false);
    }

    private void AddCitations(string invocationId)
    {
        using var database = MotifDatabase.OpenOwned(_store, new ProjectLocator(_project, "inventory-test"),
            MotifSchema.CurrentSchema, MotifProductVersion.Current);
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA foreign_keys = OFF;
            INSERT INTO Assessments (AssessmentId, SelectionName, SelectionWordsJson, SelectionSha256,
                GrammarSourceSha256, SavedUtc, Assessor, Kind, ScopeJson, ScopeDigest, TokeniserName,
                TokeniserVersion, BaselineToken, InvocationId, SubjectKind)
            VALUES ('assessment-cited', 'Default', '[]', 'selection', 'grammar', '2026-10-24T00:00:00Z',
                'PanGloss', 'ParseTime', '{}', 'scope', 'tokeniser', '1', 'baseline', $invocation, 'words');
            UPDATE MotifMetadata SET CurrentAssessmentId = 'assessment-cited' WHERE Id = 1;
            INSERT INTO AssessmentPins (AssessmentId, PinnedBy, PinnedUtc)
            VALUES ('assessment-cited', 'inventory-test', '2026-10-24T00:00:00Z');
            INSERT INTO Reports (ReportId, AssessmentId, ReportJson, Kind, CreatedUtc)
            VALUES ('report-cited', 'assessment-cited', '{}', 'coverage', '2026-10-24T00:00:00Z');
            INSERT INTO Reports (ReportId, ReportJson, EvidenceJson, Kind, CreatedUtc)
            VALUES ('report-frozen', $report, $frozen, 'parsimony', '2026-10-24T00:00:00Z');
            INSERT INTO ProposalDifferences (DifferenceId, ProposalId, TrialJobId, BeforeAssessmentId,
                AfterAssessmentId, TextId, BaselineToken, SummaryJson, SummaryDigest, CreatedUtc)
            VALUES ('difference-cited', 'missing-proposal', 'trial-job', 'assessment-cited', 'assessment-cited',
                'text', 'baseline', '{}', 'summary', '2026-10-24T00:00:00Z');
            """;
        command.Parameters.AddWithValue("$invocation", invocationId);
        command.Parameters.AddWithValue("$frozen", CreateFrozenReportEvidence("assessment-cited"));
        command.Parameters.AddWithValue("$report", JsonSerializer.Serialize(
            CreateParsimonyReport("report-frozen", [], ["assessment-cited"]),
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        command.ExecuteNonQuery();
    }

    private static string CreateFrozenReportEvidence(string assessmentId)
        => CreateFrozenReportEvidence([assessmentId]);

    private static string CreateFrozenReportEvidence(IReadOnlyList<string> assessmentIds)
    {
        const string digest = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        var inputs = new ParsimonyReportInputs("inventory-bundle",
            new BaselineToken("inventory-test", "sha256:" + digest, "projection-v1",
                "2026-10-24T00:00:00Z", "sha256:" + digest),
            "baseline", null, "model-fingerprint", new ParsimonyArtifactDigest(1, digest),
            new ParsimonyArtifactDigest(1, digest), null, null, assessmentIds);
        return JsonSerializer.Serialize(new
        {
            inputs,
            grammarFactsPath = "grammar-facts.sqlite",
            evidencePath = "evidence.sqlite",
            findings = Array.Empty<object>(),
            joinQuality = (object?)null,
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    private static long FileInfoLength(string path) => new FileInfo(path).Length;

    private CliRun RunInventory(bool developerCommands = true)
    {
        var start = new ProcessStartInfo(BuildOutput.Cli)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add("store");
        start.ArgumentList.Add("artifacts");
        start.ArgumentList.Add("--root");
        start.ArgumentList.Add(_root);
        start.ArgumentList.Add("--stores");
        start.ArgumentList.Add(StoresManifest);
        start.ArgumentList.Add("--json");
        start.Environment[RunnerOptions.RootVariable] = _workerRoot;
        start.Environment["MOTIF_DEVELOPER_COMMANDS"] = developerCommands ? "1" : null;
        using var process = Process.Start(start)!;
        process.StandardInput.Close();
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        Assert.True(process.WaitForExit(60000), "The artifact inventory CLI did not exit within its bound.");
        return new CliRun(process.ExitCode, outputTask.GetAwaiter().GetResult(), errorTask.GetAwaiter().GetResult());
    }

    private static Dictionary<string, FileState> SnapshotTree(string root) =>
        Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .ToDictionary(path => Path.GetRelativePath(root, path), Capture, StringComparer.Ordinal);

    private static FileState Capture(string path)
    {
        if (Directory.Exists(path))
        {
            var directory = new DirectoryInfo(path);
            return new FileState(-1, directory.LastWriteTimeUtc.Ticks, string.Empty);
        }
        var info = new FileInfo(path);
        return new FileState(info.Length, info.LastWriteTimeUtc.Ticks,
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
        try { Directory.Delete(_workerRoot, recursive: true); } catch (IOException) { }
    }

    private sealed record FileState(long Length, long LastWriteUtcTicks, string Sha256);
    private sealed record ProducerInvocation(string AssessmentId, string InvocationId, string SourcePath);
    private sealed record CliRun(int ExitCode, string Output, string Error);
}

public sealed class RequiresLinuxHardLinkIdentityFactAttribute : FactAttribute
{
    public RequiresLinuxHardLinkIdentityFactAttribute()
    {
        if (!OperatingSystem.IsLinux()) Skip = "The current physical file identity gate is Linux-only.";
    }
}
