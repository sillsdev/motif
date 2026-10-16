using System.Text.Json;
using System.Text.Json.Serialization;
using SIL.LCModel;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Canonicalization;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Analysis;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.Config;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Host.Parser;
using SIL.Motif.Runner.DryRun;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Scheduling;
using SIL.Motif.Worker.Store;
using DryRunModel = SIL.Motif.Model.DryRun.DryRun;

namespace SIL.Motif.Worker.Jobs;

/// <summary>
/// The closed wire shape a Trial job's <c>InputJson</c> carries. Written by the CLI when it enqueues a
/// Trial and read back by <see cref="TrialJobHandler"/> — the two never share the proposal JSON any other
/// way, since they run in different processes.
/// </summary>
/// <param name="ProposalJson">
/// The frozen canonical semantic content of a finalized revision or mutable Draft.
/// </param>
/// <param name="Scope">The declared Assessment scope to use, or <c>null</c> for the project's default.</param>
/// <param name="Limits">Optional per-word limits copied from the earlier Assessment being compared.</param>
/// <param name="WordList">
/// The named words stored beside the project's store instead of in <c>words</c>, when inline they would carry the
/// input past its structured JSON bound.
/// </param>
public sealed record TrialJobInput(
    [property: JsonPropertyName("proposalJson")] string ProposalJson,
    [property: JsonPropertyName("scope")] string? Scope,
    [property: JsonPropertyName("words")] IReadOnlyList<string>? Words = null,
    [property: JsonPropertyName("allWords")] bool AllWords = false,
    [property: JsonPropertyName("prerequisites")] IReadOnlyList<FrozenProposalRevision>? Prerequisites = null,
    [property: JsonPropertyName("limits")] TrialLimits? Limits = null,
    [property: JsonPropertyName("baselineAssessmentIds")] IReadOnlyList<string>? BaselineAssessmentIds = null,
    [property: JsonPropertyName("negativeWords")] IReadOnlyList<string>? NegativeWords = null,
    [property: JsonPropertyName("wordList")] JobWordList? WordList = null)
{
    /// <exception cref="InvalidOperationException">The input is not a well-formed Trial job input.</exception>
    public static TrialJobInput Parse(string inputJson) =>
        JsonSerializer.Deserialize<TrialJobInput>(inputJson, MotifJson.CreateOptions())
        ?? throw new InvalidOperationException("A Trial job's input could not be read.");
}

/// <summary>
/// Runs a Trial: one throwaway, writable copy of the published Baseline, opened once, that a Proposal is
/// applied to and read back as a Dry Run, then saved and handed to an Assessor for the candidate's
/// Assessments — all from that same copy.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this cannot reuse <see cref="DryRunJobHandler"/>'s in-place open.</b> A plain Dry Run opens the
/// published Baseline's <c>.fwdata</c> directly (<c>BaselineScratchFactory.OpenSingleUse</c>) and never
/// saves it, because saving an in-place candidate would overwrite the immutable published directory. A
/// Trial's candidate must be saved — an Assessor reads bytes on disk, not an open cache — so it is opened
/// through a private, disposable file copy instead (<c>ScratchCacheFactory.CreateFromFileCopy</c>), which
/// still costs exactly one Baseline load. Saving that copy in place and pointing the Assessor at its own
/// folder needs no second copy.
/// </para>
/// <para>
/// A failure is <see cref="JobStatus.Failed"/> only while nothing has been committed yet. Once the Dry Run
/// is published, every later failure — building the candidate for Assessment, or the Assessor itself —
/// lands on <see cref="JobStatus.CompletedWithAssessmentFailure"/>, because the Dry Run already succeeded
/// and is retained.
/// </para>
/// </remarks>
internal sealed class TrialJobHandler
{
    /// <summary>The durable job kind this handler drives.</summary>
    internal const string TrialKind = "trial";

    private readonly BaselineRepository _baselines;
    private readonly ProjectLaneRegistry _lanes;
    private readonly IProjectConfigurationReader _configuration;
    private readonly IAssessorCatalog _assessors;
    private readonly IAssessmentRepository _assessments;
    private readonly string _workerRoot;
    private readonly Func<string, string, CancellationToken,
        Task<(IReadOnlyCollection<Guid> AppliedProposalIds, DryRunScratch? Scratch)>> _openScratch;
    private readonly Func<DryRunScratch?, PrerequisiteExecutionPlan, CancellationToken, Task<DryRunModel>> _runDryRun;
    private readonly Func<LcmCache?, CancellationToken, Task<string>> _prepareForAssessment;
    private readonly Func<string, string, Action<bool>?, CancellationToken, Task<GrammarCheckResponse?>>? _grammarHealth;

