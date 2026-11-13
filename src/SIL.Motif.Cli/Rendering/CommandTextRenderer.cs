using System;
using System.Globalization;
using System.Text;
using System.Text.Json;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.HumanJudgments;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Phonology;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Review;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Assess;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Commands.Review;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Host.PanGloss;
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
        where T : class? =>
        outcome.Succeeded
            ? RenderSuccess(outcome.Value!, asJson && successAsJson)
            : RenderRefusal(outcome.Refusal!, asJson);

    private static CommandResult RenderSuccess<T>(T value, bool asJson) where T : class?
    {
        if (asJson)
            return new CommandResult(0, (value is WordTraceResponse trace ? TraceJson(trace) :
                value is TrialReviewResult review ? JsonSerializer.Serialize(review, ReviewJson.Options) :
                value is AssessCommandResponse assessment ? AssessSummary.Full(assessment).ToJsonString(new JsonSerializerOptions { WriteIndented = true }) :
                value is TrialRecord trial ? AssessSummary.Trial(trial).ToJsonString(new JsonSerializerOptions { WriteIndented = true }) :
                value is TimingResponse timing ? TimingPresentation.Timing(timing).ToJsonString(new JsonSerializerOptions { WriteIndented = true }) :
                value is OverviewResponse overview ? TimingPresentation.Overview(overview).ToJsonString(new JsonSerializerOptions { WriteIndented = true }) :
                value is ParsimonyReportResponse or ParsimonyReportSetResponse ? ParsimonyReportJson.Serialize(value) :
                ProjectionJson.Serialize(value)) +
                Environment.NewLine);

        if (value is null)
            return new CommandResult(0, "No Trial is recorded for this Proposal." + Environment.NewLine);

        var text = value switch
        {
            CorpusListProjection p => ProjectionText.Render(p),
            CorpusDetailProjection p => ProjectionText.Render(p),
            ProjectConfigurationProjection p => ProjectionText.Render(p),
            StorePruneResponse r => RenderStorePrune(r),
            DryRunProjection p => ProjectionText.Render(p),
            RetirementProposalReviewProjection p => ProjectionText.Render(p),
            RetirementReviewQueryResponse r => RenderRetirementReview(r),
            CorpusAddedResponse r => RenderCorpusAdded(r),
            CorpusDocumentAddedResponse r => RenderCorpusDocumentAdded(r),
            CorpusBundleAddedResponse r => RenderCorpusBundleAdded(r),
            PhonologyExamplesSavedResponse r => RenderPhonologyExamplesSaved(r),
            PhonologyExamplesListResponse r => RenderPhonologyExamplesList(r),
            PhonologyExamplesPageResponse r => RenderPhonologyExamplesPage(r),
            PhonologyResultsPageResponse r => RenderPhonologyResultsPage(r),
            PhonologyExampleResultResponse r => RenderPhonologyExampleResult(r),
            ReportKindListResponse r => RenderReportKindList(r),
            ReportResponse r => RenderReport(r),
            ParsimonyReportResponse r => RenderParsimonyReport(r),
            ParsimonyReportSetResponse r => RenderParsimonyReportSet(r),
            ProposalParsimonyStatus r => ParsimonyStatusText.Render(r),
            ParsimonyLatestReportResponse r => "Latest Parsimony Report " + r.ReportId + "\n  Baseline bundle: " + r.BundleId,
            ParsimonyExpectationProjection p => RenderParsimonyExpectations(p),
            FindComposerResponse r => RenderFindComposer(r),
            TrialRecord r => RenderTrial(r),
            TrialReviewResult r => TrialReviewTextRenderer.Render(r),
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
            ProjectInitializationResponse r => RenderProjectInitialization(r),
            ProjectTagResponse r => "Project tag: " + r.Tag + Environment.NewLine,
            ProjectCreatedResponse r => RenderProjectCreated(r),
            ProjectWordFormingResponse r => RenderProjectWordForming(r),
            NamedSelectionProjection p => RenderDefaultSelection(new DefaultSelectionResponse(p)),
            ProjectSetupResponse r => r.SetupSkipped
                ? "Setup skipped for this project." + Environment.NewLine
                : "Setup remains open." + Environment.NewLine,
            TextInventoryResponse r => RenderTextInventory(r),
            OverviewResponse r => RenderOverview(r),
            GrammarCheckResponse r => RenderGrammarCheck(r),
            WarningsResponse r => RenderWarnings(r),
            ProposalDifferenceResponse r => RenderProposalDifference(r),
            TimingResponse r => TimingTextRenderer.RenderTiming(r),
            ActivitySummary r => RenderActivity(r),
            ObjectUsesResponse r => RenderUses(r),
            InspectResponse r => RenderInspect(r),
            HandoffCommandResponse r => RenderHandoff(r),
            WordTraceResponse r => RenderTrace(r),
            WritingSystemsResponse r => RenderWritingSystems(r),
            GdrListResponse r => RenderGdrList(r),
            GdrShowResponse r => RenderGdrShow(r),
            SourceListResponse r => SourceTextRenderer.RenderList(r),
            SourceShowResponse r => SourceTextRenderer.RenderShow(r),
            SourceWhereResponse r => SourceTextRenderer.RenderWhere(r),
            CurrentGetResponse r => SourceTextRenderer.RenderCurrent(r),
            SourceLocalResponse r => SourceTextRenderer.RenderLocal(r),
            WordContextResponse r => RenderWordContext(r),
            ProposalStandings r => RenderProposalStandings(r),
            ProposalTextChangedResponse r => ProposalCommandRenderer.RenderTextChanged(r),
            ProposalCommentChangedResponse r => ProposalCommandRenderer.RenderCommentChanged(r),
            ProposalPictureChangedResponse r => ProposalCommandRenderer.RenderPictureChanged(r),
            ProposalPicturesResponse r => ProposalCommandRenderer.RenderPictures(r),
            RequestListResponse r => RequestRenderer.RenderList(r),
            RequestCreatedResponse r => $"Request {r.Request.RequestId} is open: {r.Request.TargetLabel}.{Environment.NewLine}",
            RequestWithdrawnResponse r => $"Request {r.RequestId} withdrawn.{Environment.NewLine}",
            _ => throw new NotSupportedException($"No text rendering registered for '{typeof(T)}'."),
        };
        return new CommandResult(0, text);
    }

    private static string RenderTrial(TrialRecord trial)
    {
        var text = new StringBuilder();
        text.AppendLine($"Trial {trial.JobId}: {trial.Status} ({trial.Freshness})");
        AppendDroppedGrammar(text, trial);
        if (trial.Work is not { } work)
        {
            text.AppendLine("Parser work is unavailable.");
            AppendWordResults(text, trial);
            return text.ToString();
        }
        if (work.PercentChange is { } change)
            text.AppendLine((change == 0 ? "These words took the same parser work"
                : $"These words took {Math.Abs(change):0.#}% {(change > 0 ? "more" : "less")} work to parse") + ".");
        else text.AppendLine("Parser work cannot be compared for every Trial word.");
        if (work.WorkIncreaseWarning)
            text.AppendLine("Parser work increased by more than 20% over the Baseline.");
        if (work.IncreasedWords.Count > 0) text.AppendLine("Words with more than 5% extra work:");
        foreach (var word in work.IncreasedWords)
            text.AppendLine($"  {word.Word}: {word.PercentMore:0.#}% more work");
        text.AppendLine($"Stopped: {work.WorkLimitStops} at a parser limit; {work.TimeSafetyNetStops} at the time safety net.");
        if (work.WorkLimitWords.Count > 0) text.AppendLine("  Parser limit: " + string.Join(", ", work.WorkLimitWords));
        if (work.TimeSafetyNetWords.Count > 0) text.AppendLine("  Time safety net: " + string.Join(", ", work.TimeSafetyNetWords));
        AppendWordResults(text, trial);
        return text.ToString();
    }

    private static void AppendWordResults(StringBuilder text, TrialRecord trial)
    {
        if (trial.WordResults.Count == 0) return;
        text.AppendLine($"Words ({trial.WordResults.Count:N0}):");
        foreach (var word in trial.WordResults)
        {
            text.AppendLine($"  {word.Word}: {word.Outcome}" + (word.IsIncomplete ? $" ({word.CompletionStatus})" : string.Empty));
            if (word.Comparison is { } comparison) text.AppendLine("    " + comparison.Headline);
            foreach (var reading in word.Readings ?? [])
                text.AppendLine("    " + string.Join(" + ", reading.Morphs.Select(morph =>
                    morph.Gloss.Length == 0 ? morph.Form : $"{morph.Form} ({morph.Gloss})")));
        }
    }

    // Only a finished Trial has a dropped-grammar check to report; an active one has none yet.
    private static void AppendDroppedGrammar(StringBuilder text, TrialRecord trial)
    {
        if (trial.Status != JobStatus.Completed) return;
        var dropped = trial.DroppedGrammarOrUnchecked;
        if (!dropped.Checked)
        {
            text.AppendLine("  Dropped grammar was not checked, so this Trial is not clean: " + dropped.NotCheckedReason +
                " Try the Proposal again.");
            return;
        }
        text.AppendLine(dropped.IsClean
            ? "  Clean: the parser dropped no grammar that the Proposal adds or edits."
            : "  NOT clean: PanGloss dropped grammar that the Proposal adds or edits, with only a warning.");
        foreach (var finding in dropped.Introduced)
        {
            var named = finding.Objects.Count == 0
                ? "an object PanGloss did not identify"
                : string.Join(", ", finding.Objects.Select(item =>
                    $"{item.Kind ?? "object"} {item.Name} ({item.MotifId})"));
            text.AppendLine($"    {finding.Code} on {named}: {finding.Message}");
        }
        if (dropped.OmittedFindings > 0) text.AppendLine($"    and {dropped.OmittedFindings} more.");
        if (dropped.PreExisting > 0)
            text.AppendLine($"  {dropped.PreExisting} dropped finding(s) were already in the Baseline's grammar.");
    }

    private static string RenderActivity(ActivitySummary summary)
    {
        var text = new StringBuilder();
        text.AppendLine($"Calls: {summary.Total}, errors: {summary.Errors}");
        text.AppendLine($"Followed next: {summary.FollowedNext}, ignored next: {summary.IgnoredNext}");
        text.AppendLine("Calls by tool:");
        foreach (var (tool, count) in summary.ByTool) text.AppendLine($"  {tool}: {count}");
        text.AppendLine("Refusals by code:");
        foreach (var (code, count) in summary.RefusalsByCode) text.AppendLine($"  {code}: {count}");
        if (summary.RefusalsByCode.Count == 0) text.AppendLine("  none");
        return text.ToString();
    }

    private static string RenderProposalStandings(ProposalStandings standings)
    {
        if (standings.Proposals.Count == 0)
            return (standings.SetAsideCount == 0
                ? "No Proposals yet." + Environment.NewLine
                : "No open Proposals. " + ClosedProposalHint(standings.SetAsideCount)) +
                UnreadableProposalsText(standings);

        var text = new StringBuilder("Proposals" + Environment.NewLine);
        foreach (var group in standings.Proposals.GroupBy(card => card.Group).OrderBy(group => group.Key))
        {
            text.AppendLine();
            text.AppendLine(ProposalGroupText(group.Key) + " (" + group.Count().ToString(CultureInfo.CurrentCulture) + ")");
            foreach (var card in group)
            {
                text.AppendLine("  " + card.Label + " — " + card.Status + (card.IsDraft ? " (Draft)" : string.Empty));
                text.AppendLine("    " + card.ProposalId);
                text.AppendLine("    Trial: " + TrialStandingText(card.Standing));
                if (card.ChangeKinds.Count > 0)
                    text.AppendLine("    Changes: " + string.Join(", ", card.ChangeKinds.Select(kind => kind.Text)));
            }
        }

        var closedShown = standings.Proposals.Count(card => card.Group == ProposalGroup.SetAside);
        if (closedShown < standings.SetAsideCount)
        {
            text.AppendLine();
            text.AppendLine(ClosedProposalHint(standings.SetAsideCount - closedShown));
        }
        return text.Append(UnreadableProposalsText(standings)).ToString();
    }

    private static string UnreadableProposalsText(ProposalStandings standings)
    {
        if (standings.Unreadable.Count == 0) return string.Empty;
        var text = new StringBuilder(Environment.NewLine);
        text.AppendLine(standings.Unreadable.Count == 1
            ? "1 Proposal could not be read, so it is not listed. Nothing was changed or deleted."
            : $"{standings.Unreadable.Count.ToString(CultureInfo.CurrentCulture)} Proposals could not be read, " +
                "so they are not listed. Nothing was changed or deleted.");
        foreach (var item in standings.Unreadable)
            text.AppendLine($"  {UnreadableProposalName(item)}: {item.Reason}");
        return text.ToString();
    }

    private static string UnreadableProposalName(UnreadableProposal item)
    {
        var notes = new List<string>();
        if (item.Label != item.ProposalId) notes.Add(item.Label);
        if (item.DraftName is not null) notes.Add("draft " + item.DraftName);
        return notes.Count == 0 ? item.ProposalId : $"{item.ProposalId} ({string.Join(", ", notes)})";
    }

    private static string ProposalGroupText(ProposalGroup group) => group switch
    {
        ProposalGroup.WaitingForYou => "Waiting for you",
        ProposalGroup.NeedsAnotherTry => "Needs another try",
        ProposalGroup.NotTried => "Not tried yet",
        ProposalGroup.NotAppliedYet => "Your changes not applied yet",
        ProposalGroup.SetAside => "Closed",
        _ => throw new ArgumentOutOfRangeException(nameof(group), group, null),
    };

    private static string TrialStandingText(TrialStanding standing) => standing.Kind switch
    {
        TrialStandingKind.NotTried => "Not tried yet",
        TrialStandingKind.Trying => "In progress",
        TrialStandingKind.Failed => "Did not finish",
        TrialStandingKind.Current => "Current",
        TrialStandingKind.NeedsAnotherTry when standing.Reason == TrialStaleReason.BaselineMoved =>
            "Needs another try because the project changed",
        TrialStandingKind.NeedsAnotherTry when standing.Reason == TrialStaleReason.DroppedGrammarNotChecked =>
            "Needs another try because the check for dropped grammar did not finish",
        TrialStandingKind.NeedsAnotherTry => "Needs another try because the Proposal changed",
        _ => throw new ArgumentOutOfRangeException(nameof(standing), standing.Kind, null),
    };

    private static string ClosedProposalHint(int count) =>
        count.ToString(CultureInfo.CurrentCulture) + " closed Proposal" + (count == 1 ? " is" : "s are") +
        " hidden. Use --include-set-aside to show them.";

    // Corpus's own refusals never carried an "error: " prefix before they were typed; preserved verbatim.
    private static CommandResult RenderRefusal(Refusal refusal, bool asJson)
    {
        if (asJson)
        {
            var envelope = new FailureEnvelope(
                refusal.Reason, refusal.Message, refusal.Facts.Count > 0 ? refusal.Facts : null, refusal.Code,
                refusal.ParserDiagnostic);
            return new CommandResult(
                FailureEnvelope.ExitCodeFor(refusal.Reason),
                ProjectionJson.Serialize(envelope) + Environment.NewLine, refusal.Reason);
        }

        var prefix = refusal.Code.StartsWith("corpus.", StringComparison.Ordinal) ? "" : "error: ";
        return new CommandResult(
            FailureEnvelope.ExitCodeFor(refusal.Reason), prefix + refusal.Message + Environment.NewLine,
            refusal.Reason);
    }

    private static string RenderStorePrune(StorePruneResponse r)
    {
        var text = string.Format(CultureInfo.CurrentCulture, CliStrings.StorePruneFormat,
            r.AssessmentsRemoved, r.BytesBefore, r.BytesAfter, r.InvocationDirectoriesRemoved,
            r.UnownedDirectoriesKept, r.UnownedBytesKept, r.InvocationParentPath);
        if (r.ByteBudget > 0)
            text += string.Format(CultureInfo.CurrentCulture, CliStrings.StorePruneBudgetFormat, r.ByteBudget,
                r.BytesInUse, r.OverBudget ? CliStrings.StorePruneOverBudget : "");
        text += Reasons(CliStrings.StorePruneKeptAssessments, r.AssessmentsKept) +
            Reasons(CliStrings.StorePruneKeptRuns, r.RunDirectoriesKept);
        if (r.UserDataKept is not null)
            text += string.Format(CultureInfo.CurrentCulture, CliStrings.StorePruneUserDataFormat, r.SourcesRemoved,
                r.BaselinesRemoved, r.UserDataBytesRemoved, r.UserDataBytesKept) +
                Reasons(CliStrings.StorePruneKeptUserData, r.UserDataKept);
        return text + (r.UnownedSizeComplete ? "" : CliStrings.StorePruneSizeIncomplete) +
            (r.CheckpointComplete ? "" : CliStrings.StorePruneIncomplete) + Environment.NewLine;

        static string Reasons(string format, IReadOnlyDictionary<string, int>? counts) =>
            counts is null || counts.Count == 0 ? "" : string.Format(CultureInfo.CurrentCulture, format,
                string.Join(", ", counts.Select(pair => $"{pair.Key}={pair.Value}")));
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

    private static string RenderProjectInitialization(ProjectInitializationResponse response)
    {
        var text = response.Status == "already-initialized"
            ? "This project is already prepared for Motif Proposals."
            : "Project prepared for Motif Proposals.";
        if (response.RecoveryCopyPath is not null)
            text += " Keep the recovery copy at '" + response.RecoveryCopyPath + "'.";
        return text + Environment.NewLine;
    }

    private static string RenderPhonologyExamplesSaved(PhonologyExamplesSavedResponse response)
    {
        var set = response.ExampleSet;
        return $"Saved phonology Example Set '{set.Name}' revision {set.Revision.RevisionId} " +
            $"with {set.Revision.ExampleCount} examples." + Environment.NewLine +
            response.EntryVerification switch
            {
                "verified" => "FieldWorks entries: found in the current Baseline." + Environment.NewLine,
                "not-verified-no-baseline" => "FieldWorks entries: not checked, because no Baseline is captured." + Environment.NewLine,
                "not-verified-baseline-file-missing" => "FieldWorks entries: not checked, because the Baseline file is missing or unreadable." + Environment.NewLine,
                _ => "",
            };
    }

    private static string RenderPhonologyExamplesList(PhonologyExamplesListResponse response)
    {
        if (response.Items.Count == 0) return "No phonology Example Sets are saved." + Environment.NewLine;
        var builder = new StringBuilder();
        foreach (var set in response.Items)
            builder.AppendLine($"{set.Name}  {set.Revision.RevisionId}  {set.Revision.ExampleCount} examples");
        AppendContinuation(builder, response.Continuation);
        return builder.ToString();
    }

    private static string RenderPhonologyExamplesPage(PhonologyExamplesPageResponse response)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"{response.ExampleSet.Name}  {response.ExampleSet.Revision.RevisionId}");
        foreach (var example in response.Items)
        {
            builder.AppendLine($"{example.Id}: {example.Underlying} → {string.Join(" | ", example.Expected)}");
            builder.AppendLine("  Context: " + (example.Context switch
            {
                PhonologyGenerationContext.HcXmlEntry hc => $"HC XML entry {hc.EntryId}",
                PhonologyGenerationContext.FwdataEntry fw => $"FieldWorks entry {fw.EntryGuid}; MSA {fw.MsaGuid} ({fw.MsaClass})",
                _ => "unknown",
            }));
            if (example.Citation is { } citation)
                builder.AppendLine("  Citation: " + citation.Source +
                    (citation.Locator is null ? "" : ", " + citation.Locator));
        }
        AppendContinuation(builder, response.Continuation);
        return builder.ToString();
    }

    private static string RenderPhonologyResultsPage(PhonologyResultsPageResponse response)
    {
        var builder = new StringBuilder();
        AppendPhonologyCheckHeader(builder, response.Assessment);
        foreach (var item in response.Items)
        {
            builder.AppendLine(item.Evidence switch
            {
                PhonologyRowSummary.Produced row =>
                    $"{item.Example.Id}: {item.Example.Underlying} → {string.Join(" | ", item.Example.Expected)}  " +
                    $"{row.Disposition.ToString().ToLowerInvariant()} ({row.Completion.ToString().ToLowerInvariant()})" +
                    (row.Observed is null ? "" : "  observed " + row.Observed) +
                    $"  rules applied {row.AppliedRuleCount}" +
                    (row.AppliedRulesOmitted > 0 ? $" ({row.AppliedRulesOmitted} not listed here)" : ""),
                PhonologyRowSummary.Unavailable row =>
                    $"{item.Example.Id}: unavailable ({row.Reason.ToString().ToLowerInvariant()}): {row.Detail}",
                _ => $"{item.Example.Id}: unknown",
            });
        }
        AppendContinuation(builder, response.Continuation);
        return builder.ToString();
    }

    private static string RenderPhonologyExampleResult(PhonologyExampleResultResponse response)
    {
        var builder = new StringBuilder();
        AppendPhonologyCheckHeader(builder, response.Assessment);
        var example = response.Result.Example;
        builder.AppendLine($"{example.Id}: {example.Underlying} → {string.Join(" | ", example.Expected)}");
        switch (response.Result.Evidence)
        {
            case PhonologyRowEvidence.Produced row:
                builder.AppendLine($"  {row.Disposition.ToString().ToLowerInvariant()} " +
                    $"({row.Completion.ToString().ToLowerInvariant()}); observed " + (row.Observed ?? "nothing"));
                builder.AppendLine("  Ordered rules: " + string.Join(", ", row.OrderedRuleIds));
                builder.AppendLine("  Visited rules: " + string.Join(", ", row.VisitedRuleIds));
                builder.AppendLine("  Applied rules: " + string.Join(", ", row.AppliedRuleIds));
                break;
            case PhonologyRowEvidence.Unavailable row:
                builder.AppendLine($"  unavailable ({row.Reason.ToString().ToLowerInvariant()}): {row.Detail}");
                break;
        }
        return builder.ToString();
    }

    private static void AppendPhonologyCheckHeader(StringBuilder builder, PhonologyAssessmentHeader header)
    {
        builder.AppendLine($"Phonology check {header.AssessmentId} of '{header.ExampleSet.Name}' " +
            $"revision {header.ExampleSet.Revision.RevisionId}. Advisory evidence: it does not change Readiness or Apply.");
        builder.AppendLine(header.Invocation switch
        {
            PhonologyInvocationBinding.Produced p =>
                $"  {p.AgreeCount} agree, {p.DisagreeCount} disagree, {p.UnavailableCount} unavailable.",
            PhonologyInvocationBinding.Unavailable u =>
                $"  Not measured ({u.Reason.ToString().ToLowerInvariant()}): {u.Detail}",
            _ => "  Unknown invocation.",
        });
    }

    private static void AppendContinuation<T>(StringBuilder builder, T? continuation) where T : class
    {
        if (continuation is null) return;
        builder.Append("Next page cursor: ");
        builder.AppendLine(ProjectionJson.Serialize(continuation));
    }

    private static string RenderProjectWordForming(ProjectWordFormingResponse r)
    {
        var sb = new StringBuilder();
        sb.AppendLine(r.Added.Count > 0
            ? $"Declared word-forming in '{r.Vernacular}': {string.Join(" ", r.Added)}"
            : $"Nothing to add: those characters are already word-forming in '{r.Vernacular}'.");
        if (r.WordFormingCharacters.Count > 0)
            sb.AppendLine($"  Word-forming beyond letters: {string.Join(" ", r.WordFormingCharacters)}");
        sb.AppendLine("A word the parser reads also needs each of these as a phoneme grapheme (compose-author-phoneme).");
        return sb.ToString();
    }

    private static string RenderProjectCreated(ProjectCreatedResponse r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Created project '{r.ProjectName}' at '{r.FwDataPath}'.");
        sb.AppendLine($"  Vernacular: {r.Vernacular} ({r.VernacularName})");
        if (r.WordFormingCharacters.Count > 0)
            sb.AppendLine($"  Word-forming beyond letters: {string.Join(" ", r.WordFormingCharacters)}");
        sb.AppendLine($"  Analysis:   {r.Analysis}");
        sb.AppendLine();
        sb.AppendLine("Next: motif project initialize --project \"" + r.FwDataPath + "\" --confirm \"" +
            "Initialize this project to work with Motif Proposals?\"");
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
        if (response.GrammarExportFormat is { } format)
            text.AppendLine($"  Grammar format: {format.Name} v{format.Version}");
        text.AppendLine(response.Text);
        return text.ToString();
    }

    private static string RenderParsimonyReport(ParsimonyReportResponse response)
    {
        var text = new StringBuilder();
        text.AppendLine("Parsimony Report " + response.ReportId);
        text.AppendLine("  Baseline bundle: " + response.Inputs.BundleId);
        text.AppendLine("  Assessments: " + (response.AssessmentIds.Count == 0
            ? "(none)" : string.Join(", ", response.AssessmentIds)));
        text.Append(response.Text);
        return text.ToString();
    }

    private static string RenderParsimonyReportSet(ParsimonyReportSetResponse response)
    {
        var text = new StringBuilder();
        text.AppendLine($"Parsimony job {response.JobId}: {response.Reports.Count} Reports on bundle {response.BundleId}");
        foreach (var report in response.Reports)
        {
            text.AppendLine();
            text.AppendLine(RenderParsimonyReport(report).TrimEnd());
        }
        if (response.Note is { } note)
        {
            text.AppendLine();
            text.AppendLine(note);
        }
        return text.ToString();
    }

    private static string RenderParsimonyExpectations(ParsimonyExpectationProjection projection)
    {
        var text = new StringBuilder();
        text.AppendLine($"Parsimony expectations {projection.Contract}");
        text.AppendLine($"Project: {projection.ProjectId}");
        text.AppendLine($"Digest:  {projection.Digest}");
        text.AppendLine($"Reviewed negatives: {projection.ReviewedNegatives.Count}");
        foreach (var item in projection.ReviewedNegatives)
        {
            var target = item.Target is SurfaceNegativeTarget ? "surface" : "reading";
            text.AppendLine($"  {item.Status}  {target}  {item.WritingSystem}:{item.Form}  case={item.CaseId}");
            text.AppendLine($"    context: {item.Context}");
            if (item.Target is ReadingNegativeTarget reading)
                foreach (var morph in reading.Morphs)
                    text.AppendLine($"    morph: form={morph.Identity.Form ?? "(unavailable)"} " +
                        $"msa={morph.Identity.Msa ?? "(unavailable)"} inflType={morph.Identity.InflType ?? "(none)"}");
            if (item.Issue is not null) text.AppendLine($"    issue: {item.Issue}");
        }
        RenderNative("Approved readings", projection.ApprovedReadings);
        RenderNative("Disapproved readings", projection.DisapprovedReadings);
        text.AppendLine($"Issues: {projection.Issues.Count}");
        foreach (var issue in projection.Issues)
            text.AppendLine($"  {issue.SourceKind} {issue.SourceId} [{issue.Code}]: {issue.Detail}");
        return text.ToString();

        void RenderNative(string title, IReadOnlyList<NativeReadingExpectation> readings)
        {
            text.AppendLine($"{title}: {readings.Count}");
            foreach (var reading in readings)
            {
                text.AppendLine($"  {reading.WritingSystem}:{reading.Form}  morphs={reading.Morphs.Count} " +
                    $"wordforms={reading.SourceWordformIds.Count} analyses={reading.SourceAnalysisIds.Count}");
                foreach (var morph in reading.Morphs)
                    text.AppendLine($"    morph: form={morph.Form ?? "(unavailable)"} " +
                        $"msa={morph.Msa ?? "(unavailable)"} inflType={morph.InflType ?? "(none)"}");
                if (reading.SourceWordformIds.Count > 0)
                    text.AppendLine($"    wordform ids: {string.Join(", ", reading.SourceWordformIds)}");
                if (reading.SourceAnalysisIds.Count > 0)
                    text.AppendLine($"    analysis ids: {string.Join(", ", reading.SourceAnalysisIds)}");
                if (reading.UnavailableReason is not null)
                    text.AppendLine($"    unavailable: {reading.UnavailableReason}");
            }
        }
    }

    private static string RenderRetirementReview(RetirementReviewQueryResponse response)
    {
        if (!response.Applicable)
            return "This Draft does not contain a rule-based allomorph retirement.\n";

        var text = new StringBuilder();
        text.AppendLine("Sound-rule replacement review");
        if (response.DryRun is { } dryRun)
            text.Append(ProjectionText.Render(dryRun));
        foreach (var item in response.Unavailable)
            text.AppendLine(item);
        if (response.Review is { } review)
            text.Append(ProjectionText.Render(review));
        return text.ToString();
    }

    private static string RenderFindComposer(FindComposerResponse found)
    {
        if (found.NoMatch)
        {
            var lines = new StringBuilder();
            lines.AppendLine(found.Missing);
            if (found.NearestSubjects is { Count: > 0 } subjects)
                lines.Append("Nearest Subjects: ").AppendLine(string.Join(", ", subjects.Select(s => s.Id + " (" + s.Title + ")")));
            if (found.NearestComposers is { Count: > 0 } composers)
                lines.Append("Weakest overlap: ").AppendLine(string.Join(", ", composers.Select(c => c.Verb + " " + c.Score)));
            lines.Append(found.Next);
            return lines.ToString();
        }
        if (found.Results.Count == 0)
            return "No composer fits that goal. Try other words, or list a Subject with --subject <id>.";
        var text = new StringBuilder();
        foreach (var match in found.Results)
        {
            text.Append(match.Verb).Append(" - ").AppendLine(match.Title);
            text.Append("  ").AppendLine(match.Description);
            text.Append("  Subjects: ").AppendLine(string.Join(", ", match.Subjects));
            if (match.Redirect is not null)
                text.Append("  Wrong choice? ").AppendLine(match.Redirect);
            if (match.Guides is { Count: > 0 })
                text.Append("  Guides: ").AppendLine(string.Join(", ", match.Guides.Select(guide => "encoding/" + guide)));
            text.AppendLine(match.IntentSchema is null
                ? "  Intent: described on the Help page (motif help " + match.Verb + ")."
                : "  Intent schema: use --json.");
            if (match.Example is not null)
                text.AppendLine("  Example:").AppendLine(string.Join(Environment.NewLine,
                    match.Example.Split('\n').Select(line => "    " + line)));
            if (match.SubjectPage is { } page)
                text.Append("  ").Append(page.Title).Append(": ").AppendLine(page.Excerpt ?? "No page.");
            text.AppendLine();
        }
        return text.ToString().TrimEnd();
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
        text.AppendLine("  Status:  " + (response.Status is JobStatus.StoppedByBudget
            ? JobStatusJson.ToWire(JobStatus.StoppedByBudget) : response.Status?.ToString()));
        text.AppendLine("  Attempt: " + response.Attempt);
        text.AppendLine("  Updated: " + response.UpdatedUtc);
        foreach (var warning in response.Warnings ?? [])
            text.AppendLine("  Warning: " + warning.Message);
        if (response.QueueOrder is { } queueOrder)
            text.AppendLine("  Queue order: " + queueOrder.ToString("R"));
        if (response.FailureCode is { } failureCode)
            text.AppendLine("  Failure: " + failureCode + (response.FailureDetail is { } detail ? " - " + detail : ""));
        if (response.StageProgress is { } stage)
            text.AppendLine($"  Stage:   {stage.Stage} ({stage.Step} of {stage.Steps})" +
                (stage.Detail is null ? string.Empty : " " + stage.Detail));
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

    /// <summary>The counts-and-one-page rendering <c>assess --summary</c> prints, as JSON or text.</summary>
    public static CommandResult RenderAssessSummary(System.Text.Json.Nodes.JsonObject page, bool asJson)
    {
        if (asJson) return new CommandResult(0, page.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) +
            Environment.NewLine);
        var text = new StringBuilder();
        text.AppendLine(CountsLine(page["counts"]!.Deserialize<AssessmentCounts>(
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!));
        text.AppendLine("Assessment: " + page["assessment"] + "  (" + page["showing"] + ")");
        foreach (var word in page["words"]!.AsArray())
            text.AppendLine($"  {word!["word"]}: {word["outcome"]}" +
                (word["reading"] is { } reading ? " (" + reading + ")" : string.Empty) +
                (word["stoppedBy"] is { } stoppedBy ? ", stopped by " + stoppedBy : string.Empty));
        text.AppendLine("Parser work comparison unavailable: no paired Assessment was selected.");
        if (page["nextOffset"] is { } next)
            text.AppendLine($"More: motif assess <project> --summary --offset {next}");
        return new CommandResult(0, text.ToString());
    }

    private static string CountsLine(AssessmentCounts counts) =>
        $"  Counts: {counts.Words} word(s); {counts.Analysed} analysed " +
        $"({counts.Accepted} accepted, {counts.GuessOnly} guess-only); {counts.NoAnalysis} no-analysis; " +
        $"{counts.Capped} stopped at a parser limit; {counts.TimedOut} stopped at the time safety net; {counts.Skipped} skipped";

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
        text.AppendLine(CountsLine(response.Counts));
        text.AppendLine();
        text.AppendLine(response.SummaryMarkdown);
        foreach (var word in response.Words)
        {
            if (word.Comparison is not { } comparison) continue;
            text.AppendLine($"  {word.Word}: {comparison.Headline}");
            if (comparison.Detail.Length > 0) text.AppendLine("    " + comparison.Detail);
        }
        return text.ToString();
    }

    // Human-mode dispatch always asks for text output, so response.Text is always populated here.
    private static string RenderStats(StatsCommandResponse response) => response.Text ?? string.Empty;

    private static string RenderDefaultSelection(DefaultSelectionResponse response) =>
        RenderDefaultOnly(response) + RenderParsimonySelection(response.ParsimonySelection);

    private static string RenderParsimonySelection(NamedSelectionProjection? selection) => selection is null
        ? "No Parsimony Selection is saved." + Environment.NewLine
        : $"Parsimony Selection: {selection.Name}{Environment.NewLine}" +
            $"  Texts:           {selection.TextIds.Count:N0}{Environment.NewLine}" +
            $"  Updated:         {selection.UpdatedUtc}{Environment.NewLine}";

    private static string RenderDefaultOnly(DefaultSelectionResponse response)
    {
        if (response.Selection is null)
            return "No default Selection is saved." + Environment.NewLine +
                (response.SetupSkipped ? "Setup skipped for this project." + Environment.NewLine : string.Empty);
        var selection = response.Selection;
        var timeLimit = selection.Limits.TimeMode switch
        {
            SelectionTimeLimitMode.Explicit => $"{selection.Limits.ExplicitPerWordLimitMs!.Value:N0} ms on this computer",
            SelectionTimeLimitMode.Estimated when !selection.Limits.PerWordParserWorkLimit.IsUnbounded => "estimated",
            _ => "none",
        };
        return $"Default Selection: {selection.Name}{Environment.NewLine}" +
            $"  Texts:           {selection.TextIds.Count:N0}{Environment.NewLine}" +
            $"  Added words:     {selection.AddedWords.Count:N0}{Environment.NewLine}" +
            (selection.WordList is { } list
                ? $"  Word list:       {list.Source} {list.Sha256}, {list.Counts.Count:N0} words with a count, " +
                  $"{selection.AddedWords.Count - list.Counts.Count:N0} with none known{Environment.NewLine}"
                : string.Empty) +
            $"  Work limit:      {(selection.Limits.PerWordParserWorkLimit.Work is { } work ? work.ToString("N0", CultureInfo.CurrentCulture) + " parser work" : "none")}{Environment.NewLine}" +
            $"  Time safety net: {timeLimit}{Environment.NewLine}" +
            $"  Updated:         {selection.UpdatedUtc}{Environment.NewLine}";
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
        OverviewMeasuresTextRenderer.AppendLeftOut(text, response.TextCoverage);
        OverviewMeasuresTextRenderer.AppendUnparsed(text, response.LookFirst.UnparsedWords);
        OverviewMeasuresTextRenderer.AppendUntaggedStems(text, response.LookFirst.StemsWithoutInflectionClass);
        OverviewMeasuresTextRenderer.AppendNotMeasured(text, response.TextCoverage);
        OverviewMeasuresTextRenderer.AppendBands(text, response.FrequencyBands);
        OverviewMeasuresTextRenderer.AppendAmbiguity(text, response.Ambiguity);
        text.AppendLine($"Accuracy   {response.Accuracy.ApprovedWordsKept:N0}/{response.Accuracy.ApprovedWordCount:N0} approved kept  " +
            $"{response.Accuracy.Violations:N0} violations  {response.Accuracy.UnknownWords:N0} unknown");
        text.AppendLine($"           rejected analyses rebuilt {response.Accuracy.RejectedAnalysesRebuilt:N0}  " +
            $"rejected words matched {response.Accuracy.RejectedWordsInMatchCell:N0}/{response.Accuracy.RejectedWordCount:N0}  " +
            $"candidates confirmed {response.Accuracy.CandidatesConfirmed:N0}/{response.Accuracy.CandidateWordCount:N0}");
        TimingTextRenderer.AppendOverviewSpeed(text, response.Timing);
        if (response.Warnings is not null)
            text.AppendLine($"Warnings   {response.Warnings.Count?.ToString("N0") ?? "unknown"} findings " +
                $"({CountLabel(response.Warnings.ErrorCount, "error", "errors")}, " +
                $"{CountLabel(response.Warnings.WarningCount, "warning", "warnings")}, " +
                $"{response.Warnings.InformationCount?.ToString("N0") ?? "unknown"} information)");
        if (response.Warnings?.YourWords is { } touched)
        {
            text.AppendLine($"           {TouchedLine(touched)}");
            if (CandidateLine(touched.ByMembershipOnly, touched.BySpellingOnly) is { } candidates)
                text.AppendLine($"           Not counted: {candidates}");
        }
        return text.ToString();
    }

    private static string RenderWritingSystems(WritingSystemsResponse response)
    {
        if (!response.HasBaseline) return "No Baseline yet. Capture a Baseline to read writing systems." + Environment.NewLine;
        var text = new StringBuilder();
        foreach (var ws in response.WritingSystems)
        {
            text.AppendLine($"{ws.Kind} {ws.Position + 1}: {ws.Name} ({ws.Id}, {ws.Abbreviation}){(ws.IsDefault ? " [default]" : "")}");
            text.AppendLine($"  {ws.FontFamily}; {(ws.RightToLeft ? "right to left" : "left to right")}; features: {ws.FontFeatures}");
            foreach (var size in ws.StyleSizes) text.AppendLine($"  {size.Key}: {size.Value.ToString(CultureInfo.InvariantCulture)} pt");
        }
        text.AppendLine("Change fonts and direction in FieldWorks: Format > Set up Vernacular/Analysis Writing Systems; sizes: Format > Styles.");
        return text.ToString();
    }

    private static string RenderGdrList(GdrListResponse response)
    {
        if (!response.HasBaseline) return "No Baseline yet. Capture a Baseline to read Grammar Design Records." + Environment.NewLine;
        if (response.Records.Count == 0) return "This project has no Grammar Design Records." + Environment.NewLine;
        var text = new StringBuilder();
        foreach (var record in response.Records) text.AppendLine($"{record.Abbreviation}  {record.Title}");
        return text.ToString();
    }

    private static string RenderGdrShow(GdrShowResponse response)
    {
        var text = new StringBuilder();
        text.AppendLine($"{response.Record.Abbreviation}  {response.Record.Title}");
        if (response.Record.Description.Length > 0) text.AppendLine(response.Record.Description);
        foreach (var paragraph in response.Discussion)
        {
            text.AppendLine();
            text.AppendLine(string.Concat(paragraph.Runs.Select(run => run.Text)));
        }
        var links = response.Discussion.SelectMany(paragraph => paragraph.Runs).Where(run => run.Target is not null).ToArray();
        if (links.Length > 0) text.AppendLine();
        foreach (var run in links)
        {
            var target = run.Target!;
            text.AppendLine(target.State switch
            {
                GdrLinkState.Resolved => $"Link '{run.Text}' -> {target.Kind}: {target.Name}" + (target.Description.Length > 0 ? $" ({target.Description})" : ""),
                GdrLinkState.External => $"Link '{run.Text}' -> {target.Name}",
                _ => $"Link '{run.Text}' is broken: its target is not in this project.",
            });
        }
        return text.ToString();
    }

    private static string RenderWordContext(WordContextResponse response)
    {
        var text = new StringBuilder();
        text.AppendLine($"Word: {response.Word}");
        if (!response.HasBaseline)
        {
            text.AppendLine("No Baseline has been captured; word membership and stored analyses are unknown.");
            text.AppendLine("Run: motif baseline capture --project <fwdata>");
            return text.ToString();
        }
        text.AppendLine($"Baseline: {response.Baseline?.BundleDigest ?? "identity not recorded"}");
        text.AppendLine($"Captured: {response.Baseline?.CapturedUtc ?? "not recorded"}");
        text.AppendLine($"FieldWorks source save: {response.SourceLastWriteUtc?.ToString("O") ?? "not recorded"}");
        if (response.IsStale) text.AppendLine("FieldWorks saved since; these analyses remain the captured Baseline's.");
        text.AppendLine(response.IsInFieldWorks switch
        {
            true => "Present in the captured FieldWorks Baseline",
            false => "Absent from the captured FieldWorks Baseline",
            _ => "Word membership not recorded",
        });
        foreach (var analysis in response.Analyses)
        {
            var opinion = analysis.StoredAnalysisOpinion switch
            {
                "approved" => "Approved",
                "disapproved" => "Disapproved",
                "candidate" => "Unknown",
                _ => "Opinion not recorded",
            };
            text.AppendLine($"  {opinion}: {analysis.StoredAnalysisId ?? "identity not recorded"} " +
                string.Join(" + ", analysis.Morphs.Select(morph => $"{morph.Form} {morph.Gloss}")));
        }
        if (response.Analyses.Count == 0) text.AppendLine("No stored analyses in the captured FieldWorks Baseline");
        if (response.WordAnalysesLink is { } link) text.AppendLine($"Word Analyses: {link}");
        return text.ToString();
    }

    private static string RenderProposalDifference(ProposalDifferenceResponse response)
    {
        var summary = response.Summary;
        var text = new StringBuilder();
        // Words are listed only while their detail is kept and readable, so the counts say so only then.
        var listing = response.WordDetailKept && !response.WordDetailUnreadable;
        text.AppendLine($"Difference {summary.DifferenceId} for Proposal {summary.ProposalId}");
        text.AppendLine($"  Text: {summary.TextTitle} ({summary.TextId})");
        text.AppendLine(summary.Reference == ProposalDifferenceReferences.SourceValidated
            ? "  Source-validated decisions (staged by a named non-human agent from cited sources, not a person's): " +
              $"{CountLabel(summary.WordsWithApprovedDecisions, "word has", "words have")} an approved analysis"
            : "  Human decisions: as recorded in FieldWorks for this Text at the current Baseline; " +
              $"{CountLabel(summary.WordsWithApprovedDecisions, "word has", "words have")} an approved analysis");
        text.AppendLine($"  {CountLabel(summary.Words, "word", "words")}, {CountLabel(summary.Occurrences, "occurrence", "occurrences")} " +
            $"compared; {CountLabel(summary.UnboundOccurrences, "occurrence", "occurrences")} could not be matched to one measured word");
        if (listing && summary.ListedWordCount < summary.Words)
            text.AppendLine($"  {summary.ListedWordCount:N0} of {summary.Words:N0} words are listed and " +
                $"{summary.Words - summary.ListedWordCount:N0} {(summary.Words - summary.ListedWordCount == 1 ? "is" : "are")} counted but not listed: only words that changed or fall " +
                $"in a category are listed, each cell, category and limitation keeping its best {ProposalDifferenceListing.MostWordsPerGroup:N0}, " +
                $"and at most {ProposalDifferenceListing.MostPlacesPerWord:N0} places for each word");
        text.AppendLine($"  Could be bad: {summary.CouldBeBadWords}; improved: {summary.ImprovedWords} (advisory only)");
        foreach (var bucket in summary.Buckets)
            text.AppendLine($"  {BucketCode(bucket.Bucket)}: {CountLabel(bucket.Words, "word", "words")}, " +
                $"{CountLabel(bucket.Occurrences, "occurrence", "occurrences")}{NotListed(listing, bucket.Words, bucket.WordsListed)}");
        foreach (var item in response.UnreadableEvidence ?? [])
            text.AppendLine($"  Not counted: staged decisions of Applied Proposal {item.ProposalId}" +
                (item.Label is { Length: > 0 } ? $" '{item.Label}'" : "") + $" could not be read: {item.Reason}");
        text.AppendLine("  Before -> after:");
        foreach (var cell in summary.Matrix)
            text.AppendLine($"    {cell.BeforeState} -> {cell.AfterState}: {CountLabel(cell.Words, "word", "words")}" +
                NotListed(listing, cell.Words, cell.WordsListed));
        foreach (var warning in summary.Warnings) text.AppendLine($"  Warning: {warning}");
        if (summary.MeasuredWordsOutsideText > 0)
            text.AppendLine($"  {CountLabel(summary.MeasuredWordsOutsideText, "measured word is", "measured words are")} " +
                "not in this Text");
        foreach (var limit in summary.Limitations)
            text.AppendLine($"  Limit {System.Text.Json.JsonSerializer.Serialize(limit.Limitation).Trim('"')}: " +
                $"{CountLabel(limit.Words, "word", "words")}, {CountLabel(limit.Occurrences, "occurrence", "occurrences")}" +
                NotListed(listing, limit.Words, limit.WordsListed));
        if (response.Saved is { Count: > 0 } saved)
        {
            text.AppendLine($"  Saved Differences of this Proposal: {saved.Count:N0}" +
                (response.SavedTotal > saved.Count ? $" of {response.SavedTotal:N0}" : ""));
            foreach (var other in saved)
                text.AppendLine($"    {(other.DifferenceId == summary.DifferenceId ? "*" : "-")} {other.TextTitle} ({other.TextId}), " +
                    $"{(other.Reference == ProposalDifferenceReferences.SourceValidated ? "source-validated" : "human")} decisions, " +
                    $"made {other.CreatedUtc}, {other.DifferenceId}{(other.WordDetailKept ? "" : ", summary only")}");
        }
        if (response.SavedUnreadable > 0)
            text.AppendLine($"  {CountLabel(response.SavedUnreadable, "saved Difference", "saved Differences")} of this " +
                "Proposal could not be read and " + (response.SavedUnreadable == 1 ? "is" : "are") + " not listed");
        foreach (var damaged in response.SavedDamaged ?? [])
            text.AppendLine($"    ! Text {damaged.TextId}, " +
                $"{(damaged.Reference == ProposalDifferenceReferences.SourceValidated ? "source-validated" : "human")} decisions, " +
                $"made {damaged.CreatedUtc}, {damaged.DifferenceId}: unreadable; produce it again to replace it");
        if (response.WordDetailUnreadable)
        {
            text.AppendLine("  The word detail of this Difference cannot be read, so only this summary is shown");
            text.AppendLine("  Make the Difference again to replace it: motif difference produce");
            return text.ToString();
        }
        if (!response.WordDetailKept)
        {
            text.AppendLine("  Word detail is no longer kept: the Proposal was decided or deferred, only this summary remains");
            text.AppendLine("  A deferred Proposal can make the Difference again to see its words: motif difference produce");
            return text.ToString();
        }
        text.AppendLine($"  Showing {response.Words.Count} of {response.MatchingWords} matching words");
        foreach (var word in response.Words)
            text.AppendLine($"    {word.Word} x{word.Occurrences}: {word.BeforeState} -> {word.AfterState}" +
                (word.Buckets.Count == 0 ? "" : " [" + string.Join(", ", word.Buckets.Select(BucketCode)) + "]"));
        foreach (var word in response.Words.Where(word => word.Evidence is not null))
            foreach (var item in word.Evidence!)
                text.AppendLine($"    {word.Word}: source-validated {item.Tier} for {item.Level} {item.Subject}" +
                    (item.Agent is null ? "" : $" ({item.Agent} {item.Opinion}, not a person)"));
        if (response.NextOffset is { } next) text.AppendLine($"  More: --offset {next}");
        if (response.Occurrences is { } places)
        {
            text.AppendLine(places.PlacesListed < places.Total
                ? $"  Places of {places.Word}: showing {places.Places.Count} of {places.PlacesListed:N0} listed; " +
                  $"{places.Total:N0} in the Text, so {CountedPlaces(places.Total - places.PlacesListed)} counted but not listed " +
                  $"(at most {ProposalDifferenceListing.MostPlacesPerWord:N0} places are listed for each word; the rest are in the Text)"
                : $"  Places of {places.Word}: showing {places.Places.Count} of {places.Total:N0}");
            foreach (var place in places.Places)
                text.AppendLine($"    line {place.LineNumber}, word {place.OccurrenceIndex + 1} (segment {place.SegmentId})");
            if (places.NextOffset is { } nextPlace) text.AppendLine($"  More places: --occurrence-offset {nextPlace}");
        }
        return text.ToString();
    }

    // Says how many of a count's words the Difference lists, so a count is never read as an empty list.
    private static string NotListed(bool listing, int words, int listed) => !listing || listed >= words ? ""
        : $" ({listed:N0} listed, {words - listed:N0} counted but not listed)";

    private static string BucketCode(ProposalDifferenceBucket bucket) =>
        SIL.Motif.Worker.Store.ProposalDifferenceRepository.BucketName(bucket);

    private static string RenderWarnings(WarningsResponse response)
    {
        if (!response.HasBaseline)
            return "No Baseline has been captured. Run: motif baseline capture --project <fwdata>" + Environment.NewLine;
        if (!response.HasCheck)
            return "Grammar not checked yet for this Baseline. Run: motif grammar check --project <fwdata>" +
                Environment.NewLine;
        var text = new StringBuilder();
        text.AppendLine($"Grammar findings: {response.TotalCount:N0} " +
            $"({CountLabel(response.ErrorCount, "error", "errors")}, {CountLabel(response.WarningCount, "warning", "warnings")}, " +
            $"{response.InformationCount:N0} information)");
        text.AppendLine(CheckedInventory(response.CheckedLexemeCount, response.CheckedRuleCount));
        if (response.ObjectTimingDetail is { IsComplete: false, Description: { } detail }) text.AppendLine(detail);
        foreach (var advisory in response.Advisories)
            text.AppendLine($"Advisory: {advisory.Message}");
        if (response.YourWords is { } touched)
        {
            text.AppendLine(TouchedLine(touched));
            if (CandidateLine(touched.ByMembershipOnly, touched.BySpellingOnly) is { } candidates)
                text.AppendLine($"Not counted: {candidates}");
        }
        foreach (var kind in response.ByKind)
        {
            text.AppendLine($"  {kind.Code}: {kind.Count:N0} {kind.Level.ToWireValue()}" +
                (kind.YourWords is not { } words ? string.Empty : kind.WordAttributionComplete == true
                    ? $", {words:N0} of your words" : words == 0 ? ", no known matches; incomplete"
                    : $", at least {words:N0} of your words; incomplete"));
            if (CandidateLine(kind.ByMembershipOnly ?? 0, kind.BySpellingOnly ?? 0) is { } candidates)
                text.AppendLine($"    Not counted: {candidates}");
        }
        foreach (var finding in response.Findings)
        {
            text.AppendLine($"  {finding.Text}");
            if (finding.Explanation is { Length: > 0 } explanation) text.AppendLine($"    {explanation}");
            if (finding.Guidance is { Length: > 0 } guidance) text.AppendLine($"    {guidance}");
            foreach (var place in finding.FieldWorksPlaces)
                text.AppendLine($"    In FieldWorks: {place.Describe(FieldWorksLinks.ToolName)}");
            foreach (var subject in finding.Subject)
            {
                var state = subject.Status switch
                {
                    GrammarSubjectStatus.UnresolvedReference => "unresolved reference",
                    GrammarSubjectStatus.ProjectSettings => "project setting",
                    _ => "object",
                };
                text.AppendLine($"    {subject.Text} ({state})" + (subject.Field is { } field ? $" > {field}" : ""));
                if (subject.LinkStatus == FieldWorksLinkStatus.Unavailable)
                    text.AppendLine($"      FieldWorks link unavailable: {subject.LinkReason switch
                    {
                        FieldWorksLinkReason.UnresolvedReference => "the referenced item is absent or unusable as the expected class",
                        FieldWorksLinkReason.ProjectSettings => "a project setting has no individual FieldWorks object",
                        FieldWorksLinkReason.MissingProject => "no FieldWorks project name supplied",
                        FieldWorksLinkReason.GuidNotRecorded => "source item has no FieldWorks GUID",
                        FieldWorksLinkReason.InvalidGuid => "source item has an invalid FieldWorks GUID",
                        FieldWorksLinkReason.UnsupportedKind => "source item has no verified FieldWorks tool",
                        _ => "reason not recorded",
                    }}");
            }
            if (finding.HelpUrl is { } helpUrl) text.AppendLine($"    PanGloss reference: {helpUrl}");
            text.AppendLine($"    {YourWordsLine(finding)}");
            var limits = finding.AttributionLimits.Where(reason => reason != finding.AttributionReason).ToArray();
            if (limits.Length > 0)
                text.AppendLine($"    Attribution limits: {string.Join("; ", limits.Select(AttributionLimitText))}");
            if (finding.YourWords is { } yours)
            {
                var members = yours.Match == WarningWordsMatch.Membership ? yours.Words : yours.MembershipCandidates;
                var spelled = yours.Match == WarningWordsMatch.Spelling ? yours.Words : yours.SpellingCandidates;
                if (members.Count > 0)
                    text.AppendLine($"    Membership candidates; not confirmed uses of the named object: {WordList(members)}");
                if (spelled.Count > 0)
                    text.AppendLine($"    Spelling candidates; not confirmed uses of the phoneme: {WordList(spelled)}");
            }
        }
        return text.ToString();
    }

    private static string TouchedLine(WarningWordsTouched touched) => touched.IsComplete
        ? $"{touched.Words:N0} of your words use something a finding names ({touched.NoParse:N0} don't parse)"
        : (touched.Words == 0 ? "No known word matches" :
            $"At least {touched.Words:N0} of your words use something a finding names ({touched.NoParse:N0} don't parse)") +
          (touched.AttributionLimits.Count > 0 ? "; some named connections could not be followed"
              : "; word evidence is unavailable for some findings");

    private static string? CandidateLine(int members, int spelled)
    {
        var parts = new List<string>();
        if (members > 0) parts.Add($"{members:N0} membership candidates");
        if (spelled > 0) parts.Add($"{spelled:N0} spelling candidates; not confirmed uses of the phoneme");
        return parts.Count == 0 ? null : string.Join("; ", parts);
    }

    private static string YourWordsLine(GrammarWarning finding) => finding.AttributionState switch
    {
        WarningAttributionState.ExactUses => "Your words: " + WordList(finding.YourWords!.Words),
        WarningAttributionState.MembershipCandidates => "Your words: membership candidates only",
        WarningAttributionState.SpellingCandidates => "Your words: spelling candidates only",
        WarningAttributionState.NoneInSelection => "Your words: none in the Selection",
        WarningAttributionState.ProjectWide => "Your words: project-wide; no word attribution",
        WarningAttributionState.MissingObject => "Your words: missing object; " + (finding.AttributionReason switch
        {
            WarningAttributionReason.UnresolvedReference => "PanGloss recorded an unresolved reference",
            WarningAttributionReason.WrongClass => "its GUID belongs to a different FieldWorks class",
            _ => "its GUID is absent from the checked Baseline",
        }),
        WarningAttributionState.UnresolvedIdentity => "Your words: unresolved identity; " + (finding.AttributionReason switch
        {
            WarningAttributionReason.NamedWithoutProjectGuid => "the named subject has no project GUID",
            WarningAttributionReason.UnresolvedEnvironmentNotation => "environment notation does not identify a natural-class object",
            WarningAttributionReason.UnsupportedKind => "the named class has no supported route to words",
            _ => "PanGloss names no subject",
        }),
        _ => "Your words: evidence unavailable; no usable stored Parse all words",
    };

    private static string AttributionLimitText(WarningAttributionReason reason) => reason switch
    {
        WarningAttributionReason.UnresolvedEnvironmentNotation => "environment notation does not identify a natural-class object",
        WarningAttributionReason.UnsupportedKind => "an owner's class has no supported route to words",
        WarningAttributionReason.NoWordAttribution => "an owner has no word attribution",
        WarningAttributionReason.UnresolvedReference => "PanGloss recorded an unresolved reference",
        WarningAttributionReason.StaleGuid => "a subject's GUID is absent from the checked Baseline",
        WarningAttributionReason.WrongClass => "a subject's GUID belongs to a different FieldWorks class",
        WarningAttributionReason.NamedWithoutProjectGuid => "a named subject has no project GUID",
        _ => "PanGloss names no subject",
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
            return "No Baseline has been captured. Run: motif baseline capture --project <fwdata>" + Environment.NewLine;

        var errorCount = response.Findings.Count(finding => finding.Severity == GrammarDiagnosticLevel.Error);
        var warningCount = response.Findings.Count(finding => finding.Severity == GrammarDiagnosticLevel.Warning);
        var informationCount = response.Findings.Count(finding => finding.Severity == GrammarDiagnosticLevel.Information);
        var text = new StringBuilder();
        text.AppendLine($"Grammar findings: {response.Findings.Count:N0} " +
            $"({CountLabel(errorCount, "error", "errors")}, {CountLabel(warningCount, "warning", "warnings")}, " +
            $"{informationCount:N0} information)");
        text.AppendLine(CheckedInventory(response.CheckedLexemeCount, response.CheckedRuleCount));
        if (response.ObjectTimingDetail is { IsComplete: false, Description: { } detail }) text.AppendLine(detail);
        foreach (var advisory in response.Advisories)
            text.AppendLine($"Advisory: {advisory.Message}");
        foreach (var summary in response.Summary)
            text.AppendLine($"  {summary.Code}: {summary.Count:N0} {summary.Level.ToWireValue()}");
        foreach (var finding in response.Findings)
            text.AppendLine($"  {finding.Text}");
        return text.ToString();
    }

    // The words that use the object, the words it ran in, then what the asked-about words share.
    private static string RenderUses(ObjectUsesResponse response)
    {
        var text = new StringBuilder();
        var name = response.Ref?.Label ?? response.Ref?.TimingKey ?? response.Ref?.GrammaticalInfoId ?? response.Ref?.AllomorphId;
        text.AppendLine(name is null ? $"Uses ({response.AssessmentId})" : $"Uses of {name} ({response.AssessmentId})");
        if (response.IsStale)
            text.AppendLine("  Warning: FieldWorks has changed since the current Baseline.");
        if (response.ObjectTimingDetail is { IsComplete: false, Description: { } detail }) text.AppendLine("  " + detail);
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

    private static string RenderInspect(InspectResponse response)
    {
        var text = new StringBuilder();
        var subject = response.Subject;
        var name = subject.Label ?? subject.TimingKey?.Key ?? subject.AllomorphId ?? subject.GrammaticalInfoId ??
            subject.ObjectId ?? subject.WarningCode;
        var kind = subject.Kind.ToString().ToLowerInvariant();
        text.AppendLine($"Inspect {kind} {name}" + response.Resolution switch
        {
            InspectorResolution.Resolved => "",
            InspectorResolution.NotInBaseline => ": not in the Baseline",
            InspectorResolution.Contradictory => ": the names given belong to different FieldWorks objects",
            InspectorResolution.NotAuthored => ": no FieldWorks identity",
            InspectorResolution.NoBaseline => ": no Baseline yet",
            _ => "",
        });
        if (response.IsStale)
            text.AppendLine("  Warning: FieldWorks has changed since the current Baseline.");
        if (Section(text, "In FieldWorks", response.Facts) is { } facts) AppendFacts(text, facts);
        if (Section(text, "Your words that use it", response.Uses) is { } uses) AppendUseWords(text, "Your words that use it", uses);
        if (Section(text, "Words it ran in", response.RanIn) is { } ranIn) AppendUseWords(text, "Words it ran in", ranIn);
        if (Section(text, "Warnings that name it", response.Warnings) is { } warnings)
        {
            text.AppendLine($"  Warnings that name it: {warnings.Count:N0}");
            foreach (var finding in warnings)
                text.AppendLine($"    {finding.Group ?? finding.CodeLabel}: {(finding.Description.Length > 0 ? finding.Description : finding.Text)}");
        }
        return text.ToString();
    }

    // A section not requested prints nothing; one that could not be read says so, with its reason.
    private static T? Section<T>(StringBuilder text, string heading, InspectorSection<T> section) where T : class
    {
        if (section.Status == InspectorSectionStatus.Available) return section.Value;
        if (section.Status != InspectorSectionStatus.NotRequested)
            text.AppendLine($"  {heading}: {(section.Status == InspectorSectionStatus.Absent ? "absent" : "not read for this kind")} ({section.Reason})");
        return null;
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
            var timing = word.Calls is null && word.ElapsedNs is null && word.ParserWork is null ? "" :
                $", {CountLabel(word.Calls, "call", "calls")}, " +
                (word.ParserWork is { } work ? $"{work:N0} parser work, " : "parser work not recorded, ") +
                (word.ElapsedNs is { } ns ? "time on this computer: " + (ns / 1_000_000d).ToString("N3", CultureInfo.CurrentCulture) + " ms" : "time on this computer not recorded");
            text.AppendLine($"    {word.Row.Word}: {word.Row.Meaning}{timing}");
            if (word.Row.MeaningDetail.Length > 0) text.AppendLine("      " + word.Row.MeaningDetail);
        }
        if (words.NotCountingDisapproved > 0)
            text.AppendLine($"    Not counting {words.NotCountingDisapproved:N0} disapproved");
    }

    private static string CountedPlaces(int count) => count == 1 ? "1 is" : $"{count:N0} are";

    private static string CountLabel(int? count, string singular, string plural) => count is { } value
        ? $"{value:N0} {(value == 1 ? singular : plural)}"
        : "unknown";

    private static string CheckedInventory(int? lexemeCount, int? ruleCount) =>
        $"Checked {CountLabel(lexemeCount, "lexeme", "lexemes")} and {CountLabel(ruleCount, "rule", "rules")}";

    private static string FormatPercent(double? value) => value is { } percent
        ? percent.ToString("N0", CultureInfo.CurrentCulture) + "%"
        : "not recorded";

    private static string ShortHash(string? value) => value is null ? "unknown" : value[..Math.Min(8, value.Length)] + "…";

    private static string TraceJson(WordTraceResponse response) => ProjectionJson.Serialize(StandaloneTraceProjection.From(response));

    // The reading the window shows, in the same order: the answer, why attempts stopped, the path, then every name.
    private static string RenderTrace(WordTraceResponse response)
    {
        var reading = response.Reading;
        var labels = new TraceDisplayLabels(reading.Refs);
        var text = new StringBuilder();
        var analyses = reading.Analyses.Count;
        text.AppendLine(response.Parsed
            ? $"Trace of {response.Word}: parsed, {analyses:N0} {(analyses == 1 ? "analysis" : "analyses")}"
            : $"Trace of {response.Word}: no parse");
        text.AppendLine(response.Complete
            ? "  Search: complete"
            : "  Search: stopped early. " + (response.StopReason ?? "The parser did not finish."));
        if (response.LowercaseFailure is { } lowercaseFailure)
            text.AppendLine("  " + lowercaseFailure);
        if (response.Lowercase is { } lowercase)
            text.AppendLine($"  Lowercase form {lowercase.Word}: " + (lowercase.Parsed
                ? $"parsed, {lowercase.Reading.Analyses.Count:N0} {(lowercase.Reading.Analyses.Count == 1 ? "analysis" : "analyses")}"
                : "no parse"));
        if (response.Provenance is { Warning.Length: > 0 } provenance)
            text.AppendLine("  Warning: " + provenance.Warning);
        if (response.HostCapture?.Baseline is { } baseline)
            text.AppendLine($"  Baseline: {baseline.Token.BundleDigest}, captured {baseline.Token.CapturedUtc}, " +
                $"source saved {baseline.SourceLastWriteUtc:O}.");
        else text.AppendLine("  Baseline source not recorded");
        if (response.GrammarSourceAvailability == TraceEvidenceAvailability.NotRecorded)
            text.AppendLine("  Grammar source not recorded");
        if (!response.Parsed)
        {
            foreach (var reason in reading.NoParseReasons)
                text.AppendLine("  No parse reason: " + reason);
            if (reading.NoParseReasons.Count == 0 && reading.StopGroups.Count == 0)
                text.AppendLine("  No reason for the lack of a parse was recorded.");
        }
        foreach (var analysis in reading.Analyses)
        {
            text.AppendLine("  Analysis: " + string.Join(" + ", analysis.Morphs.Select(MorphText)));
        }
        if (reading.Attempts.Count == 0) text.AppendLine("  No terminal attempt recorded; tree progress retained");
        foreach (var group in reading.StopGroups)
            text.AppendLine($"  Stopped {group.Count:N0} attempt(s): " +
                (labels.Resolve(group.RuleRefId, group.Rule) is { } name ? name + ": " : "") +
                (group.Explanation ?? (group.ReasonCode is { } code
                    ? $"Recorded reason code {code}: {TraceFailureSentences.Explain(code)} Owner explanation not recorded."
                    : "Reason not recorded. Owner explanation not recorded.")));
        if (reading.RulesOnBestPath.Count > 0)
        {
            text.AppendLine(response.Parsed ? "  Rules on the parse:" : "  Rules on the closest attempt:");
            foreach (var rule in reading.RulesOnBestPath)
                text.AppendLine($"    {rule.Kind} {labels.Resolve(rule.RefId, rule.Rule)}, {rule.Outcome}: {rule.Explanation} " +
                    $"(steps {string.Join(", ", rule.StepIds)})");
        }
        foreach (var step in Walk(reading.Root))
        {
            foreach (var detail in TraceEvidenceDisplay.Details(step).Where(detail =>
                !detail.StartsWith("Recorded template slot:", StringComparison.Ordinal) &&
                !detail.StartsWith("Phonological non-unapplication:", StringComparison.Ordinal) &&
                !detail.StartsWith("Typed rejection operands", StringComparison.Ordinal)))
                text.AppendLine($"  Recorded event {step.EventEvidence?.ProducerStepId ?? step.StepId}: {detail}");
            if (step.FailureEvidence?.Payload is { ValueKind: JsonValueKind.Object } payload)
                foreach (var name in new[] { "requestedShape", "builtShape" })
                    if (payload.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
                        text.AppendLine($"  Recorded {name}: {value.GetString()}");
        }
        if (reading.Refs.Count > 0)
        {
            text.AppendLine("  Names:");
            foreach (var reference in reading.Refs)
                text.AppendLine($"    {reference.Kind} {labels.Resolve(reference.Id, reference.Label)} [{reference.IdentityQuality}]" +
                    (reference.FieldWorks is { } fieldWorks ? $"  opens in {fieldWorks.ToolName}: {fieldWorks.Link}" : ""));
        }
        return text.ToString();

        static IEnumerable<TraceStep> Walk(TraceStep step) =>
            new[] { step }.Concat(step.Children.SelectMany(Walk));

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
