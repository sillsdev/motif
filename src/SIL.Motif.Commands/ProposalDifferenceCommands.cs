using System.Globalization;
using System.Text;
using System.Text.Json;
using SIL.Motif.Contract;
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

    /// <summary>Compares the Trial's before and after parses of one Text and saves the result.</summary>
    public static CommandOutcome<ProposalDifferenceResponse> Produce(ProposalDifferenceProduceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
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

            var assessments = new AssessmentRepository(database);
            var job = new JobRepository(database).Get(trial.JobId) ?? throw new InvalidDataException("Trial job not found.");
            var input = TrialJobInput.Parse(job.InputJson);
            if (!trial.AssessmentIds.TryGetValue(AssessmentKinds.ParseTime, out var afterId))
                return Refuse("difference.no-parse-assessment", FailureReason.Refused,
                    "The Trial recorded no ParseTime Assessment, so there are no parses to compare.");
            var after = assessments.Get(afterId);
            var before = (input.BaselineAssessmentIds ?? []).Select(assessments.Get).FirstOrDefault(record =>
                record.Kind == AssessmentKinds.ParseTime && record.Assessor == after.Assessor &&
                record.BaselineToken == after.BaselineToken);
            if (before is null || before.Words is null || after.Words is null)
                return Refuse("difference.no-before", FailureReason.Refused,
                    "The Trial has no matching Baseline ParseTime Assessment. Trial the Proposal again.");

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
            try { built = Build(text, before.Words, after.Words); }
            catch (DifferenceRefusalException refusal) { return CommandOutcome<ProposalDifferenceResponse>.Refused(refusal.Refusal); }
            var summary = Summarize(built, trial.JobId, request.ProposalId, before.AssessmentId, after.AssessmentId,
                text, JsonSerializer.Serialize(read.Baseline.Token, MotifJson.CreateOptions()));
            new ProposalDifferenceRepository(database).Publish(new NewProposalDifference(summary, built.Words));
            return Show(database, summary, new ProposalDifferenceFilter(), 0, 25);
        });
    }

    /// <summary>Reads a saved Difference's summary and one page of its matching words.</summary>
    public static CommandOutcome<ProposalDifferenceResponse> Show(ProposalDifferenceShowRequest request)
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
        foreach (var state in new[] { request.BeforeState, request.AfterState })
            if (state is not null && !IsStateName(state))
                return Refuse("difference.invalid-state", FailureReason.InvalidArgument,
                    "Choose a state such as approved-unique, approved-with-alternatives, no-approved-match or no-parse.");

        return ProjectStoreCommand.Run(request.ProjectPath, MotifProductVersion.CurrentText, (database, _) =>
        {
            var repository = new ProposalDifferenceRepository(database);
            var id = request.DifferenceId ?? (request.ProposalId is null ? null : repository.LatestFor(request.ProposalId));
            if (id is null)
                return Refuse("difference.none", FailureReason.NotFound,
                    "No saved Difference was found. Run: motif difference produce --project <fwdata> --proposal <id> --text <guid>");
            var summary = repository.GetSummary(id);
            if (summary is null)
                return Refuse("difference.none", FailureReason.NotFound, "There is no Difference with that id.");
            return Show(database, summary, new ProposalDifferenceFilter(request.CouldBeBad, bucket,
                request.BeforeState, request.AfterState), request.Offset, request.Limit);
        });
    }

    private static CommandOutcome<ProposalDifferenceResponse> Show(MotifDatabase database,
        ProposalDifferenceSummary summary, ProposalDifferenceFilter filter, int offset, int limit)
    {
        var (matching, words) = new ProposalDifferenceRepository(database).ReadWords(summary.DifferenceId, filter, offset, limit);
        var next = offset + words.Count < matching ? offset + words.Count : (int?)null;
        return CommandOutcome<ProposalDifferenceResponse>.Success(
            new ProposalDifferenceResponse(summary, words, matching, next));
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

    private static bool IsStateName(string name) => name == NoStateName ||
        Enum.GetValues<HumanAgreementApprovalState>().Any(state => ProposalDifferenceRepository.ApprovalStateName(state) == name);

    private sealed record Built(IReadOnlyList<NewProposalDifferenceWord> Words, int UnboundOccurrences);

    private static Dictionary<string, AssessedWord> OnePerForm(IReadOnlyList<AssessedWord> words, string side)
    {
        var byForm = new Dictionary<string, AssessedWord>(StringComparer.Ordinal);
        foreach (var group in words.GroupBy(word => Nfd(word.Word)))
        {
            var distinct = group.Select(word => JsonSerializer.Serialize(word.Morphology, MotifJson.CreateOptions()))
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
        IReadOnlyList<AssessedWord> afterWords)
    {
        var before = OnePerForm(beforeWords, "before");
        var after = OnePerForm(afterWords, "after");
        var wordforms = text.Wordforms.ToDictionary(wordform => wordform.WordformId);
        var byForm = new Dictionary<string, List<(Guid Wordform, ProposalDifferenceOccurrence Where)>>(StringComparer.Ordinal);
        var unbound = 0;
        foreach (var line in text.Lines)
            foreach (var token in line.Tokens)
            {
                if (token.Status is null || token.WordformId is not { } wordformId) continue;
                var measured = token.Forms.Select(form => Nfd(form.Text)).Where(form => form.Length > 0).Distinct(StringComparer.Ordinal)
                    .Where(form => before.ContainsKey(form) || after.ContainsKey(form)).ToArray();
                if (measured.Length != 1) { unbound++; continue; }
                if (!byForm.TryGetValue(measured[0], out var list)) byForm[measured[0]] = list = [];
                list.Add((wordformId, new ProposalDifferenceOccurrence(text.TextId.ToString("D"),
                    line.SegmentId.ToString("D"), token.OccurrenceIndex, line.Number)));
            }

        var words = new List<NewProposalDifferenceWord>();
        foreach (var (form, tokens) in byForm.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var ids = tokens.Select(token => token.Wordform).Distinct().ToArray();
            if (ids.Length != 1) { unbound += tokens.Count; continue; }
            var wordform = wordforms[ids[0]];
            var decisions = wordform.Analyses.Select(analysis => new HumanAnalysisDecision(
                CanonicalId.FromGuid(analysis.AnalysisId).Value, GradeOf(analysis.Opinion), analysis.Identity)).ToArray();
            var compiled = HumanAgreementClassifier.Compile(decisions, wordform.IncorrectSpelling
                ? CapturedSpellingStatus.Incorrect : CapturedSpellingStatus.Undecided);
            var facts = HumanAgreementDifferenceClassifier.Compare(before.GetValueOrDefault(form)?.Morphology,
                after.GetValueOrDefault(form)?.Morphology, compiled);
            words.Add(new NewProposalDifferenceWord(form, facts, tokens.Select(token => token.Where).ToArray()));
        }
        return new Built(words, unbound);
    }

    private static ProposalDifferenceSummary Summarize(Built built, string jobId, string proposalId, string beforeId,
        string afterId, SIL.Motif.Host.Texts.BaselineTextReadIndex text, string baselineToken)
    {
        var buckets = built.Words.SelectMany(word => word.Facts.Buckets.Select(item => item.Bucket).Distinct()
                .Select(bucket => (bucket, word)))
            .GroupBy(pair => pair.bucket).OrderBy(group => (int)group.Key)
            .Select(group => new ProposalDifferenceBucketCount(group.Key, group.Count(),
                group.Sum(pair => pair.word.Occurrences.Count))).ToArray();
        var matrix = built.Words.GroupBy(word => (Before: ProposalDifferenceRepository.StateName(word.Facts.Before),
                After: ProposalDifferenceRepository.StateName(word.Facts.After)))
            .OrderBy(group => group.Key.Before, StringComparer.Ordinal).ThenBy(group => group.Key.After, StringComparer.Ordinal)
            .Select(group => new ProposalDifferenceMatrixCell(group.Key.Before, group.Key.After, group.Count(),
                group.Sum(word => word.Occurrences.Count))).ToArray();
        return new ProposalDifferenceSummary(CanonicalId.Mint("difference/").Value, proposalId, jobId, beforeId, afterId,
            text.TextId.ToString("D"), text.Title, baselineToken, new ProposalDifferencePolicy(), built.Words.Count,
            built.Words.Sum(word => word.Occurrences.Count), built.UnboundOccurrences,
            built.Words.Count(word => word.Facts.After.ApprovedExpectedMorphologyCount > 0),
            built.Words.Count(word => word.Facts.CouldBeBad), built.Words.Count(word => word.Facts.Improved), buckets,
            matrix, DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
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