    /// <param name="baselines">This project's published-Baseline store.</param>
    /// <param name="proposals">
    /// This project's Proposal store, used only to resolve prerequisites (<see cref="ProposalRepository.PlanPrerequisites"/>)
    /// — never to read the requested Proposal itself, which the job's own <c>InputJson</c> already carries.
    /// </param>
    /// <param name="lanes">Serializes Baseline-dependent work per project.</param>
    /// <param name="configuration">Reads the project's declared Assessment scopes and policy.</param>
    /// <param name="assessors">Resolves the scope's named Assessor.</param>
    /// <param name="assessments">Where produced Assessments are recorded.</param>
    /// <param name="openScratch">
    /// Opens the one private, writable copy of the published Baseline this Trial measures against — a
    /// single Baseline load — and reads which Proposals are already applied in it.
    /// </param>
    /// <param name="runDryRun">Runs the resolved plan against the scratch <paramref name="openScratch"/> opened.</param>
    /// <param name="prepareForAssessment">
    /// Saves the mutated scratch in place and returns the directory an Assessor can read it from. Never
    /// opens the Baseline again.
    /// </param>
    internal TrialJobHandler(BaselineRepository baselines, ProposalRepository proposals,
        ProjectLaneRegistry lanes, IProjectConfigurationReader configuration, IAssessorCatalog assessors,
        IAssessmentRepository assessments,
        Func<string, string, CancellationToken,
            Task<(IReadOnlyCollection<Guid> AppliedProposalIds, DryRunScratch? Scratch)>> openScratch,
        Func<DryRunScratch?, PrerequisiteExecutionPlan, CancellationToken, Task<DryRunModel>> runDryRun,
        Func<LcmCache?, CancellationToken, Task<string>> prepareForAssessment, string workerRoot,
        Func<string, string, Action<bool>?, CancellationToken, Task<GrammarCheckResponse?>>? grammarHealth = null)
    {
        _baselines = baselines ?? throw new ArgumentNullException(nameof(baselines));
        ArgumentNullException.ThrowIfNull(proposals);
        _lanes = lanes ?? throw new ArgumentNullException(nameof(lanes));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _assessors = assessors ?? throw new ArgumentNullException(nameof(assessors));
        _assessments = assessments ?? throw new ArgumentNullException(nameof(assessments));
        ArgumentException.ThrowIfNullOrWhiteSpace(workerRoot);
        _workerRoot = workerRoot;
        _openScratch = openScratch ?? throw new ArgumentNullException(nameof(openScratch));
        _runDryRun = runDryRun ?? throw new ArgumentNullException(nameof(runDryRun));
        _prepareForAssessment = prepareForAssessment ?? throw new ArgumentNullException(nameof(prepareForAssessment));
        _grammarHealth = grammarHealth;
    }

