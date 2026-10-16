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
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;
using DryRunModel = SIL.Motif.Model.DryRun.DryRun;

namespace SIL.Motif.Commands;

/// <summary>What <c>apply</c> decided about one Proposal, with the evidence the decision used.</summary>
/// <param name="Readiness">The Readiness reasons, or none when the Proposal is ready.</param>
/// <param name="Candidate">The Correctness Assessment covering the Proposal's current content, if any.</param>
/// <param name="BoundDryRun">The Dry Run the Proposal's anchor names, if one is still current.</param>
/// <param name="HasAnchor">Whether a Dry Run is bound to the Proposal at all.</param>
internal sealed record ApplyAuthorizationDecision(
    Readiness.Decision Readiness, AssessmentRecord? Candidate, DryRunModel? BoundDryRun, bool HasAnchor)
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
            Forced: forced && reasons.Count > 0);
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
        var candidate = manifest.CurrentIntentDigest is null
            ? null
            : ProposalCommands.FindCandidateAssessment(assessments, record.ProposalId, manifest.CurrentIntentDigest);
        var current = assessments.GetCurrent();
        var baselines = new BaselineRepository(database);
        var projectKey = ProjectWorkspaceKey.Compute(project);
        var baseline = baselines.GetCurrent(projectKey);
        var currentBaselineToken = baseline is null ? current?.BaselineToken
            : JsonSerializer.Serialize(baseline.Token, MotifJson.CreateOptions());
        if (baseline is not null && !ProposalCommands.AssessmentUsesCurrentBaseline(baselines, projectKey, current))
            current = candidate is null ? null : assessments.FindLatestBaselineAssessment(
                RegressionChecker.RequiredKind, currentBaselineToken!, candidate.Selection.Sha256,
                candidate.Selection.Words);
        var candidateBaselineToken = candidate?.BaselineToken ?? "";
        if (baseline is not null && candidate is not null)
            candidateBaselineToken = ProposalCommands.AssessmentUsesCurrentBaseline(baselines, projectKey, candidate)
                ? currentBaselineToken! : "stale:" + candidate.BaselineToken;
        var currentCorrectness = current is not null &&
            string.Equals(current.Kind, RegressionChecker.RequiredKind, System.StringComparison.Ordinal)
                ? current.ToCorrectness()
                : null;

        var boundDryRun = ProposalCommands.FindCurrentBoundDryRun(database, project, record.ProposalJson!, manifest,
                content) ?? ProposalCommands.FindTrialBoundDryRun(database, project, record.ProposalId, manifest);
        var exemptionReason = boundDryRun is null
            ? null
            : JudgmentOnlyProposalReadiness.ExemptionFor(content, boundDryRun.ExpectedEffects);

        var readiness = Host.Assess.Readiness.Evaluate(
            candidate?.ToCorrectness(), currentCorrectness, currentBaselineToken,
            candidateBaselineToken, configuration.GateOnRegression,
            ProposalCommands.ChangedWords(content), exemptionReason);
        return new ApplyAuthorizationDecision(readiness, candidate, boundDryRun, true);
    }
}
