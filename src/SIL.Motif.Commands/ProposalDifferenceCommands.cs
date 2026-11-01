using System.Globalization;
using System.Text;
using System.Text.Json;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Host.Analysis;
using SIL.Motif.Host.Parser;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Commands;

/// <summary>
/// Compares a completed Trial's parses with the human decisions FieldWorks records, and reads the saved result.
/// </summary>
/// <remarks>
/// The comparison reads only stored evidence: two ParseTime Assessments and the current Baseline's captured
/// decisions. It starts no parser and writes the whole result in one transaction.
/// </remarks>
public static class ProposalDifferenceCommands
{
    private const string NoStateName = "unavailable";

    // Built once: fresh options per word rebuilt the converter metadata every time.
    private static readonly JsonSerializerOptions Json = MotifJson.CreateOptions();

    /// <summary>Compares the Trial's before and after parses of one Text against human decisions and saves the result.</summary>
    public static CommandOutcome<ProposalDifferenceResponse> Produce(ProposalDifferenceProduceRequest request) =>
        Produce(request, ProposalDifferenceReferences.Human);

    /// <summary>
    /// Compares the Trial's before and after parses of one Text and saves the result. The reference is
    /// <c>human</c> (FieldWorks' recorded decisions) or <c>source-validated</c> (decisions a named non-human agent
    /// staged through Applied Proposals, never a person's). It is an argument, not a request field, so the default
    /// agent tool is unchanged.
    /// </summary>
    /// <param name="request">The Proposal, the Text and, optionally, one completed Trial.</param>
    /// <param name="reference">Which decisions the parses are compared with.</param>
    public static CommandOutcome<ProposalDifferenceResponse> Produce(ProposalDifferenceProduceRequest request,
        string reference) => Produce(request, reference, null, default);

    /// <summary>The same, told how far it has got and able to be cancelled.</summary>
    /// <param name="request">The Proposal, the Text and, optionally, one completed Trial.</param>
    /// <param name="reference">Which decisions the parses are compared with.</param>
    /// <param name="progress">Told how far the comparison has got; the CLI and MCP pass none.</param>
    /// <param name="cancellationToken">
    /// Stops the comparison; a cancelled run saves nothing and returns <c>difference.cancelled</c>.
    /// </param>
    public static CommandOutcome<ProposalDifferenceResponse> Produce(ProposalDifferenceProduceRequest request,
        string reference, IProgress<AssessmentProgress>? progress, CancellationToken cancellationToken) =>
        ProduceCore(request, reference, progress, cancellationToken);

    /// <summary>
    /// The window's Make Difference: compared with the decisions the Proposal's saved Difference for the Text was
    /// read against, or a person's when it has none, resolved inside the store run so a write by another process
    /// can never be replaced by the other reference.
    /// </summary>
    /// <param name="request">The Proposal, the Text and, optionally, one completed Trial.</param>
    /// <param name="progress">Told how far the comparison has got.</param>
    /// <param name="cancellationToken">Stops the comparison; a cancelled run saves nothing.</param>
    public static CommandOutcome<ProposalDifferenceResponse> Produce(ProposalDifferenceProduceRequest request,
        IProgress<AssessmentProgress>? progress, CancellationToken cancellationToken) =>
        ProduceCore(request, null, progress, cancellationToken);

