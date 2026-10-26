using System.Text.Json;
using System.Text.Json.Serialization;
using SIL.Motif.Commands.Queries;
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
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Host.Parser;
using SIL.Motif.Host.Store;
using SIL.Motif.Model.DryRun;
using SIL.Motif.Projection;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Commands;

/// <summary>Reads the most recent stored Trial evidence for a Proposal.</summary>
public static class Trials
{
    private static readonly JsonSerializerOptions DraftJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Reads the chosen Trial for <paramref name="proposalId"/>: the newest completed or active one, else the newest
    /// that stopped; null when none is recorded.
    /// </summary>
    public static TrialRecord? LatestFor(MotifDatabase database, ProjectLocator project, string proposalId) =>
        ReadLatest(database, project, proposalId, includeWordResults: false);

    /// <summary>Reads the latest Trial for a Proposal, returning a successful null when none exists.</summary>
    public static CommandOutcome<TrialRecord?> Read(ReadTrialRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ProjectStoreCommand.Run<TrialRecord?>(request.ProjectPath, MotifProductVersion.CurrentText,
            (database, project) =>
            CommandOutcome<TrialRecord?>.Success(ReadLatest(database, project, request.ProposalId,
                request.IncludeWordResults)));
    }

    /// <summary>
    /// Reads every Proposal's chosen Trial in one pass over the project's Trial jobs, keyed by Proposal id. A
    /// Proposal with no readable Trial is absent; <see cref="TrialStanding.Of"/> reads that as not tried.
    /// </summary>
    public static IReadOnlyDictionary<string, TrialRecord> LatestForAll(MotifDatabase database,
        ProjectLocator project)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(project);
        var context = CreateReadContext(database, project);
        var latest = new Dictionary<string, TrialRecord>(StringComparer.Ordinal);
        foreach (var group in ReadCandidates(context.Jobs, proposalId: null)
            .GroupBy(candidate => candidate.Proposal.ProposalId))
        {
            if (Choose(group, context, includeWordResults: false) is { } record)
                latest[record.ProposalId] = record;
        }
        return latest;
    }

    /// <summary>Lists this Proposal's Trials newest first.</summary>
    public static IReadOnlyList<TrialRecord> ListFor(MotifDatabase database, ProjectLocator project,
        string proposalId)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(project);
        var id = CanonicalId.Parse(proposalId);
        var context = CreateReadContext(database, project);
        return ReadCandidates(context.Jobs, id)
            .Select(candidate => TryBuildRecord(candidate, context, includeWordResults: false))
            .Where(record => record is not null)
            .Cast<TrialRecord>()
            .ToArray();
    }

    /// <summary>Every Trial job that names this Proposal, running or not, so deleting the Proposal removes them.</summary>
    internal static IReadOnlyList<JobRecord> JobsFor(MotifDatabase database, ProjectLocator project,
        CanonicalId proposalId) =>
        ReadCandidates(new JobRepository(database).ListByProjectAndKind(ProjectWorkspaceKey.Compute(project),
            TrialJobHandler.TrialKind), proposalId).Select(candidate => candidate.Job).ToArray();

    private static TrialRecord? ReadLatest(MotifDatabase database, ProjectLocator project, string proposalId,
        bool includeWordResults)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(project);
        var id = CanonicalId.Parse(proposalId);
        var context = CreateReadContext(database, project);
        return Choose(ReadCandidates(context.Jobs, id), context, includeWordResults);
    }

    // The one Trial-selection rule: the newest completed or active Trial, else the newest that stopped.
    private static TrialRecord? Choose(IEnumerable<TrialCandidate> newestFirst, TrialReadContext context,
        bool includeWordResults)
    {
        var candidates = newestFirst.ToArray();
        var preferred = candidates.Where(candidate => candidate.Job.Status == JobStatus.Completed ||
                !JobStateMachine.IsTerminal(candidate.Job.Status))
            .Concat(candidates.Where(candidate => candidate.Job.Status != JobStatus.Completed &&
                JobStateMachine.IsTerminal(candidate.Job.Status)));
        foreach (var candidate in preferred)
        {
            var record = TryBuildRecord(candidate, context, includeWordResults);
            if (record is not null) return record;
        }
        return null;
    }

    private static TrialReadContext CreateReadContext(MotifDatabase database, ProjectLocator project)
    {
        OperationRegistryBootstrap.Initialize();
        var workspaceKey = ProjectWorkspaceKey.Compute(project);
        var proposals = new ProposalRepository(database);
        return new TrialReadContext(database,
            () => new BaselineRepository(database).GetCurrentEvidence(workspaceKey), proposals,
            new AssessmentRepository(database), new JobRepository(database)
                .ListByProjectAndKind(workspaceKey, TrialJobHandler.TrialKind)
                .OrderByDescending(job => job.CreatedUtc, StringComparer.Ordinal)
                .ThenByDescending(job => job.JobId, StringComparer.Ordinal).ToArray());
    }

    private static IEnumerable<TrialCandidate> ReadCandidates(IReadOnlyList<JobRecord> jobs,
        CanonicalId? proposalId)
    {
        foreach (var job in jobs)
        {
            TrialCandidate? candidate;
            try
            {
                var (readJob, input, proposal) = ReadInput(job);
                candidate = proposalId is null || proposal.ProposalId == proposalId
                    ? new TrialCandidate(readJob, input, proposal)
                    : null;
            }
            catch (Exception exception) when (exception is JsonException or InvalidDataException or ArgumentException)
            {
                candidate = null;
            }
            if (candidate is not null) yield return candidate;
        }
    }

    private static TrialRecord? TryBuildRecord(TrialCandidate candidate, TrialReadContext context,
        bool includeWordResults)
    {
        // Outside the catch below: a damaged stored Draft must not read as a Proposal never tried.
        var currentIntentDigest = context.CurrentIntentDigest(candidate.Proposal.ProposalId);
        try
        {
            return BuildRecord(candidate.Job, candidate.Input, candidate.Proposal, context.Current,
                context.Database, context.Proposals, context.Assessments,
                currentIntentDigest, includeWordResults);
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or KeyNotFoundException)
        {
            return null;
        }
    }

    private static (JobRecord Job, TrialJobInput Input, Contract.Model.Proposal Proposal) ReadInput(JobRecord job)
    {
        var input = TrialJobInput.Parse(job.InputJson);
        return (job, input, ProposalJsonParser.Parse(input.ProposalJson));
    }

    private static TrialRecord? BuildRecord(JobRecord job, TrialJobInput input, Contract.Model.Proposal proposal,
        CurrentBaselineEvidence? current, MotifDatabase database, ProposalRepository proposals,
        AssessmentRepository assessments, string? currentIntentDigest, bool includeWordResults)
    {
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
        var words = input.Words ?? rows.SelectMany(record => record.Selection.Words)
            .Distinct(StringComparer.Ordinal).ToArray();
        var dryRunResult = job.DryRunJson is null ? null : JobCommands.ParsePublishedDryRun(job.DryRunJson);
        var dryRun = dryRunResult is null ? null : DryRunProjectionBuilder.Build(proposalId, dryRunResult, proposal)
            with
            {
                EvidenceCurrent = completion is not null && JobCommands.IsDryRunEvidenceCurrent(database,
                    job.ProjectKey, currentIntentDigest, dryRunResult.IntentDigest, completion.BaselineToken),
            };
        var freshness = !completed
            ? TrialFreshness.Incomplete
            : GetFreshness(sourceBaseline, intentDigest, current, currentIntentDigest);
        DateTimeOffset? completedUtc = JobStateMachine.IsTerminal(job.Status)
            ? DateTimeOffset.Parse(job.UpdatedUtc, System.Globalization.CultureInfo.InvariantCulture)
            : null;

        var tokenJson = JsonSerializer.Serialize(sourceBaseline, MotifJson.CreateOptions());
        var rootAssessmentId = assessmentIds.GetValueOrDefault(AssessmentKinds.ParseTime);
        var wordResults = !includeWordResults || rootAssessmentId is null
            ? []
            : assessments.Get(rootAssessmentId).Words?.Select(AssessmentWordRows.FromRecorded).ToArray() ?? [];
        var grammarCheckId = new GrammarCheckRepository(database).GetLatest(tokenJson, proposalId, intentDigest) is null
            ? null
            : "trial:" + proposalId + ":" + intentDigest;

        string? label;
        try { label = proposals.Get(proposal.ProposalId).Label; }
        catch (KeyNotFoundException) { label = null; }

        var allWords = completion?.AllWords ?? input.AllWords;
        return new TrialRecord(job.JobId, proposalId, intentDigest, sourceBaseline, dryRun, assessmentIds,
            words, allWords, input.Limits, job.Status, completedUtc, freshness)
        {
            WordScope = allWords ? TrialWordScope.AllWords
                : input.Words is null && input.WordList is null ? TrialWordScope.DefaultSelection
                    : TrialWordScope.ChosenWords,
            Label = label,
            GrammarCheckId = grammarCheckId,
            WordResults = wordResults,
            GrammarCheckWarning = completion?.GrammarCheckWarning,
        };
    }

    private static TrialFreshness GetFreshness(BaselineToken source, string intentDigest,
        CurrentBaselineEvidence? current, string? currentIntentDigest)
    {
        if (current is null || current.Baseline.Token != source) return TrialFreshness.BaselineMoved;
        return currentIntentDigest == intentDigest ? TrialFreshness.Current : TrialFreshness.ProposalChanged;
    }

    private static string? ReadCurrentIntentDigest(ProposalRecord? record)
    {
        if (record?.ProposalJson is null) return null;
        var json = record.DraftName is null
            ? record.ProposalJson
            : ProposalCommands.BuildProposalJson(JsonSerializer.Deserialize<DraftDocument>(record.ProposalJson,
                DraftJsonOptions) ?? throw new InvalidDataException("Stored Draft content is incomplete."));
        return IntentDigest.Compute(ProposalJsonParser.Parse(json));
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
        [property: JsonPropertyName("grammarCheckWarning")] string? GrammarCheckWarning = null);

    private sealed record TrialCandidate(JobRecord Job, TrialJobInput Input, Contract.Model.Proposal Proposal);

    // Each Proposal's current content is digested at most once per read, from one listing of the Proposals.
    private sealed class TrialReadContext(MotifDatabase database, Func<CurrentBaselineEvidence?> current,
        ProposalRepository proposals, AssessmentRepository assessments, IReadOnlyList<JobRecord> jobs)
    {
        private Dictionary<CanonicalId, ProposalRecord>? _records;
        private readonly Dictionary<CanonicalId, string?> _digests = [];

        public MotifDatabase Database { get; } = database;
        // A project with no Trial job never needs the Baseline summary, so it is read on first use.
        private readonly Lazy<CurrentBaselineEvidence?> _current = new(current);

        public CurrentBaselineEvidence? Current => _current.Value;
        public ProposalRepository Proposals { get; } = proposals;
        public AssessmentRepository Assessments { get; } = assessments;
        public IReadOnlyList<JobRecord> Jobs { get; } = jobs;

        public string? CurrentIntentDigest(CanonicalId proposalId)
        {
            if (_digests.TryGetValue(proposalId, out var known)) return known;
            _records ??= Proposals.List(new ProposalListFilter(IncludeArchived: true))
                .GroupBy(record => record.ProposalId).ToDictionary(group => group.Key, group => group.First());
            return _digests[proposalId] = ReadCurrentIntentDigest(_records.GetValueOrDefault(proposalId));
        }
    }
}