    /// <summary>Runs one claimed Trial job through to a terminal outcome, or parks it waiting for a Baseline.</summary>
    internal async Task<JobOutcome?> RunAsync(ClaimedJob claim, ProjectLocator project, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(claim);
        if (claim.Job.Status != JobStatus.Running)
            throw new InvalidOperationException("A Trial must already be claimed by a runner.");
        using var parseLease = SIL.Motif.Host.Store.ProjectParseLease.TryAcquire(claim.Database);
        if (parseLease is null)
            return new JobOutcome(JobStatus.Failed, JobFailureCategory.Unknown,
                JsonSerializer.Serialize(new { detail = SIL.Motif.Host.Store.ProjectParseLease.BusyRefusal().Message }));
        var workspaceKey = ProjectWorkspaceKey.Compute(project);
        if (!StringComparer.Ordinal.Equals(claim.Job.Kind, TrialKind) ||
            !StringComparer.Ordinal.Equals(claim.Job.ProjectKey, workspaceKey))
            throw new InvalidOperationException("The job does not address this project's Trial.");

        var baseline = _baselines.GetCurrent(workspaceKey);
        if (baseline is null)
        {
            claim.Transition(JobStatus.WaitingForBaseline);
            return null;
        }

        var input = TrialJobInput.Parse(claim.Job.InputJson);
        var proposal = ProposalJsonParser.Parse(input.ProposalJson);
        var configuration = _configuration.Read(project);
        var scopeConfiguration = ResolveScope(configuration, input.Scope);
        var assessor = _assessors.Resolve(scopeConfiguration.Assessor);
        var lane = _lanes.GetOrCreate(workspaceKey);

        // Reported while queued behind the lane; the lane's own dispatch resumes this directly into Running.
        claim.Transition(JobStatus.WaitingForBaseline);

        var scratchRoot = AllocateScratchRoot(claim.Job.JobId);
        CandidateRun candidate;
        var preserveScratch = false;
        try
        {
            var words = input.WordList is { } list ? JobWordLists.Read(claim.Database, list) : input.Words;
            candidate = await RunCandidateAsync(claim, lane, proposal, input.Prerequisites ?? [],
                scopeConfiguration, words, input.AllWords, input.Limits, baseline.FwDataPath,
                scratchRoot, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            TryDeleteDirectory(scratchRoot);
            var detail = JsonSerializer.Serialize(new { detail = exception.Message });
            // A successful terminal status may not carry a failure category, so only Failed gets one here.
            return claim.Job.DryRunPublished
                ? new JobOutcome(JobStatus.CompletedWithAssessmentFailure, JobFailureCategory.None, detail)
                : new JobOutcome(JobStatus.Failed, JobFailureCategory.Unknown, detail);
        }

        // The run already happened; cancellation now must still stop it from becoming durable further.
        if (cancellationToken.IsCancellationRequested)
        {
            TryDeleteDirectory(scratchRoot);
            return new JobOutcome(JobStatus.Cancelled, JobFailureCategory.Cancellation);
        }

        try
        {
            string? grammarCheckWarning = null;
            if (_grammarHealth is not null)
            {
                var grammarPhase = new JobStageProgress("grammar-health", 1, 2, "grammar-health");
                claim.PublishStageProgress(grammarPhase);
                var grammarWait = claim.ObserveParserWait(grammarPhase);
                try
                {
                    var grammarCheck = await _grammarHealth(candidate.GrammarPath,
                        project.FieldWorksProjectIdentity, grammarWait.Observe, cancellationToken).ConfigureAwait(false);
                    if (grammarCheck is null)
                    {
                        grammarCheckWarning = "The grammar check did not return a report.";
                    }
                    else
                    {
                        new TrialGrammarCheckRepository(claim.Database).Save(baseline.Token,
                            proposal.ProposalId, IntentDigest.Compute(proposal), candidate.Selection.Sha256,
                            grammarCheck, baseline.SourceLastWriteUtc);
                    }
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    grammarCheckWarning = exception.Message;
                }
            }
            var limitMs = candidate.Scope.PerWordLimit is { } limit ? (int?)limit.TotalMilliseconds : null;
            var initialProgress = new TrialWordProgress(0, candidate.Scope.WordForms.Count, null)
                { PerWordLimitMs = limitMs };
            claim.PublishTrialProgress(initialProgress);
            var batchPhase = new JobStageProgress("batch", _grammarHealth is null ? 1 : 2,
                _grammarHealth is null ? 1 : 2, "assessment");
            var batchWait = claim.ObserveParserWait(batchPhase);
            var produced = await assessor.ProduceAsync(candidate.Scope, candidate.ExportedDirectory,
                    progress => claim.PublishTrialProgress(progress with { PerWordLimitMs = limitMs }),
                    batchWait.Observe, cancellationToken)
                .ConfigureAwait(false);
            var artifactLeases = produced.Select(item => item.ArtifactLease).OfType<AssessmentArtifactLease>().Distinct().ToArray();
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var durableAssessments = produced.Select(item => item.Invocation is { } invocation
                    ? item with
                    {
                        Invocation = invocation with
                        {
                            SourcePath = AssessmentSourceArtifactStore.Persist(_workerRoot,
                                invocation.SourcePath, invocation.SourceBytesSha256),
                        },
                    }
                    : item).ToArray();
                var assessmentIds = RecordAll(durableAssessments, proposal, candidate.DryRun,
                    candidate.Selection, candidate.Scope, scopeConfiguration, assessor.Name, baseline.Token,
                    candidate.ProjectStandings);
                foreach (var lease in artifactLeases) lease.Retain();
                var completionJson = JsonSerializer.Serialize(
                    new TrialCompletion(baseline.Token, assessmentIds, input.AllWords, grammarCheckWarning),
                    MotifJson.CreateOptions());
                TrialScratchRetention.DeleteEarlier(claim.Database, workspaceKey,
                    proposal.ProposalId.Value, claim.Job.JobId);
                preserveScratch = true;
                return new JobOutcome(JobStatus.Completed, JobFailureCategory.None, completionJson);
            }
            finally
            {
                foreach (var lease in artifactLeases) lease.Dispose();
            }
        }
        catch (OperationCanceledException)
        {
            return new JobOutcome(JobStatus.Cancelled, JobFailureCategory.Cancellation);
        }
        catch (Exception exception)
        {
            var detail = JsonSerializer.Serialize(new { detail = exception.Message });
            return new JobOutcome(JobStatus.CompletedWithAssessmentFailure, JobFailureCategory.None, detail);
        }
        finally
        {
            if (!preserveScratch) TryDeleteDirectory(scratchRoot);
        }
    }