    private static CommandOutcome<ProposalDifferenceResponse> ProduceCore(ProposalDifferenceProduceRequest request,
        string? reference, IProgress<AssessmentProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (reference is not (null or ProposalDifferenceReferences.Human or ProposalDifferenceReferences.SourceValidated))
            return Refuse("difference.invalid-reference", FailureReason.InvalidArgument,
                "The reference is human or source-validated. To read a Proposal against both, produce a Difference " +
                "against each in turn; a Proposal keeps one Difference per Text, so the newer replaces the older.");
        if (cancellationToken.IsCancellationRequested) return Cancelled();
        return ProjectStoreCommand.Run(request.ProjectPath, MotifProductVersion.CurrentText, (database, project) =>
        {
            if (!CanonicalId.TryParse(request.ProposalId, out _))
                return Refuse("difference.invalid-proposal", FailureReason.InvalidArgument,
                    "The Proposal id is not a Motif id. Use an id that `motif proposals` lists.");
            if (!Guid.TryParse(request.TextId, out var textId))
                return Refuse("difference.invalid-text", FailureReason.InvalidArgument,
                    "The Text id is not a GUID. Use an id that `motif texts list` shows.");

            var trial = Trials.ListFor(database, project, request.ProposalId)
                .FirstOrDefault(record => request.JobId is null
                    ? record.Status == JobStatus.Completed : record.JobId == request.JobId);
            if (trial is null)
                return Refuse("difference.no-trial", FailureReason.NotFound, request.JobId is null
                    ? "The Proposal has no completed Trial. Run: motif proposal trial --project <fwdata> <proposalId>"
                    : "The Proposal has no Trial with that job id.");
            if (trial.Status != JobStatus.Completed)
                return Refuse("difference.trial-incomplete", FailureReason.Refused,
                    "The Trial did not complete. Trial the Proposal again before comparing it.");

            var saved = new ProposalDifferenceRepository(database);
            var existing = saved.FindFor(request.ProposalId, textId.ToString("D"));
            var resolved = reference ?? existing?.Summary.Reference ?? ProposalDifferenceReferences.Human;
            var sourceValidated = resolved == ProposalDifferenceReferences.SourceValidated
                ? SourceValidatedDecisions.Read(database) : null;
            var decided = saved.ProposalIsDecided(request.ProposalId);
            if (existing is not null && IsSameComparison(existing.Summary, trial, resolved, sourceValidated?.Digest) &&
                (existing.WordDetailKept || decided))
                return Show(database, existing.Summary.DifferenceId, new ProposalDifferenceFilter(), 0, 25);
            if (decided) return Decided();

            var assessments = new AssessmentRepository(database);
            var job = new JobRepository(database).Get(trial.JobId) ?? throw new InvalidDataException("Trial job not found.");
            var input = TrialJobInput.Parse(job.InputJson);
            if (!trial.AssessmentIds.TryGetValue(AssessmentKinds.ParseTime, out var afterId))
                return Refuse("difference.no-parse-assessment", FailureReason.Refused,
                    "The Trial recorded no ParseTime Assessment, so there are no parses to compare.");
            var after = assessments.GetSelected(afterId);
            var recorded = (input.BaselineAssessmentIds ?? []).Select(assessments.GetSelected).ToArray();
            var before = recorded.FirstOrDefault(record =>
                record.Kind == AssessmentKinds.ParseTime && record.Assessor == after.Assessor &&
                record.BaselineToken == after.BaselineToken);
            var noBefore = NoBeforeMessage(recorded, before, after);
            if (noBefore is not null)
                return Refuse("difference.no-before", FailureReason.Refused, noBefore);

            var key = ProjectWorkspaceKey.Compute(project);
            var baselines = new BaselineRepository(database);
            var evidence = baselines.GetCurrentEvidence(key);
            if (evidence is null)
                return Refuse("difference.no-baseline", FailureReason.NotFound,
                    "The project has no Baseline. Open the project so its decisions can be captured.");
            if (!evidence.Baseline.Token.HasSameSemanticIdentity(trial.Baseline))
                return Refuse("difference.baseline-changed", FailureReason.Refused,
                    "The project changed after this Trial ran, so its recorded decisions may differ. Trial the Proposal again.");
            var summaryText = evidence.Summary.Texts.FirstOrDefault(text => text.TextId == textId);
            if (summaryText is null)
                return Refuse("difference.text-not-found", FailureReason.NotFound,
                    "The current Baseline has no Text with that id. Use an id that `motif texts list` shows.");
            var read = baselines.ReadCurrentSelectionIndexes(key, [textId])
                ?? throw new InvalidDataException("The Baseline vanished during the read.");
            if (!read.Baseline.Token.HasSameSemanticIdentity(trial.Baseline))
                return Refuse("difference.baseline-changed", FailureReason.Refused,
                    "The project changed after this Trial ran, so its recorded decisions may differ. Trial the Proposal again.");
            var text = read.Texts.Single();

            Built built;
            try { built = Build(text, before!.Words!, after.Words!, sourceValidated, progress, cancellationToken); }
            catch (DifferenceRefusalException refusal) { return CommandOutcome<ProposalDifferenceResponse>.Refused(refusal.Refusal); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return Cancelled(); }
            var summary = ProposalDifferenceSummaries.Create(built, trial.JobId, request.ProposalId, before, after, text,
                JsonSerializer.Serialize(read.Baseline.Token, Json), resolved, sourceValidated?.Digest, sourceValidated?.Unreadable);
            if (cancellationToken.IsCancellationRequested) return Cancelled();
            // Publish checks the token once more just before it commits, and rolls back when it was cancelled.
            try { saved.Publish(new NewProposalDifference(summary, built.Words), cancellationToken); }
            catch (ProposalDecidedException) { return Decided(); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return Cancelled(); }
            return Show(database, summary.DifferenceId, new ProposalDifferenceFilter(), 0, 25);
        });
    }

