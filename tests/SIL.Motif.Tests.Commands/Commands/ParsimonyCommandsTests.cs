using System.Text.Json;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Parsimony;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker;
using SIL.Motif.Worker.Parsimony;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

public sealed class ParsimonyCommandsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), nameof(ParsimonyCommandsTests),
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void ShowReadsTheStoredParsimonyReportWithoutOpeningItsEvidenceFiles()
    {
        Directory.CreateDirectory(_root);
        var fwDataPath = Path.Combine(_root, "project.fwdata");
        File.WriteAllText(fwDataPath, "project marker");
        var project = new SIL.Motif.Contract.Projects.ProjectLocator(fwDataPath, "project");
        var reportId = "report/stored-parsimony";
        var response = new ParsimonyReportResponse(reportId, Inputs(), [], "Stored recommendation.")
        {
            Notes = [],
            JoinQuality = new ParsimonyJoinQuality("default-selection", false, 0, 0, 0, 0, 0, 0, 0,
                null, null, null, null, null),
        };
        var reportJson = JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        using (var database = MotifDatabase.OpenOwned(ProjectDatabaseCatalog.DatabasePathFor(project), project,
                   MotifSchema.CurrentSchema, new Version(1, 0)))
        {
            new ReportRepository(database).Save(new ReportRecord(reportId, null, null,
                reportJson, "{}", "parsimony", response.Text));
        }

        var shown = ParsimonyCommands.Show(new ShowParsimonyReportRequest(
            fwDataPath, "1.0", reportId));

        Assert.True(shown.Succeeded, shown.Refusal?.Message);
        Assert.Equal(reportId, shown.Value!.ReportId);
        Assert.Equal("Stored recommendation.", shown.Value.Text);
        Assert.Empty(shown.Value.AssessmentIds);
    }

    [Fact]
    public void EnqueueRefusesCataloguedMeasuresWhenTheirFixedQueryIsUnavailable()
    {
        const string measureId = "B-adhoc-is-slot-order";
        Assert.NotNull(MeasureCatalog.Find(measureId));
        Assert.True(MeasureRunner.Supports(measureId));
        var request = new EnqueueParsimonyReportRequest(
            Path.Combine(_root, "missing.fwdata"), "1.0", measureId);

        var result = ParsimonyCommands.Enqueue(request, _ => false);

        Assert.False(result.Succeeded);
        Assert.Equal("parsimony.measure-unavailable", result.Refusal!.Code);
    }

    [Fact]
    public void ReviewedNegativeRequiresTheExactHumanConfirmationBeforeOpeningAProject()
    {
        var result = ParsimonyCommands.ConfirmReviewedNegative(new ConfirmReviewedNegativeRequest(
            Path.Combine(_root, "missing.fwdata"), "1.0", "negative-case", "{}", "yes"));

        Assert.False(result.Succeeded);
        Assert.Equal("parsimony.negative-confirmation-required", result.Refusal!.Code);
        Assert.Contains(ParsimonyCommands.ReviewedNegativeConfirmationPhrase, result.Refusal.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void LatestRefusesWithNoReportYetWhenTheProjectHasNoParsimonyReport()
    {
        var fwDataPath = CreateProject("latest-empty");

        var latest = ParsimonyCommands.ReadLatest(new ReadLatestParsimonyReportRequest(fwDataPath, "1.0"));

        Assert.False(latest.Succeeded);
        Assert.Equal("parsimony.no-report", latest.Refusal!.Code);
        Assert.Equal("No Parsimony Report yet.", latest.Refusal.Message);
    }

    [Fact]
    public void LatestNamesTheNewestParsimonyReportAndItsBundleWithoutOpeningEvidence()
    {
        var fwDataPath = CreateProject("latest-newest");
        var project = new SIL.Motif.Contract.Projects.ProjectLocator(fwDataPath, "project");
        using (var database = MotifDatabase.OpenOwned(ProjectDatabaseCatalog.DatabasePathFor(project), project,
                   MotifSchema.CurrentSchema, new Version(1, 0)))
        {
            var repository = new ReportRepository(database);
            SaveParsimonyReport(repository, "report/older", "bundle/older", "2026-10-01T09:00:00.0000000+00:00");
            SaveParsimonyReport(repository, "report/newest", "bundle/newest", "2026-10-02T09:00:00.0000000+00:00");
            repository.Save(new ReportRecord("report/correctness", null, null, "{}", "{}", "correctness",
                "other kind", "2026-10-09T09:00:00.0000000+00:00"));
        }

        var latest = ParsimonyCommands.ReadLatest(new ReadLatestParsimonyReportRequest(fwDataPath, "1.0"));

        Assert.True(latest.Succeeded, latest.Refusal?.Message);
        Assert.Equal("report/newest", latest.Value!.ReportId);
        Assert.Equal("bundle/newest", latest.Value.BundleId);
    }

    [Fact]
    public void LatestRefusesAStoredReportWhoseIdentityDoesNotMatchItsRow()
    {
        var fwDataPath = CreateProject("latest-mismatch");
        var project = new SIL.Motif.Contract.Projects.ProjectLocator(fwDataPath, "project");
        var response = new ParsimonyReportResponse("report/inside", Inputs("bundle/x"), [], "text")
        {
            JoinQuality = new ParsimonyJoinQuality("default-selection", false, 0, 0, 0, 0, 0, 0, 0, null, null, null, null, null),
            Notes = [],
        };
        using (var database = MotifDatabase.OpenOwned(ProjectDatabaseCatalog.DatabasePathFor(project), project,
                   MotifSchema.CurrentSchema, new Version(1, 0)))
        {
            new ReportRepository(database).Save(new ReportRecord("report/row", null, null,
                JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web)), "{}",
                "parsimony", "text", "2026-10-02T09:00:00.0000000+00:00"));
        }

        var latest = ParsimonyCommands.ReadLatest(new ReadLatestParsimonyReportRequest(fwDataPath, "1.0"));

        Assert.False(latest.Succeeded);
        Assert.Equal("parsimony.report-damaged", latest.Refusal!.Code);
    }

    private string CreateProject(string name)
    {
        var directory = Path.Combine(_root, name);
        Directory.CreateDirectory(directory);
        var fwDataPath = Path.Combine(directory, "project.fwdata");
        File.WriteAllText(fwDataPath, "project marker");
        var project = new SIL.Motif.Contract.Projects.ProjectLocator(fwDataPath, "project");
        using (MotifDatabase.OpenOwned(ProjectDatabaseCatalog.DatabasePathFor(project), project,
                   MotifSchema.CurrentSchema, new Version(1, 0))) { }
        return fwDataPath;
    }

    private static void SaveParsimonyReport(ReportRepository repository, string reportId, string bundleId,
        string createdUtc)
    {
        var response = new ParsimonyReportResponse(reportId, Inputs(bundleId), [], "Stored recommendation.")
        {
            JoinQuality = new ParsimonyJoinQuality("default-selection", false, 0, 0, 0, 0, 0, 0, 0, null, null, null, null, null),
            Notes = [],
        };
        repository.Save(new ReportRecord(reportId, null, null,
            JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web)), "{}",
            "parsimony", response.Text, createdUtc));
    }

    [Fact]
    public void DamagedReportWithNullAssessmentIdsIsRefused()
    {
        AssertDamagedReportRefused(node => node["assessmentIds"] = null);
    }

    [Fact]
    public void DamagedReportWithNullInputsIsRefused()
    {
        AssertDamagedReportRefused(node => node["inputs"] = null);
    }

    [Fact]
    public void DamagedReportWithNullFindingsIsRefused()
    {
        AssertDamagedReportRefused(node => node["findings"] = null);
    }

    [Fact]
    public void DamagedReportWithNullFindingMemberIsRefused()
    {
        AssertDamagedReportRefused(node =>
            node["findings"]![0]!.AsObject()["evidenceRefs"] = null, [Finding()]);
    }

    [Fact]
    public void AStoredReportWithoutNotesIsDamaged()
    {
        AssertDamagedReportRefused(node => node["notes"] = null);
    }

    [Fact]
    public void AStoredReportFromBeforeNotesIsRefusedWithRerunGuidance()
    {
        var fwDataPath = CreateProject();
        var reportId = "report/damaged-parsimony";
        SaveReport(fwDataPath, reportId, ReportJson(node => node.Remove("notes"), null));

        var shown = ParsimonyCommands.Show(new ShowParsimonyReportRequest(fwDataPath, "1.0", reportId));

        Assert.False(shown.Succeeded);
        Assert.Equal("parsimony.report-damaged", shown.Refusal!.Code);
        Assert.EndsWith("Run the measure again for a new Report; this one cannot be read.", shown.Refusal.Message,
            StringComparison.Ordinal);
    }

    private void AssertDamagedReportRefused(Action<JsonObject> damage, ParsimonyFinding[]? findings = null)
    {
        var fwDataPath = CreateProject();
        var reportId = "report/damaged-parsimony";
        SaveReport(fwDataPath, reportId, ReportJson(damage, findings));

        var shown = ParsimonyCommands.Show(new ShowParsimonyReportRequest(fwDataPath, "1.0", reportId));
        Assert.False(shown.Succeeded);
        Assert.Equal("parsimony.report-damaged", shown.Refusal!.Code);

        var disposed = ParsimonyCommands.RecordDisposition(new RecordParsimonyDispositionFromFindingRequest(
            fwDataPath, "1.0", reportId, "finding-1", "keep", RecordTypeId, "draft"));
        Assert.False(disposed.Succeeded);
        Assert.Equal("parsimony.report-damaged", disposed.Refusal!.Code);
    }

    [Fact]
    public void EvidenceWithAnOlderSchemaIsRefusedWithRebuildGuidance()
    {
        Directory.CreateDirectory(_root);
        var evidencePath = Path.Combine(_root, "older-evidence.sqlite");
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection(
                   new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = evidencePath, Pooling = false }.ToString()))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA application_id={EvidenceSchema.ApplicationId}; " +
                $"PRAGMA user_version={EvidenceSchema.Version - 1};";
            command.ExecuteNonQuery();
        }

        var exception = Assert.Throws<InvalidDataException>(() => EvidenceWriter.Validate(evidencePath,
            "{}", new string('a', 64), "sha256:" + new string('3', 64)));
        Assert.Contains("Delete", exception.Message, StringComparison.Ordinal);
        Assert.Contains("run the measure again", exception.Message, StringComparison.Ordinal);
        Assert.EndsWith("Delete this Parsimony evidence and run the measure again; Motif rebuilds it.",
            exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EvidenceWithAnOlderTableLayoutIsRefusedWithRebuildGuidance()
    {
        const string bundleId = "bundle-older-layout";
        var fwDataPath = PublishOlderBundle(bundleId, "report-older-layout",
            JsonSerializer.Serialize(new { inputs = new { bundleId } }));

        var view = ParsimonyViewsCommand.View(new ReadParsimonyViewRequest(fwDataPath, "1.0",
            new ParsimonyNamedViewRequest(bundleId, "join-quality", new ParsimonyViewFilters())));

        Assert.False(view.Succeeded);
        Assert.Equal("parsimony.bundle-invalid", view.Refusal!.Code);
        Assert.EndsWith("Delete this Parsimony evidence and run the measure again; Motif rebuilds it.",
            view.Refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GroupFindingWithNullIdentityInStoredReportIsRefusedAsDamaged()
    {
        const string bundleId = "bundle-null-identity";
        const string reportId = "report/damaged-parsimony";
        var reportJson = ReportJson(node =>
        {
            node["inputs"]!["bundleId"] = bundleId;
            node["findings"]![0]!["attachesTo"]!["identity"] = null;
        }, [GroupFinding()]);
        var fwDataPath = PublishOlderBundle(bundleId, reportId, reportJson);

        var view = ParsimonyViewsCommand.View(new ReadParsimonyViewRequest(fwDataPath, "1.0",
            new ParsimonyNamedViewRequest(bundleId, "join-quality",
                new ParsimonyViewFilters(ReportId: reportId))));

        Assert.False(view.Succeeded);
        Assert.Equal("parsimony.report-damaged", view.Refusal!.Code);
        Assert.Contains("is damaged", view.Refusal.Message, StringComparison.Ordinal);
        Assert.Equal("available",
            StoredBundleState(new SIL.Motif.Contract.Projects.ProjectLocator(fwDataPath, "project"), bundleId));
    }

    [Fact]
    public void ViewNamingAnotherBundlesReportIsRefusedAndLeavesTheBundleAvailable()
    {
        const string bundleId = "bundle-intact";
        const string reportId = "report/damaged-parsimony";
        var reportJson = ReportJson(node =>
        {
            node["inputs"]!["bundleId"] = bundleId;
            node["inputs"]!["evidence"]!["sha256"] = new string('9', 64);
        }, [GroupFinding()]);
        var fwDataPath = PublishOlderBundle(bundleId, reportId, reportJson);

        var view = ParsimonyViewsCommand.View(new ReadParsimonyViewRequest(fwDataPath, "1.0",
            new ParsimonyNamedViewRequest(bundleId, "join-quality",
                new ParsimonyViewFilters(ReportId: reportId))));

        Assert.False(view.Succeeded);
        Assert.Equal("parsimony.report-bundle-mismatch", view.Refusal!.Code);
        Assert.Equal(FailureReason.Refused, view.Refusal.Reason);
        Assert.Equal("available",
            StoredBundleState(new SIL.Motif.Contract.Projects.ProjectLocator(fwDataPath, "project"), bundleId));
    }

    [Fact]
    public void ABundleHeldOpenExclusivelyIsBusyNotCorrupt()
    {
        const string bundleId = "bundle-held-open";
        var fwDataPath = PublishOlderBundle(bundleId, "report-held-open",
            JsonSerializer.Serialize(new { inputs = new { bundleId } }));
        var project = new SIL.Motif.Contract.Projects.ProjectLocator(fwDataPath, "project");
        var query = new ParsimonyNamedViewRequest(bundleId, "join-quality", new ParsimonyViewFilters());
        string factsPath;
        using (var database = MotifDatabase.OpenOwned(ProjectDatabaseCatalog.DatabasePathFor(project), project,
                   MotifSchema.CurrentSchema, new Version(1, 0)))
        {
            factsPath = new EvidenceArtifactRepository(database).Get(bundleId)!.GrammarFactsPath;
        }

        using (var held = new FileStream(factsPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var busy = ParsimonyViewsCommand.View(new ReadParsimonyViewRequest(fwDataPath, "1.0", query));

            Assert.False(busy.Succeeded);
            Assert.Equal("parsimony.bundle-busy", busy.Refusal!.Code);
            Assert.Equal(FailureReason.Busy, busy.Refusal.Reason);
            Assert.Equal("available", StoredBundleState(project, bundleId));
        }

        var afterRelease = ParsimonyViewsCommand.View(new ReadParsimonyViewRequest(fwDataPath, "1.0", query));
        Assert.NotEqual("parsimony.bundle-busy", afterRelease.Refusal?.Code);
        Assert.Equal("corrupt", StoredBundleState(project, bundleId));
    }

    [Fact]
    public void ALeaseLostBeforeTheQueryRefusesAsLeaseLostAndLeavesTheBundleAvailable()
    {
        const string bundleId = "bundle-lease-lost";
        var fwDataPath = PublishOlderBundle(bundleId, "report-lease-lost",
            JsonSerializer.Serialize(new { inputs = new { bundleId } }));
        var project = new SIL.Motif.Contract.Projects.ProjectLocator(fwDataPath, "project");
        using var leaseLoss = new CancellationTokenSource();
        leaseLoss.Cancel();

        var refused = ParsimonyViewsCommand.ViewWithLeaseSignal(new ReadParsimonyViewRequest(fwDataPath, "1.0",
            new ParsimonyNamedViewRequest(bundleId, "join-quality", new ParsimonyViewFilters())), leaseLoss.Token);

        Assert.False(refused.Succeeded);
        Assert.Equal("parsimony.lease-lost", refused.Refusal!.Code);
        Assert.Equal(FailureReason.Busy, refused.Refusal.Reason);
        Assert.Equal("available", StoredBundleState(project, bundleId));
    }

    private static string StoredBundleState(SIL.Motif.Contract.Projects.ProjectLocator project, string bundleId)
    {
        using var database = MotifDatabase.OpenOwned(ProjectDatabaseCatalog.DatabasePathFor(project), project,
            MotifSchema.CurrentSchema, new Version(1, 0));
        return new EvidenceArtifactRepository(database).Get(bundleId)!.State;
    }

    [Fact]
    public void AnUnclassifiedFailureRefusesWithoutMarkingTheBundle()
    {
        const string bundleId = "bundle-unclassified";
        var fwDataPath = PublishOlderBundle(bundleId, "report-unclassified",
            JsonSerializer.Serialize(new { inputs = new { bundleId } }));
        var project = new SIL.Motif.Contract.Projects.ProjectLocator(fwDataPath, "project");
        using (var database = MotifDatabase.OpenOwned(ProjectDatabaseCatalog.DatabasePathFor(project), project,
                   MotifSchema.CurrentSchema, new Version(1, 0)))
        using (var connection = database.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE ParsimonyBundles SET BaselineTokenJson='{' WHERE BundleId=$bundle;";
            command.Parameters.AddWithValue("$bundle", bundleId);
            command.ExecuteNonQuery();
        }

        var view = ParsimonyViewsCommand.View(new ReadParsimonyViewRequest(fwDataPath, "1.0",
            new ParsimonyNamedViewRequest(bundleId, "join-quality", new ParsimonyViewFilters())));

        Assert.False(view.Succeeded);
        Assert.Equal("parsimony.bundle-invalid", view.Refusal!.Code);
        Assert.Equal("available", StoredBundleState(project, bundleId));
    }

    private string PublishOlderBundle(string bundleId, string reportId, string reportJson)
    {
        var fwDataPath = CreateProject();
        var project = new SIL.Motif.Contract.Projects.ProjectLocator(fwDataPath, "project");
        var workerRoot = RunnerOptions.ResolveRoot();
        var projectKey = ProjectWorkspaceKey.Compute(project);
        var token = new BaselineToken("project-identity", "sha256:" + new string('1', 64), "projection-v1",
            "2026-10-05T00:00:00Z", "sha256:" + new string('2', 64));
        var modelFingerprint = "sha256:" + new string('3', 64);
        var materialKey = new string('4', 64);
        var publisher = new EvidenceArtifactPublisher(workerRoot, projectKey);
        var staging = publisher.CreateStagingDirectory(bundleId, token, modelFingerprint, materialKey);
        File.WriteAllText(Path.Combine(staging, "grammar-facts.sqlite"), "facts");
        WriteOlderEvidenceWithoutTables(Path.Combine(staging, "evidence.sqlite"));
        publisher.FlushAndClose(staging);
        var final = publisher.Publish(staging, bundleId);
        var facts = Path.Combine(final, "grammar-facts.sqlite");
        var evidence = Path.Combine(final, "evidence.sqlite");
        var baselineRoot = Path.Combine(workerRoot, ProjectWorkspaceKey.StorageSegment(projectKey), "baseline",
            "source");
        Directory.CreateDirectory(baselineRoot);
        var bundle = new ParsimonyBundleRecord(bundleId, JsonSerializer.Serialize(token,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)), final, facts, evidence, modelFingerprint,
            1, HashFile(facts), 1, HashFile(evidence), "2026-10-05T00:00:00Z", projectKey, token.BundleDigest[7..],
            materialKey, "available", null, baselineRoot);
        var report = new ReportRecord(reportId, null, null, reportJson, "{}", "parsimony", "stored report");

        using var database = MotifDatabase.OpenOwned(ProjectDatabaseCatalog.DatabasePathFor(project), project,
            MotifSchema.CurrentSchema, new Version(1, 0));
        new EvidenceArtifactRepository(database).Publish(bundle, report,
            requireCurrentBaseline: false, setCurrentBaseline: false);
        return fwDataPath;
    }

    private static void WriteOlderEvidenceWithoutTables(string path)
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection(
            new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA application_id={EvidenceSchema.ApplicationId}; " +
            $"PRAGMA user_version={EvidenceSchema.Version - 1};";
        command.ExecuteNonQuery();
    }

    private static string HashFile(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))
        .ToLowerInvariant();

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static ParsimonyReportInputs Inputs(string bundleId = "bundle/stored-parsimony") => new(
        bundleId,
        new BaselineToken("project-identity", "sha256:" + new string('1', 64), "projection-v1",
            "2026-10-05T00:00:00Z", "sha256:" + new string('2', 64)),
        "baseline", null, "sha256:" + new string('3', 64),
        new ParsimonyArtifactDigest(4, new string('4', 64)),
        new ParsimonyArtifactDigest(1, new string('5', 64)), null, null, []);

    private const string RecordTypeId = "AAAAAAAAAAAAAAAAAAAAAA";

    private string CreateProject()
    {
        Directory.CreateDirectory(_root);
        var fwDataPath = Path.Combine(_root, "project.fwdata");
        File.WriteAllText(fwDataPath, "project marker");
        return fwDataPath;
    }

    private static void SaveReport(string fwDataPath, string reportId, string reportJson)
    {
        var project = new SIL.Motif.Contract.Projects.ProjectLocator(fwDataPath, "project");
        using var database = MotifDatabase.OpenOwned(ProjectDatabaseCatalog.DatabasePathFor(project), project,
            MotifSchema.CurrentSchema, new Version(1, 0));
        new ReportRepository(database).Save(new ReportRecord(reportId, null, null,
            reportJson, "{}", "parsimony", "Stored recommendation."));
    }

    private static string ReportJson(Action<JsonObject>? damage, ParsimonyFinding[]? findings)
    {
        var response = new ParsimonyReportResponse("report/damaged-parsimony", Inputs(), findings ?? [],
            "Stored recommendation.")
        {
            Notes = [],
            JoinQuality = new ParsimonyJoinQuality("default-selection", false, 0, 0, 0, 0, 0, 0, 0,
                null, null, null, null, null),
        };
        var node = JsonSerializer.SerializeToNode(response, new JsonSerializerOptions(JsonSerializerDefaults.Web))!
            .AsObject();
        damage?.Invoke(node);
        return node.ToJsonString();
    }

    private static ParsimonyFinding GroupFinding() => Finding() with
    {
        AttachesTo = new ParsimonyFindingAttachment(ParsimonyAttachmentKind.Group, "entry/group", null,
            ParsimonyGroupKind.UnusedStatement),
    };

    private static ParsimonyFinding Finding() => new("finding-1", "measure-1", ParsimonyAxis.Parsimony,
        ParsimonyTier.Static, new ParsimonyFindingAttachment(ParsimonyAttachmentKind.Project, "project"), null,
        new ParsimonyMeasureNumber(1, 2, "case"),
        new ParsimonyMeasureThreshold(ParsimonyThresholdOperator.GreaterThan, 1.0, "v1"),
        "digest", [], "recipe-link", [], ParsimonyVerification.NotRun);
}