    // What one attempt on a throwaway copy produces; assembled on the lane, read off it.
    private sealed record CandidateRun(DryRunModel DryRun, AssessmentScope.Words Scope, Selection Selection,
        string ExportedDirectory, string GrammarPath, IReadOnlyDictionary<string, string> ProjectStandings);

    private async Task<CandidateRun> RunCandidateAsync(ClaimedJob claim, ProjectLane lane,
        Contract.Model.Proposal proposal, IReadOnlyList<FrozenProposalRevision> frozenPrerequisites,
        AssessmentScopeConfiguration scopeConfiguration,
        IReadOnlyList<string>? requestedWords, bool allWords, TrialLimits? limits, string baselineFwDataPath,
        string scratchRoot, CancellationToken cancellationToken)
    {
        CandidateRun? candidate = null;
        await lane.EnqueueAsync(ProjectWorkItem.CandidateExport(async (_, laneToken) =>
        {
            claim.Transition(JobStatus.Running);
            var (appliedProposalIds, scratch) = await _openScratch(baselineFwDataPath, scratchRoot, laneToken)
                .ConfigureAwait(false);
            try
            {
                var plan = PrerequisiteExecutionPlan.CreateFromFrozen(proposal,
                    frozenPrerequisites.Select(item => item.Validate()).ToArray(), appliedProposalIds);
                var dryRun = await _runDryRun(scratch, plan, laneToken).ConfigureAwait(false);
                claim.PublishDryRun(DryRunJobHandler.BuildPublishedDryRunJson(dryRun));

                var cache = scratch?.PeekCache();
                var words = requestedWords is not null
                    ? requestedWords.Distinct(StringComparer.Ordinal).ToArray()
                    : cache is null ? [] : WordQueryResolver.Resolve(
                        allWords ? WordQueryResolver.AllWordformsQueryText : scopeConfiguration.Query, cache).ToArray();
                var scope = new AssessmentScope.Words(words, ParseCollect(scopeConfiguration.Collect),
                    limits is { } selectedLimits ? selectedLimits.PerWordLimit : scopeConfiguration.PerWordLimit,
                    limits?.PerWordStepLimit ?? scopeConfiguration.PerWordStepLimit);
                var projectStandings = ReadProjectStandings(cache, words);
                var exportedDirectory = await _prepareForAssessment(cache, laneToken).ConfigureAwait(false);
                candidate = new CandidateRun(dryRun, scope, Selection.Create(scopeConfiguration.Name, words),
                    exportedDirectory, Path.GetFullPath(cache?.ProjectId.Path
                        ?? throw new InvalidOperationException("A Trial requires a scratch project for grammar health.")),
                    projectStandings);
            }
            finally
            {
                scratch?.Dispose();
            }
        }), cancellationToken).ConfigureAwait(false);

        // The lane either runs the work item to completion or rethrows, so this is set by here.
        return candidate ?? throw new InvalidOperationException("The lane produced no candidate.");
    }

