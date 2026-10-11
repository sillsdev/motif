using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Retirement;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.Config;
using SIL.Motif.Host.Parsimony;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Worker.Parsimony;

/// <summary>The checks a build runs on what it stages; tests replace them to count how often each one runs.</summary>
/// <param name="FactsIntegrity">The whole-file SQLite check of the staged grammar facts, run once per bundle.</param>
/// <param name="OpenSession">Opens the one verifying session a job renders from.</param>
internal sealed record EvidenceBundleChecks(Action<string> FactsIntegrity,
    Func<string, string, ParsimonyReportInputs, ParsimonyQuerySession> OpenSession)
{
    /// <summary>The production checks, with an optional replacement for the facts integrity check.</summary>
    public static EvidenceBundleChecks Create(Action<string>? factsIntegrity = null) =>
        new(factsIntegrity ?? ParsimonyFactsIntegrity.Verify,
            (facts, evidence, inputs) => new ParsimonyQuerySession(facts, evidence, inputs));
}

/// <summary>Everything one evidence bundle needs once its facts and evidence files exist on disk.</summary>
/// <param name="BundleId">The bundle being built.</param>
/// <param name="Token">The Baseline the evidence is bound to.</param>
/// <param name="InputKind">Either <c>baseline</c> or <c>candidate</c>.</param>
/// <param name="CandidateIdentity">The candidate identity, null for a Baseline input.</param>
/// <param name="MaterialKey">The identity-inputs key stored with the bundle.</param>
/// <param name="FactsPath">The grammar-facts file to stage.</param>
/// <param name="FactsSha256">The digest PanGloss reported for the facts file, without a prefix.</param>
/// <param name="FactsSchemaVersion">The facts schema generation.</param>
/// <param name="ModelFingerprint">The grammar model fingerprint the facts carry.</param>
/// <param name="EvidencePath">The evidence file to stage.</param>
/// <param name="Evidence">The schema generation and digest the evidence writer reported.</param>
/// <param name="Scope">The evidence scope the Report reads.</param>
/// <param name="MeasureId">The measure the Report evaluates.</param>
/// <param name="AssessmentIds">The Assessment references frozen into the bundle.</param>
/// <param name="Assessments">The loaded Assessments, in the order of <paramref name="AssessmentIds"/>.</param>
/// <param name="RetirementTranslation">The retirement expectation translation a candidate Report carries.</param>
/// <param name="BaselineRootDirectory">The Baseline bundle directory the evidence came from.</param>
/// <param name="IncludeDispositionProjection">Whether the stored Report carries the disposition projection.</param>
internal sealed record EvidenceBundleBuild(string BundleId, BaselineToken Token, string InputKind,
    string? CandidateIdentity, string MaterialKey, string FactsPath, string FactsSha256, int FactsSchemaVersion,
    string ModelFingerprint, string EvidencePath, ParsimonyArtifactDigest Evidence, ParsimonyScopeBinding Scope,
    string MeasureId, IReadOnlyList<string> AssessmentIds, IReadOnlyList<ReportableAssessment> Assessments,
    RetirementExpectationTranslation? RetirementTranslation, string BaselineRootDirectory,
    bool IncludeDispositionProjection);

/// <summary>A staged, verified bundle and its rendered Report, ready to be moved and recorded.</summary>
internal sealed record PreparedEvidenceBundle(ParsimonyBundleRecord Bundle, ReportRecord Report,
    ParsimonyReportResponse Response, RenderedReport Rendered, string StagingDirectory, string FinalDirectory);

/// <summary>A Report written onto an existing Baseline bundle instead of a new one.</summary>
internal sealed record ReusedEvidenceBundle(string ReportId, string BundleId);

