using System.Linq;
using System.Text.Json;
using SIL.Motif.Commands.Store;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.Config;
using SIL.Motif.Host.Store;
using SIL.Motif.Projection.Store;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;
using DryRunModel = SIL.Motif.Model.DryRun.DryRun;

namespace SIL.Motif.Commands;

/// <summary>What <c>apply</c> decided about one Proposal, with the evidence the decision used.</summary>
/// <param name="Readiness">The Readiness reasons, or none when the Proposal is ready.</param>
/// <param name="Candidate">The Correctness Assessment covering the Proposal's current content, if any.</param>
/// <param name="BoundDryRun">The Dry Run the Proposal's anchor names, if one is still current.</param>
/// <param name="HasAnchor">Whether a Dry Run is bound to the Proposal at all.</param>
/// <param name="Considered">How many Correctness Assessments of the revision were weighed to find the candidate.</param>
/// <param name="Unread">How many older Assessments of the revision were left unread past the newest ten.</param>
/// <param name="PendingRefusal">The pending changes that must be resolved before another Proposal can be applied.</param>
internal sealed record ApplyAuthorizationDecision(
    Readiness.Decision Readiness, AssessmentRecord? Candidate, DryRunModel? BoundDryRun, bool HasAnchor,
    int Considered = 0, int Unread = 0, Refusal? PendingRefusal = null)
{
    /// <summary>Whether Apply may proceed: a bound Dry Run is required even with force, Readiness only without.</summary>
    public bool Authorized(bool force) => PendingRefusal is null && HasAnchor && (force || Readiness.Reasons.Count == 0);

    /// <summary>The Readiness shown to a person, naming a missing bound Dry Run as a reason.</summary>
    public ReadinessProjection ToProjection(bool forced)
    {
        var reasons = HasAnchor ? Readiness.Reasons
            : [.. Readiness.Reasons, "no Dry Run is bound to it, so nothing has shown what it would change"];
        if (PendingRefusal is not null) reasons = [.. reasons, PendingRefusal.Message];
        return new ReadinessProjection(
            IsReady: reasons.Count == 0,
            CorrectnessAssessmentRequired: Readiness.CorrectnessAssessmentRequired,
            CorrectnessAssessmentExempt: !Readiness.CorrectnessAssessmentRequired,
            CorrectnessAssessmentExemptionReason: Readiness.CorrectnessAssessmentExemptionReason,
            Reasons: reasons,
            Forced: forced && reasons.Count > 0)
        {
            DroppedGrammarReason = Readiness.DroppedGrammarReason,
            DroppedGrammar = Readiness.Dropped,
        };
    }
}

/// <summary>
/// The one place that decides whether a Proposal may be applied, so the window's Review and
/// <c>motif apply</c> cannot reach different verdicts from the same store.
/// </summary>
internal static class ApplyAuthorization
{
    internal static Refusal? PendingChangesRefusal(MotifDatabase database, CanonicalId proposalId)
    {
        var pending = new ProposalRepository(database).FindPendingChangesDraft();
        if (pending is null || pending.ProposalId == proposalId) return null;
        var draft = JsonSerializer.Deserialize<DraftDocument>(pending.ProposalJson!,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("The pending Draft has no content.");
        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var provenance = draft.ComposerProvenance
            .Where(item => PendingChanges.Property(item, "changeId") is not null)
            .GroupBy(item => PendingChanges.Property(item, "changeId")!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);
        var operationNames = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in draft.ComposerProvenance)
        {
            if (!entry.TryGetProperty("operationIds", out var ids) || ids.ValueKind != JsonValueKind.Array) continue;
            var input = entry.TryGetProperty("input", out var value) ? value : default;
            var label = PendingChanges.Property(input, "pendingName");
            if (label is null) continue;
            foreach (var id in ids.EnumerateArray())
                if (id.ValueKind == JsonValueKind.String) operationNames[id.GetString()!] = label;
        }
        foreach (var operation in draft.Operations)
        {
            var changeId = operation.Extensions is { } ext ? PendingChanges.Property(ext, "changeId") : null;
            if (changeId is not null)
            {
                if (!seen.Add(changeId)) continue;
                var word = provenance.TryGetValue(changeId, out var entry) ? PendingChanges.Property(entry, "word") : null;
                names.Add(string.IsNullOrWhiteSpace(word) ? "change '" + changeId + "'" : "word '" + word + "'");
            }
            else names.Add(operationNames.GetValueOrDefault(operation.OperationId) ??
                "change '" + (operation.Target ?? operation.OperationId) + "'");
        }
        if (names.Count == 0) return null;
        var listed = string.Join(", ", names.Take(3));
        if (names.Count > 3) listed += $" and {names.Count - 3} more";
        var noun = names.Count == 1 ? "change" : "changes";
        var pronoun = names.Count == 1 ? "it" : "them";
        return new Refusal(RefusalCodes.ApplyPendingChanges, FailureReason.Refused,
            $"You have {names.Count} {noun} not applied yet ({listed}). Apply or discard {pronoun} first.");
    }

