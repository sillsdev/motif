using System.Linq;
using System.Text.Json;
using SIL.Motif.Commands.Store;
using SIL.Motif.Contract;
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
internal sealed record ApplyAuthorizationDecision(
    Readiness.Decision Readiness, AssessmentRecord? Candidate, DryRunModel? BoundDryRun, bool HasAnchor,
    int Considered = 0, int Unread = 0)
{
    /// <summary>Whether Apply may proceed: a bound Dry Run is required even with force, Readiness only without.</summary>
    public bool Authorized(bool force) => HasAnchor && (force || Readiness.Reasons.Count == 0);

    /// <summary>The Readiness shown to a person, naming a missing bound Dry Run as a reason.</summary>
    public ReadinessProjection ToProjection(bool forced)
    {
        var reasons = HasAnchor ? Readiness.Reasons
            : [.. Readiness.Reasons, "no Dry Run is bound to it, so nothing has shown what it would change"];
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
    /// <summary>Weighs the Proposal's evidence the way Apply does, without refusing or changing anything.</summary>
    public static ApplyAuthorizationDecision Evaluate(MotifDatabase database, ProjectLocator project,
        ProposalRecord record, SIL.Motif.Contract.Model.Proposal content, ManifestDocument manifest)
    {
        if (manifest.Anchor is null)
            return new ApplyAuthorizationDecision(new Readiness.Decision([], false), null, null, false);

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
        return new ApplyAuthorizationDecision(readiness, candidate, boundDryRun, true, choice.Considered, choice.Unread);
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