    private static CommandOutcome<ProposalDifferenceResponse> Cancelled() =>
        Refuse(RefusalCodes.DifferenceCancelled, FailureReason.Cancelled,
            "The comparison was cancelled before anything was saved.");

    private static CommandOutcome<ProposalDifferenceResponse> Decided() =>
        Refuse("difference.proposal-decided", FailureReason.Refused,
            "The Proposal is decided, so its word detail is gone and a new comparison would not be kept. " +
            "Read the saved summary with: motif difference show --project <fwdata> --proposal <id>");

    // Same Trial, Baseline, policy, reference and staged decisions: the saved result already answers it.
    private static bool IsSameComparison(ProposalDifferenceSummary summary, TrialRecord trial, string reference,
        string? decisionsDigest) =>
        summary.TrialJobId == trial.JobId && summary.Policy == new ProposalDifferencePolicy() &&
        summary.Reference == reference && summary.DecisionsDigest == decisionsDigest &&
        JsonSerializer.Deserialize<BaselineToken>(summary.BaselineToken, Json) is { } token &&
        token.HasSameSemanticIdentity(trial.Baseline);

    // Says which condition left the Trial without a usable before side, and what to do about it.
    private static string? NoBeforeMessage(IReadOnlyList<AssessmentRecord> recorded, AssessmentRecord? before,
        AssessmentRecord after)
    {
        if (recorded.Count == 0)
            return "No Baseline Assessment was recorded for this Trial, so there is no before side. " +
                "Re-run the Trial with: motif proposal trial --project <fwdata> <proposalId>; it now records one.";
        if (!recorded.Any(record => record.Kind == AssessmentKinds.ParseTime && record.Assessor == after.Assessor))
            return $"The Trial's Baseline Assessments include no ParseTime Assessment by the assessor '{after.Assessor}' " +
                "that measured the Trial. Re-run the Trial so its Baseline is assessed with the same assessor.";
        if (before is null)
            return "The Baseline changed since the Trial: its Baseline ParseTime Assessment was taken on a different " +
                "Baseline token. Recapture the Baseline, then re-run the Trial.";
        if (before.Words is not { Count: > 0 } || after.Words is not { Count: > 0 })
            return "A ParseTime Assessment of this Trial has no per-word results to compare. " +
                "Re-run the Trial so both sides record their words.";
        return null;
    }

    /// <summary>Reads a saved Difference's summary and one page of its matching words.</summary>
    public static CommandOutcome<ProposalDifferenceResponse> Show(ProposalDifferenceShowRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Offset < 0 || request.Limit is < 1 or > 200)
            return Refuse("difference.invalid-page", FailureReason.InvalidArgument,
                "Offset must be zero or more and limit between 1 and 200.");
        ProposalDifferenceBucket? bucket = null;
        if (request.Bucket is not null)
        {
            if (!TryParseBucket(request.Bucket, out var parsed))
                return Refuse("difference.invalid-bucket", FailureReason.InvalidArgument,
                    "Choose a category code such as delta.regression, delta.overgeneration or delta.improved.unique.");
            bucket = parsed;
        }
        HumanAgreementLimitation? limitation = null;
        if (request.Limitation is not null)
        {
            if (!TryParseLimitation(request.Limitation, out var parsedLimitation))
                return Refuse("difference.invalid-limitation", FailureReason.InvalidArgument,
                    "Choose a limitation code such as capped, timed-out, projection-unavailable or parser-refused.");
            limitation = parsedLimitation;
        }
        if (request.OccurrenceOffset is < 0 || request.OccurrenceLimit is < 1 or > 200)
            return Refuse(RefusalCodes.DifferenceInvalidPage, FailureReason.InvalidArgument,
                "Occurrence offset must be zero or more and occurrence limit between 1 and 200.");
        if (request.Word is null && (request.OccurrenceOffset is not null || request.OccurrenceLimit is not null))
            return Refuse("difference.occurrence-needs-word", FailureReason.InvalidArgument,
                "Occurrence offset and occurrence limit page one word's places, so give the word as well.");
        string? word = null;
        if (request.Word is not null)
        {
            var trimmed = request.Word.Trim();
            if (trimmed.Length == 0)
                return Refuse("difference.invalid-word", FailureReason.InvalidArgument,
                    "Give the word to show places for, or leave the word out.");
            word = Nfd(trimmed);
        }
        foreach (var state in new[] { request.BeforeState, request.AfterState })
            if (state is not null && !IsStateName(state))
                return Refuse("difference.invalid-state", FailureReason.InvalidArgument,
                    "Choose a state such as approved-unique, approved-with-alternatives, no-approved-match or no-parse.");

