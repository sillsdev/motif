using System.Globalization;
using System.Text;
using SIL.Motif.Contract.Review;

namespace SIL.Motif.Cli.Rendering;

/// <summary>Renders the shared Trial review without changing its evidence.</summary>
public static class TrialReviewTextRenderer
{
    /// <summary>Renders a saved report, pending Trial, or absence of a Trial.</summary>
    public static string Render(TrialReviewResult result) => result switch
    {
        NoTrialResult => "No Trial is recorded for this Proposal.\n",
        PendingTrialResult pending => $"Trial {pending.JobId}: {pending.JobState}\n",
        AvailableTrialResult available => RenderReport(available.Report),
        _ => throw new ArgumentException("Unknown Trial review result.", nameof(result))
    };

    private static string RenderReport(ReportView report)
    {
        var text = new StringBuilder();
        if (report.Summary.Flags.Count == 0) text.AppendLine("No red flags observed.");
        else
            foreach (var flag in report.Summary.Flags)
                text.AppendLine(flag.Text + ": " + Flag(flag));
        text.AppendLine(report.Summary.Notice);
        text.AppendLine($"Incomplete: {Count(report.Summary.Incomplete.AffectedWords)} wordforms; {Count(report.Summary.Incomplete.RequiredCases)} required cases");
        if (report.Limitations.Count != 0 || report.Summary.Incomplete.EvidenceGapCount != 0)
            text.AppendLine("Some comparisons could not be made; see Incomplete and limitations.");
        text.AppendLine("Wordforms: " + Coverage(report.Summary.Wordforms.Before) + " before; " + Coverage(report.Summary.Wordforms.After) + " after");
        text.AppendLine("Sampled tokens: " + Coverage(report.Summary.SampledTokens.Before) + " before; " + Coverage(report.Summary.SampledTokens.After) + " after");
        text.AppendLine("Paradigm cells: " + Coverage(report.Summary.ParadigmCells.Before) + " before; " + Coverage(report.Summary.ParadigmCells.After) + " after");
        text.AppendLine("Parser work: " + (report.Summary.ParserWork.PercentChange is { } change
            ? change.ToString("+0.#;-0.#;0", CultureInfo.InvariantCulture) + "%" : "not comparable"));
        text.AppendLine($"Trial {report.Pins.TrialJobId}; report {report.ReportId}; {report.Freshness.Relation}");
        foreach (var section in report.Sections)
        {
            text.AppendLine(section.Label + ":");
            foreach (var group in section.Groups)
            {
                text.AppendLine($"  {group.Cause}: {Count(group.Count)} {group.Unit} ({Count(group.Remaining)} more)");
                foreach (var sample in group.Samples) text.AppendLine("    " + Row(sample));
            }
        }
        if (report.Page is { } page)
        {
            text.AppendLine($"{page.Section}: {Count(page.TotalMatching)} {page.Unit}");
            foreach (var item in page.Items) text.AppendLine("  " + Row(item));
            if (page.NextCursor is not null) text.AppendLine("Next cursor: " + page.NextCursor);
        }
        if (report.Word is { } word)
        {
            text.AppendLine(Row(word.Row));
            text.AppendLine("  Before: " + SideSummary(word.Row.Before));
            text.AppendLine("  After: " + SideSummary(word.Row.After));
            foreach (var childPage in new[] { word.Memberships, word.Expectations, word.Readings, word.SourceReferences })
            {
                text.AppendLine($"  {childPage.Section}: {Count(childPage.TotalMatching)} ({childPage.ListAvailability})");
                foreach (var item in childPage.Items) text.AppendLine("    " + Row(item));
                if (childPage.NextCursor is not null) text.AppendLine($"    {childPage.Section} next cursor: {childPage.NextCursor}");
            }
        }
        if (report.Trace is { } trace)
        {
            text.AppendLine($"Trace {trace.TraceId} ({trace.Side}, {trace.Origin}, {trace.Completion})");
            foreach (var node in trace.Nodes)
                text.AppendLine($"  {node.Id}: {node.Label}" + (node.ReasonCode is null ? "" : $" — {node.ReasonCode}"));
            if (trace.NextCursor is not null) text.AppendLine("Next cursor: " + trace.NextCursor);
            if (!trace.CaptureComplete) text.AppendLine("Trace capture was truncated: " + trace.TruncationReason);
            if (trace.ReadingAgreement == ReadingAgreement.Different)
                text.AppendLine("The diagnostic readings differ from the saved Trial evidence.");
        }
        return text.ToString();
    }

    private static string Flag(FlagFact flag) => flag.Facts switch
    {
        NewWordFacts f => $"{Count(flag.WordCount)} wordforms ({f.NoParseWords} no parse; {f.LostApprovedWords} Approved lost)",
        ReadingLossFacts f => $"{f.UniqueReadings} readings",
        NegativeFacts f => $"{f.Words} wordforms; {f.Memberships} memberships",
        WorkChange f => f.PercentChange is { } percent ? $"{percent.ToString("0.#", CultureInfo.InvariantCulture)}%" : "not comparable",
        NewFindingFacts f => $"{Count(f.Warnings)} warnings; " + string.Join(", ", f.Parsimony.Select(p => $"{p.MeasureId}: {p.Findings} {p.Unit}")),
        _ => "observed"
    };

    private static string Count(long? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "unknown";
    private static string Coverage(Coverage value) => Count(value.Numerator) + "/" + Count(value.Denominator) +
        (value.Precision == CoveragePrecision.LowerBound ? " (lower bound)" : "");
    private static string SideSummary(WordSide side) =>
        $"{side.Completion}; readings {Count(side.ObservedReadingCount)}; Approved matched {Count(side.ApprovedMatched)}, " +
        $"missing {Count(side.ApprovedMissing)}, unknown {Count(side.ApprovedUnknown)}";

    private static string Row(ReviewRow row) => row switch
    {
        RuleWorkRow rule => rule.Name + ": " + (rule.PercentChange is { } percent
            ? percent.ToString("+0.##;-0.##;0", CultureInfo.InvariantCulture) + "%" : rule.Status),
        WordRow word => word.Word + " (" + word.CaseKey + ")",
        ReadingRow reading => $"{reading.Side} reading {reading.IdentityDigest} ({reading.IdentityState}): " +
            string.Join("; ", reading.Morphs.Select(morph => $"{morph.Form} [{morph.FormId}, {morph.MsaId}] {morph.Gloss}")) +
            " expectations: " + string.Join(", ", reading.ExpectationIds) + " sources: " + string.Join(", ", reading.SourceRefs),
        ExpectationRow expectation => $"{expectation.Id} ({expectation.Assertion}, {expectation.Reference}; " +
            $"{expectation.ActorId}, {expectation.RevisionId}): {expectation.Before} before; {expectation.After} after; " +
            "reading " + expectation.ReadingIdentityDigest + "; sources: " + string.Join(", ", expectation.SourceRefs),
        SourceRow source => $"{source.Id}: {source.Citation} {source.Locator} {source.Excerpt} ({source.ActorId})",
        DiagnosticRow warning => warning.Code + ": " + warning.Description,
        FindingRow finding => finding.MeasureId + ": " + finding.FindingId,
        _ => row.ToString() ?? ""
    };
}
