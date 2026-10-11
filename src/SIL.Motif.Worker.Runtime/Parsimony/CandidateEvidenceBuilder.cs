using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Canonicalization;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Config;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Parsimony;
using SIL.Motif.Host.Store;
using SIL.Motif.Model.DryRun;
using SIL.Motif.Projection.HumanJudgments;
using SIL.Motif.Runner.AppliedLog;
using SIL.Motif.Runner.DryRun;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Scheduling;
using SIL.Motif.Worker.Store;
using DryRunModel = SIL.Motif.Model.DryRun.DryRun;

namespace SIL.Motif.Worker.Parsimony;

/// <summary>Builds paired Parsimony evidence from the exact source and candidate states of one Dry Run.</summary>
public sealed class ParsimonyCandidateEvidenceBuilder
{
    public const string JobKind = "parsimony-candidate";

    private readonly MotifDatabase _database;
    private readonly string _projectKey;
    private readonly ProjectLocator _project;
    private readonly RunnerOptions _options;
    private readonly IPanGlossInvoker _invoker;
    private readonly ProjectLaneRegistry _lanes;
    private readonly EvidenceArtifactPublisher _publisher;
    private readonly EvidenceBundlePipeline _pipeline;
    private readonly EvidenceArtifactRepository _artifacts;
    private readonly EvidenceRetentionCleaner _retention;
    private readonly FwDataProjectLoader _loader = new();

    public ParsimonyCandidateEvidenceBuilder(MotifDatabase database, string projectKey, ProjectLocator project,
        RunnerOptions options, IPanGlossInvoker invoker, ProjectLaneRegistry lanes,
        Action<string>? factsIntegrityCheck = null)
        : this(database, projectKey, project, options, invoker, lanes,
            EvidenceBundleChecks.Create(factsIntegrityCheck), FileDigestCache.Shared)
    {
    }