/// <summary>
/// The one sequence that turns written facts and evidence files into a publishable bundle: stage, compare digests,
/// check the facts once, open one verifying session, render. A Baseline job and a Dry Run comparison differ only
/// in the evidence they feed in, so the staging, digest and reuse rules live here and nowhere else.
/// </summary>
internal sealed class EvidenceBundlePipeline
{
    /// <summary>Marks the key of a bundle built by a Baseline job.</summary>
    public const string BaselineJobOrigin = "baseline-job";

    /// <summary>Marks the key of a bundle built beside a candidate by a Dry Run comparison.</summary>
    public const string ComparisonOrigin = "dry-run-comparison";

    private readonly EvidenceArtifactPublisher _publisher;
    private readonly EvidenceArtifactRepository _artifacts;
    private readonly RunnerOptions _options;
    private readonly string _projectKey;
    private readonly EvidenceBundleChecks _checks;
    private readonly FileDigestCache _digests;

    public EvidenceBundlePipeline(EvidenceArtifactPublisher publisher, EvidenceArtifactRepository artifacts,
        RunnerOptions options, string projectKey, EvidenceBundleChecks checks, FileDigestCache digests)
    {
        _publisher = publisher;
        _artifacts = artifacts;
        _options = options;
        _projectKey = projectKey;
        _checks = checks;
        _digests = digests;
    }

    /// <summary>The cached digest of the project file, so an unchanged frozen project is hashed once.</summary>
    public string ProjectDigest(string path) => _digests.Of(path);

    /// <summary>The digest of a file's bytes as they are now, never remembered.</summary>
    public string FreshDigest(string path) => _digests.Fresh(path);

    /// <summary>The digest of the configured parser executable, or null when it is absent.</summary>
    /// <param name="fresh">True to read the executable through again, as the after-the-run check requires.</param>
    public string? ParserIdentity(bool fresh = false) =>
        _options.ParserPath is { } parserPath && File.Exists(parserPath)
            ? fresh ? _digests.Fresh(parserPath) : _digests.Of(parserPath) : null;

    /// <summary>Strips the <c>sha256:</c> prefix a digest may carry.</summary>
    public static string RemoveDigestPrefix(string digest) =>
        digest.StartsWith("sha256:", StringComparison.Ordinal) ? digest[7..] : digest;

