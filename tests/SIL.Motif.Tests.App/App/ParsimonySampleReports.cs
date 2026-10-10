using System.Text.Json;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Tests.App;

/// <summary>
/// A stored Parsimony Report shaped the way the Parsimony page reads one, for the walkthrough's stored Report and for
/// the page's state captures. The variants differ only in what the page is shown: incomplete inputs or the Baseline
/// the Report was measured from.
/// </summary>
internal static class ParsimonySampleReports
{
    internal const string ReportId = "report/parsimony-sample";
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

    internal static string Json(ParsimonyReportResponse report) =>
        JsonSerializer.Serialize(report, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private static ParsimonyFinding Finding(string id, long numerator, long denominator, IReadOnlyList<string> items,
        IReadOnlyList<string> limitations) => (new ParsimonyFinding(
        id, ParsimonyMeasure, ParsimonyAxis.Parsimony, ParsimonyTier.Static,
        new ParsimonyFindingAttachment(ParsimonyAttachmentKind.AuthoredObject, "attachment-" + id,
            ParsimonyAuthoredObjectKind.AdhocProhibition),
        null, new ParsimonyMeasureNumber(numerator, denominator, "prohibitions"),
        new ParsimonyMeasureThreshold(ParsimonyThresholdOperator.GreaterThan, 0, "v1"),
        "sha256:" + new string('7', 64),
        [new ParsimonyEvidenceReference(BundleId, "adhoc-context", JsonDocument.Parse("{}").RootElement)],
        "guide:parsimony/recipes/" + ParsimonyMeasure, limitations,
        ParsimonyVerification.NotRun) with { ItemNames = items });
}
