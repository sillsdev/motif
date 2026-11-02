using System.Text.Json;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Parser;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Parsimony;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Commands;

/// <summary>
/// Reads whether Parsimony has measured a Proposal's candidate after its Trial. It reads stored jobs and Reports only:
/// it queues nothing, starts no parse, and has no effect on the Trial, Readiness or Apply.
/// </summary>
public static class ProposalParsimonyStatusCommand
{
    /// <summary>The reason code a status carries when the parser is not set up.</summary>
    public const string ParserMissingCode = "status-parser-missing";

    private static readonly string[] GroupOrder = ["grammar", "text"];

    /// <summary>Reads the status of one Proposal.</summary>
    /// <param name="request">The Proposal to read.</param>
    /// <param name="parserPath">The PanGloss executable this caller would run jobs with, or null when none is set up.</param>
    public static CommandOutcome<ProposalParsimonyStatus> Read(ReadProposalParsimonyStatusRequest request,
        string? parserPath)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!CanonicalId.TryParse(request.ProposalId, out var proposalId))
            return CommandOutcome<ProposalParsimonyStatus>.Refused(new Refusal(RefusalCodes.ProposalNotFound,
                FailureReason.Refused, $"'{request.ProposalId}' is not a Proposal id."));
        return ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion, (database, project) =>
            CommandOutcome<ProposalParsimonyStatus>.Success(Build(database, project, proposalId.Value,
                ParserIdentity.Read(parserPath)?.Key)));
    }

    private static ProposalParsimonyStatus Build(MotifDatabase database, ProjectLocator project,
        string proposalId, string? parserKey)
    {
        var projectKey = ProjectWorkspaceKey.Compute(project);
        var hasSelection = new NamedSelectionRepository(database).GetParsimony() is not null;
        var trial = LatestCompletedTrial(database, projectKey, proposalId);
        var job = ParsimonyAutoRun.LatestJob(database, projectKey, proposalId);
        var intentDigest = trial is null ? null : TryIntentDigest(trial);

        ProposalParsimonyStatus Make(ProposalParsimonyState state, ProposalParsimonyReason? reason,
            ProposalParsimonyStale? stale = null, ProposalParsimonyFailure? failure = null,
            IReadOnlyList<ProposalParsimonyGroup>? grammar = null) =>
            new(ProposalParsimonyStatus.CurrentSchemaVersion, proposalId, intentDigest, trial?.JobId, state, reason,
                job is null ? null : JobOf(database, job), stale, failure,
                [.. (grammar ?? [UnrunGrammar()]), TextGroup(hasSelection)]);

        if (parserKey is null)
            return Make(ProposalParsimonyState.NotSetUp, new ProposalParsimonyReason(ParserMissingCode,
                "PanGloss is not set up, so Parsimony cannot run."));
        if (trial is null)
            return Make(ProposalParsimonyState.NotYetRun, new ProposalParsimonyReason("status-no-trial",
                "Parsimony runs after the Proposal's Trial finishes, and no Trial has finished."));
        if (job is null && SkippedForTimeout(database, trial, parserKey) is { } skipped)
            return Make(ProposalParsimonyState.Failed, new ProposalParsimonyReason("status-skipped-timeout",
                "Parsimony already timed out on this grammar with this PanGloss, so it is not queued again by itself."),
                failure: new ProposalParsimonyFailure(ProposalParsimonyFailureCategory.TimedOut,
                    "PanGloss ran past its time limit on this grammar. Run `motif parsimony` to try again; a new " +
                    "Baseline or PanGloss clears this.", false, skipped.UpdatedUtc));
        if (job is null)
            return Make(ProposalParsimonyState.NotYetRun, new ProposalParsimonyReason("status-not-queued",
                "The Trial has finished and no Parsimony run exists for it yet."));

        switch (job.Status)
        {
            case JobStatus.Queued or JobStatus.WaitingForBaseline:
                return Make(ProposalParsimonyState.Queued, new ProposalParsimonyReason("status-queued",
                    "Parsimony is waiting for other work to finish."));
            case JobStatus.Running:
                return Make(ProposalParsimonyState.Running, new ProposalParsimonyReason("status-running",
                    "Parsimony is measuring the candidate."));
            case JobStatus.Completed:
                return Finished(database, job, trial, parserKey, Make);
            default:
                return Make(ProposalParsimonyState.Failed, new ProposalParsimonyReason("status-failed",
                    "The newest Parsimony run did not finish. The Trial is unaffected."), failure: FailureOf(job));
        }
    }

    private static ProposalParsimonyStatus Finished(MotifDatabase database, JobRecord job, JobRecord trial,
        string parserKey, Func<ProposalParsimonyState, ProposalParsimonyReason?, ProposalParsimonyStale?,
            ProposalParsimonyFailure?, IReadOnlyList<ProposalParsimonyGroup>?, ProposalParsimonyStatus> make)
    {
        var recorded = ParsimonyCandidateJobInputKey(job);
        var current = CurrentInputs(database, job.ProjectKey, trial, parserKey);
        var grammar = ReadGrammar(job);
        if (recorded is null || current is null)
            return make(ProposalParsimonyState.Stale, new ProposalParsimonyReason("status-freshness-unknown",
                "Whether these results are still current could not be checked."),
                new ProposalParsimonyStale([], recorded?.ToKey() ?? "", current?.ToKey() ?? ""), null, grammar);
        var reasons = Differences(recorded, current);
        if (reasons.Count > 0)
            return make(ProposalParsimonyState.Stale, new ProposalParsimonyReason("status-stale",
                "These results were measured against inputs that have since changed."),
                new ProposalParsimonyStale(reasons, recorded!.ToKey(), current!.ToKey()), null, grammar);
        return make(ProposalParsimonyState.Ready, new ProposalParsimonyReason("status-freshness-partial",
            "Checked against the Baseline, Proposal, prerequisites, PanGloss and measures. A changed human judgment " +
            "or Parsimony Selection is not checked; run again after changing either."), null, null, grammar);
    }

    private static ParsimonyAutoInputs? ParsimonyCandidateJobInputKey(JobRecord job)
    {
        try { return ParsimonyAutoInputs.FromKey(ParsimonyCandidateJobInput.Parse(job.InputJson).InputsKey); }
        catch (Exception exception) when (exception is InvalidDataException or JsonException) { return null; }
    }

    private static ParsimonyAutoInputs? CurrentInputs(MotifDatabase database, string projectKey, JobRecord trial,
        string parserKey)
    {
        var baseline = new BaselineRepository(database).GetCurrentPublished(projectKey)?.Token ??
            ParsimonyAutoRun.TrialBaseline(trial);
        if (baseline is null) return null;
        try { return ParsimonyAutoInputs.For(trial.InputJson, baseline) with { Parser = parserKey }; }
        catch (Exception exception) when (exception is InvalidDataException or JsonException or ArgumentException)
        {
            return null;
        }
    }

    internal static IReadOnlyList<ProposalParsimonyStaleReason> Differences(ParsimonyAutoInputs recorded,
        ParsimonyAutoInputs current)
    {
        var reasons = new List<ProposalParsimonyStaleReason>();
        if (recorded.Baseline != current.Baseline) reasons.Add(ProposalParsimonyStaleReason.BaselineChanged);
        if (recorded.Proposal != current.Proposal) reasons.Add(ProposalParsimonyStaleReason.ProposalChanged);
        if (recorded.Prerequisites != current.Prerequisites) reasons.Add(ProposalParsimonyStaleReason.PrerequisiteChanged);
        if (recorded.Parser != current.Parser) reasons.Add(ProposalParsimonyStaleReason.ParserChanged);
        if (recorded.Schema != current.Schema) reasons.Add(ProposalParsimonyStaleReason.SchemaChanged);
        if (recorded.Measures != current.Measures) reasons.Add(ProposalParsimonyStaleReason.MeasuresChanged);
        return reasons;
    }

    private static JobRecord? LatestCompletedTrial(MotifDatabase database, string projectKey, string proposalId)
    {
        var id = CanonicalId.Parse(proposalId);
        return new JobRepository(database).ListByProjectAndKind(projectKey, "trial")
            .Where(trial => trial.Status == JobStatus.Completed && Names(trial, id.Value))
            .OrderByDescending(trial => trial.UpdatedUtc, StringComparer.Ordinal).FirstOrDefault();
    }

    private static bool Names(JobRecord trial, string proposalId)
    {
        try { return FrozenProposalRevision.Create(TrialJobInput.Parse(trial.InputJson).ProposalJson).ProposalId == proposalId; }
        catch (Exception exception) when (exception is InvalidOperationException or JsonException or ArgumentException)
        {
            return false;
        }
    }

    private static string? TryIntentDigest(JobRecord trial)
    {
        try { return FrozenProposalRevision.Create(TrialJobInput.Parse(trial.InputJson).ProposalJson).IntentDigest; }
        catch (Exception exception) when (exception is InvalidOperationException or JsonException or ArgumentException)
        {
            return null;
        }
    }

    private static ProposalParsimonyJob JobOf(MotifDatabase database, JobRecord job)
    {
        JobStageProgress? stage = null;
        if (job.Status == JobStatus.Running && !string.IsNullOrWhiteSpace(job.ProgressJson))
        {
            try { stage = JsonSerializer.Deserialize<JobStageProgress>(job.ProgressJson, MotifJson.CreateOptions()); }
            catch (JsonException) { /* A progress line that cannot be read is simply not shown. */ }
        }
        var terminal = job.Status is not (JobStatus.Queued or JobStatus.WaitingForBaseline or JobStatus.Running);
        return new ProposalParsimonyJob(job.JobId, JobStatusJson.ToWire(job.Status), stage, job.CreatedUtc,
            terminal ? job.UpdatedUtc : null,
            job.Status is JobStatus.Queued or JobStatus.WaitingForBaseline ? QueuedBehind(database, job) : null,
            job.Status == JobStatus.Completed ? StageTimingsOf(job) : null);
    }

    private static IReadOnlyList<ParsimonyStageTiming>? StageTimingsOf(JobRecord job)
    {
        if (string.IsNullOrWhiteSpace(job.ResultJson)) return null;
        try
        {
            return JsonSerializer.Deserialize<ParsimonyCandidateEvidenceResponse>(job.ResultJson,
                MotifJson.CreateOptions())?.StageTimings;
        }
        catch (JsonException) { return null; }
    }

    private static string? QueuedBehind(MotifDatabase database, JobRecord job)
    {
        var ahead = new JobRepository(database).ListActive(job.ProjectKey)
            .Where(other => other.JobId != job.JobId && other.Kind != ParsimonyCandidateEvidenceBuilder.JobKind)
            .Select(other => other.Kind).ToArray();
        if (ahead.Length == 0) return null;
        return ahead.Contains("trial", StringComparer.Ordinal) ? "trial" : "parse";
    }

    private static JobRecord? SkippedForTimeout(MotifDatabase database, JobRecord trial, string parserKey)
    {
        var current = CurrentInputs(database, trial.ProjectKey, trial, parserKey);
        return current is null ? null
            : ParsimonyAutoRun.TimedOutBefore(database, trial.ProjectKey, current.Baseline, current.Parser);
    }

    private static ProposalParsimonyFailure FailureOf(JobRecord job)
    {
        var detail = ParsimonyAutoRun.FailureDetail(job);
        var category = job.Status switch
        {
            JobStatus.Cancelled => ProposalParsimonyFailureCategory.Cancelled,
            JobStatus.StoppedByBudget => ProposalParsimonyFailureCategory.TimedOut,
            _ when ParsimonyAutoRun.IsTimedOut(job) => ProposalParsimonyFailureCategory.TimedOut,
            _ when job.FailureCategory == JobFailureCategory.Cancellation => ProposalParsimonyFailureCategory.Cancelled,
            _ when Mentions(detail, "PanGloss changed", "executable", "not found", "unavailable") =>
                ProposalParsimonyFailureCategory.ParserUnavailable,
            _ when job.FailureCategory == JobFailureCategory.ParserRefusal => ProposalParsimonyFailureCategory.FactsFailed,
            _ when job.FailureCategory == JobFailureCategory.Semantic => ProposalParsimonyFailureCategory.EvidenceFailed,
            _ => ProposalParsimonyFailureCategory.Internal,
        };
        var text = category switch
        {
            ProposalParsimonyFailureCategory.TimedOut =>
                "PanGloss ran past its time limit on this grammar, so Parsimony could not measure it.",
            ProposalParsimonyFailureCategory.ParserUnavailable =>
                "PanGloss was missing or changed while Parsimony ran.",
            ProposalParsimonyFailureCategory.FactsFailed => "PanGloss could not read the grammar.",
            ProposalParsimonyFailureCategory.EvidenceFailed => "Motif could not build the comparison evidence.",
            ProposalParsimonyFailureCategory.Cancelled => "The Parsimony run was cancelled.",
            _ => "The Parsimony run stopped for a reason Motif could not classify.",
        };
        return new ProposalParsimonyFailure(category, detail is null ? text : text + " " + detail,
            category is not ProposalParsimonyFailureCategory.TimedOut, job.UpdatedUtc);
    }

    private static bool Mentions(string? text, params string[] needles) =>
        text is not null && needles.Any(needle => text.Contains(needle, StringComparison.OrdinalIgnoreCase));

    private static ProposalParsimonyGroup UnrunGrammar() => new(ParsimonyMeasureGroup.Grammar,
        ProposalParsimonyGroupState.NotYetRun, null, null, [], []);

    private static ProposalParsimonyGroup TextGroup(bool hasSelection) => hasSelection
        ? new ProposalParsimonyGroup(ParsimonyMeasureGroup.Text, ProposalParsimonyGroupState.NotYetRun,
            "The Text group is not measured automatically yet; run parsimony --group text.", null, [], [])
        : new ProposalParsimonyGroup(ParsimonyMeasureGroup.Text, ProposalParsimonyGroupState.NotSetUp,
            ParsimonyCommands.TextGroupSkippedNote, null, [], []);

    private static IReadOnlyList<ProposalParsimonyGroup> ReadGrammar(JobRecord job)
    {
        if (job.ResultJson is null) return [UnrunGrammar()];
        ParsimonyCandidateEvidenceResponse? response;
        try { response = JsonSerializer.Deserialize<ParsimonyCandidateEvidenceResponse>(job.ResultJson, MotifJson.CreateOptions()); }
        catch (JsonException) { return [UnrunGrammar()]; }
        if (response is null) return [UnrunGrammar()];
        var sides = new List<(string Measure, ParsimonyReportResponse Before, ParsimonyReportResponse After,
            IReadOnlyList<ParsimonyCandidateFindingChange> Changes)>
        {
            (response.MeasureId, response.Before, response.After, response.FindingChanges),
        };
        sides.AddRange((response.MoreMeasures ?? []).Select(item => (item.MeasureId, item.Before, item.After,
            item.FindingChanges)));
        var runs = sides.SelectMany(side => side.After.MeasureRuns).ToArray();
        var changes = sides.Select(side => new ProposalParsimonyMeasureChange(side.Measure,
            Count(side.Changes, "added"), Count(side.Changes, "removed"), Count(side.Changes, "unchanged"),
            Count(side.Changes, "evidence-changed"), side.Before.ReportId, side.After.ReportId)).ToArray();
        var state = runs.Any(run => run.Status == ParsimonyMeasureStatus.Computed) ? ProposalParsimonyGroupState.Ready
            : runs.Any(run => run.Status == ParsimonyMeasureStatus.Failed) ? ProposalParsimonyGroupState.Failed
            : ProposalParsimonyGroupState.Abstained;
        return [new ProposalParsimonyGroup(ParsimonyMeasureGroup.Grammar, state, null,
            new ProposalParsimonyCandidate(response.Before.Inputs.BundleId, response.After.Inputs.BundleId,
                response.Candidate.IdentitySha256), runs, changes)];
    }

    private static int Count(IReadOnlyList<ParsimonyCandidateFindingChange> changes, string kind) =>
        changes.Count(change => change.Change == kind);
}
