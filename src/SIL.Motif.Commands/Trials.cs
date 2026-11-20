using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Commands.Review;
using SIL.Motif.Commands.Assess;
using SIL.Motif.Commands.Store;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Canonicalization;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Review;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Host.Parser;
using SIL.Motif.Host.Store;
using SIL.Motif.Model.DryRun;
using SIL.Motif.Projection;
using SIL.Motif.Host.Parsimony;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Worker.Parsimony;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Commands;

/// <summary>Reads the most recent stored Trial evidence for a Proposal.</summary>
public static class Trials
{
    /// <summary>
    /// Reads the newest Trial job for <paramref name="proposalId"/> across all job states, or null if none is recorded.
    /// </summary>
    public static TrialRecord? LatestFor(MotifDatabase database, ProjectLocator project, string proposalId,
        string? parserPath = null) =>
        ReadLatest(database, project, proposalId, includeWordResults: false, parserPath);

    /// <summary>
    /// Reads the newest Trial that measured the Proposal's full base sample, skipping working-set Trials, for the
    /// summaries that stand in for a gate; null when only working-set Trials exist.
    /// </summary>
    public static TrialRecord? LatestStratifiedFor(MotifDatabase database, ProjectLocator project, string proposalId,
        string? parserPath = null)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(project);
        var id = CanonicalId.Parse(proposalId);
        var context = CreateReadContext(database, project, parserPath);
        var selected = IdentifyJobs(context.Jobs, id.Value, requireIdentity: true)
            .FirstOrDefault(item => !TrialSampleKind.IsWorkingSet(TrialJobInput.Parse(item.Job.InputJson).Sample));
        return selected is null ? null : ReadSelected(selected, context, includeWordResults: false);
    }

    /// <summary>Reads the latest Trial for a Proposal, returning a successful null when none exists.</summary>
    public static CommandOutcome<TrialRecord?> Read(ReadTrialRequest request, string? parserPath = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ProjectStoreCommand.ReadExisting(request.ProjectPath, CommandOutcome<TrialRecord?>.Success(null),
            (database, project) =>
            CommandOutcome<TrialRecord?>.Success(ReadLatest(database, project, request.ProposalId,
                request.IncludeWordResults, parserPath)));
    }

    /// <summary>
    /// Reads every Proposal's chosen Trial in one pass over the project's Trial jobs, keyed by Proposal id. A
    /// Proposal with no readable Trial is absent; <see cref="TrialStanding.Of"/> reads that as not tried.
    /// </summary>
    public static IReadOnlyDictionary<string, TrialRecord> LatestForAll(MotifDatabase database,
        ProjectLocator project, string? parserPath = null)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(project);
        var context = CreateReadContext(database, project, parserPath);
        var latest = new Dictionary<string, TrialRecord>(StringComparer.Ordinal);
        foreach (var group in IdentifyJobs(context.Jobs, proposalId: null, requireIdentity: true).GroupBy(candidate => candidate.ProposalId))
        {
            context.LoadProposals();
            // A Proposal whose own content cannot be read has no standing; the Proposals listing reports it.
            try { _ = context.CurrentIntentDigest(group.Key); }
            catch (Exception exception) when (IsUnreadableStoredProposal(exception)) { continue; }
            if (ReadSelected(group.First(), context, includeWordResults: false) is { } record)
                latest[record.ProposalId] = record;
        }
        return latest;
    }

    /// <summary>Lists this Proposal's Trials newest first.</summary>
    public static IReadOnlyList<TrialRecord> ListFor(MotifDatabase database, ProjectLocator project,
        string proposalId, string? parserPath = null)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(project);
        var id = CanonicalId.Parse(proposalId);
        var context = CreateReadContext(database, project, parserPath);
        var identified = IdentifyJobs(context.Jobs, id.Value, requireIdentity: true).ToArray();
        return identified.Select(item => ReadCandidate(item.Job)).OfType<TrialCandidate>()
            .Select(candidate => TryBuildRecord(candidate, context, includeWordResults: false, identified[0].Job))
            .Where(record => record is not null)
            .Cast<TrialRecord>()
            .ToArray();
    }

    /// <summary>
    /// The dropped-grammar result of each completed Trial of this Proposal, keyed by every Assessment id the Trial
    /// recorded: a not-checked result when the Trial recorded none. Reads only the Trial jobs' stored results, so
    /// a Review refresh does not rebuild every Trial.
    /// </summary>
    internal static IReadOnlyDictionary<string, DroppedGrammar> DroppedGrammarByAssessment(MotifDatabase database,
        ProjectLocator project, CanonicalId proposalId)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(project);
        var byAssessment = new Dictionary<string, DroppedGrammar>(StringComparer.Ordinal);
        foreach (var candidate in ReadCandidates(OrderedJobs(database, ProjectWorkspaceKey.Compute(project)),
            proposalId.Value))
        {
            if (candidate.Job.Status != JobStatus.Completed) continue;
            TrialCompletion? completion;
            try { completion = ReadCompletion(candidate.Job.ResultJson); }
            catch (Exception exception) when (exception is JsonException or InvalidDataException) { continue; }
            if (completion is null) continue;
            var dropped = completion.DroppedGrammar
                ?? DroppedGrammar.NotChecked("The Trial recorded no dropped-grammar check.");
            foreach (var assessmentId in completion.AssessmentIds) byAssessment.TryAdd(assessmentId, dropped);
        }
        return byAssessment;
    }

    /// <summary>
    /// The Assessment ids that working-set Trials of this Proposal recorded: they measured only a chosen word list,
    /// so none of them can stand as the Proposal's evidence for Apply.
    /// </summary>
    internal static IReadOnlySet<string> WorkingSetAssessmentIds(MotifDatabase database, ProjectLocator project,
        CanonicalId proposalId)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var candidate in ReadCandidates(OrderedJobs(database, ProjectWorkspaceKey.Compute(project)),
            proposalId.Value))
        {
            if (!TrialSampleKind.IsWorkingSet(candidate.Input.Sample)) continue;
            try { ids.UnionWith(ReadCompletion(candidate.Job.ResultJson)?.AssessmentIds ?? []); }
            catch (Exception exception) when (exception is JsonException or InvalidDataException) { }
        }
        return ids;
    }

    /// <summary>The working-set refusal for a read Trial, or null when it is an ordinary Trial.</summary>
    internal static Refusal? WorkingSetRefusal(TrialRecord trial, string use) =>
        TrialSampleKind.IsWorkingSet(trial.Sample) ? WorkingSetNotAGate(use) : null;

    private static Refusal WorkingSetNotAGate(string use) =>
        new(RefusalCodes.TrialWorkingSetNotAGate, FailureReason.Refused,
            "That Trial measured only a chosen word list (a working-set Trial), so it cannot be used to " + use +
            ". Run a Trial without --no-base-sample.");

    /// <summary>The working-set refusal for a Trial job, or null when it is an ordinary Trial.</summary>
    internal static Refusal? WorkingSetRefusal(JobRecord job, string use) =>
        TrialSampleKind.IsWorkingSet(TrialJobInput.Parse(job.InputJson).Sample) ? WorkingSetNotAGate(use) : null;

    /// <summary>Every Trial job that names this Proposal, running or not, so deleting the Proposal removes them.</summary>
    internal static IReadOnlyList<JobRecord> JobsFor(MotifDatabase database, ProjectLocator project,
        CanonicalId proposalId) =>
        ReadCandidates(new JobRepository(database).ListByProjectAndKind(ProjectWorkspaceKey.Compute(project),
            TrialJobHandler.TrialKind), proposalId.Value).Select(candidate => candidate.Job).ToArray();

    private static TrialRecord? ReadLatest(MotifDatabase database, ProjectLocator project, string proposalId,
        bool includeWordResults, string? parserPath)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(project);
        var id = CanonicalId.Parse(proposalId);
        var context = CreateReadContext(database, project, parserPath);
        var newest = IdentifyJobs(context.Jobs, id.Value, requireIdentity: true).FirstOrDefault();
        return newest is null ? null : ReadSelected(newest, context, includeWordResults);
    }

    private static TrialRecord? ReadSelected(IdentifiedTrial selected, TrialReadContext context, bool includeWordResults)
    {
        var candidate = ReadCandidate(selected.Job);
        var record = candidate is null ? null : TryBuildRecord(candidate, context, includeWordResults, selected.Job);
        if (record is null)
            System.Diagnostics.Trace.TraceWarning("The newest Trial '{0}' of a Proposal could not be read; it reads as not tried.",
                selected.Job.JobId);
        return record;
    }

    private static TrialReadContext CreateReadContext(MotifDatabase database, ProjectLocator project, string? parserPath = null)
    {
        OperationRegistryBootstrap.Initialize();
        var workspaceKey = ProjectWorkspaceKey.Compute(project);
        var proposals = new ProposalRepository(database);
        return new TrialReadContext(database,
            () => new BaselineRepository(database).GetCurrentEvidence(workspaceKey), proposals,
            new AssessmentRepository(database), OrderedJobs(database, workspaceKey), parserPath);
    }

    private static IReadOnlyList<JobRecord> OrderedJobs(MotifDatabase database, string projectKey) =>
        new JobRepository(database).ListByProjectAndKind(projectKey, TrialJobHandler.TrialKind)
            .OrderByDescending(job => DateTimeOffset.Parse(job.CreatedUtc, CultureInfo.InvariantCulture))
            .ThenByDescending(job => job.JobId, StringComparer.Ordinal).ToArray();

    internal static string ProposalOf(JobRecord job) =>
        IdentifyJobs([job], proposalId: null, requireIdentity: true).Single().ProposalId.Value;

    internal static JobRecord? SelectJob(MotifDatabase database, string projectKey, string proposalId, string? jobId = null) =>
        IdentifyJobs(OrderedJobs(database, projectKey).Where(job => jobId is null || job.JobId == jobId),
            proposalId, requireIdentity: true).FirstOrDefault()?.Job;

    private static IEnumerable<IdentifiedTrial> IdentifyJobs(IEnumerable<JobRecord> jobs, string? proposalId,
        bool requireIdentity = false)
    {
        foreach (var job in jobs)
        {
            CanonicalId id;
            try
            {
                using var input = JsonDocument.Parse(job.InputJson);
                if (input.RootElement.ValueKind != JsonValueKind.Object ||
                    !input.RootElement.TryGetProperty("proposalJson", out var document) ||
                    document.ValueKind != JsonValueKind.String)
                    throw new InvalidDataException("The stored Trial has no Proposal document.");
                using var proposal = JsonDocument.Parse(document.GetString()!);
                if (proposal.RootElement.ValueKind != JsonValueKind.Object ||
                    !proposal.RootElement.TryGetProperty("proposalId", out var identity) ||
                    identity.ValueKind != JsonValueKind.String || !CanonicalId.TryParse(identity.GetString(), out id))
                    throw new InvalidDataException("The stored Trial has no readable Proposal identity.");
            }
            catch (Exception exception) when (!requireIdentity && exception is (JsonException or InvalidDataException))
            {
                continue;
            }
            if (proposalId is null || id.Value == proposalId) yield return new IdentifiedTrial(job, id);
        }
    }

    private static IEnumerable<TrialCandidate> ReadCandidates(IEnumerable<JobRecord> jobs, string? proposalId) =>
        IdentifyJobs(jobs, proposalId).Select(job => ReadCandidate(job.Job)).OfType<TrialCandidate>();

    private static TrialCandidate? ReadCandidate(JobRecord job)
    {
        try
        {
            var input = TrialJobInput.Parse(job.InputJson);
            return new TrialCandidate(job, input, ProposalJsonParser.Parse(input.ProposalJson));
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or ArgumentException or ContractParseException)
        {
            return null;
        }
    }

    private static TrialRecord? TryBuildRecord(TrialCandidate candidate, TrialReadContext context,
        bool includeWordResults, JobRecord newest)
    {
        // Outside the catch below: a damaged stored Draft must not read as a Proposal never tried.
        var currentIntentDigest = context.CurrentIntentDigest(candidate.Proposal.ProposalId);
        try
        {
            return BuildRecord(candidate.Job, candidate.Input, candidate.Proposal, context,
                currentIntentDigest, includeWordResults, newest);
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or KeyNotFoundException)
        {
            return null;
        }
    }

    private static TrialRecord? BuildRecord(JobRecord job, TrialJobInput input, Contract.Model.Proposal proposal,
        TrialReadContext context, string? currentIntentDigest, bool includeWordResults, JobRecord newest)
    {
        var current = context.Current;
        var database = context.Database;
        var assessments = context.Assessments;
        var completion = ReadCompletion(job.ResultJson);
        var sourceBaseline = completion?.BaselineToken ?? current?.Baseline.Token;
        if (sourceBaseline is null) return null;

        var completed = job.Status == JobStatus.Completed && completion is not null;
        var proposalId = proposal.ProposalId.Value;
        var intentDigest = IntentDigest.Compute(proposal);

        var rows = completion is null
            ? []
            : assessments.ListByProposalAndDigest(proposal.ProposalId, intentDigest)
                .Where(record => completion.AssessmentIds.Contains(record.AssessmentId, StringComparer.Ordinal))
                .ToArray();
        if (completion is not null &&
            !completion.AssessmentIds.Order(StringComparer.Ordinal)
                .SequenceEqual(rows.Select(record => record.AssessmentId).Order(StringComparer.Ordinal),
                    StringComparer.Ordinal))
            throw new InvalidDataException("A Trial result names an Assessment that its Proposal revision does not own.");
        var assessmentIds = rows.GroupBy(record => record.Kind, StringComparer.Ordinal)
            .ToDictionary(group => group.Key,
                group => group.OrderBy(record => record.SavedUtc, StringComparer.Ordinal)
                    .ThenBy(record => record.AssessmentId, StringComparer.Ordinal).Last().AssessmentId,
                StringComparer.Ordinal);
        // A Trial queued before its words were normalized may hold them as typed.
        var words = input.Words?.Select(word => word.Normalize(System.Text.NormalizationForm.FormD))
                .Distinct(StringComparer.Ordinal).ToArray()
            ?? rows.SelectMany(record => record.Selection.Words).Distinct(StringComparer.Ordinal).ToArray();
        var dryRunResult = job.DryRunJson is null ? null : JobCommands.ParsePublishedDryRun(job.DryRunJson);
        var dryRun = dryRunResult is null ? null : DryRunProjectionBuilder.Build(proposalId, dryRunResult, proposal)
            with
            {
                EvidenceCurrent = completion is not null && JobCommands.IsDryRunEvidenceCurrent(database,
                    job.ProjectKey, currentIntentDigest, dryRunResult.IntentDigest, completion.BaselineToken),
            };
        var freshness = !completed
            ? TrialFreshness.Incomplete
            : ToTrialFreshness(context.Review.EvidenceFreshness(job, proposalId, newest, context.ReadIntentDigest,
                () => current?.Baseline.Token));
        DateTimeOffset? completedUtc = JobStateMachine.IsTerminal(job.Status)
            ? DateTimeOffset.Parse(job.UpdatedUtc, System.Globalization.CultureInfo.InvariantCulture)
            : null;

        var tokenJson = JsonSerializer.Serialize(sourceBaseline, MotifJson.CreateOptions());
        var rootAssessmentId = assessmentIds.GetValueOrDefault(AssessmentKinds.ParseTime);
        var wordResults = !includeWordResults || rootAssessmentId is null
            ? []
            : assessments.GetSelected(rootAssessmentId).Words?.Select(AssessmentWordRows.FromRecorded).ToArray() ?? [];
        var trialAssessment = rootAssessmentId is null ? null : assessments.GetSelected(rootAssessmentId);
        var baselineAssessment = (input.BaselineAssessmentIds ?? []).Select(assessments.GetSelected)
            .FirstOrDefault(record => record.Kind == AssessmentKinds.ParseTime && trialAssessment is not null &&
                record.Assessor == trialAssessment.Assessor && Assess.BeforeAssessmentReuse.MatchesBaseline(database, record, trialAssessment));
        var work = trialAssessment is not null
            ? TrialWorkComparison.Compare(baselineAssessment, trialAssessment, baselineAssessment is not null) : null;
        var readingIdentity = completed && trialAssessment is not null && ComposesFeatureRemoval(proposal)
            ? ProveFeatureRemoval(baselineAssessment, trialAssessment) : null;
        var grammarCheckId = new GrammarCheckRepository(database).GetLatest(tokenJson, proposalId, intentDigest) is null
            ? null
            : "trial:" + proposalId + ":" + intentDigest;

        string? label;
        try { label = context.Proposals.Get(proposal.ProposalId).Label; }
        catch (KeyNotFoundException) { label = null; }

        var allWords = completion?.AllWords ?? input.AllWords;
        return new TrialRecord(job.JobId, proposalId, intentDigest, sourceBaseline, dryRun, assessmentIds,
            words, allWords, input.Limits, job.Status, completedUtc, freshness)
        {
            WordScope = allWords ? TrialWordScope.AllWords
                : input.Words is null && input.WordList is null ? TrialWordScope.DefaultSelection
                    : TrialWordScope.ChosenWords,
            Label = label,
            Sample = TrialSampleKind.IsWorkingSet(input.Sample) ? TrialSampleKind.WorkingSet : TrialSampleKind.Stratified,
            SampleWords = input.SampleWords,
            WordListDigest = input.WordListDigest,
            GrammarCheckId = grammarCheckId,
            WordResults = wordResults,
            Work = work,
            ReadingIdentity = readingIdentity,
            GrammarCheckWarning = completion?.GrammarCheckWarning,
            DroppedGrammar = completion?.DroppedGrammar,
            DroppedGrammarNotChecked = completed && completion?.DroppedGrammar is not { Checked: true },
        };
    }

    private static bool ComposesFeatureRemoval(Contract.Model.Proposal proposal) =>
        proposal.Operations.Any(operation => operation.Rationale == RemoveExceptionFeatureComposer.Rationale);

    // The Trial's words are the affected words: removing a rule exclusion can change any word the rule reads.
    private static ReadingIdentityResult ProveFeatureRemoval(AssessmentRecord? before, AssessmentRecord after) =>
        before is null
            ? new ReadingIdentityResult(ReadingIdentityStatus.Incomplete, after.Selection.Words.Count, 0, 0, 0, 0, [], [],
                ["The Trial has no matching before Assessment to compare readings with."])
            : FeatureRemovalReadingProof.Prove(RecipeVerificationTrialProjection.From(before),
                RecipeVerificationTrialProjection.From(after), after.Selection.Words);

    private static TrialFreshness ToTrialFreshness(Freshness freshness) =>
        freshness.Relation switch
        {
            FreshnessRelation.Current => TrialFreshness.Current,
            FreshnessRelation.Historical when freshness.Reasons.Contains("baseline-changed") => TrialFreshness.BaselineMoved,
            FreshnessRelation.Historical when freshness.Reasons.Contains("proposal-changed") => TrialFreshness.ProposalChanged,
            FreshnessRelation.Historical when freshness.Reasons.Contains("parser-changed") => TrialFreshness.ParserChanged,
            FreshnessRelation.Historical => TrialFreshness.Historical,
            _ => TrialFreshness.Unknown,
        };

    /// <summary>Whether a failure came from stored Proposal or Draft content that cannot be parsed.</summary>
    internal static bool IsUnreadableStoredProposal(Exception exception) =>
        exception is ContractParseException or JsonException or InvalidDataException;

    internal static string? ReadCurrentIntentDigest(ProposalRecord? record)
    {
        if (record is null) return null;
        if (record.ProposalJson is null) return record.DraftName is null ? record.IntentDigest : null;
        return IntentDigest.Compute(ProposalJsonParser.Parse(JobCommands.CanonicalProposalJson(record)));
    }

    private static TrialCompletion? ReadCompletion(string? resultJson)
    {
        if (resultJson is null) return null;
        using var document = JsonDocument.Parse(resultJson);
        var root = document.RootElement;
        if (!root.TryGetProperty("baselineToken", out _) || !root.TryGetProperty("assessmentIds", out _))
            return null;
        return JsonSerializer.Deserialize<TrialCompletion>(resultJson, MotifJson.CreateOptions())
            ?? throw new InvalidDataException("A completed Trial result is incomplete.");
    }

    private sealed record TrialCompletion(
        [property: JsonPropertyName("baselineToken")] BaselineToken BaselineToken,
        [property: JsonPropertyName("assessmentIds")] IReadOnlyList<string> AssessmentIds,
        [property: JsonPropertyName("allWords")] bool AllWords,
        [property: JsonPropertyName("grammarCheckWarning")] string? GrammarCheckWarning = null,
        [property: JsonPropertyName("droppedGrammar")] DroppedGrammar? DroppedGrammar = null,
        [property: JsonPropertyName("reviewAfter")] IReadOnlyList<ReviewAfterPartition>? ReviewAfter = null);

    private sealed record IdentifiedTrial(JobRecord Job, CanonicalId ProposalId);

    private sealed record TrialCandidate(JobRecord Job, TrialJobInput Input, Contract.Model.Proposal Proposal);

    // Each Proposal's current content is digested at most once per read, from one listing of the Proposals.
    private sealed class TrialReadContext(MotifDatabase database, Func<CurrentBaselineEvidence?> current,
        ProposalRepository proposals, AssessmentRepository assessments, IReadOnlyList<JobRecord> jobs, string? parserPath)
    {
        private Dictionary<CanonicalId, ProposalRecord>? _records;
        private readonly Dictionary<CanonicalId, string?> _digests = [];

        public MotifDatabase Database { get; } = database;
        public TrialReviewCommands Review { get; } = new(database, parserPath);
        // A project with no Trial job never needs the Baseline summary, so it is read on first use.
        private readonly Lazy<CurrentBaselineEvidence?> _current = new(current);

        public CurrentBaselineEvidence? Current => _current.Value;
        public ProposalRepository Proposals { get; } = proposals;
        public AssessmentRepository Assessments { get; } = assessments;
        public IReadOnlyList<JobRecord> Jobs { get; } = jobs;

        public void LoadProposals() => _records ??= Proposals.List(new ProposalListFilter(IncludeArchived: true))
            .GroupBy(record => record.ProposalId).ToDictionary(group => group.Key, group => group.First());

        public string? ReadIntentDigest(string proposalId)
        {
            try { return CurrentIntentDigest(CanonicalId.Parse(proposalId)); }
            catch (Exception exception) when (IsUnreadableStoredProposal(exception) || exception is ArgumentException)
            {
                return null;
            }
        }

        public string? CurrentIntentDigest(CanonicalId proposalId)
        {
            if (_digests.TryGetValue(proposalId, out var known)) return known;
            LoadProposals();
            return _digests[proposalId] = ReadCurrentIntentDigest(_records!.GetValueOrDefault(proposalId));
        }
    }
}