        return ProjectStoreCommand.Run(request.ProjectPath, MotifProductVersion.CurrentText, (database, _) =>
        {
            var repository = new ProposalDifferenceRepository(database);
            var id = request.DifferenceId ?? (request.ProposalId is null ? null : repository.LatestFor(request.ProposalId));
            if (id is null && request.ProposalId is not null && repository.ProposalIsDecided(request.ProposalId))
                return Refuse("difference.none", FailureReason.NotFound,
                    "This Proposal was decided and has no saved Difference, and a new one cannot be made for a decided Proposal.");
            if (id is null)
                return Refuse("difference.none", FailureReason.NotFound,
                    "No saved Difference was found. Run: motif difference produce --project <fwdata> --proposal <id> --text <guid>");
            return Show(database, id, new ProposalDifferenceFilter(request.CouldBeBad, bucket,
                request.BeforeState, request.AfterState, limitation, word), request.Offset, request.Limit,
                word is null ? null : new ProposalDifferenceOccurrenceRequest(word,
                    request.OccurrenceOffset ?? 0, request.OccurrenceLimit ?? 25), cancellationToken);
        });
    }

    private static CommandOutcome<ProposalDifferenceResponse> Show(MotifDatabase database,
        string differenceId, ProposalDifferenceFilter filter, int offset, int limit,
        ProposalDifferenceOccurrenceRequest? occurrences = null, CancellationToken cancellationToken = default)
    {
        var page = new ProposalDifferenceRepository(database).ReadPage(differenceId, filter, offset, limit, occurrences,
            cancellationToken);
        if (page is null)
            return Refuse("difference.none", FailureReason.NotFound, "There is no Difference with that id.");
        var next = offset + page.Words.Count < page.Matching ? offset + page.Words.Count : (int?)null;
        var words = page.Words;
        IReadOnlyList<ProposalDifferenceUnreadableEvidence>? unreadable = null;
        if (page.Saved.Summary.Reference == ProposalDifferenceReferences.SourceValidated && words.Count > 0)
        {
            var decisions = SourceValidatedDecisions.Read(database);
            words = decisions.WithEvidence(words);
            if (decisions.Unreadable.Count > 0) unreadable = decisions.Unreadable;
        }
        return CommandOutcome<ProposalDifferenceResponse>.Success(new ProposalDifferenceResponse(
            page.Saved.Summary, words, page.Matching, next, page.Saved.WordDetailKept, page.Occurrences, unreadable));
    }

    private static CommandOutcome<ProposalDifferenceResponse> Refuse(string code, FailureReason reason, string message) =>
        CommandOutcome<ProposalDifferenceResponse>.Refused(new Refusal(code, reason, message));

    private static bool TryParseBucket(string code, out ProposalDifferenceBucket bucket)
    {
        try
        {
            var options = new JsonSerializerOptions();
            options.Converters.Add(new StrictStringEnumConverter<ProposalDifferenceBucket>());
            bucket = JsonSerializer.Deserialize<ProposalDifferenceBucket>(JsonSerializer.Serialize(code), options);
            return true;
        }
        catch (JsonException) { bucket = default; return false; }
    }

    private static bool TryParseLimitation(string code, out HumanAgreementLimitation limitation)
    {
        try
        {
            var options = new JsonSerializerOptions();
            options.Converters.Add(new StrictStringEnumConverter<HumanAgreementLimitation>());
            limitation = JsonSerializer.Deserialize<HumanAgreementLimitation>(JsonSerializer.Serialize(code), options);
            return true;
        }
        catch (JsonException) { limitation = default; return false; }
    }

    private static bool IsStateName(string name) => name == NoStateName ||
        Enum.GetValues<HumanAgreementApprovalState>().Any(state => ProposalDifferenceRepository.ApprovalStateName(state) == name);

    internal sealed record Built(IReadOnlyList<NewProposalDifferenceWord> Words, int UnboundOccurrences,
        int MeasuredWordsOutsideText);

    // Joins on the key AssessmentComparer.JoinKey gives a word, so a Trial and a comparison agree on one spelling.
    private static Dictionary<string, AssessedWord> OnePerForm(IReadOnlyList<AssessedWord> words, string side)
    {
        var byForm = new Dictionary<string, AssessedWord>(StringComparer.Ordinal);
        foreach (var group in words.GroupBy(word => word.Word.Normalize(NormalizationForm.FormD)))
        {
            var distinct = group.Select(word => JsonSerializer.Serialize(word.Morphology, Json))
                .Distinct(StringComparer.Ordinal).Count();
            if (distinct > 1)
                throw new DifferenceRefusalException(new Refusal("difference.duplicate-parse-rows", FailureReason.StoreInconsistent,
                    $"The {side} Assessment holds several different parses for '{group.Key}', so no one of them can be compared."));
            byForm[group.Key] = group.First();
        }
        return byForm;
    }

    internal sealed class DifferenceRefusalException(Refusal refusal) : Exception(refusal.Message)
    {
        public Refusal Refusal { get; } = refusal;
    }

    private static Built Build(SIL.Motif.Host.Texts.BaselineTextReadIndex text, IReadOnlyList<AssessedWord> beforeWords,
        IReadOnlyList<AssessedWord> afterWords, SourceValidatedDecisions? sourceValidated,
        IProgress<AssessmentProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        progress?.Report(new AssessmentProgress(AssessmentStage.SelectingWords, 0, null, "Reading the Text."));
        var before = OnePerForm(beforeWords, "before");
        var after = OnePerForm(afterWords, "after");
        var wordforms = text.Wordforms.ToDictionary(wordform => wordform.WordformId);
        var byForm = new Dictionary<string, List<(Guid Wordform, ProposalDifferenceOccurrence Where)>>(StringComparer.Ordinal);
        var unbound = 0;
        var inText = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in text.Lines)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var token in line.Tokens)
            {
                foreach (var form in token.Forms) inText.Add(Nfd(form.Text));
                if (token.Status is null || token.WordformId is not { } wordformId) continue;
                var measured = token.Forms.Select(form => Nfd(form.Text)).Where(form => form.Length > 0).Distinct(StringComparer.Ordinal)
                    .Where(form => before.ContainsKey(form) || after.ContainsKey(form)).ToArray();
                if (measured.Length != 1) { unbound++; continue; }
                if (!byForm.TryGetValue(measured[0], out var list)) byForm[measured[0]] = list = [];
                list.Add((wordformId, new ProposalDifferenceOccurrence(text.TextId.ToString("D"),
                    line.SegmentId.ToString("D"), token.OccurrenceIndex, line.Number)));
            }
        }

        var words = new List<NewProposalDifferenceWord>();
        var done = 0;
        // About a hundred reports however many words: each one is posted to the window.
        var every = Math.Max(1, byForm.Count / 100);
        foreach (var (form, tokens) in byForm.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (done++ % every == 0)
                progress?.Report(new AssessmentProgress(AssessmentStage.Finalizing, done - 1, byForm.Count,
                    "Comparing words."));
            var ids = tokens.Select(token => token.Wordform).Distinct().ToArray();
            if (ids.Length != 1) { unbound += tokens.Count; continue; }
            var wordform = wordforms[ids[0]];
            var decisions = wordform.Analyses.Select(analysis => new HumanAnalysisDecision(
                CanonicalId.FromGuid(analysis.AnalysisId).Value,
                GradeOf(sourceValidated is null ? analysis.Opinion : sourceValidated.OpinionFor(analysis.AnalysisId)),
                analysis.Identity)).ToArray();
            var incorrect = sourceValidated is null ? wordform.IncorrectSpelling
                : sourceValidated.IncorrectWordforms.Contains(wordform.WordformId);
            var compiled = HumanAgreementClassifier.Compile(decisions,
                incorrect ? CapturedSpellingStatus.Incorrect : CapturedSpellingStatus.Undecided);
            var facts = HumanAgreementDifferenceClassifier.Compare(before.GetValueOrDefault(form)?.Morphology,
                after.GetValueOrDefault(form)?.Morphology, compiled);
            if (sourceValidated is not null)
                facts = sourceValidated.MarkDisagreements(facts, wordform.Analyses.Select(analysis => analysis.AnalysisId)) with
                {
                    WordformId = CanonicalId.FromGuid(wordform.WordformId).Value,
                };
            words.Add(new NewProposalDifferenceWord(form, facts, tokens.Select(token => token.Where).ToArray()));
        }
        var outside = before.Keys.Concat(after.Keys).Distinct(StringComparer.Ordinal).Count(form => !inText.Contains(form));
        return new Built(words, unbound, outside);
    }

    internal static string GradeOf(string opinion) => opinion switch
    {
        "approved" => ReadingGrade.Approved,
        "disapproved" => ReadingGrade.Disapproved,
        "unknown" => ReadingGrade.Candidate,
        _ => throw new DifferenceRefusalException(new Refusal("difference.unexpected-opinion", FailureReason.StoreInconsistent,
            $"The captured decisions hold an opinion '{opinion}' that is not approved, disapproved or unknown. Capture the Baseline again.")),
    };

    private static string Nfd(string value) => value.Trim().Normalize(NormalizationForm.FormD);
}