    /// <summary>Weighs the Proposal's evidence the way Apply does, without refusing or changing anything.</summary>
    public static ApplyAuthorizationDecision Evaluate(MotifDatabase database, ProjectLocator project,
        ProposalRecord record, SIL.Motif.Contract.Model.Proposal content, ManifestDocument manifest)
    {
        var pendingRefusal = PendingChangesRefusal(database, record.ProposalId);
        if (manifest.Anchor is null)
            return new ApplyAuthorizationDecision(new Readiness.Decision([], false), null, null, false, PendingRefusal: pendingRefusal);

        var configuration = new ProjectConfigurationReader().Read(project);
        var assessments = new AssessmentRepository(database);
        var candidateIds = manifest.CurrentIntentDigest is null
            ? []
            : ProposalCommands.CandidateAssessmentIds(assessments, record.ProposalId, manifest.CurrentIntentDigest);
        var baselines = new BaselineRepository(database);
        var projectKey = ProjectWorkspaceKey.Compute(project);
        var baseline = baselines.GetCurrent(projectKey);

        var boundDryRun = ProposalCommands.FindCurrentBoundDryRun(database, project, record.ProposalJson!, manifest,
                content) ?? ProposalCommands.FindTrialBoundDryRun(database, project, record.ProposalId, manifest);
        var exemptionReason = boundDryRun is null
            ? null
            : JudgmentOnlyProposalReadiness.ExemptionFor(content, boundDryRun.ExpectedEffects) ??
                ProseOnlyProposalReadiness.ExemptionFor(content, boundDryRun.ExpectedEffects) ??
                SourceOnlyProposalReadiness.ExemptionFor(content, boundDryRun.ExpectedEffects);
        var changedWords = ProposalCommands.ChangedWords(content);

        // The current Assessment and whether it sits on the current Baseline do not depend on the candidate.
        var currentAssessment = assessments.GetCurrent();
        var currentBaselineToken = baseline is null ? currentAssessment?.BaselineToken
            : JsonSerializer.Serialize(baseline.Token, MotifJson.CreateOptions());
        var currentIsStale = baseline is not null &&
            !ProposalCommands.AssessmentUsesCurrentBaseline(baselines, projectKey, currentAssessment);

        var droppedByAssessment = new Lazy<IReadOnlyDictionary<string, DroppedGrammar>>(() =>
            Trials.DroppedGrammarByAssessment(database, project, record.ProposalId));

        Readiness.Decision EvaluateFor(AssessmentRecord? candidate)
        {
            var current = currentAssessment;
            if (currentIsStale)
            {
                current = null;
                if (candidate is not null && baseline is not null)
                    current = ReusedBeforeCorrectness(database, project, record.ProposalId, assessments, candidate);
                if (current is null && candidate is not null)
                    current = assessments.FindLatestBaselineAssessment(
                        RegressionChecker.RequiredKind, currentBaselineToken!, candidate.Selection.Sha256,
                        candidate.Selection.Words);
            }
            var candidateBaselineToken = candidate?.BaselineToken ?? "";
            if (baseline is not null && candidate is not null)
                candidateBaselineToken = ProposalCommands.AssessmentUsesCurrentBaseline(baselines, projectKey, candidate)
                    ? currentBaselineToken! : "stale:" + candidate.BaselineToken;
            var currentCorrectness = current is not null &&
                string.Equals(current.Kind, RegressionChecker.RequiredKind, System.StringComparison.Ordinal)
                    ? current.ToCorrectness()
                    : null;
            // Judged for the Trial that produced this Assessment; with that Trial gone nothing was checked.
            var dropped = candidate is null ? null : droppedByAssessment.Value.GetValueOrDefault(candidate.AssessmentId)
                ?? DroppedGrammar.NotChecked("The Trial that made this Assessment is no longer stored.");
            return Host.Assess.Readiness.Evaluate(
                candidate?.ToCorrectness(), currentCorrectness, currentBaselineToken,
                candidateBaselineToken, configuration.GateOnRegression, changedWords, exemptionReason, dropped,
                word => SIL.Motif.Host.Parser.LowercasePass.ForProjectIfNeeded(project.FullFwDataPath, [word]).LowercaseOf(word));
        }

        var choice = ProposalCommands.ChooseCandidate(candidateIds, assessments.Get, EvaluateFor);
        var candidate = choice.Candidate;
        var readiness = choice.Decision;
        return new ApplyAuthorizationDecision(readiness, candidate, boundDryRun, true, choice.Considered, choice.Unread, pendingRefusal);
    }

    private static AssessmentRecord? ReusedBeforeCorrectness(MotifDatabase database, ProjectLocator project,
        CanonicalId proposalId, AssessmentRepository assessments,
        AssessmentRecord candidate)
    {
        var trials = Trials.ListFor(database, project, proposalId.Value);
        var jobs = new JobRepository(database);
        foreach (var trial in trials)
        {
            if (!trial.AssessmentIds.Values.Contains(candidate.AssessmentId, StringComparer.Ordinal)) continue;
            var job = jobs.Get(trial.JobId);
            if (job is null) continue;
            foreach (var parseId in TrialJobInput.Parse(job.InputJson).BaselineAssessmentIds ?? [])
            {
                var parse = assessments.GetHeader(parseId);
                if (parse?.Kind != AssessmentKinds.ParseTime || parse.Invocation is null ||
                    !Assess.BeforeAssessmentReuse.MatchesBaseline(database, parse, candidate)) continue;
                var header = assessments.ListByInvocationAndKind(parse.Invocation.InvocationId, AssessmentKinds.Correctness)
                    .FirstOrDefault(item => item.Selection.Sha256 == candidate.Selection.Sha256 &&
                        item.Selection.Words.SequenceEqual(candidate.Selection.Words, StringComparer.Ordinal));
                if (header is not null) return assessments.Get(header.AssessmentId);
            }
        }
        return null;
    }
}
