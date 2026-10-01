using System;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Commands.Catalog;
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
            return new CommandResult(0, (value is WordTraceResponse trace ? TraceJson(trace) : ProjectionJson.Serialize(value)) +
                Environment.NewLine);

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
            ReviewNumbersResponse r => RenderReviewNumbers(r),
            MeasurePendingResult r => RenderReviewNumbers(r.Numbers),
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
            GrammarCheckResponse r => RenderGrammarCheck(r),
            WarningsResponse r => RenderWarnings(r),
            TimingResponse r => RenderTiming(r),
            ObjectUsesResponse r => RenderUses(r),
            HandoffCommandResponse r => RenderHandoff(r),
            WordTraceResponse r => RenderTrace(r),
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

    private static string RenderReviewNumbers(ReviewNumbersResponse numbers) => (numbers.Comparability switch
    {
        ReviewComparability.NoEarlierAssessment =>
            $"{numbers.TouchedWordsCovered} of {numbers.TouchedWordCount} touched words kept their approved " +
            "analyses. There is no earlier Assessment to compare.",
        ReviewComparability.DifferentAssessor =>
            "These Assessments were made by different Assessors and cannot be compared.",
        ReviewComparability.NoSharedWords =>
            "The regression check could not compare these Assessments because they share no words.",
        ReviewComparability.Compared =>
            $"Among {numbers.SharedWordCount} shared {(numbers.SharedWordCount == 1 ? "word" : "words")}, " +
            $"approved kept: {numbers.ApprovedKeptBefore} → {numbers.ApprovedKeptAfter}.",
        _ => throw new ArgumentOutOfRangeException(nameof(numbers), numbers.Comparability, null),
    }) + Environment.NewLine;

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
            $"  Time limit:  {(selection.PerWordLimitMs is { } timeLimit ? $"{timeLimit:N0} ms" : "none")}{Environment.NewLine}" +
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
        if (response.Timing.Attribution.MeasuredWordCount > 0)
        {
            var attribution = response.Timing.Attribution;
            var split = response.Timing.Kinds.Select(kind => $"{kind.Name} {FormatShare(kind.ShareOfWordTime)}");
            if (attribution.NotAttributedShare is { } notAttributed)
                split = split.Append($"not attributed {FormatShare(notAttributed)}");
            text.AppendLine($"           {attribution.MeasuredWordCount:N0} words, {attribution.WordTimeMs / 1000:N1} s " +
                $"total word time: {string.Join(", ", split)}");
        }
        if (response.Warnings is not null)
            text.AppendLine($"Warnings   {response.Warnings.Count?.ToString("N0") ?? "unknown"} findings " +
                $"({CountLabel(response.Warnings.ErrorCount, "error", "errors")}, " +
                $"{CountLabel(response.Warnings.WarningCount, "warning", "warnings")}, " +
                $"{response.Warnings.InformationCount?.ToString("N0") ?? "unknown"} information)");
        if (response.Warnings?.YourWords is { } touched)
            text.AppendLine($"           {TouchedLine(touched)}");
        return text.ToString();
    }

    private static string RenderWarnings(WarningsResponse response)
    {
        if (!response.HasBaseline)
            return "No Baseline has been captured. Run: motif baseline capture <fwdata>" + Environment.NewLine;
        if (!response.HasCheck)
            return "Grammar not checked yet for this Baseline. Run: motif grammar check --project <fwdata>" +
                Environment.NewLine;
        var text = new StringBuilder();
        text.AppendLine($"Grammar findings: {response.TotalCount:N0} " +
            $"({CountLabel(response.ErrorCount, "error", "errors")}, {CountLabel(response.WarningCount, "warning", "warnings")}, " +
            $"{response.InformationCount:N0} information)");
        if (response.YourWords is { } touched)
            text.AppendLine(TouchedLine(touched));
        foreach (var kind in response.ByKind)
            text.AppendLine($"  {kind.Code}: {kind.Count:N0} {kind.Level.ToWireValue()}" +
                (kind.YourWords is { } words ? $", {words:N0} of your words" : string.Empty));
        foreach (var finding in response.Findings)
        {
            text.AppendLine($"  {finding.Text}");
            if (finding.YourWords is { } yours)
                text.AppendLine($"    {YourWordsLine(yours)}");
        }
        return text.ToString();
    }

    private static string TouchedLine(WarningWordsTouched touched) =>
        $"{touched.Words:N0} of your words use something a finding names ({touched.NoParse:N0} don't parse" +
        (touched.BySpellingOnly > 0 ? $", {touched.BySpellingOnly:N0} matched only by spelling)" : ")");

    private static string YourWordsLine(WarningWords yours) => yours.Match switch
    {
        WarningWordsMatch.CantTell => "Your words: can't tell, " + yours.CantTell switch
        {
            WarningCantTell.NotInProject => "the object it names is not in the project",
            WarningCantTell.KindNotFollowed => "Motif doesn't follow this kind of object to words",
            _ => "PanGloss names no object",
        },
        _ when yours.Words.Count == 0 => "Your words: none in the Selection",
        WarningWordsMatch.Spelling => "Your words, matched by spelling: " + WordList(yours.Words),
        _ => "Your words: " + WordList(yours.Words),
    };

    private static string WordList(IReadOnlyList<ObjectUseWord> words)
    {
        const int shown = 10;
        var listed = string.Join(", ", words.Take(shown).Select(word => $"{word.Row.Word} ({word.Row.Meaning})"));
        return words.Count > shown ? $"{listed} and {words.Count - shown:N0} more" : listed;
    }

    private static string RenderGrammarCheck(GrammarCheckResponse response)
    {
        if (!response.HasBaseline)
            return "No Baseline has been captured. Run: motif baseline capture <fwdata>" + Environment.NewLine;

        var errorCount = response.Findings.Count(finding => finding.Severity == GrammarDiagnosticLevel.Error);
        var warningCount = response.Findings.Count(finding => finding.Severity == GrammarDiagnosticLevel.Warning);
        var informationCount = response.Findings.Count(finding => finding.Severity == GrammarDiagnosticLevel.Information);
        var text = new StringBuilder();
        text.AppendLine($"Grammar findings: {response.Findings.Count:N0} " +
            $"({CountLabel(errorCount, "error", "errors")}, {CountLabel(warningCount, "warning", "warnings")}, " +
            $"{informationCount:N0} information)");
        foreach (var summary in response.Summary)
            text.AppendLine($"  {summary.Code}: {summary.Count:N0} {summary.Level.ToWireValue()}");
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
        var attribution = response.Attribution;
        text.AppendLine($"  Total word time: {attribution.WordTimeMs:N2} ms for {attribution.MeasuredWordCount:N0} " +
            "measured word(s); every share below is of this total");
        text.AppendLine($"  By {response.By}:");
        foreach (var row in response.Aggregates)
            text.AppendLine($"    {row.Name}{(response.By == "rule" ? $" [{row.Key}]" : string.Empty)}: " +
                $"{row.SelfMs:N2} ms ({FormatShare(row.ShareOfWordTime)}), {FormatCalls(row.Calls, row.Kind)}, " +
                $"{row.WordsTouched:N0} words");
        if (attribution.NotAttributedMs is { } notAttributed)
            text.AppendLine($"    Not attributed: {notAttributed:N2} ms ({FormatShare(attribution.NotAttributedShare)})");
        if (attribution.Overrun)
            text.AppendLine($"  Warning: objects recorded {attribution.OverrunMs:N2} ms more than their words' own time.");
        if (response.CostliestWords.Count > 0)
        {
            text.AppendLine("  Costliest words:");
            foreach (var word in response.CostliestWords)
                text.AppendLine($"    {word.Word}: {word.SelfMs:N2} ms" + (word.WordTimeMs is { } whole
                    ? $" ({FormatShare(word.ShareOfWordTime)} of its {whole:N2} ms)" : string.Empty) +
                    $", {FormatCalls(word.Calls, null)}");
        }
        return text.ToString();
    }

    private static string FormatShare(double? share) => share is { } value
        ? value.ToString("P1", CultureInfo.CurrentCulture) : "no word time";

    private static string FormatCalls(long? calls, string? kind) => calls is { } value
        ? $"{value:N0} {(kind is { Length: > 0 } ? kind + " " : string.Empty)}calls" : "calls not counted";

    // The words that use the object, the words it ran in, then what the asked-about words share.
    private static string RenderUses(ObjectUsesResponse response)
    {
        var text = new StringBuilder();
        var name = response.Ref?.Label ?? response.Ref?.TimingKey ?? response.Ref?.GrammaticalInfoId ?? response.Ref?.AllomorphId;
        text.AppendLine(name is null ? $"Uses ({response.AssessmentId})" : $"Uses of {name} ({response.AssessmentId})");
        if (response.IsStale)
            text.AppendLine("  Warning: FieldWorks has changed since the current Baseline.");
        if (response.Facts is { } facts)
            AppendFacts(text, facts);
        if (response.Uses is { } uses)
            AppendUseWords(text, "Your words that use it", uses);
        if (response.RanIn is { } ranIn)
            AppendUseWords(text, "Words it ran in", ranIn);
        if (response.Shared is { } shared)
        {
            text.AppendLine(shared.Count == 0 ? "  These words share no morpheme." : "  What these words share:");
            foreach (var morpheme in shared)
                text.AppendLine($"    {morpheme.Morpheme.Form} {morpheme.Morpheme.Gloss}: {CountLabel(morpheme.Count, "word", "words")} " +
                    $"({string.Join(", ", morpheme.Words)})");
        }
        if (response.UnknownWords.Count > 0)
            text.AppendLine("  Not in this Assessment: " + string.Join(", ", response.UnknownWords));
        return text.ToString();
    }

    private static void AppendFacts(StringBuilder text, ObjectFacts facts)
    {
        static string In(TraceFieldWorksTarget? target) => target is null ? "" : ", in " + target.ToolName;
        static string Joined(params string?[] parts) => string.Join(", ", parts.Where(part => !string.IsNullOrEmpty(part)));
        text.AppendLine("  In FieldWorks, from the Baseline:");
        if (facts.Entry is { } entry)
            text.AppendLine($"    Entry: {entry.Headword}" + (entry.MorphType is null ? "" : $" ({entry.MorphType})") + In(entry.FieldWorks));
        foreach (var sense in facts.Senses)
            text.AppendLine($"    Sense {sense.Number}: " + Joined(sense.Gloss, sense.Definition));
        if (facts.GrammaticalInfo is { } info)
        {
            text.AppendLine("    Grammatical info: " + Joined(info.Kind, info.Category?.Name,
                info.ResultCategory is { } result ? "makes " + result.Name : null) + In(info.Category?.FieldWorks));
            foreach (var slot in info.Slots)
                text.AppendLine($"    Slot: {slot.Name}" + (slot.Optional ? " (optional)" : "") +
                    (slot.Templates.Count == 0 ? "" : ", in " + string.Join(", ", slot.Templates.Select(template => template.Name))));
            if (info.RequiredFeatures is { } required) text.AppendLine($"    Requires: {required.Notation}");
            if (info.AddedFeatures is { } added) text.AppendLine($"    Adds: {added.Notation}");
        }
        foreach (var allomorph in facts.Allomorphs)
            text.AppendLine($"    Allomorph: {allomorph.Form}" +
                string.Concat(allomorph.Environments.Select(environment => " " + environment.Notation)) +
                (allomorph.RequiredFeatures is { } needs ? $", requires {needs.Notation}" : "") +
                (allomorph.IsAsked ? " (this one)" : ""));
        if (facts.Rule is { } rule)
            text.AppendLine($"    Rule: {rule.Kind} {rule.Name}" + In(rule.FieldWorks));
    }

    private static void AppendUseWords(StringBuilder text, string heading, ObjectUseWords words)
    {
        text.AppendLine($"  {heading}: {CountLabel(words.Words.Count, "word", "words")}" + (words.ByMeaning.Count == 0 ? "" :
            " (" + string.Join(", ", words.ByMeaning.Select(meaning => $"{meaning.Meaning} {meaning.Words:N0}")) + ")"));
        foreach (var word in words.Words)
        {
            var timing = word.Calls is null && word.ElapsedNs is null ? "" :
                $", {CountLabel(word.Calls, "call", "calls")}, " +
                (word.ElapsedNs is { } ns ? (ns / 1_000_000d).ToString("N3", CultureInfo.CurrentCulture) + " ms" : "time not recorded");
            text.AppendLine($"    {word.Row.Word}: {word.Row.Meaning}{timing}");
        }
    }

    private static string CountLabel(int? count, string singular, string plural) => count is { } value
        ? $"{value:N0} {(value == 1 ? singular : plural)}"
        : "unknown";

    private static string FormatPercent(double? value) => value is { } percent
        ? percent.ToString("N0", CultureInfo.CurrentCulture) + "%"
        : "not recorded";

    private static string FormatMs(double? value) => value is null ? "unknown" : value.Value.ToString("N0") + " ms";

    private static string ShortHash(string? value) => value is null ? "unknown" : value[..Math.Min(8, value.Length)] + "…";

    // The tree, attempts and analyses are the reading's own objects; printing them twice only doubles the output.
    private static string TraceJson(WordTraceResponse response)
    {
        var json = JsonNode.Parse(ProjectionJson.Serialize(response))!.AsObject();
        if (response.Reading is not { } reading) return ProjectionJson.Serialize(response);
        if (ReferenceEquals(response.Root, reading.Root)) json.Remove("root");
        if (ReferenceEquals(response.Candidates, reading.Attempts)) json.Remove("candidates");
        if (ReferenceEquals(response.Analyses, reading.Analyses)) json.Remove("analyses");
        return json.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    // The reading the window shows, in the same order: the answer, why attempts stopped, the path, then every name.
    private static string RenderTrace(WordTraceResponse response)
    {
        var reading = response.Reading ?? TraceReadingBuilder.Build(response);
        var text = new StringBuilder();
        var analyses = reading.Analyses.Count;
        text.AppendLine(response.Parsed
            ? $"Trace of {response.Word}: parsed, {analyses:N0} {(analyses == 1 ? "analysis" : "analyses")}"
            : $"Trace of {response.Word}: no parse");
        text.AppendLine(response.Complete
            ? $"  Search: complete, {response.ParserSteps?.ToString("N0") ?? "unknown"} parser steps"
            : "  Search: stopped early. " + (response.StopReason ?? "The parser did not finish."));
        if (response.Provenance is { Warning.Length: > 0 } provenance)
            text.AppendLine("  Warning: " + provenance.Warning);
        foreach (var analysis in reading.Analyses)
        {
            var ways = analysis.FoundWays > 1 ? $" (found {analysis.FoundWays:N0} ways)" : "";
            text.AppendLine("  Analysis: " + string.Join(" + ", analysis.Morphs.Select(MorphText)) + ways);
        }
        foreach (var group in reading.StopGroups)
            text.AppendLine($"  Stopped {group.Count:N0} attempt(s): " + (group.Rule is null ? "" : group.Rule + ": ") +
                (group.Explanation ?? group.ReasonCode ?? "no reason recorded"));
        if (reading.RulesOnBestPath.Count > 0)
        {
            text.AppendLine(response.Parsed ? "  Rules on the parse:" : "  Rules on the closest attempt:");
            foreach (var rule in reading.RulesOnBestPath)
                text.AppendLine($"    {rule.Kind} {rule.Rule}, {rule.Outcome}: {rule.Explanation} " +
                    $"(steps {string.Join(", ", rule.StepIds)})");
        }
        if (reading.Refs.Count > 0)
        {
            text.AppendLine("  Names:");
            foreach (var reference in reading.Refs)
                text.AppendLine($"    {reference.Id}  {reference.Kind} {reference.Label} [{reference.IdentityQuality}]" +
                    (reference.FieldWorks is { } fieldWorks ? $"  opens in {fieldWorks.ToolName}: {fieldWorks.Link}" : ""));
        }
        return text.ToString();

        static string MorphText(TraceMorph morph) =>
            (morph.Form ?? morph.GuessedString ?? "?") + (morph.Gloss is { Length: > 0 } gloss ? $" ({gloss})" : "");
    }

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
