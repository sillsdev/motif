using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.Parser;
using SIL.Motif.Host.Baselines;
using SIL.Motif.Projection;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Store;
using System.Text.Json;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Baselines;

namespace SIL.Motif.Commands.Catalog;

/// <summary>Reads stored Assessment timings for all or a selected set of words.</summary>
public static class TimingCommand
{
    /// <summary>Returns per-word timing and the stored PanGloss object timings for one Assessment.</summary>
    public static CommandOutcome<TimingResponse> Timing(TimingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Top <= 0 || request.By is not ("kind" or "rule") ||
            string.IsNullOrWhiteSpace(request.WordSet) || request.Rule is not null && request.Rule.Identity is null)
            return CommandOutcome<TimingResponse>.Refused(new Refusal(
                "timing.invalid-request", FailureReason.InvalidArgument,
                "Timing requires --by kind|rule and a positive --top value."));

        return ProjectStoreCommand.Run(request.ProjectPath, MotifProductVersion.CurrentText, (database, project) =>
        {
            var assessments = new AssessmentRepository(database);
            var current = CurrentEvidenceQuery.ReadCurrentEvidence(
                database, project, includeDefaultSelection: request.AssessmentId is null,
                includeResolvedReadings: false, includeWordContext: false);
            if (!current.Succeeded)
                return CommandOutcome<TimingResponse>.Refused(current.Refusal!);
            var currentEvidence = current.Value!;
            EvidenceSet? trialSet = null;
            IReadOnlyList<string> trialAssessmentIds = [];
            if (request.ProposalId is { } proposalId)
            {
                var trial = Trials.LatestFor(database, project, proposalId);
                if (trial is null)
                    return CommandOutcome<TimingResponse>.Refused(new Refusal("trial.none", FailureReason.NotFound,
                        "No Trial is recorded for this Proposal."));
                var trialEvidence = EvidenceSets.ForTrial(database, project, trial);
                if (!trialEvidence.Succeeded) return CommandOutcome<TimingResponse>.Refused(trialEvidence.Refusal!);
                trialSet = trialEvidence.Value;
                trialAssessmentIds = new[] { trialSet!.RootAssessmentId }.Concat(trialSet.ReplacementAssessmentIds)
                    .Where(id => id is not null).Select(id => id!).ToArray();
                if (currentEvidence.Baseline?.Token != trialSet.Baseline)
                    currentEvidence = currentEvidence with { Freshness = EvidenceFreshness.Stale };
            }
            AssessmentRecord? assessment = null;
            if (request.AssessmentId is not null)
            {
                try { assessment = assessments.Get(request.AssessmentId); }
                catch (KeyNotFoundException) { }
                if (assessment is not null && !assessment.Kind.IsStoredKind(AssessmentKind.ParseTime))
                    return RefusedTiming("timing.wrong-kind", "The named Assessment did not collect parse-time results.");
                if (trialSet is not null && !trialAssessmentIds.Contains(request.AssessmentId, StringComparer.Ordinal))
                    return RefusedTiming("timing.wrong-assessment",
                        "The named Assessment is not part of this Proposal's Trial.");
            }
            else
            {
                if (trialSet?.RootAssessmentId is { } trialRoot)
                {
                    try { assessment = assessments.Get(trialRoot); }
                    catch (KeyNotFoundException) { }
                }
                else assessment = currentEvidence.MatchingAssessment;
            }

            if (assessment is null || !assessment.Kind.IsStoredKind(AssessmentKind.ParseTime))
                return RefusedTiming("timing.no-assessment", "No matching stored ParseTime Assessment is available.");

            var evidenceSet = trialSet is not null
                ? AssessmentEvidenceSet.Create(assessment, trialSet.ReplacementAssessmentIds.Select(assessments.Get).ToArray())
                : request.AssessmentId is null ? currentEvidence.EvidenceSet! : AssessmentEvidenceSet.Create(assessment, []);
            var replacements = evidenceSet.Components.Skip(1).ToList();
            foreach (var overrideId in request.OverrideAssessmentIds ?? [])
            {
                AssessmentRecord? replacement;
                try { replacement = assessments.Get(overrideId); }
                catch (KeyNotFoundException) { replacement = null; }
                if (replacement is null)
                    return RefusedTiming("timing.override-not-found", "A stored re-run Assessment was not found.");
                if (trialSet is not null && !trialAssessmentIds.Contains(overrideId, StringComparer.Ordinal))
                    return RefusedTiming("timing.invalid-override",
                        "A re-run is not part of this Proposal's Trial.");
                if (!replacement.Kind.IsStoredKind(AssessmentKind.ParseTime) ||
                    replacement.BaselineToken != assessment.BaselineToken)
                    return RefusedTiming("timing.invalid-override", "A re-run must be a ParseTime Assessment of the same Baseline.");
                replacements.Add(replacement);
            }
            if (request.OverrideAssessmentIds is { Count: > 0 })
                evidenceSet = AssessmentEvidenceSet.Create(assessment, replacements);
            var selected = ResolveWords(request, evidenceSet.Words, database, currentEvidence);
            if (!selected.Succeeded) return CommandOutcome<TimingResponse>.Refused(selected.Refusal!);
            var selectedWords = selected.Value!;
            var selectedNames = selectedWords.Select(word => word.Word).ToHashSet(StringComparer.Ordinal);
            var objectRows = evidenceSet.ObjectTimings.Where(row => selectedNames.Contains(row.Word)).ToArray();
            var aggregates = TimingAggregation.Aggregate(selectedWords, objectRows, request.By, request.Rule, request.Top);
            var aggregatesAvailable = true;
            if (!evidenceSet.TimingDetail.IsComplete)
            {
                var wholeRun = evidenceSet.Components.Count == 1 && selectedWords.Count == evidenceSet.Words.Count;
                if (!wholeRun || assessment.TimingEvidence is not { HasRetriedWords: false, StatisticsUnavailable: false } timingEvidence)
                {
                    var wordTimes = selectedWords.Select(TimingAggregation.WordTimeMs).OfType<double>().ToArray();
                    aggregates = ([], [], new WordTimeAttribution(wordTimes.Length, wordTimes.Sum(), 0, null, null, 0, false));
                    aggregatesAvailable = false;
                }
                else
                {
                    var totals = TimingAggregation.AggregateTotals(selectedWords, timingEvidence, request.By,
                        assessment.Invocation?.InvocationId is { } invocation ? "invocation:" + invocation : null);
                    aggregates = (totals.Aggregates, aggregates.CostliestWords, totals.Attribution);
                }
            }
            var summary = TimingAggregation.SummarizeWords(selectedWords, request.Top);
            BaselineToken? measuredBaseline;
            try { measuredBaseline = JsonSerializer.Deserialize<BaselineToken>(assessment.BaselineToken, MotifJson.CreateOptions()); }
            catch (JsonException) { measuredBaseline = null; }
            DateTimeOffset? measuredSave = null;
            string? measuredBaselinePath = null;
            if (assessment.Invocation?.InvocationId is { } invocationId)
            {
                try
                {
                    var retained = new RetainedInvocationRepository(database).Get(invocationId);
                    measuredBaselinePath = retained.BaselineFwDataPath;
                    if (retained.BaselineToken == measuredBaseline) measuredSave = retained.BaselineSourceLastWriteUtc;
                }
                catch (KeyNotFoundException) { }
            }
            if (measuredBaseline is not null && measuredBaseline == currentEvidence.Baseline?.Token)
            {
                measuredBaselinePath ??= currentEvidence.Baseline.FwDataPath;
                measuredSave ??= currentEvidence.Baseline.SourceLastWriteUtc;
            }
            var relation = measuredBaseline is null || currentEvidence.Baseline is null
                ? TimingEvidenceRelation.Unknown
                : measuredBaseline != currentEvidence.Baseline.Token ? TimingEvidenceRelation.Historical
                : currentEvidence.LastFieldWorksSaveUtc > measuredSave ? TimingEvidenceRelation.SavedSince
                : TimingEvidenceRelation.Current;
            var namedAggregates = NameLexicalEntries(aggregates.Aggregates, measuredBaselinePath);
            return CommandOutcome<TimingResponse>.Success(new TimingResponse(
                assessment.AssessmentId, request.WordSet, request.By, selectedWords.Count,
                summary.MedianMs, summary.Percentile95Ms, summary.SlowestWords,
                namedAggregates, aggregates.CostliestWords)
            {
                ObjectTimingDetail = evidenceSet.TimingDetail,
                ObjectTimingAggregatesAvailable = aggregatesAvailable,
                IsStale = relation is TimingEvidenceRelation.Historical or TimingEvidenceRelation.SavedSince,
                EvidenceRelation = relation,
                Baseline = measuredBaseline,
                SourceLastWriteUtc = measuredSave,
                CurrentProjectIsStale = currentEvidence.Freshness == EvidenceFreshness.Stale,
                Attribution = aggregates.Attribution,
                WorkStepAggregates = evidenceSet.TimingDetail.IsComplete
                    ? WorkStepsByObject(objectRows, selectedWords) : [],
                Words = selectedWords.Select(word => new TimingWordRow(word.Word, word.ElapsedMs,
                    IsStepLimited(word) ? TimingCompletion.StepLimit :
                    word.Outcome == WordOutcome.TimedOut.ToStoredOutcome() || word.Morphology?.TimedOut == true ? "Time limit" :
                    ParserRefusals.Of(word.Morphology, word.Outcome) is not null ? TimingCompletion.Refused : TimingCompletion.Finished)
                    { ElapsedNs = word.ElapsedNs, WorkSteps = word.WorkSteps, Origin = word.Origin }).ToArray(),
            });
        });
    }

    private static IReadOnlyList<TimingWorkAggregate> WorkStepsByObject(
        IReadOnlyList<AssessmentObjectTiming> rows, IReadOnlyList<AssessedWord> words)
    {
        var byWord = rows.ToLookup(row => row.Word, StringComparer.Ordinal);
        if (words.Any(word => word.WorkSteps is not { } total ||
            !byWord[word.Word].Any(row => row.Kind == "unattributed") ||
            byWord[word.Word].Any(row => row.WorkSteps is null ||
                ObjectIdentity.Create(row.Kind, row.Key, row.IdentityQuality, row.Scope) is null) ||
            byWord[word.Word].Sum(row => row.WorkSteps!.Value) != total)) return [];
        return rows.Where(row => row.WorkSteps is not null)
            .GroupBy(row => ObjectIdentity.Create(row.Kind, row.Key, row.IdentityQuality, row.Scope))
            .Where(group => group.Key is not null)
            .Select(group =>
            {
                var first = group.First();
                return new TimingWorkAggregate(first.Key, first.Object, first.Kind,
                    group.Sum(row => row.WorkSteps!.Value), first.Kind == "unattributed");
            })
            .OrderBy(row => row.IsUnattributed).ThenByDescending(row => row.WorkSteps)
            .ThenBy(row => row.Name, StringComparer.Ordinal).ToArray();
    }

    private static IReadOnlyList<TimingAggregateRow> NameLexicalEntries(
        IReadOnlyList<TimingAggregateRow> rows, string? baselinePath)
    {
        if (baselinePath is null || !File.Exists(baselinePath) ||
            !rows.Any(row => row.Kind == "lex_entry" && row.IdentityQuality == "authored")) return rows;

        using var reader = BaselineReadCache.Open(baselinePath);
        return rows.Select(row =>
        {
            if (row.Kind != "lex_entry" || row.IdentityQuality != "authored") return row;
            var facts = ObjectFactsReader.Read(reader.Cache, ObjectUseRef.ForTimingKey(row.TimingKey), _ => null);
            if (facts?.Entry is not { Headword.Length: > 0 } entry) return row;
            var glosses = facts.Senses.Select(sense => sense.Gloss)
                .Where(gloss => !string.IsNullOrWhiteSpace(gloss)).Distinct(StringComparer.Ordinal).ToArray();
            var name = glosses.Length == 0 ? entry.Headword : $"{entry.Headword} · {string.Join(", ", glosses)}";
            return row with { Name = name };
        }).ToArray();
    }

    private static CommandOutcome<IReadOnlyList<AssessedWord>> ResolveWords(
        TimingRequest request,
        IReadOnlyList<AssessedWord> allWords,
        MotifDatabase database,
        CurrentEvidenceSnapshot currentEvidence)
    {
        if (request.ExplicitWords is { Count: > 0 } explicitWords)
        {
            var explicitSet = NormalizeWords(explicitWords);
            return Success(allWords.Where(word => explicitSet.Contains(word.Word)).ToArray());
        }

        var selection = TimingWordSet.Parse(request.WordSet);
        if (selection is null)
            return RefusedInvalidWordSet("A matrix cell must use cell:<standing>:<column>.");
        if (selection is TimingWordSet.All) return Success(allWords);
        if (selection is TimingWordSet.StepLimited)
            return Success(allWords.Where(IsStepLimited).ToArray());
        if (selection is TimingWordSet.Slowest)
            return Success(TimingAggregation.SelectSlowestWords(allWords, request.Top));
        if (selection is TimingWordSet.MatrixCell cell)
        {
            var standing = TimingWordSet.StandingName(cell.Standing);
            return Success(allWords.Where(word =>
            {
                var placement = Place(word);
                return placement.Column == cell.Column && StringComparer.Ordinal.Equals(placement.Standing, standing);
            }).ToArray());
        }

        var name = ((TimingWordSet.Named)selection).Name;
        var repository = new NamedSelectionRepository(database);
        var saved = repository.Get(name);
        if (saved is null)
            return RefusedWords("timing.word-set-not-found", $"No saved Selection named '{name}' exists.");
        if (currentEvidence.ProjectSummary is not { } summary)
            return RefusedWords("timing.no-baseline", "A Baseline is required to resolve a saved Selection.");
        var resolved = CurrentEvidenceQuery.ResolveSelection(summary, saved);
        if (!resolved.Succeeded)
            return CommandOutcome<IReadOnlyList<AssessedWord>>.Refused(resolved.Refusal!);
        var selectedNames = resolved.Value!.Selection.Words.ToHashSet(StringComparer.Ordinal);
        return Success(allWords.Where(word => selectedNames.Contains(word.Word)).ToArray());
    }

    private static ComparePlacement Place(AssessedWord word) => CompareSemantics.Place(new CompareWordFacts(
        word.ProjectStanding, word.Outcome, word.IsIncomplete, word.Morphology,
        word.ReadingGrades, word.MissedApprovedCount ?? 0) { AnalysisComparison = word.AnalysisComparison });

    private static bool IsStepLimited(AssessedWord word) =>
        word.Outcome == WordOutcome.Capped.ToStoredOutcome() || word.Morphology?.Capped == true;

    private static HashSet<string> NormalizeWords(IEnumerable<string> words) => words
        .Where(word => !string.IsNullOrWhiteSpace(word))
        .Select(word => word.Trim().Normalize(System.Text.NormalizationForm.FormD))
        .ToHashSet(StringComparer.Ordinal);

    private static CommandOutcome<IReadOnlyList<AssessedWord>> Success(IReadOnlyList<AssessedWord> words) =>
        CommandOutcome<IReadOnlyList<AssessedWord>>.Success(words);

    private static CommandOutcome<IReadOnlyList<AssessedWord>> RefusedWords(string code, string message) =>
        CommandOutcome<IReadOnlyList<AssessedWord>>.Refused(new Refusal(code, FailureReason.NotFound, message));

    private static CommandOutcome<IReadOnlyList<AssessedWord>> RefusedInvalidWordSet(string message) =>
        CommandOutcome<IReadOnlyList<AssessedWord>>.Refused(new Refusal(
            "timing.invalid-word-set", FailureReason.InvalidArgument, message));

    private static CommandOutcome<TimingResponse> RefusedTiming(string code, string message) =>
        CommandOutcome<TimingResponse>.Refused(new Refusal(code, FailureReason.NotFound, message));
}
