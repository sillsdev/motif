using System.Text.Json;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Parsimony;

namespace SIL.Motif.Tests.App;

/// <summary>
/// A stored Parsimony Report shaped the way the Parsimony page reads one, for the walkthrough's stored Report and for
/// the page's state captures. The variants differ only in what the page is shown: incomplete inputs or the Baseline
/// the Report was measured from.
/// </summary>
internal static class ParsimonySampleReports
{
    // Shaped like a minted Report id, so the page's view filter accepts it as a canonical id.
    internal const string ReportId = "report/AAAAAAAAAAAAAAAAAAAAAA";
    internal const string BundleId = "bundle/parsimony-sample";
    internal const string ParsimonyMeasure = "P-adhoc-duplicate";

    internal static string RestrictivenessMeasure { get; } =
        MeasureCatalog.All.First(measure => measure.Axis == ParsimonyAxis.Restrictiveness).Id;

    /// <summary>The Report listing two parsimony findings; <paramref name="incomplete"/> adds an unmeasured check.</summary>
    internal static ParsimonyReportResponse Listed(BaselineToken token, bool incomplete = false)
    {
        var inputs = new ParsimonyReportInputs(BundleId, token, "baseline", null, "sha256:" + new string('3', 64),
            new ParsimonyArtifactDigest(4, new string('4', 64)), new ParsimonyArtifactDigest(1, new string('5', 64)),
            null, null, []);
        var runs = new List<ParsimonyMeasureRun>
        {
            new(ParsimonyMeasure, ParsimonyMeasureStatus.Computed, 4, 2, "prohibitions",
                new ParsimonyMeasureNumber(2, 4, "prohibitions"), 0.5, null),
        };
        if (incomplete)
            runs.Add(new(RestrictivenessMeasure, ParsimonyMeasureStatus.NotAvailable, null, null, "forms", null, null,
                "The parser evidence table is missing."));
        return new ParsimonyReportResponse(ReportId, inputs,
            [
                Finding("finding-1", 2, 3, ["ka- + -ni"], ["Counts only the prohibitions in this sample grammar."]),
                Finding("finding-2", 1, 4, [], []),
            ], "Stored Parsimony Report.")
        {
            JoinQuality = new ParsimonyJoinQuality("default-selection", false, 0, 0, 0, 0, 0, 0, 0, null, null, null, null, null),
            Notes = incomplete
                ? [new ParsimonyNote("not-checked", RestrictivenessMeasure,
                    $"Not checked: {RestrictivenessMeasure} — the parser evidence table is missing.", null)]
                : [],
            MeasureRuns = runs,
        };
    }

    internal static ParsimonyReportResponse ReviewWorkspace(BaselineToken token)
    {
        var report = Listed(token);
        ParsimonyFinding[] findings =
        [
            Finding("review-active", 2, 4, ["ka- + -ni"], ["Counts only the prohibitions in this sample grammar."]),
            Finding("review-asked", 1, 4, ["ma- + -tu"], ["Paired parser verification was not collected for this item."]),
            Finding("review-kept", 3, 6, ["mu- + -ta"], ["Paired parser verification was not collected for this item."]),
            Finding("review-deferred", 1, 5, ["sa- + -li"], ["The paired parser check has not run for this form."],
                RestrictivenessMeasure, ParsimonyAxis.Restrictiveness, "forms"),
            Finding("review-suppressed", 1, 3, ["ku- + -na"], ["The saved decision remains tied to this Report finding."]),
        ];
        return report with
        {
            Findings = findings,
            MeasureRuns =
            [
                new(ParsimonyMeasure, ParsimonyMeasureStatus.Computed, 9, 3, "prohibitions",
                    new ParsimonyMeasureNumber(3, 9, "prohibitions"), 1d / 3d, null),
                new(RestrictivenessMeasure, ParsimonyMeasureStatus.Computed, 5, 1, "forms",
                    new ParsimonyMeasureNumber(1, 5, "forms"), 0.2, null),
            ],
        };
    }

    /// <summary>A current all-measure job; each sample finding belongs to exactly one Report.</summary>
    internal static ParsimonyReportSetResponse CurrentSet(ParsimonyReportResponse sample)
    {
        var inputs = new ParsimonyReportInputs(sample.Inputs.BundleId, sample.Inputs.BaselineToken,
            "baseline", null, sample.Inputs.ModelFingerprint,
            new ParsimonyArtifactDigest(PanGlossInterfaceVersions.FactsSchemaVersion,
                sample.Inputs.GrammarFacts.Sha256),
            new ParsimonyArtifactDigest(EvidenceSchema.Version, sample.Inputs.Evidence.Sha256),
            null, null, [], ParsimonyEvidenceScopeKind.ProjectApproved);
        var reports = ParsimonyAdmission.SupportedMeasureIds().Select((id, index) =>
        {
            var definition = MeasureCatalog.Find(id)!;
            var findings = sample.Findings.Where(finding => finding.MeasureId == id)
                .Select(finding => finding with
                {
                    Axis = definition.Axis, Tier = definition.Tier, Threshold = definition.Threshold!,
                }).ToArray();
            var run = sample.MeasureRuns.SingleOrDefault(candidate => candidate.MeasureId == id);
            // Measures without sample findings have completed empty inventories.
            var number = findings.FirstOrDefault()?.Number ??
                new ParsimonyMeasureNumber(0, 0, definition.Unit);
            run ??= new ParsimonyMeasureRun(id, ParsimonyMeasureStatus.Computed, number.Denominator,
                number.Numerator, number.Unit, number,
                number.Denominator == 0 ? null : (double)number.Numerator / number.Denominator, null);
            return sample with
            {
                ReportId = id == ParsimonySampleReports.ParsimonyMeasure ? sample.ReportId
                    : CanonicalId.FromGuid(new Guid(index + 1, 0, 0, new byte[8]), "report/").Value,
                Inputs = inputs, Findings = findings, MeasureRuns = [run],
                JoinQuality = sample.JoinQuality is { } quality
                    ? quality with { Scope = "project-approved" } : null,
                Notes = sample.Notes.Where(note => note.MeasureId == id).ToArray(),
            };
        }).ToArray();
        return new ParsimonyReportSetResponse("job/sample-current-all", inputs.BundleId, reports);
    }

    internal static string Json(ParsimonyReportResponse report) =>
        JsonSerializer.Serialize(report, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private static ParsimonyFinding Finding(string id, long numerator, long denominator, IReadOnlyList<string> items,
        IReadOnlyList<string> limitations, string measureId = ParsimonyMeasure,
        ParsimonyAxis axis = ParsimonyAxis.Parsimony, string unit = "prohibitions") => (new ParsimonyFinding(
        id, measureId, axis, ParsimonyTier.Static,
        new ParsimonyFindingAttachment(ParsimonyAttachmentKind.AuthoredObject, "attachment-" + id,
            ParsimonyAuthoredObjectKind.AdhocProhibition),
        null, new ParsimonyMeasureNumber(numerator, denominator, unit),
        new ParsimonyMeasureThreshold(ParsimonyThresholdOperator.GreaterThan, 0, "v1"),
        "sha256:" + new string('7', 64),
        [new ParsimonyEvidenceReference(BundleId, "adhoc-context", JsonDocument.Parse("{}").RootElement)],
        "guide:parsimony/recipes/" + measureId.ToLowerInvariant(), limitations,
        ParsimonyVerification.NotRun) with { ItemNames = items });
}
