using System.Globalization;
using System.Text;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Review;

namespace SIL.Motif.Cli.Rendering;

/// <summary>Renders the shared Trial review without changing its evidence.</summary>
public static class TrialReviewTextRenderer
{
    private const int TextListLimit = 10;
    private const int RegressionTextWords = 2;

    /// <summary>Renders a saved report, pending Trial, or absence of a Trial.</summary>
    public static string Render(TrialReviewResult result) => result switch
    {
        NoTrialResult => "No Trial is recorded for this Proposal.\n",
        PendingTrialResult pending => $"Trial {pending.JobId}: {pending.JobState}\n",
        TrialJobResult job => $"Trial {job.JobId}: {JobStatusJson.ToWire(job.JobStatus)}" +
            (job.ReportId is null ? "\n" : $", report {job.ReportId} ({job.ReportDigest})\n"),
        AvailableTrialResult available => RenderReport(available.Report),
        _ => throw new ArgumentException("Unknown Trial review result.", nameof(result))
    };

    internal static string RenderRuleAttempts(RuleAttemptSummary attempts)
    {
        var text = new StringBuilder();
        AppendRuleAttempts(text, attempts);
        return text.ToString();
    }

    private static string RenderReport(ReportView report)
    {
        var text = new StringBuilder();
        if (report.Summary.Flags.Count == 0) text.AppendLine("No red flags observed.");
        else
            foreach (var flag in report.Summary.Flags)
                text.AppendLine(flag.Text + ": " + Flag(flag));
        text.AppendLine(report.Summary.Notice);
        if (report.Gate is { } gate)
        {
            text.AppendLine("Gate: " + gate.State.ToString().ToLowerInvariant() + (gate.Reason is null ? "" : "; " + gate.Reason));
            if (gate.ExpectedLosses.Count > 0) text.AppendLine("Expected losses: " + Capped(gate.ExpectedLosses));
            if (gate.UnexpectedLosses.Count > 0) text.AppendLine("Unexpected losses: " + Capped(gate.UnexpectedLosses));
        }
        if (report.Regressions is { } regressions) AppendRegressions(text, regressions);
        if (report.Losses is { } losses)
        {
            if (losses.NewNoParseWords.Count > 0) text.AppendLine("New no-parse words: " + string.Join(", ", losses.NewNoParseWords));
            if (losses.NewApprovedLossWords.Count > 0) text.AppendLine("New Approved losses: " + string.Join(", ", losses.NewApprovedLossWords));
            foreach (var reading in losses.NewlyLostReadings)
                text.AppendLine("Lost reading: " + reading.Word + "; " + reading.ReadingIdentity +
                    (reading.Count is { } copies ? "; " + copies + " lost, " + reading.RemainingReadingCount + " lexicon-backed remaining" : ""));
            if (losses.RetirementAttestation is { } attestation)
                text.AppendLine("Retirement multiset of " + attestation.WaivedWords.Count + " waived word(s): " +
                    (attestation.Complete ? "complete" : "incomplete (" + string.Join(", ", attestation.Limitations) + ")") +
                    "; " + attestation.LostCopies + " lost copies across " + attestation.CaseCount + " case(s)");
            if (losses.RetirementAttestation is { Page: { } shown, PageCount: { } of })
                text.AppendLine("Retirement rows: page " + shown + " of " + of + " (" + losses.RetirementAttestation.RowCount +
                    " in all); read the others with --page <N>.");
        }
        if (report.HeldWords is { Count: > 0 } held)
            text.AppendLine("New second readings held for review: " + string.Join(", ", held));
        foreach (var stopped in report.StoppedWords ?? [])
            text.AppendLine(stopped.Word + " (" + stopped.Side.ToString().ToLowerInvariant() + "): " +
                string.Join(", ", stopped.Reasons) + "; " + string.Join("; ", stopped.Details));
        text.AppendLine($"Incomplete: {Count(report.Summary.Incomplete.AffectedWords)} wordforms; {Count(report.Summary.Incomplete.RequiredCases)} required cases");
        if (report.Limitations.Count != 0 || report.Summary.Incomplete.EvidenceGapCount != 0)
            text.AppendLine("Some comparisons could not be made; see Incomplete and limitations.");
        text.AppendLine("Wordforms: " + Coverage(report.Summary.Wordforms.Before) + " before; " + Coverage(report.Summary.Wordforms.After) + " after");
        text.AppendLine("Sampled tokens: " + Coverage(report.Summary.SampledTokens.Before) + " before; " + Coverage(report.Summary.SampledTokens.After) + " after");
        text.AppendLine("Paradigm cells: " + Coverage(report.Summary.ParadigmCells.Before) + " before; " + Coverage(report.Summary.ParadigmCells.After) + " after");
        if (report.Summary.Gains is { } gains) AppendGains(text, gains);
        if (report.Summary.RuleAttempts is { Groups.Count: > 0 } attempts) AppendRuleAttempts(text, attempts);
        if (report.Summary.ShadowedAlternates is { } shadowed) AppendShadowedAlternates(text, shadowed);
        if (report.Summary.GdrSync is { InSync: false } gdr) AppendGdrSync(text, gdr);
        text.AppendLine("Parser work: " + (report.Summary.ParserWork.PercentChange is { } change
            ? change.ToString("+0.#;-0.#;0", CultureInfo.InvariantCulture) + "%" : "not comparable"));
        text.AppendLine($"Trial {report.Pins.TrialJobId}; report {report.ReportId}; {report.Freshness.Relation}");
        if (report.Freshness.Reasons.Count > 0)
            text.AppendLine("Freshness: " + string.Join(", ", report.Freshness.Reasons));
        foreach (var section in report.Sections)
        {
            text.AppendLine(section.Label + ":");
            foreach (var group in section.Groups)
            {
                text.AppendLine($"  {group.Cause}: {Count(group.Count)} {group.Unit} ({Count(group.Remaining)} more)" +
                    (group.SamplePositionsTotal is { } positions ? $"; {positions} sampled positions" : ""));
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

    internal static string RenderLossList(IReadOnlyList<string> words) => Capped(words);

    internal static string RenderRegressions(TrialRegressions regressions)
    {
        var text = new StringBuilder();
        AppendRegressions(text, regressions);
        return text.ToString();
    }

    private static string Capped(IReadOnlyList<string> words) => words.Count <= TextListLimit
        ? string.Join(", ", words)
        : string.Join(", ", words.Take(TextListLimit)) + $" and {words.Count - TextListLimit:N0} more; use --json for all";

    private static void AppendRegressions(StringBuilder text, TrialRegressions regressions)
    {
        var parts = regressions.Groups.Select(group =>
        {
            var shown = group.Words.Take(RegressionTextWords).Select(word => $"{word.Word} ({Analyses(word.Before)} -> " +
                (word.AfterReadingCount == 0 && word.After.Count == 0 ? "no analysis" : Analyses(word.After)) + ")").ToList();
            var rest = group.Total - shown.Count;
            return $"{group.Cause}: {group.Total:N0}: {string.Join("; ", shown)}" +
                (rest > 0 ? $"; and {rest:N0} more. Next: {group.NextStep}" : "");
        }).ToList();
        if (parts.Count == 0) return;
        var first = regressions.Groups.SelectMany(group => group.Words).FirstOrDefault();
        var detail = first is null ? "" : $" || First word {first.Word}: " + (first.TouchedBy is null
            ? "the overlap with the Proposal's operations was not computed (see Limitations)"
            : first.TouchedBy.Count == 0
            ? "no Proposal operation names an object its Baseline reading uses (this says nothing about cause)"
            : "touched by " + string.Join("; ", first.TouchedBy.Select(touch =>
                $"operation {touch.Position} ({touch.Kind}) sharing {string.Join(", ", touch.SharedIds)}")) +
              " (an overlap of ids, not a cause)") + $"{(first.TraceBaseline is null ? "" : $". Trace: {first.TraceBaseline} | {first.TraceTrial}")}; --json has every word's ids, overlap and trace.";
        text.AppendLine("Regressed words, " + string.Join(" || ", parts) + detail);
    }

    private static string Analyses(IReadOnlyList<string> analyses) =>
        analyses.Count == 0 ? "not retained" : string.Join(" | ", analyses);

    private static void AppendGains(StringBuilder text, GainSummary gains)
    {
        var size = gains.Size;
        text.AppendLine($"Attested gained: {gains.AttestedGained}; generated gained: {gains.GeneratedGained} (probe forms, not evidence)");
        text.AppendLine($"Grammar size delta: {size.ObjectsAdded} objects (entries {size.Entries}, senses {size.Senses}, " +
            $"MSAs {size.Msas}, allomorphs {size.Allomorphs}, variants {size.Variants}, affix processes {size.AffixProcesses}, " +
            $"phonological rules {size.PhonologicalRules}, environments {size.Environments}, " +
            $"exception features {size.ExceptionFeatures} on {size.ExceptionFeatureHosts} hosts, other {size.OtherObjects})");
        var ratio = gains.ObjectsPerAttestedType is { } value ? value.ToString("0.##", CultureInfo.InvariantCulture) : "none";
        text.AppendLine($"Objects per attested type gained: {ratio}; gain gate {gains.Gate.Mode.ToString().ToLowerInvariant()}: " +
            $"{gains.Gate.Outcome}" +
            (gains.Gate.Hold ? "; HOLD (Autonomous Apply reads this through a declared gate)" : ""));
    }

    // Definite rows read as a warning and maybe rows as a note.
    private static void AppendShadowedAlternates(StringBuilder text, SIL.Motif.Contract.Responses.ShadowedAlternatesReport report)
    {
        var definite = report.Definite.ToArray();
        var maybe = report.Maybe.ToArray();
        if (definite.Length > 0)
        {
            text.AppendLine($"Warning: {definite.Length} shadowed alternate{(definite.Length == 1 ? "" : "s")} can never be selected: " +
                string.Join("; ", definite.Take(TextListLimit).Select(row =>
                    $"{row.Label} is shadowed by {ShadowedBy(row)} ({string.Join(", ", row.Reasons)})")) +
                (definite.Length > TextListLimit ? $"; and {definite.Length - TextListLimit:N0} more; use --json for all" : "") +
                ". Remove the alternate or move it before the one that shadows it and narrow its environment.");
        }
        if (maybe.Length > 0)
        {
            text.AppendLine($"Note: {maybe.Length} alternate{(maybe.Length == 1 ? "" : "s")} may be shadowed (PanGloss could not prove it): " +
                string.Join("; ", maybe.Take(TextListLimit).Select(row =>
                    $"{row.Label} behind {ShadowedBy(row)} ({string.Join(", ", row.Reasons)})")) +
                (maybe.Length > TextListLimit ? $"; and {maybe.Length - TextListLimit:N0} more; use --json for all" : "") + ".");
        }
        foreach (var tier in new[] { SIL.Motif.Contract.Responses.ShadowedAlternateTiers.Definite, SIL.Motif.Contract.Responses.ShadowedAlternateTiers.Maybe })
        {
            var dead = report.DeadContinuations.Where(row => row.Tier == tier).ToArray();
            if (dead.Length == 0) continue;
            text.AppendLine((tier == SIL.Motif.Contract.Responses.ShadowedAlternateTiers.Definite ? "Warning: " : "Note: ") +
                $"{dead.Length} alternate{(dead.Length == 1 ? " is" : "s are")} " +
                (tier == SIL.Motif.Contract.Responses.ShadowedAlternateTiers.Definite ? "selected but rejected by every following affix: "
                    : "possibly selected but rejected by every following affix: ") +
                string.Join("; ", dead.Take(TextListLimit).Select(row =>
                    row.Label + (row.BlockedBy.Count > 0 ? $" ({string.Join(", ", row.BlockedBy)})" : ""))) +
                (dead.Length > TextListLimit ? $"; and {dead.Length - TextListLimit:N0} more; use --json for all" : "") + ".");
        }
    }

    private static string ShadowedBy(SIL.Motif.Contract.Responses.ShadowedAlternate row) => row.ShadowedBy.Count == 0
        ? "an earlier alternate" : string.Join(", ", row.ShadowedBy.Select(form => $"'{form}'"));

    private static void AppendGdrSync(StringBuilder text, SIL.Motif.Contract.Responses.GdrCheckResponse gdr)
    {
        foreach (var finding in gdr.Findings)
            text.AppendLine("Guide check (advisory, incomplete until fixed): " + finding.Message);
    }

    private static void AppendRuleAttempts(StringBuilder text, RuleAttemptSummary attempts)
    {
        if (attempts.Groups.Count == 0)
            text.AppendLine($"No same-shape group met the {attempts.Threshold}-variant threshold " +
                $"({attempts.ShapesEvaluated:N0} added objects checked).");
        foreach (var group in attempts.Groups.Take(TextListLimit))
            text.AppendLine($"Rule attempt: {group.Count} added variants share shape {group.ShapeKey}; " +
                (group.AttemptRecorded ? "attempt recorded" : "no rule attempt recorded (advisory)"));
        if (attempts.Groups.Count > TextListLimit)
            text.AppendLine($"  and {attempts.Groups.Count - TextListLimit:N0} more; use --json for all");
        if (attempts.Hold) text.AppendLine("HOLD: rule attempt required (opted in; Autonomous Apply reads this through a declared gate)");
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
        $"missing {Count(side.ApprovedMissing)}, unknown {Count(side.ApprovedUnknown)}" +
        (side.StopDetails is { Count: > 0 } details ? "; " + string.Join("; ", details) : "");

    private static string Row(ReviewRow row) => row switch
    {
        RuleWorkRow rule => rule.Name + ": " + (rule.PercentChange is { } percent
            ? percent.ToString("+0.##;-0.##;0", CultureInfo.InvariantCulture) + "%" : rule.Status),
        WordRow word => word.Word + " (" + word.CaseKey + ")" +
            (word.Work.PercentChange is { } change ? " " + change.ToString("+0.##;-0.##;0", CultureInfo.InvariantCulture) + "%" : "") +
            (word.SamplePositions is { } positions ? $"; {Count(word.SamplePositionsTotal)} positions: " +
                string.Join(", ", positions.Select(position => position.OccurrenceId + " " + position.SourceLocator)) : ""),
        ReadingRow reading => $"{reading.Side} reading" + (reading.IdentityDigest is { } id ? " " + id : "") +
            $" ({reading.IdentityState}): " +
            string.Join("; ", reading.Morphs.Select(morph => $"{morph.Form} [{morph.FormId}, {morph.MsaId}] {morph.Gloss}")) +
            " expectations: " + string.Join(", ", reading.ExpectationIds) + " sources: " + string.Join(", ", reading.SourceRefs),
        ExpectationRow expectation => $"{expectation.Id} ({expectation.Assertion}, {expectation.Reference}; " +
            $"{expectation.ActorId}, {expectation.RevisionId}): {expectation.Before} before; {expectation.After} after; " +
            "reading " + expectation.ReadingIdentityDigest + "; context: " + expectation.Context + "; sources: " + string.Join(", ", expectation.SourceRefs),
        SourceRow source => $"{source.Id}: {source.Citation} {source.Locator} {source.Excerpt} ({source.ActorId})",
        DiagnosticRow warning => warning.Code + ": " + warning.Description,
        FindingRow finding => finding.MeasureId + ": " + finding.FindingId,
        _ => row.ToString() ?? ""
    };
}
