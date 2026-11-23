using System;
using System.Globalization;
using System.Text;
using System.Text.Json;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.HandAnalyses;
using SIL.Motif.Contract.HumanJudgments;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Phonology;
using SIL.Motif.Contract.Query;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Review;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Assess;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Commands.Review;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Corpus;
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
    private const int TextListLimit = 10;

    /// <param name="successAsJson">
    /// Whether a successful result renders as JSON. A job just enqueued never grew a JSON success shape
    /// and must keep printing its bare id even when the caller passed <c>--json</c>; a refusal always
    /// follows <paramref name="asJson"/> regardless.
    /// </param>
    public static CommandResult Render<T>(CommandOutcome<T> outcome, bool asJson, bool successAsJson = true,
        bool includeEffects = false)
        where T : class? =>
        outcome.Succeeded
            ? RenderSuccess(outcome.Value!, asJson && successAsJson, includeEffects)
            : RenderRefusal(outcome.Refusal!, asJson);

    private static CommandResult RenderSuccess<T>(T value, bool asJson, bool includeEffects) where T : class?
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
            WorkerStopResponse r => RenderWorkerStop(r),
            StoreRelocateResponse r => string.Format(CultureInfo.CurrentCulture,
                r.Changed ? CliStrings.StoreRelocateFormat : CliStrings.StoreRelocateUnchangedFormat,
                r.OldProjectPath, r.NewProjectPath, r.IdentityEvidence) +
                (r.OldProjectStillExists ? CliStrings.StoreRelocateOldStillExists : "") + Environment.NewLine,
            DryRunProjection p => ProjectionText.Render(p, includeEffects),
            RetirementProposalReviewProjection p => ProjectionText.Render(p),
            RetirementReviewQueryResponse r => RenderRetirementReview(r),
            CorpusAddedResponse r => RenderCorpusAdded(r),
            CorpusDocumentAddedResponse r => RenderCorpusDocumentAdded(r),
            CorpusBundleAddedResponse r => RenderCorpusBundleAdded(r),
            CorpusViewerDocument r => RenderCorpusViewerDocument(r),
            CorpusRangeResponse r => RenderCorpusRange(r),
            RebuildCorpusIndexResponse r => $"Indexed {r.CorpusCount:N0} Corpora.{Environment.NewLine}",
            ArtifactInventoryResponse r => RenderArtifactInventory(r),
            PhonologyExamplesSavedResponse r => RenderPhonologyExamplesSaved(r),
            PhonologyExamplesListResponse r => RenderPhonologyExamplesList(r),
            PhonologyExamplesPageResponse r => RenderPhonologyExamplesPage(r),
            PhonologyResultsPageResponse r => RenderPhonologyResultsPage(r),
            PhonologyExampleResultResponse r => RenderPhonologyExampleResult(r),
            ReportKindListResponse r => RenderReportKindList(r),
            ReportResponse r => RenderReport(r),
            ParsimonyReportResponse r => RenderParsimonyReport(r),
            ParsimonyReportSetResponse r => RenderParsimonyReportSet(r),
            ParsimonyCandidateEvidenceResponse r => RenderParsimonyCandidateEvidence(r),
            ParsimonyMeasureCatalogResponse r => RenderParsimonyMeasureCatalog(r),
            ParsimonyNamedViewResponse r => RenderParsimonyNamedView(r),
            NotebookRecordTypesResponse r => RenderNotebookRecordTypes(r),
            RuleAttemptSummary r => TrialReviewTextRenderer.RenderRuleAttempts(r),
            ProposalParsimonyStatus r => ParsimonyStatusText.Render(r),
            ParsimonyLatestReportResponse r => "Latest Parsimony Report " + r.ReportId + "\n  Baseline bundle: " + r.BundleId,
            ParsimonyExpectationProjection p => RenderParsimonyExpectations(p),
            FindComposerResponse r => RenderFindComposer(r),
            GroupResponse r => RenderGroup(r),
            ProposalComparison r => RenderProposalComparison(r),
            TrialExportReceipt r => RenderTrialExport(r),
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
            ProgressResponse r => RenderProgress(r),
            HandAnalysisCheckResponse r => "Hand analysis checked; no records changed.\n" +
                string.Join("\n", r.Pieces.Select(piece => $"  {piece.PieceKey}: type {piece.EffectiveMorphTypeId}; allomorph {piece.AllomorphId}")) +
                "\n" + string.Join("\n", r.Diagnostics.Select(diagnostic => $"  {diagnostic.Pointer}: {diagnostic.Message}")) + "\n",
            SourceProgressResponse r => RenderSourceProgress(r),
            OverviewResponse r => RenderOverview(r),
            GrammarCheckResponse r => RenderGrammarCheck(r),
            WarningsResponse r => RenderWarnings(r),
            ProposalDifferenceResponse r => RenderProposalDifference(r),
            TimingResponse r => TimingTextRenderer.RenderTiming(r),
            SIL.Motif.Contract.Query.Corpus.PairsQueryResponse r => PairsText.Render(r),
            ActivitySummary r => RenderActivity(r),
            ObjectUsesResponse r => RenderUses(r),
            InspectResponse r => RenderInspect(r),
            HandoffCommandResponse r => RenderHandoff(r),
            WordTraceResponse r => RenderTrace(r),
            RejectionTraceResponse r => RenderRejectionTrace(r),
            WhyNotResponse r => WhyNotRenderer.Render(r),
            FormsQueryResponse r => RenderFormsQuery(r),
            GrammarReadResponse r => GrammarReadTextRenderer.Render(r),
            ProjectStoreResetResponse r => r.Deleted
                ? $"Deleted the Motif store at {r.StorePath}.{Environment.NewLine}"
                : $"No Motif store was deleted at {r.StorePath}.{Environment.NewLine}",
            ProjectStoreUpgradeResponse r => RenderProjectStoreUpgrade(r),
            ProposalDeletedResponse r => RenderProposalDeleted(r),
            ProposalReviewResponse r => RenderProposalReview(r),
            ReviewMarkResponse r => $"Proposal {r.ProposalId}: entry {r.EntryKey} is " +
                (r.LookedAt ? "marked looked at." : "no longer marked looked at.") + Environment.NewLine,
            WordReadStateResponse r => RenderWordReadState(r),
            WritingSystemsResponse r => RenderWritingSystems(r),
            RecordImportResponse r => RenderRecordImport(r),
            GdrListResponse r => RenderGdrList(r),
            GdrShowResponse r => RenderGdrShow(r),
            GdrCheckResponse r => RenderGdrCheck(r),
            GdrEditResponse r => RenderGdrEdit(r),
            SourcePassageResponse r => SourceTextRenderer.RenderPassage(r),
            SourceListResponse r => SourceTextRenderer.RenderList(r),
            SourceShowResponse r => SourceTextRenderer.RenderShow(r),
            SourceWhereResponse r => SourceTextRenderer.RenderWhere(r),
            CurrentGetResponse r => SourceTextRenderer.RenderCurrent(r),
            SourceLocalResponse r => SourceTextRenderer.RenderLocal(r),
            SourceStageResponse r => SourceTextRenderer.RenderStaged(r),
            SourceApprovalStageResponse r => SourceTextRenderer.RenderApprovalStaged(r),
            DecisionEvidenceExportResponse r => RenderDecisionEvidenceExportPage(r),
            WordContextResponse r => RenderWordContext(r),
            LexicalEntryResponse r => RenderLexicalEntries(r),
            ProposalStandings r => RenderProposalStandings(r),
            ProposalStatusChangedResponse r => ProposalCommandRenderer.RenderStatusChanged(r),
            ProposalTextChangedResponse r => ProposalCommandRenderer.RenderTextChanged(r),
            ProposalCommentChangedResponse r => ProposalCommandRenderer.RenderCommentChanged(r),
            ProposalPictureChangedResponse r => ProposalCommandRenderer.RenderPictureChanged(r),
            ProposalPicturesResponse r => ProposalCommandRenderer.RenderPictures(r),
            RequestListResponse r => RequestRenderer.RenderList(r),
            HypothesisResponse r => HypothesisRenderer.RenderChanged(r),
            HypothesisNeighboursResponse r => HypothesisRenderer.RenderNeighbours(r),
            HypothesisListResponse r => HypothesisRenderer.RenderList(r),
            HypothesisReportResponse r => r.Text,
            CoverageResponse r => CoverageRenderer.Render(r),
            DisposeFormsResponse r => DispositionRenderer.RenderDisposed(r),
            DisposeFromFileResponse r => DispositionRenderer.RenderFromFile(r),
            UndisposeFormsResponse r => DispositionRenderer.RenderUndisposed(r),
            ShowDispositionResponse r => DispositionRenderer.RenderShown(r),
            ListDispositionsResponse r => DispositionRenderer.RenderListed(r),
            RequestCreatedResponse r => $"Request {r.Request.RequestId} is open: {r.Request.TargetLabel}.{Environment.NewLine}",
            RequestWithdrawnResponse r => $"Request {r.RequestId} withdrawn.{Environment.NewLine}",
            _ => throw new NotSupportedException($"No text rendering registered for '{typeof(T)}'."),
        };
        return new CommandResult(0, text);
    }

    private static string RenderArtifactInventory(ArtifactInventoryResponse response)
    {
        var text = new StringBuilder();
        text.AppendLine($"Artifact inventory at {response.RootPath}: {response.Artifacts.Count:N0} artifact(s) in {response.Stores.Count:N0} store(s).");
        text.AppendLine($"Inventory: {(response.InventoryComplete ? "complete" : "incomplete")}; " +
            $"reference domain: {(response.ReferenceDomainComplete ? "complete" : "incomplete")}; " +
            $"declared complete: {response.ReferenceDomainDeclaredComplete}.");
        if (response.LogicalBytes is { } bytes) text.AppendLine($"Logical size: {bytes:N0} bytes.");
        foreach (var store in response.Stores.Take(TextListLimit))
            text.AppendLine($"  Store {store.DatabasePath}: {store.Status} ({store.ReasonCode}); " +
                $"schema {store.SchemaVersion?.ToString(CultureInfo.CurrentCulture) ?? "unknown"}; " +
                $"snapshot {store.SnapshotSha256 ?? "unavailable"}.");
        AppendTextListOverflow(text, response.Stores.Count, TextListLimit);
        foreach (var artifact in response.Artifacts.Take(TextListLimit))
        {
            text.AppendLine($"  {artifact.Family}: {artifact.Path}; logical " +
                $"{artifact.LogicalBytes?.ToString("N0", CultureInfo.CurrentCulture) ?? "unknown"} bytes, allocated " +
                $"{artifact.AllocatedBytes?.ToString("N0", CultureInfo.CurrentCulture) ?? "unknown"} bytes; " +
                $"{artifact.Disposition} ({artifact.ReasonCode}); references " +
                $"{(artifact.ReferenceDomainComplete ? "complete" : "incomplete")}.");
            RenderLimitedValues(text, "    owners", artifact.OwnerInvocationIds, id => id);
            RenderLimitedValues(text, "    retained by", artifact.RetainingRecordIds, id => id);
        }
        AppendTextListOverflow(text, response.Artifacts.Count, TextListLimit);
        foreach (var diagnostic in response.Diagnostics.Take(TextListLimit))
            text.AppendLine($"  {diagnostic.Code}" + (diagnostic.Path is null ? "." : $": {diagnostic.Path}"));
        AppendTextListOverflow(text, response.Diagnostics.Count, TextListLimit);
        return text.ToString();
    }

    private static string RenderCorpusViewerDocument(CorpusViewerDocument response)
    {
        var text = new StringBuilder();
        text.AppendLine($"Document {response.DocumentId} in Corpus {response.CorpusId}: " +
            $"{response.Text.Length:N0} characters, {response.Boundaries.Count:N0} word boundaries; sha256 {response.Sha256}.");
        text.AppendLine(response.BoundaryDescription);
        text.AppendLine("Boundaries:");
        foreach (var boundary in response.Boundaries.Take(TextListLimit)) RenderCorpusSpan(text, "  Boundary", boundary);
        AppendTextListOverflow(text, response.Boundaries.Count, TextListLimit);
        text.AppendLine("Wordforms:");
        foreach (var wordform in response.Wordforms.Take(TextListLimit))
        {
            text.AppendLine($"  {wordform.Wordform}: {wordform.Frequency:N0}");
            foreach (var occurrence in wordform.Occurrences.Take(TextListLimit))
                RenderCorpusSpan(text, "    Occurrence", occurrence);
            AppendTextListOverflow(text, wordform.Occurrences.Count, TextListLimit);
        }
        AppendTextListOverflow(text, response.Wordforms.Count, TextListLimit);
        text.AppendLine(response.Text);
        return text.ToString();
    }

    private static void RenderCorpusSpan(StringBuilder text, string label, CorpusWordSpan span) =>
        text.AppendLine($"{label}: {span.Surface}; UTF-16 [{span.Utf16Start:N0}, {span.Utf16Start + span.Utf16Length:N0}); " +
            $"UTF-8 bytes [{span.Utf8Start:N0}, {span.Utf8Start + span.Utf8Length:N0}).");

    private static string RenderCorpusRange(CorpusRangeResponse response)
    {
        var text = new StringBuilder();
        text.AppendLine($"Document range at byte {response.Offset:N0}: {response.Bytes.Length:N0} bytes " +
            $"(sha256 {response.Sha256}).");
        text.Append(Encoding.UTF8.GetString(response.Bytes));
        if (response.Bytes.Length == 0 || response.Bytes[^1] != (byte)'\n') text.AppendLine();
        return text.ToString();
    }

    private static string RenderDecisionEvidenceExportPage(DecisionEvidenceExportResponse response)
    {
        var text = new StringBuilder();
        text.AppendLine($"Decision evidence page: {response.Rows.Count:N0} record(s)." +
            (response.Next is null ? " This is the last page." : " More records are available."));
        foreach (var row in response.Rows)
            text.AppendLine($"  Proposal {row.ProposalId}, {row.Kind} on {row.Subject} ({row.Tier}).");
        return text.ToString();
    }

    private static string RenderFormsQuery(FormsQueryResponse response)
    {
        var result = response.Result;
        var text = new StringBuilder();
        text.AppendLine($"Query results from Trial {response.TrialId}" +
            (response.IsStale ? " (stale evidence)." : "."));
        if (response.UnresolvedVariants > 0)
            text.AppendLine($"Unresolved variants: {response.UnresolvedVariants:N0}.");
        foreach (var note in response.NotMeasured.Take(TextListLimit)) text.AppendLine("Not measured: " + note);
        AppendTextListOverflow(text, response.NotMeasured.Count, TextListLimit);
        foreach (var row in result.Rows.Take(TextListLimit))
            text.AppendLine($"  {row.Form} ({row.Kind}) from {row.Base}" +
                (row.Tokens is { } tokens ? $"; {tokens:N0} tokens" : string.Empty));
        AppendTextListOverflow(text, result.Rows.Count, TextListLimit);
        foreach (var group in result.Groups.Take(TextListLimit))
            text.AppendLine($"  {string.Join(" / ", group.Key)}: {group.Count:N0}" +
                (group.Tokens is { } tokens ? $" ({tokens:N0} tokens)" : string.Empty));
        AppendTextListOverflow(text, result.Groups.Count, TextListLimit);
        foreach (var item in (result.Items ?? []).Take(TextListLimit))
        {
            text.AppendLine("  " + item.Id + ": " + string.Join("; ", item.Values.Take(TextListLimit).Select(pair =>
                pair.Key + "=" + string.Join(", ", pair.Value.Take(TextListLimit)) +
                (pair.Value.Length > TextListLimit
                    ? $"; and {pair.Value.Length - TextListLimit:N0} more values; use --json for all"
                    : string.Empty))));
            AppendTextListOverflow(text, item.Values.Count, TextListLimit);
        }
        AppendTextListOverflow(text, result.Items?.Count ?? 0, TextListLimit);
        foreach (var id in result.Ids.Take(TextListLimit)) text.AppendLine("  Id: " + id);
        AppendTextListOverflow(text, result.Ids.Count, TextListLimit);
        foreach (var score in (result.Scores ?? []).Take(TextListLimit)) RenderQueryScore(text, score, "Score");
        AppendTextListOverflow(text, result.Scores?.Count ?? 0, TextListLimit);
        if (result.Evidence is { } evidence) RenderQueryEvidence(text, evidence);
        foreach (var note in (result.Notes ?? []).Take(TextListLimit)) text.AppendLine("Note: " + note);
        AppendTextListOverflow(text, result.Notes?.Count ?? 0, TextListLimit);
        if (result.Rows.Count == 0 && result.Groups.Count == 0 && (result.Items is null || result.Items.Count == 0) &&
            result.Ids.Count == 0 && (result.Scores is null || result.Scores.Count == 0) &&
            result.Evidence is null && (result.Notes is null || result.Notes.Count == 0))
            text.AppendLine("No matching forms.");
        return text.ToString();
    }

    private static void RenderQueryEvidence(StringBuilder text, QueryEvidence evidence)
    {
        text.AppendLine($"Evidence: {evidence.Source}, {evidence.Members:N0} of {evidence.Universe:N0} member(s); " +
            $"{evidence.Shared.Count:N0} shared context(s), {evidence.Examples.Count:N0} example(s), " +
            $"{evidence.Questions.Count:N0} question(s), {evidence.TooFew:N0} context(s) too small to judge.");
        foreach (var context in evidence.Shared.Take(TextListLimit))
            text.AppendLine($"  Context {context.Label}: {context.Deviants:N0}/{context.Scope:N0} deviant(s); " +
                $"status {context.Status}; expressible {context.Expressible}; held-out {context.HeldOut}.");
        AppendTextListOverflow(text, evidence.Shared.Count, TextListLimit);
        foreach (var score in evidence.Scores.Take(TextListLimit)) RenderQueryScore(text, score, "Evidence score");
        AppendTextListOverflow(text, evidence.Scores.Count, TextListLimit);
        foreach (var example in evidence.Examples.Take(TextListLimit))
            text.AppendLine($"  Example {example.Id}: {example.Text}" +
                (example.Tokens is { } tokens ? $" ({tokens:N0} tokens)" : string.Empty));
        AppendTextListOverflow(text, evidence.Examples.Count, TextListLimit);
        foreach (var question in evidence.Questions.Take(TextListLimit))
            text.AppendLine($"  Question {question.Id}: {question.Text}");
        AppendTextListOverflow(text, evidence.Questions.Count, TextListLimit);
        if (evidence.SameShape is { } sameShape)
            text.AppendLine($"  Same shape {sameShape.Key}: {sameShape.Count:N0} members; " +
                $"rule attempt {(sameShape.RuleAttemptRequired ? "required" : "not required")} for {sameShape.Change}.");
        foreach (var note in evidence.NotMeasured.Take(TextListLimit)) text.AppendLine("  Not measured: " + note);
        AppendTextListOverflow(text, evidence.NotMeasured.Count, TextListLimit);
    }

    private static void RenderQueryScore(StringBuilder text, QueryScore score, string label)
    {
        text.AppendLine($"{label} {score.Scorer} ({score.Version}, {score.Unit}): {score.Verdict ?? "no verdict"}.");
        RenderQueryValues(text, "  Counts", score.Counts);
        RenderQueryValues(text, "  Numbers", score.Numbers);
        if (score.Controls is { Count: > 0 }) RenderQueryValues(text, "  Controls", score.Controls);
        foreach (var context in (score.Contexts ?? []).Take(TextListLimit))
            text.AppendLine($"  Context {context.Label}: {context.Deviants:N0}/{context.Scope:N0}; " +
                $"status {context.Status}; expressible {context.Expressible}; held-out {context.HeldOut}.");
        AppendTextListOverflow(text, score.Contexts?.Count ?? 0, TextListLimit);
        foreach (var note in score.Notes.Take(TextListLimit)) text.AppendLine("  Note: " + note);
        AppendTextListOverflow(text, score.Notes.Count, TextListLimit);
    }

    private static void RenderQueryValues<T>(StringBuilder text, string label, IReadOnlyDictionary<string, T> values)
    {
        if (values.Count == 0)
        {
            text.AppendLine(label + ": none.");
            return;
        }
        foreach (var pair in values.Take(TextListLimit))
            text.AppendLine($"{label} {pair.Key}: {pair.Value}.");
        AppendTextListOverflow(text, values.Count, TextListLimit);
    }

    private static void RenderQueryValues(StringBuilder text, string label, IReadOnlyDictionary<string, double?> values)
    {
        if (values.Count == 0)
        {
            text.AppendLine(label + ": none.");
            return;
        }
        foreach (var pair in values.Take(TextListLimit))
            text.AppendLine($"{label} {pair.Key}: {pair.Value?.ToString("G", CultureInfo.CurrentCulture) ?? "not available"}.");
        AppendTextListOverflow(text, values.Count, TextListLimit);
    }

    private static void AppendTextListOverflow(StringBuilder text, int total, int shown)
    {
        if (total > shown) text.AppendLine($"  and {total - shown:N0} more; use --json for all");
    }

    private static string ParsimonyThresholdOperatorText(ParsimonyThresholdOperator threshold) => threshold switch
    {
        ParsimonyThresholdOperator.GreaterThan => ">",
        ParsimonyThresholdOperator.GreaterThanOrEqual => ">=",
        ParsimonyThresholdOperator.Equal => "=",
        ParsimonyThresholdOperator.RankedOnly => "ranked-only",
        _ => threshold.ToString(),
    };

    private static string RenderNotebookRecordTypes(NotebookRecordTypesResponse response)
    {
        var text = new StringBuilder();
        text.AppendLine($"Notebook record types ({response.RecordTypes.Count:N0}):");
        foreach (var type in response.RecordTypes.Take(TextListLimit))
            text.AppendLine($"  {type.Name} ({type.Id})");
        AppendTextListOverflow(text, response.RecordTypes.Count, TextListLimit);
        return text.ToString();
    }

    private static string RenderParsimonyCandidateEvidence(ParsimonyCandidateEvidenceResponse response)
    {
        var text = new StringBuilder();
        text.AppendLine($"Parsimony comparison for Proposal {response.Candidate.ProposalId}, measure {response.MeasureId}: " +
            $"{response.FindingChanges.Count:N0} finding change(s).");
        text.AppendLine($"Approved analyses preserved: {response.ApprovedAnalysisPreservation.PreservedCount:N0}/" +
            $"{response.ApprovedAnalysisPreservation.BaselineCount:N0} ({response.ApprovedAnalysisPreservation.Status}).");
        foreach (var tuple in response.ApprovedAnalysisPreservation.UnmatchedBaselineTuples.Take(TextListLimit))
            text.AppendLine($"  Unmatched Approved analysis: wordform {tuple.WordformGuid}; " +
                $"analysis {tuple.AnalysisGuid}; content sha256 {tuple.ContentSha256}.");
        AppendTextListOverflow(text, response.ApprovedAnalysisPreservation.UnmatchedBaselineTuples.Count, TextListLimit);
        foreach (var change in response.FindingChanges.Take(TextListLimit))
        {
            text.AppendLine($"  Finding {change.Change}: {change.FindingId}");
            RenderCandidateFinding(text, "    Before", change.Before);
            RenderCandidateFinding(text, "    After", change.After);
        }
        AppendTextListOverflow(text, response.FindingChanges.Count, TextListLimit);
        foreach (var measure in (response.MoreMeasures ?? []).Take(TextListLimit))
        {
            text.AppendLine($"  Additional measure: {measure.MeasureId} ({measure.FindingChanges.Count:N0} changes).");
            foreach (var change in measure.FindingChanges.Take(TextListLimit))
            {
                text.AppendLine($"    Finding {change.Change}: {change.FindingId}");
                RenderCandidateFinding(text, "      Before", change.Before);
                RenderCandidateFinding(text, "      After", change.After);
            }
            AppendTextListOverflow(text, measure.FindingChanges.Count, TextListLimit);
        }
        AppendTextListOverflow(text, response.MoreMeasures?.Count ?? 0, TextListLimit);
        foreach (var timing in (response.StageTimings ?? []).Take(TextListLimit))
            text.AppendLine($"  Stage {timing.Stage} {timing.Side}: {timing.Milliseconds:N0} ms.");
        AppendTextListOverflow(text, response.StageTimings?.Count ?? 0, TextListLimit);
        return text.ToString();
    }

    private static void RenderCandidateFinding(StringBuilder text, string label, ParsimonyFinding? finding)
    {
        if (finding is null)
        {
            text.AppendLine(label + ": no finding.");
            return;
        }
        text.AppendLine($"{label}: {finding.Description ?? finding.MeasureId} ({finding.MeasureId}; {finding.Axis}; {finding.Tier}); " +
            $"{finding.Number.Numerator:N0}/{finding.Number.Denominator:N0} {finding.Number.Unit}; " +
            $"threshold {ParsimonyThresholdOperatorText(finding.Threshold.Operator)} " +
            $"{finding.Threshold.Value?.ToString("G", CultureInfo.CurrentCulture) ?? "ranked only"} " +
            $"({finding.Threshold.Version}).");
        text.AppendLine($"      Attached to {finding.AttachesTo.Kind} {finding.AttachesTo.Identity}" +
            (finding.GroupKey is null ? "." : $"; group {finding.GroupKey}."));
        text.AppendLine($"      Evidence digest: {finding.EvidenceDigest}; verification: {finding.Verification}; recipe: {finding.RecipeLink}.");
        RenderLimitedValues(text, "      item names", finding.ItemNames, name => name);
        RenderLimitedValues(text, "      limitations", finding.Limitations, limitation => limitation);
        foreach (var reference in finding.EvidenceRefs.Take(TextListLimit))
            text.AppendLine($"      Evidence view {reference.View} from bundle {reference.BundleId}: " +
                JsonSerializer.Serialize(reference.Arguments) + ".");
        AppendTextListOverflow(text, finding.EvidenceRefs.Count, TextListLimit);
    }

    private static string RenderParsimonyMeasureCatalog(ParsimonyMeasureCatalogResponse response)
    {
        var text = new StringBuilder();
        text.AppendLine($"Parsimony measure set {response.MeasureSetVersion}: {response.Measures.Count:N0} measure(s), " +
            $"{response.Views.Count:N0} view(s).");
        foreach (var measure in response.Measures.Take(TextListLimit))
        {
            text.AppendLine($"  {measure.Id}: {measure.Axis}; {measure.Tier} tier; counts {measure.Unit}; " +
                $"recipe {measure.RecipeLink}." +
                (measure.Partial is null ? string.Empty : " " + measure.Partial));
            text.AppendLine($"    Attaches to: {measure.AttachesTo}" +
                (measure.AuthoredObjectKind is null ? string.Empty : $" / {measure.AuthoredObjectKind}") +
                (measure.GroupKind is null ? string.Empty : $" / {measure.GroupKind}") + ".");
            text.AppendLine("    Threshold: " + (measure.Threshold is null ? "none." :
                $"{ParsimonyThresholdOperatorText(measure.Threshold.Operator)} " +
                $"{measure.Threshold.Value?.ToString("G", CultureInfo.CurrentCulture) ?? "ranked only"} " +
                $"({measure.Threshold.Version})."));
            RenderLimitedValues(text, "    required capabilities", measure.RequiredCapabilities, capability => capability);
            RenderLimitedValues(text, "    citations", measure.CitationKeys, citation => citation);
            RenderLimitedValues(text, "    evidence views", measure.EvidenceViews, view => view);
        }
        AppendTextListOverflow(text, response.Measures.Count, TextListLimit);
        foreach (var view in response.Views.Take(TextListLimit))
        {
            text.AppendLine($"  View {view.Code} v{view.Version}: {view.Description}; filter {view.FilterShape}.");
            RenderLimitedValues(text, "    required capabilities", view.RequiredCapabilities, capability => capability);
        }
        AppendTextListOverflow(text, response.Views.Count, TextListLimit);
        return text.ToString();
    }

    private static string RenderParsimonyNamedView(ParsimonyNamedViewResponse response)
    {
        var text = new StringBuilder();
        var definition = ParsimonyViewCatalog.Find(response.View);
        text.AppendLine($"Parsimony view {response.View} v{response.Version}: {response.Status}; bundle {response.BundleId}; " +
            $"showing {response.Returned:N0} of {response.Total?.ToString("N0", CultureInfo.CurrentCulture) ?? "unknown"} row(s)." +
            (response.Truncated ? " More rows are available." : string.Empty));
        text.AppendLine($"  Schema filter: {definition?.FilterShape ?? "unknown"}.");
        RenderLimitedValues(text, "  capabilities", response.Capabilities, capability => capability);
        text.AppendLine("  Items digest: " + (response.ItemsDigest ?? "unavailable") + ".");
        if (response.ActiveCount is not null || response.SuppressedCount is not null ||
            response.ResurfacedCount is not null || response.UnresolvedJudgmentCount is not null)
            text.AppendLine($"  Disposition counts: {response.ActiveCount?.ToString("N0", CultureInfo.CurrentCulture) ?? "unknown"} active, " +
                $"{response.SuppressedCount?.ToString("N0", CultureInfo.CurrentCulture) ?? "unknown"} suppressed, " +
                $"{response.ResurfacedCount?.ToString("N0", CultureInfo.CurrentCulture) ?? "unknown"} resurfaced, " +
                $"{response.UnresolvedJudgmentCount?.ToString("N0", CultureInfo.CurrentCulture) ?? "unknown"} unresolved.");
        if (response.JudgmentProjectionDigest is not null)
            text.AppendLine("  Judgment projection digest: " + response.JudgmentProjectionDigest);
        if (response.Detail is not null) text.AppendLine(response.Detail);
        foreach (var row in response.Rows.Take(TextListLimit))
            text.AppendLine("  " + JsonSerializer.Serialize(row, row.GetType()));
        AppendTextListOverflow(text, response.Rows.Count, TextListLimit);
        if (response.NextCursor is not null) text.AppendLine("Next cursor: " + response.NextCursor);
        return text.ToString();
    }

    private static string RenderProjectStoreUpgrade(ProjectStoreUpgradeResponse response)
    {
        var report = response.Report;
        var text = new StringBuilder();
        text.AppendLine($"Upgraded Motif store from schema {report.SourceSchema} to {report.TargetSchema}.");
        if (report.BackupPath is not null) text.AppendLine("Backup: " + report.BackupPath);
        foreach (var (table, count) in report.Carried.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            text.AppendLine($"  Kept {table}: {count:N0} row(s).");
        if (report.Dropped.Count > 0)
            text.AppendLine("Discarded computed tables: " + string.Join(", ", report.Dropped));
        foreach (var revision in report.UnreadableRevisions.Take(TextListLimit))
            text.AppendLine($"Unreadable revision in Proposal {revision.ProposalId}: {revision.Error}");
        AppendTextListOverflow(text, report.UnreadableRevisions.Count, TextListLimit);
        return text.ToString();
    }

    private static string RenderProposalDeleted(ProposalDeletedResponse response) =>
        $"Deleted Proposal {response.ProposalId} and {response.TrialsDeleted:N0} Trial(s).{Environment.NewLine}";

    private static string RenderProposalReview(ProposalReviewResponse response)
    {
        var text = new StringBuilder();
        text.AppendLine($"Proposal {response.ProposalId}" +
            (response.Label is null ? string.Empty : $" — {response.Label}") +
            $": {response.Entries.Count:N0} change(s), " +
            $"{response.LookedAtCount:N0} marked looked at.");
        foreach (var entry in response.Entries.Take(TextListLimit))
        {
            text.AppendLine($"  {entry.Ordinal + 1}. {entry.Title} ({entry.EntryKey})");
            RenderLimitedValues(text, "    operations", entry.OperationIds, id => id);
            if (entry.DependsOnEntries.Count > 0) RenderLimitedValues(text, "    depends on", entry.DependsOnEntries, id => id);
            if (entry.Citations.Count > 0) RenderLimitedValues(text, "    cites", entry.Citations,
                citation => citation.Locator is null ? citation.Source : $"{citation.Source}, {citation.Locator}");
            text.AppendLine($"    lookedAt: {entry.LookedAt.ToString().ToLowerInvariant()}");
            switch (entry)
            {
                case WordChangeEntry word:
                    RenderWordChangeEntry(text, word);
                    break;
                case GrammarChangeEntry grammar:
                    RenderGrammarChangeEntry(text, grammar);
                    break;
            }
        }
        AppendTextListOverflow(text, response.Entries.Count, TextListLimit);
        if (response.Trial is not null) text.AppendLine($"Latest Trial: {response.Trial.Status}.");
        if (response.Readiness is { } readiness)
        {
            text.AppendLine($"Apply readiness: {(readiness.IsReady ? "ready" : "not ready")}." +
                (readiness.CorrectnessAssessmentExempt
                    ? " Correctness Assessment is exempt: " + readiness.CorrectnessAssessmentExemptionReason
                    : readiness.CorrectnessAssessmentRequired ? " Correctness Assessment is required." : string.Empty));
            foreach (var reason in readiness.Reasons.Take(TextListLimit)) text.AppendLine("  " + reason);
            AppendTextListOverflow(text, readiness.Reasons.Count, TextListLimit);
        }
        foreach (var fit in response.Fits.Take(TextListLimit))
        {
            text.AppendLine($"Change {fit.ChangeId}: {fit.Status}.");
            foreach (var reason in fit.Reasons.Take(TextListLimit)) text.AppendLine("  " + reason);
            AppendTextListOverflow(text, fit.Reasons.Count, TextListLimit);
        }
        AppendTextListOverflow(text, response.Fits.Count, TextListLimit);
        return text.ToString();
    }

    private static void RenderWordChangeEntry(StringBuilder text, WordChangeEntry entry)
    {
        var change = entry.Change;
        text.AppendLine($"    Change: {change.Kind} for {change.Word} ({change.WordformId}).");
        if (change.DisplayReading is not null) text.AppendLine("    Display reading: " + change.DisplayReading);
        if (change.AssessmentId is not null) text.AppendLine("    Assessment: " + change.AssessmentId);
        if (change.StoredAnalysisId is not null) text.AppendLine("    Stored analysis: " + change.StoredAnalysisId);
        if (change.ReadingIndex is { } index) text.AppendLine($"    Reading index: {index:N0}");
        if (change.OriginPage is not null) text.AppendLine("    Origin page: " + change.OriginPage);
        foreach (var analysis in change.Analyses.Take(TextListLimit))
        {
            text.AppendLine($"    Existing analysis ({analysis.Opinion}; touched {analysis.Touched.ToString().ToLowerInvariant()}; " +
                $"stored {analysis.Stored.ToString().ToLowerInvariant()}):");
            RenderParserReading(text, "      ", analysis.Reading);
        }
        AppendTextListOverflow(text, change.Analyses.Count, TextListLimit);
        RenderPairedReading(text, "    Reading", entry.Reading);
        if (entry.Fit is { } fit) RenderReviewFit(text, fit);
    }

    private static void RenderPairedReading(StringBuilder text, string label, Paired<ParserReading?> reading)
    {
        if (reading.HasBefore && reading.Before is { } before)
        {
            text.AppendLine(label + " before:");
            RenderParserReading(text, "  ", before);
        }
        if (reading.HasAfter && reading.After is { } after)
        {
            text.AppendLine(label + " with Proposal:");
            RenderParserReading(text, "  ", after);
        }
        if (reading.AfterRefusal is { } refusal) text.AppendLine(label + " with Proposal unavailable: " + refusal.Message);
        if (!reading.HasBefore && !reading.HasAfter && reading.AfterRefusal is null)
            text.AppendLine(label + ": unavailable.");
    }

    private static void RenderParserReading(StringBuilder text, string indent, ParserReading reading)
    {
        if (reading.StoredAnalysisId is not null) text.AppendLine(indent + "Stored analysis: " + reading.StoredAnalysisId);
        if (reading.StoredAnalysisOpinion is not null) text.AppendLine(indent + "Stored opinion: " + reading.StoredAnalysisOpinion);
        foreach (var morph in reading.Morphs.Take(TextListLimit))
        {
            text.Append(indent).Append("Morph: ").Append(morph.Form);
            if (morph.FormWritingSystem is not null) text.Append(" (").Append(morph.FormWritingSystem).Append(')');
            text.Append(" — ").Append(morph.Gloss).Append(" [").Append(morph.Category).Append(']');
            if (morph.InflectionType is not null) text.Append("; inflection ").Append(morph.InflectionType);
            if (morph.Guessed) text.Append("; guessed");
            if (morph.Entry is not null) text.Append("; entry ").Append(morph.Entry);
            text.AppendLine();
        }
        AppendTextListOverflow(text, reading.Morphs.Count, TextListLimit);
    }

    private static void RenderReviewFit(StringBuilder text, ChangeFit fit)
    {
        text.AppendLine($"    Fit: {fit.Status}.");
        foreach (var reason in fit.Reasons.Take(TextListLimit)) text.AppendLine("      " + reason);
        AppendTextListOverflow(text, fit.Reasons.Count, TextListLimit);
        if (fit.Uncertainty is { } uncertainty)
            text.AppendLine("    Fit uncertainty: " + uncertainty.Reason);
    }

    private static void RenderGrammarChangeEntry(StringBuilder text, GrammarChangeEntry entry)
    {
        text.AppendLine("    Composer: " + entry.Composer);
        text.AppendLine("    Summary: " + entry.Summary);
        foreach (var effect in entry.Effects.Take(TextListLimit)) RenderEffectView(text, "    Effect", effect);
        AppendTextListOverflow(text, entry.Effects.Count, TextListLimit);
        foreach (var reading in entry.AffectedReadings.Take(TextListLimit)) RenderAffectedReading(text, reading);
        AppendTextListOverflow(text, entry.AffectedReadings.Count, TextListLimit);
        foreach (var part in entry.Parts.Take(TextListLimit)) RenderReviewPart(text, part);
        AppendTextListOverflow(text, entry.Parts.Count, TextListLimit);
    }

    private static void RenderEffectView(StringBuilder text, string label, EffectView effect)
    {
        text.AppendLine($"{label} {effect.Field} on {effect.CanonicalId}:");
        foreach (var change in effect.Changes.Take(TextListLimit))
            text.AppendLine($"      {change.Ws}: {change.Before ?? "(absent)"} → {change.After ?? "(absent)"}");
        AppendTextListOverflow(text, effect.Changes.Count, TextListLimit);
        RenderLimitedValues(text, "      operations", effect.OperationIds, id => id);
    }

    private static void RenderAffectedReading(StringBuilder text, RetirementAffectedReading reading)
    {
        text.AppendLine($"    Affected reading {reading.CaseId} for {reading.Word} ({reading.WordformId ?? "no wordform id"}): " +
            $"{reading.SurfaceBefore} → {reading.SurfaceAfter ?? "(none)"}; {reading.Opinion}; {reading.VerificationStatus}.");
        text.AppendLine($"      Rule attribution: {reading.RuleAttributionStatus}" +
            (reading.RuleId is null ? "." : $" ({reading.RuleId})."));
        if (reading.AttributionDetail is not null) text.AppendLine("      Attribution detail: " + reading.AttributionDetail);
    }

    private static void RenderReviewPart(StringBuilder text, RetirementReviewPart part)
    {
        text.AppendLine($"    Part {part.Title} ({part.Id}):");
        RenderLimitedValues(text, "      operations", part.OperationIds, id => id);
        foreach (var effect in part.Effects.Take(TextListLimit)) RenderEffectView(text, "      Effect", effect);
        AppendTextListOverflow(text, part.Effects.Count, TextListLimit);
        foreach (var detail in part.Details.Take(TextListLimit))
            text.AppendLine($"      Detail {detail.Kind} {detail.Label}: {detail.BeforeText ?? "(none)"} → {detail.AfterText ?? "(none)"}");
        AppendTextListOverflow(text, part.Details.Count, TextListLimit);
    }

    private static void RenderLimitedValues<T>(StringBuilder text, string label, IReadOnlyList<T> values,
        Func<T, string> format)
    {
        text.AppendLine(label + ": " + (values.Count == 0 ? "none" : string.Join(", ", values.Take(TextListLimit).Select(format))));
        AppendTextListOverflow(text, values.Count, TextListLimit);
    }

    private static string RenderWordReadState(WordReadStateResponse response)
    {
        var text = new StringBuilder();
        text.AppendLine($"Read occurrences: {response.ReadOccurrences.Count:N0}; " +
            $"skipped without a current parse: {response.SkippedOccurrences.Count:N0}; " +
            $"Baseline: {(response.HasBaseline ? "available" : "none")}.");
        foreach (var occurrence in response.ReadOccurrences.Take(TextListLimit))
            text.AppendLine($"  Text {occurrence.TextId}, paragraph {occurrence.ParagraphId}, " +
                $"segment {occurrence.SegmentId}, position {occurrence.Index}.");
        AppendTextListOverflow(text, response.ReadOccurrences.Count, TextListLimit);
        foreach (var occurrence in response.SkippedOccurrences.Take(TextListLimit))
            text.AppendLine($"  Skipped Text {occurrence.TextId}, paragraph {occurrence.ParagraphId}, " +
                $"segment {occurrence.SegmentId}, position {occurrence.Index}.");
        AppendTextListOverflow(text, response.SkippedOccurrences.Count, TextListLimit);
        return text.ToString();
    }

    private static string RenderGroup(GroupResponse group)
    {
        var text = new StringBuilder();
        text.Append($"Group {group.Kind}: {group.Query} (read {group.ReadAt}, parse state {group.ParseState}, min stem {group.MinStem}, fold {group.Fold})\n");
        text.Append($"{group.MatchingTypes:N0} of {group.CorpusTypes:N0} word types in {group.Groups:N0} stems: " +
            $"{group.Known:N0} known, {group.Fitting:N0} fitting, {group.NotFitting:N0} not fitting. Page {group.Page} of {group.Pages}.\n");
        foreach (var row in group.Rows)
        {
            text.Append($"{row.Stem}  [{row.Status}]  {row.Tokens:N0} tokens  " +
                string.Join(", ", row.Words.Select(word => $"{word.Word} ({word.ParseState}, {word.Tokens})")));
            if (row.WordsTotal > row.Words.Count) text.Append($" +{row.WordsTotal - row.Words.Count} more");
            if (row.SiblingsTotal > 0)
                text.Append("  | also: " + string.Join(", ", row.Siblings) +
                    (row.SiblingsTotal > row.Siblings.Count ? $" +{row.SiblingsTotal - row.Siblings.Count} more" : ""));
            text.Append('\n');
        }
        foreach (var note in group.Notes) text.Append(note).Append('\n');
        return text.ToString().TrimEnd('\n');
    }

    private static string RenderProposalComparison(ProposalComparison comparison)
    {
        var text = new StringBuilder();
        text.Append("Comparison of each Proposal's Trial before and after, and of the approved analyses each Proposal keeps and loses.\n");
        text.Append($"Snapshot {comparison.Snapshot} (pass it with --page)\n" +
            $"Forms compared: {comparison.FormsCompared:N0}  Diverging: {comparison.FormsDiverging:N0}  Rows: {comparison.RowsTotal:N0} ({comparison.Pages:N0} pages)\n");
        for (var index = 0; index < comparison.Columns.Count; index++)
        {
            var column = comparison.Columns[index];
            text.Append($"{index + 1}. {column.ProposalId}: {column.Gained:N0} gained, {column.Lost:N0} lost, " +
                $"{column.Unchanged:N0} unchanged, {column.NotMeasured:N0} not measured, {column.Incomplete:N0} incomplete; " +
                $"approved: {column.ApprovedKept:N0} kept, {column.ApprovedLost:N0} lost\n");
        }
        if (comparison.ApprovedSkippedOtherAgents > 0)
            text.Append($"{comparison.ApprovedSkippedOtherAgents:N0} analyses approved only by another human agent are not counted as approved.\n");
        text.Append($"Approved analyses: {comparison.ApprovedTotal:N0}; {comparison.ApprovedUncounted:N0} counted in no column.\n");
        text.Append(comparison.Complete ? "Comparison complete" :
            "Comparison incomplete: " + string.Join(", ", comparison.Limitations.Select(RenderLimitation)));
        if (comparison.Page is { } page)
            foreach (var row in page.Rows) text.Append('\n').Append(ProjectionJson.Serialize(row));
        return text.ToString();
    }

    private static string RenderLimitation(CompareLimitation item) => item.Code switch
    {
        CompareLimitation.ParserVersionDiffers => $"{item.Code} {item.Count} (PanGloss versions: {item.Detail ?? "unrecorded"})",
        CompareLimitation.TrialStale => $"{item.Code} {item.Count} (the Proposal changed after its Trial)",
        CompareLimitation.TrialSuperseded => $"{item.Code} {item.Count} (a newer Trial exists or the Proposal was superseded)",
        CompareLimitation.ApprovedUncounted => $"{item.Code} {item.Count} (approved analyses with no Reading identity or on words no Trial measured)",
        _ => item.Code + " " + item.Count,
    };

    private static string RenderTrialExport(TrialExportReceipt receipt)
    {
        var counts = receipt.Denominators;
        var location = receipt.PathIsStoreSuffix ? "Trial evidence next to the paired store" : "Trial evidence";
        var comparison = receipt.ComparisonComplete ? "Comparison complete" : "Comparison incomplete";
        var summary = $"{location}: {receipt.Path} ({receipt.Bytes:N0} bytes, {receipt.PublishMilliseconds:N0} ms to write and hash)\n" +
            $"Submitted: {counts.Submitted:N0}  Expanded: {counts.Expanded:N0}\n" +
            $"Before: {counts.Before.Dispatched:N0} dispatched, {counts.Before.Finished:N0} finished\n" +
            $"After: {counts.After.Dispatched:N0} dispatched, {counts.After.Finished:N0} finished\n" + comparison;
        return receipt.Page is null ? summary : summary + "\n" + ProjectionJson.Serialize(receipt.Page);
    }

    private static string RenderTrial(TrialRecord trial)
    {
        var text = new StringBuilder();
        text.AppendLine($"Trial {trial.JobId}: {trial.Status} ({trial.Freshness})");
        AppendDroppedGrammar(text, trial);
        if (trial.ReadingIdentity is { } identity) ProjectionText.AppendReadingIdentity(text, identity);
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
        if (work.WorkLimitStops + work.TimeSafetyNetStops > 0) AppendSuspectRules(text, trial);
        AppendWordResults(text, trial);
        return text.ToString();
    }

    // A stopped word has no per-rule cost, so rules the Dry Run flagged as expensive are the suspects.
    private static void AppendSuspectRules(StringBuilder text, TrialRecord trial)
    {
        var suspects = trial.DryRun?.Warnings
            .Where(warning => warning.Contains(DryRunWarningMarkers.AlphaRuleExpansion, StringComparison.Ordinal)).ToArray() ?? [];
        text.AppendLine("  No per-rule cost is recorded for stopped words." +
            (suspects.Length == 0 ? "" : " Rules this Proposal adds or edits that the Dry Run flagged as expensive:"));
        foreach (var suspect in suspects) text.AppendLine("    " + suspect);
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
        TrialStandingKind.WorkingSetOnly => "Working-set Trial only",
        TrialStandingKind.NeedsAnotherTry when standing.Reason == TrialStaleReason.BaselineMoved =>
            "Needs another try because the project changed",
        TrialStandingKind.NeedsAnotherTry when standing.Reason == TrialStaleReason.DroppedGrammarNotChecked =>
            "Needs another try because the check for dropped grammar did not finish",
        TrialStandingKind.NeedsAnotherTry when standing.Reason == TrialStaleReason.ParserChanged =>
            "Needs another try because the parser changed",
        TrialStandingKind.NeedsAnotherTry when standing.Reason == TrialStaleReason.EvidenceUnverified =>
            "Needs another try because the evidence could not establish current results",
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
                refusal.Reason, RefusalText.Bound(refusal.Message),
                refusal.Facts.Count > 0 ? RefusalText.Bound(refusal.Facts) : null, refusal.Code, refusal.ParserDiagnostic);
            return new CommandResult(
                FailureEnvelope.ExitCodeFor(refusal.Reason),
                ProjectionJson.Serialize(envelope) + Environment.NewLine, refusal.Reason);
        }

        var prefix = refusal.Code.StartsWith("corpus.", StringComparison.Ordinal) ? "" : "error: ";
        return new CommandResult(
            FailureEnvelope.ExitCodeFor(refusal.Reason), prefix + RefusalText.ForTerminal(refusal.Message, refusal.Code) + Environment.NewLine,
            refusal.Reason);
    }

    private static string RenderWorkerStop(WorkerStopResponse r)
    {
        if (r.Workers.Count == 0)
            return string.Format(CultureInfo.CurrentCulture, r.ProjectPath is null
                ? CliStrings.WorkerStopNoneForRoot : CliStrings.WorkerStopNoneForProject,
                r.ProjectPath ?? r.Root) + Environment.NewLine;
        return string.Concat(r.Workers.Select(worker => string.Format(CultureInfo.CurrentCulture,
            worker.Outcome == "stopped" ? CliStrings.WorkerStopStopped : CliStrings.WorkerStopSkipped,
            worker.Pid, worker.Build, worker.Root, worker.Note) + Environment.NewLine));
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
        else if (response.FailureDetail is { } uncodedDetail)
            text.AppendLine("  Failure: " + uncodedDetail);
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
        text.AppendLine(WordsGivenLine(page["inputWords"]?.GetValue<int>() ?? 0, page["lowercaseWords"]?.GetValue<int>() ?? 0));
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

    private static string WordsGivenLine(int given, int lowercase) =>
        $"  {given:N0} words given, {lowercase:N0} lowercase forms added";

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
        if (AssessSummary.Scope(response) is { } scope) text.AppendLine("  Scope: " + scope);
        foreach (var entry in response.Selection.Provenance)
            text.AppendLine("    " + entry.Source + ": " + entry.Count);
        text.AppendLine("  Assessments: " + string.Join(", ", response.AssessmentIds));
        text.AppendLine(CountsLine(response.Counts));
        text.AppendLine(WordsGivenLine(response.InputWords, response.LowercaseWords));
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
    private static string RenderStats(StatsCommandResponse response) =>
        (response.TrialScope is { } scope ? $"Scope: {scope}\n" : string.Empty) + (response.Text ?? string.Empty);

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

    private static string RenderSourceProgress(SourceProgressResponse response)
    {
        var lines = new List<string>();
        if (response.ApprovedPlaces is { } approved)
        {
            lines.Add($"Source {response.Source}: {approved:N0} of {response.EligiblePlaces:N0} eligible places approved");
            lines.Add("Evidence revision: " + response.EvidenceRevision);
            lines.AddRange(response.Passages.Select(row => $"{row.Locator}: {row.ApprovedPlaces:N0} of {row.EligiblePlaces:N0} eligible places approved"));
        }
        else
        {
            lines.AddRange([$"Source {response.Source}: {response.EligiblePlaces:N0} eligible places", "Approved places: unavailable",
                "Evidence revision: " + response.EvidenceRevision]);
            lines.AddRange(response.Passages.Select(row => $"{row.Locator}: {row.EligiblePlaces:N0} eligible places; approved unavailable"));
        }
        if (response.UnreadableApprovals.Count > 0)
            lines.Add("Approvals that cannot be read, so not counted: " + string.Join(", ", response.UnreadableApprovals));
        foreach (var conflict in response.ConflictedApprovals)
            lines.Add($"{conflict.Anchor}: approved with different analyses ({string.Join(", ", conflict.Analyses)}), so not counted. Approve it again to choose one.");
        if (response.OrphanedApprovals.Count > 0)
            lines.Add("Approvals for Passages the Source no longer has, so not counted: " + string.Join(", ", response.OrphanedApprovals));
        if (response.NextCursor is { } cursor) lines.Add("Next cursor: " + cursor);
        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    private static string RenderProgress(ProgressResponse response)
    {
        static string Counts(ProgressBuckets counts) =>
            $"{counts.Approved:N0}/{counts.Total:N0} approved, {counts.Misspelled:N0} misspelled, {counts.Unmeasured:N0} unmeasured";
        var lines = new List<string> { "Progress " + response.Revision };
        if (response.Totals is { } totals)
        {
            lines.Add("Tokens: " + Counts(totals.Tokens));
            lines.Add("Types:  " + Counts(totals.Types));
        }
        lines.AddRange(response.Rows.Select(row =>
            $"{row.Title} ({row.Id:D}): tokens {Counts(row.Tokens)}; types {Counts(row.Types)}"));
        if (!response.Freshness.JudgementsCurrent) lines.Add("Judgements may be stale; refresh the saved project.");
        lines.Add("Parser evidence, Checked and changed readings are unavailable.");
        if (response.NextCursor is { } cursor) lines.Add("Next cursor: " + cursor);
        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
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
        if (response.TrialScope is { } trialScope) text.AppendLine($"Trial scope: {trialScope}");
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

    private static string RenderRecordImport(RecordImportResponse r)
    {
        var text = new StringBuilder();
        text.AppendLine(r.Mode == "draft"
            ? $"Staged {r.OperationCount:N0} operation(s) from the {r.RecordKind} file in Draft {r.DraftName} ({r.ProposalId}); {r.RefusalCount:N0} change(s) refused and not staged. Nothing was applied."
            : $"Dry run of the {r.RecordKind} file: {r.ChangeCount:N0} change(s) lower to {r.OperationCount:N0} operation(s). Nothing was written.");
        text.AppendLine($"  record: {r.RecordId}; base: {r.BaseVerdict}; refused: {r.RefusalCount:N0}; page {r.Page} of {r.PageCount}");
        foreach (var change in r.Changes ?? [])
            text.AppendLine($"  change {(change.Status == "refused" ? "REFUSED " + change.RefusalCode + " " : "")}{change.Kind}  {change.Class}.{change.Field}{(change.WritingSystem is null ? "" : "[" + change.WritingSystem + "]")}: " +
                $"{change.Old ?? "(none)"} -> {change.New ?? "(cleared)"}");
        foreach (var operation in r.Operations)
            text.AppendLine($"  op {operation.OperationId}  {operation.Kind}  {operation.Target}");
        foreach (var refusal in r.Refusals)
            text.AppendLine($"  refused {refusal.Code}  {refusal.Class}.{refusal.Field}: {refusal.Reason}");
        if (r.Page < r.PageCount)
            text.AppendLine($"  More: --page {r.Page + 1}");
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
        if (!response.HasBaseline) return "No Baseline yet. Capture a Baseline to read the Grammar Guide." + Environment.NewLine;
        if (response.Records.Count == 0) return "This project has no Grammar Guide." + Environment.NewLine;
        var text = new StringBuilder();
        foreach (var record in response.Records) text.AppendLine($"{record.Abbreviation}  {record.Title}");
        return text.ToString();
    }

    private static string RenderGdrEdit(GdrEditResponse response)
    {
        var text = new StringBuilder();
        text.AppendLine(response.Fields.Count == 0
            ? $"{response.Abbreviation}: the Markdown matches the section, so nothing was staged in {response.DraftName}."
            : $"{response.Abbreviation}: staged {string.Join(", ", response.Fields)} in {response.DraftName} ({response.OperationCount} operation(s)).");
        foreach (var warning in response.Warnings) text.AppendLine("warning: " + warning);
        if (response.Fields.Count > 0)
            text.AppendLine($"Preview it with 'guide show {response.Abbreviation} --as-of {response.DraftName}'; Apply it with the rest of {response.DraftName}.");
        return text.ToString();
    }

    private static string RenderGdrCheck(GdrCheckResponse response)
    {
        if (!response.HasBaseline) return "No Baseline yet. Capture a Baseline to check the Grammar Guide." + Environment.NewLine;
        var text = new StringBuilder();
        var counts = response.Counts;
        text.AppendLine(response.InSync ? "The Grammar Guide is in sync with the grammar." : $"{response.Findings.Count} Guide warning(s). The Grammar Guide is not in sync with the grammar.");
        text.AppendLine($"Read {counts.Records} record(s), {counts.Sections} section(s), {counts.Links} link(s); " +
            $"{counts.ExamplesChecked} of {counts.Examples} example word(s) settled by stored Trial or Assessment rows.");
        foreach (var kind in response.Coverage) text.AppendLine($"  {kind.Kind}: {kind.Covered} of {kind.Objects} have a section");
        foreach (var finding in response.Findings)
        {
            text.AppendLine();
            text.AppendLine($"warning {finding.Code}: {finding.Message}");
            if (finding.Subjects.Count > 0)
                text.AppendLine("  " + string.Join(", ", finding.Subjects) + (finding.Count > finding.Subjects.Count ? $", and {finding.Count - finding.Subjects.Count} more" : ""));
        }
        if (response.IsStale) text.AppendLine().AppendLine("The project was saved since this Baseline, so capture a new one for a current check.");
        if (response.Timing is { } timing) text.AppendLine().AppendLine($"Opened the Baseline in {timing.LoadMilliseconds} ms; checked in {timing.CheckMilliseconds} ms.");
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

    private static string RenderLexicalEntries(LexicalEntryResponse response)
    {
        var text = new StringBuilder();
        foreach (var lookup in response.Lookups)
        {
            if (!lookup.Found)
            {
                text.AppendLine($"{lookup.Id}: not in the Baseline as an entry or a sense");
                continue;
            }
            text.AppendLine($"{lookup.Headword ?? "(no headword)"} ({lookup.EntryId})");
            foreach (var sense in lookup.Senses)
                text.AppendLine($"  {(sense.SenseId == lookup.SenseId ? "*" : " ")} {sense.Outline} {sense.Gloss ?? "(no gloss)"} ({sense.SenseId})");
        }
        text.AppendLine($"Baseline: {response.Baseline.BundleDigest}; FieldWorks source save {response.SourceLastWriteUtc:O}");
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
        foreach (var kind in response.ByKind.Take(KindRowLimit))
        {
            text.AppendLine($"  {kind.Code}: {kind.Count:N0} {kind.Level.ToWireValue()}" +
                (kind.YourWords is not { } words ? string.Empty : kind.WordAttributionComplete == true
                    ? $", {words:N0} of your words" : words == 0 ? ", no known matches; incomplete"
                    : $", at least {words:N0} of your words; incomplete"));
            if (CandidateLine(kind.ByMembershipOnly ?? 0, kind.BySpellingOnly ?? 0) is { } candidates)
                text.AppendLine($"    Not counted: {candidates}");
        }
        AppendMoreKinds(text, response.ByKind.Count);
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
        AppendFindingsPage(text, response.FindingsOffset, response.Findings.Count, response.FindingsTotal, response.NextOffset, response.NextStep);
        return text.ToString();
    }

    private const int KindRowLimit = 20;

    private static void AppendMoreKinds(StringBuilder text, int kinds)
    {
        if (kinds > KindRowLimit)
            text.AppendLine($"  and {kinds - KindRowLimit:N0} more finding codes; --json lists every one.");
    }

    private static void AppendFindingsPage(StringBuilder text, int? offset, int shown, int? total, int? next, string? step)
    {
        if (offset is not { } from || total is not { } all) return;
        text.AppendLine(shown == 0
            ? $"No findings at offset {from} of {all:N0}."
            : $"Showing findings {from + 1:N0}-{from + shown:N0} of {all:N0}.");
        if (next is { } following)
            text.AppendLine($"Next: {step ?? $"--offset {following} --limit {shown}"}, or --all for every finding.");
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

        var errorCount = response.ErrorTotal ?? response.Findings.Count(finding => finding.Severity == GrammarDiagnosticLevel.Error);
        var warningCount = response.WarningTotal ?? response.Findings.Count(finding => finding.Severity == GrammarDiagnosticLevel.Warning);
        var informationCount = response.InformationTotal ??
            response.Findings.Count(finding => finding.Severity == GrammarDiagnosticLevel.Information);
        var text = new StringBuilder();
        text.AppendLine($"Grammar findings: {response.FindingsTotal ?? response.Findings.Count:N0} " +
            $"({CountLabel(errorCount, "error", "errors")}, {CountLabel(warningCount, "warning", "warnings")}, " +
            $"{informationCount:N0} information)");
        text.AppendLine(CheckedInventory(response.CheckedLexemeCount, response.CheckedRuleCount));
        if (response.ObjectTimingDetail is { IsComplete: false, Description: { } detail }) text.AppendLine(detail);
        foreach (var advisory in response.Advisories)
            text.AppendLine($"Advisory: {advisory.Message}");
        foreach (var summary in response.Summary.Take(KindRowLimit))
            text.AppendLine($"  {summary.Code}: {summary.Count:N0} {summary.Level.ToWireValue()}");
        AppendMoreKinds(text, response.Summary.Count);
        foreach (var finding in response.Findings)
            text.AppendLine($"  {finding.Text}");
        AppendFindingsPage(text, response.FindingsOffset, response.Findings.Count, response.FindingsTotal, response.NextOffset, response.NextStep);
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

    private static string RenderRejectionTrace(RejectionTraceResponse response)
    {
        var text = new StringBuilder();
        text.AppendLine($"Rejection trace of {response.Input} against {response.TracedAgainst}: search {response.Search.Status}, " +
            $"{response.Outputs.Count:N0} output(s), {response.Bound.PathsShown:N0} of {response.Bound.TerminalPathsRetained:N0} retained path(s) shown.");
        foreach (var path in response.Paths)
        {
            text.AppendLine($"  Path {path.PathId} ({path.Outcome}, {path.Attribution}): {path.Sentence}");
            if (path.EarliestRejection is { } stop)
            {
                var subject = stop.Subject.Resolved
                    ? string.Join(", ", stop.Subject.Ids.Where(id => id.IsSubject).Select(id => id.Id))
                    : stop.Subject.Unknown ?? "unknown";
                text.AppendLine($"    Stopped by {stop.Reason} at {stop.Stage}; subject {stop.Subject.Kind} {subject}");
            }
        }
        foreach (var note in response.Notes) text.AppendLine("  " + note);
        if (response.Bound.Truncated)
            text.AppendLine($"  Left out here: {response.Bound.PathsOmittedByCap:N0} path(s), {response.Bound.OutputsOmittedByCap:N0} output(s). Raise --max-paths to see more.");
        return text.ToString();
    }

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
