using System;
using System.Globalization;
using System.Text;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Worker.Jobs;
using ProjectionText = SIL.Motif.Projection.Rendering.CommandTextRenderer;

namespace SIL.Motif.Cli.Rendering;

/// <summary>
/// Turns a Corpus, configuration, Report, comparison, or job command's typed
/// <see cref="CommandOutcome{T}"/> into the same human text and <c>--json</c> shape the pre-typed
/// handlers used to render themselves. A <see cref="Refusal"/> becomes a <see cref="FailureEnvelope"/>
/// only here — a command handler never constructs one.
/// </summary>
public static class CommandTextRenderer
{
    /// <param name="successAsJson">
    /// Whether a successful result renders as JSON. A job just enqueued never grew a JSON success shape
    /// and must keep printing its bare id even when the caller passed <c>--json</c>; a refusal always
    /// follows <paramref name="asJson"/> regardless.
    /// </param>
    public static CommandResult Render<T>(CommandOutcome<T> outcome, bool asJson, bool successAsJson = true)
        where T : class =>
        outcome.Succeeded
            ? RenderSuccess(outcome.Value!, asJson && successAsJson)
            : RenderRefusal(outcome.Refusal!, asJson);

    private static CommandResult RenderSuccess<T>(T value, bool asJson) where T : class
    {
        if (asJson)
            return new CommandResult(0, ProjectionJson.Serialize(value) + Environment.NewLine);

        var text = value switch
        {
            CorpusListProjection p => ProjectionText.Render(p),
            CorpusDetailProjection p => ProjectionText.Render(p),
            ProjectConfigurationProjection p => ProjectionText.Render(p),
            DryRunProjection p => ProjectionText.Render(p),
            CorpusAddedResponse r => RenderCorpusAdded(r),
            CorpusDocumentAddedResponse r => RenderCorpusDocumentAdded(r),
            CorpusBundleAddedResponse r => RenderCorpusBundleAdded(r),
            ReportKindListResponse r => RenderReportKindList(r),
            ReportResponse r => RenderReport(r),
            CompareResponse r => RenderCompare(r),
            JobEnqueuedResponse r => r.JobId + Environment.NewLine,
            JobStatusResponse r => RenderJobStatus(r),
            JobQueueListResponse r => RenderJobQueueList(r),
            JobAssessmentsResponse r => RenderJobAssessments(r),
            BaselineCaptureResponse r => RenderBaselineCaptured(r),
            AssessCommandResponse r => RenderAssessed(r),
            StatsCommandResponse r => RenderStats(r),
            DefaultSelectionResponse r => RenderDefaultSelection(r),
            ProjectSetupResponse r => r.SetupSkipped
                ? "Setup skipped for this project." + Environment.NewLine
                : "Setup remains open." + Environment.NewLine,
            TextInventoryResponse r => RenderTextInventory(r),
            OverviewResponse r => RenderOverview(r),
            WarningsResponse r => RenderWarnings(r),
            TimingResponse r => RenderTiming(r),
            HandoffCommandResponse r => RenderHandoff(r),
            _ => throw new NotSupportedException($"No text rendering registered for '{typeof(T)}'."),
        };
        return new CommandResult(0, text);
    }

    // Corpus's own refusals never carried an "error: " prefix before they were typed; preserved verbatim.
    private static CommandResult RenderRefusal(Refusal refusal, bool asJson)
    {
        if (asJson)
        {
            var envelope = new FailureEnvelope(
                refusal.Reason, refusal.Message, refusal.Facts.Count > 0 ? refusal.Facts : null, refusal.Code);
            return new CommandResult(
                FailureEnvelope.ExitCodeFor(refusal.Reason),
                ProjectionJson.Serialize(envelope) + Environment.NewLine, refusal.Reason);
        }

        var prefix = refusal.Code.StartsWith("corpus.", StringComparison.Ordinal) ? "" : "error: ";
        return new CommandResult(
            FailureEnvelope.ExitCodeFor(refusal.Reason), prefix + refusal.Message + Environment.NewLine,
            refusal.Reason);
    }

    private static string RenderCorpusAdded(CorpusAddedResponse r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Created corpus '{r.CorpusId}'.");
        sb.AppendLine($"  Origin:       {r.Description}");
        if (!string.IsNullOrWhiteSpace(r.Uri)) sb.AppendLine($"  Location:     {r.Uri}");
        sb.AppendLine($"  Licence:      {r.Licence ?? "(none recorded)"}");
        sb.AppendLine($"  Tokenisation: {r.Tokeniser} {r.TokeniserVersion}");
        sb.AppendLine();
        sb.AppendLine(r.DerivationNote);
        sb.AppendLine();
        sb.AppendLine(
            "Add documents with: motif add-document --corpus " + r.CorpusId + " --doc <id> --source <file-or-url>");
        return sb.ToString();
    }

