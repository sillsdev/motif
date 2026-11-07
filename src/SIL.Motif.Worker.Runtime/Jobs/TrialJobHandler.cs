using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SIL.LCModel;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Canonicalization;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Analysis;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.Config;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Parser;
using SIL.Motif.Host.Store;
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
    [property: JsonPropertyName("wordList")] JobWordList? WordList = null,
    [property: JsonPropertyName("reviewInputs")] ReviewInputReference? ReviewInputs = null,
    [property: JsonPropertyName("reviewBaselines")] IReadOnlyList<ReviewBaselinePartition>? ReviewBaselines = null,
    [property: JsonPropertyName("reviewBeforeStopCode")] string? ReviewBeforeStopCode = null,
    [property: JsonPropertyName("reviewBeforePartial")] string? ReviewBeforePartial = null)
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
    internal static readonly AsyncLocal<CancellationToken?> BudgetStopForTests = new();
    internal static readonly AsyncLocal<TimeSpan?> BudgetDurationForTests = new();
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
    private readonly Func<string, string, string, Action<bool>?, CancellationToken, Task<GrammarCheckResponse?>>? _grammarHealthWithSnapshot;
    private readonly Func<bool> _retainScratch;

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
        Func<string, string, Action<bool>?, CancellationToken, Task<GrammarCheckResponse?>>? grammarHealth = null,
        Func<bool>? retainScratch = null,
        Func<string, string, string, Action<bool>?, CancellationToken, Task<GrammarCheckResponse?>>? grammarHealthWithSnapshot = null)
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
        _grammarHealthWithSnapshot = grammarHealthWithSnapshot;
        _retainScratch = retainScratch ?? (() => TrialScratchRetention.RetainRequested);
    }

    /// <summary>Called once a Trial has committed, with the Baseline it measured; its failures are only reported.</summary>
    internal Action<ClaimedJob, BaselineToken>? AfterCommit { get; set; }

    /// <summary>How long a parser stage of a Trial may run before it is reported as not checked.</summary>
    internal TimeSpan StageTimeout { get; set; } = PanGlossInvoker.DefaultWallClockCap;

    /// <summary>Runs one claimed Trial job through to a terminal outcome, or parks it waiting for a Baseline.</summary>
    internal async Task<JobOutcome?> RunAsync(ClaimedJob claim, ProjectLocator project, CancellationToken cancellationToken)
    {
        var input = TrialJobInput.Parse(claim.Job.InputJson);
        ReviewEvidenceState? review = null;
        if (input.ReviewInputs is { } reference)
        {
            var frozen = ReviewInputArtifacts.Read(claim.Database, reference).Inputs;
            var ledger = TrialReviewEvidence.Open(claim.Database, claim.JobId, reference);
            if (input.ReviewBeforePartial is { } partial)
                ledger = ledger with { Before = TrialReviewEvidence.ReadBefore(claim.Database, partial) };
            foreach (var partition in input.ReviewBaselines ?? [])
            {
                var words = partition.AssessmentIds.SelectMany(id => _assessments.Get(id).Words ?? []);
                ledger = ledger with { Before = ledger.Before.Concat(TrialReviewEvidence.Observe(frozen, partition.CaseKeys, words))
                    .GroupBy(item => item.CaseKey, StringComparer.Ordinal).Select(group => group.Last()).ToArray() };
            }
            if (input.ReviewBeforeStopCode is { } beforeStop)
                ledger = TrialReviewEvidence.Interrupt(ledger, frozen, "before", beforeStop);
            review = new ReviewEvidenceState(claim.Database, claim.CapturedClaimToken, frozen, ledger);
            review.Save();
        }
        var seconds = new NamedSelectionRepository(claim.Database).GetDefault()?.Limits.AssessBudgetSeconds ?? SIL.Motif.Contract.Requests.SelectionParsingLimits.DefaultAssessBudgetSeconds;
        var remaining = (BudgetDurationForTests.Value ?? TimeSpan.FromSeconds(seconds)) - claim.ClaimElapsed;
        using var budget = new CancellationTokenSource();
        using var forcedBudget = BudgetStopForTests.Value?.Register(budget.Cancel);
        if (remaining <= TimeSpan.Zero) budget.Cancel();
        else budget.CancelAfter(remaining);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, budget.Token);
        var progress = new BudgetProgress { Total = review?.Inputs.Cases.Count ?? 0 };
        try
        {
            return await RunBudgetedAsync(claim, project, progress, review, linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            review?.Interrupt(cancellationToken.IsCancellationRequested ? "cancelled" : RefusalCodes.AssessmentStoppedByBudget);
            if (!budget.IsCancellationRequested || cancellationToken.IsCancellationRequested)
                return new JobOutcome(JobStatus.Cancelled, JobFailureCategory.Cancellation);
            var completed = Volatile.Read(ref progress.Done);
            var unfinished = Math.Max(0, Volatile.Read(ref progress.Total) - completed);
            var message = $"Parsing stopped at the time limit: {completed} words done, {unfinished} not done. No Assessment was saved.";
            var baseline = _baselines.GetCurrentPublished(ProjectWorkspaceKey.Compute(project));
            return new JobOutcome(JobStatus.StoppedByBudget, ResultJson: JsonSerializer.Serialize(new
            {
                code = RefusalCodes.AssessmentStoppedByBudget, message, wordsDone = completed, wordsNotDone = unfinished,
                baselineToken = baseline?.Token,
            }, MotifJson.CreateOptions()));
        }
    }

    private sealed class BudgetProgress { public int Done; public int Total; }

    private sealed class ReviewEvidenceState(MotifDatabase database, string? claimToken, Contract.Review.FrozenReviewInputs inputs,
        TrialReviewLedger ledger)
    {
        public Contract.Review.FrozenReviewInputs Inputs { get; } = inputs;
        public TrialReviewLedger Ledger { get; private set; } = ledger;
        public void Save() => TrialReviewEvidence.SaveForClaim(database, Ledger, claimToken);
        public void Add(IReadOnlyCollection<string> keys, IEnumerable<AssessedWord> words)
        {
            Ledger = Ledger with { After = Ledger.After.Concat(TrialReviewEvidence.Observe(Inputs, keys, words))
                .GroupBy(item => item.CaseKey, StringComparer.Ordinal).Select(group => group.Last()).ToArray() };
            Save();
        }
        public void Interrupt(string code)
        {
            Ledger = TrialReviewEvidence.Interrupt(Ledger, Inputs, "after", code);
            Save();
        }
    }

    private async Task<JobOutcome?> RunBudgetedAsync(ClaimedJob claim, ProjectLocator project,
        BudgetProgress progress, ReviewEvidenceState? review, CancellationToken cancellationToken)
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

        var baseline = _baselines.GetCurrentPublished(workspaceKey);
        if (baseline is null)
        {
            claim.Transition(JobStatus.WaitingForBaseline);
            return null;
        }

        var input = TrialJobInput.Parse(claim.Job.InputJson);
        var reviewArtifact = input.ReviewInputs is { } reviewInputs
            ? ReviewInputArtifacts.Read(claim.Database, reviewInputs) : null;
        if (reviewArtifact is not null && reviewArtifact.Baseline != baseline.Token)
            throw new InvalidOperationException("The Trial's captured review Baseline is no longer current.");
        progress.Total = review?.Inputs.Cases.Count ?? input.Words?.Count ?? 0;
        var proposal = ProposalJsonParser.Parse(input.ProposalJson);
        var scopeConfiguration = reviewArtifact?.Scope ?? ResolveScope(_configuration.Read(project), input.Scope);
        var assessor = _assessors.Resolve(scopeConfiguration.Assessor);
        var lane = _lanes.GetOrCreate(workspaceKey);

        // Reported while queued behind the lane; the lane's own dispatch resumes this directly into Running.
        claim.Transition(JobStatus.WaitingForBaseline);

        var scratchRoot = TrialScratchPath.ForJob(claim.Job.JobId);
        using var scratchLease = TrialScratchRetention.Lock(scratchRoot);
        while (!scratchLease.TryAcquire())
            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
        AllocateScratchRoot(claim.Job.JobId);
        CandidateRun candidate;
        var preserveScratch = _retainScratch();
        if (preserveScratch) TrialScratchRetention.MarkRetained(scratchRoot);
        try
        {
            var words = input.WordList is { } list ? JobWordLists.Read(claim.Database, list) : input.Words;
            candidate = await RunCandidateAsync(claim, lane, proposal, input.Prerequisites ?? [],
                scopeConfiguration, words, input.AllWords, input.Limits, baseline.FwDataPath,
                scratchRoot, reviewArtifact is null ? null : ReviewInputPartitions.Create(reviewArtifact.Inputs),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (!preserveScratch) TryDeleteDirectory(scratchRoot);
            var detail = JobFailureDetail.Serialize(exception);
            // A successful terminal status may not carry a failure category, so only Failed gets one here.
            return claim.Job.DryRunPublished
                ? new JobOutcome(JobStatus.CompletedWithAssessmentFailure, JobFailureCategory.None, detail)
                : new JobOutcome(JobStatus.Failed, JobFailureCategory.Unknown, detail);
        }
        catch (OperationCanceledException)
        {
            if (!preserveScratch) TryDeleteDirectory(scratchRoot);
            throw;
        }

        // The run already happened; cancellation now must still stop it from becoming durable further.
        if (cancellationToken.IsCancellationRequested)
        {
            if (!preserveScratch) TryDeleteDirectory(scratchRoot);
            cancellationToken.ThrowIfCancellationRequested();
        }

        progress.Total = candidate.Partitions.Sum(partition => partition.Scope.WordForms.Count);
        try
        {
            string? grammarCheckWarning = null;
            PanGlossImportedSnapshot? importedSnapshot = null;
            DroppedGrammar droppedGrammar;
            if (_grammarHealth is not null)
            {
                var grammarPhase = new JobStageProgress("grammar-health", 1, 2, "grammar-health");
                claim.PublishStageProgress(grammarPhase);
                var grammarWait = claim.ObserveParserWait(grammarPhase);
                var snapshotPath = Path.Combine(scratchRoot, "grammar-snapshot.json");
                async Task<GrammarCheckResponse?> Check(string path, string name, Action<bool>? wait, CancellationToken token)
                {
                    if (_grammarHealthWithSnapshot is null || path != candidate.GrammarPath || assessor is not PanGlossAssessor)
                        return await _grammarHealth(path, name, wait, token).ConfigureAwait(false);
                    var response = await _grammarHealthWithSnapshot(path, name, snapshotPath, wait, token).ConfigureAwait(false);
                    if (response is not null) importedSnapshot = PanGlossImportedSnapshot.Capture(path, snapshotPath)
                        with { TransferOwnership = !preserveScratch };
                    return response;
                }
                var (grammarCheck, dropped) = await DroppedGrammarCheck.CheckAsync(Check, claim.Database,
                    baseline.Token, baseline.FwDataPath, candidate.GrammarPath, project.FieldWorksProjectIdentity,
                    proposal, grammarWait.Observe, StageTimeout, cancellationToken).ConfigureAwait(false);
                droppedGrammar = dropped;
                if (grammarCheck is not null)
                    new TrialGrammarCheckRepository(claim.Database).Save(baseline.Token,
                        proposal.ProposalId, IntentDigest.Compute(proposal), candidate.Selection.Sha256,
                        grammarCheck, baseline.SourceLastWriteUtc);
                else
                    grammarCheckWarning = dropped.NotCheckedReason;
            }
            else
            {
                droppedGrammar = DroppedGrammar.NotChecked("No grammar check is configured for this Trial.");
            }
            var limitMs = candidate.Scope.PerWordLimit is { } limit ? (int?)limit.TotalMilliseconds : null;
            var totalWords = candidate.Partitions.Sum(partition => partition.Scope.ParsedWords.Count);
            var initialProgress = new TrialWordProgress(0, totalWords, null)
                { PerWordLimitMs = limitMs };
            claim.PublishTrialProgress(initialProgress);
            var batchPhase = new JobStageProgress("batch", _grammarHealth is null ? 1 : 2,
                _grammarHealth is null ? 1 : 2, "assessment");
            var batchWait = claim.ObserveParserWait(batchPhase);
            var artifactLeases = new List<AssessmentArtifactLease>();
            var producedGroups = new List<(CandidatePartition Partition, IReadOnlyList<ProducedAssessment> Produced)>();
            var published = false;
            var preserveArtifacts = false;
            try
            {
                var completedEarlier = 0;
                foreach (var partition in candidate.Partitions)
                {
                    void WordProgress(TrialWordProgress wordProgress)
                    {
                        var completed = completedEarlier + wordProgress.Completed;
                        Interlocked.Exchange(ref progress.Done, completed);
                        claim.PublishTrialProgress(wordProgress with { Completed = completed, Total = totalWords, PerWordLimitMs = limitMs });
                    }
                    var produced = assessor is PanGlossAssessor panGloss
                        ? await panGloss.ProduceAsync(partition.Scope, candidate.ExportedDirectory, WordProgress,
                            batchWait.Observe, cancellationToken, importedSnapshot, rows =>
                            {
                                if (review is null) return;
                                review.Add(partition.CaseKeys, rows.Select(row => new AssessedWord(row.Word,
                                    row.InvalidShape ? "skipped" : row.TimedOut ? "timed-out" : row.Capped ? "stopped" :
                                    row.Analyses.Count > 0 || row.Unavailable.Count > 0 ? "analysed" : "no-analysis", [])
                                    { Morphology = row }));
                            }).ConfigureAwait(false)
                        : await assessor.ProduceAsync(partition.Scope, candidate.ExportedDirectory, WordProgress,
                            batchWait.Observe, cancellationToken).ConfigureAwait(false);
                    producedGroups.Add((partition, produced));
                    if (review is not null)
                    {
                        var measured = PrepareAll(produced, proposal, candidate.DryRun, partition.Selection, partition.Scope,
                            scopeConfiguration, assessor.Name, baseline.Token, candidate.ProjectStandings);
                        review.Add(partition.CaseKeys, measured.SelectMany(record => record.Words));
                    }
                    artifactLeases.AddRange(produced.Select(item => item.ArtifactLease).OfType<AssessmentArtifactLease>()
                        .Where(lease => !artifactLeases.Contains(lease)));
                    completedEarlier += partition.Scope.ParsedWords.Count;
                }
                cancellationToken.ThrowIfCancellationRequested();
                var records = new List<NewAssessmentRecord>();
                var reviewAfter = new List<ReviewAfterPartition>();
                foreach (var group in producedGroups)
                {
                    var part = PrepareAll(group.Produced, proposal, candidate.DryRun,
                        group.Partition.Selection, group.Partition.Scope, scopeConfiguration, assessor.Name, baseline.Token,
                        candidate.ProjectStandings);
                    records.AddRange(part);
                    reviewAfter.Add(new(group.Partition.WritingSystemId, group.Partition.CaseKeys,
                        part.Select(record => record.AssessmentId).ToArray()));
                }
                var assessmentIds = records.Select(record => record.AssessmentId).ToArray();
                var completionJson = JsonSerializer.Serialize(
                    new TrialCompletion(baseline.Token, assessmentIds, input.AllWords, grammarCheckWarning,
                        droppedGrammar, reviewArtifact is null ? null : reviewAfter),
                    MotifJson.CreateOptions());
                try
                {
                    _assessments.RecordBatchForClaim(records, claim.JobId, claim.CapturedClaimToken!, completionJson);
                }
                catch (TrialPublicationOutcomeUnknownException exception)
                {
                    preserveArtifacts = true;
                    foreach (var lease in artifactLeases)
                    {
                        try { lease.Retain(); }
                        catch (Exception retainException)
                        {
                            ReportTrialMaintenanceFailure(claim.JobId, "retain uncertain Assessment artifacts", retainException);
                        }
                    }
                    ReportTrialMaintenanceFailure(claim.JobId, "confirm publication", exception);
                    ReleaseSettledTrialParseLease(parseLease, claim.JobId);
                    return new JobOutcome(JobStatus.CompletedWithAssessmentFailure, JobFailureCategory.None,
                        JsonSerializer.Serialize(new { detail = exception.Message }));
                }

                published = true;
                foreach (var lease in artifactLeases)
                {
                    try { lease.Retain(); }
                    catch (Exception exception)
                    {
                        preserveArtifacts = true;
                        ReportTrialMaintenanceFailure(claim.JobId, "retain Assessment artifacts", exception);
                    }
                }

                ReleaseSettledTrialParseLease(parseLease, claim.JobId);
                // Advisory follow-on work: whatever it does, the committed Trial stays as it is.
                try { AfterCommit?.Invoke(claim, baseline.Token); }
                catch (Exception exception) { ReportTrialMaintenanceFailure(claim.JobId, "queue Parsimony", exception); }
                return null;
            }
            finally
            {
                if (!published && !preserveArtifacts)
                {
                    foreach (var lease in artifactLeases)
                    {
                        try { lease.Dispose(); }
                        catch (Exception exception)
                        {
                            ReportTrialMaintenanceFailure(claim.JobId, "discard unpublished Assessment artifacts", exception);
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            review?.Interrupt(exception is AssessorUnavailableException { MemoryKill: not null }
                ? RefusalCodes.AssessParserMemoryLimit : "assess.unavailable");
            var detail = JobFailureDetail.Serialize(exception);
            return new JobOutcome(JobStatus.CompletedWithAssessmentFailure, JobFailureCategory.None, detail);
        }
        finally
        {
            if (!preserveScratch)
            {
                if (TrialScratchRetention.IsMarkedForDeletion(scratchRoot)) TryDeleteDirectory(scratchRoot);
                scratchLease.Release();
                try { TrialScratchRetention.PruneProject(claim.Database, workspaceKey, claim.JobId); }
                catch (Exception exception)
                {
                    ReportTrialMaintenanceFailure(claim.JobId, "prune Trial copies", exception);
                }
            }
        }
    }

    // What one attempt on a throwaway copy produces; assembled on the lane, read off it.
    private sealed record CandidatePartition(string? WritingSystemId, IReadOnlyList<string> CaseKeys,
        AssessmentScope.Words Scope, Selection Selection);

    private sealed record CandidateRun(DryRunModel DryRun, AssessmentScope.Words Scope, Selection Selection,
        string ExportedDirectory, string GrammarPath, IReadOnlyDictionary<string, string> ProjectStandings,
        IReadOnlyList<CandidatePartition> Partitions);

    private async Task<CandidateRun> RunCandidateAsync(ClaimedJob claim, ProjectLane lane,
        Contract.Model.Proposal proposal, IReadOnlyList<FrozenProposalRevision> frozenPrerequisites,
        AssessmentScopeConfiguration scopeConfiguration,
        IReadOnlyList<string>? requestedWords, bool allWords, TrialLimits? limits, string baselineFwDataPath,
        string scratchRoot, IReadOnlyList<ReviewParsePartition>? reviewPartitions, CancellationToken cancellationToken)
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
                // The parser is asked about the same NFD spelling an Assess asks about, and a Selection stores.
                IEnumerable<string> chosen = requestedWords is not null
                    ? requestedWords
                    : cache is null ? [] : WordQueryResolver.Resolve(
                        allWords ? WordQueryResolver.AllWordformsQueryText : scopeConfiguration.Query, cache);
                var words = chosen.Select(word => word.Normalize(NormalizationForm.FormD))
                    .Distinct(StringComparer.Ordinal).ToArray();
                var scope = new AssessmentScope.Words(words, ParseCollect(scopeConfiguration.Collect),
                    limits is { } selectedLimits ? selectedLimits.PerWordLimit : scopeConfiguration.PerWordLimit,
                    limits?.PerWordParserWorkLimit ?? scopeConfiguration.PerWordParserWorkLimit)
                    { Casing = cache is null ? null : LowercasePass.ForCache(cache) };
                var partitions = reviewPartitions is null || reviewPartitions.Count == 0
                    ? [new CandidatePartition(null, [], scope, Selection.Create(scopeConfiguration.Name, words))]
                    : reviewPartitions.Select(partition =>
                    {
                        var partitionScope = new AssessmentScope.Words(partition.Words, scope.Collect,
                            scope.PerWordLimit, scope.PerWordParserWorkLimit)
                        {
                            Casing = cache is null ? null : LowercasePass.ForCache(cache, partition.WritingSystemId),
                        };
                        return new CandidatePartition(partition.WritingSystemId, partition.CaseKeys,
                            partitionScope, Selection.Create(scopeConfiguration.Name, partition.Words));
                    }).ToArray();
                var projectStandings = ReadProjectStandings(cache,
                    partitions.SelectMany(partition => partition.Scope.ParsedWords).Distinct(StringComparer.Ordinal).ToArray());
                var exportedDirectory = await _prepareForAssessment(cache, laneToken).ConfigureAwait(false);
                candidate = new CandidateRun(dryRun, scope, Selection.Create(scopeConfiguration.Name, words),
                    exportedDirectory, Path.GetFullPath(cache?.ProjectId.Path
                        ?? throw new InvalidOperationException("A Trial requires a scratch project for grammar health.")),
                    projectStandings, partitions);
            }
            finally
            {
                scratch?.Dispose();
            }
        }), cancellationToken).ConfigureAwait(false);

        // The lane either runs the work item to completion or rethrows, so this is set by here.
        return candidate ?? throw new InvalidOperationException("The lane produced no candidate.");
    }

    private IReadOnlyList<NewAssessmentRecord> PrepareAll(IReadOnlyList<ProducedAssessment> produced,
        Contract.Model.Proposal proposal,
        DryRunModel dryRun, Selection selection, AssessmentScope.Words scope,
        AssessmentScopeConfiguration scopeConfiguration, string assessorName, BaselineToken baselineToken,
        IReadOnlyDictionary<string, string> projectStandings)
    {
        // The query is what the scope was told to do; the words are only what it resolved to on this run.
        var scopeJson = ScopeCodec.Write(new StoredScope.Trial(
            scopeConfiguration.Query, scope.WordForms, scope.Collect, scope.PerWordLimit, scope.PerWordParserWorkLimit));
        var scopeDigest = AssessmentMaterial.Digest(scopeJson);
        var baselineTokenJson = JsonSerializer.Serialize(baselineToken, MotifJson.CreateOptions());

        var records = new List<NewAssessmentRecord>();
        var wordStatistics = TrialWordStatistics.Read(produced, scope.ParsedWords);
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
                    TimingEvidence = item.Kind == AssessmentKind.ParseTime
                        ? new AssessmentTimingEvidence([], 0, false, 0) { StatisticsUnavailable = true } : null,
                    Rows = record.RequireWordMaterial().Rows.Select(word =>
                    {
                        word = TrialWordStatistics.Apply(word, wordStatistics);
                        return projectStandings.TryGetValue(
                            word.Word.Normalize(System.Text.NormalizationForm.FormD), out var standing)
                            ? word with { ProjectStanding = standing }
                            : word;
                    }).ToArray(),
                },
            });
        }
        return records;
    }

    private static void ReportTrialMaintenanceFailure(string jobId, string action, Exception exception)
    {
        try { Console.Error.WriteLine($"warning: Trial '{jobId}' could not {action}: {exception}"); }
        catch (Exception) { }
    }

    private static void ReleaseSettledTrialParseLease(SIL.Motif.Host.Store.ProjectParseLease parseLease,
        string jobId)
    {
        try { parseLease.Dispose(); }
        catch (Exception exception)
        {
            ReportTrialMaintenanceFailure(jobId, "release project parse admission", exception);
        }
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
        [property: JsonPropertyName("grammarCheckWarning")] string? GrammarCheckWarning = null,
        [property: JsonPropertyName("droppedGrammar")] DroppedGrammar? DroppedGrammar = null,
        [property: JsonPropertyName("reviewAfter"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        IReadOnlyList<ReviewAfterPartition>? ReviewAfter = null);
}
