using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Canonicalization;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Contract.Review;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Runtime.Review;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Commands.Review;

internal static class LegacyTrialReviewProjection
{
    internal static ReportView? Read(JobRecord job, AssessmentRepository assessments)
    {
        if (job.Status != JobStatus.Completed || job.ResultJson is null) return null;
        var input = TrialJobInput.Parse(job.InputJson);
        if (input.ReviewInputs is not null) return null;
        var pins = ReadPins(job, input, assessments);
        if (pins is null) return null;
        var digest = pins.EvidenceManifestDigest;
        var unavailable = new Coverage(null, null, null, null, null, CoveragePrecision.Unavailable, ["legacy-review-inputs-missing"]);
        var side = new IncompleteSide(null, null, []);
        var summary = new ReviewSummary("No red flags means only 'not bad', never 'good'.", [],
            new IncompleteSummary("Incomplete", null, null, side, side, [], 1),
            new WordformSummary(null, unavailable, unavailable, null, null), new ApprovedSummary(null, null, null, null, null),
            new CoverageSummary(0, unavailable, unavailable, null), new CoverageSummary(0, unavailable, unavailable, null),
            [], new NegativeSummary(0, null, null, null), new WorkChange(null, 0, digest, WorkComparison.NotComparable,
                ["legacy-review-inputs-missing"]), new WarningSummary(null, null, null), [], new EffectsSummary(null, false), []);
        var limitations = new[] { new Limitation("legacy-review-inputs-missing", [],
            "This Trial predates captured review scope, exact identities and expectations; exhaustive review details are unavailable.", []) };
        return new ReportView("motif.proposal-review-report", 1, "legacy/" + job.JobId, digest, DetailLevel.Summary,
            pins, new Freshness(FreshnessRelation.Unknown, ["freshness-not-checked"]), summary,
            Enum.GetValues<ReviewFlagId>().Select(id => new FlagEvidence(id, EvidenceState.Unavailable,
                ["legacy-review-inputs-missing"], 0)).ToArray(), limitations, DetailAvailability.Unavailable, [], null, null, null, []);
    }

    internal static ReportPins? ReadPins(JobRecord job, TrialJobInput input, AssessmentRepository assessments)
    {
        if (job.Status != JobStatus.Completed || job.ResultJson is null) return null;
        using var result = JsonDocument.Parse(job.ResultJson);
        if (!result.RootElement.TryGetProperty("baselineToken", out var token)) return null;
        var baseline = token.Deserialize<BaselineToken>(MotifJson.CreateOptions());
        if (baseline is null) return null;
        var proposal = ProposalJsonParser.Parse(input.ProposalJson);
        var digest = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(job.InputJson + "\n" + job.ResultJson)));
        var before = (input.BaselineAssessmentIds ?? []).Concat(input.ReviewBaselines?.SelectMany(part => part.AssessmentIds) ?? [])
            .Distinct(StringComparer.Ordinal).ToArray();
        var after = result.RootElement.TryGetProperty("assessmentIds", out var ids) && ids.ValueKind == JsonValueKind.Array
            ? ids.EnumerateArray().Select(id => id.GetString()!).Distinct(StringComparer.Ordinal).ToArray() : [];
        CheckpointPin[] checkpoints =
        [
            TrialReviewPublisher.Checkpoint(assessments, Side.Before, before, input),
            TrialReviewPublisher.Checkpoint(assessments, Side.After, after, input),
        ];
        return new ReportPins(job.ProjectKey, proposal.ProposalId.Value, null, IntentDigest.Compute(proposal),
            null, (input.Prerequisites ?? []).Select(item => new PrerequisitePin(item.ProposalId, item.IntentDigest)).ToArray(),
            job.JobId, baseline, new CandidatePin(job.JobId, null, null), null, checkpoints, 0, null, null,
            new WarningCheckPins(null, null), [], digest, "legacy-projection-v1");
    }
}