    /// <summary>The key a bundle is stored under, covering every input that changes its content.</summary>
    public static string MaterialKey(string origin, string inputKind, BaselineToken token, string modelFingerprint,
        string parserDigest, int factsSchemaVersion, int evidenceSchemaVersion, ParsimonyScopeBinding scope,
        IReadOnlyList<string> assessmentIds, string? candidateIdentity) => Hash(JsonSerializer.Serialize(new
    {
        origin,
        baselineToken = token,
        inputKind,
        candidateIdentity,
        modelFingerprint,
        parserDigest,
        factsSchemaVersion,
        evidenceSchemaVersion,
        scope,
        assessmentIds,
    }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

    /// <summary>The Baseline token as stored beside the evidence.</summary>
    public static string TokenJson(BaselineToken token) => JsonSerializer.Serialize(token,
        new JsonSerializerOptions(JsonSerializerDefaults.Web));

    /// <summary>Records the parser overlay and its capability status on a projection.</summary>
    public static ParsimonyEvidenceProjection WithParserOverlay(ParsimonyEvidenceProjection projection,
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

    /// <summary>Stages, verifies and renders one bundle; the caller moves it into place and records it.</summary>
    /// <param name="build">The files and identity of the bundle.</param>
    /// <param name="located">Told the staging and final directories before any file is copied into them.</param>
    public PreparedEvidenceBundle Prepare(EvidenceBundleBuild build, Action<string, string> located)
    {
        var staging = _publisher.StagingDirectory(build.BundleId, build.Token, build.ModelFingerprint,
            build.MaterialKey, build.InputKind, build.CandidateIdentity);
        var final = _publisher.FinalDirectory(build.BundleId, build.Token, build.ModelFingerprint,
            build.MaterialKey, build.InputKind, build.CandidateIdentity);
        _artifacts.SetBuildClaimPaths(build.BundleId, staging, final);
        located(staging, final);
        _publisher.CreateStagingDirectory(build.BundleId, build.Token, build.ModelFingerprint, build.MaterialKey,
            build.InputKind, build.CandidateIdentity);
        var stagedFacts = Path.Combine(staging, "grammar-facts.sqlite");
        var stagedEvidence = Path.Combine(staging, "evidence.sqlite");
        File.Copy(build.FactsPath, stagedFacts, overwrite: false);
        File.Copy(build.EvidencePath, stagedEvidence, overwrite: false);
        _publisher.FlushAndClose(staging);
        // The writer validated this evidence file and the digest proves the copy is the same bytes.
        var evidenceSha = RemoveDigestPrefix(_digests.Fresh(stagedEvidence));
        var factsSha = RemoveDigestPrefix(_digests.Fresh(stagedFacts));
        if (!StringComparer.Ordinal.Equals(evidenceSha, build.Evidence.Sha256))
            throw new InvalidDataException("The evidence artifact changed while it was staged for publication.");
        if (!StringComparer.Ordinal.Equals(factsSha, build.FactsSha256))
            throw new InvalidDataException("The grammar-facts artifact changed while it was staged for publication.");
        var inputs = new ParsimonyReportInputs(build.BundleId, build.Token, build.InputKind, build.CandidateIdentity,
            build.ModelFingerprint, new ParsimonyArtifactDigest(build.FactsSchemaVersion, factsSha),
            new ParsimonyArtifactDigest(build.Evidence.SchemaVersion, evidenceSha),
            build.Scope.Selection is null ? null : RemoveDigestPrefix(build.Scope.Selection.SelectionSha256), null,
            build.AssessmentIds, build.Scope.Kind, build.RetirementTranslation);
        _checks.FactsIntegrity(stagedFacts);
        var (reportId, response, rendered, reportJson, evidenceJson) = Render(inputs, stagedFacts, stagedEvidence,
            build.Assessments, build.MeasureId, build.AssessmentIds, build.IncludeDispositionProjection);
        var bundle = new ParsimonyBundleRecord(build.BundleId, TokenJson(build.Token), final,
            Path.Combine(final, "grammar-facts.sqlite"), Path.Combine(final, "evidence.sqlite"),
            build.ModelFingerprint, build.FactsSchemaVersion, factsSha, build.Evidence.SchemaVersion, evidenceSha,
            JobTimestamp.FormatUtc(DateTimeOffset.UtcNow), _projectKey, RemoveDigestPrefix(build.Token.BundleDigest),
            build.MaterialKey, "available", null, build.BaselineRootDirectory, build.InputKind,
            build.CandidateIdentity);
        var report = new ReportRecord(reportId, null, null, reportJson, evidenceJson, "parsimony", rendered.Text);
        return new PreparedEvidenceBundle(bundle, report, response, rendered, staging, final);
    }

    /// <summary>
    /// Writes a new Report onto an available Baseline bundle whose identity inputs are unchanged, or returns null
    /// so the caller builds fresh. Only Baseline bundles are reused: a candidate bundle's identity needs the
    /// facts model fingerprint, which exists only after PanGloss has run, and its key does not cover the
    /// Assessment contents, so the same key would not prove the same evidence (ADR 0060).
    /// </summary>
    public ReusedEvidenceBundle? TryReuseBaseline(MotifDatabase database, ClaimedJob claimed, string claimToken,
        CurrentBaselineEvidence current, ParsimonyScopeBinding scope, string measureId,
        IReadOnlyList<string> assessmentIds, CancellationToken cancellationToken)
    {
        var parserIdentity = ParserIdentity();
        if (parserIdentity is null) return null;
        var parserDigest = RemoveDigestPrefix(parserIdentity);
        var baseline = current.Baseline;
        foreach (var bundle in _artifacts.ListAvailableBaselineBundles(_projectKey,
                     RemoveDigestPrefix(baseline.Token.BundleDigest)))
        {
            if (bundle.GrammarFactsSchemaVersion != SIL.Motif.Host.PanGloss.PanGlossInterfaceVersions.FactsSchemaVersion ||
                bundle.EvidenceSchemaVersion != EvidenceSchema.Version ||
                !StringComparer.Ordinal.Equals(bundle.MaterialKey, MaterialKey(BaselineJobOrigin, "baseline",
                    baseline.Token, bundle.ModelFingerprint, parserDigest, bundle.GrammarFactsSchemaVersion,
                    bundle.EvidenceSchemaVersion, scope, assessmentIds, null)) ||
                !File.Exists(bundle.GrammarFactsPath) || !File.Exists(bundle.EvidencePath))
                continue;
            try
            {
                using var lease = _artifacts.AcquireReaderLease(bundle.BundleId, "job-" + claimed.JobId);
                var sourceDigest = ProjectDigest(baseline.FwDataPath);
                _ = ParserAssessmentMaterial.Read(database, assessmentIds, baseline.Token, sourceDigest,
                    parserDigest, out var reportableAssessments);
                var inputs = new ParsimonyReportInputs(bundle.BundleId, baseline.Token, "baseline", null,
                    bundle.ModelFingerprint, new ParsimonyArtifactDigest(bundle.GrammarFactsSchemaVersion,
                        bundle.GrammarFactsSha256), new ParsimonyArtifactDigest(bundle.EvidenceSchemaVersion,
                        bundle.EvidenceSha256),
                    scope.Selection is null ? null : RemoveDigestPrefix(scope.Selection.SelectionSha256),
                    null, assessmentIds, scope.Kind);
                _checks.FactsIntegrity(bundle.GrammarFactsPath);
                var (reportId, _, rendered, reportJson, evidenceJson) = Render(inputs, bundle.GrammarFactsPath,
                    bundle.EvidencePath, reportableAssessments, measureId, assessmentIds, true);
                _artifacts.PublishReportOnExistingBundle(bundle.BundleId,
                    new ReportRecord(reportId, null, null, reportJson, evidenceJson, "parsimony", rendered.Text),
                    claimed.JobId, claimToken, cancellationToken);
                return new ReusedEvidenceBundle(reportId, bundle.BundleId);
            }
            catch (Exception exception) when (exception is InvalidDataException or IOException or
                Microsoft.Data.Sqlite.SqliteException or KeyNotFoundException)
            {
                // A damaged or vanished bundle is not reused; the fresh build replaces it.
            }
        }
        return null;
    }

    private (string ReportId, ParsimonyReportResponse Response, RenderedReport Rendered, string ReportJson,
        string EvidenceJson) Render(ParsimonyReportInputs inputs, string factsPath, string evidencePath,
        IReadOnlyList<ReportableAssessment> assessments, string measureId, IReadOnlyList<string> assessmentIds,
        bool includeDispositionProjection)
    {
        var reportInput = ReportInput.FromParsimonyFiles(inputs, factsPath, evidencePath, assessments, measureId);
        // One session verifies the files and serves the join-quality read and every measure of the render.
        RenderedReport rendered;
        using (var session = _checks.OpenSession(factsPath, evidencePath, inputs))
            rendered = new ParsimonyReportProducer().Produce(reportInput, session);
        var reportId = CanonicalId.Mint("report/").Value;
        var dispositionProjection = includeDispositionProjection ? rendered.ParsimonyDispositionProjection : null;
        if (dispositionProjection is not null)
            dispositionProjection = dispositionProjection with
            {
                ReportId = reportId,
                Findings = Array.AsReadOnly(dispositionProjection.Findings
                    .Select(item => item with { ReportId = reportId }).ToArray()),
            };
        var response = new ParsimonyReportResponse(reportId, inputs, rendered.ParsimonyFindings ?? [], rendered.Text)
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
        return (reportId, response, rendered, reportJson, evidenceJson);
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
