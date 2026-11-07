using System.Security.Cryptography;
using System.Text.Json;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Review;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Parsimony;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Worker.Runtime.Review;

internal sealed record TrialReviewParsimonySnapshot(string JobId, string ResultDigest,
    IReadOnlyList<ParsimonyPair> Pins, ReviewFindingDelta Findings, IReadOnlyList<ReviewSectionItem> Rows);

internal static class TrialReviewParsimony
{
    internal static TrialReviewParsimonySnapshot? Read(MotifDatabase database, JobRecord trial,
        TrialJobInput input, BaselineToken baseline, string intentDigest)
    {
        var jobs = new JobRepository(database).ListByProjectAndKind(trial.ProjectKey, ParsimonyCandidateEvidenceBuilder.JobKind)
            .Where(job => job.Status == JobStatus.Completed).OrderByDescending(job => job.CreatedUtc, StringComparer.Ordinal)
            .ThenByDescending(job => job.JobId, StringComparer.Ordinal);
        foreach (var job in jobs)
        {
            try
            {
                var request = ParsimonyCandidateJobInput.Parse(job.InputJson);
                if (request.DryRunJobId != trial.JobId) continue;
                var binding = ParsimonyAutoInputs.FromKey(request.InputsKey);
                var expected = ParsimonyAutoInputs.For(trial.InputJson, baseline);
                var response = RetainedParsimonyReportReader.Read(job.ResultJson);
                if (binding is null || binding != expected || response is null || response.Candidate.DryRunJobId != trial.JobId ||
                    response.Candidate.ProposalId != expected.ProposalId || response.Candidate.ProposalIntentDigest != intentDigest ||
                    response.Candidate.BaselineToken != baseline) continue;
                var pairs = new[] { new ParsimonyCandidateMeasureResult(response.MeasureId, response.Before, response.After, response.FindingChanges) }
                    .Concat(response.MoreMeasures ?? []).ToArray();
                var before = pairs.Select(pair => Measure(pair.MeasureId, pair.Before, binding)).ToArray();
                var after = pairs.Select(pair => Measure(pair.MeasureId, pair.After, binding)).ToArray();
                var delta = ReviewFindingComparison.Compare(new ReviewFindingInputs(null, null, before, after));
                var pins = pairs.Select(pair => new ParsimonyPair("grammar", pair.MeasureId, pair.Before.ReportId, pair.After.ReportId)).ToArray();
                var rows = new List<ReviewSectionItem>();
                foreach (var pair in pairs)
                {
                    var comparison = delta.Measures.Single(item => item.MeasureId == pair.MeasureId && item.Group == "grammar");
                    if (comparison.Limitation is not null) continue;
                    var previous = pair.Before.Findings.Where(item => item.MeasureId == pair.MeasureId).ToDictionary(item => item.FindingId);
                    var current = pair.After.Findings.Where(item => item.MeasureId == pair.MeasureId).ToDictionary(item => item.FindingId);
                    foreach (var id in previous.Keys.Union(current.Keys).Order(StringComparer.Ordinal))
                    {
                        var finding = current.GetValueOrDefault(id) ?? previous[id];
                        var change = comparison.Added.Contains(id) ? FindingChange.Added :
                            comparison.Removed.Contains(id) ? FindingChange.Removed :
                            comparison.EvidenceChanged.Contains(id) ? FindingChange.EvidenceChanged : FindingChange.Unchanged;
                        var row = new FindingRow(pair.MeasureId + ":" + id, pair.MeasureId, id, change, "grammar",
                            comparison.Unit, finding.Tier.ToString(), null, finding.EvidenceDigest, []);
                        rows.Add(new ReviewSectionItem("parsimony", row, Group: pair.MeasureId, Outcome: change.ToString().ToLowerInvariant(),
                            Flags: change == FindingChange.Added ? [ReviewFlagId.NewWarningsOrParsimony] : [],
                            Changed: change != FindingChange.Unchanged));
                    }
                }
                return new TrialReviewParsimonySnapshot(job.JobId, Hash(job.ResultJson!), pins, delta, rows);
            }
            catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidDataException or InvalidOperationException)
            { }
        }
        return null;
    }

    private static ReviewFindingMeasure Measure(string id, ParsimonyReportResponse report, ParsimonyAutoInputs binding)
    {
        var run = report.MeasureRuns.SingleOrDefault(item => item.MeasureId == id);
        var universe = Hash(JsonSerializer.Serialize(new { report.Inputs.BaselineToken, report.Inputs.EvidenceScope,
            report.Inputs.SelectionSha256, report.Inputs.ExpectationRevisionSha256 }, MotifJson.CreateOptions()));
        var version = binding.Schema + "/" + binding.Measures;
        return new ReviewFindingMeasure("grammar", id, version, binding.Measures, universe,
            run?.Unit ?? "unknown", run?.Status == ParsimonyMeasureStatus.Computed,
            report.Findings.Where(finding => finding.MeasureId == id)
                .Select(finding => new ReviewFinding(finding.FindingId, finding.EvidenceDigest, null)).ToArray());
    }

    private static string Hash(string value) => "sha256:" + Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)));
}
