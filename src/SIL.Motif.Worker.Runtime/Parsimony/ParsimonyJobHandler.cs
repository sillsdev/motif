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
    private readonly EvidenceBundlePipeline _pipeline;
    private readonly EvidenceArtifactRepository _artifacts;
    private readonly EvidenceRetentionCleaner _retention;

    public ParsimonyJobHandler(MotifDatabase database, BaselineRepository baselines, string projectKey,
        ProjectLocator project, RunnerOptions options, IPanGlossInvoker invoker,
        IEvidencePublicationHooks? publicationHooks = null, Action<string>? factsIntegrityCheck = null)
        : this(database, baselines, projectKey, project, options, invoker, publicationHooks,
            EvidenceBundleChecks.Create(factsIntegrityCheck), FileDigestCache.Shared)
    {
    }

    internal ParsimonyJobHandler(MotifDatabase database, BaselineRepository baselines, string projectKey,
        ProjectLocator project, RunnerOptions options, IPanGlossInvoker invoker,
        IEvidencePublicationHooks? publicationHooks, EvidenceBundleChecks checks, FileDigestCache digests)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _baselines = baselines ?? throw new ArgumentNullException(nameof(baselines));
        _projectKey = string.IsNullOrWhiteSpace(projectKey)
            ? throw new ArgumentException("A project key is required.", nameof(projectKey)) : projectKey;
        ArgumentNullException.ThrowIfNull(project);
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _invoker = invoker ?? throw new ArgumentNullException(nameof(invoker));
        _publisher = new EvidenceArtifactPublisher(options.Root, _projectKey, publicationHooks);
        _artifacts = new EvidenceArtifactRepository(database);
        _pipeline = new EvidenceBundlePipeline(_publisher, _artifacts, options, _projectKey, checks, digests);
        _retention = new EvidenceRetentionCleaner(_artifacts, WorkspaceOwnership.Bootstrap(options.Root));
    }

    public async Task<JobOutcome?> RunAsync(ClaimedJob claimed, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(claimed);
        var input = JsonSerializer.Deserialize<ParsimonyJobInput>(claimed.Job.InputJson, MotifJson.CreateOptions())
            ?? throw new InvalidDataException("The Parsimony job has no input.");
        var admission = ParsimonyAdmission.Check(input.MeasureId, input.ScopeBinding?.Kind, input.AssessmentIds);
        if (admission is not null) return Failure(JobFailureCategory.Semantic, admission.Message);
        var assessmentIds = input.AssessmentIds ?? [];

        var current = _baselines.GetCurrentEvidence(_projectKey);
        if (current is null)
            return Failure(JobFailureCategory.Semantic,
                "No Baseline is recorded. Capture a Baseline before requesting a Parsimony report.");
        var baseline = current.Baseline;
        var root = Path.GetFullPath(baseline.RootDirectory);
        if (!Directory.Exists(root) || !File.Exists(baseline.FwDataPath))
            return Failure(JobFailureCategory.Infrastructure,
                "The recorded Baseline bundle is missing; refresh the Baseline before requesting a Parsimony report.");

        var claimToken = claimed.Job.ClaimToken;
        if (string.IsNullOrWhiteSpace(claimToken))
            return Failure(JobFailureCategory.Infrastructure, "The Parsimony job has no active writer claim.");
        var reused = _pipeline.TryReuseBaseline(_database, claimed, claimToken, current, input.ScopeBinding,
            input.MeasureId, assessmentIds, cancellationToken);
        if (reused is not null) return Completed(reused.ReportId, reused.BundleId, assessmentIds);
        var bundleId = "parsimony-bundle-" + Guid.NewGuid().ToString("N");
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
            var parserIdentity = _pipeline.ParserIdentity();
            var imported = await _invoker.RunAsync(new PanGlossRequest.Import(privateProjectPath, snapshotPath),
                "parsimony-import", cancellationToken).ConfigureAwait(false);
            if (imported is not PanGlossOutcome.Completed)
                return Failure(CategoryOf(imported), imported.Message);

            var tokenJson = EvidenceBundlePipeline.TokenJson(baseline.Token);
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
            if (_pipeline.ParserIdentity(fresh: true) != parserIdentity)
                return Failure(JobFailureCategory.Infrastructure,
                    "The parser executable changed during the invocation.");

            var factsPath = Path.Combine(working, "grammar-facts.sqlite");
            File.Copy(facts.Path, factsPath, overwrite: false);
            using var cache = new FwDataProjectLoader().LoadScratchCache(privateProjectPath);
            var projection = EvidenceProjectionBuilder.Build(cache, textWords.Projection, input.ScopeBinding,
                cancellationToken);
            var parserDigest = parserIdentity is null ? "unavailable" : EvidenceBundlePipeline.RemoveDigestPrefix(parserIdentity);
            ParserOverlayProjection parserOverlay;
            IReadOnlyList<ReportableAssessment> reportableAssessments;
            try
            {
                var sourceDigest = _pipeline.ProjectDigest(privateProjectPath);
                var assessmentSources = ParserAssessmentMaterial.Read(_database, assessmentIds, baseline.Token,
                    sourceDigest, parserDigest, out reportableAssessments);
                parserOverlay = AssessmentEvidenceProjector.Build(assessmentSources, projection.Wordforms,
                    sourceDigest, parserDigest, reviewedNegatives: projection.ReviewedNegatives);
            }
            catch (Exception exception) when (exception is InvalidDataException or IOException or ArgumentException)
            {
                return Failure(JobFailureCategory.Semantic, exception.Message);
            }
            projection = EvidenceBundlePipeline.WithParserOverlay(projection, parserOverlay);
            var workingEvidencePath = Path.Combine(working, "evidence.sqlite");
            var evidenceDigest = EvidenceWriter.Write(workingEvidencePath, projection, tokenJson,
                baseline.Token.BundleDigest, facts.ModelFingerprint);

            var materialKey = EvidenceBundlePipeline.MaterialKey(EvidenceBundlePipeline.BaselineJobOrigin,
                "baseline", baseline.Token, facts.ModelFingerprint, parserDigest, facts.SchemaVersion,
                evidenceDigest.SchemaVersion, input.ScopeBinding, assessmentIds, null);
            var prepared = _pipeline.Prepare(new EvidenceBundleBuild(bundleId, baseline.Token, "baseline", null,
                materialKey, factsPath, EvidenceBundlePipeline.RemoveDigestPrefix(facts.OutputSha256), facts.SchemaVersion,
                facts.ModelFingerprint, workingEvidencePath, evidenceDigest, input.ScopeBinding, input.MeasureId,
                assessmentIds, reportableAssessments, null, root, IncludeDispositionProjection: true),
                (stagingDirectory, finalPlan) => (staging, finalDirectory) = (stagingDirectory, finalPlan));
            finalDirectory = _publisher.Publish(prepared.StagingDirectory, bundleId);
            staging = null;
            _artifacts.Publish(prepared.Bundle, prepared.Report, cancellationToken,
                () => _publisher.BeforeStoreCommit(finalDirectory),
                () => _publisher.AfterStoreCommit(finalDirectory));
            _ = _retention.Clean(_projectKey);
            return Completed(prepared.Report.ReportId, bundleId, assessmentIds);
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

    private static JobOutcome Completed(string reportId, string bundleId, IReadOnlyList<string> assessmentIds) =>
        new(JobStatus.Completed, ResultJson: JsonSerializer.Serialize(new
        {
            reportId,
            bundleId,
            assessmentIds,
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

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
