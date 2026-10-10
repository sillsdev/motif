using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Cryptography;
using System.Text;
using SIL.LCModel;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.Config;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Parsimony;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Worker.Parsimony;

/// <summary>Builds and publishes one frozen Baseline Parsimony Report without opening the live project.</summary>
public sealed class ParsimonyJobHandler
{
    public const string JobKind = "parsimony-report";

    private readonly MotifDatabase _database;
    private readonly BaselineRepository _baselines;
    private readonly string _projectKey;
    private readonly RunnerOptions _options;
    private readonly IPanGlossInvoker _invoker;
    private readonly EvidenceArtifactPublisher _publisher;
    private readonly Action<string> _factsIntegrityCheck;
    private readonly EvidenceArtifactRepository _artifacts;
    private readonly EvidenceRetentionCleaner _retention;

    public ParsimonyJobHandler(MotifDatabase database, BaselineRepository baselines, string projectKey,
        ProjectLocator project, RunnerOptions options, IPanGlossInvoker invoker,
        IEvidencePublicationHooks? publicationHooks = null, Action<string>? factsIntegrityCheck = null)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _baselines = baselines ?? throw new ArgumentNullException(nameof(baselines));
        _projectKey = string.IsNullOrWhiteSpace(projectKey)
            ? throw new ArgumentException("A project key is required.", nameof(projectKey)) : projectKey;
        ArgumentNullException.ThrowIfNull(project);
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _invoker = invoker ?? throw new ArgumentNullException(nameof(invoker));
        _publisher = new EvidenceArtifactPublisher(options.Root, _projectKey, publicationHooks);
        _factsIntegrityCheck = factsIntegrityCheck ?? ParsimonyFactsIntegrity.Verify;
        _artifacts = new EvidenceArtifactRepository(database);
        _retention = new EvidenceRetentionCleaner(_artifacts, WorkspaceOwnership.Bootstrap(options.Root));
    }

    public async Task<JobOutcome?> RunAsync(ClaimedJob claimed, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(claimed);
        var input = JsonSerializer.Deserialize<ParsimonyJobInput>(claimed.Job.InputJson, MotifJson.CreateOptions())
            ?? throw new InvalidDataException("The Parsimony job has no input.");
        if (string.IsNullOrWhiteSpace(input.MeasureId))
            return Failure(JobFailureCategory.Semantic, "The Parsimony job does not name a measure.");
        if (input.ScopeBinding is null || !Enum.IsDefined(input.ScopeBinding.Kind))
            return Failure(JobFailureCategory.Semantic, "The Parsimony job does not name a supported evidence scope.");
        var assessmentIds = input.AssessmentIds ?? [];
        if (assessmentIds.Any(string.IsNullOrWhiteSpace) ||
            assessmentIds.Distinct(StringComparer.Ordinal).Count() != assessmentIds.Count)
            return Failure(JobFailureCategory.Semantic, "The Parsimony job has invalid Assessment references.");
        if (MeasureCatalog.Find(input.MeasureId) is null)
            return Failure(JobFailureCategory.Semantic, $"Unknown Parsimony measure '{input.MeasureId}'.");
        if (!MeasureRunner.Supports(input.MeasureId))
            return Failure(JobFailureCategory.Semantic,
                $"Parsimony measure '{input.MeasureId}' is catalogued but not supported by this build.");

        var current = _baselines.GetCurrentEvidence(_projectKey);
        if (current is null)
            return Failure(JobFailureCategory.Semantic,
                "No Baseline is recorded. Capture a Baseline before requesting a Parsimony report.");
        var baseline = current.Baseline;
        var root = Path.GetFullPath(baseline.RootDirectory);
        if (!Directory.Exists(root) || !File.Exists(baseline.FwDataPath))
            return Failure(JobFailureCategory.Infrastructure,
                "The recorded Baseline bundle is missing; refresh the Baseline before requesting a Parsimony report.");

        var bundleId = "parsimony-bundle-" + Guid.NewGuid().ToString("N");
        var claimToken = claimed.Job.ClaimToken;
        if (string.IsNullOrWhiteSpace(claimToken))
            return Failure(JobFailureCategory.Infrastructure, "The Parsimony job has no active writer claim.");
        var working = _publisher.WorkingDirectory(bundleId);
        _artifacts.RegisterBuildClaim(new ParsimonyBuildClaim(bundleId, _projectKey, claimed.JobId, claimToken,
            root, working));
        string? staging = null;
        string? finalDirectory = null;
        try
        {
            _publisher.CreateWorkingDirectory(bundleId);
            var privateProjectPath = CopyBaseline(root, baseline.FwDataPath, Path.Combine(working, "source"));
            var textWords = _baselines.GetCurrentTextWords(_projectKey,
                current.Summary.Texts.Select(text => text.TextId).ToArray(), cancellationToken);
            if (textWords is null || textWords.Baseline.Token != baseline.Token)
                return Failure(JobFailureCategory.Semantic,
                    "The Baseline changed while the Parsimony evidence was being prepared; retry against the current Baseline.");

            var snapshotPath = Path.Combine(working, "grammar-snapshot.json");
            var parserIdentity = ParserIdentity();
            var imported = await _invoker.RunAsync(new PanGlossRequest.Import(privateProjectPath, snapshotPath),
                "parsimony-import", cancellationToken).ConfigureAwait(false);
            if (imported is not PanGlossOutcome.Completed)
                return Failure(CategoryOf(imported), imported.Message);

            var tokenJson = TokenJson(baseline.Token);
            var contextJson = JsonSerializer.Serialize(new
            {
                format = "pangloss-facts-context",
                version = 1,
                baselineToken = baseline.Token,
                inputKind = "baseline",
                dryRunDigest = (string?)null,
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            var factsOutcome = await _invoker.RunAsync(
                new PanGlossRequest.Facts(snapshotPath, contextJson),
                "parsimony-facts", cancellationToken).ConfigureAwait(false);
            if (factsOutcome is not PanGlossOutcome.Completed completed || completed.Facts is null)
                return Failure(CategoryOf(factsOutcome), factsOutcome.Message);
            var facts = completed.Facts;
            using var factsLease = completed.FactsArtifactLease;
            // Evidence must name one parser, pinned by `ReplacingTheParserBetweenImportAndFactsRefuses`.
            if (ParserIdentity() != parserIdentity)
                return Failure(JobFailureCategory.Infrastructure,
                    "The parser executable changed during the invocation.");

            var factsPath = Path.Combine(working, "grammar-facts.sqlite");
            File.Copy(facts.Path, factsPath, overwrite: false);
            using var cache = new FwDataProjectLoader().LoadScratchCache(privateProjectPath);
            var projection = EvidenceProjectionBuilder.Build(cache, textWords.Projection, input.ScopeBinding,
                cancellationToken);
            var parserDigest = parserIdentity is null ? "unavailable" : RemoveDigestPrefix(parserIdentity);
            ParserOverlayProjection parserOverlay;
            IReadOnlyList<ReportableAssessment> reportableAssessments;
            try
            {
                var sourceDigest = BatchInvocationEvidence.DigestFile(privateProjectPath);
                var assessmentSources = ParserAssessmentMaterial.Read(_database, assessmentIds, baseline.Token,
                    sourceDigest, parserDigest, out reportableAssessments);
                parserOverlay = AssessmentEvidenceProjector.Build(assessmentSources, projection.Wordforms,
                    sourceDigest, parserDigest, reviewedNegatives: projection.ReviewedNegatives);
            }
            catch (Exception exception) when (exception is InvalidDataException or IOException or ArgumentException)
            {
                return Failure(JobFailureCategory.Semantic, exception.Message);
            }
            projection = WithParserOverlay(projection, parserOverlay);
            var workingEvidencePath = Path.Combine(working, "evidence.sqlite");
            var evidenceDigest = EvidenceWriter.Write(workingEvidencePath, projection, tokenJson,
                baseline.Token.BundleDigest, facts.ModelFingerprint);

            var materialKey = Hash(JsonSerializer.Serialize(new
            {
                baselineToken = baseline.Token,
                inputKind = "baseline",
                modelFingerprint = facts.ModelFingerprint,
                parserDigest,
                factsSchemaVersion = facts.SchemaVersion,
                evidenceSchemaVersion = evidenceDigest.SchemaVersion,
                scope = input.ScopeBinding,
                assessmentIds,
            }));
            staging = _publisher.StagingDirectory(bundleId, baseline.Token, facts.ModelFingerprint, materialKey);
            finalDirectory = _publisher.FinalDirectory(bundleId, baseline.Token, facts.ModelFingerprint, materialKey);
            _artifacts.SetBuildClaimPaths(bundleId, staging, finalDirectory);
            _publisher.CreateStagingDirectory(bundleId, baseline.Token, facts.ModelFingerprint, materialKey);
            File.Copy(factsPath, Path.Combine(staging, "grammar-facts.sqlite"), overwrite: false);
            File.Copy(workingEvidencePath, Path.Combine(staging, "evidence.sqlite"), overwrite: false);
            _publisher.FlushAndClose(staging);
            var publishedEvidenceDigest = Hash(File.ReadAllBytes(Path.Combine(staging, "evidence.sqlite")));
            var publishedFactsDigest = Hash(File.ReadAllBytes(Path.Combine(staging, "grammar-facts.sqlite")));
            if (!StringComparer.Ordinal.Equals(publishedEvidenceDigest, evidenceDigest.Sha256))
                throw new InvalidDataException("The evidence artifact changed while it was staged for publication.");
            if (!StringComparer.Ordinal.Equals(publishedFactsDigest, RemoveDigestPrefix(facts.OutputSha256)))
                throw new InvalidDataException("The grammar-facts artifact changed while it was staged for publication.");
            var stagedEvidencePath = Path.Combine(staging, "evidence.sqlite");
            EvidenceWriter.Validate(stagedEvidencePath, tokenJson, baseline.Token.BundleDigest, facts.ModelFingerprint);

            var inputs = new ParsimonyReportInputs(bundleId, baseline.Token, "baseline", null,
                facts.ModelFingerprint, new ParsimonyArtifactDigest(facts.SchemaVersion,
                    publishedFactsDigest), new ParsimonyArtifactDigest(evidenceDigest.SchemaVersion,
                    publishedEvidenceDigest),
                input.ScopeBinding.Selection is null ? null : RemoveDigestPrefix(input.ScopeBinding.Selection.SelectionSha256),
                null, assessmentIds, input.ScopeBinding.Kind);
            var stagedFactsPath = Path.Combine(staging, "grammar-facts.sqlite");
            _factsIntegrityCheck(stagedFactsPath);
            using (var verified = new ParsimonyQuerySession(Path.Combine(staging, "grammar-facts.sqlite"),
                       stagedEvidencePath, inputs))
                _ = verified.ReadJoinQuality(input.ScopeBinding.Kind);
            var reportInput = ReportInput.FromParsimonyFiles(inputs, Path.Combine(staging, "grammar-facts.sqlite"),
                stagedEvidencePath, reportableAssessments, input.MeasureId);
            var rendered = new ParsimonyReportProducer().Produce(reportInput, new ReportQuery(), AssessorCatalog.Empty);
            var reportId = SIL.Motif.Contract.Ids.CanonicalId.Mint("report/").Value;
            var dispositionProjection = rendered.ParsimonyDispositionProjection;
            if (dispositionProjection is not null)
                dispositionProjection = dispositionProjection with
                {
                    ReportId = reportId,
                    Findings = Array.AsReadOnly(dispositionProjection.Findings
                        .Select(item => item with { ReportId = reportId }).ToArray()),
                };
            var response = new ParsimonyReportResponse(reportId, inputs,
                rendered.ParsimonyFindings ?? [], rendered.Text)
            {
                AssessmentIds = assessmentIds,
                MeasureRuns = rendered.ParsimonyMeasureRuns ?? [],
                JoinQuality = rendered.ParsimonyJoinQuality,
                DispositionProjection = dispositionProjection,
                Notes = rendered.ParsimonyNotes ?? [],
            };
            var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
            var reportJson = JsonSerializer.Serialize(response, options);
            var evidenceJson = JsonSerializer.Serialize(new
            {
                inputs,
                grammarFactsPath = "grammar-facts.sqlite",
                evidencePath = "evidence.sqlite",
                findings = response.Findings.Select(item => new { item.FindingId, item.EvidenceDigest }),
                joinQuality = response.JoinQuality,
            }, options);
            finalDirectory = _publisher.Publish(staging, bundleId);
            staging = null;
            var finalFacts = Path.Combine(finalDirectory, "grammar-facts.sqlite");
            var finalEvidence = Path.Combine(finalDirectory, "evidence.sqlite");
            _artifacts.Publish(
                new ParsimonyBundleRecord(bundleId, tokenJson, finalDirectory, finalFacts, finalEvidence,
                    facts.ModelFingerprint, facts.SchemaVersion, publishedFactsDigest,
                    evidenceDigest.SchemaVersion, publishedEvidenceDigest,
                    JobTimestamp.FormatUtc(DateTimeOffset.UtcNow), _projectKey,
                    RemoveDigestPrefix(baseline.Token.BundleDigest), materialKey, "available", null, root),
                new ReportRecord(reportId, null, null, reportJson, evidenceJson, "parsimony", rendered.Text),
                cancellationToken, () => _publisher.BeforeStoreCommit(finalDirectory),
                () => _publisher.AfterStoreCommit(finalDirectory));
            _ = _retention.Clean(_projectKey);
            return new JobOutcome(JobStatus.Completed, ResultJson: JsonSerializer.Serialize(new
            {
                reportId,
                bundleId,
                assessmentIds,
            }, options));
        }
        finally
        {
            var registered = _artifacts.Get(bundleId) is not null;
            if (!registered)
            {
                _publisher.Discard(staging);
                _publisher.Discard(finalDirectory);
            }
            _publisher.Discard(working);
            if (!registered) _artifacts.RemoveBuildClaim(bundleId);
        }
    }

    private string? ParserIdentity() =>
        _options.ParserPath is { } parserPath && File.Exists(parserPath)
            ? BatchInvocationEvidence.DigestFile(parserPath) : null;

    private static string CopyBaseline(string baselineRoot, string fwDataPath, string destination)
    {
        var source = Path.GetFullPath(fwDataPath);
        var relative = Path.GetRelativePath(baselineRoot, source);
        if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar) ||
            !File.Exists(source))
            throw new InvalidDataException("The recorded Baseline project path is outside its bundle or missing.");
        CopyDirectory(baselineRoot, destination);
        return Path.Combine(destination, relative);
    }

    private static void CopyDirectory(string source, string destination)
    {
        if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("The Baseline bundle contains a reparse point.");
        Directory.CreateDirectory(destination);
        foreach (var entry in Directory.EnumerateFileSystemEntries(source))
        {
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("The Baseline bundle contains a reparse point.");
            var target = Path.Combine(destination, Path.GetFileName(entry));
            if ((attributes & FileAttributes.Directory) != 0)
                CopyDirectory(entry, target);
            else
                File.Copy(entry, target, overwrite: false);
        }
    }

    private static string TokenJson(BaselineToken token) => JsonSerializer.Serialize(token,
        new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private static ParsimonyEvidenceProjection WithParserOverlay(ParsimonyEvidenceProjection projection,
        ParserOverlayProjection overlay)
    {
        var status = !overlay.Requested ? "not_requested"
            : overlay.Runs.Any(item => item.Status == "complete") ? "complete" : "unavailable";
        var reason = status == "not_requested" ? "No Assessment overlay was requested."
            : status == "complete" ? null : "The requested parser sidecars were unavailable or invalid.";
        var capabilities = projection.Capabilities.Select(item => item.Capability == "parser-overlay"
            ? item with { Status = status, Reason = reason }
            : item).ToArray();
        return projection with { ParserOverlay = overlay, Capabilities = capabilities };
    }

    private static string RemoveDigestPrefix(string digest) => digest.StartsWith("sha256:", StringComparison.Ordinal)
        ? digest[7..] : digest;

    private static string Hash(string value) => Hash(Encoding.UTF8.GetBytes(value));

    private static string Hash(byte[] value) =>
        Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();

    private static JobFailureCategory CategoryOf(PanGlossOutcome outcome) => outcome switch
    {
        PanGlossOutcome.Refused => JobFailureCategory.ParserRefusal,
        PanGlossOutcome.Cancelled => JobFailureCategory.Cancellation,
        _ => JobFailureCategory.Infrastructure,
    };

    private static JobOutcome Failure(JobFailureCategory category, string detail) =>
        new(JobStatus.Failed, category, JsonSerializer.Serialize(new { detail }));
}

public sealed record ParsimonyJobInput(
    [property: JsonPropertyName("measureId")] string MeasureId,
    [property: JsonPropertyName("scopeBinding")] ParsimonyScopeBinding? ScopeBinding,
    [property: JsonPropertyName("assessmentIds")] IReadOnlyList<string>? AssessmentIds = null);