    internal ParsimonyCandidateEvidenceBuilder(MotifDatabase database, string projectKey, ProjectLocator project,
        RunnerOptions options, IPanGlossInvoker invoker, ProjectLaneRegistry lanes, EvidenceBundleChecks checks,
        FileDigestCache digests)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _projectKey = string.IsNullOrWhiteSpace(projectKey)
            ? throw new ArgumentException("A project key is required.", nameof(projectKey)) : projectKey;
        _project = project ?? throw new ArgumentNullException(nameof(project));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _invoker = invoker ?? throw new ArgumentNullException(nameof(invoker));
        _lanes = lanes ?? throw new ArgumentNullException(nameof(lanes));
        _publisher = new EvidenceArtifactPublisher(options.Root, _projectKey);
        _artifacts = new EvidenceArtifactRepository(database);
        _pipeline = new EvidenceBundlePipeline(_publisher, _artifacts, options, _projectKey, checks, digests);
        _retention = new EvidenceRetentionCleaner(_artifacts, WorkspaceOwnership.Bootstrap(options.Root));
    }

    /// <summary>Reconstructs a completed Dry Run on a private copy and stores paired evidence and Reports.</summary>
    public async Task<JobOutcome?> RunAsync(ClaimedJob claim, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(claim);
        OperationRegistryBootstrap.Initialize();
        if (claim.Job.Status != JobStatus.Running || claim.Job.Kind != JobKind || claim.Job.ProjectKey != _projectKey ||
            ProjectWorkspaceKey.Compute(_project) != _projectKey)
            throw new InvalidOperationException("The job does not address this project's candidate Parsimony evidence.");

        ParsimonyCandidateJobInput input;
        JobRecord? dryRun;
        DryRunJobInput dryRunInput;
        DryRunSourceBinding source;
        try
        {
            input = ParsimonyCandidateJobInput.Parse(claim.Job.InputJson);
            dryRun = new JobRepository(_database).Get(input.DryRunJobId);
            if (dryRun is null || dryRun.Kind != ProjectJobHandlers.DryRunKind ||
                dryRun.Status != JobStatus.CompletedDryRunOnly || !dryRun.DryRunPublished || dryRun.DryRunJson is null)
                return Failure(JobFailureCategory.Semantic,
                    "Candidate evidence requires the completed Dry Run with its published effects.");
            dryRunInput = DryRunJobInput.Parse(dryRun.InputJson);
            if (dryRun.ResultJson is null)
                throw new InvalidDataException("The completed Dry Run has no exact source result.");
            var completion = DryRunJobCompletion.Parse(dryRun.ResultJson);
            source = completion.SourceBaseline;
            if (dryRunInput.SourceBaseline is not null && dryRunInput.SourceBaseline != source)
                throw new InvalidDataException("The Dry Run input and result name different Baseline sources.");
            source.Validate();
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException or IOException or JsonException)
        {
            return Failure(JobFailureCategory.Semantic,
                "The Dry Run lacks sufficient frozen source or Proposal evidence. Rerun its Dry Run. " + exception.Message);
        }

        var admission = ParsimonyAdmission.Check(input.MeasureId, input.ScopeBinding?.Kind, input.AssessmentIds);
        if (admission is not null) return Failure(JobFailureCategory.Semantic, admission.Message);
        if (_options.ParserPath is null || !File.Exists(_options.ParserPath))
            return Failure(JobFailureCategory.Infrastructure,
                "The configured PanGloss executable is unavailable for grammar facts.");

        var claimToken = claim.Job.ClaimToken;
        if (string.IsNullOrWhiteSpace(claimToken))
            return Failure(JobFailureCategory.Infrastructure, "The candidate Parsimony job has no active writer claim.");

        var beforeBundleId = "parsimony-bundle-" + Guid.NewGuid().ToString("N");
        var afterBundleId = "parsimony-bundle-" + Guid.NewGuid().ToString("N");
        var beforeWorking = _publisher.WorkingDirectory(beforeBundleId);
        var afterWorking = _publisher.WorkingDirectory(afterBundleId);
        _artifacts.RegisterBuildClaim(new ParsimonyBuildClaim(beforeBundleId, _projectKey, claim.JobId,
            claimToken, source.RootDirectory, beforeWorking));
        _artifacts.RegisterBuildClaim(new ParsimonyBuildClaim(afterBundleId, _projectKey, claim.JobId,
            claimToken, source.RootDirectory, afterWorking));
        string? beforeStaging = null;
        string? afterStaging = null;
        string? beforeFinal = null;
        string? afterFinal = null;
        try
        {
            var assessmentIds = input.AssessmentIds ?? [];
            var assessmentSource = ParserAssessmentMaterial.ReadSourceArtifact(_database, assessmentIds);
            var parserBefore = _pipeline.ParserIdentity();
            if (parserBefore is null)
                return Failure(JobFailureCategory.Infrastructure,
                    "The configured PanGloss executable is unavailable for grammar facts.");
            var parserIdentity = EvidenceBundlePipeline.RemoveDigestPrefix(parserBefore);
            _publisher.CreateWorkingDirectory(beforeBundleId);
            _publisher.CreateWorkingDirectory(afterBundleId);
            var prepared = await PrepareProjectStatesAsync(dryRunInput, dryRun.DryRunJson!, source,
                Path.Combine(afterWorking, "project-copies"), input.ScopeBinding!, assessmentSource,
                cancellationToken)
                .ConfigureAwait(false);
            var baselineFacts = await BuildFactsAsync(prepared.BaselineProjectPath, source.Token, false, null,
                Path.Combine(beforeWorking, "facts"), cancellationToken).ConfigureAwait(false);
            var candidateFacts = await BuildFactsAsync(prepared.CandidateProjectPath, source.Token, true,
                prepared.DryRun.EffectDigest, Path.Combine(afterWorking, "facts"), cancellationToken)
                .ConfigureAwait(false);
            if (_pipeline.ParserIdentity(fresh: true) != parserBefore || baselineFacts.SchemaVersion != candidateFacts.SchemaVersion)
                return Failure(JobFailureCategory.Semantic,
                    "PanGloss changed during the comparison; rerun the candidate evidence job.");

            var judgmentDigest = CombineJudgmentDigests(prepared.BaselineJudgments, prepared.CandidateJudgments);
            var candidateIdentity = ComputeCandidateIdentity(dryRunInput.Proposal, prepared.Prerequisites,
                source.Token, prepared.DryRun.EffectDigest, AnchorDigest(prepared.PublishedAnchor),
                source.SourceSha256, prepared.CandidateSourceSha256, candidateFacts.ModelFingerprint,
                parserIdentity, candidateFacts.SchemaVersion, EvidenceSchema.Version, input.ScopeBinding!,
                judgmentDigest, input.RetirementExpectationTranslation?.MappingDigest);
            var baselineTokenJson = EvidenceBundlePipeline.TokenJson(source.Token);
            var beforeEvidencePath = Path.Combine(beforeWorking, "evidence.sqlite");
            var beforeEvidence = EvidenceWriter.Write(beforeEvidencePath, prepared.BaselineEvidence,
                baselineTokenJson, source.Token.BundleDigest, baselineFacts.ModelFingerprint);
            ParserOverlayProjection parserOverlay;
            IReadOnlyList<ReportableAssessment> reportableAssessments;
            try
            {
                var assessmentSources = ParserAssessmentMaterial.Read(_database, assessmentIds, source.Token,
                    prepared.CandidateSourceSha256, parserIdentity, out reportableAssessments,
                    dryRunInput.Proposal.ProposalId, dryRunInput.Proposal.IntentDigest);
                parserOverlay = AssessmentEvidenceProjector.Build(assessmentSources,
                    prepared.CandidateEvidence.Wordforms, prepared.CandidateSourceSha256, parserIdentity,
                    dryRunInput.Proposal.ProposalId, dryRunInput.Proposal.IntentDigest,
                    prepared.CandidateEvidence.ReviewedNegatives);
            }
            catch (Exception exception) when (exception is InvalidDataException or IOException or ArgumentException)
            {
                return Failure(JobFailureCategory.Semantic, exception.Message);
            }
            var candidateEvidenceProjection = EvidenceBundlePipeline.WithParserOverlay(prepared.CandidateEvidence, parserOverlay);
            var afterEvidencePath = Path.Combine(afterWorking, "evidence.sqlite");
            var afterEvidence = EvidenceWriter.Write(afterEvidencePath, candidateEvidenceProjection,
                baselineTokenJson, source.Token.BundleDigest, candidateFacts.ModelFingerprint,
                "candidate", candidateIdentity);

            var beforeMaterial = EvidenceBundlePipeline.MaterialKey(EvidenceBundlePipeline.ComparisonOrigin,
                "baseline", source.Token, baselineFacts.ModelFingerprint, parserIdentity, baselineFacts.SchemaVersion,
                beforeEvidence.SchemaVersion, input.ScopeBinding!, [], null);
            var afterMaterial = EvidenceBundlePipeline.MaterialKey(EvidenceBundlePipeline.ComparisonOrigin,
                "candidate", source.Token, candidateFacts.ModelFingerprint, parserIdentity,
                candidateFacts.SchemaVersion, afterEvidence.SchemaVersion, input.ScopeBinding!, [],
                candidateIdentity);
            var before = PrepareReport(beforeBundleId, beforeMaterial, source, baselineFacts, beforeEvidence,
                beforeEvidencePath, input.ScopeBinding!, input.MeasureId, "baseline", null, [], [],
                input.RetirementExpectationTranslation,
                (staging, final) => (beforeStaging, beforeFinal) = (staging, final), cancellationToken);
            var after = PrepareReport(afterBundleId, afterMaterial, source, candidateFacts, afterEvidence,
                afterEvidencePath, input.ScopeBinding!, input.MeasureId, "candidate", candidateIdentity,
                assessmentIds, reportableAssessments, input.RetirementExpectationTranslation,
                (staging, final) => (afterStaging, afterFinal) = (staging, final), cancellationToken);
            beforeFinal = _publisher.Publish(before.StagingDirectory, before.Bundle.BundleId);
            beforeStaging = null;
            afterFinal = _publisher.Publish(after.StagingDirectory, after.Bundle.BundleId);
            afterStaging = null;

            _artifacts.Publish(before.Bundle, before.Report, cancellationToken,
                () => _publisher.BeforeStoreCommit(before.FinalDirectory),
                () => _publisher.AfterStoreCommit(before.FinalDirectory),
                requireCurrentBaseline: false, setCurrentBaseline: false);
            beforeFinal = null;
            _artifacts.Publish(after.Bundle, after.Report, cancellationToken,
                () => _publisher.BeforeStoreCommit(after.FinalDirectory),
                () => _publisher.AfterStoreCommit(after.FinalDirectory));
            afterFinal = null;
            _ = _retention.Clean(_projectKey);

            var binding = new ParsimonyCandidateBinding(candidateIdentity, source.Token, dryRun.JobId,
                dryRunInput.Proposal.ProposalId, dryRunInput.Proposal.IntentDigest,
                dryRunInput.Proposal.ContentSha256, prepared.Prerequisites.Select(item =>
                    new ParsimonyPrerequisiteBinding(item.ProposalId, item.IntentDigest, item.ContentSha256)).ToArray(),
                prepared.DryRun.EffectDigest, AnchorDigest(prepared.PublishedAnchor), source.SourceSha256,
                prepared.CandidateSourceSha256, judgmentDigest);
            var response = new ParsimonyCandidateEvidenceResponse(1, input.MeasureId, binding,
                before.Response, after.Response, CompareFindings(before.Response, after.Response),
                CompareApprovedAnalyses(ApprovedTuples(prepared.BaselineEvidence),
                    ApprovedTuples(prepared.CandidateEvidence)));
            return new JobOutcome(JobStatus.Completed, ResultJson: JsonSerializer.Serialize(response,
                MotifJson.CreateOptions()));
        }
        catch (OperationCanceledException)
        {
            return new JobOutcome(JobStatus.Cancelled, JobFailureCategory.Cancellation);
        }
        catch (PanGlossInvocationException exception)
        {
            return exception.Category == JobFailureCategory.Cancellation
                ? new JobOutcome(JobStatus.Cancelled, JobFailureCategory.Cancellation)
                : Failure(exception.Category, exception.Message);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException or
                                          InvalidOperationException or JsonException or UnauthorizedAccessException)
        {
            return Failure(JobFailureCategory.Semantic, exception.Message);
        }
        finally
        {
            DiscardAttempt(beforeBundleId, beforeWorking, beforeStaging, beforeFinal);
            DiscardAttempt(afterBundleId, afterWorking, afterStaging, afterFinal);
        }
    }

    internal static string ComputeCandidateIdentity(FrozenProposalRevision proposal,
        IReadOnlyList<FrozenProposalRevision> prerequisites, BaselineToken baselineToken,
        string dryRunEffectDigest, string dryRunAnchorDigest, string baselineSourceSha256,
        string candidateSourceSha256, string modelFingerprint, string parserIdentity, int factsSchemaVersion,
        int evidenceSchemaVersion, ParsimonyScopeBinding scopeBinding, string humanJudgmentDigest,
        string? retirementExpectationMappingDigest = null)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        ArgumentNullException.ThrowIfNull(prerequisites);
        ArgumentNullException.ThrowIfNull(baselineToken);
        ArgumentNullException.ThrowIfNull(scopeBinding);
        proposal.Validate();
        foreach (var prerequisite in prerequisites) prerequisite.Validate();
        var material = JsonSerializer.Serialize(new
        {
            format = "motif-parsimony-candidate-material",
            version = 1,
            baselineToken,
            proposal,
            prerequisites,
            dryRunEffectDigest,
            dryRunAnchorDigest,
            baselineSourceSha256,
            candidateSourceSha256,
            modelFingerprint,
            parserIdentity,
            factsSchemaVersion,
            evidenceSchemaVersion,
            scopeBinding,
            humanJudgmentDigest,
            retirementExpectationMappingDigest,
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return Hash(material);
    }

    internal static BoundDryRunAnchor VerifyReconstructedDryRun(string publishedDryRunJson, DryRunModel reconstructed)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publishedDryRunJson);
        ArgumentNullException.ThrowIfNull(reconstructed);
        using var published = JsonDocument.Parse(publishedDryRunJson);
        var root = published.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !StringComparer.Ordinal.Equals(root.GetProperty("intentDigest").GetString(), reconstructed.IntentDigest) ||
            !StringComparer.Ordinal.Equals(root.GetProperty("effectDigest").GetString(), reconstructed.EffectDigest))
            throw new InvalidDataException("The candidate copy did not reproduce the published Dry Run effects.");
        var publishedAnchor = JsonSerializer.Deserialize<BoundDryRunAnchor>(root.GetProperty("anchor").GetRawText(),
            MotifJson.CreateOptions()) ?? throw new InvalidDataException("The published Dry Run has no anchor.");
        if (publishedAnchor.IntentDigest != reconstructed.Anchor.IntentDigest ||
            publishedAnchor.FootprintDigest != reconstructed.Anchor.FootprintDigest ||
            publishedAnchor.EffectDigest != reconstructed.Anchor.EffectDigest ||
            publishedAnchor.RunnerVersion != reconstructed.Anchor.RunnerVersion ||
            publishedAnchor.LibLcmVersion != reconstructed.Anchor.LibLcmVersion ||
            publishedAnchor.ProjectionVersion != reconstructed.Anchor.ProjectionVersion)
            throw new InvalidDataException("The candidate copy did not reproduce the published Dry Run anchor.");
        return publishedAnchor;
    }

    internal static ParsimonyApprovedAnalysisPreservation CompareApprovedAnalyses(
        IReadOnlyList<ParsimonyApprovedAnalysisTuple> baseline,
        IReadOnlyList<ParsimonyApprovedAnalysisTuple> candidate)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(candidate);
        var candidateSet = candidate.ToHashSet();
        var unmatched = baseline.Where(tuple => !candidateSet.Contains(tuple))
            .OrderBy(tuple => tuple.WordformGuid, StringComparer.Ordinal)
            .ThenBy(tuple => tuple.AnalysisGuid, StringComparer.Ordinal)
            .ThenBy(tuple => tuple.ContentSha256, StringComparer.Ordinal).ToArray();
        return new ParsimonyApprovedAnalysisPreservation(
            unmatched.Length == 0 ? "preserved" : "inconclusive", baseline.Count,
            baseline.Count - unmatched.Length, unmatched);
    }

    private async Task<CandidateProjectStates> PrepareProjectStatesAsync(
        DryRunJobInput input, string publishedDryRunJson, DryRunSourceBinding source, string copyRoot,
        ParsimonyScopeBinding scope, ParserAssessmentSourceArtifact? assessmentSource,
        CancellationToken cancellationToken)
    {
        CandidateProjectStates? result = null;
        var lane = _lanes.GetOrCreate(_projectKey);
        await lane.EnqueueAsync(ProjectWorkItem.CandidateExport(async (_, laneToken) =>
        {
            var scratchFactory = new ScratchCacheFactory(_loader);
            var baselineCache = scratchFactory.CreateFromFileCopy(source.FwDataPath,
                Path.Combine(copyRoot, "baseline"));
            try
            {
                var baselinePath = Path.GetFullPath(baselineCache.ProjectId.Path);
                var baselineWords = TextWordsProjectionBuilder.Build(baselineCache, laneToken);
                var baselineEvidence = EvidenceProjectionBuilder.Build(baselineCache, baselineWords, scope, laneToken);
                var baselineJudgments = HumanJudgmentReader.Read(baselineCache);
                var candidateCache = scratchFactory.CreateFromFileCopy(source.FwDataPath,
                    Path.Combine(copyRoot, "candidate"));
                using var scratch = DryRunScratch.Adopt(candidateCache, "candidate Parsimony file copy");
                var applied = ProjectAppliedLog.ReadAll(scratch.PeekCache())
                    .Select(entry => entry.ProposalId).ToArray();
                var plan = input.BuildExecutionPlan(applied);
                var dryRun = ProposalDryRunner.Run(scratch, plan);
                var publishedAnchor = VerifyReconstructedDryRun(publishedDryRunJson, dryRun);
                var orderedPrerequisites = plan.Prerequisites.Select(item => input.Prerequisites.Single(frozen =>
                    frozen.ProposalId == item.ProposalId.Value)).ToArray();
                var candidate = scratch.PeekCache();
                var candidateWords = TextWordsProjectionBuilder.Build(candidate, laneToken);
                var reconstructedEvidence = EvidenceProjectionBuilder.Build(candidate, candidateWords, scope, laneToken);
                var candidateJudgments = HumanJudgmentReader.Read(candidate);
                ParsimonyEvidenceProjection candidateEvidence;
                string candidatePath;
                string candidateSourceSha256;
                if (assessmentSource is null)
                {
                    candidateEvidence = reconstructedEvidence;
                    _loader.Save(candidate);
                    candidatePath = Path.GetFullPath(candidate.ProjectId.Path);
                    candidateSourceSha256 = "sha256:" + Hash(File.ReadAllBytes(candidatePath));
                }
                else
                {
                    AssessmentSourceArtifactStore.Verify(assessmentSource.SourcePath,
                        assessmentSource.SourceBytesSha256);
                    using var assessedCandidate = _loader.LoadScratchCache(assessmentSource.SourcePath);
                    var assessedWords = TextWordsProjectionBuilder.Build(assessedCandidate, laneToken);
                    candidateEvidence = EvidenceProjectionBuilder.Build(assessedCandidate, assessedWords, scope,
                        laneToken);
                    if (ProjectionDigest(reconstructedEvidence) != ProjectionDigest(candidateEvidence))
                        throw new InvalidDataException(
                            "The retained Trial source does not reproduce the candidate state in the Dry Run.");
                    candidateJudgments = HumanJudgmentReader.Read(assessedCandidate);
                    candidatePath = assessmentSource.SourcePath;
                    candidateSourceSha256 = assessmentSource.SourceBytesSha256;
                }
                result = new CandidateProjectStates(baselinePath, candidatePath, dryRun,
                    baselineEvidence, candidateEvidence, baselineJudgments, candidateJudgments,
                    candidateSourceSha256, orderedPrerequisites, publishedAnchor);
            }
            finally
            {
                baselineCache.Dispose();
            }
        }), cancellationToken).ConfigureAwait(false);
        return result ?? throw new InvalidOperationException("The project lane produced no candidate state.");
    }

    private async Task<FactsResult> BuildFactsAsync(string projectPath, BaselineToken token, bool candidate,
        string? dryRunEffectDigest, string outputDirectory, CancellationToken cancellationToken)
    {
        var snapshotPath = Path.Combine(outputDirectory, "grammar-snapshot.json");
        Directory.CreateDirectory(outputDirectory);
        var imported = await _invoker.RunAsync(new PanGlossRequest.Import(projectPath, snapshotPath),
            "parsimony-candidate-import", cancellationToken).ConfigureAwait(false);
        if (imported is not PanGlossOutcome.Completed)
            throw new PanGlossInvocationException(CategoryOf(imported), imported.Message);
        var contextJson = JsonSerializer.Serialize(new
        {
            format = "pangloss-facts-context",
            version = 1,
            baselineToken = token,
            inputKind = candidate ? "proposal-dry-run" : "baseline",
            dryRunDigest = candidate ? dryRunEffectDigest : null,
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var factsDirectory = Path.Combine(outputDirectory, "facts-output");
        var outcome = await _invoker.RunAsync(new PanGlossRequest.Facts(snapshotPath, contextJson, factsDirectory),
            "parsimony-candidate-facts", cancellationToken).ConfigureAwait(false);
        if (outcome is not PanGlossOutcome.Completed completed || completed.Facts is null)
            throw new PanGlossInvocationException(CategoryOf(outcome), outcome.Message);
        using var lease = completed.FactsArtifactLease;
        var destination = Path.Combine(outputDirectory, "grammar-facts.sqlite");
        File.Copy(completed.Facts.Path, destination, overwrite: false);
        var digest = StripDigest(_pipeline.FreshDigest(destination));
        if (!StringComparer.Ordinal.Equals(digest, StripDigest(completed.Facts.OutputSha256)))
            throw new InvalidDataException("PanGloss facts changed before Motif adopted the artifact.");
        return new FactsResult(destination, completed.Facts.ModelFingerprint, completed.Facts.SchemaVersion,
            digest);
    }

    private PreparedEvidenceBundle PrepareReport(string bundleId, string materialKey,
        DryRunSourceBinding source, FactsResult facts, ParsimonyArtifactDigest evidenceDigest,
        string evidencePath, ParsimonyScopeBinding scope, string measureId, string inputKind,
        string? candidateIdentity, IReadOnlyList<string> assessmentIds, IReadOnlyList<ReportableAssessment> assessments,
        SIL.Motif.Contract.Retirement.RetirementExpectationTranslation? retirementTranslation,
        Action<string, string> located, CancellationToken cancellation) =>
        _pipeline.Prepare(new EvidenceBundleBuild(bundleId, source.Token, inputKind, candidateIdentity, materialKey,
            facts.Path, facts.Sha256, facts.SchemaVersion, facts.ModelFingerprint, evidencePath, evidenceDigest,
            scope, measureId, assessmentIds, assessments, retirementTranslation, source.RootDirectory,
            IncludeDispositionProjection: false), located, cancellation: cancellation);

    private static IReadOnlyList<ParsimonyCandidateFindingChange> CompareFindings(
        ParsimonyReportResponse before, ParsimonyReportResponse after)
    {
        var old = before.Findings.ToDictionary(item => item.FindingId, StringComparer.Ordinal);
        var current = after.Findings.ToDictionary(item => item.FindingId, StringComparer.Ordinal);
        var changes = new List<ParsimonyCandidateFindingChange>();
        foreach (var id in old.Keys.Union(current.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            old.TryGetValue(id, out var previous);
            current.TryGetValue(id, out var next);
            var kind = previous is null ? "added" : next is null ? "removed" :
                previous.EvidenceDigest == next.EvidenceDigest ? "unchanged" : "evidence-changed";
            changes.Add(new ParsimonyCandidateFindingChange(id, kind, previous, next));
        }
        return changes;
    }

    private static IReadOnlyList<ParsimonyApprovedAnalysisTuple> ApprovedTuples(
        ParsimonyEvidenceProjection projection) => projection.Wordforms
        .SelectMany(wordform => wordform.Analyses.Where(analysis => analysis.Opinion == "approved")
            .Select(analysis => new ParsimonyApprovedAnalysisTuple(GuidText(wordform.Guid),
                GuidText(analysis.Guid), analysis.ContentSha256)))
        .OrderBy(tuple => tuple.WordformGuid, StringComparer.Ordinal)
        .ThenBy(tuple => tuple.AnalysisGuid, StringComparer.Ordinal)
        .ThenBy(tuple => tuple.ContentSha256, StringComparer.Ordinal).ToArray();

    private static string CombineJudgmentDigests(HumanJudgmentProjectSnapshot baseline,
        HumanJudgmentProjectSnapshot candidate) => Hash(JsonSerializer.Serialize(new
    {
        baselineProjectId = baseline.ProjectId,
        baselineLogicalDigest = baseline.LogicalDigest,
        candidateProjectId = candidate.ProjectId,
        candidateLogicalDigest = candidate.LogicalDigest,
    }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

    private static string AnchorDigest(BoundDryRunAnchor anchor) => Hash(JsonSerializer.Serialize(anchor,
        MotifJson.CreateOptions()));

    private void DiscardAttempt(string bundleId, string working, string? staging, string? final)
    {
        if (_artifacts.Get(bundleId) is null)
        {
            _publisher.Discard(staging);
            _publisher.Discard(final);
            _artifacts.RemoveBuildClaim(bundleId);
        }
        _publisher.Discard(working);
    }

    private static string ProjectionDigest(ParsimonyEvidenceProjection projection) =>
        Hash(CanonicalJson.Canonicalize(JsonSerializer.Serialize(projection, MotifJson.CreateOptions())));

    private static string GuidText(Guid value) => value.ToString("D").ToLowerInvariant();

    private static string StripDigest(string digest) => digest.StartsWith("sha256:", StringComparison.Ordinal)
        ? digest[7..] : digest;

    private static string Hash(string value) => Hash(Encoding.UTF8.GetBytes(value));

    private static string Hash(byte[] value) => Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();

    private static JobFailureCategory CategoryOf(PanGlossOutcome outcome) => outcome switch
    {
        PanGlossOutcome.Refused => JobFailureCategory.ParserRefusal,
        PanGlossOutcome.Cancelled => JobFailureCategory.Cancellation,
        _ => JobFailureCategory.Infrastructure,
    };

    private static JobOutcome Failure(JobFailureCategory category, string detail) =>
        new(JobStatus.Failed, category, JsonSerializer.Serialize(new { detail }));

    private sealed record CandidateProjectStates(string BaselineProjectPath, string CandidateProjectPath,
        DryRunModel DryRun, ParsimonyEvidenceProjection BaselineEvidence,
        ParsimonyEvidenceProjection CandidateEvidence, HumanJudgmentProjectSnapshot BaselineJudgments,
        HumanJudgmentProjectSnapshot CandidateJudgments, string CandidateSourceSha256,
        IReadOnlyList<FrozenProposalRevision> Prerequisites, BoundDryRunAnchor PublishedAnchor);

    private sealed record FactsResult(string Path, string ModelFingerprint, int SchemaVersion, string Sha256);

    private sealed class PanGlossInvocationException(JobFailureCategory category, string message) : Exception(message)
    {
        public JobFailureCategory Category { get; } = category;
    }
}