    private static string RenderCorpusDocumentAdded(CorpusDocumentAddedResponse r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Added document '{r.DocumentId}' to corpus '{r.CorpusId}'.");
        sb.AppendLine($"  Title:      {r.Title}");
        sb.AppendLine($"  Source:     {r.Source}");
        sb.AppendLine($"  Characters: {r.CharacterCount:N0}");
        sb.AppendLine($"  SHA-256:    {r.ContentSha256}");
        if (!string.IsNullOrWhiteSpace(r.Licence)) sb.AppendLine($"  Licence:    {r.Licence}");
        return sb.ToString();
    }

    private static string RenderCorpusBundleAdded(CorpusBundleAddedResponse r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Ingested corpus '{r.CorpusId}' from bundle: {r.DocumentCount} document(s).");
        sb.AppendLine($"  Origin:  {r.OriginDescription}");
        sb.AppendLine($"  Licence: {r.Licence ?? "(none recorded)"}");
        sb.AppendLine();
        sb.AppendLine(r.DerivationRestrictions);
        sb.AppendLine();
        sb.AppendLine(r.AccuracyClaimsNote);
        return sb.ToString();
    }

    private static string RenderReportKindList(ReportKindListResponse response)
    {
        var text = new StringBuilder();
        if (response.Kinds.Count == 0)
        {
            text.AppendLine("No report kinds registered.");
            return text.ToString();
        }
        foreach (var kind in response.Kinds)
            text.AppendLine(kind.Kind + "  " + kind.Description);
        return text.ToString();
    }

    private static string RenderReport(ReportResponse response)
    {
        var text = new StringBuilder();
        text.AppendLine("Report " + response.ReportId);
        text.AppendLine("  Assessment: " + response.AssessmentId);
        text.AppendLine("  Kind:       " + response.Kind);
        text.AppendLine(response.Text);
        return text.ToString();
    }

    private static string RenderCompare(CompareResponse response)
    {
        var text = new StringBuilder();
        text.AppendLine("Comparison " + response.AssessmentId);
        text.Append(response.Text);
        return text.ToString();
    }

    private static string RenderJobStatus(JobStatusResponse response)
    {
        var text = new StringBuilder();
        text.AppendLine("Job " + response.JobId);
        text.AppendLine("  Kind:    " + response.Kind);
        text.AppendLine("  Status:  " + response.Status);
        text.AppendLine("  Attempt: " + response.Attempt);
        text.AppendLine("  Updated: " + response.UpdatedUtc);
        if (response.QueueOrder is { } queueOrder)
            text.AppendLine("  Queue order: " + queueOrder.ToString("R"));
        return text.ToString();
    }

    private static string RenderJobQueueList(JobQueueListResponse response)
    {
        var text = new StringBuilder();
        if (response.Jobs.Count == 0)
        {
            text.AppendLine("No active jobs.");
            return text.ToString();
        }
        for (var index = 0; index < response.Jobs.Count; index++)
        {
            var job = response.Jobs[index];
            text.AppendLine((index + 1) + ". " + job.JobId + "  " + job.Kind + "  " +
                JobStatusJson.ToWire(job.Status) + "  " + job.ProjectPath);
        }
        return text.ToString();
    }

    private static string RenderBaselineCaptured(BaselineCaptureResponse response)
    {
        var text = new StringBuilder();
        text.AppendLine("Captured Baseline for " + response.FwDataPath);
        text.AppendLine("  Project identity: " + response.Token.ProjectIdentity);
        text.AppendLine("  Bundle digest:    " + response.Token.BundleDigest);
        text.AppendLine("  Captured:         " + response.Token.CapturedUtc);
        text.AppendLine("  Last save:        " + response.SourceLastWriteUtc.ToString("O") +
            "  (as of FieldWorks' last save)");
        text.AppendLine("  FieldWorks:       " + (response.FieldWorksHeldProject
            ? "holds the project open"
            : "does not currently hold the project"));
        text.AppendLine(response.ReusedExistingBytes
            ? "  The saved bytes matched the current Baseline; nothing new was written."
            : "  A new Baseline was captured and published.");
        if (response.RegistrationFailure is not null)
            text.AppendLine("  Warning: " + response.RegistrationFailure.Message);
        return text.ToString();
    }