    private IReadOnlyList<string> RecordAll(IReadOnlyList<ProducedAssessment> produced, Contract.Model.Proposal proposal,
        DryRunModel dryRun, Selection selection, AssessmentScope.Words scope,
        AssessmentScopeConfiguration scopeConfiguration, string assessorName, BaselineToken baselineToken,
        IReadOnlyDictionary<string, string> projectStandings)
    {
        // The query is what the scope was told to do; the words are only what it resolved to on this run.
        var scopeJson = ScopeCodec.Write(new StoredScope.Trial(
            scopeConfiguration.Query, scope.WordForms, scope.Collect, scope.PerWordLimit, scope.PerWordStepLimit));
        var scopeDigest = AssessmentMaterial.Digest(scopeJson);
        var baselineTokenJson = JsonSerializer.Serialize(baselineToken, MotifJson.CreateOptions());

        var ids = new List<string>();
        var records = new List<NewAssessmentRecord>();
        foreach (var item in produced)
        {
            var assessmentId = CanonicalId.Mint("assessment/").Value;
            var record = AssessmentMaterial.ToWordRecord(item, assessmentId, proposal.ProposalId,
                dryRun.IntentDigest, assessorName, scopeJson, scopeDigest, baselineTokenJson,
                new AssessmentSubject.Words(selection, "none", "1"));
            records.Add(record with
            {
                Material = record.RequireWordMaterial() with
                {
                    Rows = record.RequireWordMaterial().Rows.Select(word => projectStandings.TryGetValue(
                        word.Word.Normalize(System.Text.NormalizationForm.FormD), out var standing)
                        ? word with { ProjectStanding = standing }
                        : word).ToArray(),
                },
            });
            ids.Add(assessmentId);
        }
        _assessments.RecordBatch(records);
        return ids;
    }

    private static IReadOnlyDictionary<string, string> ReadProjectStandings(LcmCache? cache,
        IReadOnlyList<string> words)
    {
        if (cache is null) throw new InvalidOperationException("A Trial requires its scratch project to be open.");
        var approved = ApprovedMorphologyReader.Read(cache);
        var candidates = ApprovedMorphologyReader.ReadCandidates(cache);
        var rejected = ApprovedMorphologyReader.ReadDisapproved(cache);
        var incorrect = ApprovedMorphologyReader.ReadIncorrectSpellings(cache)
            .Select(word => word.Normalize(System.Text.NormalizationForm.FormD)).ToHashSet(StringComparer.Ordinal);
        return words.Select(word => word.Normalize(System.Text.NormalizationForm.FormD))
            .Distinct(StringComparer.Ordinal).ToDictionary(word => word,
            word => incorrect.Contains(word)
                ? ProjectStanding.IncorrectSpelling
                : approved.GetValueOrDefault(word)?.Count > 0
                    ? ProjectStanding.Approved
                    : candidates.GetValueOrDefault(word)?.Count > 0
                        ? ProjectStanding.Candidate
                        : rejected.GetValueOrDefault(word)?.Count > 0
                            ? ProjectStanding.Rejected
                            : ProjectStanding.NotPresent,
            StringComparer.Ordinal);
    }

    private static AssessmentScopeConfiguration ResolveScope(ProjectConfiguration configuration, string? requestedName)
    {
        if (requestedName is not null)
        {
            foreach (var candidate in configuration.Scopes)
            {
                if (string.Equals(candidate.Name, requestedName, StringComparison.Ordinal)) return candidate;
            }
            throw new InvalidOperationException($"No Assessment scope named '{requestedName}' is declared.");
        }

        foreach (var candidate in configuration.Scopes)
        {
            if (string.Equals(candidate.Name, AssessmentScopeConfiguration.DefaultName, StringComparison.Ordinal))
                return candidate;
        }
        return configuration.Scopes[0];
    }

    private static IReadOnlyList<AssessmentKind> ParseCollect(IReadOnlyList<string> collect)
    {
        if (collect.Count == 0) return Array.Empty<AssessmentKind>();
        var kinds = new List<AssessmentKind>(collect.Count);
        foreach (var name in collect)
        {
            if (!Enum.TryParse<AssessmentKind>(name, ignoreCase: true, out var kind))
                throw new InvalidOperationException($"'{name}' does not name a known Assessment kind.");
            kinds.Add(kind);
        }
        return kinds;
    }

    private static string AllocateScratchRoot(string jobId)
    {
        var path = TrialScratchPath.ForJob(jobId);
        TryDeleteDirectory(path);
        Directory.CreateDirectory(path);
        return path;
    }

    // Best effort: a leaked temp directory must not fail a Trial that already succeeded or already failed.
    private static void TryDeleteDirectory(string path)
    {
        try { Directory.Delete(path, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private sealed record TrialCompletion(
        [property: JsonPropertyName("baselineToken")] BaselineToken BaselineToken,
        [property: JsonPropertyName("assessmentIds")] IReadOnlyList<string> AssessmentIds,
        [property: JsonPropertyName("allWords")] bool AllWords,
        [property: JsonPropertyName("grammarCheckWarning")] string? GrammarCheckWarning = null);
}
