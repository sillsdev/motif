using System.Security.Cryptography;
using System.Reflection;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SIL.LCModel;
using SIL.LCModel.DomainServices;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Parsimony;
using SIL.Motif.Host.Store;
using SIL.Motif.Host.Texts;
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

[Collection(LcmCacheTestCollection.Name)]
public sealed class ParsimonyBaselineReportTests : IDisposable
{
    private const string RootCategoryGuid = "20000000-0000-0000-0000-000000000001";
    private const string ChildCategoryGuid = "20000000-0000-0000-0000-000000000002";
    private const string AncestorTemplateGuid = "20000000-0000-0000-0000-000000000231";
    private const string ChildTemplateGuid = "20000000-0000-0000-0000-000000000232";
    private const string UnnamedTemplateGuid = "20000000-0000-0000-0000-000000000233";
    private const string PrefixTemplateGuid = "20000000-0000-0000-0000-000000000234";
    private const string PositiveAlternativeTemplateGuid = "20000000-0000-0000-0000-000000000235";
    private const string NegativeAlternativeTemplateGuid = "20000000-0000-0000-0000-000000000236";
    private const string AlternativeFailureTemplateOneGuid = "20000000-0000-0000-0000-000000000237";
    private const string AlternativeFailureTemplateTwoGuid = "20000000-0000-0000-0000-000000000238";
    private const string ChildSharedNumberTemplateGuid = "20000000-0000-0000-0000-000000000239";
    private const string AdhocSlotOrderLeftTemplateGuid = "20000000-0000-0000-0000-000000000260";
    private const string AdhocSlotOrderRightTemplateGuid = "20000000-0000-0000-0000-000000000261";
    private const string AdhocSlotOrderOtherSlotGuid = "20000000-0000-0000-0000-000000000262";
    private const string AdhocSlotOrderPrimarySlotGuid = "20000000-0000-0000-0000-000000000263";
    private const string AdhocSlotOrderCrossSideTemplateGuid = "20000000-0000-0000-0000-000000000264";
    private const string AdhocSlotOrderCrossPrimarySlotGuid = "20000000-0000-0000-0000-000000000265";
    private const string AdhocSlotOrderCrossOtherSlotGuid = "20000000-0000-0000-0000-000000000266";
    private const string AdhocSlotOrderUnorderedCrossSideTemplateGuid = "20000000-0000-0000-0000-000000000267";
    private const string AdhocSlotOrderUnorderedCrossPrimarySlotGuid = "20000000-0000-0000-0000-000000000268";
    private const string AdhocSlotOrderUnorderedCrossOtherSlotGuid = "20000000-0000-0000-0000-000000000269";
    private const string RequiredSlotGuid = "20000000-0000-0000-0000-000000000226";
    private const string NegativeSlotGuid = "20000000-0000-0000-0000-000000000227";
    private const string AlternativeRequiredSlotOneGuid = "20000000-0000-0000-0000-000000000228";
    private const string AlternativeRequiredSlotTwoGuid = "20000000-0000-0000-0000-000000000229";
    private const string AlternativeFillerSlotGuid = "20000000-0000-0000-0000-000000000230";
    private const string AcceptedPositiveAnalysisGuid = "20000000-0000-0000-0000-000000000241";
    private const string AlternativeBlockedAnalysisGuid = "20000000-0000-0000-0000-000000000245";
    private const string ContradictedAnalysisGuid = "20000000-0000-0000-0000-000000000243";
    private const string AdhocSlotOrderCounterexampleAnalysisGuid = "20000000-0000-0000-0000-000000000246";
    private const string AdhocSlotOrderCounterexampleWordformGuid = "20000000-0000-0000-0000-000000000255";
    private const string FillerlessAnalysisGuid = "20000000-0000-0000-0000-000000000244";
    private const string BroadEnvironmentGuid = "90000000-0000-0000-0000-000000000001";
    private const string BroadEnvironmentNaturalClassGuid = "90000000-0000-0000-0000-000000000002";
    private const string BroadEnvironmentTargetAllomorphGuid = "90000000-0000-0000-0000-000000000003";
    private readonly string _root = Path.Combine(Path.GetTempPath(), nameof(ParsimonyBaselineReportTests),
        Guid.NewGuid().ToString("N"));
    private readonly PristineProjectFixture _pristine;
    private readonly ProjectLocator _project;
    private readonly MotifDatabase _database;
    private readonly JobRepository _jobs;
    private readonly BaselineRepository _baselines;
    private readonly string _projectKey;
    private readonly BaselineToken _token;
    private SeededText _seededText = null!;

    public ParsimonyBaselineReportTests(PristineProjectFixture pristine)
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

    [Fact]
    public async Task BaselineFactsLeaveTheScratchDirectoryToTheInvoker()
    {
        await PublishBaselineAsync(_pristine);
        var invoker = new FactsRequestCaptureInvoker();
        var job = CreateJob("P-adhoc-duplicate");
        var handler = new ParsimonyJobHandler(_database, _baselines, _projectKey, _project,
            new RunnerOptions { Root = Path.Combine(_root, "worker") }, invoker);

        _ = await handler.RunAsync(Claim(job.JobId), CancellationToken.None);

        Assert.NotNull(invoker.FactsRequest);
        Assert.Null(invoker.FactsRequest!.ArtifactDirectory);
    }

    [Fact]
    public void AlternationBoundaryNormalizationUsesIdentityInsteadOfMarkerName()
    {
        var word = ParsimonyQuerySession.NormalizeBoundaryReference("boundary",
            LangProjectTags.kguidPhRuleWordBdry.ToString("D"), "#");
        var wordSyntax = ParsimonyQuerySession.NormalizeBoundaryReference("wordBoundary", null, null);
        var morpheme = ParsimonyQuerySession.NormalizeBoundaryReference("boundary",
            LangProjectTags.kguidPhRuleMorphBdry.ToString("D"), "+");
        var literalHash = ParsimonyQuerySession.NormalizeBoundaryReference("boundary",
            "90000000-0000-0000-0000-000000000003", "#");

        Assert.Equal(wordSyntax, word);
        Assert.Equal(("morphemeBoundary", null, "morpheme boundary"), morpheme);
        Assert.Equal(("boundary", "90000000-0000-0000-0000-000000000003", "#"), literalHash);
        Assert.NotEqual(word, literalHash);
    }

    public void Dispose()
    {
        _database.Dispose();
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public async Task BaselineFactsProduceStoredDuplicateFindingsWithoutChangingTheBaseline()
    {
        var publishedRoot = await PublishBaselineAsync(_pristine);
        var fwDataPath = Path.Combine(publishedRoot, NewLangProjFixture.ProjectName + ".fwdata");
        var sourceDigest = Sha256OfDirectory(publishedRoot);
        var liveDigest = Sha256Of(_project.FullFwDataPath);
        Assert.False(File.Exists(fwDataPath + ".lock"));
        Assert.False(File.Exists(_project.FullFwDataPath + ".lock"));

        var parserPath = FactsParser(_pristine.Seed);
        using var invoker = new SIL.Motif.Host.PanGloss.PanGlossInvoker(parserPath);
        var selection = SIL.Motif.Host.Corpus.Selection.Create("Default",
            [SeededProject.AnalysedWordForm, SeededProject.UnanalysedWordForm]);
        var scopeBinding = new ParsimonyScopeBinding(ParsimonyEvidenceScopeKind.DefaultSelection,
            new ParsimonySelectionSnapshot("Default", [_seededText.TextId], [], selection.Words,
                selection.Sha256));
        var job = CreateJob("P-adhoc-duplicate", scopeBinding);
        var handler = new ParsimonyJobHandler(_database, _baselines, _projectKey, _project,
            new RunnerOptions { Root = Path.Combine(_root, "worker"), ParserPath = parserPath }, invoker);
        var claim = Claim(job.JobId);
        var outcome = await handler.RunAsync(claim, CancellationToken.None);
        Assert.True(outcome!.Status == JobStatus.Completed, outcome.ResultJson);
        claim.Transition(outcome!.Status, outcome.Category, outcome.ResultJson);

        Assert.Equal(JobStatus.Completed, _jobs.Get(job.JobId)!.Status);
        using var result = JsonDocument.Parse(outcome.ResultJson!);
        var reportId = result.RootElement.GetProperty("reportId").GetString()!;
        var report = Assert.IsType<ReportRecord>(new ReportRepository(_database).Get(reportId));
        var response = JsonSerializer.Deserialize<ParsimonyReportResponse>(report.ReportJson,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        Assert.Equal("parsimony", report.Kind);
        Assert.Equal(ParsimonyEvidenceScopeKind.DefaultSelection, response.Inputs.EvidenceScope);
        Assert.NotNull(response.JoinQuality);
        Assert.True(response.JoinQuality.ScopeAvailable);
        Assert.Equal(2, response.JoinQuality.ProjectWordforms);
        Assert.Equal(1, response.JoinQuality.ProjectJudgedWordforms);
        Assert.Equal(1, response.JoinQuality.ProjectApprovedReadings);
        Assert.Equal(2, response.JoinQuality.ScopeForms);
        Assert.Equal(2, response.JoinQuality.ScopeWordforms);
        Assert.Equal(1, response.JoinQuality.ScopeApprovedReadings);
        Assert.Equal(2, response.JoinQuality.ScopeLexemes);
        Assert.Equal(3, response.JoinQuality.ScopeTextOccurrences);
        Assert.Empty(response.AssessmentIds);
        Assert.Empty(response.Inputs.AssessmentIds);
        Assert.Equal(2, response.Findings.Count);
        Assert.All(response.Findings, finding =>
        {
            Assert.Equal(2, finding.Number.Numerator);
            Assert.Equal(3, finding.Number.Denominator);
            Assert.Contains("Loader state is unknown for 1 enabled prohibition(s); they were excluded.",
                finding.Limitations);
        });
        Assert.DoesNotContain(response.Findings, finding => finding.AttachesTo.Identity == "00000000-0000-0000-0000-000000000003");

        var bundle = new EvidenceArtifactRepository(_database).Get(response.Inputs.BundleId)!;
        var artifacts = new EvidenceArtifactRepository(_database);
        ParsimonyMeasureResult originalDuplicateResult;
        using (var originalSession = new ParsimonyQuerySession(bundle.GrammarFactsPath, bundle.EvidencePath,
                   response.Inputs))
            originalDuplicateResult = MeasureRunner.Execute("P-adhoc-duplicate", originalSession, bundle.BundleId);

        var unrelatedTextEvidence = Path.Combine(_root, "unrelated-text-edit.sqlite");
        File.Copy(bundle.EvidencePath, unrelatedTextEvidence);
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
               {
                   DataSource = Path.GetFullPath(unrelatedTextEvidence),
                   Mode = SqliteOpenMode.ReadWrite,
                   Pooling = false,
               }.ToString()))
        {
            connection.Open();
            using var updateText = connection.CreateCommand();
            updateText.CommandText = "UPDATE texts SET title = title || ' unrelated edit';";
            Assert.Equal(1, updateText.ExecuteNonQuery());
        }

        var unrelatedTextInputs = new ParsimonyReportInputs(response.Inputs.BundleId,
            response.Inputs.BaselineToken, response.Inputs.InputKind, response.Inputs.Candidate,
            response.Inputs.ModelFingerprint, response.Inputs.GrammarFacts,
            new ParsimonyArtifactDigest(bundle.EvidenceSchemaVersion, Sha256Of(unrelatedTextEvidence)),
            response.Inputs.SelectionSha256, response.Inputs.ExpectationRevisionSha256,
            response.Inputs.AssessmentIds, response.Inputs.EvidenceScope,
            response.Inputs.RetirementExpectationTranslation);
        ParsimonyMeasureResult editedTextDuplicateResult;
        using (var editedTextSession = new ParsimonyQuerySession(bundle.GrammarFactsPath, unrelatedTextEvidence,
                   unrelatedTextInputs))
            editedTextDuplicateResult = MeasureRunner.Execute("P-adhoc-duplicate", editedTextSession, bundle.BundleId);
        Assert.NotEqual(response.Inputs.Evidence.Sha256, unrelatedTextInputs.Evidence.Sha256);
        Assert.Equal(originalDuplicateResult.Findings.Select(finding => finding.EvidenceDigest),
            editedTextDuplicateResult.Findings.Select(finding => finding.EvidenceDigest));

        Assert.DoesNotContain("job:" + job.JobId, artifacts.GetPinReasons(bundle.BundleId));
        var bundleDirectory = bundle.EvidenceDirectory;
        var expectedDirectory = ParsimonyArtifactPath.BundleDirectory(Path.Combine(_root, "worker"), _projectKey,
            bundle.BaselineDigest, bundle.ModelFingerprint, bundle.MaterialKey, bundle.InputKind,
            bundle.CandidateIdentity, bundle.BundleId);
        Assert.Equal(Path.GetFullPath(expectedDirectory), Path.GetFullPath(bundleDirectory));
        var factsPath = Path.Combine(bundleDirectory, "grammar-facts.sqlite");
        Assert.Equal(new[] { "evidence.sqlite", "grammar-facts.sqlite" },
            Directory.EnumerateFiles(bundleDirectory).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.Equal("allomorphCoOccurrence", ReadText(factsPath,
            "SELECT subject_kind FROM load_fact WHERE subject_guid='00000000-0000-0000-0000-000000000001';"));
        Assert.Equal("{\"identity\":{\"guid\":\"00000000-0000-0000-0000-000000000001\",\"kind\":\"object\"}," +
            "\"kind\":\"allomorphCoOccurrence\"}", ReadText(factsPath,
                "SELECT subject_key FROM load_fact WHERE subject_guid='00000000-0000-0000-0000-000000000001';"));
        Assert.Equal("not_considered", ReadText(factsPath,
            "SELECT disposition FROM load_fact WHERE subject_guid='00000000-0000-0000-0000-000000000005';"));
        Assert.Equal(1, CountRows(Path.Combine(bundleDirectory, "evidence.sqlite"), "texts"));
        Assert.Equal(2, CountRows(Path.Combine(bundleDirectory, "evidence.sqlite"), "wordforms"));
        Assert.Equal(1, CountRows(Path.Combine(bundleDirectory, "evidence.sqlite"), "analyses"));
        File.WriteAllText(bundle.EvidencePath, "corrupt evidence");
        Assert.Throws<InvalidDataException>(() => new ParsimonyQuerySession(bundle.GrammarFactsPath,
            bundle.EvidencePath, response.Inputs));
        Assert.Equal(report.RenderedText, new ReportRepository(_database).Get(reportId)!.RenderedText);
        Assert.Equal(sourceDigest, Sha256OfDirectory(publishedRoot));
        Assert.Equal(liveDigest, Sha256Of(_project.FullFwDataPath));
        Assert.False(File.Exists(fwDataPath + ".lock"));
        Assert.False(File.Exists(_project.FullFwDataPath + ".lock"));
        Assert.Equal(new[] { "describe", "import", "facts" }, FakeParser.Invocations(parserPath));
    }

    [Fact]
    public async Task MissingBaselineFailsExplicitlyBeforeInvokingTheParser()
    {
        var parserPath = FakeParser.CopyRecordingInvocations(Path.Combine(_root, "parser-without-baseline"));
        using var invoker = new SIL.Motif.Host.PanGloss.PanGlossInvoker(parserPath);
        var job = CreateJob("P-adhoc-duplicate");
        var handler = new ParsimonyJobHandler(_database, _baselines, _projectKey, _project,
            new RunnerOptions { Root = Path.Combine(_root, "worker-without-baseline"), ParserPath = parserPath }, invoker);

        var outcome = await handler.RunAsync(Claim(job.JobId), CancellationToken.None);

        Assert.Equal(JobStatus.Failed, outcome!.Status);
        Assert.Equal(JobFailureCategory.Semantic, outcome.Category);
        Assert.Contains("No Baseline is recorded", outcome.ResultJson, StringComparison.Ordinal);
        Assert.Empty(FakeParser.Invocations(parserPath));
    }

    [Fact]
    public async Task ReplacingTheParserBetweenImportAndFactsRefuses()
    {
        await PublishBaselineAsync(_pristine);
        var parserPath = FactsParser(_pristine.Seed);
        using var real = new SIL.Motif.Host.PanGloss.PanGlossInvoker(parserPath);
        var invoker = new ParserReplacingInvoker(real, parserPath);
        var job = CreateJob("P-adhoc-duplicate");
        var workerRoot = Path.Combine(_root, "replaced-parser-worker");
        var handler = new ParsimonyJobHandler(_database, _baselines, _projectKey, _project,
            new RunnerOptions { Root = workerRoot, ParserPath = parserPath }, invoker);

        var outcome = await handler.RunAsync(Claim(job.JobId), CancellationToken.None);

        Assert.True(invoker.ReplacedBeforeFacts);
        Assert.Equal(JobStatus.Failed, outcome!.Status);
        Assert.Equal(JobFailureCategory.Infrastructure, outcome.Category);
        Assert.Contains("parser executable changed during the invocation", outcome.ResultJson, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFiles(workerRoot, "evidence.sqlite", SearchOption.AllDirectories));
        Assert.Empty(Directory.EnumerateFiles(workerRoot, "grammar-facts.sqlite", SearchOption.AllDirectories));
    }

    /// <summary>Replaces the parser's bytes between Import and Facts, as an operator would.</summary>
    private sealed class ParserReplacingInvoker(SIL.Motif.Host.PanGloss.IPanGlossInvoker inner, string parserPath)
        : SIL.Motif.Host.PanGloss.IPanGlossInvoker
    {
        public bool ReplacedBeforeFacts { get; private set; }

        public Task<SIL.Motif.Host.PanGloss.PanGlossOutcome> RunAsync(SIL.Motif.Host.PanGloss.PanGlossRequest request,
            string label, CancellationToken cancellationToken, TimeSpan? wallClockCap = null)
        {
            if (request is SIL.Motif.Host.PanGloss.PanGlossRequest.Facts)
            {
                // A trailing byte leaves the apphost runnable while changing its digest.
                using var stream = new FileStream(parserPath, FileMode.Append, FileAccess.Write);
                stream.WriteByte(0);
                ReplacedBeforeFacts = true;
            }
            return inner.RunAsync(request, label, cancellationToken, wallClockCap);
        }
    }

    [Fact]
    public async Task RegisteredStaticMeasuresUseExactFactsAndFullEligibilityCounts()
    {
        var publishedRoot = await PublishBaselineAsync(_pristine);
        var parserPath = FactsParser(_pristine.Seed);
        using var invoker = new SIL.Motif.Host.PanGloss.PanGlossInvoker(parserPath);
        var scopeBinding = new ParsimonyScopeBinding(ParsimonyEvidenceScopeKind.ProjectApproved, null);
        var job = CreateJob("P-allo-duplicate-form", scopeBinding);
        var handler = new ParsimonyJobHandler(_database, _baselines, _projectKey, _project,
            new RunnerOptions { Root = Path.Combine(_root, "measure-worker"), ParserPath = parserPath }, invoker);
        var outcome = await handler.RunAsync(Claim(job.JobId), CancellationToken.None);
        Assert.True(outcome!.Status == JobStatus.Completed, outcome.ResultJson);
        using var result = JsonDocument.Parse(outcome.ResultJson!);
        var reportId = result.RootElement.GetProperty("reportId").GetString()!;
        var storedReport = Assert.IsType<ReportRecord>(new ReportRepository(_database).Get(reportId));
        var reportResponse = JsonSerializer.Deserialize<ParsimonyReportResponse>(storedReport.ReportJson,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var persistedMeasureRun = Assert.Single(reportResponse.MeasureRuns);
        Assert.Equal("P-allo-duplicate-form", persistedMeasureRun.MeasureId);
        Assert.Equal(ParsimonyMeasureStatus.NotAvailable, persistedMeasureRun.Status);
        var bundleId = result.RootElement.GetProperty("bundleId").GetString()!;
        var bundle = new ParsimonyBundleRepository(_database).Get(bundleId)!;
        AddNamedMeasureFacts(bundle.GrammarFactsPath);

        var inputs = new ParsimonyReportInputs(bundle.BundleId, _token, "baseline", null,
            bundle.ModelFingerprint,
            new ParsimonyArtifactDigest(bundle.GrammarFactsSchemaVersion, Sha256Of(bundle.GrammarFactsPath)),
            new ParsimonyArtifactDigest(bundle.EvidenceSchemaVersion, bundle.EvidenceSha256),
            null, null, [], ParsimonyEvidenceScopeKind.ProjectApproved);
        using var session = new ParsimonyQuerySession(bundle.GrammarFactsPath, bundle.EvidencePath, inputs);
        var sessionConnection = SessionConnection(session);
        Assert.Equal(1L, ReadScalar(sessionConnection, "PRAGMA query_only;"));
        Assert.Equal(1L, ReadScalar(sessionConnection,
            "SELECT COUNT(*) FROM sqlite_temp_schema WHERE type='view' AND name='approved_morphs';"));
        using (var otherConnection = new SqliteConnection("Data Source=:memory:"))
        {
            otherConnection.Open();
            Assert.Equal(0L, ReadScalar(otherConnection,
                "SELECT COUNT(*) FROM sqlite_temp_schema WHERE type='view' AND name='approved_morphs';"));
        }
        Assert.Throws<ArgumentException>(() => ParsimonyViewsQuery.Execute(session,
            new ParsimonyNamedViewRequest(bundleId + "-other", "join-quality", new ParsimonyViewFilters())));
        Assert.Throws<ArgumentException>(() => MeasureRunner.Execute("P-allo-duplicate-form", session,
            bundleId + "-other"));

        var wrongModelInputs = new ParsimonyReportInputs(bundle.BundleId, _token, "baseline", null,
            "sha256:another-model", inputs.GrammarFacts, inputs.Evidence, null, null, [],
            ParsimonyEvidenceScopeKind.ProjectApproved);
        Assert.Throws<InvalidDataException>(() => new ParsimonyQuerySession(bundle.GrammarFactsPath,
            bundle.EvidencePath, wrongModelInputs));
        var candidateInputs = new ParsimonyReportInputs(bundle.BundleId, _token, "candidate", "candidate/one",
            bundle.ModelFingerprint, inputs.GrammarFacts, inputs.Evidence, null, null, [],
            ParsimonyEvidenceScopeKind.ProjectApproved);
        Assert.Throws<InvalidDataException>(() => new ParsimonyQuerySession(bundle.GrammarFactsPath,
            bundle.EvidencePath, candidateInputs));

        var duplicates = MeasureRunner.Execute("P-allo-duplicate-form", session, bundleId);
        Assert.Equal(ParsimonyMeasureStatus.Computed, duplicates.Run.Status);
        Assert.Equal(3, duplicates.Run.EligibleItems);
        Assert.Equal(1, duplicates.Run.FindingItems);
        Assert.Equal(1d / 3d, duplicates.Run.Rate);
        var duplicate = Assert.Single(duplicates.Findings);
        Assert.Equal(ParsimonyGroupKind.AllomorphDuplicateForms, duplicate.AttachesTo.GroupKind);
        Assert.Equal(2, duplicate.EvidenceRefs.Count);
        Assert.Contains(duplicate.Limitations,
            limitation => limitation.Contains("Allomorphs with different environment lists are excluded",
                StringComparison.Ordinal));
        var entryGuid = duplicate.GroupKey!.Split('/')[1];
        Assert.Equal("puᵀ", session.DescribeObject(entryGuid));
        Assert.All(duplicate.EvidenceRefs, reference =>
            Assert.Equal("puᵀ", session.DescribeObject(reference.Arguments.GetProperty("objectGuid").GetString()!)));
        Assert.Null(session.DescribeObject(Guid.NewGuid().ToString("D")));

        var statements = MeasureRunner.Execute("P-statement-unused", session, bundleId);
        Assert.Equal(ParsimonyMeasureStatus.Computed, statements.Run.Status);
        Assert.Equal(4, statements.Run.EligibleItems);
        Assert.Equal(4, statements.Run.FindingItems);
        Assert.Equal(1d, statements.Run.Rate);
        Assert.Contains(statements.Findings, finding => finding.AttachesTo.Identity.Contains(
            "10000000-0000-0000-0000-000000000022", StringComparison.Ordinal));
        Assert.Contains(statements.Findings, finding => finding.AttachesTo.Identity.Contains(
            "10000000-0000-0000-0000-000000000032", StringComparison.Ordinal));

        var firstPage = ParsimonyViewsQuery.Execute(session, new ParsimonyNamedViewRequest(bundleId,
            "statement-usage", new ParsimonyViewFilters(StatementKind: "environment"), 1));
        Assert.Equal(2, firstPage.Total);
        Assert.Equal(1, firstPage.Returned);
        Assert.True(firstPage.Truncated);
        var nextPage = ParsimonyViewsQuery.Execute(session, new ParsimonyNamedViewRequest(bundleId,
            "statement-usage", new ParsimonyViewFilters(StatementKind: "environment"), 1, firstPage.NextCursor));
        Assert.Equal(firstPage.Total, nextPage.Total);
        Assert.Equal(firstPage.ItemsDigest, nextPage.ItemsDigest);
        Assert.Equal(1, nextPage.Returned);
        var otherBundleInputs = new ParsimonyReportInputs(bundleId + "-other", _token, "baseline", null,
            bundle.ModelFingerprint, inputs.GrammarFacts, inputs.Evidence, null, null, [],
            ParsimonyEvidenceScopeKind.ProjectApproved);
        using var otherBundleSession = new ParsimonyQuerySession(bundle.GrammarFactsPath, bundle.EvidencePath,
            otherBundleInputs);
        Assert.Throws<ArgumentException>(() => ParsimonyViewsQuery.Execute(otherBundleSession,
            new ParsimonyNamedViewRequest(otherBundleInputs.BundleId, "statement-usage",
                new ParsimonyViewFilters(StatementKind: "environment"), 1, firstPage.NextCursor)));
        Assert.Throws<ArgumentException>(() => ParsimonyViewsQuery.Execute(session,
            new ParsimonyNamedViewRequest(bundleId + "-other", "statement-usage",
                new ParsimonyViewFilters(StatementKind: "environment"), 1, firstPage.NextCursor)));
        Assert.Throws<ArgumentException>(() => ParsimonyViewsQuery.Execute(session,
            new ParsimonyNamedViewRequest(bundleId, "unknown-view", new ParsimonyViewFilters())));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ParsimonyViewFilters>(
            "{\"ObjectGuid\":\"10000000-0000-0000-0000-000000000001\",\"sql\":\"DROP TABLE facts.environment\"}",
            MotifJson.CreateOptions()));
        Assert.Throws<ArgumentException>(() => ParsimonyViewsQuery.Execute(session,
            new ParsimonyNamedViewRequest(bundleId, "join-quality",
                new ParsimonyViewFilters(ObjectGuid: "10000000-0000-0000-0000-000000000001"))));
        Assert.Throws<KeyNotFoundException>(() => ParsimonyViewsQuery.Execute(session,
            new ParsimonyNamedViewRequest(bundleId, "allomorph-context",
                new ParsimonyViewFilters(ObjectGuid: "10000000-0000-0000-0000-000000000099"))));

        Assert.Empty(session.ReadApprovedWitnesses("m.morph_guid", "not-present"));
        var sidecars = new[] { bundle.GrammarFactsPath + "-wal", bundle.GrammarFactsPath + "-shm",
            bundle.GrammarFactsPath + "-journal", bundle.EvidencePath + "-wal", bundle.EvidencePath + "-shm",
            bundle.EvidencePath + "-journal" };
        Assert.All(sidecars, path => Assert.False(File.Exists(path), path));
        Assert.Throws<KeyNotFoundException>(() => ParsimonyViewsQuery.Execute(session,
            new ParsimonyNamedViewRequest(bundleId, "allomorph-context",
                new ParsimonyViewFilters(ObjectGuid: "00000000-0000-0000-0000-000000000001' OR 1=1 --"))));
        var parserCases = ParsimonyViewsQuery.Execute(session,
            new ParsimonyNamedViewRequest(bundleId, "parser-cases", new ParsimonyViewFilters()));
        Assert.Equal(ParsimonyMeasureStatus.NotAvailable, parserCases.Status);
        Assert.Null(parserCases.Total);
        Assert.Empty(parserCases.Rows);
        var unrequestedNegativeMeasure = MeasureRunner.Execute("R-word-negative-accepted", session, bundleId).Run;
        Assert.Equal(ParsimonyMeasureStatus.NotAvailable, unrequestedNegativeMeasure.Status);
        Assert.Null(unrequestedNegativeMeasure.EligibleItems);
        Assert.Null(unrequestedNegativeMeasure.Rate);

        var emptyFactsPath = Path.Combine(_root, "empty-allomorph-facts.sqlite");
        File.Copy(bundle.GrammarFactsPath, emptyFactsPath);
        using (var emptyFacts = new SqliteConnection(new SqliteConnectionStringBuilder
               { DataSource = emptyFactsPath, Pooling = false }.ToString()))
        {
            emptyFacts.Open();
            using var command = emptyFacts.CreateCommand();
            command.CommandText = "DELETE FROM allomorph_form;";
            command.ExecuteNonQuery();
        }
        var emptyInputs = new ParsimonyReportInputs(bundle.BundleId, _token, "baseline", null,
            bundle.ModelFingerprint,
            new ParsimonyArtifactDigest(bundle.GrammarFactsSchemaVersion, Sha256Of(emptyFactsPath)),
            inputs.Evidence, null, null, [], ParsimonyEvidenceScopeKind.ProjectApproved);
        using var emptySession = new ParsimonyQuerySession(emptyFactsPath, bundle.EvidencePath, emptyInputs);
        var emptyRun = MeasureRunner.Execute("P-allo-duplicate-form", emptySession, bundleId).Run;
        Assert.Equal(ParsimonyMeasureStatus.Computed, emptyRun.Status);
        Assert.Equal(0, emptyRun.EligibleItems);
        Assert.Equal(0, emptyRun.FindingItems);
        Assert.Null(emptyRun.Rate);

        var ruleFactsPath = Path.Combine(_root, "rule-context-facts.sqlite");
        File.Copy(bundle.GrammarFactsPath, ruleFactsPath);
        AddPhonologicalRuleFact(ruleFactsPath);
        var ruleInputs = new ParsimonyReportInputs(bundle.BundleId, _token, "baseline", null,
            bundle.ModelFingerprint,
            new ParsimonyArtifactDigest(bundle.GrammarFactsSchemaVersion, Sha256Of(ruleFactsPath)),
            inputs.Evidence, null, null, [], ParsimonyEvidenceScopeKind.ProjectApproved);
        using var ruleSession = new ParsimonyQuerySession(ruleFactsPath, bundle.EvidencePath, ruleInputs);
        var ruleUsage = ruleSession.ReadStatementUsage();
        Assert.Equal("defined-but-unreferenced", Assert.Single(ruleUsage, row => row.StatementGuid ==
            "10000000-0000-0000-0000-000000000021").Classification);
        var ruleRun = MeasureRunner.Execute("P-statement-unused", ruleSession, bundleId);
        var unusedEnvironment = Assert.Single(ruleRun.Findings, finding => finding.AttachesTo.Identity.Contains(
            "10000000-0000-0000-0000-000000000021", StringComparison.Ordinal));
        Assert.Equal("environment \"unused\"", ParsimonyReportProducer.DescribeFinding(ruleSession, unusedEnvironment));
    }

    [Fact]
    public async Task TemplateMeasuresUseAncestorAndAlternativeOrdersAndKeepSlotBlockingTextOnly()
    {
        await PublishBaselineAsync(_pristine);
        var parserPath = FactsParser(_pristine.Seed);
        using var invoker = new SIL.Motif.Host.PanGloss.PanGlossInvoker(parserPath);
        var scope = new ParsimonyScopeBinding(ParsimonyEvidenceScopeKind.ProjectApproved, null);
        var job = CreateJob("R-tmpl-precedence", scope);
        var handler = new ParsimonyJobHandler(_database, _baselines, _projectKey, _project,
            new RunnerOptions { Root = Path.Combine(_root, "template-measure-worker"), ParserPath = parserPath }, invoker);

        var outcome = await handler.RunAsync(Claim(job.JobId), CancellationToken.None);

        Assert.True(outcome!.Status == JobStatus.Completed, outcome.ResultJson);
        using var result = JsonDocument.Parse(outcome.ResultJson!);
        var bundleId = result.RootElement.GetProperty("bundleId").GetString()!;
        var bundle = new ParsimonyBundleRepository(_database).Get(bundleId)!;
        AddNamedMeasureFacts(bundle.GrammarFactsPath);
        AddTemplateMeasureFacts(bundle.GrammarFactsPath, bundle.EvidencePath);
        AddPositiveAndNegativeAlternativeTemplates(bundle.GrammarFactsPath, bundle.EvidencePath);
        var inputs = new ParsimonyReportInputs(bundle.BundleId, _token, "baseline", null,
            bundle.ModelFingerprint,
            new ParsimonyArtifactDigest(bundle.GrammarFactsSchemaVersion, Sha256Of(bundle.GrammarFactsPath)),
            new ParsimonyArtifactDigest(bundle.EvidenceSchemaVersion, Sha256Of(bundle.EvidencePath)), null, null,
            [], ParsimonyEvidenceScopeKind.ProjectApproved);
        using var session = new ParsimonyQuerySession(bundle.GrammarFactsPath, bundle.EvidencePath, inputs);

        var precedence = MeasureRunner.Execute("R-tmpl-precedence", session, bundleId);
        Assert.Equal(ParsimonyMeasureStatus.Computed, precedence.Run.Status);
        var orderAnswer = ParsimonyMorphFixtureBuilder.For(_pristine.Seed, "R-tmpl-precedence").Answer;
        Assert.Equal(orderAnswer.EligibleItems, precedence.Run.EligibleItems);
        Assert.Equal(orderAnswer.FindingItems, precedence.Run.FindingItems);
        Assert.Equal(orderAnswer.FindingTargets, precedence.Findings.Select(finding => finding.AttachesTo.Identity));
        Assert.Contains(ContradictedAnalysisGuid, orderAnswer.Witnesses);
        Assert.DoesNotContain(precedence.Findings, finding => finding.EvidenceRefs.Any(reference =>
            orderAnswer.Exclusions.Contains(reference.Arguments.TryGetProperty("objectGuid", out var id)
                ? id.GetString() ?? string.Empty : string.Empty, StringComparer.Ordinal)));
        Assert.Equal(1d / 3d, precedence.Run.Rate);
        Assert.Contains("min(1,1)/2=0.50", precedence.Run.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain(precedence.Findings, finding => finding.EvidenceRefs.Any(reference =>
            reference.View == "approved-morph-sequences" && reference.Arguments.GetProperty("objectGuid")
                .GetString() == AcceptedPositiveAnalysisGuid));
        var contradiction = Assert.Single(precedence.Findings);
        Assert.Equal(AncestorTemplateGuid, contradiction.AttachesTo.Identity);
        Assert.Contains(contradiction.EvidenceRefs,
            reference => reference.View == "approved-morph-sequences" &&
                reference.Arguments.GetProperty("objectGuid").GetString() == orderAnswer.Witnesses.Single());
        Assert.Equal("Ancestor order", session.DescribeObject(AncestorTemplateGuid));
        Assert.Equal("unnamed template in Ancestor category", session.DescribeObject(UnnamedTemplateGuid));
        Assert.Contains("cd: root (root) + c (C) + d (D)", session.DescribeObject(ContradictedAnalysisGuid),
            StringComparison.Ordinal);
        var templateView = ParsimonyViewsQuery.Execute(session, new ParsimonyNamedViewRequest(bundleId,
            "template-order", new ParsimonyViewFilters(ObjectGuid: PrefixTemplateGuid)));
        var prefixTemplate = Assert.IsType<ParsimonyTemplateViewRow>(Assert.Single(templateView.Rows));
        Assert.Equal(new[] { "20000000-0000-0000-0000-000000000221",
                "20000000-0000-0000-0000-000000000222" },
            prefixTemplate.Slots.Select(slot => slot.SlotGuid));

        var sequenceView = ParsimonyViewsQuery.Execute(session, new ParsimonyNamedViewRequest(bundleId,
            "approved-morph-sequences", new ParsimonyViewFilters(ObjectGuid: ContradictedAnalysisGuid)));
        var sequence = Assert.IsType<ParsimonyApprovedMorphSequenceViewRow>(Assert.Single(sequenceView.Rows));
        Assert.Equal("cd", sequence.Wordform);
        Assert.Equal(new[] { "root", "c", "d" }, sequence.Morphs.Select(morph => morph.Forms["qaa"]));
        Assert.Equal("C", sequence.Morphs[1].Glosses["qaa"]);

        var slotView = ParsimonyViewsQuery.Execute(session, new ParsimonyNamedViewRequest(bundleId, "slot-context",
            new ParsimonyViewFilters(ObjectGuid: RequiredSlotGuid)));
        var slot = Assert.IsType<ParsimonySlotContextViewRow>(Assert.Single(slotView.Rows));
        Assert.False(slot.Optional);
        Assert.Contains(slot.TemplateUses, item => item.TemplateGuid == AncestorTemplateGuid);
        Assert.Contains(slot.TemplateUses, item => item.TemplateGuid == ChildSharedNumberTemplateGuid &&
            item.CategoryGuid == ChildCategoryGuid);

        var blocking = MeasureRunner.Execute("R-slot-blocking", session, bundleId);
        Assert.Equal(ParsimonyMeasureStatus.Computed, blocking.Run.Status);
        var obligationAnswer = ParsimonyMorphFixtureBuilder.For(_pristine.Seed, "R-slot-blocking").Answer;
        Assert.Equal(obligationAnswer.EligibleItems, blocking.Run.EligibleItems);
        Assert.Equal(obligationAnswer.FindingItems, blocking.Run.FindingItems);
        Assert.Contains(FillerlessAnalysisGuid, obligationAnswer.Witnesses);
        Assert.Contains(AlternativeBlockedAnalysisGuid, obligationAnswer.Witnesses);
        Assert.Equal(obligationAnswer.FindingTargets.Order(StringComparer.Ordinal),
            blocking.Findings.Select(finding => finding.AttachesTo.Identity).Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal));
        Assert.DoesNotContain(blocking.Findings, finding => finding.EvidenceRefs.Any(reference =>
            reference.View == "approved-morph-sequences" && reference.Arguments.GetProperty("objectGuid")
                .GetString() == AcceptedPositiveAnalysisGuid));
        var blockedSlot = Assert.Single(blocking.Findings,
            finding => finding.AttachesTo.Identity == RequiredSlotGuid);
        Assert.Equal(RequiredSlotGuid, blockedSlot.AttachesTo.Identity);
        Assert.Equal(1, blockedSlot.Number.Numerator);
        Assert.Equal(2, blockedSlot.Number.Denominator);
        Assert.Equal(ParsimonyVerification.Inconclusive, blockedSlot.Verification);
        Assert.Contains(blockedSlot.Limitations,
            item => item.Contains("does not recommend making the slot optional", StringComparison.Ordinal));
        Assert.Contains(blockedSlot.EvidenceRefs, reference => reference.View == "slot-context");
        Assert.Contains(blockedSlot.EvidenceRefs,
            reference => reference.View == "approved-morph-sequences" &&
                reference.Arguments.GetProperty("objectGuid").GetString() == FillerlessAnalysisGuid);

        var ambiguousFindings = blocking.Findings.Where(finding =>
            finding.AttachesTo.Identity is AlternativeRequiredSlotOneGuid or AlternativeRequiredSlotTwoGuid).ToArray();
        Assert.Equal(2, ambiguousFindings.Length);
        foreach (var finding in ambiguousFindings)
        {
            Assert.Contains(finding.Limitations, item => item.Contains("joint or ambiguous", StringComparison.Ordinal));
            Assert.Contains(finding.Limitations, item => item.Contains("Alternative A", StringComparison.Ordinal));
            Assert.Contains(finding.Limitations, item => item.Contains("Alternative B", StringComparison.Ordinal));
            Assert.Contains(finding.Limitations, item => item.Contains("Required One", StringComparison.Ordinal));
            Assert.Contains(finding.Limitations, item => item.Contains("Required Two", StringComparison.Ordinal));
            Assert.Contains(finding.EvidenceRefs, reference => reference.View == "approved-morph-sequences" &&
                reference.Arguments.GetProperty("objectGuid").GetString() == AlternativeBlockedAnalysisGuid);
            Assert.Contains(finding.EvidenceRefs, reference => reference.View == "slot-context" &&
                reference.Arguments.GetProperty("objectGuid").GetString() == AlternativeRequiredSlotOneGuid);
            Assert.Contains(finding.EvidenceRefs, reference => reference.View == "slot-context" &&
                reference.Arguments.GetProperty("objectGuid").GetString() == AlternativeRequiredSlotTwoGuid);
            Assert.Contains(finding.EvidenceRefs, reference => reference.View == "template-order" &&
                reference.Arguments.GetProperty("objectGuid").GetString() == AlternativeFailureTemplateOneGuid);
            Assert.Contains(finding.EvidenceRefs, reference => reference.View == "template-order" &&
                reference.Arguments.GetProperty("objectGuid").GetString() == AlternativeFailureTemplateTwoGuid);
        }
    }

    [Fact]
    public async Task AffixPositionAndZeroAffixUseLoadedMorphologyAndConservativeReviewFloors()
    {
        await PublishBaselineAsync(_pristine);
        var parserPath = FactsParser(_pristine.Seed);
        using var invoker = new SIL.Motif.Host.PanGloss.PanGlossInvoker(parserPath);
        var job = CreateJob("R-tmpl-precedence",
            new ParsimonyScopeBinding(ParsimonyEvidenceScopeKind.ProjectApproved, null));
        var handler = new ParsimonyJobHandler(_database, _baselines, _projectKey, _project,
            new RunnerOptions { Root = Path.Combine(_root, "affix-position-zero-affix-worker"), ParserPath = parserPath }, invoker);
        var outcome = await handler.RunAsync(Claim(job.JobId), CancellationToken.None);
        Assert.Equal(JobStatus.Completed, outcome!.Status);

        using var result = JsonDocument.Parse(outcome.ResultJson!);
        var bundleId = result.RootElement.GetProperty("bundleId").GetString()!;
        var bundle = new ParsimonyBundleRepository(_database).Get(bundleId)!;
        AddNamedMeasureFacts(bundle.GrammarFactsPath);
        AddTemplateMeasureFacts(bundle.GrammarFactsPath, bundle.EvidencePath);
        AddPositiveAndNegativeAlternativeTemplates(bundle.GrammarFactsPath, bundle.EvidencePath);
        AddAffixPositionAndZeroAffixFacts(bundle.GrammarFactsPath, bundle.EvidencePath);
        var inputs = new ParsimonyReportInputs(bundle.BundleId, _token, "baseline", null,
            bundle.ModelFingerprint,
            new ParsimonyArtifactDigest(bundle.GrammarFactsSchemaVersion, Sha256Of(bundle.GrammarFactsPath)),
            new ParsimonyArtifactDigest(bundle.EvidenceSchemaVersion, Sha256Of(bundle.EvidencePath)),
            null, null, [], ParsimonyEvidenceScopeKind.ProjectApproved);
        using var session = new ParsimonyQuerySession(bundle.GrammarFactsPath, bundle.EvidencePath, inputs);

        Assert.True(MeasureRunner.Supports("B-affix-unslotted"));
        var unslotted = MeasureRunner.Execute("B-affix-unslotted", session, bundleId);
        Assert.Equal(ParsimonyMeasureStatus.Computed, unslotted.Run.Status);
        Assert.Equal(7, unslotted.Run.EligibleItems);
        Assert.Equal(1, unslotted.Run.FindingItems);
        var unslottedAnswer = ParsimonyMorphFixtureBuilder.For(_pristine.Seed, "B-affix-unslotted").Answer;
        Assert.Equal(unslottedAnswer.EligibleItems, unslotted.Run.EligibleItems);
        Assert.Equal(unslottedAnswer.FindingItems, unslotted.Run.FindingItems);
        Assert.Equal(unslottedAnswer.FindingTargets, unslotted.Findings.Select(finding => finding.AttachesTo.Identity));
        var aMsa = "20000000-0000-0000-0000-000000000111";
        var bMsa = "20000000-0000-0000-0000-000000000112";
        var aRow = Assert.IsType<ParsimonyUnslottedAffixViewRow>(Assert.Single(
            ParsimonyViewsQuery.Execute(session, new ParsimonyNamedViewRequest(bundleId, "unslotted-affixes",
                new ParsimonyViewFilters(ObjectGuid: aMsa))).Rows));
        Assert.All(aRow.Positions, position => Assert.Equal("suffix", position.Side));
        Assert.Equal(new[] { 1, 2 }, aRow.Positions.Select(position => position.SignedDistance)
            .Order().ToArray());
        var conflictedOrder = Assert.Single(aRow.Precedence);
        Assert.Equal(1, conflictedOrder.FirstBeforeSecond);
        Assert.Equal(1, conflictedOrder.SecondBeforeFirst);
        Assert.False(conflictedOrder.SuggestPartialOrder);
        Assert.Contains(unslotted.Findings, finding => finding.AttachesTo.Identity == aMsa &&
            ParsimonyReportProducer.DescribeFinding(session, finding).Contains("MSA for a", StringComparison.Ordinal));
        Assert.Equal(unslottedAnswer.Exclusions, new[] { "20000000-0000-0000-0000-000000000112" });

        var factsCopy = Path.Combine(_root, "affix-position-support-facts.sqlite");
        var evidenceCopy = Path.Combine(_root, "affix-position-support-evidence.sqlite");
        File.Copy(bundle.GrammarFactsPath, factsCopy);
        File.Copy(bundle.EvidencePath, evidenceCopy);
        AddAffixPositionTriageSupportFacts(factsCopy, evidenceCopy);
        var supportInputs = new ParsimonyReportInputs(bundle.BundleId, _token, "baseline", null,
            bundle.ModelFingerprint,
            new ParsimonyArtifactDigest(bundle.GrammarFactsSchemaVersion, Sha256Of(factsCopy)),
            new ParsimonyArtifactDigest(bundle.EvidenceSchemaVersion, Sha256Of(evidenceCopy)),
            null, null, [], ParsimonyEvidenceScopeKind.ProjectApproved);
        using (var supportSession = new ParsimonyQuerySession(factsCopy, evidenceCopy, supportInputs))
        {
            var supportRow = Assert.Single(supportSession.ReadUnslottedAffixes(), row => row.MsaGuid == aMsa);
            var order = Assert.Single(supportRow.Precedence, item => item.FirstMsaGuid == aMsa &&
                item.SecondMsaGuid == bMsa);
            Assert.Equal(3, order.FirstBeforeSecond);
            Assert.Equal(0, order.SecondBeforeFirst);
            Assert.Equal(1, order.ExcludedAnalyses);
            Assert.Equal(3, order.WordTypeCount);
            Assert.Equal(2, order.StemCount);
            Assert.True(order.SuggestPartialOrder);

            var omittedOptionalFiller = Assert.IsType<ParsimonyApprovedMorphSequenceViewRow>(Assert.Single(
                ParsimonyViewsQuery.Execute(supportSession, new ParsimonyNamedViewRequest(bundleId,
                    "approved-morph-sequences", new ParsimonyViewFilters(
                        ObjectGuid: "30000000-0000-0000-0000-000000000417"))).Rows));
            var includedOptionalFiller = Assert.IsType<ParsimonyApprovedMorphSequenceViewRow>(Assert.Single(
                ParsimonyViewsQuery.Execute(supportSession, new ParsimonyNamedViewRequest(bundleId,
                    "approved-morph-sequences", new ParsimonyViewFilters(
                        ObjectGuid: "30000000-0000-0000-0000-000000000419"))).Rows));
            Assert.Equal(new[] { "root", "a" }, omittedOptionalFiller.Morphs.Select(morph => morph.Forms["qaa"]));
            Assert.Equal(new[] { "root", "d", "a" }, includedOptionalFiller.Morphs.Select(morph => morph.Forms["qaa"]));
            Assert.Equal(new[] { 1, 2 }, supportRow.Positions.Select(position => position.SignedDistance)
                .Order().ToArray());
        }

        Assert.True(MeasureRunner.Supports("B-affix-null-vs-optional"));
        var nullOptional = MeasureRunner.Execute("B-affix-null-vs-optional", session, bundleId);
        Assert.Equal(ParsimonyMeasureStatus.Computed, nullOptional.Run.Status);
        Assert.Equal(0, nullOptional.Run.EligibleItems);
        Assert.Equal(0, nullOptional.Run.FindingItems);
        var zeroAnswer = ParsimonyMorphFixtureBuilder.For(_pristine.Seed, "B-affix-null-vs-optional").Answer;
        Assert.Equal(zeroAnswer.EligibleItems, nullOptional.Run.EligibleItems);
        Assert.Equal(zeroAnswer.FindingItems, nullOptional.Run.FindingItems);
        var nullRows = ParsimonyViewsQuery.Execute(session,
                new ParsimonyNamedViewRequest(bundleId, "null-optional", new ParsimonyViewFilters()))
            .Rows.Cast<ParsimonyNullOptionalViewRow>().ToArray();
        Assert.Equal(13, nullRows.Length);
        var processMsa = "30000000-0000-0000-0000-000000000342";
        Assert.Equal("process-rule-excluded",
            Assert.Single(nullRows, row => row.MsaGuid == processMsa).Classification);
        Assert.Single(nullRows, row => row.Classification == "process-rule-excluded");
        Assert.Contains(nullRows, row => row.MsaGuid == "30000000-0000-0000-0000-000000000302" &&
            row.Classification == "empty-or-dropped-form-excluded");
        Assert.Contains(nullRows, row => row.MsaGuid == "30000000-0000-0000-0000-000000000312" &&
            row.Classification == "empty-or-dropped-form-excluded");
        var droppedMarker = Assert.Single(nullRows, row =>
            row.MsaGuid == "30000000-0000-0000-0000-000000000322");
        var droppedRealization = Assert.Single(droppedMarker.Realizations);
        Assert.Equal("empty-or-dropped-form-excluded", droppedMarker.Classification);
        Assert.False(droppedRealization.CompilerRecognizedZero);
        Assert.Equal("^0", droppedRealization.Forms["qaa"]);
        Assert.All(zeroAnswer.Exclusions, msaGuid => Assert.Contains(nullRows, row => row.MsaGuid == msaGuid));
        Assert.DoesNotContain(nullRows, row => row.Classification == "optional-slot-candidate");
        Assert.DoesNotContain(nullRows, row => row.Classification == "obligatory-slot-alternative");
        var allZeroRows = session.ReadNullOptionalAffixes();
        Assert.Contains(allZeroRows, row => row.Classification == "empty-or-dropped-form-excluded");
        Assert.Contains(allZeroRows, row => row.Classification == "synthetic-null-excluded");
        Assert.Contains(allZeroRows, row => row.Classification == "process-rule-excluded");
        Assert.Contains(allZeroRows, row => row.Classification == "abstract-form-excluded");
        Assert.Contains(allZeroRows, row => row.MsaGuid == "30000000-0000-0000-0000-000000000362" &&
            row.Classification == "empty-or-dropped-form-excluded" && !row.IsLoadedZeroOnly);
    }

    [Fact]
    public async Task AViewPageThatExceedsItsDeadlineReturnsNotComputed()
    {
        var (bundle, inputs) = await PublishNamedMeasureBundleAsync();
        var slowRan = false;
        using var session = new ParsimonyQuerySession(bundle.GrammarFactsPath, bundle.EvidencePath, inputs,
            CancellationToken.None, connection =>
            {
                if (slowRan) return;
                slowRan = true;
                RunSlowStatement(connection, _ => { });
            });

        var response = ParsimonyViewsQuery.Execute(session,
            new ParsimonyNamedViewRequest(bundle.BundleId, "join-quality", new ParsimonyViewFilters()),
            pageDeadline: TimeSpan.FromMilliseconds(100));

        Assert.Equal(ParsimonyMeasureStatus.Inconclusive, response.Status);
        Assert.Null(response.Total);
        Assert.Equal(0, response.Returned);
        Assert.Empty(response.Rows);
        Assert.Null(response.NextCursor);
        Assert.False(response.Truncated);
        Assert.Contains("did not finish within 100 milliseconds", response.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LosingTheLeaseCancelsAnInFlightQuery()
    {
        var (bundle, inputs) = await PublishNamedMeasureBundleAsync();
        using var leaseLoss = new CancellationTokenSource();
        var slowRan = false;
        using var session = new ParsimonyQuerySession(bundle.GrammarFactsPath, bundle.EvidencePath, inputs,
            leaseLoss.Token, connection =>
            {
                if (slowRan) return;
                slowRan = true;
                RunSlowStatement(connection, row =>
                {
                    if (row == 3) leaseLoss.Cancel();
                });
            });

        Assert.Throws<ParsimonyLeaseLostException>(() => ParsimonyViewsQuery.Execute(session,
            new ParsimonyNamedViewRequest(bundle.BundleId, "join-quality", new ParsimonyViewFilters())));
    }

    [Fact]
    public async Task NaturalClassUsedOnlyByAnAffixProcessIsNotUnused()
    {
        const string naturalClass = "10000000-0000-0000-0000-000000000041";
        await RunStatementUsageAsync(factsPath => AddStatementUsageFacts(factsPath,
            "INSERT INTO natural_class(guid, kind, name) VALUES ('" + naturalClass + "', 'segments', 'vowels');",
            StatementReference("naturalClass", naturalClass, "affixProcess", "10000000-0000-0000-0000-000000000042",
                "process_input", "applied")),
            result => Assert.DoesNotContain(result.Findings, finding => finding.AttachesTo.Identity.Contains(
                naturalClass, StringComparison.Ordinal)));
    }

    [Fact]
    public async Task NaturalClassUsedOnlyInARuleContextIsNotUnused()
    {
        const string naturalClass = "10000000-0000-0000-0000-000000000051";
        await RunStatementUsageAsync(factsPath => AddStatementUsageFacts(factsPath,
            "INSERT INTO natural_class(guid, kind, name) VALUES ('" + naturalClass + "', 'segments', 'vowels');",
            StatementReference("naturalClass", naturalClass, "phonologicalRule",
                "10000000-0000-0000-0000-000000000052", "rewrite_left_context", "applied")),
            result => Assert.DoesNotContain(result.Findings, finding => finding.AttachesTo.Identity.Contains(
                naturalClass, StringComparison.Ordinal)));
    }

    [Fact]
    public async Task EnvironmentUsedOnlyAsAnInfixPositionIsNotUnused()
    {
        const string environment = "10000000-0000-0000-0000-000000000061";
        await RunStatementUsageAsync(factsPath => AddStatementUsageFacts(factsPath,
            "INSERT INTO environment(guid, name, representation, parse_status) VALUES ('" + environment +
            "', 'infix', '_', 'valid');",
            StatementReference("environment", environment, "allomorph", "10000000-0000-0000-0000-000000000062",
                "position_env", "applied")),
            result => Assert.DoesNotContain(result.Findings, finding => finding.AttachesTo.Identity.Contains(
                environment, StringComparison.Ordinal)));
    }

    [Fact]
    public async Task AClassReferencedOnlyByAnUnloadedAllomorphIsUnusedForParsing()
    {
        const string naturalClass = "10000000-0000-0000-0000-000000000071";
        const string segmentedAllomorph = "10000000-0000-0000-0000-000000000072";
        // owner_not_loaded is the format's effect for a referrer the compiler never loaded.
        await RunStatementUsageAsync(factsPath => AddStatementUsageFacts(factsPath,
            "INSERT INTO natural_class(guid, kind, name) VALUES ('" + naturalClass + "', 'segments', 'segmented');",
            StatementReference("naturalClass", naturalClass, "allomorph", segmentedAllomorph, "form_segment",
                "owner_not_loaded")),
            result =>
            {
                Assert.Equal(ParsimonyMeasureStatus.Computed, result.Run.Status);
                var finding = Assert.Single(result.Findings, finding => finding.AttachesTo.Identity.Contains(
                    naturalClass, StringComparison.Ordinal));
                Assert.Contains(finding.Limitations, limitation => limitation.Contains(
                    $"allomorph {segmentedAllomorph} (owner_not_loaded)", StringComparison.Ordinal));
            });
    }

    [Fact]
    public async Task StatementUnusedIsNotAvailableWhenReferencesAreMissing()
    {
        // The fake leaves the derived sections unavailable, so the reference index is incomplete.
        await RunStatementUsageAsync(_ => { }, result =>
        {
            Assert.Equal(ParsimonyMeasureStatus.NotAvailable, result.Run.Status);
            Assert.Empty(result.Findings);
            var note = Assert.IsType<string>(result.NoteReason);
            Assert.StartsWith("the grammar facts do not include ", note, StringComparison.Ordinal);
            Assert.NotNull(ParsimonyNotes.NotChecked(result));
        });
    }

    [Fact]
    public async Task AClassWithNoReferenceIsUnused()
    {
        const string naturalClass = "10000000-0000-0000-0000-000000000081";
        await RunStatementUsageAsync(factsPath => AddStatementUsageFacts(factsPath,
            "INSERT INTO natural_class(guid, kind, name) VALUES ('" + naturalClass + "', 'segments', 'orphan');"),
            result =>
            {
                var finding = Assert.Single(result.Findings, finding => finding.AttachesTo.Identity.Contains(
                    naturalClass, StringComparison.Ordinal));
                Assert.Contains(finding.Limitations, limitation => limitation.Contains("No grammar object references",
                    StringComparison.Ordinal));
            });
    }

    private static string StatementReference(string targetKind, string target, string referrerKind, string referrer,
        string role, string parserEffect) =>
        "INSERT INTO statement_reference(target_kind, target_guid, referrer_kind, referrer_guid, role, ordinal, " +
        $"parser_effect) VALUES ('{targetKind}', '{target}', '{referrerKind}', '{referrer}', '{role}', 0, " +
        $"'{parserEffect}');";

    private static void AddStatementUsageFacts(string factsPath, params string[] statements)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(factsPath),
            Pooling = false,
            ForeignKeys = true,
        }.ToString());
        connection.Open();
        using var transaction = connection.BeginTransaction();
        foreach (var sql in statements)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }
        using (var sections = connection.CreateCommand())
        {
            sections.Transaction = transaction;
            sections.CommandText = "UPDATE artifact_section SET status='complete', reason_code=NULL WHERE section IN " +
                "('environments', 'features', 'phonology', 'patterns', 'load_accounting', 'compound_rules', " +
                "'affix_processes');";
            sections.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    private async Task RunStatementUsageAsync(Action<string> addFacts, Action<ParsimonyMeasureResult> assert)
    {
        await PublishBaselineAsync(_pristine);
        var parserPath = FactsParser(_pristine.Seed);
        using var invoker = new SIL.Motif.Host.PanGloss.PanGlossInvoker(parserPath);
        var job = CreateJob("P-statement-unused",
            new ParsimonyScopeBinding(ParsimonyEvidenceScopeKind.ProjectApproved, null));
        var handler = new ParsimonyJobHandler(_database, _baselines, _projectKey, _project,
            new RunnerOptions { Root = Path.Combine(_root, "statement-usage-worker"), ParserPath = parserPath },
            invoker);
        var outcome = await handler.RunAsync(Claim(job.JobId), CancellationToken.None);
        Assert.Equal(JobStatus.Completed, outcome!.Status);

        using var result = JsonDocument.Parse(outcome.ResultJson!);
        var bundleId = result.RootElement.GetProperty("bundleId").GetString()!;
        var bundle = new ParsimonyBundleRepository(_database).Get(bundleId)!;
        addFacts(bundle.GrammarFactsPath);
        var inputs = new ParsimonyReportInputs(bundle.BundleId, _token, "baseline", null,
            bundle.ModelFingerprint,
            new ParsimonyArtifactDigest(bundle.GrammarFactsSchemaVersion, Sha256Of(bundle.GrammarFactsPath)),
            new ParsimonyArtifactDigest(bundle.EvidenceSchemaVersion, bundle.EvidenceSha256),
            null, null, [], ParsimonyEvidenceScopeKind.ProjectApproved);
        using var session = new ParsimonyQuerySession(bundle.GrammarFactsPath, bundle.EvidencePath, inputs);
        assert(MeasureRunner.Execute("P-statement-unused", session, bundleId));
    }

    private async Task<(ParsimonyBundleRecord Bundle, ParsimonyReportInputs Inputs)> PublishNamedMeasureBundleAsync()
    {
        await PublishBaselineAsync(_pristine);
        var parserPath = FactsParser(_pristine.Seed);
        using var invoker = new SIL.Motif.Host.PanGloss.PanGlossInvoker(parserPath);
        var scopeBinding = new ParsimonyScopeBinding(ParsimonyEvidenceScopeKind.ProjectApproved, null);
        var job = CreateJob("P-allo-duplicate-form", scopeBinding);
        var handler = new ParsimonyJobHandler(_database, _baselines, _projectKey, _project,
            new RunnerOptions { Root = Path.Combine(_root, "measure-worker"), ParserPath = parserPath }, invoker);
        var outcome = await handler.RunAsync(Claim(job.JobId), CancellationToken.None);
        Assert.True(outcome!.Status == JobStatus.Completed, outcome.ResultJson);
        using var result = JsonDocument.Parse(outcome.ResultJson!);
        var bundleId = result.RootElement.GetProperty("bundleId").GetString()!;
        var bundle = new ParsimonyBundleRepository(_database).Get(bundleId)!;
        AddNamedMeasureFacts(bundle.GrammarFactsPath);
        var inputs = new ParsimonyReportInputs(bundle.BundleId, _token, "baseline", null, bundle.ModelFingerprint,
            new ParsimonyArtifactDigest(bundle.GrammarFactsSchemaVersion, Sha256Of(bundle.GrammarFactsPath)),
            new ParsimonyArtifactDigest(bundle.EvidenceSchemaVersion, bundle.EvidenceSha256),
            null, null, [], ParsimonyEvidenceScopeKind.ProjectApproved);
        return (bundle, inputs);
    }

    [Fact]
    public async Task OpeningASessionDoesNotRunTheForeignKeyCheck()
    {
        var (bundle, inputs) = await PublishNamedMeasureBundleAsync();
        var copy = Path.Combine(_root, "integrity-skipped-facts.sqlite");
        File.Copy(bundle.GrammarFactsPath, copy);
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
               {
                   DataSource = Path.GetFullPath(copy),
                   Mode = SqliteOpenMode.ReadWrite,
                   Pooling = false,
               }.ToString()))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA foreign_keys=OFF;" +
                "CREATE TABLE zz_parent(id INTEGER PRIMARY KEY);" +
                "CREATE TABLE zz_child(parent_id INTEGER REFERENCES zz_parent(id));" +
                "INSERT INTO zz_child(parent_id) VALUES (99);";
            command.ExecuteNonQuery();
        }
        var copyInputs = new ParsimonyReportInputs(inputs.BundleId, inputs.BaselineToken, inputs.InputKind,
            inputs.Candidate, inputs.ModelFingerprint,
            new ParsimonyArtifactDigest(inputs.GrammarFacts.SchemaVersion, Sha256Of(copy)), inputs.Evidence,
            inputs.SelectionSha256, inputs.ExpectationRevisionSha256, inputs.AssessmentIds, inputs.EvidenceScope,
            inputs.RetirementExpectationTranslation);

        using var session = new ParsimonyQuerySession(copy, bundle.EvidencePath, copyInputs);

        Assert.Equal(bundle.BundleId, session.BundleId);
    }

    [Fact]
    public async Task PublishingRunsTheIntegrityCheckOnce()
    {
        await PublishBaselineAsync(_pristine);
        var parserPath = FactsParser(_pristine.Seed);
        using var invoker = new SIL.Motif.Host.PanGloss.PanGlossInvoker(parserPath);
        var scopeBinding = new ParsimonyScopeBinding(ParsimonyEvidenceScopeKind.ProjectApproved, null);
        var job = CreateJob("P-allo-duplicate-form", scopeBinding);
        var checkedPaths = new List<string>();
        var handler = new ParsimonyJobHandler(_database, _baselines, _projectKey, _project,
            new RunnerOptions { Root = Path.Combine(_root, "integrity-worker"), ParserPath = parserPath }, invoker,
            null, path =>
            {
                checkedPaths.Add(path);
                ParsimonyFactsIntegrity.Verify(path);
            });

        var outcome = await handler.RunAsync(Claim(job.JobId), CancellationToken.None);

        Assert.True(outcome!.Status == JobStatus.Completed, outcome.ResultJson);
        Assert.Single(checkedPaths);
        Assert.EndsWith("grammar-facts.sqlite", checkedPaths[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFactsFileFailingTheIntegrityCheckIsNeverPublished()
    {
        await PublishBaselineAsync(_pristine);
        var parserPath = FactsParser(_pristine.Seed);
        using var inner = new SIL.Motif.Host.PanGloss.PanGlossInvoker(parserPath);
        var invoker = new ForeignKeyViolatingFactsInvoker(inner);
        var scopeBinding = new ParsimonyScopeBinding(ParsimonyEvidenceScopeKind.ProjectApproved, null);
        var job = CreateJob("P-allo-duplicate-form", scopeBinding);
        var workerRoot = Path.Combine(_root, "integrity-refused-worker");
        var handler = new ParsimonyJobHandler(_database, _baselines, _projectKey, _project,
            new RunnerOptions { Root = workerRoot, ParserPath = parserPath }, invoker);

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            handler.RunAsync(Claim(job.JobId), CancellationToken.None));

        Assert.Contains("foreign-key violation", error.Message, StringComparison.Ordinal);
        using (var connection = _database.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT COUNT(*) FROM ParsimonyBundles;";
            Assert.Equal(0L, Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture));
        }
        var published = Directory.Exists(workerRoot)
            ? Directory.EnumerateFiles(workerRoot, "grammar-facts.sqlite", SearchOption.AllDirectories).ToArray()
            : [];
        Assert.Empty(published);
    }

    /// <summary>Returns facts with a foreign-key violation and a digest recomputed to match.</summary>
    private sealed class ForeignKeyViolatingFactsInvoker(SIL.Motif.Host.PanGloss.IPanGlossInvoker inner)
        : SIL.Motif.Host.PanGloss.IPanGlossInvoker
    {
        public async Task<SIL.Motif.Host.PanGloss.PanGlossOutcome> RunAsync(
            SIL.Motif.Host.PanGloss.PanGlossRequest request, string label, CancellationToken cancellationToken,
            TimeSpan? wallClockCap = null)
        {
            var outcome = await inner.RunAsync(request, label, cancellationToken, wallClockCap).ConfigureAwait(false);
            if (request is not SIL.Motif.Host.PanGloss.PanGlossRequest.Facts ||
                outcome is not SIL.Motif.Host.PanGloss.PanGlossOutcome.Completed completed ||
                completed.Facts is not { } facts)
                return outcome;

            var path = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(facts.Path)!,
                "fk-violating-" + System.IO.Path.GetFileName(facts.Path));
            File.Copy(facts.Path, path);
            using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
                   {
                       DataSource = Path.GetFullPath(path),
                       Mode = SqliteOpenMode.ReadWrite,
                       Pooling = false,
                   }.ToString()))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "PRAGMA foreign_keys=OFF;" +
                    "CREATE TABLE zz_parent(id INTEGER PRIMARY KEY);" +
                    "CREATE TABLE zz_child(parent_id INTEGER REFERENCES zz_parent(id));" +
                    "INSERT INTO zz_child(parent_id) VALUES (99);";
                command.ExecuteNonQuery();
            }
            return completed with
            {
                Facts = facts with
                {
                    Path = path,
                    OutputBytes = new FileInfo(path).Length,
                    OutputSha256 = Sha256Of(path),
                },
            };
        }
    }

    /// <summary>Runs a counting user-defined function over 2,000 rows (about 10 s if never interrupted).</summary>
    private static void RunSlowStatement(SqliteConnection connection, Action<int> onRow)
    {
        var rows = 0;
        connection.CreateFunction<long, long>("motif_test_slow_row", value =>
        {
            onRow(++rows);
            Thread.Sleep(5);
            return value;
        });
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT SUM(motif_test_slow_row(n)) FROM (WITH RECURSIVE counter(n) AS " +
            "(SELECT 1 UNION ALL SELECT n + 1 FROM counter WHERE n < 2000) SELECT n FROM counter);";
        command.ExecuteScalar();
    }

    private static SqliteConnection SessionConnection(ParsimonyQuerySession session) =>
        (SqliteConnection)(typeof(ParsimonyQuerySession).GetField("_connection",
            BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(session)
            ?? throw new InvalidOperationException("The query session connection was not found."));

    private static void AddAffixPositionAndZeroAffixFacts(string factsPath, string evidencePath)
    {
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
               { DataSource = Path.GetFullPath(factsPath), Pooling = false, ForeignKeys = true }.ToString()))
        {
            connection.Open();
            using var transaction = connection.BeginTransaction();
            ParsimonyMorphPatch.RemoveMsaSlot(connection, transaction,
                "20000000-0000-0000-0000-000000000111", "20000000-0000-0000-0000-000000000221");
            transaction.Commit();
        }

        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
               { DataSource = Path.GetFullPath(factsPath), Pooling = false, ForeignKeys = true }.ToString()))
        {
            connection.Open();
            using var transaction = connection.BeginTransaction();
            void Execute(string sql, params (string Name, object Value)[] values)
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = sql;
                foreach (var (name, value) in values) command.Parameters.AddWithValue(name, value);
                command.ExecuteNonQuery();
            }

            // Allomorph mappings follow AddOrder, which creates the allomorph output they name.
            void AddMapping(string kind, string guid, string outputKind, string outputKey,
                string quality = "authored") => Execute(
                (outputKind == "morphRule" ? CompiledOutputSql.EnsureRuleOutput : "") + CompiledOutputSql.MappingRow,
                ("$kind", kind), ("$guid", guid), ("$key", kind + ":" + guid), ("$output", outputKey),
                ("$role", kind == "msa" ? "rule" : "form"), ("$quality", quality));

            // No measure under test reads these rows' phone condition; 1 keeps them out of the final-elsewhere flag.
            void AddOrder(string entry, string msa, string allomorph, int order, string? output = null)
            {
                var outputKey = output ?? $"morph_rule:{msa}@Morphology#allo{order}";
                Execute(CompiledOutputSql.EnsureOwner + CompiledOutputSql.EnsureAllomorphOutput + CompiledOutputSql.OrderRow,
                    ("$owner", msa + ":" + outputKey), ("$bucket", "Morphology"), ("$output", outputKey),
                    ("$order", order), ("$phone", 1), ("$gate", 0), ("$entry", entry), ("$msa", msa),
                    ("$sourceKey", allomorph), ("$allomorph", allomorph));
            }

            var existingAffixes = new[]
            {
                (Entry: "20000000-0000-0000-0000-000000000011", Msa: "20000000-0000-0000-0000-000000000111",
                    Allomorph: "20000000-0000-0000-0000-000000000211"),
                (Entry: "20000000-0000-0000-0000-000000000012", Msa: "20000000-0000-0000-0000-000000000112",
                    Allomorph: "20000000-0000-0000-0000-000000000212"),
                (Entry: "20000000-0000-0000-0000-000000000013", Msa: "20000000-0000-0000-0000-000000000113",
                    Allomorph: "20000000-0000-0000-0000-000000000213"),
                (Entry: "20000000-0000-0000-0000-000000000014", Msa: "20000000-0000-0000-0000-000000000114",
                    Allomorph: "20000000-0000-0000-0000-000000000214"),
                (Entry: "20000000-0000-0000-0000-000000000015", Msa: "20000000-0000-0000-0000-000000000115",
                    Allomorph: "20000000-0000-0000-0000-000000000215"),
                (Entry: "20000000-0000-0000-0000-000000000016", Msa: "20000000-0000-0000-0000-000000000116",
                    Allomorph: "20000000-0000-0000-0000-000000000216"),
            };
            Execute("UPDATE msa_slot SET role='clitic_slot' " +
                    "WHERE msa_guid='20000000-0000-0000-0000-000000000113';");
            foreach (var (entry, msa, allomorph) in existingAffixes)
            {
                AddMapping("msa", msa, "morphRule", "morph_rule:" + msa + "@Morphology");
                AddOrder(entry, msa, allomorph, 0);
                AddMapping("allomorph", allomorph, "allomorph", $"morph_rule:{msa}@Morphology#allo0");
            }

            var zeroAffixes = new[]
            {
                (Entry: "30000000-0000-0000-0000-000000000301", Msa: "30000000-0000-0000-0000-000000000302",
                    Allomorph: "30000000-0000-0000-0000-000000000303", Slot: "30000000-0000-0000-0000-000000000304",
                    Name: "Optional zero", Optional: true, Abstract: false, Loaded: false),
                (Entry: "30000000-0000-0000-0000-000000000311", Msa: "30000000-0000-0000-0000-000000000312",
                    Allomorph: "30000000-0000-0000-0000-000000000313", Slot: "30000000-0000-0000-0000-000000000314",
                    Name: "Required feature zero", Optional: false, Abstract: false, Loaded: false),
                (Entry: "30000000-0000-0000-0000-000000000321", Msa: "30000000-0000-0000-0000-000000000322",
                    Allomorph: "30000000-0000-0000-0000-000000000323", Slot: "30000000-0000-0000-0000-000000000324",
                    Name: "Dropped zero", Optional: true, Abstract: false, Loaded: false),
                (Entry: "30000000-0000-0000-0000-000000000351", Msa: "30000000-0000-0000-0000-000000000352",
                    Allomorph: "30000000-0000-0000-0000-000000000353", Slot: "30000000-0000-0000-0000-000000000354",
                    Name: "Abstract zero", Optional: true, Abstract: true, Loaded: false),
            };
            foreach (var zero in zeroAffixes)
            {
                Execute("INSERT INTO lex_entry(guid, source_ordinal, lexeme_morph_type) " +
                        "VALUES ($entry, 1, 'suffix');", ("$entry", zero.Entry));
                Execute("INSERT INTO entry_citation_form(entry_guid, ordinal, writing_system, form) " +
                        "VALUES ($entry, 0, 'qaa', $form);", ("$entry", zero.Entry),
                    ("$form", zero.Name));
                Execute("INSERT INTO msa(msa_guid, entry_guid, kind) VALUES ($msa, $entry, 'inflectional');",
                    ("$msa", zero.Msa), ("$entry", zero.Entry));
                Execute("INSERT INTO msa_slot(msa_guid, role, ordinal, slot_guid) " +
                        "VALUES ($msa, 'slot', 0, $slot);", ("$msa", zero.Msa), ("$slot", zero.Slot));
                Execute("INSERT INTO affix_slot(guid, category_guid, name, optional) " +
                        "VALUES ($slot, $category, $name, $optional);",
                    ("$slot", zero.Slot), ("$category", RootCategoryGuid), ("$name", zero.Name),
                    ("$optional", zero.Optional ? 1 : 0));
                Execute("INSERT INTO allomorph(guid, entry_guid, ordinal, morph_type, form_class, is_abstract) " +
                        "VALUES ($allomorph, $entry, 0, 'suffix', 'affix', $abstract);",
                    ("$allomorph", zero.Allomorph), ("$entry", zero.Entry),
                    ("$abstract", zero.Abstract ? 1 : 0));
                Execute("INSERT INTO allomorph_form(allomorph_guid, ordinal, writing_system, form) " +
                        "VALUES ($allomorph, 0, 'qaa', '');", ("$allomorph", zero.Allomorph));
                AddMapping("msa", zero.Msa, "morphRule", "morph_rule:" + zero.Msa + "@Morphology");
                if (!zero.Loaded) continue;
                AddOrder(zero.Entry, zero.Msa, zero.Allomorph, 0);
                AddMapping("allomorph", zero.Allomorph, "allomorph",
                    $"morph_rule:{zero.Msa}@Morphology#allo0");
            }

            const string droppedZeroAllomorph = "30000000-0000-0000-0000-000000000323";
            Execute("UPDATE allomorph_form SET form='^0' WHERE allomorph_guid=$allomorph;",
                ("$allomorph", droppedZeroAllomorph));
            Execute("INSERT INTO load_fact(subject_kind, subject_key, pipeline_stage, decision_ordinal, " +
                    "subject_guid, context_key, disposition, loaded, reason_code) VALUES ('allomorph', " +
                    "$subjectKey, 'compile', 0, $allomorph, 'lexEntryForm:morphology', 'rejected', 0, " +
                    "'grammar.mrule.unreachable-compacted');",
                ("$subjectKey", JsonSerializer.Serialize(new
                {
                    identity = new { guid = droppedZeroAllomorph, kind = "object" },
                    kind = "allomorph",
                })), ("$allomorph", droppedZeroAllomorph));

            const string mixedEntry = "30000000-0000-0000-0000-000000000361";
            const string mixedMsa = "30000000-0000-0000-0000-000000000362";
            const string mixedZero = "30000000-0000-0000-0000-000000000363";
            const string mixedNonzero = "30000000-0000-0000-0000-000000000364";
            const string mixedSlot = "30000000-0000-0000-0000-000000000365";
            Execute("INSERT INTO lex_entry(guid, source_ordinal, lexeme_morph_type) " +
                    "VALUES ($entry, 1, 'suffix');", ("$entry", mixedEntry));
            Execute("INSERT INTO entry_citation_form(entry_guid, ordinal, writing_system, form) " +
                    "VALUES ($entry, 0, 'qaa', 'Mixed zero and suffix');", ("$entry", mixedEntry));
            Execute("INSERT INTO msa(msa_guid, entry_guid, kind) VALUES ($msa, $entry, 'inflectional');",
                ("$msa", mixedMsa), ("$entry", mixedEntry));
            Execute("INSERT INTO msa_slot(msa_guid, role, ordinal, slot_guid) " +
                    "VALUES ($msa, 'slot', 0, $slot);", ("$msa", mixedMsa), ("$slot", mixedSlot));
            Execute("INSERT INTO affix_slot(guid, category_guid, name, optional) " +
                    "VALUES ($slot, $category, 'Mixed slot', 1);",
                ("$slot", mixedSlot), ("$category", RootCategoryGuid));
            foreach (var item in new[] { (Guid: mixedZero, Form: ""), (Guid: mixedNonzero, Form: "s") })
            {
                Execute("INSERT INTO allomorph(guid, entry_guid, ordinal, morph_type, form_class, is_abstract) " +
                        "VALUES ($allomorph, $entry, $ordinal, 'suffix', 'affix', 0);",
                    ("$allomorph", item.Guid), ("$entry", mixedEntry),
                    ("$ordinal", item.Guid == mixedZero ? 0 : 1));
                Execute("INSERT INTO allomorph_form(allomorph_guid, ordinal, writing_system, form) " +
                        "VALUES ($allomorph, 0, 'qaa', $form);", ("$allomorph", item.Guid), ("$form", item.Form));
                if (item.Form.Length > 0)
                {
                    const int order = 1;
                    AddOrder(mixedEntry, mixedMsa, item.Guid, order);
                    AddMapping("allomorph", item.Guid, "allomorph", $"morph_rule:{mixedMsa}@Morphology#allo{order}");
                }
            }
            AddMapping("msa", mixedMsa, "morphRule", "morph_rule:" + mixedMsa + "@Morphology");

            const string syntheticEntry = "30000000-0000-0000-0000-000000000331";
            const string syntheticMsa = "30000000-0000-0000-0000-000000000332";
            Execute("INSERT INTO lex_entry(guid, source_ordinal, lexeme_morph_type) " +
                    "VALUES ($entry, 1, 'suffix');", ("$entry", syntheticEntry));
            Execute("INSERT INTO entry_citation_form(entry_guid, ordinal, writing_system, form) " +
                    "VALUES ($entry, 0, 'qaa', 'Synthetic null');", ("$entry", syntheticEntry));
            Execute("INSERT INTO msa(msa_guid, entry_guid, kind) VALUES ($msa, $entry, 'inflectional');",
                ("$msa", syntheticMsa), ("$entry", syntheticEntry));
            AddMapping("msa", syntheticMsa, "morphRule", "morph_rule:" + syntheticMsa + "@Morphology");
            Execute(CompiledOutputSql.EnsureAllomorphOutput +
                    "INSERT INTO compiled_allomorph_order(owner_output_id, source_entry_guid, source_msa_guid, " +
                    "source_allomorph_key, source_allomorph_guid, output_id, compiled_order) VALUES (NULL, $entry, " +
                    "$msa, '', NULL, (SELECT output_id FROM compiled_output WHERE key=$output), 0);",
                ("$owner", "synthetic-null-owner"), ("$bucket", "Morphology"), ("$output", "synthetic-null-output"),
                ("$order", 0), ("$phone", 1), ("$gate", 0), ("$entry", syntheticEntry), ("$msa", syntheticMsa));

            const string processEntry = "30000000-0000-0000-0000-000000000341";
            const string processMsa = "30000000-0000-0000-0000-000000000342";
            Execute("INSERT INTO lex_entry(guid, source_ordinal, lexeme_morph_type) " +
                    "VALUES ($entry, 1, 'suffix');", ("$entry", processEntry));
            Execute("INSERT INTO entry_citation_form(entry_guid, ordinal, writing_system, form) " +
                    "VALUES ($entry, 0, 'qaa', 'Process rule');", ("$entry", processEntry));
            Execute("INSERT INTO msa(msa_guid, entry_guid, kind) VALUES ($msa, $entry, 'inflectional');",
                ("$msa", processMsa), ("$entry", processEntry));
            AddMapping("msa", processMsa, "morphRule", "morph_rule:" + processMsa + "@Morphology");
            const string processAllomorph = "30000000-0000-0000-0000-000000000343";
            Execute("INSERT INTO allomorph(guid, entry_guid, ordinal, morph_type, form_class, is_abstract) " +
                    "VALUES ($allomorph, $entry, 0, 'suffix', 'affix', 1);",
                ("$allomorph", processAllomorph), ("$entry", processEntry));
            Execute("INSERT INTO allomorph_form(allomorph_guid, ordinal, writing_system, form) " +
                    "VALUES ($allomorph, 0, 'qaa', '');", ("$allomorph", processAllomorph));
            AddOrder(processEntry, processMsa, processAllomorph, 0);
            AddMapping("allomorph", processAllomorph, "allomorph",
                $"morph_rule:{processMsa}@Morphology#allo0");
            Execute("INSERT INTO load_fact(subject_kind, subject_key, pipeline_stage, decision_ordinal, " +
                    "subject_guid, context_key, disposition, loaded, reason_code) VALUES ('affixProcess', " +
                    "$subjectKey, 'import', 0, $allomorph, '', 'represented', 1, 'represented');",
                ("$subjectKey", JsonSerializer.Serialize(new
                {
                    identity = new { guid = processAllomorph, kind = "object" },
                    kind = "affixProcess",
                })), ("$allomorph", processAllomorph));

            const string featureMsa = "30000000-0000-0000-0000-000000000312";
            const string featureEntry = "30000000-0000-0000-0000-000000000311";
            Execute("INSERT INTO feature_structure(fs_id, system, owner_kind, owner_guid, role, path) " +
                    "VALUES (920001, 'morphosyntactic', 'msa', $msa, 'features', 'root');", ("$msa", featureMsa));
            Execute("INSERT INTO feature_assignment(fs_id, ordinal, feature_guid, value_kind, value_guid) " +
                    "VALUES (920001, 0, '30000000-0000-0000-0000-000000000901', 'closed', " +
                    "'30000000-0000-0000-0000-000000000902');");
            Execute("INSERT INTO sense(sense_guid, entry_guid, msa_guid) " +
                    "VALUES ('30000000-0000-0000-0000-000000000361', $entry, $msa);",
                ("$entry", featureEntry), ("$msa", featureMsa));
            Execute("INSERT INTO adhoc_prohibition(prohibition_guid, kind, disabled, adjacency, primary_guid, " +
                    "target_kind) VALUES ('30000000-0000-0000-0000-000000000371', 'morpheme', 0, 'anywhere', " +
                    "$msa, 'msa');", ("$msa", featureMsa));
            Execute("UPDATE artifact_section SET status='complete', reason_code=NULL " +
                    "WHERE section IN ('compiled_mappings', 'load_accounting', 'adhoc_prohibitions');");
            transaction.Commit();
        }

        using var evidence = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(evidencePath), Pooling = false, ForeignKeys = true,
        }.ToString());
        evidence.Open();
        using var evidenceTransaction = evidence.BeginTransaction();
        void InsertEvidence(string sql, params (string Name, object Value)[] values)
        {
            using var command = evidence.CreateCommand();
            command.Transaction = evidenceTransaction;
            command.CommandText = sql;
            foreach (var (name, value) in values) command.Parameters.AddWithValue(name, value);
            command.ExecuteNonQuery();
        }

        const string featureAnalysis = "30000000-0000-0000-0000-000000000381";
        const string featureWordform = "30000000-0000-0000-0000-000000000382";
        InsertEvidence("INSERT INTO wordforms(wordform_guid, spelling_status) VALUES ($guid, 0);",
            ("$guid", featureWordform));
        InsertEvidence("INSERT INTO wordform_forms(wordform_guid, writing_system, form, form_nfd) " +
                       "VALUES ($guid, 'qaa', 'passive-zero', 'passive-zero');", ("$guid", featureWordform));
        InsertEvidence("INSERT INTO analyses(analysis_guid, wordform_guid, opinion, source_kind, content_sha256) " +
                       "VALUES ($analysis, $wordform, 'approved', 'manual', $digest);",
            ("$analysis", featureAnalysis), ("$wordform", featureWordform), ("$digest", new string('c', 64)));
        InsertEvidence("INSERT INTO analysis_morphs(analysis_guid, ordinal, morph_guid, msa_guid, entry_guid) " +
                       "VALUES ($analysis, 0, $morph, $msa, $entry);",
            ("$analysis", featureAnalysis), ("$morph", "30000000-0000-0000-0000-000000000313"),
            ("$msa", "30000000-0000-0000-0000-000000000312"),
            ("$entry", "30000000-0000-0000-0000-000000000311"));
        InsertEvidence("INSERT INTO analysis_morph_forms(analysis_guid, morph_ordinal, writing_system, form, form_nfd) " +
                       "VALUES ($analysis, 0, 'qaa', '', '');", ("$analysis", featureAnalysis));
        InsertEvidence("INSERT INTO analysis_morph_texts(analysis_guid, morph_ordinal, kind, writing_system, text, text_nfd) " +
                       "VALUES ($analysis, 0, 'gloss', 'qaa', 'passive', 'passive');", ("$analysis", featureAnalysis));
        InsertEvidence("UPDATE artifact_metadata SET wordform_count=(SELECT COUNT(*) FROM wordforms), " +
                       "analysis_count=(SELECT COUNT(*) FROM analyses) WHERE singleton=1;");
        evidenceTransaction.Commit();
    }

    private static void AddAffixPositionTriageSupportFacts(string factsPath, string evidencePath)
    {
        const string secondStemEntry = "30000000-0000-0000-0000-000000000401";
        const string secondStemMsa = "30000000-0000-0000-0000-000000000402";
        const string secondStemMorph = "30000000-0000-0000-0000-000000000403";
        using (var facts = new SqliteConnection(new SqliteConnectionStringBuilder
               { DataSource = Path.GetFullPath(factsPath), Pooling = false, ForeignKeys = true }.ToString()))
        {
            facts.Open();
            using var transaction = facts.BeginTransaction();
            void Execute(string sql, params (string Name, object Value)[] values)
            {
                using var command = facts.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = sql;
                foreach (var (name, value) in values) command.Parameters.AddWithValue(name, value);
                command.ExecuteNonQuery();
            }

            Execute("INSERT INTO lex_entry(guid, source_ordinal, lexeme_morph_type) " +
                    "VALUES ($entry, 99, 'stem');", ("$entry", secondStemEntry));
            Execute("INSERT INTO entry_citation_form(entry_guid, ordinal, writing_system, form) " +
                    "VALUES ($entry, 0, 'qaa', 'second-root');", ("$entry", secondStemEntry));
            Execute("INSERT INTO msa(msa_guid, entry_guid, kind) VALUES ($msa, $entry, 'stem');",
                ("$msa", secondStemMsa), ("$entry", secondStemEntry));
            Execute("INSERT INTO msa_category(msa_guid, role, ordinal, category_guid) " +
                    "VALUES ($msa, 'pos', 0, $category);", ("$msa", secondStemMsa), ("$category", ChildCategoryGuid));
            Execute("INSERT INTO allomorph(guid, entry_guid, ordinal, morph_type, form_class, is_abstract) " +
                    "VALUES ($morph, $entry, 0, 'stem', 'stem', 0);", ("$morph", secondStemMorph), ("$entry", secondStemEntry));
            Execute("INSERT INTO allomorph_form(allomorph_guid, ordinal, writing_system, form) " +
                    "VALUES ($morph, 0, 'qaa', 'second-root');", ("$morph", secondStemMorph));
            transaction.Commit();
        }

        using var evidence = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(evidencePath), Pooling = false, ForeignKeys = true,
        }.ToString());
        evidence.Open();
        using var evidenceTransaction = evidence.BeginTransaction();
        void ExecuteEvidence(string sql, params (string Name, object Value)[] values)
        {
            using var command = evidence.CreateCommand();
            command.Transaction = evidenceTransaction;
            command.CommandText = sql;
            foreach (var (name, value) in values) command.Parameters.AddWithValue(name, value);
            command.ExecuteNonQuery();
        }

        var oppositeAnalysis = "20000000-0000-0000-0000-000000000242";
        var wordform = "20000000-0000-0000-0000-000000000252";
        var aMorph = "20000000-0000-0000-0000-000000000211";
        var bMorph = "20000000-0000-0000-0000-000000000212";
        var aMsa = "20000000-0000-0000-0000-000000000111";
        var bMsa = "20000000-0000-0000-0000-000000000112";
        var aEntry = "20000000-0000-0000-0000-000000000011";
        var bEntry = "20000000-0000-0000-0000-000000000012";
        ExecuteEvidence("UPDATE wordform_forms SET form='ab-two', form_nfd='ab-two' " +
                        "WHERE wordform_guid=$wordform;", ("$wordform", wordform));
        ExecuteEvidence("UPDATE analysis_morphs SET morph_guid=$morph, msa_guid=$msa, entry_guid=$entry " +
                        "WHERE analysis_guid=$analysis AND ordinal=1;", ("$analysis", oppositeAnalysis),
            ("$morph", aMorph), ("$msa", aMsa), ("$entry", aEntry));
        ExecuteEvidence("UPDATE analysis_morphs SET morph_guid=$morph, msa_guid=$msa, entry_guid=$entry " +
                        "WHERE analysis_guid=$analysis AND ordinal=2;", ("$analysis", oppositeAnalysis),
            ("$morph", bMorph), ("$msa", bMsa), ("$entry", bEntry));
        ExecuteEvidence("UPDATE analysis_morph_forms SET form='a', form_nfd='a' " +
                        "WHERE analysis_guid=$analysis AND morph_ordinal=1;", ("$analysis", oppositeAnalysis));
        ExecuteEvidence("UPDATE analysis_morph_forms SET form='b', form_nfd='b' " +
                        "WHERE analysis_guid=$analysis AND morph_ordinal=2;", ("$analysis", oppositeAnalysis));
        ExecuteEvidence("UPDATE analysis_morph_texts SET text='A', text_nfd='A' " +
                        "WHERE analysis_guid=$analysis AND morph_ordinal=1 AND kind='gloss';",
            ("$analysis", oppositeAnalysis));
        ExecuteEvidence("UPDATE analysis_morph_texts SET text='B', text_nfd='B' " +
                        "WHERE analysis_guid=$analysis AND morph_ordinal=2 AND kind='gloss';",
            ("$analysis", oppositeAnalysis));

        const string firstRootMorph = "20000000-0000-0000-0000-000000000210";
        const string firstRootMsa = "20000000-0000-0000-0000-000000000110";
        const string firstRootEntry = "20000000-0000-0000-0000-000000000010";
        InsertAnalysis("30000000-0000-0000-0000-000000000413", "30000000-0000-0000-0000-000000000414",
            "ab-second-stem", secondStemMorph, secondStemMsa, secondStemEntry, repeated: false);
        InsertAnalysis("30000000-0000-0000-0000-000000000415", "30000000-0000-0000-0000-000000000416",
            "aab-ambiguous", firstRootMorph, firstRootMsa, firstRootEntry, repeated: true);
        InsertMorphSequence("30000000-0000-0000-0000-000000000417",
            "30000000-0000-0000-0000-000000000418", "root-a",
            (firstRootMorph, firstRootMsa, firstRootEntry, "root", "root"),
            (aMorph, aMsa, aEntry, "a", "A"));
        InsertMorphSequence("30000000-0000-0000-0000-000000000419",
            "30000000-0000-0000-0000-000000000420", "root-d-a",
            (firstRootMorph, firstRootMsa, firstRootEntry, "root", "root"),
            ("20000000-0000-0000-0000-000000000214", "20000000-0000-0000-0000-000000000114",
                "20000000-0000-0000-0000-000000000014", "d", "D"),
            (aMorph, aMsa, aEntry, "a", "A"));
        ExecuteEvidence("UPDATE artifact_metadata SET wordform_count=(SELECT COUNT(*) FROM wordforms), " +
                        "analysis_count=(SELECT COUNT(*) FROM analyses) WHERE singleton=1;");
        evidenceTransaction.Commit();

        void InsertAnalysis(string analysis, string wordformGuid, string word,
            string rootMorph, string rootMsa, string rootEntry, bool repeated)
        {
            ExecuteEvidence("INSERT INTO wordforms(wordform_guid, spelling_status) VALUES ($guid, 0);",
                ("$guid", wordformGuid));
            ExecuteEvidence("INSERT INTO wordform_forms(wordform_guid, writing_system, form, form_nfd) " +
                            "VALUES ($guid, 'qaa', $form, $form);", ("$guid", wordformGuid), ("$form", word));
            ExecuteEvidence("INSERT INTO analyses(analysis_guid, wordform_guid, opinion, source_kind, content_sha256) " +
                            "VALUES ($analysis, $wordform, 'approved', 'manual', $digest);",
                ("$analysis", analysis), ("$wordform", wordformGuid), ("$digest", new string('d', 64)));
            var morphs = repeated
                ? new[] { (rootMorph, rootMsa, rootEntry, "root", "root"),
                    (aMorph, aMsa, aEntry, "a", "A"), (aMorph, aMsa, aEntry, "a", "A"),
                    (bMorph, bMsa, bEntry, "b", "B") }
                : new[] { (rootMorph, rootMsa, rootEntry, "root", "root"),
                    (aMorph, aMsa, aEntry, "a", "A"), (bMorph, bMsa, bEntry, "b", "B") };
            for (var ordinal = 0; ordinal < morphs.Length; ordinal++)
            {
                var morph = morphs[ordinal];
                ExecuteEvidence("INSERT INTO analysis_morphs(analysis_guid, ordinal, morph_guid, msa_guid, entry_guid) " +
                                "VALUES ($analysis, $ordinal, $morph, $msa, $entry);",
                    ("$analysis", analysis), ("$ordinal", ordinal), ("$morph", morph.Item1),
                    ("$msa", morph.Item2), ("$entry", morph.Item3));
                ExecuteEvidence("INSERT INTO analysis_morph_forms(analysis_guid, morph_ordinal, writing_system, form, form_nfd) " +
                                "VALUES ($analysis, $ordinal, 'qaa', $form, $form);",
                    ("$analysis", analysis), ("$ordinal", ordinal), ("$form", morph.Item4));
                ExecuteEvidence("INSERT INTO analysis_morph_texts(analysis_guid, morph_ordinal, kind, writing_system, text, text_nfd) " +
                                "VALUES ($analysis, $ordinal, 'gloss', 'qaa', $gloss, $gloss);",
                    ("$analysis", analysis), ("$ordinal", ordinal), ("$gloss", morph.Item5));
            }
        }

        void InsertMorphSequence(string analysis, string wordformGuid, string word,
            params (string Morph, string Msa, string Entry, string Form, string Gloss)[] morphs)
        {
            ExecuteEvidence("INSERT INTO wordforms(wordform_guid, spelling_status) VALUES ($guid, 0);",
                ("$guid", wordformGuid));
            ExecuteEvidence("INSERT INTO wordform_forms(wordform_guid, writing_system, form, form_nfd) " +
                            "VALUES ($guid, 'qaa', $form, $form);", ("$guid", wordformGuid), ("$form", word));
            ExecuteEvidence("INSERT INTO analyses(analysis_guid, wordform_guid, opinion, source_kind, content_sha256) " +
                            "VALUES ($analysis, $wordform, 'approved', 'manual', $digest);",
                ("$analysis", analysis), ("$wordform", wordformGuid), ("$digest", new string('e', 64)));
            for (var ordinal = 0; ordinal < morphs.Length; ordinal++)
            {
                var morph = morphs[ordinal];
                ExecuteEvidence("INSERT INTO analysis_morphs(analysis_guid, ordinal, morph_guid, msa_guid, entry_guid) " +
                                "VALUES ($analysis, $ordinal, $morph, $msa, $entry);",
                    ("$analysis", analysis), ("$ordinal", ordinal), ("$morph", morph.Morph),
                    ("$msa", morph.Msa), ("$entry", morph.Entry));
                ExecuteEvidence("INSERT INTO analysis_morph_forms(analysis_guid, morph_ordinal, writing_system, form, form_nfd) " +
                                "VALUES ($analysis, $ordinal, 'qaa', $form, $form);",
                    ("$analysis", analysis), ("$ordinal", ordinal), ("$form", morph.Form));
                ExecuteEvidence("INSERT INTO analysis_morph_texts(analysis_guid, morph_ordinal, kind, writing_system, text, text_nfd) " +
                                "VALUES ($analysis, $ordinal, 'gloss', 'qaa', $gloss, $gloss);",
                    ("$analysis", analysis), ("$ordinal", ordinal), ("$gloss", morph.Gloss));
            }
        }
    }

    private static long ReadScalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    [Fact]
    public async Task DuplicateGroupsWithDifferentMorphTypesHaveDistinctFindingIds()
    {
        var (_, measure) = await SeedDuplicateAllomorphsAsync(
            (DuplicateMemberOne, "prefix", "ka"),
            (DuplicateMemberTwo, "prefix", "ka"),
            (DuplicateMemberThree, "suffix", "ka"),
            (DuplicateMemberFour, "suffix", "ka"));

        var findings = measure().Findings;
        Assert.Equal(2, findings.Count);
        Assert.Equal(2, findings.Select(finding => finding.FindingId).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task DuplicateFindingIdSurvivesAFormChange()
    {
        var (factsPath, measure) = await SeedDuplicateAllomorphsAsync(
            (DuplicateMemberOne, "stem", "pu"),
            (DuplicateMemberTwo, "stem", "pu"));
        var before = Assert.Single(measure().Findings);

        ExecuteFactsSql(factsPath, "UPDATE allomorph_form SET form = 'po';");
        var after = Assert.Single(measure().Findings);

        Assert.Equal(before.FindingId, after.FindingId);
        Assert.NotEqual(before.EvidenceDigest, after.EvidenceDigest);
    }

    [Fact]
    public async Task DuplicateFindingIdChangesWhenAMemberIsReplaced()
    {
        var (factsPath, measure) = await SeedDuplicateAllomorphsAsync(
            (DuplicateMemberOne, "stem", "pu"),
            (DuplicateMemberTwo, "stem", "pu"));
        var before = Assert.Single(measure().Findings);

        ExecuteFactsSql(factsPath,
            $"UPDATE allomorph SET is_abstract = 1 WHERE guid = '{DuplicateMemberTwo}';",
            "INSERT INTO allomorph(guid, entry_guid, ordinal, morph_type, form_class, is_abstract) " +
                $"VALUES ('{DuplicateMemberThree}', '{DuplicateEntry}', 2, 'stem', 'stem', 0);",
            "INSERT INTO allomorph_form(allomorph_guid, ordinal, writing_system, form) " +
                $"VALUES ('{DuplicateMemberThree}', 0, 'qaa', 'pu');");
        var after = Assert.Single(measure().Findings);

        Assert.NotEqual(before.FindingId, after.FindingId);
    }

    private const string DuplicateEntry = "10000000-0000-0000-0000-000000000101";
    private const string DuplicateMemberOne = "10000000-0000-0000-0000-000000000201";
    private const string DuplicateMemberTwo = "10000000-0000-0000-0000-000000000202";
    private const string DuplicateMemberThree = "10000000-0000-0000-0000-000000000203";
    private const string DuplicateMemberFour = "10000000-0000-0000-0000-000000000204";

    /// <summary>Seeds one entry's allomorphs after a baseline job; returns a re-measure function.</summary>
    private async Task<(string FactsPath, Func<ParsimonyMeasureResult> Measure)> SeedDuplicateAllomorphsAsync(
        params (string Guid, string MorphType, string Form)[] allomorphs)
    {
        await PublishBaselineAsync(_pristine);
        var parserPath = FactsParser(_pristine.Seed);
        using var invoker = new SIL.Motif.Host.PanGloss.PanGlossInvoker(parserPath);
        var scopeBinding = new ParsimonyScopeBinding(ParsimonyEvidenceScopeKind.ProjectApproved, null);
        var job = CreateJob("P-allo-duplicate-form", scopeBinding);
        var handler = new ParsimonyJobHandler(_database, _baselines, _projectKey, _project,
            new RunnerOptions { Root = Path.Combine(_root, "duplicate-identity-worker"), ParserPath = parserPath }, invoker);
        var outcome = await handler.RunAsync(Claim(job.JobId), CancellationToken.None);
        Assert.True(outcome!.Status == JobStatus.Completed, outcome.ResultJson);
        using var result = JsonDocument.Parse(outcome.ResultJson!);
        var bundleId = result.RootElement.GetProperty("bundleId").GetString()!;
        var bundle = new ParsimonyBundleRepository(_database).Get(bundleId)!;

        var statements = new List<string>
        {
            "INSERT INTO writing_system(tag) VALUES ('qaa');",
            $"INSERT INTO lex_entry(guid, source_ordinal, lexeme_morph_type) VALUES ('{DuplicateEntry}', 0, 'stem');",
        };
        for (var index = 0; index < allomorphs.Length; index++)
        {
            var (guid, morphType, form) = allomorphs[index];
            statements.Add("INSERT INTO allomorph(guid, entry_guid, ordinal, morph_type, form_class, is_abstract) " +
                $"VALUES ('{guid}', '{DuplicateEntry}', {index}, '{morphType}', '{(morphType is "stem" or "root" or "boundStem" ? "stem" : "affix")}', 0);");
            statements.Add("INSERT INTO allomorph_form(allomorph_guid, ordinal, writing_system, form) " +
                $"VALUES ('{guid}', 0, 'qaa', '{form}');");
        }
        statements.Add("UPDATE artifact_section SET status='complete', reason_code=NULL " +
            "WHERE section IN ('environments', 'features', 'patterns', 'load_accounting');");
        ExecuteFactsSql(bundle.GrammarFactsPath, statements.ToArray());

        return (bundle.GrammarFactsPath, () =>
        {
            var inputs = new ParsimonyReportInputs(bundle.BundleId, _token, "baseline", null,
                bundle.ModelFingerprint,
                new ParsimonyArtifactDigest(bundle.GrammarFactsSchemaVersion, Sha256Of(bundle.GrammarFactsPath)),
                new ParsimonyArtifactDigest(bundle.EvidenceSchemaVersion, bundle.EvidenceSha256),
                null, null, [], ParsimonyEvidenceScopeKind.ProjectApproved);
            using var session = new ParsimonyQuerySession(bundle.GrammarFactsPath, bundle.EvidencePath, inputs);
            return MeasureRunner.Execute("P-allo-duplicate-form", session, bundle.BundleId);
        });
    }

    [Fact]
    public async Task DuplicateProhibitionCaptionNamesTheProhibitionInWords()
    {
        using var run = await RunAdhocDuplicateAsync("prohibition-caption-worker", factsPath => ExecuteFactsSql(factsPath,
            "INSERT OR IGNORE INTO writing_system(tag) VALUES ('qaa');",
            "INSERT OR IGNORE INTO lex_entry(guid, source_ordinal, lexeme_morph_type) " +
            "VALUES ('50000000-0000-0000-0000-000000000001', 0, 'stem');",
            "WITH targets(guid) AS (SELECT primary_guid FROM adhoc_prohibition WHERE target_kind='allomorph' " +
            "UNION SELECT target_guid FROM adhoc_other WHERE target_kind='allomorph') " +
            "INSERT INTO allomorph(guid, entry_guid, ordinal, morph_type, form_class, is_abstract) " +
            "SELECT guid, '50000000-0000-0000-0000-000000000001', ROW_NUMBER() OVER (ORDER BY guid) - 1, 'prefix', 'affix', 0 " +
            "FROM targets;",
            "INSERT INTO allomorph_form(allomorph_guid, ordinal, writing_system, form) " +
            "SELECT guid, 0, 'qaa', 'form-' || substr(guid, 25) FROM allomorph WHERE entry_guid='50000000-0000-0000-0000-000000000001';"));

        Assert.NotEmpty(run.Findings);
        foreach (var finding in run.Findings)
        {
            var caption = ParsimonyReportProducer.DescribeFinding(run.Session, finding);
            Assert.Matches("^\"Allomorph co-prohibition: form-[0-9a-f]+, form-[0-9a-f]+\"$", caption);
            Assert.True(Guid.TryParse(finding.AttachesTo.Identity, out _));
            Assert.DoesNotContain(finding.AttachesTo.Identity, caption, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task DuplicateProhibitionWithUnnamedTargetsSaysUnnamed()
    {
        using var run = await RunAdhocDuplicateAsync("prohibition-unnamed-worker", _ => { });

        Assert.NotEmpty(run.Findings);
        foreach (var finding in run.Findings)
        {
            var caption = ParsimonyReportProducer.DescribeFinding(run.Session, finding);
            Assert.Equal("\"Allomorph co-prohibition (unnamed)\"", caption);
            Assert.DoesNotContain(finding.AttachesTo.Identity, caption, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task DuplicateMorphemeProhibitionNamesItsMorphemes()
    {
        const string firstEntry = "50000000-0000-0000-0000-000000000011";
        const string secondEntry = "50000000-0000-0000-0000-000000000012";
        const string firstMsa = "50000000-0000-0000-0000-000000000021";
        const string secondMsa = "50000000-0000-0000-0000-000000000022";
        const string prohibition = "50000000-0000-0000-0000-000000000031";
        using var run = await RunAdhocDuplicateAsync("prohibition-morpheme-worker", factsPath => ExecuteFactsSql(factsPath,
            "INSERT OR IGNORE INTO writing_system(tag) VALUES ('qaa');",
            $"INSERT INTO lex_entry(guid, source_ordinal, lexeme_morph_type) VALUES ('{firstEntry}', 0, 'stem');",
            $"INSERT INTO lex_entry(guid, source_ordinal, lexeme_morph_type) VALUES ('{secondEntry}', 1, 'stem');",
            $"INSERT INTO entry_citation_form(entry_guid, ordinal, writing_system, form) VALUES ('{firstEntry}', 0, 'qaa', 'alpha');",
            $"INSERT INTO entry_citation_form(entry_guid, ordinal, writing_system, form) VALUES ('{secondEntry}', 0, 'qaa', 'beta');",
            $"INSERT INTO msa(msa_guid, entry_guid, kind) VALUES ('{firstMsa}', '{firstEntry}', 'stem');",
            $"INSERT INTO msa(msa_guid, entry_guid, kind) VALUES ('{secondMsa}', '{secondEntry}', 'stem');",
            $"INSERT INTO adhoc_prohibition(prohibition_guid, kind, disabled, adjacency, primary_guid, target_kind) " +
            $"VALUES ('{prohibition}', 'morpheme', 0, 'anywhere', '{firstMsa}', 'msa');",
            $"INSERT INTO adhoc_other(prohibition_guid, ordinal, target_guid, target_kind) " +
            $"VALUES ('{prohibition}', 0, '{secondMsa}', 'msa');"));

        var caption = run.Session.DescribeObject(prohibition);
        Assert.NotNull(caption);
        Assert.StartsWith("Morpheme co-prohibition: ", caption, StringComparison.Ordinal);
        Assert.Equal("Morpheme co-prohibition: " + run.Session.DescribeMsa(firstMsa) + ", " +
            run.Session.DescribeMsa(secondMsa), caption);
        Assert.Contains("alpha", caption, StringComparison.Ordinal);
        Assert.Contains("beta", caption, StringComparison.Ordinal);
        Assert.DoesNotContain(prohibition, caption, StringComparison.Ordinal);
    }

    private sealed record AdhocDuplicateRun(ParsimonyQuerySession Session, IReadOnlyList<ParsimonyFinding> Findings)
        : IDisposable
    {
        public void Dispose() => Session.Dispose();
    }

    /// <summary>Runs P-adhoc-duplicate on the baseline, then measures the bundle after facts edits.</summary>
    private async Task<AdhocDuplicateRun> RunAdhocDuplicateAsync(string workerRoot, Action<string> editFacts)
    {
        await PublishBaselineAsync(_pristine);
        var parserPath = FactsParser(_pristine.Seed);
        using var invoker = new SIL.Motif.Host.PanGloss.PanGlossInvoker(parserPath);
        var selection = SIL.Motif.Host.Corpus.Selection.Create("Default",
            [SeededProject.AnalysedWordForm, SeededProject.UnanalysedWordForm]);
        var scopeBinding = new ParsimonyScopeBinding(ParsimonyEvidenceScopeKind.DefaultSelection,
            new ParsimonySelectionSnapshot("Default", [_seededText.TextId], [], selection.Words,
                selection.Sha256));
        var job = CreateJob("P-adhoc-duplicate", scopeBinding);
        var handler = new ParsimonyJobHandler(_database, _baselines, _projectKey, _project,
            new RunnerOptions { Root = Path.Combine(_root, workerRoot), ParserPath = parserPath }, invoker);
        var outcome = await handler.RunAsync(Claim(job.JobId), CancellationToken.None);
        Assert.True(outcome!.Status == JobStatus.Completed, outcome.ResultJson);
        using var result = JsonDocument.Parse(outcome.ResultJson!);
        var reportId = result.RootElement.GetProperty("reportId").GetString()!;
        var report = Assert.IsType<ReportRecord>(new ReportRepository(_database).Get(reportId));
        var response = JsonSerializer.Deserialize<ParsimonyReportResponse>(report.ReportJson,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var bundle = new EvidenceArtifactRepository(_database).Get(response.Inputs.BundleId)!;
        editFacts(bundle.GrammarFactsPath);
        var inputs = new ParsimonyReportInputs(response.Inputs.BundleId, response.Inputs.BaselineToken,
            response.Inputs.InputKind, response.Inputs.Candidate, response.Inputs.ModelFingerprint,
            new ParsimonyArtifactDigest(bundle.GrammarFactsSchemaVersion, Sha256Of(bundle.GrammarFactsPath)),
            response.Inputs.Evidence, response.Inputs.SelectionSha256, response.Inputs.ExpectationRevisionSha256,
            response.Inputs.AssessmentIds, response.Inputs.EvidenceScope,
            response.Inputs.RetirementExpectationTranslation);
        var session = new ParsimonyQuerySession(bundle.GrammarFactsPath, bundle.EvidencePath, inputs);
        var findings = MeasureRunner.Execute("P-adhoc-duplicate", session, bundle.BundleId).Findings;
        return new AdhocDuplicateRun(session, findings);
    }

    private static void ExecuteFactsSql(string factsPath, params string[] statements)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(factsPath),
            Pooling = false,
            ForeignKeys = true,
        }.ToString());
        connection.Open();
        using var transaction = connection.BeginTransaction();
        foreach (var sql in statements)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    private static void AddPhonologicalRuleFact(string factsPath)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(factsPath),
            Pooling = false,
            ForeignKeys = true,
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO phonological_rule(guid, name, kind, direction, order_index, " +
            "effective_stratum_key) VALUES ('10000000-0000-0000-0000-000000000099', 'rule-99', 'rewrite', " +
            "'leftToRight', 0, NULL);";
        command.ExecuteNonQuery();
    }

    [Fact]
    public async Task AdhocSlotOrderMeasureKeepsControlsAndApprovedCounterexamplesVisible()
    {
        await PublishBaselineAsync(_pristine);
        var parserPath = FactsParser(_pristine.Seed);
        using var invoker = new SIL.Motif.Host.PanGloss.PanGlossInvoker(parserPath);
        var scope = new ParsimonyScopeBinding(ParsimonyEvidenceScopeKind.ProjectApproved, null);
        var job = CreateJob("R-tmpl-precedence", scope);
        var handler = new ParsimonyJobHandler(_database, _baselines, _projectKey, _project,
            new RunnerOptions { Root = Path.Combine(_root, "adhoc-slot-order-worker"), ParserPath = parserPath }, invoker);
        var outcome = await handler.RunAsync(Claim(job.JobId), CancellationToken.None);
        Assert.True(outcome!.Status == JobStatus.Completed, outcome.ResultJson);
        using var resultDocument = JsonDocument.Parse(outcome.ResultJson!);
        var bundleId = resultDocument.RootElement.GetProperty("bundleId").GetString()!;
        var bundle = new ParsimonyBundleRepository(_database).Get(bundleId)!;
        AddNamedMeasureFacts(bundle.GrammarFactsPath);
        AddTemplateMeasureFacts(bundle.GrammarFactsPath, bundle.EvidencePath);
        AddAdhocSlotOrderFacts(bundle.GrammarFactsPath, bundle.EvidencePath);

        ParsimonyMeasureResult ExecuteMeasure()
        {
            var inputs = new ParsimonyReportInputs(bundle.BundleId, _token, "baseline", null,
                bundle.ModelFingerprint,
                new ParsimonyArtifactDigest(bundle.GrammarFactsSchemaVersion, Sha256Of(bundle.GrammarFactsPath)),
                new ParsimonyArtifactDigest(bundle.EvidenceSchemaVersion, Sha256Of(bundle.EvidencePath)),
                null, null, [], ParsimonyEvidenceScopeKind.ProjectApproved);
            using var session = new ParsimonyQuerySession(bundle.GrammarFactsPath, bundle.EvidencePath, inputs);
            return MeasureRunner.Execute("B-adhoc-is-slot-order", session, bundleId);
        }

        Assert.True(MeasureRunner.Supports("B-adhoc-is-slot-order"));
        var withoutMinorityWitness = ExecuteMeasure();
        Assert.Equal(4, CountRows(bundle.GrammarFactsPath, "adhoc_group"));
        Assert.Equal(8, CountRows(bundle.GrammarFactsPath, "adhoc_group_member"));
        Assert.Contains("immediate adjacency", ReadText(bundle.GrammarFactsPath,
            "SELECT text FROM adhoc_group_text WHERE group_guid='40000000-0000-0000-0000-000000000002' " +
            "AND field='description' AND writing_system='qaa';"), StringComparison.Ordinal);
        Assert.Contains("person hierarchy", ReadText(bundle.GrammarFactsPath,
            "SELECT text FROM adhoc_group_text WHERE group_guid='40000000-0000-0000-0000-000000000003' " +
            "AND field='description' AND writing_system='qaa';"), StringComparison.Ordinal);
        Assert.DoesNotContain(withoutMinorityWitness.Findings, finding => finding.EvidenceRefs.Any(reference =>
            reference.View == "adhoc-context" &&
            reference.Arguments.GetProperty("objectGuid").GetString() ==
                "30000000-0000-0000-0000-000000000005"));
        Assert.Equal(ParsimonyMeasureStatus.Inconclusive, withoutMinorityWitness.Run.Status);
        Assert.Equal(8, withoutMinorityWitness.Run.EligibleItems);
        Assert.Equal(6, withoutMinorityWitness.Run.FindingItems);
        Assert.Contains("30000000-0000-0000-0000-000000000001", withoutMinorityWitness.Run.Detail,
            StringComparison.Ordinal);
        Assert.Contains("30000000-0000-0000-0000-000000000002", withoutMinorityWitness.Run.Detail,
            StringComparison.Ordinal);
        Assert.Contains("adjacentToLeft", withoutMinorityWitness.Run.Detail, StringComparison.Ordinal);
        Assert.Contains("30000000-0000-0000-0000-000000000005", withoutMinorityWitness.Run.Detail,
            StringComparison.Ordinal);
        Assert.Contains(AncestorTemplateGuid, withoutMinorityWitness.Run.Detail, StringComparison.Ordinal);
        Assert.Contains("30000000-0000-0000-0000-000000000011", withoutMinorityWitness.Run.Detail,
            StringComparison.Ordinal);
        Assert.Contains(AdhocSlotOrderCrossSideTemplateGuid, withoutMinorityWitness.Run.Detail, StringComparison.Ordinal);
        Assert.Contains("30000000-0000-0000-0000-000000000014", withoutMinorityWitness.Run.Detail,
            StringComparison.Ordinal);
        Assert.Contains("no complete compiled order", withoutMinorityWitness.Run.Detail,
            StringComparison.Ordinal);
        Assert.Contains("Anywhere", withoutMinorityWitness.Run.Detail, StringComparison.Ordinal);
        Assert.Contains("conjunctive", withoutMinorityWitness.Run.Detail, StringComparison.Ordinal);
        Assert.Contains("allomorph", withoutMinorityWitness.Run.Detail, StringComparison.Ordinal);
        Assert.Contains("cross-root", withoutMinorityWitness.Run.Detail, StringComparison.Ordinal);
        Assert.Contains("target-unreachable", withoutMinorityWitness.Run.Detail, StringComparison.Ordinal);
        foreach (var ruleGuid in Enumerable.Range(1, 14).Select(index =>
                     $"30000000-0000-0000-0000-{index:000000000000}"))
            Assert.Contains(ruleGuid, withoutMinorityWitness.Run.Detail, StringComparison.Ordinal);
        var family = Assert.Single(withoutMinorityWitness.Findings,
            finding => finding.AttachesTo.GroupKind == ParsimonyGroupKind.AdhocSlotOrder &&
                finding.Number.Numerator == 2);
        Assert.Equal(2, family.EvidenceRefs.Count(reference => reference.View == "adhoc-context"));
        Assert.Contains(family.EvidenceRefs, reference => reference.View == "template-order");
        var adjacencyQuestions = withoutMinorityWitness.Findings.Where(finding =>
            finding.Limitations.Any(item => item.Contains("adjacent co-occurrence", StringComparison.OrdinalIgnoreCase))).ToArray();
        Assert.Equal(2, adjacencyQuestions.Length);
        Assert.All(adjacencyQuestions, finding => Assert.Contains(finding.Limitations,
            item => item.Contains("Trial", StringComparison.Ordinal)));
        foreach (var excludedGuid in Enumerable.Range(5, 6).Select(index =>
                     $"30000000-0000-0000-0000-{index:000000000000}"))
            Assert.DoesNotContain(withoutMinorityWitness.Findings, finding =>
                finding.EvidenceRefs.Any(reference => reference.View == "adhoc-context" &&
                    reference.Arguments.GetProperty("objectGuid").GetString() == excludedGuid));
        Assert.DoesNotContain(withoutMinorityWitness.Findings, finding =>
            finding.EvidenceRefs.Any(reference => reference.View == "adhoc-context" &&
                reference.Arguments.GetProperty("objectGuid").GetString() ==
                    "30000000-0000-0000-0000-000000000013"));

        var findingIds = withoutMinorityWitness.Findings.Select(finding => finding.FindingId).ToArray();
        var evidenceDigests = withoutMinorityWitness.Findings.Select(finding => finding.EvidenceDigest).ToArray();
        RemoveAdhocGroupRows(bundle.GrammarFactsPath);
        var flatOnly = ExecuteMeasure();
        Assert.Equal(findingIds, flatOnly.Findings.Select(finding => finding.FindingId));
        Assert.Equal(evidenceDigests, flatOnly.Findings.Select(finding => finding.EvidenceDigest));
        Assert.Equal(withoutMinorityWitness.Run.Detail, flatOnly.Run.Detail);

        SetAdhocGroupSection(bundle.GrammarFactsPath, "unavailable");
        var withoutGroupedProvenance = ExecuteMeasure();
        Assert.Equal(findingIds, withoutGroupedProvenance.Findings.Select(finding => finding.FindingId));
        Assert.Equal(evidenceDigests, withoutGroupedProvenance.Findings.Select(finding => finding.EvidenceDigest));
        Assert.Contains("Grouped ad hoc provenance is unavailable", withoutGroupedProvenance.Run.Detail,
            StringComparison.Ordinal);
        Assert.All(withoutGroupedProvenance.Findings, finding => Assert.Contains(finding.Limitations,
            item => item.Contains("Grouped ad hoc provenance is unavailable", StringComparison.Ordinal)));

        SetApprovedAnalysisOpinion(bundle.EvidencePath, AdhocSlotOrderCounterexampleAnalysisGuid, "approved");
        var withMinorityWitness = ExecuteMeasure();
        Assert.DoesNotContain(withMinorityWitness.Findings, finding => finding.Number.Numerator == 2);
        Assert.Contains("Approved counterexample", withMinorityWitness.Run.Detail, StringComparison.Ordinal);
        Assert.Contains(AdhocSlotOrderCounterexampleAnalysisGuid, withMinorityWitness.Run.Detail, StringComparison.Ordinal);
        foreach (var blockedGuid in new[] { 1, 3 }.Select(index =>
                     $"30000000-0000-0000-0000-{index:000000000000}"))
            Assert.DoesNotContain(withMinorityWitness.Findings, finding => finding.EvidenceRefs.Any(reference =>
                reference.View == "adhoc-context" &&
                reference.Arguments.GetProperty("objectGuid").GetString() == blockedGuid));
    }

    private static void AddAdhocSlotOrderFacts(string factsPath, string evidencePath)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(factsPath), Pooling = false, ForeignKeys = true,
        }.ToString());
        connection.Open();
        using var transaction = connection.BeginTransaction();
        void Execute(string sql, params (string Name, object Value)[] values)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            foreach (var (name, value) in values) command.Parameters.AddWithValue(name, value);
            command.ExecuteNonQuery();
        }

        Execute("DELETE FROM template_slot WHERE template_guid=$template AND slot_guid IN ($first, $second);",
            ("$template", AncestorTemplateGuid),
            ("$first", "20000000-0000-0000-0000-000000000223"),
            ("$second", "20000000-0000-0000-0000-000000000224"));
        Execute("INSERT INTO affix_slot(guid, category_guid, name, optional) " +
                "VALUES ($guid, $category, 'AdhocSlotOrder other slot', 1);",
            ("$guid", AdhocSlotOrderOtherSlotGuid), ("$category", RootCategoryGuid));
        Execute("INSERT INTO affix_slot(guid, category_guid, name, optional) " +
                "VALUES ($guid, $category, 'AdhocSlotOrder primary slot', 1);",
            ("$guid", AdhocSlotOrderPrimarySlotGuid), ("$category", RootCategoryGuid));
        Execute("INSERT INTO affix_template(guid, category_guid, name, disabled, is_final) " +
                "VALUES ($guid, $category, 'AdhocSlotOrder left order', 0, 0);",
            ("$guid", AdhocSlotOrderLeftTemplateGuid), ("$category", ChildCategoryGuid));
        Execute("INSERT INTO affix_template(guid, category_guid, name, disabled, is_final) " +
                "VALUES ($guid, $category, 'AdhocSlotOrder right order', 0, 0);",
            ("$guid", AdhocSlotOrderRightTemplateGuid), ("$category", ChildCategoryGuid));
        Execute("INSERT INTO affix_template(guid, category_guid, name, disabled, is_final) " +
                "VALUES ($guid, $category, 'AdhocSlotOrder cross-side order', 0, 0);",
            ("$guid", AdhocSlotOrderCrossSideTemplateGuid), ("$category", ChildCategoryGuid));
        Execute("INSERT INTO affix_template(guid, category_guid, name, disabled, is_final) " +
                "VALUES ($guid, $category, 'AdhocSlotOrder unordered cross-side order', 0, 0);",
            ("$guid", AdhocSlotOrderUnorderedCrossSideTemplateGuid), ("$category", ChildCategoryGuid));
        Execute("INSERT INTO affix_slot(guid, category_guid, name, optional) " +
                "VALUES ($guid, $category, 'AdhocSlotOrder cross primary slot', 1);",
            ("$guid", AdhocSlotOrderCrossPrimarySlotGuid), ("$category", RootCategoryGuid));
        Execute("INSERT INTO affix_slot(guid, category_guid, name, optional) " +
                "VALUES ($guid, $category, 'AdhocSlotOrder cross other slot', 1);",
            ("$guid", AdhocSlotOrderCrossOtherSlotGuid), ("$category", RootCategoryGuid));
        Execute("INSERT INTO affix_slot(guid, category_guid, name, optional) " +
                "VALUES ($guid, $category, 'AdhocSlotOrder unordered cross primary slot', 1);",
            ("$guid", AdhocSlotOrderUnorderedCrossPrimarySlotGuid), ("$category", RootCategoryGuid));
        Execute("INSERT INTO affix_slot(guid, category_guid, name, optional) " +
                "VALUES ($guid, $category, 'AdhocSlotOrder unordered cross other slot', 1);",
            ("$guid", AdhocSlotOrderUnorderedCrossOtherSlotGuid), ("$category", RootCategoryGuid));
        foreach (var item in new[]
                 {
                     (Template: AdhocSlotOrderLeftTemplateGuid, Side: "suffix",
                         Slot: "20000000-0000-0000-0000-000000000223", Order: 0),
                     (Template: AdhocSlotOrderLeftTemplateGuid, Side: "suffix",
                         Slot: "20000000-0000-0000-0000-000000000224", Order: 1),
                     (Template: AdhocSlotOrderRightTemplateGuid, Side: "suffix", Slot: AdhocSlotOrderOtherSlotGuid, Order: 0),
                     (Template: AdhocSlotOrderRightTemplateGuid, Side: "suffix", Slot: AdhocSlotOrderPrimarySlotGuid, Order: 1),
                     (Template: AdhocSlotOrderCrossSideTemplateGuid, Side: "prefix", Slot: AdhocSlotOrderCrossPrimarySlotGuid, Order: 0),
                     (Template: AdhocSlotOrderCrossSideTemplateGuid, Side: "suffix", Slot: AdhocSlotOrderCrossOtherSlotGuid, Order: 1),
                     (Template: AdhocSlotOrderUnorderedCrossSideTemplateGuid, Side: "prefix",
                         Slot: AdhocSlotOrderUnorderedCrossPrimarySlotGuid, Order: 0),
                 })
            Execute("INSERT INTO template_slot(template_guid, side, ordinal, slot_guid, compiled_order) " +
                    "VALUES ($template, $side, $order, $slot, $order);",
                ("$template", item.Template), ("$side", item.Side), ("$order", item.Order), ("$slot", item.Slot));
        Execute("INSERT INTO template_slot(template_guid, side, ordinal, slot_guid, compiled_order) " +
                "VALUES ($template, 'suffix', 0, $slot, NULL);",
            ("$template", AdhocSlotOrderUnorderedCrossSideTemplateGuid), ("$slot", AdhocSlotOrderUnorderedCrossOtherSlotGuid));

        const string secondOtherEntry = "20000000-0000-0000-0000-000000000018";
        const string secondPrimaryEntry = "20000000-0000-0000-0000-000000000019";
        const string secondOtherMsa = "20000000-0000-0000-0000-000000000118";
        const string secondPrimaryMsa = "20000000-0000-0000-0000-000000000119";
        const string rightOtherEntry = "20000000-0000-0000-0000-000000000020";
        const string rightPrimaryEntry = "20000000-0000-0000-0000-000000000021";
        const string rightOtherMsa = "20000000-0000-0000-0000-000000000120";
        const string rightPrimaryMsa = "20000000-0000-0000-0000-000000000121";
        const string crossPrimaryEntry = "20000000-0000-0000-0000-000000000022";
        const string crossOtherEntry = "20000000-0000-0000-0000-000000000023";
        const string unorderedCrossPrimaryEntry = "20000000-0000-0000-0000-000000000024";
        const string unorderedCrossOtherEntry = "20000000-0000-0000-0000-000000000025";
        const string crossPrimaryMsa = "20000000-0000-0000-0000-000000000122";
        const string crossOtherMsa = "20000000-0000-0000-0000-000000000123";
        const string unorderedCrossPrimaryMsa = "20000000-0000-0000-0000-000000000124";
        const string unorderedCrossOtherMsa = "20000000-0000-0000-0000-000000000125";
        foreach (var item in new[]
                 {
                     (Entry: secondOtherEntry, Msa: secondOtherMsa, Morph: "20000000-0000-0000-0000-000000000218",
                         Slot: "20000000-0000-0000-0000-000000000224", Form: "d2", MorphType: "suffix"),
                     (Entry: secondPrimaryEntry, Msa: secondPrimaryMsa, Morph: "20000000-0000-0000-0000-000000000219",
                         Slot: "20000000-0000-0000-0000-000000000223", Form: "c2", MorphType: "suffix"),
                     (Entry: rightOtherEntry, Msa: rightOtherMsa, Morph: "20000000-0000-0000-0000-000000000220",
                         Slot: AdhocSlotOrderOtherSlotGuid, Form: "f", MorphType: "suffix"),
                     (Entry: rightPrimaryEntry, Msa: rightPrimaryMsa, Morph: "20000000-0000-0000-0000-000000000221",
                         Slot: AdhocSlotOrderPrimarySlotGuid, Form: "g", MorphType: "suffix"),
                     (Entry: crossPrimaryEntry, Msa: crossPrimaryMsa, Morph: "20000000-0000-0000-0000-000000000222",
                         Slot: AdhocSlotOrderCrossPrimarySlotGuid, Form: "h", MorphType: "prefix"),
                     (Entry: crossOtherEntry, Msa: crossOtherMsa, Morph: "20000000-0000-0000-0000-000000000223",
                         Slot: AdhocSlotOrderCrossOtherSlotGuid, Form: "i", MorphType: "suffix"),
                     (Entry: unorderedCrossPrimaryEntry, Msa: unorderedCrossPrimaryMsa,
                         Morph: "20000000-0000-0000-0000-000000000224",
                         Slot: AdhocSlotOrderUnorderedCrossPrimarySlotGuid, Form: "j", MorphType: "prefix"),
                     (Entry: unorderedCrossOtherEntry, Msa: unorderedCrossOtherMsa,
                         Morph: "20000000-0000-0000-0000-000000000225",
                         Slot: AdhocSlotOrderUnorderedCrossOtherSlotGuid, Form: "k", MorphType: "suffix"),
                 })
        {
            Execute("INSERT INTO lex_entry(guid, source_ordinal, lexeme_morph_type) VALUES ($entry, 1, 'affix');",
                ("$entry", item.Entry));
            Execute("INSERT INTO msa(msa_guid, entry_guid, kind) VALUES ($msa, $entry, 'inflectional');",
                ("$msa", item.Msa), ("$entry", item.Entry));
            Execute("INSERT INTO msa_category(msa_guid, role, ordinal, category_guid) " +
                    "VALUES ($msa, 'pos', 0, $category);",
                ("$msa", item.Msa), ("$category", ChildCategoryGuid));
            Execute("INSERT INTO msa_slot(msa_guid, role, ordinal, slot_guid) " +
                    "VALUES ($msa, 'slot', 0, $slot);",
                ("$msa", item.Msa), ("$slot", item.Slot));
            Execute("INSERT INTO allomorph(guid, entry_guid, ordinal, morph_type, form_class, is_abstract) " +
                    "VALUES ($morph, $entry, 0, $morphType, $formClass, 0);",
                ("$morph", item.Morph),
                ("$entry", item.Entry),
                ("$morphType", item.MorphType),
                ("$formClass", item.MorphType is "stem" or "root" or "boundStem" ? "stem" : "affix"));
            Execute("INSERT INTO allomorph_form(allomorph_guid, ordinal, writing_system, form) " +
                    "VALUES ($morph, 0, 'qaa', $form);",
                ("$morph", item.Morph),
                ("$form", item.Form));
            Execute(CompiledOutputSql.EnsureRuleOutput + CompiledOutputSql.MappingRow,
                ("$output", "morph_rule:" + item.Msa + "@Morphology"), ("$kind", "msa"), ("$guid", item.Msa),
                ("$key", item.Msa), ("$role", "rule"), ("$quality", "structural"));
        }

        foreach (var msa in new[]
                 {
                     "20000000-0000-0000-0000-000000000111", "20000000-0000-0000-0000-000000000112",
                     "20000000-0000-0000-0000-000000000113", "20000000-0000-0000-0000-000000000114",
                     "20000000-0000-0000-0000-000000000115", "20000000-0000-0000-0000-000000000116",
                 })
        {
            Execute("INSERT INTO msa_category(msa_guid, role, ordinal, category_guid) " +
                    "VALUES ($msa, 'pos', 0, $category);",
                ("$msa", msa), ("$category", ChildCategoryGuid));
            Execute(CompiledOutputSql.EnsureRuleOutput + CompiledOutputSql.MappingRow,
                ("$output", "morph_rule:" + msa + "@Morphology"), ("$kind", "msa"), ("$guid", msa),
                ("$key", msa), ("$role", "rule"), ("$quality", "structural"));
        }

        var rules = new[]
        {
            new Rule("30000000-0000-0000-0000-000000000001", "morpheme", "somewhereToLeft",
                "20000000-0000-0000-0000-000000000113", "msa", [("20000000-0000-0000-0000-000000000114", "msa")], true),
            new Rule("30000000-0000-0000-0000-000000000002", "morpheme", "somewhereToLeft",
                secondPrimaryMsa, "msa", [(secondOtherMsa, "msa")], true),
            new Rule("30000000-0000-0000-0000-000000000003", "morpheme", "adjacentToLeft",
                "20000000-0000-0000-0000-000000000113", "msa", [("20000000-0000-0000-0000-000000000114", "msa")], true),
            new Rule("30000000-0000-0000-0000-000000000004", "morpheme", "somewhereToLeft",
                "20000000-0000-0000-0000-000000000115", "msa", [("20000000-0000-0000-0000-000000000116", "msa")], true),
            new Rule("30000000-0000-0000-0000-000000000005", "morpheme", "somewhereToLeft",
                "20000000-0000-0000-0000-000000000112", "msa", [("20000000-0000-0000-0000-000000000111", "msa")], true),
            new Rule("30000000-0000-0000-0000-000000000006", "morpheme", "anywhere",
                "20000000-0000-0000-0000-000000000113", "msa", [("20000000-0000-0000-0000-000000000114", "msa")], true),
            new Rule("30000000-0000-0000-0000-000000000007", "morpheme", "somewhereToLeft",
                "20000000-0000-0000-0000-000000000113", "msa",
                [("20000000-0000-0000-0000-000000000114", "msa"),
                    ("20000000-0000-0000-0000-000000000115", "msa")], true),
            new Rule("30000000-0000-0000-0000-000000000008", "allomorph", "somewhereToLeft",
                "30000000-0000-0000-0000-000000000401", "allomorph",
                [("30000000-0000-0000-0000-000000000402", "allomorph")], true),
            new Rule("30000000-0000-0000-0000-000000000009", "morpheme", "somewhereToLeft",
                "20000000-0000-0000-0000-000000000113", "msa",
                [("20000000-0000-0000-0000-000000000110", "msa")], true),
            new Rule("30000000-0000-0000-0000-000000000010", "morpheme", "somewhereToLeft",
                "20000000-0000-0000-0000-000000000113", "msa", [("20000000-0000-0000-0000-000000000114", "msa")], false),
            new Rule("30000000-0000-0000-0000-000000000011", "morpheme", "somewhereToRight",
                rightPrimaryMsa, "msa", [(rightOtherMsa, "msa")], true),
            new Rule("30000000-0000-0000-0000-000000000012", "morpheme", "adjacentToRight",
                rightPrimaryMsa, "msa", [(rightOtherMsa, "msa")], true),
            new Rule("30000000-0000-0000-0000-000000000013", "morpheme", "somewhereToRight",
                crossPrimaryMsa, "msa", [(crossOtherMsa, "msa")], true),
            new Rule("30000000-0000-0000-0000-000000000014", "morpheme", "somewhereToRight",
                unorderedCrossPrimaryMsa, "msa", [(unorderedCrossOtherMsa, "msa")], true),
        };
        foreach (var rule in rules)
        {
            Execute("INSERT INTO adhoc_prohibition(prohibition_guid, kind, disabled, adjacency, primary_guid, " +
                    "target_kind) VALUES ($guid, $kind, 0, $adjacency, $primary, $targetKind);",
                ("$guid", rule.Guid), ("$kind", rule.Kind), ("$adjacency", rule.Adjacency),
                ("$primary", rule.PrimaryGuid), ("$targetKind", rule.TargetKind));
            for (var index = 0; index < rule.Others.Length; index++)
                Execute("INSERT INTO adhoc_other(prohibition_guid, ordinal, target_guid, target_kind) " +
                        "VALUES ($guid, $ordinal, $target, $targetKind);",
                    ("$guid", rule.Guid), ("$ordinal", index), ("$target", rule.Others[index].Guid),
                    ("$targetKind", rule.Others[index].Kind));
            var subjectKind = rule.Kind == "allomorph" ? "allomorphCoOccurrence" : "morphemeCoOccurrence";
            var subjectKey = JsonSerializer.Serialize(new
            {
                identity = new { guid = rule.Guid, kind = "object" },
                kind = subjectKind,
            });
            Execute("INSERT INTO load_fact(subject_kind, subject_key, pipeline_stage, decision_ordinal, " +
                    "subject_guid, context_key, disposition, loaded, reason_code) " +
                    "VALUES ($subjectKind, $subjectKey, 'compile', 0, $guid, '', $disposition, $loaded, $reason);",
                ("$subjectKind", subjectKind), ("$subjectKey", subjectKey), ("$guid", rule.Guid),
                ("$disposition", rule.Loaded ? "represented" : "rejected"), ("$loaded", rule.Loaded ? 1 : 0),
                ("$reason", rule.Loaded ? "represented" : "grammar.cooccurrence.target-unreachable"));
        }
        AddAdhocGroupFixtures(Execute,
        [
            new AdhocGroupFixture("40000000-0000-0000-0000-000000000001", "Candidate order family",
                "These two concrete prohibitions may restate one order; check every applicable placement and Approved sequence.",
                [rules[0].Guid, rules[1].Guid]),
            new AdhocGroupFixture("40000000-0000-0000-0000-000000000002", "Immediate adjacency",
                "These prohibitions forbid immediate adjacency only; retain them unless the linguist confirms a broader order restriction.",
                [rules[2].Guid, rules[11].Guid]),
            new AdhocGroupFixture("40000000-0000-0000-0000-000000000003", "Person hierarchy",
                "This restriction applies in a person hierarchy; inherited parent order and local child templates do not prove one global order, so keep it with this reason.",
                [rules[4].Guid]),
            new AdhocGroupFixture("40000000-0000-0000-0000-000000000004", "Nonordering constraints",
                "Anywhere and conjunctive multi-target rules express scope or joint membership, not one ordered pair; keep them with this reason.",
                [rules[5].Guid, rules[6].Guid, rules[12].Guid]),
        ]);
        Execute("UPDATE artifact_section SET status='complete', reason_code=NULL " +
                "WHERE section IN ('adhoc_prohibitions', 'adhoc_groups', 'compiled_mappings', 'load_accounting');");
        transaction.Commit();
        AddAdhocApprovedCounterexample(evidencePath);
    }

    private static void AddAdhocApprovedCounterexample(string evidencePath)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(evidencePath), Pooling = false, ForeignKeys = true,
        }.ToString());
        connection.Open();
        using var transaction = connection.BeginTransaction();
        void Execute(string sql, params (string Name, object Value)[] values)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            foreach (var (name, value) in values) command.Parameters.AddWithValue(name, value);
            command.ExecuteNonQuery();
        }

        Execute("INSERT INTO wordforms(wordform_guid, spelling_status) VALUES ($guid, 0);",
            ("$guid", AdhocSlotOrderCounterexampleWordformGuid));
        Execute("INSERT INTO wordform_forms(wordform_guid, writing_system, form, form_nfd) " +
                "VALUES ($guid, 'qaa', 'dcd', 'dcd');",
            ("$guid", AdhocSlotOrderCounterexampleWordformGuid));
        Execute("INSERT INTO analyses(analysis_guid, wordform_guid, opinion, source_kind, content_sha256) " +
                "VALUES ($analysis, $wordform, 'unknown', 'manual', $digest);",
            ("$analysis", AdhocSlotOrderCounterexampleAnalysisGuid), ("$wordform", AdhocSlotOrderCounterexampleWordformGuid),
            ("$digest", new string('b', 64)));
        var morphs = new[]
        {
            (Morph: "20000000-0000-0000-0000-000000000210", Msa: "20000000-0000-0000-0000-000000000110",
                Entry: "20000000-0000-0000-0000-000000000010", Form: "root", Gloss: "root"),
            (Morph: "20000000-0000-0000-0000-000000000214", Msa: "20000000-0000-0000-0000-000000000114",
                Entry: "20000000-0000-0000-0000-000000000014", Form: "d", Gloss: "D"),
            (Morph: "20000000-0000-0000-0000-000000000213", Msa: "20000000-0000-0000-0000-000000000113",
                Entry: "20000000-0000-0000-0000-000000000013", Form: "c", Gloss: "C"),
            (Morph: "20000000-0000-0000-0000-000000000214", Msa: "20000000-0000-0000-0000-000000000114",
                Entry: "20000000-0000-0000-0000-000000000014", Form: "d", Gloss: "D"),
        };
        for (var ordinal = 0; ordinal < morphs.Length; ordinal++)
        {
            var morph = morphs[ordinal];
            Execute("INSERT INTO analysis_morphs(analysis_guid, ordinal, morph_guid, msa_guid, entry_guid) " +
                    "VALUES ($analysis, $ordinal, $morph, $msa, $entry);",
                ("$analysis", AdhocSlotOrderCounterexampleAnalysisGuid), ("$ordinal", ordinal), ("$morph", morph.Morph),
                ("$msa", morph.Msa), ("$entry", morph.Entry));
            Execute("INSERT INTO analysis_morph_forms(analysis_guid, morph_ordinal, writing_system, form, form_nfd) " +
                    "VALUES ($analysis, $ordinal, 'qaa', $form, $form);",
                ("$analysis", AdhocSlotOrderCounterexampleAnalysisGuid), ("$ordinal", ordinal), ("$form", morph.Form));
            Execute("INSERT INTO analysis_morph_texts(analysis_guid, morph_ordinal, kind, writing_system, text, text_nfd) " +
                    "VALUES ($analysis, $ordinal, 'category', 'qaa', 'verb', 'verb');",
                ("$analysis", AdhocSlotOrderCounterexampleAnalysisGuid), ("$ordinal", ordinal));
            Execute("INSERT INTO analysis_morph_texts(analysis_guid, morph_ordinal, kind, writing_system, text, text_nfd) " +
                    "VALUES ($analysis, $ordinal, 'gloss', 'qaa', $gloss, $gloss);",
                ("$analysis", AdhocSlotOrderCounterexampleAnalysisGuid), ("$ordinal", ordinal), ("$gloss", morph.Gloss));
        }
        Execute("UPDATE artifact_metadata SET wordform_count=(SELECT COUNT(*) FROM wordforms), " +
                "analysis_count=(SELECT COUNT(*) FROM analyses) WHERE singleton=1;");
        transaction.Commit();
    }

    private static void SetApprovedAnalysisOpinion(string evidencePath, string analysisGuid, string opinion)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(evidencePath), Pooling = false, ForeignKeys = true,
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE analyses SET opinion=$opinion WHERE analysis_guid=$guid;";
        command.Parameters.AddWithValue("$opinion", opinion);
        command.Parameters.AddWithValue("$guid", analysisGuid);
        Assert.Equal(1, command.ExecuteNonQuery());
    }

    private static void SetAdhocGroupSection(string factsPath, string status)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(factsPath), Pooling = false, ForeignKeys = true,
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE artifact_section SET status=$status, reason_code='not published' " +
            "WHERE section='adhoc_groups';";
        command.Parameters.AddWithValue("$status", status);
        Assert.Equal(1, command.ExecuteNonQuery());
    }

    private static void RemoveAdhocGroupRows(string factsPath)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(factsPath), Pooling = false, ForeignKeys = true,
        }.ToString());
        connection.Open();
        using var transaction = connection.BeginTransaction();
        foreach (var table in new[] { "adhoc_group_text", "adhoc_group_member", "adhoc_group" })
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"DELETE FROM {table};";
            command.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    private static void AddAdhocGroupFixtures(Action<string, (string Name, object Value)[]> execute,
        IReadOnlyList<AdhocGroupFixture> groups)
    {
        foreach (var group in groups)
        {
            execute("INSERT INTO adhoc_group(group_guid) VALUES ($guid);", [("$guid", group.Guid)]);
            execute("INSERT INTO adhoc_group_text(group_guid, field, writing_system, text) " +
                    "VALUES ($guid, 'name', 'qaa', $text);",
                [("$guid", group.Guid), ("$text", group.Name)]);
            execute("INSERT INTO adhoc_group_text(group_guid, field, writing_system, text) " +
                    "VALUES ($guid, 'description', 'qaa', $text);",
                [("$guid", group.Guid), ("$text", group.Description)]);
            foreach (var member in group.Members)
                execute("INSERT INTO adhoc_group_member(group_guid, member_guid) VALUES ($group, $member);",
                    [("$group", group.Guid), ("$member", member)]);
        }
    }

    private sealed record Rule(string Guid, string Kind, string Adjacency, string PrimaryGuid, string TargetKind,
        (string Guid, string Kind)[] Others, bool Loaded);

    private sealed record AdhocGroupFixture(string Guid, string Name, string Description,
        IReadOnlyList<string> Members);

    private static void AddNamedMeasureFacts(string factsPath)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(factsPath),
            Pooling = false,
            ForeignKeys = true,
        }.ToString());
        connection.Open();
        using var transaction = connection.BeginTransaction();
        void Execute(string sql, params (string Name, object Value)[] values)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            foreach (var (name, value) in values) command.Parameters.AddWithValue(name, value);
            command.ExecuteNonQuery();
        }

        const string entry = "10000000-0000-0000-0000-000000000001";
        const string matchingOne = "10000000-0000-0000-0000-000000000011";
        const string matchingTwo = "10000000-0000-0000-0000-000000000012";
        const string differentEnvironment = "10000000-0000-0000-0000-000000000013";
        const string abstractDuplicate = "10000000-0000-0000-0000-000000000014";
        const string unusedEnvironment = "10000000-0000-0000-0000-000000000021";
        const string droppedEnvironment = "10000000-0000-0000-0000-000000000022";
        const string unusedNaturalClass = "10000000-0000-0000-0000-000000000031";
        const string droppedNaturalClass = "10000000-0000-0000-0000-000000000032";
        Execute("INSERT INTO writing_system(tag) VALUES ('qaa');");
        Execute("INSERT INTO lex_entry(guid, source_ordinal, lexeme_morph_type) VALUES ($guid, 0, 'stem');",
            ("$guid", entry));
        foreach (var (guid, ordinal, isAbstract) in new[]
                 {
                     (matchingOne, 0, false), (matchingTwo, 1, false), (differentEnvironment, 2, false),
                     (abstractDuplicate, 3, true),
                 })
        {
            Execute("INSERT INTO allomorph(guid, entry_guid, ordinal, morph_type, form_class, is_abstract) " +
                    "VALUES ($guid, $entry, $ordinal, 'stem', 'stem', $abstract);",
                ("$guid", guid), ("$entry", entry), ("$ordinal", ordinal), ("$abstract", isAbstract ? 1 : 0));
            Execute("INSERT INTO allomorph_form(allomorph_guid, ordinal, writing_system, form) " +
                    "VALUES ($guid, 0, 'qaa', 'puᵀ');", ("$guid", guid));
        }
        foreach (var guid in new[] { unusedEnvironment, droppedEnvironment })
            Execute("INSERT INTO environment(guid, name, representation, parse_status) " +
                    "VALUES ($guid, $name, '_', 'valid');",
                ("$guid", guid), ("$name", guid == unusedEnvironment ? "unused" : "dropped"));
        Execute("INSERT INTO allomorph_environment(allomorph_guid, role, ordinal, environment_guid) " +
                "VALUES ($allomorph, 'phone', 0, $environment);",
            ("$allomorph", differentEnvironment), ("$environment", droppedEnvironment));
        Execute("INSERT INTO environment_usage(allomorph_guid, role, ordinal, compile_context_key, " +
                "environment_guid, resolved_environment_guid, compiled, result) " +
                "VALUES ($allomorph, 'phone', 0, 'default', $environment, $environment, 0, 'owner_not_loaded');",
            ("$allomorph", differentEnvironment), ("$environment", droppedEnvironment));
        Execute("INSERT INTO natural_class(guid, kind, name) VALUES ($guid, 'segments', $name);",
            ("$guid", unusedNaturalClass), ("$name", "unused"));
        Execute("INSERT INTO natural_class(guid, kind, name) VALUES ($guid, 'segments', $name);",
            ("$guid", droppedNaturalClass), ("$name", "dropped"));
        Execute("INSERT INTO environment_natural_class(environment_guid, compile_context_key, side, token_path, " +
                "token_text, source_start, source_end, natural_class_guid, result) " +
                "VALUES ($environment, 'default', 'left', '0', '[NC]', 0, 4, $class, 'resolved');",
            ("$environment", droppedEnvironment), ("$class", droppedNaturalClass));
        foreach (var (guid, kind) in new[]
                 { (droppedEnvironment, "environment"), (droppedNaturalClass, "naturalClass") })
            Execute("INSERT INTO load_fact(subject_kind, subject_key, pipeline_stage, decision_ordinal, " +
                    "subject_guid, context_key, disposition, loaded, reason_code) " +
                    "VALUES ($kind, $guid, 'compile', 0, $guid, 'default', 'rejected', 0, 'unsupported');",
                ("$kind", kind), ("$guid", guid));
        Execute("INSERT INTO statement_reference(target_kind, target_guid, referrer_kind, referrer_guid, role, " +
                "ordinal, parser_effect) VALUES ('environment', $environment, 'allomorph', $allomorph, " +
                "'phone_env', 0, 'owner_not_loaded');",
            ("$environment", droppedEnvironment), ("$allomorph", differentEnvironment));
        Execute("INSERT INTO statement_reference(target_kind, target_guid, referrer_kind, referrer_guid, role, " +
                "ordinal, parser_effect) VALUES ('naturalClass', $class, 'environment', $environment, " +
                "'env_token', 0, 'not_attempted');",
            ("$class", droppedNaturalClass), ("$environment", droppedEnvironment));
        Execute("UPDATE artifact_section SET status='complete', reason_code=NULL " +
                "WHERE section IN ('environments', 'features', 'phonology', 'patterns', 'load_accounting', " +
                "'compound_rules', 'affix_processes');");
        transaction.Commit();
    }

    private static void AddTemplateMeasureFacts(string factsPath, string evidencePath)
    {
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
               { DataSource = Path.GetFullPath(factsPath), Pooling = false, ForeignKeys = true }.ToString()))
        {
            connection.Open();
            using var transaction = connection.BeginTransaction();
            void Execute(string sql, params (string Name, object Value)[] values)
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = sql;
                foreach (var (name, value) in values) command.Parameters.AddWithValue(name, value);
                command.ExecuteNonQuery();
            }

            Execute("INSERT INTO category(guid, parent_guid, sibling_ordinal, name, abbreviation) " +
                    "VALUES ($guid, NULL, 0, 'Ancestor category', 'anc');", ("$guid", RootCategoryGuid));
            Execute("INSERT INTO category(guid, parent_guid, sibling_ordinal, name, abbreviation) " +
                    "VALUES ($guid, $parent, 0, 'Child category', 'child');",
                ("$guid", ChildCategoryGuid), ("$parent", RootCategoryGuid));

            const string rootEntry = "20000000-0000-0000-0000-000000000010";
            const string rootMsa = "20000000-0000-0000-0000-000000000110";
            const string rootMorph = "20000000-0000-0000-0000-000000000210";
            Execute("INSERT INTO lex_entry(guid, source_ordinal, lexeme_morph_type) VALUES ($guid, 0, 'stem');",
                ("$guid", rootEntry));
            Execute("INSERT INTO msa(msa_guid, entry_guid, kind) VALUES ($msa, $entry, 'stem');",
                ("$msa", rootMsa), ("$entry", rootEntry));
            Execute("INSERT INTO msa_category(msa_guid, role, ordinal, category_guid) " +
                    "VALUES ($msa, 'pos', 0, $category);",
                ("$msa", rootMsa), ("$category", ChildCategoryGuid));
            Execute("INSERT INTO allomorph(guid, entry_guid, ordinal, morph_type, form_class, is_abstract) " +
                    "VALUES ($guid, $entry, 0, 'stem', 'stem', 0);",
                ("$guid", rootMorph), ("$entry", rootEntry));
            Execute("INSERT INTO allomorph_form(allomorph_guid, ordinal, writing_system, form) " +
                    "VALUES ($guid, 0, 'qaa', 'root');", ("$guid", rootMorph));

            var affixes = new[]
            {
                (Entry: "20000000-0000-0000-0000-000000000011", Msa: "20000000-0000-0000-0000-000000000111",
                    Morph: "20000000-0000-0000-0000-000000000211", Slot: "20000000-0000-0000-0000-000000000221",
                    Name: "A slot", Form: "a", Optional: true),
                (Entry: "20000000-0000-0000-0000-000000000012", Msa: "20000000-0000-0000-0000-000000000112",
                    Morph: "20000000-0000-0000-0000-000000000212", Slot: "20000000-0000-0000-0000-000000000222",
                    Name: "B slot", Form: "b", Optional: true),
                (Entry: "20000000-0000-0000-0000-000000000013", Msa: "20000000-0000-0000-0000-000000000113",
                    Morph: "20000000-0000-0000-0000-000000000213", Slot: "20000000-0000-0000-0000-000000000223",
                    Name: "C slot", Form: "c", Optional: true),
                (Entry: "20000000-0000-0000-0000-000000000014", Msa: "20000000-0000-0000-0000-000000000114",
                    Morph: "20000000-0000-0000-0000-000000000214", Slot: "20000000-0000-0000-0000-000000000224",
                    Name: "D slot", Form: "d", Optional: true),
                (Entry: "20000000-0000-0000-0000-000000000015", Msa: "20000000-0000-0000-0000-000000000115",
                    Morph: "20000000-0000-0000-0000-000000000215", Slot: "20000000-0000-0000-0000-000000000225",
                    Name: "E slot", Form: "e", Optional: true),
                (Entry: "20000000-0000-0000-0000-000000000016", Msa: "20000000-0000-0000-0000-000000000116",
                    Morph: "20000000-0000-0000-0000-000000000216", Slot: RequiredSlotGuid,
                    Name: "Required Number", Form: "num", Optional: false),
            };
            foreach (var affix in affixes)
            {
                Execute("INSERT INTO lex_entry(guid, source_ordinal, lexeme_morph_type) " +
                        "VALUES ($guid, 1, 'affix');", ("$guid", affix.Entry));
                Execute("INSERT INTO msa(msa_guid, entry_guid, kind) VALUES ($msa, $entry, 'inflectional');",
                    ("$msa", affix.Msa), ("$entry", affix.Entry));
                Execute("INSERT INTO msa_slot(msa_guid, role, ordinal, slot_guid) " +
                        "VALUES ($msa, 'slot', 0, $slot);",
                    ("$msa", affix.Msa), ("$slot", affix.Slot));
                Execute("INSERT INTO allomorph(guid, entry_guid, ordinal, morph_type, form_class, is_abstract) " +
                        "VALUES ($guid, $entry, 0, 'suffix', 'affix', 0);",
                    ("$guid", affix.Morph), ("$entry", affix.Entry));
                Execute("INSERT INTO allomorph_form(allomorph_guid, ordinal, writing_system, form) " +
                        "VALUES ($guid, 0, 'qaa', $form);",
                    ("$guid", affix.Morph), ("$form", affix.Form));
                Execute("INSERT INTO affix_slot(guid, category_guid, name, optional) " +
                        "VALUES ($guid, $category, $name, $optional);",
                    ("$guid", affix.Slot), ("$category", RootCategoryGuid), ("$name", affix.Name),
                    ("$optional", affix.Optional ? 1 : 0));
            }

            Execute("INSERT INTO affix_template(guid, category_guid, name, disabled, is_final) " +
                    "VALUES ($guid, $category, 'Ancestor order', 0, 0);",
                ("$guid", AncestorTemplateGuid), ("$category", RootCategoryGuid));
            Execute("INSERT INTO affix_template(guid, category_guid, name, disabled, is_final) " +
                    "VALUES ($guid, $category, 'Child alternative', 0, 0);",
                ("$guid", ChildTemplateGuid), ("$category", ChildCategoryGuid));
            Execute("INSERT INTO affix_template(guid, category_guid, name, disabled, is_final) " +
                    "VALUES ($guid, $category, 'Child shared required', 0, 0);",
                ("$guid", ChildSharedNumberTemplateGuid), ("$category", ChildCategoryGuid));
            Execute("INSERT INTO affix_template(guid, category_guid, name, disabled, is_final) " +
                    "VALUES ($guid, $category, '', 0, 0);",
                ("$guid", UnnamedTemplateGuid), ("$category", RootCategoryGuid));
            Execute("INSERT INTO affix_template(guid, category_guid, name, disabled, is_final) " +
                    "VALUES ($guid, $category, 'Prefix traversal', 1, 0);",
                ("$guid", PrefixTemplateGuid), ("$category", RootCategoryGuid));
            var ancestorSlots = new[]
            {
                (Slot: affixes[3].Slot, Ordinal: 0, Compiled: 0),
                (Slot: affixes[2].Slot, Ordinal: 1, Compiled: 1),
                (Slot: affixes[0].Slot, Ordinal: 2, Compiled: 2),
                (Slot: affixes[1].Slot, Ordinal: 3, Compiled: 3),
                (Slot: affixes[4].Slot, Ordinal: 4, Compiled: 4),
                (Slot: affixes[5].Slot, Ordinal: 5, Compiled: 5),
            };
            foreach (var slot in ancestorSlots)
                Execute("INSERT INTO template_slot(template_guid, side, ordinal, slot_guid, compiled_order) " +
                        "VALUES ($template, 'suffix', $ordinal, $slot, $compiled);",
                    ("$template", AncestorTemplateGuid), ("$ordinal", slot.Ordinal),
                    ("$slot", slot.Slot), ("$compiled", slot.Compiled));
            foreach (var slot in new[] { (Slot: affixes[1].Slot, Ordinal: 0), (Slot: affixes[0].Slot, Ordinal: 1) })
                Execute("INSERT INTO template_slot(template_guid, side, ordinal, slot_guid, compiled_order) " +
                        "VALUES ($template, 'suffix', $ordinal, $slot, $ordinal);",
                    ("$template", ChildTemplateGuid), ("$ordinal", slot.Ordinal), ("$slot", slot.Slot));
            foreach (var slot in new[] { (Slot: affixes[4].Slot, Ordinal: 0),
                         (Slot: affixes[5].Slot, Ordinal: 1) })
                Execute("INSERT INTO template_slot(template_guid, side, ordinal, slot_guid, compiled_order) " +
                        "VALUES ($template, 'suffix', $ordinal, $slot, $ordinal);",
                    ("$template", ChildSharedNumberTemplateGuid), ("$ordinal", slot.Ordinal), ("$slot", slot.Slot));
            foreach (var slot in new[]
                     {
                         (Slot: affixes[0].Slot, Ordinal: 0, Compiled: 1),
                         (Slot: affixes[1].Slot, Ordinal: 1, Compiled: 0),
                     })
                Execute("INSERT INTO template_slot(template_guid, side, ordinal, slot_guid, compiled_order) " +
                        "VALUES ($template, 'prefix', $ordinal, $slot, $compiled);",
                    ("$template", PrefixTemplateGuid), ("$ordinal", slot.Ordinal),
                    ("$slot", slot.Slot), ("$compiled", slot.Compiled));
            Execute("UPDATE artifact_section SET status='complete', reason_code=NULL " +
                    "WHERE section IN ('categories', 'templates', 'msas', 'entries', 'allomorphs');");
            transaction.Commit();
        }

        using var evidence = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(evidencePath), Pooling = false, ForeignKeys = true,
        }.ToString());
        evidence.Open();
        using var evidenceTransaction = evidence.BeginTransaction();
        void InsertEvidence(string sql, params (string Name, object Value)[] values)
        {
            using var command = evidence.CreateCommand();
            command.Transaction = evidenceTransaction;
            command.CommandText = sql;
            foreach (var (name, value) in values) command.Parameters.AddWithValue(name, value);
            command.ExecuteNonQuery();
        }

        var root = (Morph: "20000000-0000-0000-0000-000000000210",
            Msa: "20000000-0000-0000-0000-000000000110",
            Entry: "20000000-0000-0000-0000-000000000010", Form: "root", Gloss: "root");
        var analyses = new[]
        {
            (Analysis: "20000000-0000-0000-0000-000000000241", Wordform: "20000000-0000-0000-0000-000000000251",
                Word: "ab", Morphs: new[] { root,
                    (Morph: "20000000-0000-0000-0000-000000000211", Msa: "20000000-0000-0000-0000-000000000111",
                        Entry: "20000000-0000-0000-0000-000000000011", Form: "a", Gloss: "A"),
                    (Morph: "20000000-0000-0000-0000-000000000212", Msa: "20000000-0000-0000-0000-000000000112",
                        Entry: "20000000-0000-0000-0000-000000000012", Form: "b", Gloss: "B") }),
            (Analysis: "20000000-0000-0000-0000-000000000242", Wordform: "20000000-0000-0000-0000-000000000252",
                Word: "ba", Morphs: new[] { root,
                    (Morph: "20000000-0000-0000-0000-000000000212", Msa: "20000000-0000-0000-0000-000000000112",
                        Entry: "20000000-0000-0000-0000-000000000012", Form: "b", Gloss: "B"),
                    (Morph: "20000000-0000-0000-0000-000000000211", Msa: "20000000-0000-0000-0000-000000000111",
                        Entry: "20000000-0000-0000-0000-000000000011", Form: "a", Gloss: "A") }),
            (Analysis: ContradictedAnalysisGuid, Wordform: "20000000-0000-0000-0000-000000000253",
                Word: "cd", Morphs: new[] { root,
                    (Morph: "20000000-0000-0000-0000-000000000213", Msa: "20000000-0000-0000-0000-000000000113",
                        Entry: "20000000-0000-0000-0000-000000000013", Form: "c", Gloss: "C"),
                    (Morph: "20000000-0000-0000-0000-000000000214", Msa: "20000000-0000-0000-0000-000000000114",
                        Entry: "20000000-0000-0000-0000-000000000014", Form: "d", Gloss: "D") }),
            (Analysis: FillerlessAnalysisGuid, Wordform: "20000000-0000-0000-0000-000000000254",
                Word: "e", Morphs: new[] { root,
                    (Morph: "20000000-0000-0000-0000-000000000215", Msa: "20000000-0000-0000-0000-000000000115",
                        Entry: "20000000-0000-0000-0000-000000000015", Form: "e", Gloss: "E") }),
        };
        foreach (var item in analyses)
        {
            InsertEvidence("INSERT INTO wordforms(wordform_guid, spelling_status) VALUES ($guid, 0);",
                ("$guid", item.Wordform));
            InsertEvidence("INSERT INTO wordform_forms(wordform_guid, writing_system, form, form_nfd) " +
                           "VALUES ($guid, 'qaa', $form, $form);",
                ("$guid", item.Wordform), ("$form", item.Word));
            InsertEvidence("INSERT INTO analyses(analysis_guid, wordform_guid, opinion, source_kind, content_sha256) " +
                           "VALUES ($analysis, $wordform, 'approved', 'manual', $digest);",
                ("$analysis", item.Analysis), ("$wordform", item.Wordform), ("$digest", new string('a', 64)));
            for (var ordinal = 0; ordinal < item.Morphs.Length; ordinal++)
            {
                var morph = item.Morphs[ordinal];
                InsertEvidence("INSERT INTO analysis_morphs(analysis_guid, ordinal, morph_guid, msa_guid, entry_guid) " +
                               "VALUES ($analysis, $ordinal, $morph, $msa, $entry);",
                    [("$analysis", (object)item.Analysis), ("$ordinal", ordinal), ("$morph", morph.Morph),
                        ("$msa", morph.Msa), ("$entry", morph.Entry)]);
                InsertEvidence("INSERT INTO analysis_morph_forms(analysis_guid, morph_ordinal, writing_system, form, form_nfd) " +
                               "VALUES ($analysis, $ordinal, 'qaa', $form, $form);",
                    [("$analysis", (object)item.Analysis), ("$ordinal", ordinal), ("$form", morph.Form)]);
                InsertEvidence("INSERT INTO analysis_morph_texts(analysis_guid, morph_ordinal, kind, writing_system, text, text_nfd) " +
                               "VALUES ($analysis, $ordinal, 'category', 'qaa', 'verb', 'verb');",
                    [("$analysis", (object)item.Analysis), ("$ordinal", ordinal)]);
                InsertEvidence("INSERT INTO analysis_morph_texts(analysis_guid, morph_ordinal, kind, writing_system, text, text_nfd) " +
                               "VALUES ($analysis, $ordinal, 'gloss', 'qaa', $gloss, $gloss);",
                    [("$analysis", (object)item.Analysis), ("$ordinal", ordinal), ("$gloss", morph.Gloss)]);
            }
        }
        InsertEvidence("UPDATE artifact_metadata SET wordform_count=(SELECT COUNT(*) FROM wordforms), " +
                       "analysis_count=(SELECT COUNT(*) FROM analyses) WHERE singleton=1;");
        evidenceTransaction.Commit();
    }

    private static void AddPositiveAndNegativeAlternativeTemplates(string factsPath, string evidencePath)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(factsPath),
            Pooling = false,
            ForeignKeys = true,
        }.ToString());
        connection.Open();
        using var transaction = connection.BeginTransaction();
        void Execute(string sql, params (string Name, object Value)[] values)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            foreach (var (name, value) in values) command.Parameters.AddWithValue(name, value);
            command.ExecuteNonQuery();
        }

        const string negativeEntry = "20000000-0000-0000-0000-000000000017";
        const string negativeMsa = "20000000-0000-0000-0000-000000000117";
        Execute("INSERT INTO lex_entry(guid, source_ordinal, lexeme_morph_type) VALUES ($guid, 1, 'affix');",
            ("$guid", negativeEntry));
        Execute("INSERT INTO msa(msa_guid, entry_guid, kind) VALUES ($msa, $entry, 'inflectional');",
            ("$msa", negativeMsa), ("$entry", negativeEntry));
        Execute("INSERT INTO msa_slot(msa_guid, role, ordinal, slot_guid) " +
                "VALUES ($msa, 'slot', 0, $slot);",
            ("$msa", negativeMsa), ("$slot", NegativeSlotGuid));
        Execute("INSERT INTO affix_slot(guid, category_guid, name, optional) " +
                "VALUES ($guid, $category, 'neg', 0);",
            ("$guid", NegativeSlotGuid), ("$category", RootCategoryGuid));
        Execute("INSERT INTO affix_template(guid, category_guid, name, disabled, is_final) " +
                "VALUES ($guid, $category, 'Positive verb', 0, 0);",
            ("$guid", PositiveAlternativeTemplateGuid), ("$category", RootCategoryGuid));
        Execute("INSERT INTO affix_template(guid, category_guid, name, disabled, is_final) " +
                "VALUES ($guid, $category, 'Negative verb', 0, 0);",
            ("$guid", NegativeAlternativeTemplateGuid), ("$category", RootCategoryGuid));
        var positiveSlots = new[]
        {
            "20000000-0000-0000-0000-000000000221",
            "20000000-0000-0000-0000-000000000222",
        };
        for (var index = 0; index < positiveSlots.Length; index++)
        {
            Execute("INSERT INTO template_slot(template_guid, side, ordinal, slot_guid, compiled_order) " +
                    "VALUES ($template, 'suffix', $ordinal, $slot, $compiled);",
                ("$template", PositiveAlternativeTemplateGuid), ("$ordinal", index),
                ("$slot", positiveSlots[index]), ("$compiled", index));
            Execute("INSERT INTO template_slot(template_guid, side, ordinal, slot_guid, compiled_order) " +
                    "VALUES ($template, 'suffix', $ordinal, $slot, $compiled);",
                ("$template", NegativeAlternativeTemplateGuid), ("$ordinal", index),
                ("$slot", positiveSlots[index]), ("$compiled", index));
        }
        Execute("INSERT INTO template_slot(template_guid, side, ordinal, slot_guid, compiled_order) " +
                "VALUES ($template, 'suffix', 2, $slot, 2);",
            ("$template", NegativeAlternativeTemplateGuid), ("$slot", NegativeSlotGuid));
        AddAlternativeFailureTemplateFacts(Execute);
        transaction.Commit();

        AddAlternativeFailureEvidence(evidencePath);
    }

    private static void AddAlternativeFailureTemplateFacts(Action<string, (string Name, object Value)[]> execute)
    {
        const string fillerEntry = "20000000-0000-0000-0000-000000000018";
        const string firstEntry = "20000000-0000-0000-0000-000000000019";
        const string secondEntry = "20000000-0000-0000-0000-000000000020";
        const string fillerMsa = "20000000-0000-0000-0000-000000000118";
        const string firstMsa = "20000000-0000-0000-0000-000000000119";
        const string secondMsa = "20000000-0000-0000-0000-000000000120";
        var slots = new[]
        {
            (Entry: fillerEntry, Msa: fillerMsa, Slot: AlternativeFillerSlotGuid, Name: "Filler", Optional: true),
            (Entry: firstEntry, Msa: firstMsa, Slot: AlternativeRequiredSlotOneGuid, Name: "Required One", Optional: false),
            (Entry: secondEntry, Msa: secondMsa, Slot: AlternativeRequiredSlotTwoGuid, Name: "Required Two", Optional: false),
        };
        foreach (var slot in slots)
        {
            execute("INSERT INTO lex_entry(guid, source_ordinal, lexeme_morph_type) " +
                    "VALUES ($entry, 1, 'affix');", [("$entry", slot.Entry)]);
            execute("INSERT INTO msa(msa_guid, entry_guid, kind) VALUES ($msa, $entry, 'inflectional');",
                [("$msa", slot.Msa), ("$entry", slot.Entry)]);
            execute("INSERT INTO msa_slot(msa_guid, role, ordinal, slot_guid) " +
                    "VALUES ($msa, 'slot', 0, $slot);", [("$msa", slot.Msa), ("$slot", slot.Slot)]);
            execute("INSERT INTO affix_slot(guid, category_guid, name, optional) " +
                    "VALUES ($slot, $category, $name, $optional);",
                [("$slot", slot.Slot), ("$category", RootCategoryGuid), ("$name", slot.Name),
                    ("$optional", slot.Optional ? 1 : 0)]);
        }
        execute("INSERT INTO allomorph(guid, entry_guid, ordinal, morph_type, form_class, is_abstract) " +
                "VALUES ('20000000-0000-0000-0000-000000000217', '20000000-0000-0000-0000-000000000018', " +
                "0, 'suffix', 'affix', 0);", Array.Empty<(string Name, object Value)>());
        execute("INSERT INTO allomorph_form(allomorph_guid, ordinal, writing_system, form) " +
                "VALUES ('20000000-0000-0000-0000-000000000217', 0, 'qaa', 'x');",
            Array.Empty<(string Name, object Value)>());
        execute("INSERT INTO affix_template(guid, category_guid, name, disabled, is_final) " +
                "VALUES ($guid, $category, 'Alternative A', 0, 0);",
            [("$guid", AlternativeFailureTemplateOneGuid), ("$category", RootCategoryGuid)]);
        execute("INSERT INTO affix_template(guid, category_guid, name, disabled, is_final) " +
                "VALUES ($guid, $category, 'Alternative B', 0, 0);",
            [("$guid", AlternativeFailureTemplateTwoGuid), ("$category", RootCategoryGuid)]);
        foreach (var item in new[]
                 {
                     (Template: AlternativeFailureTemplateOneGuid, Slot: AlternativeFillerSlotGuid, Order: 0),
                     (Template: AlternativeFailureTemplateOneGuid, Slot: AlternativeRequiredSlotOneGuid, Order: 1),
                     (Template: AlternativeFailureTemplateTwoGuid, Slot: AlternativeFillerSlotGuid, Order: 0),
                     (Template: AlternativeFailureTemplateTwoGuid, Slot: AlternativeRequiredSlotTwoGuid, Order: 1),
                 })
            execute("INSERT INTO template_slot(template_guid, side, ordinal, slot_guid, compiled_order) " +
                    "VALUES ($template, 'suffix', $order, $slot, $order);",
                [("$template", item.Template), ("$order", item.Order), ("$slot", item.Slot)]);
    }

    private static void AddAlternativeFailureEvidence(string evidencePath)
    {
        const string root = "20000000-0000-0000-0000-000000000210";
        const string rootMsa = "20000000-0000-0000-0000-000000000110";
        const string rootEntry = "20000000-0000-0000-0000-000000000010";
        const string fillerMorph = "20000000-0000-0000-0000-000000000217";
        const string fillerMsa = "20000000-0000-0000-0000-000000000118";
        const string fillerEntry = "20000000-0000-0000-0000-000000000018";
        const string wordform = "20000000-0000-0000-0000-000000000255";
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(evidencePath),
            Pooling = false,
            ForeignKeys = true,
        }.ToString());
        connection.Open();
        using var transaction = connection.BeginTransaction();
        void Execute(string sql, params (string Name, object Value)[] values)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            foreach (var (name, value) in values) command.Parameters.AddWithValue(name, value);
            command.ExecuteNonQuery();
        }

        Execute("INSERT INTO wordforms(wordform_guid, spelling_status) VALUES ($guid, 0);", ("$guid", wordform));
        Execute("INSERT INTO wordform_forms(wordform_guid, writing_system, form, form_nfd) " +
                "VALUES ($guid, 'qaa', 'x', 'x');", ("$guid", wordform));
        Execute("INSERT INTO analyses(analysis_guid, wordform_guid, opinion, source_kind, content_sha256) " +
                "VALUES ($analysis, $wordform, 'approved', 'manual', $digest);",
            ("$analysis", AlternativeBlockedAnalysisGuid), ("$wordform", wordform),
            ("$digest", new string('b', 64)));
        foreach (var morph in new[]
                 {
                     (Guid: root, Msa: rootMsa, Entry: rootEntry, Form: "root", Gloss: "root"),
                     (Guid: fillerMorph, Msa: fillerMsa, Entry: fillerEntry, Form: "x", Gloss: "x"),
                 }.Select((morph, ordinal) => (morph, ordinal)))
        {
            Execute("INSERT INTO analysis_morphs(analysis_guid, ordinal, morph_guid, msa_guid, entry_guid) " +
                    "VALUES ($analysis, $ordinal, $morph, $msa, $entry);",
                ("$analysis", AlternativeBlockedAnalysisGuid), ("$ordinal", morph.ordinal),
                ("$morph", morph.morph.Guid), ("$msa", morph.morph.Msa), ("$entry", morph.morph.Entry));
            Execute("INSERT INTO analysis_morph_forms(analysis_guid, morph_ordinal, writing_system, form, form_nfd) " +
                    "VALUES ($analysis, $ordinal, 'qaa', $form, $form);",
                ("$analysis", AlternativeBlockedAnalysisGuid), ("$ordinal", morph.ordinal),
                ("$form", morph.morph.Form));
            Execute("INSERT INTO analysis_morph_texts(analysis_guid, morph_ordinal, kind, writing_system, text, text_nfd) " +
                    "VALUES ($analysis, $ordinal, 'category', 'qaa', 'verb', 'verb');",
                ("$analysis", AlternativeBlockedAnalysisGuid), ("$ordinal", morph.ordinal));
            Execute("INSERT INTO analysis_morph_texts(analysis_guid, morph_ordinal, kind, writing_system, text, text_nfd) " +
                    "VALUES ($analysis, $ordinal, 'gloss', 'qaa', $gloss, $gloss);",
                ("$analysis", AlternativeBlockedAnalysisGuid), ("$ordinal", morph.ordinal),
                ("$gloss", morph.morph.Gloss));
        }
        Execute("UPDATE artifact_metadata SET wordform_count=(SELECT COUNT(*) FROM wordforms), " +
                "analysis_count=(SELECT COUNT(*) FROM analyses) WHERE singleton=1;");
        transaction.Commit();
    }

    private async Task<string> PublishBaselineAsync(PristineProjectFixture fixture)
    {
        using var cache = fixture.NewScratch();
        _seededText = SeededProject.SeedText(cache, fixture.Seed);
        new FwDataProjectLoader().Save(cache);
        var publishedRoot = Path.Combine(_root, "published-baseline");
        var fwDataPath = await PublishedBaselineFixture.PublishAsync(cache, publishedRoot);
        _baselines.Record(_projectKey,
            new BaselinePublication(publishedRoot, fwDataPath, _token),
            DateTimeOffset.Parse("2026-10-05T00:00:00Z"), DateTimeOffset.Parse("2026-10-05T00:00:00Z"),
            TextWordsProjectionBuilder.Build(cache, CancellationToken.None), ProjectSummaryReader.Read(cache));
        return publishedRoot;
    }

    private string FactsParser(SeededProject seed)
    {
        var parserPath = FakeParser.CopyRecordingInvocations(Path.Combine(_root, "parser"));
        var primary = seed.FirstLexemeFormId.ToString("D").ToLowerInvariant();
        var other = seed.SecondLexemeFormId.ToString("D").ToLowerInvariant();
        FakeParser.WriteFactsFixture(parserPath, new
        {
            prohibitions = new[]
            {
                Prohibition("00000000-0000-0000-0000-000000000001", primary, other, "somewhereToLeft"),
                Prohibition("00000000-0000-0000-0000-000000000002", primary, other, "somewhereToLeft"),
                Prohibition("00000000-0000-0000-0000-000000000004", primary, other, "somewhereToLeft"),
                Prohibition("00000000-0000-0000-0000-000000000003", primary, other, "adjacentToLeft"),
                Prohibition("00000000-0000-0000-0000-000000000005", primary, other, "somewhereToLeft",
                    disabled: true),
                Prohibition("00000000-0000-0000-0000-000000000006", primary, other, "somewhereToLeft",
                    loaded: null),
                Prohibition("00000000-0000-0000-0000-000000000007", primary, other, "adjacentToRight",
                    compacted: true),
                Prohibition("00000000-0000-0000-0000-000000000008", primary, other, "adjacentToRight"),
            },
        });
        return parserPath;
    }

    private static object Prohibition(string id, string primary, string other, string adjacency,
        bool disabled = false, bool? loaded = true, bool compacted = false) => new
    {
        prohibitionGuid = id,
        kind = "allomorph",
        disabled,
        adjacency,
        primaryGuid = primary,
        targetKind = "allomorph",
        others = new[] { new { targetGuid = other, targetKind = "allomorph" } },
        loaded,
        compacted,
    };

    private JobRecord CreateJob(string measureId, ParsimonyScopeBinding? scopeBinding = null)
    {
        var input = JsonSerializer.Serialize(new ParsimonyJobInput(measureId,
            scopeBinding ?? new ParsimonyScopeBinding(ParsimonyEvidenceScopeKind.DefaultSelection, null)),
            SIL.Motif.Contract.MotifJson.CreateOptions());
        return _jobs.Create(Guid.NewGuid().ToString("N"), _projectKey, ParsimonyJobHandler.JobKind, input,
            "2026-10-05T00:00:00Z");
    }

    private ClaimedJob Claim(string jobId)
    {
        var claimed = new JobClaims(_database).Claim(_projectKey, "parsimony-report-test",
            JobTimestamp.FormatUtc(DateTimeOffset.UtcNow), TimeSpan.FromMinutes(5));
        Assert.NotNull(claimed);
        Assert.Equal(jobId, claimed.JobId);
        return new ClaimedJob(_jobs, claimed);
    }

    private static string Sha256Of(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    private static long CountRows(string path, string table)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(path),
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table};";
        return (long)command.ExecuteScalar()!;
    }

    private static string? ReadText(string path, string sql)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(path),
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar() as string;
    }

    private static string Sha256OfDirectory(string path)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                     .OrderBy(file => Path.GetRelativePath(path, file), StringComparer.Ordinal))
        {
            hash.AppendData(System.Text.Encoding.UTF8.GetBytes(Path.GetRelativePath(path, file)));
            hash.AppendData([0]);
            hash.AppendData(File.ReadAllBytes(file));
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    [Fact]
    public async Task UnconditionedAllomorphUsesCompilerOrderAndExcludesFallbackInvalidAndAmbiguousCases()
    {
        await PublishBaselineAsync(_pristine);
        var parserPath = FactsParser(_pristine.Seed);
        using var invoker = new SIL.Motif.Host.PanGloss.PanGlossInvoker(parserPath);
        var job = CreateJob("R-allo-unconditioned",
            new ParsimonyScopeBinding(ParsimonyEvidenceScopeKind.ProjectApproved, null));
        var handler = new ParsimonyJobHandler(_database, _baselines, _projectKey, _project,
            new RunnerOptions { Root = Path.Combine(_root, "unconditioned-worker"), ParserPath = parserPath }, invoker);
        var outcome = await handler.RunAsync(Claim(job.JobId), CancellationToken.None);
        Assert.Equal(JobStatus.Completed, outcome!.Status);

        using var result = JsonDocument.Parse(outcome.ResultJson!);
        var bundleId = result.RootElement.GetProperty("bundleId").GetString()!;
        var bundle = new ParsimonyBundleRepository(_database).Get(bundleId)!;
        AddNamedMeasureFacts(bundle.GrammarFactsPath);
        AddUnconditionedAllomorphFacts(bundle.GrammarFactsPath);
        var inputs = new ParsimonyReportInputs(bundle.BundleId, _token, "baseline", null,
            bundle.ModelFingerprint,
            new ParsimonyArtifactDigest(bundle.GrammarFactsSchemaVersion, Sha256Of(bundle.GrammarFactsPath)),
            new ParsimonyArtifactDigest(bundle.EvidenceSchemaVersion, bundle.EvidenceSha256),
            null, null, [], ParsimonyEvidenceScopeKind.ProjectApproved);
        using var session = new ParsimonyQuerySession(bundle.GrammarFactsPath, bundle.EvidencePath, inputs);

        Assert.True(MeasureRunner.Supports("R-allo-unconditioned"));
        var discovery = MeasureRunner.Execute("R-allo-unconditioned", session, bundleId);

        Assert.Equal(ParsimonyMeasureStatus.Inconclusive, discovery.Run.Status);
        Assert.Contains("Excluded 1 invalid phone restriction", discovery.Run.Detail, StringComparison.Ordinal);
        Assert.Contains("Excluded 1 family/families", discovery.Run.Detail, StringComparison.Ordinal);
        var conditionAnswer = ParsimonyMorphFixtureBuilder.For(_pristine.Seed, "R-allo-unconditioned").Answer;
        Assert.Equal(conditionAnswer.EligibleItems, discovery.Run.EligibleItems);
        Assert.Equal(conditionAnswer.FindingItems, discovery.Run.FindingItems);
        Assert.Equal(conditionAnswer.FindingTargets, discovery.Findings.Select(finding => finding.AttachesTo.Identity));
        Assert.DoesNotContain(discovery.Findings,
            finding => conditionAnswer.Exclusions.Contains(finding.AttachesTo.Identity, StringComparer.Ordinal));
        var finding = Assert.Single(discovery.Findings);
        Assert.Equal("30000000-0000-0000-0000-000000000011", finding.AttachesTo.Identity);
        Assert.Equal(new ParsimonyMeasureNumber(1, 3, "allomorphs"), finding.Number);
        Assert.Contains(finding.EvidenceRefs, reference => reference.View == "allomorph-context" &&
            reference.Arguments.GetProperty("objectGuid").GetString() == "30000000-0000-0000-0000-000000000012");
        Assert.Contains(finding.EvidenceRefs, reference => reference.View == "allomorph-context" &&
            reference.Arguments.GetProperty("objectGuid").GetString() == "30000000-0000-0000-0000-000000000013");
        Assert.DoesNotContain(discovery.Findings,
            item => item.AttachesTo.Identity == "30000000-0000-0000-0000-000000000014");
        Assert.Contains("entry \"unconditioned-allomorph-lemma\"", ParsimonyReportProducer.DescribeFinding(session, finding),
            StringComparison.Ordinal);
        var findingText = string.Join(" ", finding.Limitations);
        Assert.Contains("broad", findingText, StringComparison.Ordinal);
        Assert.Contains("conditioned", findingText, StringComparison.Ordinal);
        Assert.Contains("V _", findingText, StringComparison.Ordinal);
        Assert.Contains("final elsewhere control", findingText, StringComparison.Ordinal);
        Assert.Contains("selected-context counts are not shown", findingText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AConditionedTopSiblingLeavesTheUnconditionedCandidateWithoutAFinalElsewhereControl()
    {
        await RunUnconditionedMeasureAsync(path => AddFinalElsewhereFamilyFacts(path, gatedTop: false), result =>
        {
            var finding = Assert.Single(result.Findings);
            Assert.Equal("30000000-0000-0000-0000-000000000511", finding.AttachesTo.Identity);
            Assert.DoesNotContain("final elsewhere control", string.Join(" ", finding.Limitations),
                StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task AMorphGatedTopWithoutAPhoneConditionIsTheFinalElsewhereControl()
    {
        await RunUnconditionedMeasureAsync(path => AddFinalElsewhereFamilyFacts(path, gatedTop: true), result =>
        {
            var finding = Assert.Single(result.Findings);
            Assert.Equal("30000000-0000-0000-0000-000000000511", finding.AttachesTo.Identity);
            Assert.Contains("final elsewhere control", string.Join(" ", finding.Limitations),
                StringComparison.Ordinal);
        });
    }

    private async Task RunUnconditionedMeasureAsync(Action<string> addFacts, Action<ParsimonyMeasureResult> assert)
    {
        await PublishBaselineAsync(_pristine);
        var parserPath = FactsParser(_pristine.Seed);
        using var invoker = new SIL.Motif.Host.PanGloss.PanGlossInvoker(parserPath);
        var job = CreateJob("R-allo-unconditioned",
            new ParsimonyScopeBinding(ParsimonyEvidenceScopeKind.ProjectApproved, null));
        var handler = new ParsimonyJobHandler(_database, _baselines, _projectKey, _project,
            new RunnerOptions { Root = Path.Combine(_root, "final-elsewhere-worker"), ParserPath = parserPath }, invoker);
        var outcome = await handler.RunAsync(Claim(job.JobId), CancellationToken.None);
        Assert.Equal(JobStatus.Completed, outcome!.Status);

        using var result = JsonDocument.Parse(outcome.ResultJson!);
        var bundleId = result.RootElement.GetProperty("bundleId").GetString()!;
        var bundle = new ParsimonyBundleRepository(_database).Get(bundleId)!;
        AddNamedMeasureFacts(bundle.GrammarFactsPath);
        addFacts(bundle.GrammarFactsPath);
        var inputs = new ParsimonyReportInputs(bundle.BundleId, _token, "baseline", null,
            bundle.ModelFingerprint,
            new ParsimonyArtifactDigest(bundle.GrammarFactsSchemaVersion, Sha256Of(bundle.GrammarFactsPath)),
            new ParsimonyArtifactDigest(bundle.EvidenceSchemaVersion, bundle.EvidenceSha256),
            null, null, [], ParsimonyEvidenceScopeKind.ProjectApproved);
        using var session = new ParsimonyQuerySession(bundle.GrammarFactsPath, bundle.EvidencePath, inputs);
        assert(MeasureRunner.Execute("R-allo-unconditioned", session, bundleId));
    }

    [Fact]
    public async Task EnvironmentAndClassMeasuresDescribeAttestedTriggersWithoutCallingExtrasBad()
    {
        await PublishBaselineAsync(_pristine);
        var parserPath = FactsParser(_pristine.Seed);
        using var invoker = new SIL.Motif.Host.PanGloss.PanGlossInvoker(parserPath);
        var job = CreateJob("R-env-broad",
            new ParsimonyScopeBinding(ParsimonyEvidenceScopeKind.ProjectApproved, null));
        var handler = new ParsimonyJobHandler(_database, _baselines, _projectKey, _project,
            new RunnerOptions { Root = Path.Combine(_root, "environment-class-worker"), ParserPath = parserPath },
            invoker);
        var outcome = await handler.RunAsync(Claim(job.JobId), CancellationToken.None);
        Assert.Equal(JobStatus.Completed, outcome!.Status);

        using var result = JsonDocument.Parse(outcome.ResultJson!);
        var bundleId = result.RootElement.GetProperty("bundleId").GetString()!;
        var bundle = new ParsimonyBundleRepository(_database).Get(bundleId)!;
        AddEnvironmentAndNaturalClassFacts(bundle.GrammarFactsPath);
        AddEnvironmentAndNaturalClassEvidence(bundle.EvidencePath);
        var inputs = new ParsimonyReportInputs(bundle.BundleId, _token, "baseline", null,
            bundle.ModelFingerprint,
            new ParsimonyArtifactDigest(bundle.GrammarFactsSchemaVersion, Sha256Of(bundle.GrammarFactsPath)),
            new ParsimonyArtifactDigest(bundle.EvidenceSchemaVersion, Sha256Of(bundle.EvidencePath)),
            null, null, [], ParsimonyEvidenceScopeKind.ProjectApproved);
        using var session = new ParsimonyQuerySession(bundle.GrammarFactsPath, bundle.EvidencePath, inputs);

        var environment = MeasureRunner.Execute("R-env-broad", session, bundleId);
        Assert.Equal(ParsimonyMeasureStatus.Computed, environment.Run.Status);
        var environmentAnswer = ParsimonyMorphFixtureBuilder.For(_pristine.Seed, "R-env-broad").Answer;
        Assert.Equal(environmentAnswer.EligibleItems, environment.Run.EligibleItems);
        Assert.Equal(environmentAnswer.FindingItems, environment.Run.FindingItems);
        Assert.Equal(environmentAnswer.FindingTargets, environment.Findings.Select(finding => finding.AttachesTo.Identity));
        var environmentFinding = Assert.Single(environment.Findings,
            finding => finding.AttachesTo.Identity == BroadEnvironmentTargetAllomorphGuid);
        Assert.Equal(new ParsimonyMeasureNumber(1, 3, "context-types"), environmentFinding.Number);
        Assert.Equal(new[] { "b" }, environmentAnswer.ExpectedExcessMembers);
        var environmentText = string.Join(" ", environmentFinding.Limitations);
        Assert.Contains("extra licensed context: b", environmentText, StringComparison.Ordinal);
        Assert.Contains("attested trigger: p", environmentText, StringComparison.Ordinal);
        Assert.Contains("3 word types across 3 stems", environmentText, StringComparison.Ordinal);
        Assert.Contains("unobserved does not mean invalid", environmentText, StringComparison.Ordinal);
        Assert.DoesNotContain("delete", environmentText, StringComparison.OrdinalIgnoreCase);
        var environmentView = ParsimonyViewsQuery.Execute(session, new ParsimonyNamedViewRequest(bundleId,
            "environment-excess", new ParsimonyViewFilters(ObjectGuid: BroadEnvironmentTargetAllomorphGuid)));
        var environmentRows = environmentView.Rows.Cast<ParsimonyEnvironmentExcessViewRow>().ToArray();
        Assert.Equal(new[] { "Morphology" }, environmentRows.Select(row => row.Bucket));
        Assert.All(environmentRows, environmentRow =>
        {
            Assert.Equal(environmentAnswer.Witnesses, environmentRow.Observed);
            Assert.Equal(environmentAnswer.ExpectedContextUniverse, environmentRow.Universe);
            Assert.Equal(environmentAnswer.ExpectedLicensedContexts, environmentRow.Licensed);
            Assert.Equal(environmentAnswer.ExpectedObservedContexts, environmentRow.Observed);
            Assert.Equal(environmentAnswer.ExpectedExcessMembers, environmentRow.LicensedExtras);
            Assert.Contains("p", environmentRow.Observed);
        });

        var gateFactsPath = Path.Combine(_root, "broad-environment-gate-facts.sqlite");
        var gateEvidencePath = Path.Combine(_root, "broad-environment-gate-evidence.sqlite");
        File.Copy(bundle.GrammarFactsPath, gateFactsPath);
        File.Copy(bundle.EvidencePath, gateEvidencePath);
        AddBroadEnvironmentGateBucket(gateFactsPath);
        using (var gateSession = OpenBroadEnvironmentSession(bundle, gateFactsPath, gateEvidencePath))
        {
            var gateView = ParsimonyViewsQuery.Execute(gateSession, new ParsimonyNamedViewRequest(bundleId,
                "environment-excess", new ParsimonyViewFilters(ObjectGuid: BroadEnvironmentTargetAllomorphGuid)));
            var gateRows = gateView.Rows.Cast<ParsimonyEnvironmentExcessViewRow>().ToArray();
            Assert.Equal(new[] { "Morphology", "Phonology" }, gateRows.Select(row => row.Bucket));
            Assert.All(gateRows, row =>
            {
                Assert.Equal(environmentAnswer.ExpectedContextUniverse, row.Universe);
                Assert.Equal(environmentAnswer.ExpectedLicensedContexts, row.Licensed);
                Assert.Equal(environmentAnswer.ExpectedObservedContexts, row.Observed);
                Assert.Equal(environmentAnswer.ExpectedExcessMembers, row.LicensedExtras);
            });
        }

        var orFactsPath = Path.Combine(_root, "broad-environment-or-facts.sqlite");
        var orEvidencePath = Path.Combine(_root, "broad-environment-or-evidence.sqlite");
        File.Copy(bundle.GrammarFactsPath, orFactsPath);
        File.Copy(bundle.EvidencePath, orEvidencePath);
        AddBroadEnvironmentOrAlternative(orFactsPath);
        using (var orSession = OpenBroadEnvironmentSession(bundle, orFactsPath, orEvidencePath))
        {
            var orView = ParsimonyViewsQuery.Execute(orSession, new ParsimonyNamedViewRequest(bundleId,
                "environment-excess", new ParsimonyViewFilters(ObjectGuid: BroadEnvironmentTargetAllomorphGuid)));
            var orRows = orView.Rows.Cast<ParsimonyEnvironmentExcessViewRow>().ToArray();
            Assert.Equal(2, orRows.Length);
            Assert.All(orRows, row =>
            {
                Assert.Equal(new[] { "p", "b", "t", "s" }, row.Universe);
                Assert.Equal(new[] { "p", "b", "t" }, row.Licensed);
                Assert.Equal(new[] { "p" }, row.Observed);
                Assert.Equal(new[] { "b", "t" }, row.LicensedExtras);
            });
        }

        var naturalClass = MeasureRunner.Execute("R-nc-excess", session, bundleId);
        Assert.Equal(ParsimonyMeasureStatus.Computed, naturalClass.Run.Status);
        var classAnswer = ParsimonyMorphFixtureBuilder.For(_pristine.Seed, "R-nc-excess").Answer;
        Assert.Equal(classAnswer.EligibleItems, naturalClass.Run.EligibleItems);
        Assert.Equal(classAnswer.FindingItems, naturalClass.Run.FindingItems);
        Assert.Equal(classAnswer.FindingTargets, naturalClass.Findings.Select(finding => finding.AttachesTo.Identity));
        var classFinding = Assert.Single(naturalClass.Findings,
            finding => finding.AttachesTo.Identity == BroadEnvironmentNaturalClassGuid);
        Assert.Equal(new ParsimonyMeasureNumber(1, 3, "phoneme-types"), classFinding.Number);
        var classText = string.Join(" ", classFinding.Limitations);
        Assert.Contains("extra class member: b", classText, StringComparison.Ordinal);
        Assert.Contains("attested trigger: p", classText, StringComparison.Ordinal);
        Assert.Contains("unknown feature membership: s", classText, StringComparison.Ordinal);
        Assert.Contains("unobserved does not mean invalid", classText, StringComparison.Ordinal);
        Assert.DoesNotContain("delete", classText, StringComparison.OrdinalIgnoreCase);
        var classView = ParsimonyViewsQuery.Execute(session, new ParsimonyNamedViewRequest(bundleId,
            "natural-class-excess", new ParsimonyViewFilters(ObjectGuid: BroadEnvironmentNaturalClassGuid)));
        var classRow = Assert.IsType<ParsimonyNaturalClassExcessViewRow>(Assert.Single(classView.Rows));
        Assert.Equal(classAnswer.ExpectedExcessMembers, classRow.CurrentClassExtras);
        Assert.Equal(classAnswer.Witnesses, classRow.Observed);
        Assert.Equal(["Place=Labial"], classRow.FeatureIntersection);
        Assert.Equal(["b", "p"], classRow.IntersectionExtension.Order(StringComparer.Ordinal));
        Assert.Equal(3, classRow.ExcessDenominator);
        Assert.Equal(["s"], classRow.UnknownFeatureMembers);

        var untrainedFactsPath = Path.Combine(_root, "natural-class-untrained-facts.sqlite");
        var untrainedEvidencePath = Path.Combine(_root, "natural-class-untrained-evidence.sqlite");
        File.Copy(bundle.GrammarFactsPath, untrainedFactsPath);
        File.Copy(bundle.EvidencePath, untrainedEvidencePath);
        AddNaturalClassLeakFacts(untrainedFactsPath);
        var untrainedInputs = new ParsimonyReportInputs(bundle.BundleId, _token, "baseline", null,
            bundle.ModelFingerprint,
            new ParsimonyArtifactDigest(bundle.GrammarFactsSchemaVersion, Sha256Of(untrainedFactsPath)),
            new ParsimonyArtifactDigest(bundle.EvidenceSchemaVersion, Sha256Of(untrainedEvidencePath)),
            null, null, [], ParsimonyEvidenceScopeKind.ProjectApproved);
        using (var untrainedSession = new ParsimonyQuerySession(untrainedFactsPath, untrainedEvidencePath,
                   untrainedInputs))
        {
            var untrainedAnswer = Assert.Single(ParsimonyMorphFixtureBuilder.For(_pristine.Seed, "R-nc-excess")
                .Controls, item => item.Name == "held-out-not-in-training");
            var untrainedRun = MeasureRunner.Execute("R-nc-excess", untrainedSession, bundleId);
            Assert.Equal(untrainedAnswer.EligibleItems, untrainedRun.Run.EligibleItems);
            Assert.Equal(untrainedAnswer.FindingItems, untrainedRun.Run.FindingItems);
            var untrainedFinding = Assert.Single(untrainedRun.Findings);
            Assert.Equal(untrainedAnswer.FindingTargets.Single(), untrainedFinding.AttachesTo.Identity);
            Assert.Equal(new ParsimonyMeasureNumber(3, 4, "phoneme-types"), untrainedFinding.Number);
            var untrainedView = ParsimonyViewsQuery.Execute(untrainedSession, new ParsimonyNamedViewRequest(bundleId,
                "natural-class-excess", new ParsimonyViewFilters(ObjectGuid: untrainedAnswer.FindingTargets.Single())));
            var untrainedRow = Assert.IsType<ParsimonyNaturalClassExcessViewRow>(Assert.Single(untrainedView.Rows));
            Assert.Equal(untrainedAnswer.ExpectedExcessMembers.Order(StringComparer.Ordinal),
                untrainedRow.CurrentClassExtras.Order(StringComparer.Ordinal));
            Assert.Equal(untrainedAnswer.Witnesses, untrainedRow.Observed);
            Assert.All(untrainedAnswer.HeldOutPositiveControls,
                member => Assert.Contains(member, untrainedRow.CurrentClassExtras));
            Assert.All(untrainedAnswer.ReviewedLeakControls,
                member => Assert.Contains(member, untrainedRow.CurrentClassExtras));
            Assert.Equal(["b", "m", "p"], untrainedRow.IntersectionExtension.Order(StringComparer.Ordinal));
            Assert.Equal(["s"], untrainedRow.UnknownFeatureMembers);
        }

        var heldOutFactsPath = Path.Combine(_root, "natural-class-heldout-facts.sqlite");
        var heldOutEvidencePath = Path.Combine(_root, "natural-class-heldout-evidence.sqlite");
        File.Copy(bundle.GrammarFactsPath, heldOutFactsPath);
        File.Copy(bundle.EvidencePath, heldOutEvidencePath);
        AddNaturalClassLeakFacts(heldOutFactsPath);
        AddNaturalClassHeldOutEvidence(heldOutEvidencePath);
        var heldOutInputs = new ParsimonyReportInputs(bundle.BundleId, _token, "baseline", null,
            bundle.ModelFingerprint,
            new ParsimonyArtifactDigest(bundle.GrammarFactsSchemaVersion, Sha256Of(heldOutFactsPath)),
            new ParsimonyArtifactDigest(bundle.EvidenceSchemaVersion, Sha256Of(heldOutEvidencePath)),
            null, null, [], ParsimonyEvidenceScopeKind.ProjectApproved);
        using (var heldOutSession = new ParsimonyQuerySession(heldOutFactsPath, heldOutEvidencePath, heldOutInputs))
        {
            var heldOutAnswer = Assert.Single(ParsimonyMorphFixtureBuilder.For(_pristine.Seed, "R-nc-excess")
                .Controls, item => item.Name == "held-out-positive-control");
            var heldOutRun = MeasureRunner.Execute("R-nc-excess", heldOutSession, bundleId);
            Assert.Equal(heldOutAnswer.EligibleItems, heldOutRun.Run.EligibleItems);
            Assert.Equal(heldOutAnswer.FindingItems, heldOutRun.Run.FindingItems);
            var heldOutFinding = Assert.Single(heldOutRun.Findings);
            Assert.Equal(heldOutAnswer.FindingTargets.Single(), heldOutFinding.AttachesTo.Identity);
            Assert.Equal(new ParsimonyMeasureNumber(1, 2, "phoneme-types"), heldOutFinding.Number);
            var heldOutView = ParsimonyViewsQuery.Execute(heldOutSession, new ParsimonyNamedViewRequest(bundleId,
                "natural-class-excess", new ParsimonyViewFilters(ObjectGuid: heldOutAnswer.FindingTargets.Single())));
            var heldOutRow = Assert.IsType<ParsimonyNaturalClassExcessViewRow>(Assert.Single(heldOutView.Rows));
            Assert.Equal(heldOutAnswer.ExpectedExcessMembers, heldOutRow.CurrentClassExtras);
            Assert.Equal(heldOutAnswer.Witnesses, heldOutRow.Observed);
            Assert.Contains("t", heldOutRow.CurrentClassExtras);
            Assert.Equal(["b", "m", "p"], heldOutRow.IntersectionExtension.Order(StringComparer.Ordinal));
            Assert.Equal(["s"], heldOutRow.UnknownFeatureMembers);
        }

        var emptyIntersectionFactsPath = Path.Combine(_root, "natural-class-empty-intersection-facts.sqlite");
        File.Copy(bundle.GrammarFactsPath, emptyIntersectionFactsPath);
        RemoveNaturalClassObservedFeatureAssignments(emptyIntersectionFactsPath);
        using (var emptyIntersectionSession = new ParsimonyQuerySession(emptyIntersectionFactsPath,
                   bundle.EvidencePath, InputsFor(emptyIntersectionFactsPath, bundle.EvidencePath)))
        {
            var emptyRun = MeasureRunner.Execute("R-nc-excess", emptyIntersectionSession, bundleId);
            var emptyFinding = Assert.Single(emptyRun.Findings);
            Assert.Equal(new ParsimonyMeasureNumber(1, 3, "phoneme-types"), emptyFinding.Number);
            Assert.Contains("feature intersection is empty", string.Join(" ", emptyFinding.Limitations),
                StringComparison.Ordinal);
            var emptyView = ParsimonyViewsQuery.Execute(emptyIntersectionSession,
                new ParsimonyNamedViewRequest(bundleId, "natural-class-excess",
                    new ParsimonyViewFilters(ObjectGuid: BroadEnvironmentNaturalClassGuid)));
            var emptyRow = Assert.IsType<ParsimonyNaturalClassExcessViewRow>(Assert.Single(emptyView.Rows));
            Assert.Empty(emptyRow.FeatureIntersection);
            Assert.Equal(emptyRow.Universe, emptyRow.IntersectionExtension);
            Assert.Empty(emptyRow.UnknownFeatureMembers);
        }

        var siteFactsPath = Path.Combine(_root, "natural-class-sites-facts.sqlite");
        var siteEvidencePath = Path.Combine(_root, "natural-class-sites-evidence.sqlite");
        File.Copy(bundle.GrammarFactsPath, siteFactsPath);
        File.Copy(bundle.EvidencePath, siteEvidencePath);
        AddNaturalClassSecondUsageSite(siteFactsPath, siteEvidencePath);
        using (var siteSession = new ParsimonyQuerySession(siteFactsPath, siteEvidencePath,
                   InputsFor(siteFactsPath, siteEvidencePath)))
        {
            var siteRun = MeasureRunner.Execute("R-nc-excess", siteSession, bundleId);
            Assert.Equal(2, siteRun.Run.EligibleItems);
            Assert.Equal(2, siteRun.Run.FindingItems);
            Assert.Equal(2, siteRun.Findings.Count);
            Assert.All(siteRun.Findings, finding =>
                Assert.Equal(new ParsimonyMeasureNumber(1, 4, "phoneme-types"), finding.Number));
            var siteView = ParsimonyViewsQuery.Execute(siteSession,
                new ParsimonyNamedViewRequest(bundleId, "natural-class-excess",
                    new ParsimonyViewFilters(ObjectGuid: BroadEnvironmentNaturalClassGuid)));
            var siteRows = siteView.Rows.Cast<ParsimonyNaturalClassExcessViewRow>()
                .OrderBy(row => row.AllomorphGuid, StringComparer.Ordinal).ToArray();
            Assert.Equal(2, siteRows.Length);
            Assert.Equal(new[] { "p" }, siteRows[0].Observed);
            Assert.Equal(new[] { "b" }, siteRows[0].CurrentClassExtras);
            Assert.Equal(new[] { "b" }, siteRows[1].Observed);
            Assert.Equal(new[] { "p" }, siteRows[1].CurrentClassExtras);
            Assert.NotEqual(siteRows[0].UsageSite, siteRows[1].UsageSite);
            var unionObserved = siteRows.SelectMany(row => row.Observed).ToHashSet(StringComparer.Ordinal);
            Assert.Equal(new[] { "b", "p" }, unionObserved.Order(StringComparer.Ordinal));
            Assert.All(siteRows.SelectMany(row => row.CurrentClassExtras), extra => Assert.Contains(extra, unionObserved));
        }

        session.Dispose();
        AddUnrelatedAmbiguousContext(bundle.GrammarFactsPath, bundle.EvidencePath);
        var unrelatedAmbiguousInputs = new ParsimonyReportInputs(bundle.BundleId, _token, "baseline", null,
            bundle.ModelFingerprint,
            new ParsimonyArtifactDigest(bundle.GrammarFactsSchemaVersion, Sha256Of(bundle.GrammarFactsPath)),
            new ParsimonyArtifactDigest(bundle.EvidenceSchemaVersion, Sha256Of(bundle.EvidencePath)),
            null, null, [], ParsimonyEvidenceScopeKind.ProjectApproved);
        using (var unrelatedAmbiguousSession = new ParsimonyQuerySession(bundle.GrammarFactsPath, bundle.EvidencePath,
                   unrelatedAmbiguousInputs))
        {
            Assert.Equal(ParsimonyMeasureStatus.Computed,
                MeasureRunner.Execute("R-env-broad", unrelatedAmbiguousSession, bundleId).Run.Status);
            Assert.Equal(ParsimonyMeasureStatus.Computed,
                MeasureRunner.Execute("R-nc-excess", unrelatedAmbiguousSession, bundleId).Run.Status);
        }

        AddAmbiguousBroadEnvironmentGrapheme(bundle.GrammarFactsPath);
        var ambiguousInputs = new ParsimonyReportInputs(bundle.BundleId, _token, "baseline", null,
            bundle.ModelFingerprint,
            new ParsimonyArtifactDigest(bundle.GrammarFactsSchemaVersion, Sha256Of(bundle.GrammarFactsPath)),
            new ParsimonyArtifactDigest(bundle.EvidenceSchemaVersion, Sha256Of(bundle.EvidencePath)),
            null, null, [], ParsimonyEvidenceScopeKind.ProjectApproved);
        using var ambiguousSession = new ParsimonyQuerySession(bundle.GrammarFactsPath, bundle.EvidencePath,
            ambiguousInputs);
        var ambiguousRun = MeasureRunner.Execute("R-env-broad", ambiguousSession, bundleId);
        Assert.Equal(ParsimonyMeasureStatus.Inconclusive, ambiguousRun.Run.Status);
        Assert.Contains("ambiguous alignment", ambiguousRun.Run.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain(ambiguousRun.Findings,
            finding => finding.AttachesTo.Identity == BroadEnvironmentTargetAllomorphGuid);

        ParsimonyReportInputs InputsFor(string factsPath, string evidencePath) => new(bundle.BundleId, _token,
            "baseline", null, bundle.ModelFingerprint,
            new ParsimonyArtifactDigest(bundle.GrammarFactsSchemaVersion, Sha256Of(factsPath)),
            new ParsimonyArtifactDigest(bundle.EvidenceSchemaVersion, Sha256Of(evidencePath)),
            null, null, [], ParsimonyEvidenceScopeKind.ProjectApproved);

        var unsupportedFactsPath = Path.Combine(_root, "broad-environment-unsupported-facts.sqlite");
        var unsupportedEvidencePath = Path.Combine(_root, "broad-environment-unsupported-evidence.sqlite");
        File.Copy(bundle.GrammarFactsPath, unsupportedFactsPath);
        File.Copy(bundle.EvidencePath, unsupportedEvidencePath);
        AddUnsupportedBroadEnvironmentWindow(unsupportedFactsPath);
        using (var unsupportedSession = OpenBroadEnvironmentSession(bundle, unsupportedFactsPath, unsupportedEvidencePath))
        {
            var unsupportedRun = MeasureRunner.Execute("R-env-broad", unsupportedSession, bundleId);
            Assert.Equal(ParsimonyMeasureStatus.Inconclusive, unsupportedRun.Run.Status);
            Assert.Contains("unsupported site", unsupportedRun.Run.Detail, StringComparison.Ordinal);
            Assert.DoesNotContain(unsupportedRun.Findings,
                finding => finding.AttachesTo.Identity == BroadEnvironmentTargetAllomorphGuid);
        }

        var boundaryFactsPath = Path.Combine(_root, "broad-environment-word-boundary-facts.sqlite");
        var boundaryEvidencePath = Path.Combine(_root, "broad-environment-word-boundary-evidence.sqlite");
        File.Copy(bundle.GrammarFactsPath, boundaryFactsPath);
        File.Copy(bundle.EvidencePath, boundaryEvidencePath);
        AddBroadEnvironmentWordBoundaryCase(boundaryFactsPath, boundaryEvidencePath, includeLiteralHashMarker: false);
        using (var boundarySession = OpenBroadEnvironmentSession(bundle, boundaryFactsPath, boundaryEvidencePath))
        {
            var boundaryRun = MeasureRunner.Execute("R-env-broad", boundarySession, bundleId);
            Assert.True(boundaryRun.Run.Status == ParsimonyMeasureStatus.Computed, boundaryRun.Run.Detail);
            var boundaryView = ParsimonyViewsQuery.Execute(boundarySession, new ParsimonyNamedViewRequest(bundleId,
                "environment-excess", new ParsimonyViewFilters(ObjectGuid: BroadEnvironmentTargetAllomorphGuid)));
            var boundaryRows = boundaryView.Rows.Cast<ParsimonyEnvironmentExcessViewRow>().ToArray();
            Assert.Single(boundaryRows);
            Assert.All(boundaryRows, row =>
            {
                Assert.Contains("word boundary", row.Universe);
                Assert.Equal(new[] { "word boundary" }, row.Licensed);
                Assert.Equal(new[] { "word boundary" }, row.Observed);
                Assert.DoesNotContain("#", row.Licensed);
            });
        }

        var ambiguousBoundaryFactsPath = Path.Combine(_root, "broad-environment-ambiguous-boundary-facts.sqlite");
        var ambiguousBoundaryEvidencePath = Path.Combine(_root, "broad-environment-ambiguous-boundary-evidence.sqlite");
        File.Copy(bundle.GrammarFactsPath, ambiguousBoundaryFactsPath);
        File.Copy(bundle.EvidencePath, ambiguousBoundaryEvidencePath);
        AddBroadEnvironmentWordBoundaryCase(ambiguousBoundaryFactsPath, ambiguousBoundaryEvidencePath,
            includeLiteralHashMarker: true);
        using var ambiguousBoundarySession = OpenBroadEnvironmentSession(bundle, ambiguousBoundaryFactsPath,
            ambiguousBoundaryEvidencePath);
        var ambiguousBoundaryRun = MeasureRunner.Execute("R-env-broad", ambiguousBoundarySession, bundleId);
        Assert.Equal(ParsimonyMeasureStatus.Inconclusive, ambiguousBoundaryRun.Run.Status);
        Assert.Contains("unsupported site", ambiguousBoundaryRun.Run.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain(ambiguousBoundaryRun.Findings,
            finding => finding.AttachesTo.Identity == BroadEnvironmentTargetAllomorphGuid);
    }

    [Fact]
    public async Task TemplateMeasuresIgnoreApprovedWordsOutsideTheDefaultSelection()
    {
        await PublishBaselineAsync(_pristine);
        var (bundleId, bundle) = await RunScopedMeasureJobAsync("R-tmpl-precedence", NarrowDefaultSelection(),
            "template-scope-worker");
        AddNamedMeasureFacts(bundle.GrammarFactsPath);
        AddTemplateMeasureFacts(bundle.GrammarFactsPath, bundle.EvidencePath);
        AddPositiveAndNegativeAlternativeTemplates(bundle.GrammarFactsPath, bundle.EvidencePath);

        using (var narrow = OpenScopedSession(bundle, ParsimonyEvidenceScopeKind.DefaultSelection))
        {
            var precedence = MeasureRunner.Execute("R-tmpl-precedence", narrow, bundleId);
            Assert.Equal(ParsimonyMeasureStatus.Computed, precedence.Run.Status);
            Assert.Empty(precedence.Findings);
            var blocking = MeasureRunner.Execute("R-slot-blocking", narrow, bundleId);
            Assert.Equal(ParsimonyMeasureStatus.Computed, blocking.Run.Status);
            Assert.DoesNotContain(blocking.Findings, finding => finding.AttachesTo.Identity == RequiredSlotGuid);
        }

        using var broad = OpenScopedSession(bundle, ParsimonyEvidenceScopeKind.ProjectApproved);
        var contradiction = Assert.Single(MeasureRunner.Execute("R-tmpl-precedence", broad, bundleId).Findings);
        Assert.Equal(AncestorTemplateGuid, contradiction.AttachesTo.Identity);
    }

    [Fact]
    public async Task EnvironmentMeasuresIgnoreApprovedWordsOutsideTheDefaultSelection()
    {
        await PublishBaselineAsync(_pristine);
        var (bundleId, bundle) = await RunScopedMeasureJobAsync("R-env-broad", NarrowDefaultSelection(),
            "environment-scope-worker");
        AddEnvironmentAndNaturalClassFacts(bundle.GrammarFactsPath);
        AddEnvironmentAndNaturalClassEvidence(bundle.EvidencePath);

        using (var narrow = OpenScopedSession(bundle, ParsimonyEvidenceScopeKind.DefaultSelection))
        {
            var environment = MeasureRunner.Execute("R-env-broad", narrow, bundleId);
            Assert.Equal(ParsimonyMeasureStatus.Computed, environment.Run.Status);
            Assert.DoesNotContain(environment.Findings, finding => finding.AttachesTo.Identity == BroadEnvironmentTargetAllomorphGuid);
            var classes = MeasureRunner.Execute("R-nc-excess", narrow, bundleId);
            Assert.Equal(ParsimonyMeasureStatus.Computed, classes.Run.Status);
            Assert.DoesNotContain(classes.Findings, finding => finding.AttachesTo.Identity == BroadEnvironmentNaturalClassGuid);
        }

        using var broad = OpenScopedSession(bundle, ParsimonyEvidenceScopeKind.ProjectApproved);
        Assert.Contains(MeasureRunner.Execute("R-env-broad", broad, bundleId).Findings,
            finding => finding.AttachesTo.Identity == BroadEnvironmentTargetAllomorphGuid);
        Assert.Contains(MeasureRunner.Execute("R-nc-excess", broad, bundleId).Findings,
            finding => finding.AttachesTo.Identity == BroadEnvironmentNaturalClassGuid);
    }

    [Fact]
    public async Task TextMeasuresAreNotComputedWhenTheRequestedScopeIsUnavailable()
    {
        await PublishBaselineAsync(_pristine);
        var (bundleId, bundle) = await RunScopedMeasureJobAsync("R-tmpl-precedence",
            new ParsimonyScopeBinding(ParsimonyEvidenceScopeKind.ProjectApproved, null), "unavailable-scope-worker");
        AddNamedMeasureFacts(bundle.GrammarFactsPath);
        AddTemplateMeasureFacts(bundle.GrammarFactsPath, bundle.EvidencePath);
        AddPositiveAndNegativeAlternativeTemplates(bundle.GrammarFactsPath, bundle.EvidencePath);
        AddEnvironmentAndNaturalClassFacts(bundle.GrammarFactsPath);
        AddEnvironmentAndNaturalClassEvidence(bundle.EvidencePath);

        using var session = OpenScopedSession(bundle, ParsimonyEvidenceScopeKind.DefaultSelection);
        foreach (var measureId in new[]
                 {
                     "R-tmpl-precedence", "R-slot-blocking", "R-env-broad", "R-nc-excess", "B-affix-null-vs-optional",
                 })
        {
            var run = MeasureRunner.Execute(measureId, session, bundleId);
            Assert.Equal(ParsimonyMeasureStatus.Inconclusive, run.Run.Status);
            Assert.Contains("the Default Selection is not saved.", run.Run.Detail, StringComparison.Ordinal);
            Assert.Empty(run.Findings);
        }
    }

    [Fact]
    public async Task AMeasureThatCouldNotLookAddsANotCheckedNote()
    {
        await PublishBaselineAsync(_pristine);
        var response = await RunStoredMeasureReportAsync("R-slot-blocking",
            new ParsimonyScopeBinding(ParsimonyEvidenceScopeKind.DefaultSelection, null), "not-checked-worker");

        var run = Assert.Single(response.MeasureRuns);
        Assert.Equal(ParsimonyMeasureStatus.Inconclusive, run.Status);
        Assert.Equal("the Default Selection is not saved.", run.Detail);
        var note = Assert.Single(response.Notes);
        Assert.Equal(new ParsimonyNote("not-checked", "R-slot-blocking",
            "Not checked: A required slot blocks a form — the Default Selection is not saved.", null), note);
        Assert.Contains(note.Text, response.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("R-slot-blocking: Inconclusive", response.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ADefaultSelectionWithoutApprovedWordsIsNotChecked()
    {
        await PublishBaselineAsync(_pristine);
        var selection = SIL.Motif.Host.Corpus.Selection.Create("Default", [SeededProject.UnanalysedWordForm]);
        var response = await RunStoredMeasureReportAsync("R-slot-blocking",
            new ParsimonyScopeBinding(ParsimonyEvidenceScopeKind.DefaultSelection,
                new ParsimonySelectionSnapshot("Default", [_seededText.TextId], [], selection.Words,
                    selection.Sha256)), "no-approved-words-worker");

        var run = Assert.Single(response.MeasureRuns);
        Assert.Equal(ParsimonyMeasureStatus.Inconclusive, run.Status);
        Assert.Equal("the Default Selection has no Approved words.", run.Detail);
        var note = Assert.Single(response.Notes);
        Assert.Equal("Not checked: A required slot blocks a form — the Default Selection has no Approved words.",
            note.Text);
    }

    [Fact]
    public async Task AMeasureThatFoundNothingReportsNothing()
    {
        await PublishBaselineAsync(_pristine);
        var (bundleId, bundle) = await RunScopedMeasureJobAsync("B-affix-null-vs-optional", NarrowDefaultSelection(),
            "found-nothing-worker");
        AddNamedMeasureFacts(bundle.GrammarFactsPath);
        AddTemplateMeasureFacts(bundle.GrammarFactsPath, bundle.EvidencePath);
        AddPositiveAndNegativeAlternativeTemplates(bundle.GrammarFactsPath, bundle.EvidencePath);
        AddAffixPositionAndZeroAffixFacts(bundle.GrammarFactsPath, bundle.EvidencePath);
        const string nullMsa = "30000000-0000-0000-0000-000000000302";
        AddApprovedAnalysisOutsideSelection(bundle.EvidencePath, nullMsa, "20000000-0000-0000-0000-000000000270",
            "20000000-0000-0000-0000-000000000271");
        var inputs = new ParsimonyReportInputs(bundleId, _token, "baseline", null, bundle.ModelFingerprint,
            new ParsimonyArtifactDigest(bundle.GrammarFactsSchemaVersion, Sha256Of(bundle.GrammarFactsPath)),
            new ParsimonyArtifactDigest(bundle.EvidenceSchemaVersion, Sha256Of(bundle.EvidencePath)), null, null,
            [], ParsimonyEvidenceScopeKind.DefaultSelection);

        var rendered = new ParsimonyReportProducer().Produce(
            ReportInput.FromParsimonyFiles(inputs, bundle.GrammarFactsPath, bundle.EvidencePath, [],
                "B-affix-null-vs-optional"), new ReportQuery(), AssessorCatalog.Empty);

        var run = Assert.Single(rendered.ParsimonyMeasureRuns!);
        Assert.Equal(ParsimonyMeasureStatus.Computed, run.Status);
        Assert.Empty(rendered.ParsimonyFindings!);
        Assert.Empty(rendered.ParsimonyNotes!);
        Assert.DoesNotContain("B-affix-null-vs-optional: Computed; 0/", rendered.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("B-affix-null-vs-optional", rendered.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnIgnoredAllomorphGateAddsANoEffectNote()
    {
        await PublishBaselineAsync(_pristine);
        var (bundleId, bundle) = await RunScopedMeasureJobAsync("P-allo-alternation-family",
            new ParsimonyScopeBinding(ParsimonyEvidenceScopeKind.ProjectApproved, null), "no-effect-worker");
        AddNamedMeasureFacts(bundle.GrammarFactsPath);
        AddAlternationFamilyGrammarFacts(bundle.GrammarFactsPath);
        UpdateGrammarFacts(bundle.GrammarFactsPath,
            "INSERT INTO allomorph_gate(allomorph_guid, gate_kind, ordinal, parser_effect, reason_code) " +
            "VALUES ($allomorph, 'required_category', 0, 'ignored', 'msEnvPartOfSpeechNotRead');",
            ("$allomorph", AlternationAllomorphC2));

        var rendered = ProduceAlternationReport(bundleId, bundle);

        var note = Assert.Single(rendered.ParsimonyNotes!);
        Assert.Equal("no-effect", note.Kind);
        Assert.Equal(AlternationAllomorphC2, note.SubjectGuid);
        Assert.Matches(@"^.+ \(.+\): Required category has no effect on parsing — legacy field\.$", note.Text);
        Assert.Contains(note.Text, rendered.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(rendered.ParsimonyFindings!, finding =>
            finding.AttachesTo.Identity.Contains(AlternationAllomorphC2, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ADerivationalAllomorphGateAddsItsOwnNoEffectSentence()
    {
        await PublishBaselineAsync(_pristine);
        var (bundleId, bundle) = await RunScopedMeasureJobAsync("P-allo-alternation-family",
            new ParsimonyScopeBinding(ParsimonyEvidenceScopeKind.ProjectApproved, null), "no-effect-derivational-worker");
        AddNamedMeasureFacts(bundle.GrammarFactsPath);
        AddAlternationFamilyGrammarFacts(bundle.GrammarFactsPath);
        UpdateGrammarFacts(bundle.GrammarFactsPath,
            "INSERT INTO allomorph_gate(allomorph_guid, gate_kind, ordinal, target_guid, parser_effect, reason_code) " +
            "VALUES ($allomorph, 'inflection_class', 0, $class, 'ignored', 'derivationalMsaIgnoresAlloClasses');",
            ("$allomorph", AlternationAllomorphC2), ("$class", AlternationClass));

        var rendered = ProduceAlternationReport(bundleId, bundle);

        var note = Assert.Single(rendered.ParsimonyNotes!);
        Assert.EndsWith("): Inflection classes have no effect on parsing for a derivational affix.", note.Text,
            StringComparison.Ordinal);
        Assert.Equal(AlternationAllomorphC2, note.SubjectGuid);
    }

    [Fact]
    public async Task ANotAttemptedAllomorphGateAddsNoNote()
    {
        await PublishBaselineAsync(_pristine);
        var (bundleId, bundle) = await RunScopedMeasureJobAsync("P-allo-alternation-family",
            new ParsimonyScopeBinding(ParsimonyEvidenceScopeKind.ProjectApproved, null), "no-effect-none-worker");
        AddNamedMeasureFacts(bundle.GrammarFactsPath);
        AddAlternationFamilyGrammarFacts(bundle.GrammarFactsPath);
        UpdateGrammarFacts(bundle.GrammarFactsPath,
            "INSERT INTO allomorph_gate(allomorph_guid, gate_kind, ordinal, parser_effect) " +
            "VALUES ($allomorph, 'stem_name', 0, 'not_attempted');",
            ("$allomorph", AlternationAllomorphC2));

        var rendered = ProduceAlternationReport(bundleId, bundle);

        Assert.Empty(rendered.ParsimonyNotes!);
    }

    [Fact]
    public async Task AnAppliedAllomorphGateAddsNoNote()
    {
        await PublishBaselineAsync(_pristine);
        var (bundleId, bundle) = await RunScopedMeasureJobAsync("P-allo-alternation-family",
            new ParsimonyScopeBinding(ParsimonyEvidenceScopeKind.ProjectApproved, null), "no-effect-applied-worker");
        AddNamedMeasureFacts(bundle.GrammarFactsPath);
        AddAlternationFamilyGrammarFacts(bundle.GrammarFactsPath);
        UpdateGrammarFacts(bundle.GrammarFactsPath,
            "INSERT INTO allomorph_gate(allomorph_guid, gate_kind, ordinal, target_guid, parser_effect) " +
            "VALUES ($allomorph, 'inflection_class', 0, $class, 'applied');",
            ("$allomorph", AlternationAllomorphC2), ("$class", AlternationClass));

        var rendered = ProduceAlternationReport(bundleId, bundle);

        Assert.Empty(rendered.ParsimonyNotes!);
        Assert.DoesNotContain("has no effect on parsing", rendered.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EveryNoteForAnUnavailableCheckReadsAsOneClause()
    {
        await PublishBaselineAsync(_pristine);
        var (bundleId, bundle) = await RunScopedMeasureJobAsync("R-tmpl-precedence",
            new ParsimonyScopeBinding(ParsimonyEvidenceScopeKind.ProjectApproved, null), "note-clause-worker");

        // The pristine facts publish too few sections for most queries, so their required facts are missing.
        using (var missingFacts = OpenScopedSession(bundle, ParsimonyEvidenceScopeKind.DefaultSelection))
        {
            var notes = MeasureNotes(missingFacts, bundleId);
            Assert.Contains(notes, note => note.Text.Contains("the grammar facts do not include ",
                StringComparison.Ordinal));
            AssertNoteShapes(notes);
        }

        // With the facts present, the Default Selection that was not captured is the reason a check cannot look.
        AddNamedMeasureFacts(bundle.GrammarFactsPath);
        AddTemplateMeasureFacts(bundle.GrammarFactsPath, bundle.EvidencePath);
        AddPositiveAndNegativeAlternativeTemplates(bundle.GrammarFactsPath, bundle.EvidencePath);
        AddEnvironmentAndNaturalClassFacts(bundle.GrammarFactsPath);
        AddEnvironmentAndNaturalClassEvidence(bundle.EvidencePath);
        using (var unavailableScope = OpenScopedSession(bundle, ParsimonyEvidenceScopeKind.DefaultSelection))
        {
            var notes = MeasureNotes(unavailableScope, bundleId);
            Assert.Contains(notes, note => note.Text.EndsWith("the Default Selection is not saved.",
                StringComparison.Ordinal));
            AssertNoteShapes(notes);
        }

    }

    [Fact]
    public async Task EveryNonComputedMeasureHasExactlyOneNote()
    {
        await PublishBaselineAsync(_pristine);
        var (bundleId, bundle) = await RunScopedMeasureJobAsync("R-tmpl-precedence",
            new ParsimonyScopeBinding(ParsimonyEvidenceScopeKind.ProjectApproved, null), "every-note-worker");
        // Pristine facts publish too few sections, no Assessment is supplied, and the Default Selection is not saved.
        using var session = OpenScopedSession(bundle, ParsimonyEvidenceScopeKind.DefaultSelection);

        var statuses = new HashSet<ParsimonyMeasureStatus>();
        var nonComputed = 0;
        foreach (var measure in MeasureCatalog.All)
        {
            var result = MeasureRunner.Execute(measure.Id, session, bundleId);
            var note = ParsimonyNotes.NotChecked(result);
            statuses.Add(result.Run.Status);
            var expected = result.Run.Status == ParsimonyMeasureStatus.Computed ? 0 : 1;
            Assert.Equal(expected, note is null ? 0 : 1);
            if (note is null) continue;
            nonComputed++;
            Assert.Equal(measure.Id, note.MeasureId);
            Assert.Matches("^Not checked: .+ — [a-z].*\\.$", note.Text);
            Assert.DoesNotContain("_", note.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("facts.", note.Text, StringComparison.Ordinal);
        }
        Assert.True(nonComputed > 0);
        Assert.Contains(ParsimonyMeasureStatus.NotAvailable, statuses);
    }

    [Fact]
    public async Task AComputedCheckWithUnpublishedSelectedContextsSaysSoWithoutFindings()
    {
        await RunUnconditionedMeasureAsync(path => AddFinalElsewhereFamilyFacts(path, gatedTop: false), result =>
        {
            Assert.Equal(ParsimonyMeasureStatus.Computed, result.Run.Status);
            var note = ParsimonyNotes.NotChecked(result);
            Assert.NotNull(note);
            Assert.Contains("the effective selected contexts of each allomorph are not published",
                note.Text, StringComparison.Ordinal);
            AssertNoteShapes([note]);
        });
    }

    private static List<ParsimonyNote> MeasureNotes(ParsimonyQuerySession session, string bundleId)
    {
        var notes = new List<ParsimonyNote>();
        foreach (var measure in MeasureCatalog.All.Where(item => item.QueryId is not null))
        {
            var note = ParsimonyNotes.NotChecked(MeasureRunner.Execute(measure.Id, session, bundleId));
            if (note is not null) notes.Add(note);
        }
        return notes;
    }

    private static void AssertNoteShapes(IReadOnlyList<ParsimonyNote> notes)
    {
        Assert.NotEmpty(notes);
        Assert.All(notes, note =>
        {
            Assert.Matches("^Not checked: .+ — [a-z].*\\.$", note.Text);
            Assert.DoesNotContain("_", note.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("facts.", note.Text, StringComparison.Ordinal);
        });
        Assert.Equal(notes.Count, notes.Select(note => note.MeasureId).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task NullOptionalAffixMeasureIgnoresApprovedWordsOutsideTheDefaultSelection()
    {
        await PublishBaselineAsync(_pristine);
        var (bundleId, bundle) = await RunScopedMeasureJobAsync("B-affix-null-vs-optional", NarrowDefaultSelection(),
            "null-optional-scope-worker");
        AddNamedMeasureFacts(bundle.GrammarFactsPath);
        AddTemplateMeasureFacts(bundle.GrammarFactsPath, bundle.EvidencePath);
        AddPositiveAndNegativeAlternativeTemplates(bundle.GrammarFactsPath, bundle.EvidencePath);
        AddAffixPositionAndZeroAffixFacts(bundle.GrammarFactsPath, bundle.EvidencePath);
        const string nullMsa = "30000000-0000-0000-0000-000000000302";
        const string outsideAnalysis = "20000000-0000-0000-0000-000000000270";
        AddApprovedAnalysisOutsideSelection(bundle.EvidencePath, nullMsa, outsideAnalysis,
            "20000000-0000-0000-0000-000000000271");

        using (var broad = OpenScopedSession(bundle, ParsimonyEvidenceScopeKind.ProjectApproved))
        {
            var row = Assert.Single(broad.ReadNullOptionalAffixes(nullMsa));
            Assert.Contains(outsideAnalysis, row.ApprovedAnalysisGuids);
        }

        using var narrow = OpenScopedSession(bundle, ParsimonyEvidenceScopeKind.DefaultSelection);
        var narrowRow = Assert.Single(narrow.ReadNullOptionalAffixes(nullMsa));
        Assert.DoesNotContain(outsideAnalysis, narrowRow.ApprovedAnalysisGuids);
        Assert.DoesNotContain(narrowRow.Exclusions, text => text.Contains("Approved morphology uses", StringComparison.Ordinal));
        Assert.Equal(ParsimonyMeasureStatus.Computed,
            MeasureRunner.Execute("B-affix-null-vs-optional", narrow, bundleId).Run.Status);
    }

    private static void AddApprovedAnalysisOutsideSelection(string evidencePath, string msaGuid, string analysisGuid,
        string wordformGuid)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(evidencePath), Pooling = false, ForeignKeys = true,
        }.ToString());
        connection.Open();
        using var transaction = connection.BeginTransaction();
        void Execute(string sql, params (string Name, object Value)[] values)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            foreach (var (name, value) in values) command.Parameters.AddWithValue(name, value);
            command.ExecuteNonQuery();
        }
        Execute("INSERT INTO wordforms(wordform_guid, spelling_status) VALUES ($guid, 0);", ("$guid", wordformGuid));
        Execute("INSERT INTO wordform_forms(wordform_guid, writing_system, form, form_nfd) " +
            "VALUES ($guid, 'qaa', 'outside', 'outside');", ("$guid", wordformGuid));
        Execute("INSERT INTO analyses(analysis_guid, wordform_guid, opinion, source_kind, content_sha256) " +
            "VALUES ($analysis, $wordform, 'approved', 'manual', $digest);",
            ("$analysis", analysisGuid), ("$wordform", wordformGuid), ("$digest", new string('b', 64)));
        Execute("INSERT INTO analysis_morphs(analysis_guid, ordinal, msa_guid) VALUES ($analysis, 0, $msa);",
            ("$analysis", analysisGuid), ("$msa", msaGuid));
        Execute("UPDATE artifact_metadata SET wordform_count=(SELECT COUNT(*) FROM wordforms), " +
            "analysis_count=(SELECT COUNT(*) FROM analyses) WHERE singleton=1;");
        transaction.Commit();
    }

    private ParsimonyScopeBinding NarrowDefaultSelection()
    {
        var selection = SIL.Motif.Host.Corpus.Selection.Create("Default",
            [SeededProject.AnalysedWordForm, SeededProject.UnanalysedWordForm]);
        return new ParsimonyScopeBinding(ParsimonyEvidenceScopeKind.DefaultSelection,
            new ParsimonySelectionSnapshot("Default", [_seededText.TextId], [], selection.Words, selection.Sha256));
    }

    private async Task<(string BundleId, ParsimonyBundleRecord Bundle)> RunScopedMeasureJobAsync(string measureId,
        ParsimonyScopeBinding binding, string workerName)
    {
        var parserPath = FactsParser(_pristine.Seed);
        using var invoker = new SIL.Motif.Host.PanGloss.PanGlossInvoker(parserPath);
        var job = CreateJob(measureId, binding);
        var handler = new ParsimonyJobHandler(_database, _baselines, _projectKey, _project,
            new RunnerOptions { Root = Path.Combine(_root, workerName), ParserPath = parserPath }, invoker);
        var outcome = await handler.RunAsync(Claim(job.JobId), CancellationToken.None);
        Assert.True(outcome!.Status == JobStatus.Completed, outcome.ResultJson);
        using var result = JsonDocument.Parse(outcome.ResultJson!);
        var bundleId = result.RootElement.GetProperty("bundleId").GetString()!;
        return (bundleId, new ParsimonyBundleRepository(_database).Get(bundleId)!);
    }

    private ParsimonyQuerySession OpenScopedSession(ParsimonyBundleRecord bundle, ParsimonyEvidenceScopeKind scope)
    {
        var inputs = new ParsimonyReportInputs(bundle.BundleId, _token, "baseline", null,
            bundle.ModelFingerprint,
            new ParsimonyArtifactDigest(bundle.GrammarFactsSchemaVersion, Sha256Of(bundle.GrammarFactsPath)),
            new ParsimonyArtifactDigest(bundle.EvidenceSchemaVersion, Sha256Of(bundle.EvidencePath)), null, null,
            [], scope);
        return new ParsimonyQuerySession(bundle.GrammarFactsPath, bundle.EvidencePath, inputs);
    }

    private RenderedReport ProduceAlternationReport(string bundleId, ParsimonyBundleRecord bundle)
    {
        var inputs = new ParsimonyReportInputs(bundleId, _token, "baseline", null, bundle.ModelFingerprint,
            new ParsimonyArtifactDigest(bundle.GrammarFactsSchemaVersion, Sha256Of(bundle.GrammarFactsPath)),
            new ParsimonyArtifactDigest(bundle.EvidenceSchemaVersion, Sha256Of(bundle.EvidencePath)), null, null, [],
            ParsimonyEvidenceScopeKind.ProjectApproved);
        return new ParsimonyReportProducer().Produce(
            ReportInput.FromParsimonyFiles(inputs, bundle.GrammarFactsPath, bundle.EvidencePath, [],
                "P-allo-alternation-family"), new ReportQuery(), AssessorCatalog.Empty);
    }

    private async Task<ParsimonyReportResponse> RunStoredMeasureReportAsync(string measureId,
        ParsimonyScopeBinding binding, string workerName)
    {
        var parserPath = FactsParser(_pristine.Seed);
        using var invoker = new SIL.Motif.Host.PanGloss.PanGlossInvoker(parserPath);
        var job = CreateJob(measureId, binding);
        var handler = new ParsimonyJobHandler(_database, _baselines, _projectKey, _project,
            new RunnerOptions { Root = Path.Combine(_root, workerName), ParserPath = parserPath }, invoker);
        var outcome = await handler.RunAsync(Claim(job.JobId), CancellationToken.None);
        Assert.True(outcome!.Status == JobStatus.Completed, outcome.ResultJson);
        using var result = JsonDocument.Parse(outcome.ResultJson!);
        var reportId = result.RootElement.GetProperty("reportId").GetString()!;
        var report = Assert.IsType<ReportRecord>(new ReportRepository(_database).Get(reportId));
        return JsonSerializer.Deserialize<ParsimonyReportResponse>(report.ReportJson,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }

    [Fact]
    public async Task AlternationFamilyGroupsOneSingleEditFromIndependentMorphemesAndSuppressesAnExistingRule()
    {
        var publishedRoot = await PublishBaselineAsync(_pristine);
        var parserPath = FactsParser(_pristine.Seed);
        using var invoker = new SIL.Motif.Host.PanGloss.PanGlossInvoker(parserPath);
        var job = CreateJob("P-allo-alternation-family",
            new ParsimonyScopeBinding(ParsimonyEvidenceScopeKind.ProjectApproved, null));
        var handler = new ParsimonyJobHandler(_database, _baselines, _projectKey, _project,
            new RunnerOptions { Root = Path.Combine(_root, "alternation-worker"), ParserPath = parserPath }, invoker);
        var outcome = await handler.RunAsync(Claim(job.JobId), CancellationToken.None);
        Assert.True(outcome!.Status == JobStatus.Completed, outcome.ResultJson);

        using var result = JsonDocument.Parse(outcome.ResultJson!);
        var bundleId = result.RootElement.GetProperty("bundleId").GetString()!;
        var bundle = new ParsimonyBundleRepository(_database).Get(bundleId)!;
        AddNamedMeasureFacts(bundle.GrammarFactsPath);
        AddAlternationFamilyGrammarFacts(bundle.GrammarFactsPath);
        var inputs = new ParsimonyReportInputs(bundle.BundleId, _token, "baseline", null,
            bundle.ModelFingerprint,
            new ParsimonyArtifactDigest(bundle.GrammarFactsSchemaVersion, Sha256Of(bundle.GrammarFactsPath)),
            new ParsimonyArtifactDigest(bundle.EvidenceSchemaVersion, bundle.EvidenceSha256),
            null, null, [], ParsimonyEvidenceScopeKind.ProjectApproved);
        using var session = new ParsimonyQuerySession(bundle.GrammarFactsPath, bundle.EvidencePath, inputs);

        var discovery = MeasureRunner.Execute("P-allo-alternation-family", session, bundleId);

        Assert.True(MeasureRunner.Supports("P-allo-alternation-family"));
        Assert.True(discovery.Run.Status == ParsimonyMeasureStatus.Computed,
            discovery.Run.Detail ?? "The alternation facts were inconclusive without a reported limitation.");
        Assert.Equal(2, discovery.Run.EligibleItems);
        Assert.Equal(2, discovery.Run.FindingItems);
        var finding = Assert.Single(discovery.Findings);
        Assert.Equal(2, finding.Number.Numerator);
        Assert.Equal(2, finding.Number.Denominator);
        var view = ParsimonyViewsQuery.Execute(session,
            new ParsimonyNamedViewRequest(bundleId, "alternation-families", new ParsimonyViewFilters(), 10));
        var family = Assert.IsType<ParsimonyAlternationFamilyViewRow>(Assert.Single(view.Rows));
        Assert.Equal(ParsimonyMeasureStatus.Computed, view.Status);
        Assert.Contains("~", family.Description, StringComparison.Ordinal);
        Assert.Contains("2 prefixes", family.Description, StringComparison.Ordinal);
        Assert.Equal(2, family.Members.Count);
        Assert.Contains(family.Members, member => member.AllomorphGuids.Count == 3);

        var ruledFactsPath = Path.Combine(_root, "alternation-with-rule.sqlite");
        File.Copy(bundle.GrammarFactsPath, ruledFactsPath);
        AddExactAlternationRule(ruledFactsPath);
        var ruledInputs = new ParsimonyReportInputs(bundle.BundleId, _token, "baseline", null,
            bundle.ModelFingerprint,
            new ParsimonyArtifactDigest(bundle.GrammarFactsSchemaVersion, Sha256Of(ruledFactsPath)),
            new ParsimonyArtifactDigest(bundle.EvidenceSchemaVersion, bundle.EvidenceSha256),
            null, null, [], ParsimonyEvidenceScopeKind.ProjectApproved);
        using var ruledSession = new ParsimonyQuerySession(ruledFactsPath, bundle.EvidencePath, ruledInputs);
        var ruled = MeasureRunner.Execute("P-allo-alternation-family", ruledSession, bundleId);
        Assert.Equal(ParsimonyMeasureStatus.Computed, ruled.Run.Status);
        Assert.Empty(ruled.Findings);

        foreach (var boundaryGuid in new[]
                 {
                     LangProjectTags.kguidPhRuleWordBdry.ToString("D"),
                     "90000000-0000-0000-0000-000000000003",
                 })
        {
            var contextualFactsPath = Path.Combine(_root, "alternation-with-context-" + boundaryGuid[^1] + ".sqlite");
            File.Copy(bundle.GrammarFactsPath, contextualFactsPath);
            AddExactAlternationRule(contextualFactsPath);
            AddBoundaryContextToExactAlternationRule(contextualFactsPath, boundaryGuid);
            var contextualInputs = new ParsimonyReportInputs(bundle.BundleId, _token, "baseline", null,
                bundle.ModelFingerprint,
                new ParsimonyArtifactDigest(bundle.GrammarFactsSchemaVersion, Sha256Of(contextualFactsPath)),
                new ParsimonyArtifactDigest(bundle.EvidenceSchemaVersion, bundle.EvidenceSha256),
                null, null, [], ParsimonyEvidenceScopeKind.ProjectApproved);
            using var contextualSession = new ParsimonyQuerySession(contextualFactsPath, bundle.EvidencePath,
                contextualInputs);
            var contextual = MeasureRunner.Execute("P-allo-alternation-family", contextualSession, bundleId);
            Assert.Equal(ParsimonyMeasureStatus.Inconclusive, contextual.Run.Status);
            Assert.Contains("conditioning that differs", contextual.Run.Detail ?? string.Empty,
                StringComparison.Ordinal);
            Assert.NotEmpty(contextual.Findings);
        }

        foreach (var ruleBoundaryGuid in new[]
                 {
                     LangProjectTags.kguidPhRuleWordBdry.ToString("D"),
                     "90000000-0000-0000-0000-000000000003",
                 })
        {
            var exactContextFactsPath = Path.Combine(_root, "alternation-exact-context-" + ruleBoundaryGuid[^1] + ".sqlite");
            File.Copy(bundle.GrammarFactsPath, exactContextFactsPath);
            AddAlternationFamilyWordBoundaryConditionForAlternates(exactContextFactsPath,
                LangProjectTags.kguidPhRuleWordBdry.ToString("D"));
            AddExactAlternationRule(exactContextFactsPath);
            AddBoundaryContextToExactAlternationRule(exactContextFactsPath, ruleBoundaryGuid);
            var exactContextInputs = new ParsimonyReportInputs(bundle.BundleId, _token, "baseline", null,
                bundle.ModelFingerprint,
                new ParsimonyArtifactDigest(bundle.GrammarFactsSchemaVersion, Sha256Of(exactContextFactsPath)),
                new ParsimonyArtifactDigest(bundle.EvidenceSchemaVersion, bundle.EvidenceSha256),
                null, null, [], ParsimonyEvidenceScopeKind.ProjectApproved);
            using var exactContextSession = new ParsimonyQuerySession(exactContextFactsPath, bundle.EvidencePath,
                exactContextInputs);

            var exactContext = MeasureRunner.Execute("P-allo-alternation-family", exactContextSession, bundleId);

            if (ruleBoundaryGuid == LangProjectTags.kguidPhRuleWordBdry.ToString("D"))
            {
                Assert.Equal(ParsimonyMeasureStatus.Computed, exactContext.Run.Status);
                Assert.Empty(exactContext.Findings);
            }
            else
            {
                Assert.Equal(ParsimonyMeasureStatus.Inconclusive, exactContext.Run.Status);
                Assert.NotEmpty(exactContext.Findings);
                Assert.Contains("conditioning that differs", exactContext.Run.Detail ?? string.Empty,
                    StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public async Task ARewriteRuleWithSeveralBranchesDoesNotSuppressTheFamily()
    {
        var publishedRoot = await PublishBaselineAsync(_pristine);
        var parserPath = FactsParser(_pristine.Seed);
        using var invoker = new SIL.Motif.Host.PanGloss.PanGlossInvoker(parserPath);
        var job = CreateJob("P-allo-alternation-family",
            new ParsimonyScopeBinding(ParsimonyEvidenceScopeKind.ProjectApproved, null));
        var handler = new ParsimonyJobHandler(_database, _baselines, _projectKey, _project,
            new RunnerOptions { Root = Path.Combine(_root, "alternation-several-worker"), ParserPath = parserPath }, invoker);
        var outcome = await handler.RunAsync(Claim(job.JobId), CancellationToken.None);
        Assert.True(outcome!.Status == JobStatus.Completed, outcome.ResultJson);

        using var result = JsonDocument.Parse(outcome.ResultJson!);
        var bundleId = result.RootElement.GetProperty("bundleId").GetString()!;
        var bundle = new ParsimonyBundleRepository(_database).Get(bundleId)!;
        AddNamedMeasureFacts(bundle.GrammarFactsPath);
        AddAlternationFamilyGrammarFacts(bundle.GrammarFactsPath);

        var severalPath = Path.Combine(_root, "alternation-with-several-branches.sqlite");
        File.Copy(bundle.GrammarFactsPath, severalPath);
        AddExactAlternationRule(severalPath);
        AddSecondRewriteBranch(severalPath);
        var inputs = new ParsimonyReportInputs(bundle.BundleId, _token, "baseline", null,
            bundle.ModelFingerprint,
            new ParsimonyArtifactDigest(bundle.GrammarFactsSchemaVersion, Sha256Of(severalPath)),
            new ParsimonyArtifactDigest(bundle.EvidenceSchemaVersion, bundle.EvidenceSha256),
            null, null, [], ParsimonyEvidenceScopeKind.ProjectApproved);
        using var session = new ParsimonyQuerySession(severalPath, bundle.EvidencePath, inputs);
        var measured = MeasureRunner.Execute("P-allo-alternation-family", session, bundleId);
        Assert.Equal(ParsimonyMeasureStatus.Inconclusive, measured.Run.Status);
        Assert.Contains("additional or unresolved rule structure", measured.Run.Detail ?? string.Empty, StringComparison.Ordinal);
        Assert.NotEmpty(measured.Findings);
    }

    private static void AddSecondRewriteBranch(string factsPath)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(factsPath),
            Pooling = false,
            ForeignKeys = true,
        }.ToString());
        connection.Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO rewrite_rhs(rule_guid, ordinal)
            VALUES ('90000000-0000-0000-0000-000000000001', 1);
            INSERT INTO pattern_root(root_id, owner_kind, owner_guid, role, ordinal, source_kind)
            VALUES (203, 'phonologicalRule', '90000000-0000-0000-0000-000000000001', 'rewrite_sc', 1, 'authored');
            INSERT INTO pattern_node(node_id, root_id, parent_node_id, ordinal, kind, phoneme_guid)
            VALUES (203, 203, NULL, 0, 'phoneme', '40000000-0000-0000-0000-000000000003');
            UPDATE rewrite_rhs SET change_root_id = 203
            WHERE rule_guid = '90000000-0000-0000-0000-000000000001' AND ordinal = 1;
            """;
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    private static void AddUnconditionedAllomorphFacts(string factsPath)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(factsPath),
            Pooling = false,
            ForeignKeys = true,
        }.ToString());
        connection.Open();
        using var transaction = connection.BeginTransaction();
        void Execute(string sql, params (string Name, object Value)[] values)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            foreach (var (name, value) in values) command.Parameters.AddWithValue(name, value);
            command.ExecuteNonQuery();
        }

        const string entry = "30000000-0000-0000-0000-000000000001";
        const string mainMsa = "30000000-0000-0000-0000-000000000101";
        const string separateUnconditionedMsa = "30000000-0000-0000-0000-000000000102";
        const string separateConditionedMsa = "30000000-0000-0000-0000-000000000103";
        const string ambiguousMsa = "30000000-0000-0000-0000-000000000104";
        const string conditionedEnvironment = "30000000-0000-0000-0000-000000000201";
        const string invalidEnvironment = "30000000-0000-0000-0000-000000000202";
        Execute("INSERT INTO lex_entry(guid, source_ordinal, lexeme_morph_type) VALUES ($guid, 1, 'stem');",
            ("$guid", entry));
        Execute("INSERT INTO entry_citation_form(entry_guid, ordinal, writing_system, form) " +
                "VALUES ($entry, 0, 'qaa', 'unconditioned-allomorph-lemma');", ("$entry", entry));
        foreach (var msa in new[] { mainMsa, separateUnconditionedMsa, separateConditionedMsa, ambiguousMsa })
            Execute("INSERT INTO msa(msa_guid, entry_guid, kind) VALUES ($msa, $entry, 'stem');",
                ("$msa", msa), ("$entry", entry));

        var allomorphs = new[]
        {
            (Guid: "30000000-0000-0000-0000-000000000011", Ordinal: 1, Form: "broad"),
            (Guid: "30000000-0000-0000-0000-000000000012", Ordinal: 0, Form: "conditioned"),
            (Guid: "30000000-0000-0000-0000-000000000013", Ordinal: 2, Form: "fallback"),
            (Guid: "30000000-0000-0000-0000-000000000014", Ordinal: 3, Form: "invalid"),
            (Guid: "30000000-0000-0000-0000-000000000015", Ordinal: 4, Form: "other-unconditioned"),
            (Guid: "30000000-0000-0000-0000-000000000016", Ordinal: 5, Form: "other-conditioned"),
        };
        foreach (var item in allomorphs)
        {
            Execute("INSERT INTO allomorph(guid, entry_guid, ordinal, morph_type, form_class, is_abstract) " +
                    "VALUES ($guid, $entry, $ordinal, 'stem', 'stem', 0);",
                ("$guid", item.Guid), ("$entry", entry), ("$ordinal", item.Ordinal));
            Execute("INSERT INTO allomorph_form(allomorph_guid, ordinal, writing_system, form) " +
                    "VALUES ($guid, 0, 'qaa', $form);", ("$guid", item.Guid), ("$form", item.Form));
        }

        Execute("INSERT INTO environment(guid, name, representation, parse_status) " +
                "VALUES ($guid, 'vowel context', 'V _', 'valid');", ("$guid", conditionedEnvironment));
        Execute("INSERT INTO environment(guid, name, representation, parse_status, parse_error_code, parse_error_text) " +
                "VALUES ($guid, 'invalid context', '[', 'invalid', 'invalid-expression', 'bad condition');",
            ("$guid", invalidEnvironment));
        foreach (var (allomorph, environment) in new[]
                 {
                     ("30000000-0000-0000-0000-000000000012", conditionedEnvironment),
                     ("30000000-0000-0000-0000-000000000014", invalidEnvironment),
                     ("30000000-0000-0000-0000-000000000016", conditionedEnvironment),
                 })
            Execute("INSERT INTO allomorph_environment(allomorph_guid, role, ordinal, environment_guid) " +
                    "VALUES ($allomorph, 'phone', 0, $environment);",
                ("$allomorph", allomorph), ("$environment", environment));
        foreach (var (allomorph, environment, compiled, outcome) in new[]
                 {
                     ("30000000-0000-0000-0000-000000000012", conditionedEnvironment, 1, "represented"),
                     ("30000000-0000-0000-0000-000000000014", invalidEnvironment, 0, "invalid"),
                     ("30000000-0000-0000-0000-000000000016", conditionedEnvironment, 1, "represented"),
                 })
            Execute("INSERT INTO environment_usage(allomorph_guid, role, ordinal, compile_context_key, " +
                    "environment_guid, resolved_environment_guid, compiled, result) " +
                    "VALUES ($allomorph, 'phone', 0, 'production', $environment, $environment, $compiled, $outcome);",
                ("$allomorph", allomorph), ("$environment", environment),
                ("$compiled", compiled), ("$outcome", outcome));

        void Order(string owner, string msa, string allomorph, string output, int? order, bool hasPhoneCondition,
            bool hasMorphGate = false)
        {
            if (order is null)
            {
                Execute(CompiledOutputSql.EnsureOwner + CompiledOutputSql.CandidateOrderRow,
                    ("$owner", owner), ("$bucket", "Morphology"), ("$entry", entry), ("$msa", msa),
                    ("$sourceKey", allomorph), ("$allomorph", allomorph));
                return;
            }
            Execute(CompiledOutputSql.EnsureOwner + CompiledOutputSql.EnsureAllomorphOutput + CompiledOutputSql.OrderRow,
                ("$owner", owner), ("$bucket", "Morphology"), ("$output", output), ("$order", order.Value),
                ("$phone", hasPhoneCondition ? 1 : 0), ("$gate", hasMorphGate ? 1 : 0), ("$entry", entry), ("$msa", msa),
                ("$sourceKey", allomorph), ("$allomorph", allomorph));
        }

        const string owner = "unconditioned-allomorph-main-family";
        Order(owner, mainMsa, "30000000-0000-0000-0000-000000000011", "unconditioned-allomorph-out-0", 0, false);
        Order(owner, mainMsa, "30000000-0000-0000-0000-000000000014", "unconditioned-allomorph-invalid-out", 1, true);
        Order(owner, mainMsa, "30000000-0000-0000-0000-000000000012", "unconditioned-allomorph-out-2", 2, true);
        Order(owner, mainMsa, "30000000-0000-0000-0000-000000000013", "unconditioned-allomorph-out-3", 3, false);
        Order("unconditioned-allomorph-other-unconditioned", separateUnconditionedMsa,
            "30000000-0000-0000-0000-000000000015", "unconditioned-allomorph-other-out-0", 0, false);
        Order("unconditioned-allomorph-other-conditioned", separateConditionedMsa,
            "30000000-0000-0000-0000-000000000016", "unconditioned-allomorph-other-out-0", 0, true);
        Execute("INSERT INTO allomorph(guid, entry_guid, ordinal, morph_type, form_class, is_abstract) " +
                "VALUES ('30000000-0000-0000-0000-000000000017', $entry, 6, 'stem', 'stem', 1);",
            ("$entry", entry));
        Order(owner, mainMsa, "30000000-0000-0000-0000-000000000017", "unconditioned-allomorph-abstract-out", 3, true);
        Order("unconditioned-allomorph-ambiguous-output", ambiguousMsa,
            "30000000-0000-0000-0000-000000000015", "unconditioned-allomorph-ambiguous-out", 0, false);
        Order("unconditioned-allomorph-ambiguous-output", ambiguousMsa,
            "30000000-0000-0000-0000-000000000016", "unconditioned-allomorph-ambiguous-out", 1, false);
        Execute("UPDATE artifact_section SET status='complete', reason_code=NULL " +
                "WHERE section='compiled_mappings';");
        transaction.Commit();
    }

    // One family: an unconditioned candidate, a phone-conditioned sibling, and optionally a morph-gated top.
    private static void AddFinalElsewhereFamilyFacts(string factsPath, bool gatedTop)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(factsPath),
            Pooling = false,
            ForeignKeys = true,
        }.ToString());
        connection.Open();
        using var transaction = connection.BeginTransaction();
        void Execute(string sql, params (string Name, object Value)[] values)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            foreach (var (name, value) in values) command.Parameters.AddWithValue(name, value);
            command.ExecuteNonQuery();
        }

        const string entry = "30000000-0000-0000-0000-000000000501";
        const string msa = "30000000-0000-0000-0000-000000000502";
        const string owner = "final-elsewhere-family";
        const string environment = "30000000-0000-0000-0000-000000000521";
        const string plain = "30000000-0000-0000-0000-000000000511";
        const string conditioned = "30000000-0000-0000-0000-000000000512";
        const string gated = "30000000-0000-0000-0000-000000000513";

        Execute("INSERT INTO lex_entry(guid, source_ordinal, lexeme_morph_type) VALUES ($guid, 2, 'stem');",
            ("$guid", entry));
        Execute("INSERT INTO msa(msa_guid, entry_guid, kind) VALUES ($msa, $entry, 'stem');",
            ("$msa", msa), ("$entry", entry));
        Execute("INSERT INTO environment(guid, name, representation, parse_status) " +
                "VALUES ($guid, 'final context', 'V _', 'valid');", ("$guid", environment));
        var allomorphs = new List<(string Guid, int Ordinal, string Form)>
        {
            (plain, 0, "plain"),
            (conditioned, 1, "conditioned"),
        };
        if (gatedTop) allomorphs.Add((gated, 2, "gated"));
        foreach (var item in allomorphs)
        {
            Execute("INSERT INTO allomorph(guid, entry_guid, ordinal, morph_type, form_class, is_abstract) " +
                    "VALUES ($guid, $entry, $ordinal, 'stem', 'stem', 0);",
                ("$guid", item.Guid), ("$entry", entry), ("$ordinal", item.Ordinal));
            Execute("INSERT INTO allomorph_form(allomorph_guid, ordinal, writing_system, form) " +
                    "VALUES ($guid, 0, 'qaa', $form);", ("$guid", item.Guid), ("$form", item.Form));
        }
        Execute("INSERT INTO allomorph_environment(allomorph_guid, role, ordinal, environment_guid) " +
                "VALUES ($allomorph, 'phone', 0, $environment);",
            ("$allomorph", conditioned), ("$environment", environment));
        Execute("INSERT INTO environment_usage(allomorph_guid, role, ordinal, compile_context_key, " +
                "environment_guid, resolved_environment_guid, compiled, result) " +
                "VALUES ($allomorph, 'phone', 0, 'production', $environment, $environment, 1, 'represented');",
            ("$allomorph", conditioned), ("$environment", environment));

        void Order(string allomorph, int order, bool hasPhoneCondition, bool hasMorphGate)
        {
            Execute(CompiledOutputSql.EnsureOwner + CompiledOutputSql.EnsureAllomorphOutput + CompiledOutputSql.OrderRow,
                ("$owner", owner), ("$bucket", "Morphology"), ("$output", "final-elsewhere-out-" + order),
                ("$order", order), ("$phone", hasPhoneCondition ? 1 : 0), ("$gate", hasMorphGate ? 1 : 0),
                ("$entry", entry), ("$msa", msa), ("$sourceKey", allomorph), ("$allomorph", allomorph));
        }

        Order(plain, 0, hasPhoneCondition: false, hasMorphGate: false);
        Order(conditioned, 1, hasPhoneCondition: true, hasMorphGate: false);
        if (gatedTop) Order(gated, 2, hasPhoneCondition: false, hasMorphGate: true);
        Execute("UPDATE artifact_section SET status='complete', reason_code=NULL " +
                "WHERE section='compiled_mappings';");
        transaction.Commit();
    }

    private static void AddEnvironmentAndNaturalClassFacts(string factsPath)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(factsPath), Pooling = false, ForeignKeys = true,
        }.ToString());
        connection.Open();
        using var transaction = connection.BeginTransaction();
        void Execute(string sql, params (string Name, object Value)[] values)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            foreach (var (name, value) in values) command.Parameters.AddWithValue(name, value);
            command.ExecuteNonQuery();
        }

        const string targetEntry = "91000000-0000-0000-0000-000000000001";
        const string targetMsa = "91000000-0000-0000-0000-000000000002";
        const string labialFeature = "91000000-0000-0000-0000-000000000003";
        const string labialValue = "91000000-0000-0000-0000-000000000004";
        const string alveolarValue = "91000000-0000-0000-0000-000000000005";
        const string p = "91000000-0000-0000-0000-000000000011";
        const string b = "91000000-0000-0000-0000-000000000012";
        const string t = "91000000-0000-0000-0000-000000000013";
        const string s = "91000000-0000-0000-0000-000000000014";

        Execute("INSERT OR IGNORE INTO writing_system(tag) VALUES ('qaa');");
        Execute("INSERT INTO lex_entry(guid, source_ordinal, lexeme_morph_type) " +
                "VALUES ($guid, 910, 'suffix');", ("$guid", targetEntry));
        Execute("INSERT INTO msa(msa_guid, entry_guid, kind) VALUES ($msa, $entry, 'inflectional');",
            ("$msa", targetMsa), ("$entry", targetEntry));
        Execute("INSERT INTO feature(guid, system, kind, name, abbreviation) " +
                "VALUES ($guid, 'phonological', 'closed', 'Place', 'pl');", ("$guid", labialFeature));
        foreach (var (guid, ordinal, name) in new[]
                 { (labialValue, 0, "Labial"), (alveolarValue, 1, "Alveolar") })
            Execute("INSERT INTO feature_value(guid, feature_guid, ordinal, name, abbreviation) " +
                    "VALUES ($guid, $feature, $ordinal, $name, $name);",
                ("$guid", guid), ("$feature", labialFeature), ("$ordinal", ordinal), ("$name", name));
        var fs = 910001;
        foreach (var (guid, name, value) in new[]
                 { (p, "p", labialValue), (b, "b", labialValue), (t, "t", alveolarValue), (s, "s", (string?)null) })
        {
            if (value is not null)
            {
                Execute("INSERT INTO feature_structure(fs_id, system, owner_kind, owner_guid, role, path) " +
                        "VALUES ($fs, 'phonological', 'phoneme', $guid, 'features', '');",
                    ("$fs", fs), ("$guid", guid));
            }
            Execute("INSERT INTO phoneme(guid, feature_structure_id, name) VALUES ($guid, $fs, $name);",
                ("$guid", guid), ("$fs", value is null ? DBNull.Value : fs), ("$name", name));
            Execute("INSERT INTO phoneme_grapheme(phoneme_guid, ordinal, writing_system, grapheme) " +
                    "VALUES ($guid, 0, 'qaa', $name);", ("$guid", guid), ("$name", name));
            if (value is not null)
                Execute("INSERT INTO feature_assignment(fs_id, ordinal, feature_guid, value_kind, value_guid) " +
                        "VALUES ($fs, 0, $feature, 'closed', $value);",
                    ("$fs", fs), ("$feature", labialFeature), ("$value", value));
            fs++;
        }
        Execute("INSERT INTO natural_class(guid, kind, name) VALUES ($guid, 'segments', 'labials');",
            ("$guid", BroadEnvironmentNaturalClassGuid));
        foreach (var (phoneme, ordinal) in new[] { (p, 0), (b, 1) })
        {
            Execute("INSERT INTO natural_class_member(natural_class_guid, ordinal, phoneme_guid) " +
                    "VALUES ($class, $ordinal, $phoneme);",
                ("$class", BroadEnvironmentNaturalClassGuid), ("$ordinal", ordinal), ("$phoneme", phoneme));
            Execute("INSERT INTO natural_class_effective_member(natural_class_guid, table_key, member_key, " +
                    "phoneme_guid, identity_quality, match_kind, match_basis) " +
                    "VALUES ($class, 'segments', $phoneme, $phoneme, 'sourceGuid', 'segments', 'listed');",
                ("$class", BroadEnvironmentNaturalClassGuid), ("$phoneme", phoneme));
        }
        Execute("INSERT INTO environment(guid, name, representation, parse_status) " +
                "VALUES ($guid, 'labial left context', '[labials] _', 'valid');", ("$guid", BroadEnvironmentGuid));
        Execute("INSERT INTO environment_natural_class(environment_guid, compile_context_key, side, token_path, " +
                "token_text, source_start, source_end, natural_class_guid, result) " +
                "VALUES ($env, 'production', 'left', '0', '[labials]', 0, 9, $class, 'resolved');",
            ("$env", BroadEnvironmentGuid), ("$class", BroadEnvironmentNaturalClassGuid));
        Execute("INSERT INTO pattern_root(owner_kind, owner_guid, role, ordinal, source_kind) " +
                "VALUES ('environment', $env, 'environment_left', 0, 'resolved_environment');",
            ("$env", BroadEnvironmentGuid));
        Execute("INSERT INTO pattern_node(root_id, parent_node_id, ordinal, kind, natural_class_guid) " +
                "VALUES (last_insert_rowid(), NULL, 0, 'naturalClass', $class);",
            ("$class", BroadEnvironmentNaturalClassGuid));
        Execute("INSERT INTO allomorph(guid, entry_guid, ordinal, morph_type, form_class, is_abstract) " +
                "VALUES ($guid, $entry, 0, 'suffix', 'affix', 0);",
            ("$guid", BroadEnvironmentTargetAllomorphGuid), ("$entry", targetEntry));
        Execute("INSERT INTO allomorph_form(allomorph_guid, ordinal, writing_system, form) " +
                "VALUES ($guid, 0, 'qaa', '');", ("$guid", BroadEnvironmentTargetAllomorphGuid));
        Execute("INSERT INTO allomorph_environment(allomorph_guid, role, ordinal, environment_guid) " +
                "VALUES ($allomorph, 'phone', 0, $env);",
            ("$allomorph", BroadEnvironmentTargetAllomorphGuid), ("$env", BroadEnvironmentGuid));
        Execute("INSERT INTO environment_usage(allomorph_guid, role, ordinal, compile_context_key, " +
                "environment_guid, resolved_environment_guid, compiled, result) " +
                "VALUES ($allomorph, 'phone', 0, 'production', $env, $env, 1, 'represented');",
            ("$allomorph", BroadEnvironmentTargetAllomorphGuid), ("$env", BroadEnvironmentGuid));
        Execute(CompiledOutputSql.EnsureOwner + CompiledOutputSql.EnsureAllomorphOutput + CompiledOutputSql.OrderRow,
            ("$owner", "broad-environment-owner"), ("$bucket", "Morphology"), ("$output", "broad-environment-output"),
            ("$order", 0), ("$phone", 1), ("$gate", 0), ("$entry", targetEntry), ("$msa", targetMsa),
            ("$sourceKey", BroadEnvironmentTargetAllomorphGuid), ("$allomorph", BroadEnvironmentTargetAllomorphGuid));

        for (var index = 1; index <= 3; index++)
        {
            var stemGuid = $"92000000-0000-0000-0000-{index:D12}";
            var stemMsaGuid = $"93000000-0000-0000-0000-{index:D12}";
            var stemAllomorphGuid = $"94000000-0000-0000-0000-{index:D12}";
            Execute("INSERT INTO lex_entry(guid, source_ordinal, lexeme_morph_type) " +
                    "VALUES ($guid, $ordinal, 'stem');", ("$guid", stemGuid), ("$ordinal", 920 + index));
            Execute("INSERT INTO msa(msa_guid, entry_guid, kind) VALUES ($msa, $entry, 'stem');",
                ("$msa", stemMsaGuid), ("$entry", stemGuid));
            Execute("INSERT INTO allomorph(guid, entry_guid, ordinal, morph_type, form_class, is_abstract) " +
                    "VALUES ($guid, $entry, 0, 'stem', 'stem', 0);", ("$guid", stemAllomorphGuid), ("$entry", stemGuid));
            Execute("INSERT INTO allomorph_form(allomorph_guid, ordinal, writing_system, form) " +
                    "VALUES ($guid, 0, 'qaa', $form);", ("$guid", stemAllomorphGuid), ("$form", new string('p', index)));
        }
        Execute("UPDATE artifact_section SET status='complete', reason_code=NULL WHERE section IN " +
                "('entries', 'msas', 'allomorphs', 'features', 'phonology', 'environments', 'patterns', 'compiled_mappings');");
        transaction.Commit();
    }

    private static void AddBroadEnvironmentOrAlternative(string factsPath)
    {
        const string environment = "90000000-0000-0000-0000-000000000004";
        const string t = "91000000-0000-0000-0000-000000000013";
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(factsPath), Pooling = false, ForeignKeys = true,
        }.ToString());
        connection.Open();
        using var transaction = connection.BeginTransaction();
        void Execute(string sql, params (string Name, object Value)[] values)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            foreach (var (name, value) in values) command.Parameters.AddWithValue(name, value);
            command.ExecuteNonQuery();
        }
        Execute("INSERT INTO environment(guid, name, representation, parse_status) " +
                "VALUES ($env, 'alveolar left context', 't _', 'valid');", ("$env", environment));
        Execute("INSERT INTO pattern_root(owner_kind, owner_guid, role, ordinal, source_kind) " +
                "VALUES ('environment', $env, 'environment_left', 0, 'resolved_environment');",
            ("$env", environment));
        Execute("INSERT INTO pattern_node(root_id, parent_node_id, ordinal, kind, phoneme_guid) " +
                "VALUES (last_insert_rowid(), NULL, 0, 'phoneme', $phone);", ("$phone", t));
        Execute("INSERT INTO allomorph_environment(allomorph_guid, role, ordinal, environment_guid) " +
                "VALUES ($allomorph, 'phone', 1, $env);",
            ("$allomorph", BroadEnvironmentTargetAllomorphGuid), ("$env", environment));
        foreach (var context in new[] { "production", "default" })
            Execute("INSERT INTO environment_usage(allomorph_guid, role, ordinal, compile_context_key, " +
                    "environment_guid, resolved_environment_guid, compiled, result) " +
                    "VALUES ($allomorph, 'phone', 1, $context, $env, $env, 1, 'represented');",
                ("$allomorph", BroadEnvironmentTargetAllomorphGuid), ("$context", context), ("$env", environment));
        Execute("INSERT INTO environment_usage(allomorph_guid, role, ordinal, compile_context_key, " +
                "environment_guid, resolved_environment_guid, compiled, result) " +
                "VALUES ($allomorph, 'phone', 0, 'default', $env, $env, 1, 'represented');",
            ("$allomorph", BroadEnvironmentTargetAllomorphGuid), ("$env", BroadEnvironmentGuid));
        Execute(CompiledOutputSql.EnsureOwner + CompiledOutputSql.EnsureAllomorphOutput + CompiledOutputSql.OrderRow,
            ("$owner", "broad-environment-owner"), ("$bucket", "Phonology"), ("$output", "broad-environment-default-output"),
            ("$order", 0), ("$phone", 1), ("$gate", 0), ("$entry", "91000000-0000-0000-0000-000000000001"),
            ("$msa", "91000000-0000-0000-0000-000000000002"), ("$sourceKey", BroadEnvironmentTargetAllomorphGuid),
            ("$allomorph", BroadEnvironmentTargetAllomorphGuid));
        transaction.Commit();
    }

    private static void AddBroadEnvironmentGateBucket(string factsPath)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(factsPath), Pooling = false, ForeignKeys = true,
        }.ToString());
        connection.Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO environment_usage(allomorph_guid, role, ordinal, compile_context_key, " +
                              "environment_guid, resolved_environment_guid, compiled, result) " +
                              "VALUES ($allomorph, 'phone', 0, 'default', $environment, $environment, 1, 'represented');" +
                              CompiledOutputSql.EnsureOwner + CompiledOutputSql.EnsureAllomorphOutput +
                              CompiledOutputSql.OrderRow;
        command.Parameters.AddWithValue("$allomorph", BroadEnvironmentTargetAllomorphGuid);
        command.Parameters.AddWithValue("$sourceKey", BroadEnvironmentTargetAllomorphGuid);
        command.Parameters.AddWithValue("$environment", BroadEnvironmentGuid);
        command.Parameters.AddWithValue("$owner", "broad-environment-owner");
        command.Parameters.AddWithValue("$bucket", "Phonology");
        command.Parameters.AddWithValue("$output", "broad-environment-default-output");
        command.Parameters.AddWithValue("$order", 0);
        command.Parameters.AddWithValue("$phone", 1);
        command.Parameters.AddWithValue("$gate", 0);
        command.Parameters.AddWithValue("$entry", "91000000-0000-0000-0000-000000000001");
        command.Parameters.AddWithValue("$msa", "91000000-0000-0000-0000-000000000002");
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    private static void AddEnvironmentAndNaturalClassEvidence(string evidencePath)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(evidencePath), Pooling = false, ForeignKeys = true,
        }.ToString());
        connection.Open();
        using var transaction = connection.BeginTransaction();
        void Execute(string sql, params (string Name, object Value)[] values)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            foreach (var (name, value) in values) command.Parameters.AddWithValue(name, value);
            command.ExecuteNonQuery();
        }
        for (var index = 1; index <= 3; index++)
        {
            var stemGuid = $"92000000-0000-0000-0000-{index:D12}";
            var stemMsaGuid = $"93000000-0000-0000-0000-{index:D12}";
            var stemAllomorphGuid = $"94000000-0000-0000-0000-{index:D12}";
            var wordformGuid = $"95000000-0000-0000-0000-{index:D12}";
            var analysisGuid = $"96000000-0000-0000-0000-{index:D12}";
            Execute("INSERT INTO wordforms(wordform_guid, spelling_status) VALUES ($guid, 0);",
                ("$guid", wordformGuid));
            Execute("INSERT INTO wordform_forms(wordform_guid, writing_system, form, form_nfd) " +
                    "VALUES ($guid, 'qaa', $form, $form);",
                ("$guid", wordformGuid), ("$form", new string('p', index)));
            Execute("INSERT INTO analyses(analysis_guid, wordform_guid, opinion, source_kind, content_sha256) " +
                    "VALUES ($analysis, $wordform, 'approved', 'fixture', $digest);",
                ("$analysis", analysisGuid), ("$wordform", wordformGuid), ("$digest", new string('0', 64)));
            Execute("INSERT INTO analysis_morphs(analysis_guid, ordinal, morph_guid, msa_guid, entry_guid) " +
                    "VALUES ($analysis, 0, $morph, $msa, $entry);",
                ("$analysis", analysisGuid), ("$morph", stemAllomorphGuid),
                ("$msa", stemMsaGuid), ("$entry", stemGuid));
            Execute("INSERT INTO analysis_morph_forms(analysis_guid, morph_ordinal, writing_system, form, form_nfd) " +
                    "VALUES ($analysis, 0, 'qaa', $form, $form);",
                ("$analysis", analysisGuid), ("$form", new string('p', index)));
            Execute("INSERT INTO analysis_morphs(analysis_guid, ordinal, morph_guid, msa_guid, entry_guid) " +
                    "VALUES ($analysis, 1, $morph, $msa, $entry);",
                ("$analysis", analysisGuid), ("$morph", BroadEnvironmentTargetAllomorphGuid),
                ("$msa", "91000000-0000-0000-0000-000000000002"),
                ("$entry", "91000000-0000-0000-0000-000000000001"));
            Execute("INSERT INTO analysis_morph_forms(analysis_guid, morph_ordinal, writing_system, form, form_nfd) " +
                    "VALUES ($analysis, 1, 'qaa', '', '');", ("$analysis", analysisGuid));
        }
        Execute("UPDATE artifact_metadata SET wordform_count=(SELECT COUNT(*) FROM wordforms), " +
                "analysis_count=(SELECT COUNT(*) FROM analyses) WHERE singleton=1;");
        transaction.Commit();
    }

    private static void AddNaturalClassLeakFacts(string factsPath)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(factsPath), Pooling = false, ForeignKeys = true,
        }.ToString());
        connection.Open();
        using var transaction = connection.BeginTransaction();
        void Execute(string sql, params (string Name, object Value)[] values)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            foreach (var (name, value) in values) command.Parameters.AddWithValue(name, value);
            command.ExecuteNonQuery();
        }

        const string feature = "91000000-0000-0000-0000-000000000003";
        const string labial = "91000000-0000-0000-0000-000000000004";
        const string classGuid = "90000000-0000-0000-0000-000000000002";
        const string m = "91000000-0000-0000-0000-000000000016";
        Execute("INSERT INTO feature_structure(fs_id, system, owner_kind, owner_guid, role, path) " +
                "VALUES (910005, 'phonological', 'phoneme', $phoneme, 'features', '');", ("$phoneme", m));
        Execute("INSERT INTO phoneme(guid, feature_structure_id, name) VALUES ($guid, 910005, 'm');",
            ("$guid", m));
        Execute("INSERT INTO phoneme_grapheme(phoneme_guid, ordinal, writing_system, grapheme) " +
                "VALUES ($guid, 0, 'qaa', 'm');", ("$guid", m));
        Execute("INSERT INTO feature_assignment(fs_id, ordinal, feature_guid, value_kind, value_guid) " +
                "VALUES (910005, 0, $feature, 'closed', $value);",
            ("$feature", feature), ("$value", labial));
        foreach (var (phoneme, ordinal) in new[]
                 {
                     (m, 2),
                     ("91000000-0000-0000-0000-000000000013", 3),
                 })
        {
            Execute("INSERT INTO natural_class_member(natural_class_guid, ordinal, phoneme_guid) " +
                    "VALUES ($class, $ordinal, $phoneme);",
                ("$class", classGuid), ("$ordinal", ordinal), ("$phoneme", phoneme));
            Execute("INSERT INTO natural_class_effective_member(natural_class_guid, table_key, member_key, " +
                    "phoneme_guid, identity_quality, match_kind, match_basis) " +
                    "VALUES ($class, 'segments', $phoneme, $phoneme, 'sourceGuid', 'segments', 'listed');",
                ("$class", classGuid), ("$phoneme", phoneme));
        }
        foreach (var (index, form) in new[] { (4, "b"), (5, "m") })
        {
            var stemEntry = $"92000000-0000-0000-0000-{index:D12}";
            var stemMsa = $"93000000-0000-0000-0000-{index:D12}";
            var stemAllomorph = $"94000000-0000-0000-0000-{index:D12}";
            Execute("INSERT INTO lex_entry(guid, source_ordinal, lexeme_morph_type) " +
                    "VALUES ($entry, $ordinal, 'stem');", ("$entry", stemEntry), ("$ordinal", 920 + index));
            Execute("INSERT INTO msa(msa_guid, entry_guid, kind) VALUES ($msa, $entry, 'stem');",
                ("$msa", stemMsa), ("$entry", stemEntry));
            Execute("INSERT INTO allomorph(guid, entry_guid, ordinal, morph_type, form_class, is_abstract) " +
                    "VALUES ($guid, $entry, 0, 'stem', 'stem', 0);", ("$guid", stemAllomorph), ("$entry", stemEntry));
            Execute("INSERT INTO allomorph_form(allomorph_guid, ordinal, writing_system, form) " +
                    "VALUES ($guid, 0, 'qaa', $form);", ("$guid", stemAllomorph), ("$form", form));
        }
        transaction.Commit();
    }

    private static void AddNaturalClassHeldOutEvidence(string evidencePath)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(evidencePath), Pooling = false, ForeignKeys = true,
        }.ToString());
        connection.Open();
        using var transaction = connection.BeginTransaction();
        void Execute(string sql, params (string Name, object Value)[] values)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            foreach (var (name, value) in values) command.Parameters.AddWithValue(name, value);
            command.ExecuteNonQuery();
        }

        foreach (var (index, form) in new[] { (4, "b"), (5, "m") })
        {
            var id = index.ToString("D12");
            var stemEntry = $"92000000-0000-0000-0000-{id}";
            var stemMsa = $"93000000-0000-0000-0000-{id}";
            var stemAllomorph = $"94000000-0000-0000-0000-{id}";
            var wordform = $"95000000-0000-0000-0000-{id}";
            var analysis = $"96000000-0000-0000-0000-{id}";
            Execute("INSERT INTO wordforms(wordform_guid, spelling_status) VALUES ($guid, 0);",
                ("$guid", wordform));
            Execute("INSERT INTO wordform_forms(wordform_guid, writing_system, form, form_nfd) " +
                    "VALUES ($guid, 'qaa', $form, $form);", ("$guid", wordform), ("$form", form));
            Execute("INSERT INTO analyses(analysis_guid, wordform_guid, opinion, source_kind, content_sha256) " +
                    "VALUES ($analysis, $wordform, 'approved', 'fixture', $digest);",
                ("$analysis", analysis), ("$wordform", wordform), ("$digest", new string('2', 64)));
            Execute("INSERT INTO analysis_morphs(analysis_guid, ordinal, morph_guid, msa_guid, entry_guid) " +
                    "VALUES ($analysis, 0, $morph, $msa, $entry);",
                ("$analysis", analysis), ("$morph", stemAllomorph), ("$msa", stemMsa), ("$entry", stemEntry));
            Execute("INSERT INTO analysis_morph_forms(analysis_guid, morph_ordinal, writing_system, form, form_nfd) " +
                    "VALUES ($analysis, 0, 'qaa', $form, $form);",
                ("$analysis", analysis), ("$form", form));
            Execute("INSERT INTO analysis_morphs(analysis_guid, ordinal, morph_guid, msa_guid, entry_guid) " +
                    "VALUES ($analysis, 1, $morph, $msa, $entry);",
                ("$analysis", analysis), ("$morph", BroadEnvironmentTargetAllomorphGuid),
                ("$msa", "91000000-0000-0000-0000-000000000002"),
                ("$entry", "91000000-0000-0000-0000-000000000001"));
            Execute("INSERT INTO analysis_morph_forms(analysis_guid, morph_ordinal, writing_system, form, form_nfd) " +
                    "VALUES ($analysis, 1, 'qaa', '', '');", ("$analysis", analysis));
        }
        Execute("UPDATE artifact_metadata SET wordform_count=(SELECT COUNT(*) FROM wordforms), " +
                "analysis_count=(SELECT COUNT(*) FROM analyses) WHERE singleton=1;");
        transaction.Commit();
    }

    private static void AddAmbiguousBroadEnvironmentGrapheme(string factsPath)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(factsPath), Pooling = false, ForeignKeys = true,
        }.ToString());
        connection.Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO phoneme(guid, feature_structure_id, name) " +
                              "VALUES ('91000000-0000-0000-0000-000000000015', NULL, 'pp'); " +
                              "INSERT INTO phoneme_grapheme(phoneme_guid, ordinal, writing_system, grapheme) " +
                              "VALUES ('91000000-0000-0000-0000-000000000015', 0, 'qaa', 'pp');";
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    private ParsimonyQuerySession OpenBroadEnvironmentSession(ParsimonyBundleRecord bundle, string factsPath,
        string evidencePath)
    {
        var inputs = new ParsimonyReportInputs(bundle.BundleId, _token, "baseline", null,
            bundle.ModelFingerprint,
            new ParsimonyArtifactDigest(bundle.GrammarFactsSchemaVersion, Sha256Of(factsPath)),
            new ParsimonyArtifactDigest(bundle.EvidenceSchemaVersion, Sha256Of(evidencePath)),
            null, null, [], ParsimonyEvidenceScopeKind.ProjectApproved);
        return new ParsimonyQuerySession(factsPath, evidencePath, inputs);
    }

    private static void AddUnsupportedBroadEnvironmentWindow(string factsPath)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(factsPath), Pooling = false, ForeignKeys = true,
        }.ToString());
        connection.Open();
        using var transaction = connection.BeginTransaction();
        long sequenceNode;
        using (var select = connection.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText = "SELECT node_id FROM pattern_node WHERE natural_class_guid=$class;";
            select.Parameters.AddWithValue("$class", BroadEnvironmentNaturalClassGuid);
            sequenceNode = (long)select.ExecuteScalar()!;
        }
        using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = "UPDATE pattern_node SET kind='sequence', natural_class_guid=NULL " +
                                 "WHERE node_id=$node;";
            update.Parameters.AddWithValue("$node", sequenceNode);
            Assert.Equal(1, update.ExecuteNonQuery());
        }
        foreach (var (ordinal, phoneme) in new[]
                 {
                     (0, "91000000-0000-0000-0000-000000000011"),
                     (1, "91000000-0000-0000-0000-000000000013"),
                 })
        {
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO pattern_node(root_id, parent_node_id, ordinal, kind, phoneme_guid) " +
                                 "SELECT root_id, $parent, $ordinal, 'phoneme', $phoneme FROM pattern_node " +
                                 "WHERE node_id=$parent;";
            insert.Parameters.AddWithValue("$parent", sequenceNode);
            insert.Parameters.AddWithValue("$ordinal", ordinal);
            insert.Parameters.AddWithValue("$phoneme", phoneme);
            Assert.Equal(1, insert.ExecuteNonQuery());
        }
        transaction.Commit();
    }

    private static void AddBroadEnvironmentWordBoundaryCase(string factsPath, string evidencePath,
        bool includeLiteralHashMarker)
    {
        var wordBoundaryGuid = LangProjectTags.kguidPhRuleWordBdry.ToString("D");
        const string boundaryTargetPhonemeGuid = "98000000-0000-0000-0000-000000000002";
        const string boundaryTargetForm = "\uE000";
        const string hashMarkerGuid = "98000000-0000-0000-0000-000000000001";
        const string hashEnvironmentGuid = "90000000-0000-0000-0000-000000000005";
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
               {
                   DataSource = Path.GetFullPath(factsPath), Pooling = false, ForeignKeys = true,
               }.ToString()))
        {
            connection.Open();
            using var transaction = connection.BeginTransaction();
            void Execute(string sql, params (string Name, object Value)[] values)
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = sql;
                foreach (var (name, value) in values) command.Parameters.AddWithValue(name, value);
                command.ExecuteNonQuery();
            }
            Execute("INSERT OR IGNORE INTO boundary_marker(guid, name) VALUES ($guid, 'word boundary');",
                ("$guid", wordBoundaryGuid));
            Execute("INSERT OR IGNORE INTO boundary_marker(guid, name) VALUES ($guid, '#');",
                ("$guid", hashMarkerGuid));
            Execute("INSERT INTO phoneme(guid, feature_structure_id, name) " +
                    "VALUES ($guid, NULL, 'word-edge fixture segment'); " +
                    "INSERT INTO phoneme_grapheme(phoneme_guid, ordinal, writing_system, grapheme) " +
                    "VALUES ($guid, 0, 'qaa', $form);",
                ("$guid", boundaryTargetPhonemeGuid), ("$form", boundaryTargetForm));
            for (var index = 1; index <= 3; index++)
            {
                var stemPhonemeGuid = $"98000000-0000-0000-0000-{index + 2:D12}";
                var stemForm = char.ConvertFromUtf32(0xE000 + index);
                Execute("INSERT INTO phoneme(guid, feature_structure_id, name) " +
                        "VALUES ($guid, NULL, 'word-edge fixture stem'); " +
                        "INSERT INTO phoneme_grapheme(phoneme_guid, ordinal, writing_system, grapheme) " +
                        "VALUES ($guid, 0, 'qaa', $form);",
                    ("$guid", stemPhonemeGuid), ("$form", stemForm));
            }
            Execute("DELETE FROM environment_usage WHERE allomorph_guid=$allomorph AND role='phone' AND ordinal=1;" +
                    "DELETE FROM allomorph_environment WHERE allomorph_guid=$allomorph AND role='phone' AND ordinal=1;" +
                    "DELETE FROM environment_natural_class WHERE environment_guid=$environment;" +
                    "UPDATE environment SET name='word-boundary context', representation='_#' WHERE guid=$environment;" +
                    "UPDATE allomorph_form SET form=$form WHERE allomorph_guid=$allomorph;" +
                    "UPDATE pattern_root SET role='environment_right' WHERE owner_kind='environment' " +
                    "AND owner_guid=$environment AND role='environment_left';" +
                    "UPDATE pattern_node SET kind='boundary', natural_class_guid=NULL, phoneme_guid=NULL, " +
                    "boundary_guid=$boundary WHERE node_id=(SELECT node_id FROM pattern_node " +
                    "WHERE natural_class_guid=$class);",
                ("$allomorph", BroadEnvironmentTargetAllomorphGuid), ("$environment", BroadEnvironmentGuid),
                ("$class", BroadEnvironmentNaturalClassGuid), ("$boundary", wordBoundaryGuid), ("$form", boundaryTargetForm));
            if (includeLiteralHashMarker)
            {
                Execute("INSERT INTO environment(guid, name, representation, parse_status) " +
                        "VALUES ($env, 'literal hash context', '_#', 'valid');", ("$env", hashEnvironmentGuid));
                Execute("INSERT INTO pattern_root(owner_kind, owner_guid, role, ordinal, source_kind) " +
                        "VALUES ('environment', $env, 'environment_right', 0, 'resolved_environment');",
                    ("$env", hashEnvironmentGuid));
                Execute("INSERT INTO pattern_node(root_id, parent_node_id, ordinal, kind, boundary_guid) " +
                        "VALUES (last_insert_rowid(), NULL, 0, 'boundary', $boundary);",
                    ("$boundary", hashMarkerGuid));
                Execute("INSERT INTO allomorph_environment(allomorph_guid, role, ordinal, environment_guid) " +
                        "VALUES ($allomorph, 'phone', 1, $env);",
                    ("$allomorph", BroadEnvironmentTargetAllomorphGuid), ("$env", hashEnvironmentGuid));
                foreach (var context in new[] { "production", "default" })
                    Execute("INSERT INTO environment_usage(allomorph_guid, role, ordinal, compile_context_key, " +
                            "environment_guid, resolved_environment_guid, compiled, result) " +
                            "VALUES ($allomorph, 'phone', 1, $context, $env, $env, 1, 'represented');",
                        ("$allomorph", BroadEnvironmentTargetAllomorphGuid), ("$context", context),
                        ("$env", hashEnvironmentGuid));
            }
            transaction.Commit();
        }

        using var evidence = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(evidencePath), Pooling = false, ForeignKeys = true,
        }.ToString());
        evidence.Open();
        using var evidenceTransaction = evidence.BeginTransaction();
        using (var updateTargetForm = evidence.CreateCommand())
        {
            updateTargetForm.Transaction = evidenceTransaction;
            updateTargetForm.CommandText = "UPDATE analysis_morph_forms SET form=$form, form_nfd=$form " +
                "WHERE morph_ordinal=1 AND form='';";
            updateTargetForm.Parameters.AddWithValue("$form", boundaryTargetForm);
            Assert.Equal(3, updateTargetForm.ExecuteNonQuery());
        }
        for (var index = 1; index <= 3; index++)
        {
            using var updateWordform = evidence.CreateCommand();
            updateWordform.Transaction = evidenceTransaction;
            updateWordform.CommandText = "UPDATE analysis_morph_forms SET form=$stem, form_nfd=$stem " +
                "WHERE analysis_guid=$analysis AND morph_ordinal=0; " +
                "UPDATE wordform_forms SET form=$stem || $target, form_nfd=$stem || $target " +
                "WHERE wordform_guid=(SELECT wordform_guid FROM analyses WHERE analysis_guid=$analysis);";
            updateWordform.Parameters.AddWithValue("$stem", char.ConvertFromUtf32(0xE000 + index));
            updateWordform.Parameters.AddWithValue("$target", boundaryTargetForm);
            updateWordform.Parameters.AddWithValue("$analysis",
                $"96000000-0000-0000-0000-{index:D12}");
            Assert.Equal(2, updateWordform.ExecuteNonQuery());
        }
        evidenceTransaction.Commit();
    }

    private static void RemoveNaturalClassObservedFeatureAssignments(string factsPath)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(factsPath), Pooling = false, ForeignKeys = true,
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM feature_assignment WHERE fs_id=910001;";
        Assert.Equal(1, command.ExecuteNonQuery());
    }

    private static void AddNaturalClassSecondUsageSite(string factsPath, string evidencePath)
    {
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
               {
                   DataSource = Path.GetFullPath(factsPath), Pooling = false, ForeignKeys = true,
               }.ToString()))
        {
            connection.Open();
            using var transaction = connection.BeginTransaction();
            void Execute(string sql, params (string Name, object Value)[] values)
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = sql;
                foreach (var (name, value) in values) command.Parameters.AddWithValue(name, value);
                command.ExecuteNonQuery();
            }

            const string classGuid = BroadEnvironmentNaturalClassGuid;
            const string targetEntry = "91000000-0000-0000-0000-000000000001";
            const string targetMsa = "91000000-0000-0000-0000-000000000002";
            const string stemEntry = "97000000-0000-0000-0000-000000000001";
            const string stemMsa = "97000000-0000-0000-0000-000000000002";
            const string stemAllomorph = "97000000-0000-0000-0000-000000000003";
            const string allomorph = "90000000-0000-0000-0000-000000000004";
            const string environment = "90000000-0000-0000-0000-000000000005";
            const string x = "91000000-0000-0000-0000-000000000017";
            Execute("INSERT INTO phoneme(guid, feature_structure_id, name) VALUES ($guid, NULL, 'x');",
                ("$guid", x));
            Execute("INSERT INTO phoneme_grapheme(phoneme_guid, ordinal, writing_system, grapheme) " +
                    "VALUES ($guid, 0, 'qaa', 'x');", ("$guid", x));
            Execute("INSERT INTO lex_entry(guid, source_ordinal, lexeme_morph_type) " +
                    "VALUES ($guid, 970, 'stem');", ("$guid", stemEntry));
            Execute("INSERT INTO msa(msa_guid, entry_guid, kind) VALUES ($msa, $entry, 'stem');",
                ("$msa", stemMsa), ("$entry", stemEntry));
            Execute("INSERT INTO allomorph(guid, entry_guid, ordinal, morph_type, form_class, is_abstract) " +
                    "VALUES ($guid, $entry, 0, 'stem', 'stem', 0);", ("$guid", stemAllomorph), ("$entry", stemEntry));
            Execute("INSERT INTO allomorph_form(allomorph_guid, ordinal, writing_system, form) " +
                    "VALUES ($guid, 0, 'qaa', 'b');", ("$guid", stemAllomorph));
            Execute("INSERT INTO allomorph(guid, entry_guid, ordinal, morph_type, form_class, is_abstract) " +
                    "VALUES ($guid, $entry, 1, 'suffix', 'affix', 0);", ("$guid", allomorph), ("$entry", targetEntry));
            Execute("INSERT INTO allomorph_form(allomorph_guid, ordinal, writing_system, form) " +
                    "VALUES ($guid, 0, 'qaa', 'x');", ("$guid", allomorph));
            Execute("INSERT INTO environment(guid, name, representation, parse_status) " +
                    "VALUES ($guid, 'second labial left context', '[labials] _', 'valid');",
                ("$guid", environment));
            Execute("INSERT INTO environment_natural_class(environment_guid, compile_context_key, side, token_path, " +
                    "token_text, source_start, source_end, natural_class_guid, result) " +
                    "VALUES ($env, 'production', 'left', '0', '[labials]', 0, 9, $class, 'resolved');",
                ("$env", environment), ("$class", classGuid));
            Execute("INSERT INTO pattern_root(owner_kind, owner_guid, role, ordinal, source_kind) " +
                    "VALUES ('environment', $env, 'environment_left', 0, 'resolved_environment');",
                ("$env", environment));
            Execute("INSERT INTO pattern_node(root_id, parent_node_id, ordinal, kind, natural_class_guid) " +
                    "VALUES (last_insert_rowid(), NULL, 0, 'naturalClass', $class);",
                ("$class", classGuid));
            Execute("INSERT INTO allomorph_environment(allomorph_guid, role, ordinal, environment_guid) " +
                    "VALUES ($allomorph, 'phone', 0, $env);",
                ("$allomorph", allomorph), ("$env", environment));
            Execute("INSERT INTO environment_usage(allomorph_guid, role, ordinal, compile_context_key, " +
                    "environment_guid, resolved_environment_guid, compiled, result) " +
                    "VALUES ($allomorph, 'phone', 0, 'production', $env, $env, 1, 'represented');",
                ("$allomorph", allomorph), ("$env", environment));
            Execute(CompiledOutputSql.EnsureOwner + CompiledOutputSql.EnsureAllomorphOutput + CompiledOutputSql.OrderRow,
                ("$owner", "natural-class-second-owner"), ("$bucket", "Morphology"),
                ("$output", "natural-class-second-output"), ("$order", 0), ("$phone", 1), ("$gate", 0),
                ("$entry", targetEntry), ("$msa", targetMsa), ("$sourceKey", allomorph), ("$allomorph", allomorph));
            transaction.Commit();
        }

        using var evidence = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(evidencePath), Pooling = false, ForeignKeys = true,
        }.ToString());
        evidence.Open();
        using var evidenceTransaction = evidence.BeginTransaction();
        void AddEvidence(string sql, params (string Name, object Value)[] values)
        {
            using var command = evidence.CreateCommand();
            command.Transaction = evidenceTransaction;
            command.CommandText = sql;
            foreach (var (name, value) in values) command.Parameters.AddWithValue(name, value);
            command.ExecuteNonQuery();
        }

        const string wordform = "97000000-0000-0000-0000-000000000004";
        const string analysis = "97000000-0000-0000-0000-000000000005";
        AddEvidence("INSERT INTO wordforms(wordform_guid, spelling_status) VALUES ($guid, 0);",
            ("$guid", wordform));
        AddEvidence("INSERT INTO wordform_forms(wordform_guid, writing_system, form, form_nfd) " +
                    "VALUES ($guid, 'qaa', 'bx', 'bx');", ("$guid", wordform));
        AddEvidence("INSERT INTO analyses(analysis_guid, wordform_guid, opinion, source_kind, content_sha256) " +
                    "VALUES ($analysis, $wordform, 'approved', 'fixture', $digest);",
            ("$analysis", analysis), ("$wordform", wordform), ("$digest", new string('0', 64)));
        AddEvidence("INSERT INTO analysis_morphs(analysis_guid, ordinal, morph_guid, msa_guid, entry_guid) " +
                    "VALUES ($analysis, 0, $morph, $msa, $entry);",
            ("$analysis", analysis), ("$morph", "97000000-0000-0000-0000-000000000003"),
            ("$msa", "97000000-0000-0000-0000-000000000002"),
            ("$entry", "97000000-0000-0000-0000-000000000001"));
        AddEvidence("INSERT INTO analysis_morph_forms(analysis_guid, morph_ordinal, writing_system, form, form_nfd) " +
                    "VALUES ($analysis, 0, 'qaa', 'b', 'b');", ("$analysis", analysis));
        AddEvidence("INSERT INTO analysis_morphs(analysis_guid, ordinal, morph_guid, msa_guid, entry_guid) " +
                    "VALUES ($analysis, 1, $morph, $msa, $entry);",
            ("$analysis", analysis), ("$morph", "90000000-0000-0000-0000-000000000004"),
            ("$msa", "91000000-0000-0000-0000-000000000002"),
            ("$entry", "91000000-0000-0000-0000-000000000001"));
        AddEvidence("INSERT INTO analysis_morph_forms(analysis_guid, morph_ordinal, writing_system, form, form_nfd) " +
                    "VALUES ($analysis, 1, 'qaa', 'x', 'x');", ("$analysis", analysis));
        AddEvidence("UPDATE artifact_metadata SET wordform_count=(SELECT COUNT(*) FROM wordforms), " +
                    "analysis_count=(SELECT COUNT(*) FROM analyses) WHERE singleton=1;");
        evidenceTransaction.Commit();
    }

    private static void AddUnrelatedAmbiguousContext(string factsPath, string evidencePath)
    {
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
               {
                   DataSource = Path.GetFullPath(factsPath), Pooling = false, ForeignKeys = true,
               }.ToString()))
        {
            connection.Open();
            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "INSERT INTO writing_system(tag) VALUES ('qaz'); " +
                "INSERT INTO phoneme(guid, feature_structure_id, name) " +
                "VALUES ('91000000-0000-0000-0000-000000000016', NULL, 'pp'); " +
                "INSERT INTO phoneme_grapheme(phoneme_guid, ordinal, writing_system, grapheme) " +
                "VALUES ('91000000-0000-0000-0000-000000000011', 1, 'qaz', 'p'), " +
                "('91000000-0000-0000-0000-000000000016', 0, 'qaz', 'pp');";
            command.ExecuteNonQuery();
            transaction.Commit();
        }
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
               {
                   DataSource = Path.GetFullPath(evidencePath), Pooling = false, ForeignKeys = true,
               }.ToString()))
        {
            connection.Open();
            using var transaction = connection.BeginTransaction();
            void Execute(string sql, params (string Name, object Value)[] values)
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = sql;
                foreach (var (name, value) in values) command.Parameters.AddWithValue(name, value);
                command.ExecuteNonQuery();
            }
            const string wordform = "97000000-0000-0000-0000-000000000001";
            const string analysis = "97000000-0000-0000-0000-000000000002";
            Execute("INSERT INTO wordforms(wordform_guid, spelling_status) VALUES ($guid, 0);",
                ("$guid", wordform));
            Execute("INSERT INTO wordform_forms(wordform_guid, writing_system, form, form_nfd) " +
                    "VALUES ($guid, 'qaz', 'ppp', 'ppp');", ("$guid", wordform));
            Execute("INSERT INTO analyses(analysis_guid, wordform_guid, opinion, source_kind, content_sha256) " +
                    "VALUES ($analysis, $wordform, 'approved', 'fixture', $digest);",
                ("$analysis", analysis), ("$wordform", wordform), ("$digest", new string('1', 64)));
            Execute("INSERT INTO analysis_morphs(analysis_guid, ordinal, morph_guid, msa_guid, entry_guid) " +
                    "VALUES ($analysis, 0, '94000000-0000-0000-0000-000000000001', " +
                    "'93000000-0000-0000-0000-000000000001', '92000000-0000-0000-0000-000000000001');",
                ("$analysis", analysis));
            Execute("INSERT INTO analysis_morph_forms(analysis_guid, morph_ordinal, writing_system, form, form_nfd) " +
                    "VALUES ($analysis, 0, 'qaz', 'ppp', 'ppp');", ("$analysis", analysis));
            Execute("UPDATE artifact_metadata SET wordform_count=(SELECT COUNT(*) FROM wordforms), " +
                    "analysis_count=(SELECT COUNT(*) FROM analyses) WHERE singleton=1;");
            transaction.Commit();
        }
    }

    [Fact]
    public async Task AlternationPairsUnderDifferentGatesDoNotFormOneFamily()
    {
        await PublishBaselineAsync(_pristine);
        var (bundleId, bundle) = await RunScopedMeasureJobAsync("P-allo-alternation-family",
            new ParsimonyScopeBinding(ParsimonyEvidenceScopeKind.ProjectApproved, null), "gate-split-worker");
        AddNamedMeasureFacts(bundle.GrammarFactsPath);
        AddAlternationFamilyGrammarFacts(bundle.GrammarFactsPath);
        UpdateGrammarFacts(bundle.GrammarFactsPath,
            "UPDATE msa_category SET category_guid=$category WHERE msa_guid=$msa;",
            ("$category", AlternationOtherCategory), ("$msa", AlternationMsaC));

        using var session = OpenAlternationSession(bundleId, bundle);
        var families = session.ReadAlternationFamilies().Families;

        Assert.DoesNotContain(families, family =>
            family.Members.Any(member => member.EntryGuid == AlternationEntryB) &&
            family.Members.Any(member => member.EntryGuid == AlternationEntryC));
    }

    [Fact]
    public async Task GateSignatureCarriesReferencedGuids()
    {
        await PublishBaselineAsync(_pristine);
        var (bundleId, bundle) = await RunScopedMeasureJobAsync("P-allo-alternation-family",
            new ParsimonyScopeBinding(ParsimonyEvidenceScopeKind.ProjectApproved, null), "gate-guids-worker");
        AddNamedMeasureFacts(bundle.GrammarFactsPath);
        AddAlternationFamilyGrammarFacts(bundle.GrammarFactsPath);
        UpdateGrammarFacts(bundle.GrammarFactsPath,
            "INSERT INTO msa_slot(msa_guid, role, ordinal, slot_guid) VALUES ($msa, 'slot', 0, $slot);",
            ("$msa", AlternationMsaB), ("$slot", AlternationSlot));
        UpdateGrammarFacts(bundle.GrammarFactsPath,
            "INSERT INTO msa_inflection_class(msa_guid, role, class_guid) VALUES ($msa, 'class', $class);",
            ("$msa", AlternationMsaB), ("$class", AlternationClass));

        using var session = OpenAlternationSession(bundleId, bundle);
        var entry = Assert.Single(session.ReadAlternationInput().Entries,
            item => item.Guid == AlternationEntryB);
        var gate = Assert.Single(entry.GateKeys, key => key.Contains(AlternationSlot, StringComparison.Ordinal));
        foreach (var guid in new[] { AlternationPosCategory, AlternationSlot, AlternationClass })
            Assert.Contains(guid, gate, StringComparison.Ordinal);
    }

    [Fact]
    public async Task IdenticalGatesStillFormOneFamily()
    {
        await PublishBaselineAsync(_pristine);
        var (bundleId, bundle) = await RunScopedMeasureJobAsync("P-allo-alternation-family",
            new ParsimonyScopeBinding(ParsimonyEvidenceScopeKind.ProjectApproved, null), "gate-shared-worker");
        AddNamedMeasureFacts(bundle.GrammarFactsPath);
        AddAlternationFamilyGrammarFacts(bundle.GrammarFactsPath);

        using var session = OpenAlternationSession(bundleId, bundle);
        var family = Assert.Single(session.ReadAlternationFamilies().Families, item =>
            item.Members.Any(member => member.EntryGuid == AlternationEntryB) &&
            item.Members.Any(member => member.EntryGuid == AlternationEntryC));

        Assert.Equal(2, family.Members.Count);
    }

    [Fact]
    public async Task AlternationPairsUnderDifferentStemNamesDoNotFormOneFamily()
    {
        await PublishBaselineAsync(_pristine);
        var (bundleId, bundle) = await RunScopedMeasureJobAsync("P-allo-alternation-family",
            new ParsimonyScopeBinding(ParsimonyEvidenceScopeKind.ProjectApproved, null), "stem-split-worker");
        AddNamedMeasureFacts(bundle.GrammarFactsPath);
        AddAlternationFamilyGrammarFacts(bundle.GrammarFactsPath);
        UpdateGrammarFacts(bundle.GrammarFactsPath,
            "INSERT INTO msa_stem_name(msa_guid, role, stem_name_guid) VALUES ($msa, 'from_stem_name', $stem);",
            ("$msa", AlternationMsaB), ("$stem", AlternationStemName));

        using var session = OpenAlternationSession(bundleId, bundle);
        var families = session.ReadAlternationFamilies().Families;

        Assert.DoesNotContain(families, family =>
            family.Members.Any(member => member.EntryGuid == AlternationEntryB) &&
            family.Members.Any(member => member.EntryGuid == AlternationEntryC));
    }

    [Fact]
    public async Task AllomorphsWithDifferentGateSignaturesDoNotShareAFamily()
    {
        await PublishBaselineAsync(_pristine);
        var (bundleId, bundle) = await RunScopedMeasureJobAsync("P-allo-alternation-family",
            new ParsimonyScopeBinding(ParsimonyEvidenceScopeKind.ProjectApproved, null), "gate-signature-worker");
        AddNamedMeasureFacts(bundle.GrammarFactsPath);
        AddAlternationFamilyGrammarFacts(bundle.GrammarFactsPath);
        UpdateGrammarFacts(bundle.GrammarFactsPath,
            "UPDATE compiled_output SET gate_signature=$signature, has_morph_gate=1 WHERE key=$output;",
            ("$signature", "mpr=1;xmpr=;fs=[];stem="), ("$output", "alternation-out-" + AlternationAllomorphC2));

        using var session = OpenAlternationSession(bundleId, bundle);
        var families = session.ReadAlternationFamilies().Families;

        Assert.DoesNotContain(families, family =>
            family.Members.Any(member => member.EntryGuid == AlternationEntryB) &&
            family.Members.Any(member => member.EntryGuid == AlternationEntryC));
    }

    [Fact]
    public async Task AllomorphsWithDifferentStemNamesDoNotShareAFamily()
    {
        await PublishBaselineAsync(_pristine);
        var (bundleId, bundle) = await RunScopedMeasureJobAsync("P-allo-alternation-family",
            new ParsimonyScopeBinding(ParsimonyEvidenceScopeKind.ProjectApproved, null), "allo-stem-split-worker");
        AddNamedMeasureFacts(bundle.GrammarFactsPath);
        AddAlternationFamilyGrammarFacts(bundle.GrammarFactsPath);
        UpdateGrammarFacts(bundle.GrammarFactsPath,
            "UPDATE compiled_output SET gate_signature=$signature, has_morph_gate=1 WHERE owner_output_id=" +
            "(SELECT output_id FROM compiled_output WHERE key=$owner);",
            ("$signature", "mpr=;xmpr=;fs=[];stem=Dative"), ("$owner", "alternation-owner-" + AlternationEntryB));
        UpdateGrammarFacts(bundle.GrammarFactsPath,
            "UPDATE compiled_output SET gate_signature=$signature, has_morph_gate=1 WHERE owner_output_id=" +
            "(SELECT output_id FROM compiled_output WHERE key=$owner);",
            ("$signature", "mpr=;xmpr=;fs=[];stem=Locative"), ("$owner", "alternation-owner-" + AlternationEntryC));

        using var session = OpenAlternationSession(bundleId, bundle);
        var families = session.ReadAlternationFamilies().Families;

        Assert.DoesNotContain(families, family =>
            family.Members.Any(member => member.EntryGuid == AlternationEntryB) &&
            family.Members.Any(member => member.EntryGuid == AlternationEntryC));
    }

    [Fact]
    public async Task AlternationFamilyNamesItsSharedGate()
    {
        await PublishBaselineAsync(_pristine);
        var (bundleId, bundle) = await RunScopedMeasureJobAsync("P-allo-alternation-family",
            new ParsimonyScopeBinding(ParsimonyEvidenceScopeKind.ProjectApproved, null), "allo-named-gate-worker");
        AddNamedMeasureFacts(bundle.GrammarFactsPath);
        AddAlternationFamilyGrammarFacts(bundle.GrammarFactsPath);
        UpdateGrammarFacts(bundle.GrammarFactsPath,
            "UPDATE compiled_output SET gate_signature=$signature, has_morph_gate=1 WHERE kind='allomorph';",
            ("$signature", "mpr=;xmpr=;fs=[];stem=Dative"));

        using var session = OpenAlternationSession(bundleId, bundle);
        var family = Assert.Single(session.ReadAlternationFamilies().Families, item =>
            item.Members.Any(member => member.EntryGuid == AlternationEntryB) &&
            item.Members.Any(member => member.EntryGuid == AlternationEntryC));

        Assert.Contains("Dative", family.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task IgnoredAllomorphGatesDoNotSplitAFamily()
    {
        await PublishBaselineAsync(_pristine);
        var (bundleId, bundle) = await RunScopedMeasureJobAsync("P-allo-alternation-family",
            new ParsimonyScopeBinding(ParsimonyEvidenceScopeKind.ProjectApproved, null), "allo-ignored-gate-worker");
        AddNamedMeasureFacts(bundle.GrammarFactsPath);
        AddAlternationFamilyGrammarFacts(bundle.GrammarFactsPath);
        UpdateGrammarFacts(bundle.GrammarFactsPath,
            "INSERT INTO allomorph_gate(allomorph_guid, gate_kind, ordinal, target_guid, parser_effect, reason_code) " +
            "VALUES ($allomorph, 'inflection_class', 0, $class, 'ignored', 'derivationalMsaIgnoresAlloClasses');",
            ("$allomorph", AlternationAllomorphC2), ("$class", AlternationClass));

        using var session = OpenAlternationSession(bundleId, bundle);
        var family = Assert.Single(session.ReadAlternationFamilies().Families, item =>
            item.Members.Any(member => member.EntryGuid == AlternationEntryB) &&
            item.Members.Any(member => member.EntryGuid == AlternationEntryC));

        Assert.Equal(2, family.Members.Count);
        Assert.DoesNotContain(AlternationClass, family.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AllomorphsWithDifferentAppliedInflectionClassesDoNotShareAFamily()
    {
        await PublishBaselineAsync(_pristine);
        var (bundleId, bundle) = await RunScopedMeasureJobAsync("P-allo-alternation-family",
            new ParsimonyScopeBinding(ParsimonyEvidenceScopeKind.ProjectApproved, null), "allo-class-split-worker");
        AddNamedMeasureFacts(bundle.GrammarFactsPath);
        AddAlternationFamilyGrammarFacts(bundle.GrammarFactsPath);
        // The compiler carries an applied inflection class as a required MPR, so the signature differs.
        UpdateGrammarFacts(bundle.GrammarFactsPath,
            "UPDATE compiled_output SET gate_signature=$signature, has_morph_gate=1 WHERE key=$output;",
            ("$signature", "mpr=2;xmpr=;fs=[];stem="), ("$output", "alternation-out-" + AlternationAllomorphC2));
        UpdateGrammarFacts(bundle.GrammarFactsPath,
            "INSERT INTO allomorph_gate(allomorph_guid, gate_kind, ordinal, target_guid, parser_effect) " +
            "VALUES ($allomorph, 'inflection_class', 0, $class, 'applied');",
            ("$allomorph", AlternationAllomorphC2), ("$class", AlternationClass));

        using var session = OpenAlternationSession(bundleId, bundle);
        var families = session.ReadAlternationFamilies().Families;

        Assert.DoesNotContain(families, family =>
            family.Members.Any(member => member.EntryGuid == AlternationEntryB) &&
            family.Members.Any(member => member.EntryGuid == AlternationEntryC));
    }

    private const string AlternationStemName = "33000000-0000-0000-0000-000000000001";
    private const string AlternationEntryB = "10000000-0000-0000-0000-000000000002";
    private const string AlternationEntryC = "10000000-0000-0000-0000-000000000003";
    private const string AlternationMsaB = "70000000-0000-0000-0000-000000000002";
    private const string AlternationMsaC = "70000000-0000-0000-0000-000000000003";
    private const string AlternationPosCategory = "30000000-0000-0000-0000-000000000001";
    private const string AlternationOtherCategory = "30000000-0000-0000-0000-000000000002";
    private const string AlternationSlot = "31000000-0000-0000-0000-000000000001";
    private const string AlternationClass = "32000000-0000-0000-0000-000000000001";
    private const string AlternationAllomorphC2 = "80000000-0000-0000-0000-000000000005";

    private ParsimonyQuerySession OpenAlternationSession(string bundleId, ParsimonyBundleRecord bundle)
    {
        var inputs = new ParsimonyReportInputs(bundleId, _token, "baseline", null, bundle.ModelFingerprint,
            new ParsimonyArtifactDigest(bundle.GrammarFactsSchemaVersion, Sha256Of(bundle.GrammarFactsPath)),
            new ParsimonyArtifactDigest(bundle.EvidenceSchemaVersion, bundle.EvidenceSha256), null, null, [],
            ParsimonyEvidenceScopeKind.ProjectApproved);
        return new ParsimonyQuerySession(bundle.GrammarFactsPath, bundle.EvidencePath, inputs);
    }

    private static void UpdateGrammarFacts(string factsPath, string sql, params (string Name, object Value)[] values)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(factsPath),
            Pooling = false,
            ForeignKeys = true,
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in values) command.Parameters.AddWithValue(name, value);
        command.ExecuteNonQuery();
    }

    private static void AddAlternationFamilyGrammarFacts(string factsPath)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(factsPath),
            Pooling = false,
            ForeignKeys = true,
        }.ToString());
        connection.Open();
        using var transaction = connection.BeginTransaction();
        void Execute(string sql, params (string Name, object Value)[] values)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            foreach (var (name, value) in values) command.Parameters.AddWithValue(name, value);
            command.ExecuteNonQuery();
        }

        const string entryA = "10000000-0000-0000-0000-000000000001";
        const string entryB = "10000000-0000-0000-0000-000000000002";
        const string entryC = "10000000-0000-0000-0000-000000000003";
        const string category = "30000000-0000-0000-0000-000000000001";
        const string phonemeN = "40000000-0000-0000-0000-000000000001";
        const string phonemeM = "40000000-0000-0000-0000-000000000002";
        const string phonemeA = "40000000-0000-0000-0000-000000000003";
        const string featureNasal = "50000000-0000-0000-0000-000000000001";
        const string featurePlace = "50000000-0000-0000-0000-000000000002";
        const string valueYes = "60000000-0000-0000-0000-000000000001";
        const string valueNo = "60000000-0000-0000-0000-000000000002";
        const string valueAlveolar = "60000000-0000-0000-0000-000000000003";
        const string valueBilabial = "60000000-0000-0000-0000-000000000004";
        const string valueVowel = "60000000-0000-0000-0000-000000000005";
        const string msaA = "70000000-0000-0000-0000-000000000001";
        const string msaB = "70000000-0000-0000-0000-000000000002";
        const string msaC = "70000000-0000-0000-0000-000000000003";
        const string msaBAlternateGate = "70000000-0000-0000-0000-000000000004";
        const string allomorphB1 = "80000000-0000-0000-0000-000000000001";
        const string allomorphB2 = "80000000-0000-0000-0000-000000000002";
        const string allomorphB3 = "80000000-0000-0000-0000-000000000003";
        const string allomorphC1 = "80000000-0000-0000-0000-000000000004";
        const string allomorphC2 = "80000000-0000-0000-0000-000000000005";

        Execute("UPDATE allomorph SET is_abstract=1 WHERE entry_guid=$entry;", ("$entry", entryA));
        Execute("INSERT INTO project_writing_system(role, ordinal, writing_system_tag) " +
                "VALUES ('vernacular', 0, 'qaa');");
        foreach (var (guid, ordinal) in new[] { (entryB, 1), (entryC, 2) })
            Execute("INSERT INTO lex_entry(guid, source_ordinal, lexeme_morph_type) " +
                    "VALUES ($guid, $ordinal, 'prefix');",
                ("$guid", guid), ("$ordinal", ordinal));
        foreach (var (guid, entryGuid, kind) in new[]
                 {
                     (msaA, entryA, "stem"), (msaB, entryB, "inflectional"),
                     (msaC, entryC, "inflectional"), (msaBAlternateGate, entryB, "derivational"),
                 })
            Execute("INSERT INTO msa(msa_guid, entry_guid, kind) VALUES ($guid, $entry, $kind);",
                ("$guid", guid), ("$entry", entryGuid), ("$kind", kind));
        foreach (var msa in new[] { msaB, msaC })
            Execute("INSERT INTO msa_category(msa_guid, role, ordinal, category_guid) " +
                    "VALUES ($msa, 'pos', 0, $category);",
                ("$msa", msa), ("$category", category));

        Execute("INSERT INTO feature(guid, system, kind, name, abbreviation) " +
                "VALUES ($guid, 'phonological', 'closed', 'Nasality', 'nas');", ("$guid", featureNasal));
        Execute("INSERT INTO feature(guid, system, kind, name, abbreviation) " +
                "VALUES ($guid, 'phonological', 'closed', 'Place', 'pl');", ("$guid", featurePlace));
        foreach (var (guid, feature, ordinal, name) in new[]
                 {
                     (valueYes, featureNasal, 0, "Yes"), (valueNo, featureNasal, 1, "No"),
                     (valueAlveolar, featurePlace, 0, "Alveolar"),
                     (valueBilabial, featurePlace, 1, "Bilabial"), (valueVowel, featurePlace, 2, "Vowel"),
                 })
            Execute("INSERT INTO feature_value(guid, feature_guid, ordinal, name, abbreviation) " +
                    "VALUES ($guid, $feature, $ordinal, $name, $name);",
                ("$guid", guid), ("$feature", feature), ("$ordinal", ordinal), ("$name", name));
        foreach (var (guid, name, fsId, nasal, place) in new[]
                 {
                     (phonemeN, "n", 1, valueYes, valueAlveolar),
                     (phonemeM, "m", 2, valueYes, valueBilabial),
                     (phonemeA, "a", 3, valueNo, valueVowel),
                 })
        {
            Execute("INSERT INTO feature_structure(fs_id, system, owner_kind, owner_guid, role, path) " +
                    "VALUES ($fs, 'phonological', 'phoneme', $guid, 'features', '');",
                ("$fs", fsId), ("$guid", guid));
            Execute("INSERT INTO phoneme(guid, feature_structure_id, name) VALUES ($guid, $fs, $name);",
                ("$guid", guid), ("$fs", fsId), ("$name", name));
            Execute("INSERT INTO phoneme_grapheme(phoneme_guid, ordinal, writing_system, grapheme) " +
                    "VALUES ($guid, 0, 'qaa', $grapheme);",
                ("$guid", guid), ("$grapheme", name));
            Execute("INSERT INTO feature_assignment(fs_id, ordinal, feature_guid, value_kind, value_guid) " +
                    "VALUES ($fs, 0, $feature, 'closed', $value);",
                ("$fs", fsId), ("$feature", featureNasal), ("$value", nasal));
            Execute("INSERT INTO feature_assignment(fs_id, ordinal, feature_guid, value_kind, value_guid) " +
                    "VALUES ($fs, 1, $feature, 'closed', $value);",
                ("$fs", fsId), ("$feature", featurePlace), ("$value", place));
        }

        foreach (var (guid, entryGuid, ordinal, form) in new[]
                 {
                     (allomorphB1, entryB, 0, "na"), (allomorphB2, entryB, 1, "ma"),
                     (allomorphB3, entryB, 2, "ma"), (allomorphC1, entryC, 0, "na"),
                     (allomorphC2, entryC, 1, "ma"),
                 })
        {
            Execute("INSERT INTO allomorph(guid, entry_guid, ordinal, morph_type, form_class, is_abstract) " +
                    "VALUES ($guid, $entry, $ordinal, 'prefix', 'affix', 0);",
                ("$guid", guid), ("$entry", entryGuid), ("$ordinal", ordinal));
            Execute("INSERT INTO allomorph_form(allomorph_guid, ordinal, writing_system, form) " +
                    "VALUES ($guid, 0, 'qaa', $form);",
                ("$guid", guid), ("$form", form));
            var msa = entryGuid == entryB ? msaB : msaC;
            Execute(CompiledOutputSql.EnsureOwner + CompiledOutputSql.EnsureAllomorphOutputWithSignature +
                    CompiledOutputSql.OrderRow,
                ("$owner", "alternation-owner-" + entryGuid), ("$bucket", "Morphology"),
                ("$output", "alternation-out-" + guid), ("$order", ordinal), ("$phone", 0), ("$gate", 0),
                ("$signature", "mpr=;xmpr=;fs=[];stem="), ("$entry", entryGuid), ("$msa", msa),
                ("$sourceKey", guid), ("$allomorph", guid));
        }
        Execute("UPDATE artifact_section SET status='complete', reason_code=NULL WHERE section IN " +
                "('project', 'entries', 'msas', 'allomorphs', 'features', 'phonology', 'environments', 'patterns', " +
                "'compiled_mappings', 'load_accounting');");
        transaction.Commit();
    }

    private static void AddExactAlternationRule(string factsPath)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(factsPath),
            Pooling = false,
            ForeignKeys = true,
        }.ToString());
        connection.Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO stratum(stratum_key, ordinal, name, table_key)
            VALUES ('alternation-family-stratum', 0, 'S', 'table-one');
            INSERT INTO phonological_rule(guid, name, kind, direction, order_index, effective_stratum_key)
            VALUES ('90000000-0000-0000-0000-000000000001', 'alternation-family', 'rewrite', 'leftToRight', 0,
                'alternation-family-stratum');
            INSERT INTO rule_stratum(rule_guid, stratum_key, ordinal)
            VALUES ('90000000-0000-0000-0000-000000000001', 'alternation-family-stratum', 0);
            INSERT INTO rewrite_rhs(rule_guid, ordinal)
            VALUES ('90000000-0000-0000-0000-000000000001', 0);
            INSERT INTO pattern_root(root_id, owner_kind, owner_guid, role, ordinal, source_kind)
            VALUES (201, 'phonologicalRule', '90000000-0000-0000-0000-000000000001', 'rewrite_lhs', 0, 'authored');
            INSERT INTO pattern_root(root_id, owner_kind, owner_guid, role, ordinal, source_kind)
            VALUES (202, 'phonologicalRule', '90000000-0000-0000-0000-000000000001', 'rewrite_sc', 0, 'authored');
            INSERT INTO pattern_node(node_id, root_id, parent_node_id, ordinal, kind, phoneme_guid)
            VALUES (201, 201, NULL, 0, 'phoneme', '40000000-0000-0000-0000-000000000001');
            INSERT INTO pattern_node(node_id, root_id, parent_node_id, ordinal, kind, phoneme_guid)
            VALUES (202, 202, NULL, 0, 'phoneme', '40000000-0000-0000-0000-000000000002');
            UPDATE rewrite_rhs SET change_root_id = 202
            WHERE rule_guid = '90000000-0000-0000-0000-000000000001' AND ordinal = 0;
            INSERT INTO load_fact(subject_kind, subject_key, pipeline_stage, decision_ordinal, subject_guid,
                context_key, disposition, loaded, reason_code)
            VALUES ('phonologicalRule',
                '{"identity":{"guid":"90000000-0000-0000-0000-000000000001","kind":"object"},"kind":"phonologicalRule"}',
                'compile', 0, '90000000-0000-0000-0000-000000000001', '', 'represented', 1, 'represented');
            """;
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    private static void AddBoundaryContextToExactAlternationRule(string factsPath, string boundaryGuid)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(factsPath),
            Pooling = false,
            ForeignKeys = true,
        }.ToString());
        connection.Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO pattern_root(root_id, owner_kind, owner_guid, role, ordinal, source_kind) " +
            "VALUES (203, 'phonologicalRule', '90000000-0000-0000-0000-000000000001', 'rewrite_left_context', 0, 'authored'); " +
            "INSERT INTO pattern_node(node_id, root_id, parent_node_id, ordinal, kind, boundary_guid) " +
            "VALUES (203, 203, NULL, 0, 'boundary', $guid);";
        command.Parameters.AddWithValue("$guid", boundaryGuid);
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    private sealed class FactsRequestCaptureInvoker : IPanGlossInvoker
    {
        public PanGlossRequest.Facts? FactsRequest { get; private set; }

        public Task<PanGlossOutcome> RunAsync(PanGlossRequest request, string label,
            CancellationToken cancellationToken, TimeSpan? wallClockCap = null)
        {
            if (request is PanGlossRequest.Import import)
            {
                File.WriteAllText(import.GrammarJsonPath, "{}");
                return Task.FromResult<PanGlossOutcome>(
                    new PanGlossOutcome.Completed(string.Empty, string.Empty, TimeSpan.Zero));
            }

            if (request is PanGlossRequest.Facts facts)
            {
                FactsRequest = facts;
                return Task.FromResult<PanGlossOutcome>(new PanGlossOutcome.Unavailable("Captured facts request."));
            }

            throw new InvalidOperationException($"Unexpected PanGloss request '{request.Subcommand}'.");
        }
    }

    private static void AddAlternationFamilyWordBoundaryConditionForAlternates(string factsPath, string boundaryGuid)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(factsPath),
            Pooling = false,
            ForeignKeys = true,
        }.ToString());
        connection.Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO environment(guid, name, representation, parse_status)
            VALUES ('90000000-0000-0000-0000-000000000010', 'word edge', '#_', 'valid');
            INSERT INTO allomorph_environment(allomorph_guid, role, ordinal, environment_guid)
            VALUES
                ('80000000-0000-0000-0000-000000000002', 'phone', 0, '90000000-0000-0000-0000-000000000010'),
                ('80000000-0000-0000-0000-000000000003', 'phone', 0, '90000000-0000-0000-0000-000000000010'),
                ('80000000-0000-0000-0000-000000000005', 'phone', 0, '90000000-0000-0000-0000-000000000010');
            INSERT INTO environment_usage(allomorph_guid, role, ordinal, compile_context_key, environment_guid,
                resolved_environment_guid, compiled, result)
            VALUES
                ('80000000-0000-0000-0000-000000000002', 'phone', 0, 'alternation-family', '90000000-0000-0000-0000-000000000010', '90000000-0000-0000-0000-000000000010', 1, 'represented'),
                ('80000000-0000-0000-0000-000000000003', 'phone', 0, 'alternation-family', '90000000-0000-0000-0000-000000000010', '90000000-0000-0000-0000-000000000010', 1, 'represented'),
                ('80000000-0000-0000-0000-000000000005', 'phone', 0, 'alternation-family', '90000000-0000-0000-0000-000000000010', '90000000-0000-0000-0000-000000000010', 1, 'represented');
            INSERT INTO pattern_root(root_id, owner_kind, owner_guid, role, ordinal, source_kind)
            VALUES (301, 'environment', '90000000-0000-0000-0000-000000000010', 'environment_left', 0, 'resolved_environment');
            INSERT INTO pattern_node(node_id, root_id, parent_node_id, ordinal, kind, boundary_guid)
            VALUES (301, 301, NULL, 0, 'boundary', $boundary);
            """;
        command.Parameters.AddWithValue("$boundary", boundaryGuid);
        command.ExecuteNonQuery();
        transaction.Commit();
    }
}