    private static string RenderAssessed(AssessCommandResponse response)
    {
        var text = new StringBuilder();
        text.AppendLine("Assessed " + response.Baseline.FwDataPath);
        text.AppendLine("  Last save: " + response.Baseline.SourceLastWriteUtc.ToString("O") +
            "  (as of FieldWorks' last save)");
        text.AppendLine("  Selection: " + response.Selection.Words.Count + " word(s)");
        foreach (var entry in response.Selection.Provenance)
            text.AppendLine("    " + entry.Source + ": " + entry.Count);
        text.AppendLine("  Assessments: " + string.Join(", ", response.AssessmentIds));
        text.AppendLine();
        text.Append(response.SummaryMarkdown);
        return text.ToString();
    }

    // Human-mode dispatch always asks for text output, so response.Text is always populated here.
    private static string RenderStats(StatsCommandResponse response) => response.Text ?? string.Empty;

    private static string RenderDefaultSelection(DefaultSelectionResponse response)
    {
        if (response.Selection is null)
            return "No default Selection is saved." + Environment.NewLine +
                (response.SetupSkipped ? "Setup skipped for this project." + Environment.NewLine : string.Empty);
        var selection = response.Selection;
        return $"Default Selection: {selection.Name}{Environment.NewLine}" +
            $"  Texts:       {selection.TextIds.Count:N0}{Environment.NewLine}" +
            $"  Added words: {selection.AddedWords.Count:N0}{Environment.NewLine}" +
            $"  Time limit:  {selection.PerWordLimitMs:N0} ms{Environment.NewLine}" +
            $"  Step limit:  {selection.PerWordStepLimit?.ToArgument() ?? "project default"}{Environment.NewLine}" +
            $"  Updated:     {selection.UpdatedUtc}{Environment.NewLine}";
    }

    private static string RenderTextInventory(TextInventoryResponse response)
    {
        if (!response.HasBaseline) return "Capture a Baseline to choose Texts." + Environment.NewLine;
        if (response.Texts.Count == 0) return "This Baseline has no Texts." + Environment.NewLine;
        return string.Join(Environment.NewLine, response.Texts.Select(text =>
            $"{text.Title}  {text.InterlinearizationPercent:0}% interlinearized  {text.Id:D}")) + Environment.NewLine;
    }

