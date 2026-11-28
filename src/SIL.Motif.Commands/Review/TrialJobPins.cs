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

internal static class TrialJobPins
{
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