    private static string RenderOverview(OverviewResponse response)
    {
        var text = new StringBuilder();
        var fileName = string.IsNullOrWhiteSpace(response.ProjectFileName)
            ? response.ProjectName
            : response.ProjectFileName;
        var freshness = response.BaselineCapturedUtc is null
            ? "No Baseline"
            : response.IsStale ? "Saved since Baseline" : "Current";
        var baselineTime = response.BaselineCapturedUtc?.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);
        var savedTime = response.LastFieldWorksSaveUtc?.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture)
            ?? "unknown";
        var timestamps = baselineTime is null ? string.Empty
            : $" (Baseline {baselineTime}, FieldWorks saved {savedTime})";
        text.AppendLine($"{response.ProjectName}  {fileName}  {freshness}{timestamps}");
        if (response.SelectionResolved)
            text.AppendLine($"Selection default: {response.SelectionTextCount:N0} text + " +
                $"{response.SelectionAddedWordCount:N0} words = {response.SelectionWordCount:N0} words, " +
                $"{response.TextOccurrenceCount:N0} occurrences");
        else
            text.AppendLine("Selection default: not resolved against this Baseline");
        text.AppendLine($"Project    {response.WordformCount:N0} wordforms  {response.RuleCount:N0} rules  {response.LexemeCount:N0} lexemes");
        text.AppendLine($"Fingerprints baseline {ShortHash(response.BaselineToken?.SemanticSnapshotDigest)}  " +
            $"grammar {ShortHash(response.GrammarFingerprint)}  Selection {ShortHash(response.SelectionFingerprint)}");
        var coverage = FormatPercent(response.WordCoveragePercent);
        var occurrenceCoverage = FormatPercent(response.TextCoverage.OccurrenceCoveragePercent);
        text.AppendLine($"Coverage   {response.TextCoverage.ParsedWords:N0}/{response.SelectionWordCount:N0} parse ({coverage})  " +
            $"{response.TextCoverage.NoParseWords:N0} no parse  {response.TextCoverage.UnknownWords:N0} limit  " +
            $"{response.TextCoverage.SkippedWords:N0} skipped");
        text.AppendLine($"           {response.TextCoverage.ParsedOccurrences:N0}/{response.TextCoverage.TotalOccurrences:N0} occurrences covered ({occurrenceCoverage})");
        text.AppendLine($"Accuracy   {response.Accuracy.ApprovedWordsKept:N0}/{response.Accuracy.ApprovedWordCount:N0} approved kept  " +
            $"{response.Accuracy.Violations:N0} violations  {response.Accuracy.UnknownWords:N0} unknown");
        text.AppendLine($"           rejected analyses rebuilt {response.Accuracy.RejectedAnalysesRebuilt:N0}  " +
            $"rejected words matched {response.Accuracy.RejectedWordsInMatchCell:N0}/{response.Accuracy.RejectedWordCount:N0}  " +
            $"candidates confirmed {response.Accuracy.CandidatesConfirmed:N0}/{response.Accuracy.CandidateWordCount:N0}");
        var slowest = response.Timing.SlowestWords.FirstOrDefault();
        text.AppendLine($"Timing     median {FormatMs(response.Timing.MedianMs)}  p95 {FormatMs(response.Timing.Percentile95Ms)}  " +
            (slowest is null ? "slowest (none)" : $"slowest {slowest.Word} {slowest.ElapsedMs:N0} ms"));
        if (response.Warnings is not null)
            text.AppendLine($"Warnings   {response.Warnings.Count?.ToString("N0") ?? "unknown"} findings");
        return text.ToString();
    }

    private static string RenderWarnings(WarningsResponse response)
    {
        if (!response.HasBaseline) return "No Baseline has been captured." + Environment.NewLine;
        if (!response.HasCheck) return "Grammar not checked yet for this Baseline." + Environment.NewLine;
        var text = new StringBuilder();
        text.AppendLine($"Grammar findings: {response.TotalCount:N0} " +
            $"({response.WarningCount:N0} warnings, {response.InformationCount:N0} information)");
        foreach (var kind in response.ByKind)
            text.AppendLine($"  {kind.Code}: {kind.Count:N0} {kind.Level.ToWireValue()}");
        foreach (var finding in response.Findings)
            text.AppendLine($"  {finding.Text}");
        return text.ToString();
    }

    private static string RenderTiming(TimingResponse response)
    {
        var text = new StringBuilder();
        text.AppendLine($"Timing for {response.WordCount:N0} word(s) from {response.WordSet} ({response.AssessmentId})");
        if (response.IsStale)
            text.AppendLine("  Warning: FieldWorks has changed since the current Baseline.");
        text.AppendLine($"  Median: {FormatMs(response.MedianMs)}  p95: {FormatMs(response.Percentile95Ms)}");
        text.AppendLine($"  By {response.By}:");
        foreach (var row in response.Aggregates)
            text.AppendLine($"    {row.Name}: {row.ElapsedMs:N2} ms ({row.ShareOfTotal:P1}), " +
                $"{row.Attempts:N0} attempts, {row.WordsTouched:N0} words");
        if (response.CostliestWords.Count > 0)
        {
            text.AppendLine("  Costliest words:");
            foreach (var word in response.CostliestWords)
                text.AppendLine($"    {word.Word}: {word.ElapsedMs:N2} ms, {word.Attempts:N0} attempts");
        }
        return text.ToString();
    }

    private static string FormatPercent(double? value) => value is { } percent
        ? percent.ToString("N0", CultureInfo.CurrentCulture) + "%"
        : "not recorded";

    private static string FormatMs(double? value) => value is null ? "unknown" : value.Value.ToString("N0") + " ms";

    private static string ShortHash(string? value) => value is null ? "unknown" : value[..Math.Min(8, value.Length)] + "…";

    private static string RenderHandoff(HandoffCommandResponse response)
    {
        var text = new StringBuilder();
        text.AppendLine("Wrote Handoff folder to " + response.OutputDirectory);
        text.AppendLine("  Last save:   " + response.Baseline.SourceLastWriteUtc.ToString("O") +
            "  (as of FieldWorks' last save)");
        text.AppendLine("  Selection:   " + response.Selection.Words.Count + " word(s)");
        text.AppendLine("  Files:       " + response.Files.Count);
        text.AppendLine("  Assessments: " + (response.AssessmentIds.Count == 0
            ? "(none; --no-assess)"
            : string.Join(", ", response.AssessmentIds)));
        return text.ToString();
    }

    private static string RenderJobAssessments(JobAssessmentsResponse response)
    {
        var text = new StringBuilder();
        text.AppendLine("Job " + response.JobId);
        if (response.Assessments.Count == 0)
        {
            text.AppendLine("  No Assessments.");
            return text.ToString();
        }
        foreach (var assessment in response.Assessments)
        {
            text.AppendLine("  " + assessment.AssessmentId + "  " + assessment.Assessor + "  " +
                assessment.Kind + "  " + assessment.SavedUtc);
        }
        return text.ToString();
    }
}
