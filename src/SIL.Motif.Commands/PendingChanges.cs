using System.Text;
using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Commands.Store;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.Analysis;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Parser;
using SIL.Motif.Host.Texts;
using SIL.Motif.Host.Store;
using SIL.Motif.LiveHost.Baselines;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Commands;

/// <summary>Owns the one persistent Draft shared by every page and the CLI.</summary>
public static class PendingChanges
{
    public const string DraftName = "pending-changes";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public static CommandOutcome<PendingChangesSnapshot> Load(PendingChangesRequest request) =>
        ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion, (database, project) =>
        {
            var repository = new ProposalRepository(database);
            return CommandOutcome<PendingChangesSnapshot>.Success(
                Snapshot(database, project, repository));
        });

    public static CommandOutcome<PendingChangesSnapshot> Put(PutPendingChangeRequest request) =>
        ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion, (database, project) =>
        {
            var repository = new ProposalRepository(database);
            var current = Current(repository);
            if (Revision(current?.ProposalJson) != request.ExpectedRevision)
                return Refuse("change.revision-conflict", "The pending Draft changed. Reload it and try again.",
                    ("changeId", request.Change.ChangeId), ("expectedRevision", request.ExpectedRevision));
            var change = request.Change;
            if (string.IsNullOrWhiteSpace(change.ChangeId) || string.IsNullOrWhiteSpace(change.Word))
                return Refuse("change.invalid-identity", "A change id and word are required.",
                    ("changeId", change.ChangeId), ("wordformId", change.WordformId));
            if (change.Occurrence is not null && change.Kind is not
                (AnalysisChangeKinds.Approve or AnalysisChangeKinds.Reject or AnalysisChangeKinds.Candidate))
                return Refuse("change.occurrence-unavailable",
                    "Only an analysis opinion change can carry an occurrence anchor.",
                    ("changeId", change.ChangeId));
            if (change.Kind is AnalysisChangeKinds.Approve or AnalysisChangeKinds.Reject or AnalysisChangeKinds.Candidate &&
                change.StoredAnalysisId is null && change.ReadingIndex is null)
                return Refuse("change.analysis-identity-required",
                    "Choose one analysis explicitly before changing its opinion.", ("changeId", change.ChangeId));
            if (change.Kind == AnalysisChangeKinds.RemoveAnalysis && change.StoredAnalysisId is null)
                return Refuse("change.analysis-identity-required",
                    "Choose one stored analysis to remove.", ("changeId", change.ChangeId));
            if ((change.Kind == AnalysisChangeKinds.AddCandidate ||
                 change.StoredAnalysisId is null && change.Kind is
                     AnalysisChangeKinds.Approve or AnalysisChangeKinds.Reject or AnalysisChangeKinds.Candidate) &&
                change.AssessmentId is null)
                return Refuse("change.assessment-required",
                    "Name the Assessment that produced the chosen parser reading.",
                    ("changeId", change.ChangeId));
            var baseline = new BaselineRepository(database).GetCurrent(ProjectWorkspaceKey.Compute(project));
            if (baseline is null)
                return Refuse("change.baseline-missing", "Capture a Baseline before collecting changes.",
                    ("changeId", change.ChangeId));

            var draft = LoadDraft(current);
            var saved = ComposeAndSave(database, repository, current, draft, change, baseline, project,
                request.ExpectedRevision);
            if (!saved.Succeeded)
                return CommandOutcome<PendingChangesSnapshot>.Refused(saved.Refusal!);
            var details = saved.Value!;
            return CommandOutcome<PendingChangesSnapshot>.Success(
                Snapshot(database, project, repository) with
                {
                    SkippedWord = details.SkippedWord,
                    ReplacedChangeId = details.ReplacedChangeId,
                    CancelledChangeId = details.CancelledChangeId,
                });
        });

    public static CommandOutcome<PendingChangesSnapshot> RemoveAnalysis(RemoveAnalysisRequest request)
    {
        if (request.AnalysisId is { } analysisId && request.TextId is null && request.AnalysisIds is null)
        {
            if (request.ChangeId is null || request.WordformId is null || request.Word is null)
                return Refuse("change.analysis-identity-required", "A change id, wordform, word, and analysis are required.");
            return Put(new PutPendingChangeRequest(request.FwDataPath, request.ProductVersion, request.ExpectedRevision,
                new ChangeIntent(request.ChangeId, AnalysisChangeKinds.RemoveAnalysis, request.WordformId,
                    request.Word, StoredAnalysisId: analysisId)));
        }

        return ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion, (database, project) =>
        {
            var repository = new ProposalRepository(database);
            var current = Current(repository);
            if (Revision(current?.ProposalJson) != request.ExpectedRevision)
                return Refuse("change.revision-conflict", "The pending Draft changed. Reload it and try again.",
                    ("expectedRevision", request.ExpectedRevision));
            if ((request.TextId is null ? 0 : 1) + (request.AnalysisIds is null ? 0 : 1) != 1)
                return Refuse("change.scope-invalid", "Choose a list of analyses or one Text to remove analyses from.");
            var baseline = new BaselineRepository(database).GetCurrent(ProjectWorkspaceKey.Compute(project));
            if (baseline is null)
                return Refuse("change.baseline-missing", "Capture a Baseline before collecting analysis removals.");
            using var cache = LoadBaselineCache(baseline.FwDataPath);
            var targets = new List<IWfiAnalysis>();
            if (request.TextId is { } textId)
            {
                var textWords = new BaselineRepository(database).GetCurrentTextWords(
                    ProjectWorkspaceKey.Compute(project), [textId]);
                if (textWords is null || textWords.Baseline.Token != baseline.Token ||
                    textWords.Projection.Texts.All(text => text.TextId != textId))
                    return Refuse("change.text-unavailable", "The selected Text is not in the current Baseline.",
                        ("textId", textId.ToString("D")));
                var text = cache.ServiceLocator.GetInstance<ITextRepository>().AllInstances()
                    .SingleOrDefault(item => item.Guid == textId);
                if (text is null)
                    return Refuse("change.text-unavailable", "The selected Text is no longer in the project.",
                        ("textId", textId.ToString("D")));
                foreach (var segment in text.ContentsOA?.ParagraphsOS.OfType<IStTxtPara>()
                             .SelectMany(paragraph => paragraph.SegmentsOS) ?? [])
                foreach (var item in segment.AnalysesRS)
                {
                    var analysis = item switch
                    {
                        IWfiAnalysis direct => direct,
                        IWfiGloss gloss => gloss.Owner as IWfiAnalysis,
                        _ => null,
                    };
                    if (analysis is not null) targets.Add(analysis);
                }
            }
            else
            {
                foreach (var id in request.AnalysisIds!.Distinct(StringComparer.Ordinal))
                {
                    if (!CanonicalId.TryParse(id, out var canonical) ||
                        !cache.ServiceLocator.ObjectRepository.TryGetObject(canonical.ToGuid(), out var item) ||
                        item is not IWfiAnalysis analysis)
                        return Refuse("change.stored-analysis-missing", "A selected analysis is no longer in the project.",
                            ("analysisId", id));
                    targets.Add(analysis);
                }
            }
            targets = targets.DistinctBy(item => item.Guid).ToList();
            if (targets.Count == 0)
                return Refuse("change.no-effect", "The selected Text contains no stored analyses to remove.");

            var draft = LoadDraft(current);
            var existingOperations = draft.Operations.Count == 0 ? Array.Empty<OperationEnvelope>() :
                ProposalJsonParser.Parse(ProposalCommands.BuildProposalJson(draft)).Operations;
            var additions = new List<(OperationEnvelope Operation, ChangeFitFingerprint Fingerprint,
                string ChangeId, IWfiAnalysis Analysis, IWfiWordform Wordform, string Form, ParseAnalysis Reading)>();
            var token = JsonSerializer.Serialize(baseline.Token, JsonOptions);
            foreach (var analysis in targets)
            {
                if (analysis.Owner is not IWfiWordform wordform)
                    return Refuse("change.analysis-owner-invalid", "A selected analysis is not owned by a wordform.",
                        ("analysisId", CanonicalId.FromGuid(analysis.Guid).Value));
                var wordformId = CanonicalId.FromGuid(wordform.Guid);
                var analysisId = CanonicalId.FromGuid(analysis.Guid);
                var form = wordform.Form.VernacularDefaultWritingSystem?.Text ?? "";
                var changeId = CanonicalId.Mint().Value;
                var reading = new ParseAnalysis(analysis.MorphBundlesOS.Select(bundle => new ParseMorph(
                    bundle.MorphRA?.Guid.ToString("D"), bundle.MsaRA?.Guid.ToString("D"),
                    bundle.InflTypeRA?.Guid.ToString("D"),
                    bundle.MorphRA is null ? bundle.Form.VernacularDefaultWritingSystem?.Text : null)).ToArray());
                IReadOnlyList<OperationEnvelope> operations;
                try
                {
                    operations = AnalysisChangeComposer.Build(cache, new AnalysisChangeIntent(
                        AnalysisChangeKinds.RemoveAnalysis, wordformId, null, analysisId, changeId));
                }
                catch (Exception exception) when (exception is InvalidOperationException or FormatException)
                {
                    return Refuse("change.cannot-compose", exception.Message,
                        ("analysisId", analysisId.Value));
                }
                var fingerprint = new ChangeFitFingerprint(wordformId.Value, analysisId.Value,
                    form.Normalize(NormalizationForm.FormD), token, ChangeFitPreflight.ContentDigest(analysis),
                    ChangeFitPreflight.ReadingDigest(reading), reading,
                    analysis.GetAgentOpinion(cache.LangProject.DefaultUserAgent).ToString());
                additions.Add((operations[0], fingerprint, changeId, analysis, wordform, form, reading));
            }
            if (AnalysisOpinionSlotValidator.FindConflict(existingOperations.Concat(additions.Select(item => item.Operation)))
                is { } collision)
                return Refuse("change.slot-occupied", "Another change already addresses one of these analyses.",
                    ("operationId", collision.Existing.OperationId.Value));

            foreach (var addition in additions)
            {
                draft.Operations.Add(ToDraft(addition.Operation, addition.Fingerprint, addition.ChangeId));
                draft.ContractVersions[OperationKind.GetGroup(addition.Operation.Kind)] = "1.0";
                var intent = new ChangeIntent(addition.ChangeId, AnalysisChangeKinds.RemoveAnalysis,
                    CanonicalId.FromGuid(addition.Wordform.Guid).Value, addition.Form,
                    StoredAnalysisId: CanonicalId.FromGuid(addition.Analysis.Guid).Value);
                AddComposerProvenance(draft, new
                {
                    composer = "AnalysisChange", changeId = addition.ChangeId,
                    kind = AnalysisChangeKinds.RemoveAnalysis, wordformId = intent.WordformId,
                    word = addition.Form, storedAnalysisId = intent.StoredAnalysisId,
                    displayAnalyses = DisplayAnalyses(database, cache,
                        Path.GetFileNameWithoutExtension(baseline.FwDataPath), addition.Wordform,
                        intent, null),
                    operationIds = new[] { addition.Operation.OperationId.Value },
                });
            }
            var saved = SaveDraft(repository, current, draft);
            if (!saved)
                return Refuse("change.revision-conflict", "The pending Draft changed. Reload it and try again.",
                    ("expectedRevision", request.ExpectedRevision));
            return CommandOutcome<PendingChangesSnapshot>.Success(Snapshot(database, project, repository));
        });
    }

    public static CommandOutcome<PendingChangesSnapshot> AcceptNewSet(AcceptNewSetRequest request) =>
        ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion, (database, project) =>
        {
            var repository = new ProposalRepository(database);
            var current = Current(repository);
            if (Revision(current?.ProposalJson) != request.ExpectedRevision)
                return Refuse("change.revision-conflict", "The pending Draft changed. Reload it and try again.",
                    ("assessmentId", request.AssessmentId), ("expectedRevision", request.ExpectedRevision));
            if ((request.WordformId is not null ? 1 : 0) + (request.TextId is not null ? 1 : 0) +
                (request.Selection ? 1 : 0) != 1)
                return Refuse("change.scope-invalid", "Choose one wordform, one Selection, or one Text.",
                    ("assessmentId", request.AssessmentId));

            var baseline = new BaselineRepository(database).GetCurrent(ProjectWorkspaceKey.Compute(project));
            if (baseline is null)
                return Refuse("change.baseline-missing", "Capture a Baseline before accepting parser readings.",
                    ("assessmentId", request.AssessmentId));
            AssessmentRecord assessment;
            try { assessment = new AssessmentRepository(database).Get(request.AssessmentId); }
            catch (KeyNotFoundException)
            {
                return Refuse("change.assessment-missing", "The Assessment is unavailable.",
                    ("assessmentId", request.AssessmentId));
            }
            if (assessment.Kind != AssessmentKind.ParseTime.ToStoredKind())
                return Refuse("change.assessment-kind",
                    "Accept the new set requires an Assessment that parses every word. Run a complete Assessment first.",
                    ("assessmentId", request.AssessmentId));
            BaselineToken? assessmentBaseline;
            try
            {
                assessmentBaseline = JsonSerializer.Deserialize<BaselineToken>(assessment.BaselineToken, JsonOptions);
            }
            catch (Exception exception) when (exception is JsonException or ArgumentException)
            {
                assessmentBaseline = null;
            }
            if (assessmentBaseline != baseline.Token)
                return Refuse("change.assessment-stale", "The Assessment belongs to an older Baseline.",
                    ("assessmentId", request.AssessmentId));

            using var cache = LoadBaselineCache(baseline.FwDataPath);
            var wordforms = cache.ServiceLocator.GetInstance<IWfiWordformRepository>();
            var selected = new List<IWfiWordform>();
            if (request.WordformId is { } wordformId)
            {
                if (!CanonicalId.TryParse(wordformId, out var canonical) ||
                    !cache.ServiceLocator.ObjectRepository.TryGetObject(canonical.ToGuid(), out var item) ||
                    item is not IWfiWordform wordform)
                    return Refuse("change.wordform-missing", "The selected wordform is no longer in the project.",
                        ("wordformId", wordformId));
                selected.Add(wordform);
            }
            else if (request.TextId is { } textId)
            {
                var textWords = new BaselineRepository(database).GetCurrentTextWords(
                    ProjectWorkspaceKey.Compute(project), [textId]);
                if (textWords is null || textWords.Baseline.Token != baseline.Token ||
                    textWords.Projection.Texts.All(text => text.TextId != textId))
                    return Refuse("change.text-unavailable", "The selected Text is not in the current Baseline.",
                        ("textId", textId.ToString("D")));
                foreach (var id in textWords.Projection.Texts.SelectMany(text => text.Lines)
                             .SelectMany(line => line.Tokens).Select(token => token.WordformId)
                             .OfType<Guid>().Distinct())
                    selected.Add(wordforms.GetObject(id));
            }
            else
            {
                foreach (var form in assessment.Selection.Words)
                {
                    var matches = wordforms.AllInstances().Where(item =>
                        (item.Form.VernacularDefaultWritingSystem?.Text ?? "")
                            .Normalize(NormalizationForm.FormD) == form.Normalize(NormalizationForm.FormD))
                        .Take(2).ToArray();
                    if (matches.Length != 1)
                        return Refuse(matches.Length == 0 ? "change.wordform-missing" : "change.wordform-ambiguous",
                            matches.Length == 0 ? "A selected word isn't in the FieldWorks project."
                                : "More than one wordform matches a word in the Selection.",
                            ("word", form), ("assessmentId", request.AssessmentId));
                    selected.Add(matches[0]);
                }
            }

            var assessmentWords = assessment.Words ?? [];
            var prepared = new List<(IWfiWordform Wordform, string Form, IReadOnlyList<ParseAnalysis> Readings)>();
            foreach (var wordform in selected.DistinctBy(item => item.Guid))
            {
                var form = wordform.Form.VernacularDefaultWritingSystem?.Text ?? "";
                var matching = assessmentWords.Where(item => item.Word.Normalize(NormalizationForm.FormD) ==
                    form.Normalize(NormalizationForm.FormD)).Take(2).ToArray();
                if (matching.Length != 1)
                    return Refuse("change.assessment-word-missing",
                        "The Assessment does not contain exactly one result for a selected word.",
                        ("word", form), ("assessmentId", request.AssessmentId));
                var word = matching[0];
                if (!word.Outcome.TryParseStoredOutcome(out var outcome) || word.IsIncomplete ||
                    outcome is WordOutcome.Capped or WordOutcome.TimedOut or WordOutcome.Skipped ||
                    word.Morphology is not { } morphology || morphology.Capped || morphology.TimedOut ||
                    morphology.InvalidShape || morphology.Unavailable.Count > 0)
                    return Refuse("change.assessment-incomplete",
                        $"The Assessment did not finish parsing '{form}'. Run a complete Assessment before accepting its new set.",
                        ("word", form), ("assessmentId", request.AssessmentId));
                prepared.Add((wordform, form, morphology.Analyses));
            }

            var draft = LoadDraft(current);
            var existing = draft.Operations.Count == 0 ? Array.Empty<OperationEnvelope>() :
                ProposalJsonParser.Parse(ProposalCommands.BuildProposalJson(draft)).Operations;
            var additions = new List<(OperationEnvelope Operation, ChangeFitFingerprint Fingerprint,
                string ChangeId, IWfiWordform Wordform, string Form, ParseAnalysis Reading)>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (wordform, form, readings) in prepared)
            foreach (var reading in readings)
            {
                var wordformKey = CanonicalId.FromGuid(wordform.Guid).Value;
                var readingDigest = ChangeFitPreflight.ReadingDigest(reading);
                if (!seen.Add(wordformKey + ":" + readingDigest) ||
                    wordform.AnalysesOC.Any(analysis => AnalysisChangeComposer.Matches(analysis, reading)) ||
                    draft.Operations.Any(operation => operation.Extensions is { ValueKind: JsonValueKind.Object } extensions &&
                        extensions.TryGetProperty("changeFit", out var fit) && fit.ValueKind == JsonValueKind.Object &&
                        Property(fit, "wordformId") == wordformKey &&
                        Property(fit, "readingContentDigest") == readingDigest))
                    continue;
                var changeId = CanonicalId.Mint().Value;
                IReadOnlyList<OperationEnvelope> operations;
                try
                {
                    operations = AnalysisChangeComposer.Build(cache, new AnalysisChangeIntent(
                        AnalysisChangeKinds.AddCandidate, CanonicalId.Parse(wordformKey), reading,
                        ChangeId: changeId));
                }
                catch (Exception exception) when (exception is InvalidOperationException or FormatException)
                {
                    return Refuse("change.cannot-compose", exception.Message,
                        ("word", form), ("assessmentId", request.AssessmentId));
                }
                var token = JsonSerializer.Serialize(baseline.Token, JsonOptions);
                foreach (var operation in operations)
                    additions.Add((operation, new ChangeFitFingerprint(wordformKey, null,
                        form.Normalize(NormalizationForm.FormD), token, ReadingContentDigest: readingDigest,
                        Reading: reading), changeId, wordform, form, reading));
            }
            if (additions.Count == 0)
                return CommandOutcome<PendingChangesSnapshot>.Success(Snapshot(database, project, repository));
            if (AnalysisOpinionSlotValidator.FindConflict(existing.Concat(additions.Select(item => item.Operation)))
                is { } collision)
                return Refuse("change.slot-occupied", "Another change already addresses one of these readings.",
                    ("assessmentId", request.AssessmentId), ("operationId", collision.Existing.OperationId.Value));

            var groupId = CanonicalId.Mint().Value;
            foreach (var addition in additions)
            {
                draft.Operations.Add(ToDraft(addition.Operation, addition.Fingerprint, addition.ChangeId));
                draft.ContractVersions[OperationKind.GetGroup(addition.Operation.Kind)] = "1.0";
            }
            foreach (var group in additions.GroupBy(item => item.ChangeId, StringComparer.Ordinal))
            {
                var addition = group.First();
                var intent = new ChangeIntent(addition.ChangeId, AnalysisChangeKinds.AddCandidate,
                    CanonicalId.FromGuid(addition.Wordform.Guid).Value, addition.Form,
                    AssessmentId: request.AssessmentId, Reading: addition.Reading);
                AddComposerProvenance(draft, new
                {
                    composer = "AnalysisChange", changeId = addition.ChangeId, groupId,
                    kind = AnalysisChangeKinds.AddCandidate, wordformId = intent.WordformId,
                    word = addition.Form, assessmentId = request.AssessmentId,
                    displayAnalyses = DisplayAnalyses(database, cache,
                        Path.GetFileNameWithoutExtension(baseline.FwDataPath), addition.Wordform,
                        intent, addition.Reading),
                    operationIds = group.Select(item => item.Operation.OperationId.Value).ToArray(),
                });
            }
            var saved = SaveDraft(repository, current, draft);
            if (!saved)
                return Refuse("change.revision-conflict", "The pending Draft changed. Reload it and try again.",
                    ("assessmentId", request.AssessmentId), ("expectedRevision", request.ExpectedRevision));
            return CommandOutcome<PendingChangesSnapshot>.Success(Snapshot(database, project, repository));
        });

    private static CommandOutcome<PutDetails> ComposeAndSave(MotifDatabase database,
        ProposalRepository repository, ProposalRecord? current, DraftDocument draft,
        ChangeIntent change, BaselineRecord baseline, ProjectLocator project, string expectedRevision)
    {
        using var cache = LoadBaselineCache(baseline.FwDataPath);
        IWfiWordform wordform;
        try
        {
            var words = cache.ServiceLocator.GetInstance<IWfiWordformRepository>();
            if (string.IsNullOrWhiteSpace(change.WordformId))
            {
                var matches = words.AllInstances().Where(item =>
                    (item.Form.VernacularDefaultWritingSystem?.Text ?? "")
                        .Normalize(NormalizationForm.FormD) == change.Word.Normalize(NormalizationForm.FormD))
                    .Take(2).ToArray();
                if (matches.Length == 0)
                    return RefusePut("change.wordform-missing", "The selected wordform is no longer in the project.",
                        ("changeId", change.ChangeId), ("word", change.Word));
                if (matches.Length > 1)
                    return RefusePut("change.wordform-ambiguous", "More than one wordform has this form.",
                        ("changeId", change.ChangeId), ("word", change.Word));
                wordform = matches[0];
            }
            else wordform = words.GetObject(CanonicalId.Parse(change.WordformId).ToGuid());
        }
        catch (Exception exception) when (exception is FormatException or KeyNotFoundException or InvalidOperationException)
        {
            return RefusePut("change.wordform-missing", "The selected wordform is no longer in the project.",
                ("changeId", change.ChangeId), ("wordformId", change.WordformId));
        }
        change = change with { WordformId = CanonicalId.FromGuid(wordform.Guid).Value };
        var form = wordform.Form.VernacularDefaultWritingSystem?.Text ?? "";
        if (form.Normalize(NormalizationForm.FormD) != change.Word.Normalize(NormalizationForm.FormD))
            return RefusePut("change.wordform-changed", "The selected wordform changed its form.",
                ("changeId", change.ChangeId), ("wordformId", change.WordformId));

        OccurrenceFitEvidence? occurrenceEvidence = null;
        if (change.Occurrence is { } anchor)
        {
            var textWords = new BaselineRepository(database).GetCurrentTextWords(
                ProjectWorkspaceKey.Compute(project), [anchor.TextId]);
            if (textWords is null)
                return RefusePut("change.occurrence-unavailable",
                    "The selected Text is not in the current Baseline.",
                    ("changeId", change.ChangeId), ("wordformId", change.WordformId));
            if (textWords.Baseline.Token != baseline.Token)
                return RefusePut("change.refresh-required", "Refresh the project before collecting changes.",
                    ("changeId", change.ChangeId), ("wordformId", change.WordformId));
            if (!OccurrenceFitEvidenceResolver.TryCapture(textWords.Projection, anchor,
                    out occurrenceEvidence, out var reason))
                return RefusePut("change.occurrence-unavailable", reason,
                    ("changeId", change.ChangeId), ("wordformId", change.WordformId));
            if (!occurrenceEvidence!.ParseIsCurrent)
                return RefusePut("change.occurrence-unavailable", "The paragraph parse is not current.",
                    ("changeId", change.ChangeId), ("wordformId", change.WordformId));
            if (occurrenceEvidence.WordformId != change.WordformId)
                return RefusePut("change.occurrence-wordform-mismatch",
                    "The selected occurrence belongs to a different wordform.",
                    ("changeId", change.ChangeId), ("wordformId", change.WordformId));
        }

        if (change.Kind == AnalysisChangeKinds.AddCandidate &&
            draft.ComposerProvenance.Any(entry => Property(entry, "wordformId") == change.WordformId &&
                Property(entry, "kind") is AnalysisChangeKinds.Approve or AnalysisChangeKinds.Reject or
                    AnalysisChangeKinds.Candidate))
        {
            return CommandOutcome<PutDetails>.Success(new PutDetails(change.Word, null, null));
        }

        var reading = change.Reading;
        if (reading is null && change.StoredAnalysisId is { } storedId)
        {
            IWfiAnalysis? stored;
            try
            {
                var guid = CanonicalId.Parse(storedId).ToGuid();
                stored = wordform.AnalysesOC.SingleOrDefault(item => item.Guid == guid);
            }
            catch (Exception exception) when (exception is FormatException or InvalidOperationException)
            {
                stored = null;
            }
            if (stored is null)
                return RefusePut("change.stored-analysis-missing", "The chosen analysis is no longer under this wordform.",
                    ("changeId", change.ChangeId), ("wordformId", change.WordformId),
                    ("storedAnalysisId", storedId));
            reading = new ParseAnalysis(stored.MorphBundlesOS.Select(bundle => new ParseMorph(
                bundle.MorphRA?.Guid.ToString("D"), bundle.MsaRA?.Guid.ToString("D"),
                bundle.InflTypeRA?.Guid.ToString("D"),
                bundle.MorphRA is null ? bundle.Form.VernacularDefaultWritingSystem?.Text : null)).ToArray());
        }
        if (change.AssessmentId is { } assessmentId)
        {
            try
            {
                var assessment = new AssessmentRepository(database).Get(assessmentId);
                BaselineToken? assessmentBaseline;
                try
                {
                    assessmentBaseline = JsonSerializer.Deserialize<BaselineToken>(
                        assessment.BaselineToken, JsonOptions);
                }
                catch (Exception exception) when (exception is JsonException or ArgumentException)
                {
                    assessmentBaseline = null;
                }
                if (assessmentBaseline != baseline.Token)
                    return RefusePut("change.assessment-stale", "The Assessment belongs to an older Baseline.",
                        ("changeId", change.ChangeId), ("wordformId", change.WordformId),
                        ("assessmentId", assessmentId));
                var word = assessment.Words?.SingleOrDefault(item =>
                    item.Word.Normalize(NormalizationForm.FormD) == form.Normalize(NormalizationForm.FormD));
                if (change.StoredAnalysisId is null && CarriesReading(change.Kind) &&
                    (reading is null || (change.ReadingIndex is { } index
                        ? index < 0 || index >= (word?.Morphology?.Analyses.Count ?? 0) ||
                          ChangeFitPreflight.ReadingDigest(word!.Morphology!.Analyses[index]) !=
                          ChangeFitPreflight.ReadingDigest(reading)
                        : word?.Morphology?.Analyses.Any(item =>
                            ChangeFitPreflight.ReadingDigest(item) == ChangeFitPreflight.ReadingDigest(reading)) != true)))
                    return RefusePut("change.reading-missing", "The chosen reading is absent from the Assessment.",
                        ("changeId", change.ChangeId), ("wordformId", change.WordformId),
                        ("assessmentId", assessmentId));
            }
            catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException)
            {
                return RefusePut("change.assessment-missing", "The Assessment is unavailable.",
                    ("changeId", change.ChangeId), ("assessmentId", assessmentId));
            }
        }
        if (CarriesReading(change.Kind) && reading is null)
            return RefusePut("change.reading-missing", "Choose an exact reading for this change.",
                ("changeId", change.ChangeId), ("wordformId", change.WordformId));

        var occupied = draft.Operations.Select(operation =>
        {
            if (operation.Extensions is not { ValueKind: JsonValueKind.Object } extensions ||
                !extensions.TryGetProperty("changeFit", out var fit) ||
                fit.ValueKind != JsonValueKind.Object) return (ChangeId: (string?)null, Fits: false);
            return (ChangeId: ChangeIdOf(operation), Fits:
                Property(fit, "wordformId") == change.WordformId &&
                (change.Kind == AnalysisChangeKinds.RemoveAnalysis
                    ? Property(fit, "analysisId") == change.StoredAnalysisId
                    : reading is not null && Property(fit, "readingContentDigest") ==
                      ChangeFitPreflight.ReadingDigest(reading)));
        }).FirstOrDefault(item => item.Fits);
        if (occupied.Fits && occupied.ChangeId is null)
            return RefusePut("change.slot-occupied", "An unmapped change addresses this word and reading.",
                ("changeId", change.ChangeId), ("wordformId", change.WordformId));
        var priorKind = draft.ComposerProvenance.Where(entry => ChangeIdOf(entry) == occupied.ChangeId)
            .Select(entry => Property(entry, "kind")).LastOrDefault();
        var cancelUnstoredChoice = change.Kind == AnalysisChangeKinds.Candidate && occupied.Fits &&
            priorKind is AnalysisChangeKinds.Approve or AnalysisChangeKinds.Reject or
                AnalysisChangeKinds.Candidate or AnalysisChangeKinds.AddCandidate &&
            !wordform.AnalysesOC.Any(analysis => AnalysisChangeComposer.Matches(analysis, reading!));
        IReadOnlyList<OperationEnvelope> operations;
        try
        {
            operations = cancelUnstoredChoice ? [] : AnalysisChangeComposer.Build(cache,
                new AnalysisChangeIntent(change.Kind, CanonicalId.Parse(change.WordformId), reading,
                    change.StoredAnalysisId is null ? null : CanonicalId.Parse(change.StoredAnalysisId),
                    change.ChangeId));
        }
        catch (Exception exception) when (exception is InvalidOperationException or FormatException)
        {
            return RefusePut("change.cannot-compose", exception.Message,
                ("changeId", change.ChangeId), ("wordformId", change.WordformId));
        }
        var removed = RemoveChange(draft, change.ChangeId);
        if (occupied.ChangeId is { } occupiedId && occupiedId != change.ChangeId)
            removed += RemoveChange(draft, occupiedId);
        foreach (var group in draft.ContractVersions.Keys.ToArray())
            if (!draft.Operations.Any(operation => OperationKind.GetGroup(operation.Kind) == group))
                draft.ContractVersions.Remove(group);
        if (operations.Count == 0 && removed == 0)
            return RefusePut("change.no-effect", "The chosen analysis already has that state.",
                ("changeId", change.ChangeId));
        var existingOperations = draft.Operations.Count == 0
            ? Array.Empty<OperationEnvelope>()
            : ProposalJsonParser.Parse(ProposalCommands.BuildProposalJson(draft)).Operations;
        if (AnalysisOpinionSlotValidator.FindConflict(existingOperations.Concat(operations)) is { } collision)
        {
            var existingChangeId = collision.Existing.Extensions is { ValueKind: JsonValueKind.Object } extension &&
                extension.TryGetProperty("changeId", out var id) ? id.GetString() : null;
            return RefusePut("change.slot-occupied", "Another change already addresses that project slot.",
                ("changeId", change.ChangeId), ("existingChangeId", existingChangeId),
                ("wordformId", change.WordformId));
        }
        var token = JsonSerializer.Serialize(baseline.Token, JsonOptions);
        foreach (var operation in operations)
        {
            var analysis = operation.Target is { } target
                ? wordform.AnalysesOC.FirstOrDefault(item => item.Guid == target.ToGuid()) : null;
            var fingerprint = new ChangeFitFingerprint(change.WordformId,
                analysis is null ? null : CanonicalId.FromGuid(analysis.Guid).Value,
                form.Normalize(NormalizationForm.FormD), token,
                analysis is null ? null : ChangeFitPreflight.ContentDigest(analysis),
                reading is null ? null : ChangeFitPreflight.ReadingDigest(reading), reading,
                analysis?.GetAgentOpinion(cache.LangProject.DefaultUserAgent).ToString(),
                operation.Kind == WfiWordformSpellingStatusOperationKinds.SetSpellingStatus
                    ? wordform.SpellingStatus : null, occurrenceEvidence);
            draft.Operations.Add(ToDraft(operation, fingerprint, change.ChangeId));
            draft.ContractVersions[OperationKind.GetGroup(operation.Kind)] = "1.0";
        }
        if (operations.Count > 0)
            AddComposerProvenance(draft, new
            {
                composer = "AnalysisChange", change.ChangeId, change.Kind, change.WordformId,
                change.Word, change.AssessmentId, change.DisplayReading, change.StoredAnalysisId, change.ReadingIndex,
                change.OriginPage,
                displayAnalyses = DisplayAnalyses(database, cache, Path.GetFileNameWithoutExtension(
                    baseline.FwDataPath), wordform, change, reading),
                operationIds = operations.Select(operation => operation.OperationId.Value).ToArray(),
            });
        var saved = SaveDraft(repository, current, draft);
        if (!saved)
            return RefusePut("change.revision-conflict", "The pending Draft changed. Reload it and try again.",
                ("changeId", change.ChangeId), ("expectedRevision", expectedRevision));
        var replacedChangeId = operations.Count > 0 && occupied.ChangeId != change.ChangeId
            ? occupied.ChangeId : null;
        var cancelledChangeId = operations.Count == 0 && removed > 0
            ? occupied.ChangeId ?? change.ChangeId : null;
        return CommandOutcome<PutDetails>.Success(
            new PutDetails(null, replacedChangeId, cancelledChangeId));
    }

    private sealed record PutDetails(string? SkippedWord, string? ReplacedChangeId, string? CancelledChangeId);

    public static CommandOutcome<PendingChangesSnapshot> Remove(RemovePendingChangeRequest request) =>
        ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion, (database, project) =>
        {
            var repository = new ProposalRepository(database);
            var current = Current(repository);
            if (Revision(current?.ProposalJson) != request.ExpectedRevision)
                return Refuse("change.revision-conflict", "The pending Draft changed. Reload it and try again.",
                    ("changeId", request.ChangeId), ("expectedRevision", request.ExpectedRevision));
            if (current is null)
                return Refuse("change.not-found", "The change is not in the pending Draft.",
                    ("changeId", request.ChangeId));
            var draft = ParseDraft(current.ProposalJson!);
            var removed = RemoveChange(draft, request.ChangeId);
            if (removed == 0)
                return Refuse("change.not-found", "The change is not in the pending Draft.",
                    ("changeId", request.ChangeId));
            if (!repository.TrySaveDraft(DraftName, current.ProposalJson!,
                JsonSerializer.Serialize(draft, JsonOptions)))
                return Refuse("change.revision-conflict", "The pending Draft changed. Reload it and try again.",
                    ("changeId", request.ChangeId), ("expectedRevision", request.ExpectedRevision));
            return CommandOutcome<PendingChangesSnapshot>.Success(
                Snapshot(database, project, repository));
        });

    public static CommandOutcome<PendingChangesSnapshot> Recheck(RecheckPendingChangesRequest request) =>
        RunCheckedDraft(request.FwDataPath, request.ProductVersion, request.ExpectedRevision, null,
            (database, project, repository, current, baseline, liveLastWriteTicks, draft, proposal) =>
            WithFitCache(project, baseline, liveLastWriteTicks, (cache, cacheLastWriteTicks) =>
            {
                var fits = ChangeFitPreflight.Check(cache, proposal, baseline.Token, requireSameBaseline: false)
                    .ToDictionary(fit => fit.OperationId, StringComparer.Ordinal);
                var token = JsonSerializer.Serialize(baseline.Token, JsonOptions);
                var changed = false;
                foreach (var operation in draft.Operations)
                {
                    if (!fits.TryGetValue(operation.OperationId, out var fit) ||
                        fit.Status == ChangeFitStatus.NoLongerFits) continue;
                    var fingerprint = ReadFingerprint(operation);
                    if (fingerprint is null || fingerprint.BaselineToken == token) continue;
                    ReplaceChangeFit(operation, fingerprint with { BaselineToken = token });
                    changed = true;
                }
                if (changed && !repository.TrySaveDraft(DraftName, current.ProposalJson!,
                    JsonSerializer.Serialize(draft, JsonOptions)))
                    return Refuse("change.revision-conflict", "The pending Draft changed. Reload it and try again.");
                return CommandOutcome<PendingChangesSnapshot>.Success(
                    Snapshot(database, project, repository, cache, cacheLastWriteTicks, baseline));
            }));

    public static CommandOutcome<PendingChangesSnapshot> Reconfirm(ReconfirmPendingChangeRequest request) =>
        RunCheckedDraft(request.FwDataPath, request.ProductVersion, request.ExpectedRevision, request.ChangeId,
            (database, project, repository, current, baseline, liveLastWriteTicks, draft, proposal) =>
            {
            var operations = draft.Operations.Where(operation => ChangeIdOf(operation) == request.ChangeId).ToArray();
            if (operations.Length == 0)
                return Refuse("change.not-found", "The change is not in the pending Draft.",
                    ("changeId", request.ChangeId));
            return WithFitCache(project, baseline, liveLastWriteTicks, (cache, cacheLastWriteTicks) =>
            {
                var fits = ChangeFitPreflight.Check(cache, proposal, baseline.Token, requireSameBaseline: false)
                    .ToDictionary(fit => fit.OperationId, StringComparer.Ordinal);
                var targetFits = operations.Select(operation => fits.GetValueOrDefault(operation.OperationId))
                    .ToArray();
                if (targetFits.Any(fit => fit is null || fit.Status == ChangeFitStatus.NoLongerFits))
                    return Refuse("change.reconfirm-not-allowed",
                        "This change no longer fits the project and cannot be reconfirmed.",
                        ("changeId", request.ChangeId));
                if (!targetFits.Any(fit => fit!.Status == ChangeFitStatus.Uncertain))
                    return Refuse("change.reconfirm-unneeded",
                        "This change is not uncertain and does not need another check.",
                        ("changeId", request.ChangeId));

                var textWords = new BaselineRepository(database).GetCurrentTextWords(
                    ProjectWorkspaceKey.Compute(project), operations.Select(operation =>
                    {
                        var fingerprint = ReadFingerprint(operation);
                        return fingerprint?.Occurrence?.Anchor.TextId ?? Guid.Empty;
                    }).Where(id => id != Guid.Empty).ToArray());
                if (textWords is null)
                    return Refuse("change.occurrence-unavailable",
                        "The occurrence is not available in the current Baseline.",
                        ("changeId", request.ChangeId));
                if (textWords.Baseline.Token != baseline.Token)
                    return Refuse("change.refresh-required", "Refresh the project before checking changes again.",
                        ("changeId", request.ChangeId));

                var token = JsonSerializer.Serialize(baseline.Token, JsonOptions);
                foreach (var operation in operations)
                {
                    var fingerprint = ReadFingerprint(operation);
                    if (fingerprint?.Occurrence is not { } oldEvidence)
                        return Refuse("change.occurrence-unavailable",
                            "The occurrence evidence is missing or invalid.",
                            ("changeId", request.ChangeId));
                    if (!OccurrenceFitEvidenceResolver.TryCapture(textWords.Projection, oldEvidence.Anchor,
                            out var currentEvidence, out var reason))
                        return Refuse("change.occurrence-unavailable",
                            reason,
                            ("changeId", request.ChangeId));
                    if (currentEvidence!.WordformId != fingerprint.WordformId)
                        return Refuse("change.occurrence-wordform-mismatch",
                            "The selected occurrence belongs to a different wordform.",
                            ("changeId", request.ChangeId));
                    if (OccurrenceFitEvidenceResolver.Compare(currentEvidence, textWords.Projection) is not null)
                        return Refuse("change.occurrence-unavailable",
                            "The occurrence evidence is still uncertain. Refresh the project parse before reconfirming.",
                            ("changeId", request.ChangeId));
                    ReplaceChangeFit(operation,
                        fingerprint with { BaselineToken = token, Occurrence = currentEvidence });
                }
                if (!repository.TrySaveDraft(DraftName, current.ProposalJson!,
                    JsonSerializer.Serialize(draft, JsonOptions)))
                    return Refuse("change.revision-conflict", "The pending Draft changed. Reload it and try again.",
                        ("changeId", request.ChangeId), ("expectedRevision", request.ExpectedRevision));
                return CommandOutcome<PendingChangesSnapshot>.Success(
                    Snapshot(database, project, repository, cache, cacheLastWriteTicks, baseline));
            });
        });

    private static ChangeFitFingerprint? ReadFingerprint(DraftOperation operation)
    {
        if (operation.Extensions is not { ValueKind: JsonValueKind.Object } extensions ||
            !extensions.TryGetProperty("changeFit", out var fit) || fit.ValueKind != JsonValueKind.Object)
            return null;
        try { return JsonSerializer.Deserialize<ChangeFitFingerprint>(fit.GetRawText(), JsonOptions); }
        catch (JsonException) { return null; }
    }

    private static void ReplaceChangeFit(DraftOperation operation, ChangeFitFingerprint fingerprint)
    {
        if (operation.Extensions is not { ValueKind: JsonValueKind.Object } extensions)
            throw new InvalidDataException("The pending change has no extensions object.");
        var properties = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
            extensions.GetRawText(), JsonOptions)!;
        properties["changeFit"] = JsonSerializer.SerializeToElement(fingerprint, JsonOptions);
        operation.Extensions = JsonSerializer.SerializeToElement(properties, JsonOptions);
    }

    private static CommandOutcome<PendingChangesSnapshot> RunCheckedDraft(string fwDataPath,
        string productVersion, string expectedRevision, string? changeId,
        Func<MotifDatabase, ProjectLocator, ProposalRepository, ProposalRecord, BaselineRecord, long,
            DraftDocument, Proposal, CommandOutcome<PendingChangesSnapshot>> action) =>
        ProjectStoreCommand.Run(fwDataPath, productVersion, (database, project) =>
        {
            var repository = new ProposalRepository(database);
            var current = Current(repository);
            if (Revision(current?.ProposalJson) != expectedRevision)
                return Refuse("change.revision-conflict", "The pending Draft changed. Reload it and try again.",
                    ("changeId", changeId), ("expectedRevision", expectedRevision));
            if (current is null)
                return Refuse("change.not-found", changeId is null
                    ? "There are no pending changes to check."
                    : "The change is not in the pending Draft.", ("changeId", changeId));
            var baseline = new BaselineRepository(database).GetCurrent(ProjectWorkspaceKey.Compute(project));
            var liveLastWriteTicks = File.GetLastWriteTimeUtc(project.FullFwDataPath).Ticks;
            if (baseline is null || liveLastWriteTicks > baseline.SourceLastWriteUtc.UtcDateTime.Ticks)
                return Refuse("change.refresh-required", "Refresh the project before checking changes again.",
                    ("changeId", changeId));
            var draft = ParseDraft(current.ProposalJson!);
            var proposal = ProposalJsonParser.Parse(ProposalCommands.BuildProposalJson(draft));
            return action(database, project, repository, current, baseline, liveLastWriteTicks, draft, proposal);
        });

    private static ProposalRecord? Current(ProposalRepository repository) =>
        repository.DraftNameExists(DraftName) ? repository.GetDraft(DraftName) : null;

    private static DraftDocument LoadDraft(ProposalRecord? current) => current is null
        ? new DraftDocument
        {
            ProposalId = CanonicalId.Mint().Value,
            Label = "Changes to word analyses",
            Comment = "Changes to word analyses and spelling.",
        }
        : ParseDraft(current.ProposalJson!);

    private static bool SaveDraft(ProposalRepository repository, ProposalRecord? current, DraftDocument draft)
    {
        var json = JsonSerializer.Serialize(draft, JsonOptions);
        return current is null
            ? repository.TryCreateDraft(DraftName, CanonicalId.Parse(draft.ProposalId), json)
            : repository.TrySaveDraft(DraftName, current.ProposalJson!, json);
    }

    private static void AddComposerProvenance(DraftDocument draft, object provenance) =>
        draft.ComposerProvenance.Add(JsonSerializer.SerializeToElement(provenance, JsonOptions));

    private static DraftDocument ParseDraft(string json) =>
        JsonSerializer.Deserialize<DraftDocument>(json, JsonOptions)
        ?? throw new InvalidDataException("The pending Draft has no content.");

    private static string Revision(string? json) => DraftRevision.Compute(json);

    private static PendingChangesSnapshot Snapshot(MotifDatabase database, ProjectLocator project,
        ProposalRepository repository, LcmCache? fitCache = null, long? observedLastWriteTicks = null,
        BaselineRecord? currentBaseline = null)
    {
        var current = Current(repository);
        var fitRepository = new PendingChangeFitRepository(database);
        if (current is null)
        {
            fitRepository.Clear();
            return new PendingChangesSnapshot(null, "none", [], []);
        }
        var draft = ParseDraft(current.ProposalJson!);
        var provenance = draft.ComposerProvenance.Where(entry => ChangeIdOf(entry) is not null)
            .GroupBy(entry => ChangeIdOf(entry)!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);
        var ids = provenance.Keys.Concat(draft.Operations.Select(ChangeIdOf).OfType<string>())
            .Distinct(StringComparer.Ordinal).ToArray();
        var changes = ids.Select(id =>
        {
            provenance.TryGetValue(id, out var entry);
            var operations = draft.Operations.Where(operation => ChangeIdOf(operation) == id).ToArray();
            var operationIds = operations.Select(operation => operation.OperationId)
                .Concat(OperationIdsOf(entry)).Distinct(StringComparer.Ordinal).ToArray();
            var fingerprint = operations.Select(operation => operation.Extensions)
                .Where(extensions => extensions is { ValueKind: JsonValueKind.Object })
                .Select(extensions => extensions!.Value.TryGetProperty("changeFit", out var fit) ? fit : default)
                .FirstOrDefault(fit => fit.ValueKind == JsonValueKind.Object);
            var wordformId = Property(entry, "wordformId") ?? Property(fingerprint, "wordformId") ?? "";
            return new PendingChange(id, wordformId, Property(entry, "word") ??
                Property(fingerprint, "wordformForm") ?? "", Property(entry, "kind") ??
                operations.FirstOrDefault()?.Kind ?? "", Property(entry, "assessmentId"),
                Property(entry, "displayReading"), operationIds)
            {
                OriginPage = Property(entry, "originPage"),
                Analyses = DisplayAnalysesOf(entry),
                Occurrence = OccurrenceOf(fingerprint),
                StoredAnalysisId = Property(entry, "storedAnalysisId"),
                ReadingIndex = IntegerProperty(entry, "readingIndex"),
                GroupId = Property(entry, "groupId"),
            };
        }).ToArray();
        if (changes.Length == 0)
        {
            fitRepository.Clear();
            return new PendingChangesSnapshot(draft.ProposalId, Revision(current.ProposalJson), changes, []);
        }

        var revision = Revision(current.ProposalJson);
        currentBaseline ??= new BaselineRepository(database).GetCurrent(ProjectWorkspaceKey.Compute(project));
        var navigation = SavedProjectNavigation.Read(project.FullFwDataPath, currentBaseline?.Token.ProjectIdentity);
        changes = changes.Select(change => change with
        {
            Analyses = change.Analyses.Select(analysis => analysis with
                { Reading = navigation.Verify(analysis.Reading) }).ToArray(),
        }).ToArray();
        var baselineIdentity = BaselineIdentity(currentBaseline?.Token);
        var lastWriteTicks = observedLastWriteTicks ?? File.GetLastWriteTimeUtc(project.FullFwDataPath).Ticks;
        var fits = fitRepository.Get(revision, lastWriteTicks, baselineIdentity);
        if (fits is null)
        {
            fits = fitCache is null
                ? WithFitCache(project, currentBaseline, lastWriteTicks,
                    (cache, cacheLastWriteTicks) => SaveFit(cache, cacheLastWriteTicks))
                : SaveFit(fitCache, lastWriteTicks);

            IReadOnlyList<ChangeFit> SaveFit(LcmCache cache, long cacheLastWriteTicks)
            {
                var computed = ComputeFitSummary(cache, draft, changes, provenance, currentBaseline?.Token);
                fitRepository.Save(revision, cacheLastWriteTicks, baselineIdentity, computed);
                    return computed;
            }
        }
        fits = AddOccurrenceAnchors(fits, draft, changes);
        return new PendingChangesSnapshot(draft.ProposalId, revision, changes, fits);
    }

    private static IReadOnlyList<ChangeFit> ComputeFitSummary(LcmCache cache, DraftDocument draft,
        IReadOnlyList<PendingChange> changes, IReadOnlyDictionary<string, JsonElement> provenance,
        BaselineToken? baseline)
    {
        var proposal = ProposalJsonParser.Parse(ProposalCommands.BuildProposalJson(draft));
        var operationFits = ChangeFitPreflight.Check(cache, proposal, baseline)
            .ToDictionary(item => item.OperationId, StringComparer.Ordinal);
        return changes.Select(change =>
        {
            var operationResults = new List<ChangeFitResult>();
            var reasons = new List<string>();
            var mappingMissing = false;
            foreach (var id in change.OperationIds)
            {
                if (!draft.Operations.Any(operation => operation.OperationId == id &&
                        ChangeIdOf(operation) == change.ChangeId) || !operationFits.TryGetValue(id, out var fit))
                {
                    mappingMissing = true;
                    reasons.Add(ChangeFitReasons.MappingMissing);
                    continue;
                }
                operationResults.Add(fit);
                if (!fit.StillFits) reasons.Add(fit.Reason);
            }
            if (!provenance.ContainsKey(change.ChangeId) || change.OperationIds.Count == 0)
                reasons = [ChangeFitReasons.MappingMissing];
            mappingMissing |= !provenance.ContainsKey(change.ChangeId) || change.OperationIds.Count == 0;
            var distinctReasons = reasons.Distinct(StringComparer.Ordinal).ToArray();
            var status = distinctReasons.Length == 0 ? ChangeFitStatus.Fits :
                !mappingMissing && operationResults.Any(result => result.Status == ChangeFitStatus.Uncertain) &&
                !operationResults.Any(result => result.Status == ChangeFitStatus.NoLongerFits)
                    ? ChangeFitStatus.Uncertain : ChangeFitStatus.NoLongerFits;
            var uncertainty = status == ChangeFitStatus.Uncertain
                ? operationResults.FirstOrDefault(result => result.Status == ChangeFitStatus.Uncertain)?.Uncertainty
                : null;
            return new ChangeFit(change.ChangeId, status, distinctReasons)
            {
                Uncertainty = uncertainty,
                Occurrence = OccurrenceOf(draft, change),
            };
        }).ToArray();
    }

    private static IReadOnlyList<ChangeFit> AddOccurrenceAnchors(IReadOnlyList<ChangeFit> fits,
        DraftDocument draft, IReadOnlyList<PendingChange> changes)
    {
        var anchors = changes.ToDictionary(change => change.ChangeId,
            change => OccurrenceOf(draft, change), StringComparer.Ordinal);
        return fits.Select(fit => fit with { Occurrence = anchors.GetValueOrDefault(fit.ChangeId) }).ToArray();
    }

    private static OccurrenceAnchor? OccurrenceOf(DraftDocument draft, PendingChange change)
    {
        foreach (var id in change.OperationIds)
        {
            var operation = draft.Operations.FirstOrDefault(item => item.OperationId == id &&
                ChangeIdOf(item) == change.ChangeId);
            if (operation is not null && ReadFingerprint(operation)?.Occurrence is { } occurrence)
                return occurrence.Anchor;
        }

        return null;
    }

    private static IReadOnlyList<ReviewAnalysis> DisplayAnalyses(MotifDatabase database, LcmCache cache,
        string projectName, IWfiWordform wordform, ChangeIntent change, ParseAnalysis? reading)
    {
        var stored = StoredAnalyses(cache, projectName, CanonicalId.FromGuid(wordform.Guid).Value);
        if (change.AssessmentId is not { } assessmentId)
            return stored.Select(item => item.Analysis with
            {
                Touched = item.StoredAnalysisId == change.StoredAnalysisId,
            }).ToArray();
        try
        {
            var word = new AssessmentRepository(database).Get(assessmentId).Words?
                .SingleOrDefault(item => item.Word == change.Word);
            if (word?.Morphology is not { } morphology)
                return stored.Select(item => item.Analysis).ToArray();
            var readings = ParserReadingReader.Read(cache, projectName, morphology);
            var selected = change.ReadingIndex;
            if (selected is null && reading is { } authoredReading)
            {
                var digest = ChangeFitPreflight.ReadingDigest(authoredReading);
                var matching = morphology.Analyses.Select((analysis, index) => (analysis, index))
                    .Where(candidate => ChangeFitPreflight.ReadingDigest(candidate.analysis) == digest)
                    .Take(2).ToArray();
                if (matching.Length == 1) selected = matching[0].index;
            }
            var parserKeys = morphology.Analyses.Select(ProjectAnalysisKey.For).ToHashSet(StringComparer.Ordinal);
            return readings.Select((reading, index) =>
            {
                var key = ProjectAnalysisKey.For(morphology.Analyses[index]);
                var match = stored.FirstOrDefault(item => item.Key == key);
                return new ReviewAnalysis(reading,
                    match.Analysis?.Opinion ?? word.ReadingGrades?.ElementAtOrDefault(index) ?? ReadingGrade.NoOpinion,
                    selected == index || match.StoredAnalysisId == change.StoredAnalysisId,
                    match.Analysis is not null);
            }).Concat(stored.Where(item => !parserKeys.Contains(item.Key))
                .Select(item => item.Analysis with
                {
                    Touched = item.StoredAnalysisId == change.StoredAnalysisId,
                })).ToArray();
        }
        catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException)
        {
            return stored.Select(item => item.Analysis with
            {
                Touched = item.StoredAnalysisId == change.StoredAnalysisId,
            }).ToArray();
        }
    }

    private static IReadOnlyList<ReviewAnalysis> DisplayAnalysesOf(JsonElement entry)
    {
        if (entry.ValueKind != JsonValueKind.Object ||
            !entry.TryGetProperty("displayAnalyses", out var analyses) ||
            analyses.ValueKind != JsonValueKind.Array) return [];
        try
        {
            return JsonSerializer.Deserialize<IReadOnlyList<ReviewAnalysis>>(analyses.GetRawText(), JsonOptions)
                ?? throw new InvalidDataException("Stored change display analyses are incomplete.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Stored change display analyses are malformed.", exception);
        }
    }

    private static OccurrenceAnchor? OccurrenceOf(JsonElement fingerprint)
    {
        if (fingerprint.ValueKind != JsonValueKind.Object) return null;
        try
        {
            return JsonSerializer.Deserialize<ChangeFitFingerprint>(fingerprint.GetRawText(), JsonOptions)
                ?.Occurrence?.Anchor;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static T WithFitCache<T>(ProjectLocator project, BaselineRecord? baseline,
        long liveLastWriteTicks, Func<LcmCache, long, T> action) =>
        ProjectReadCache.ReadCurrent(project, baseline, liveLastWriteTicks, action);

    private static LcmCache LoadBaselineCache(string path)
    {
        try
        {
            return new FwDataProjectLoader().LoadScratchCache(path);
        }
        catch (LcmFileLockedException)
        {
            throw new ProjectBaselineBusyException();
        }
    }

    private static string BaselineIdentity(BaselineToken? baselineToken) => baselineToken is null
        ? ""
        : JsonSerializer.Serialize(baselineToken.SemanticIdentity, JsonOptions);

    private static IReadOnlyList<(string Key, string StoredAnalysisId, ReviewAnalysis Analysis)> StoredAnalyses(
        LcmCache cache, string projectName, string wordformId)
    {
        if (!CanonicalId.TryParse(wordformId, out var id) ||
            !cache.ServiceLocator.GetInstance<IWfiWordformRepository>().TryGetObject(id.ToGuid(), out var wordform))
            return [];
        return wordform.AnalysesOC.Select(analysis =>
        {
            var morphs = analysis.MorphBundlesOS.Select(bundle => new ParseMorph(
                bundle.MorphRA?.Guid.ToString("D"), bundle.MsaRA?.Guid.ToString("D"),
                bundle.InflTypeRA?.Guid.ToString("D"), null)).ToArray();
            var key = AnalysisContent.ComputeDigest(morphs.Select(morph =>
                new MorphBundleContent(morph.Form, morph.Msa, morph.InflType)).ToArray());
            var opinion = wordform.HumanApprovedAnalyses.Contains(analysis) ? ReadingGrade.Approved :
                wordform.HumanDisapprovedParses.Contains(analysis) ? ReadingGrade.Disapproved : ReadingGrade.Candidate;
            var reading = new ParserReading(ParserReadingReader.ReadMorphs(cache, projectName, morphs));
            return (key, CanonicalId.FromGuid(analysis.Guid).Value,
                new ReviewAnalysis(reading, opinion, false, true));
        }).ToArray();
    }

    private static string? ChangeIdOf(DraftOperation operation) =>
        operation.Extensions is { ValueKind: JsonValueKind.Object } extensions &&
        extensions.TryGetProperty("changeId", out var id)
            ? id.GetString() : null;

    private static string? ChangeIdOf(JsonElement entry) =>
        entry.ValueKind == JsonValueKind.Object && entry.TryGetProperty("changeId", out var id)
            ? id.GetString() : null;

    private static string? GroupIdOf(JsonElement entry) =>
        entry.ValueKind == JsonValueKind.Object && entry.TryGetProperty("groupId", out var id)
            ? id.GetString() : null;

    internal static string? Property(JsonElement entry, string name) =>
        entry.ValueKind == JsonValueKind.Object && entry.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int? IntegerProperty(JsonElement entry, string name) =>
        entry.ValueKind == JsonValueKind.Object && entry.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var result) ? result : null;

    private static int RemoveChange(DraftDocument draft, string changeId)
    {
        var provenance = draft.ComposerProvenance.Where(entry => ChangeIdOf(entry) == changeId ||
            GroupIdOf(entry) == changeId).ToArray();
        var changeIds = provenance.Select(ChangeIdOf).OfType<string>().ToHashSet(StringComparer.Ordinal);
        var operationIds = provenance
            .SelectMany(OperationIdsOf).ToHashSet(StringComparer.Ordinal);
        var removed = draft.Operations.RemoveAll(operation =>
            ChangeIdOf(operation) == changeId || changeIds.Contains(ChangeIdOf(operation) ?? "") ||
            operationIds.Contains(operation.OperationId));
        return removed + draft.ComposerProvenance.RemoveAll(entry => ChangeIdOf(entry) == changeId ||
            GroupIdOf(entry) == changeId);
    }

    private static IEnumerable<string> OperationIdsOf(JsonElement entry) =>
        entry.ValueKind == JsonValueKind.Object && entry.TryGetProperty("operationIds", out var ids) &&
        ids.ValueKind == JsonValueKind.Array
            ? ids.EnumerateArray().Where(id => id.ValueKind == JsonValueKind.String)
                .Select(id => id.GetString()!).ToArray()
            : [];

    private static DraftOperation ToDraft(OperationEnvelope operation, ChangeFitFingerprint fingerprint,
        string changeId) => new()
    {
        OperationId = operation.OperationId.Value,
        Kind = operation.Kind,
        Target = operation.Target?.Value,
        EntityId = operation.EntityId?.Value,
        DependsOn = operation.DependsOn.Select(item => item.OperationId.Value).ToList(),
        After = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(operation.After!.Value.GetRawText())!,
        Extensions = JsonSerializer.SerializeToElement(new { changeId, changeFit = fingerprint }, JsonOptions),
    };

    private static CommandOutcome<PendingChangesSnapshot> Refuse(string code, string message,
        params (string Name, string? Value)[] facts) => CommandOutcome<PendingChangesSnapshot>.Refused(
            new Refusal(code, FailureReason.Refused, message,
                facts.Where(item => item.Value is not null).ToDictionary(item => item.Name,
                    item => item.Value!, StringComparer.Ordinal)));

    // A spelling mark or a removal names the wordform or a stored analysis, never a parser reading.
    private static bool CarriesReading(string kind) =>
        kind is not (AnalysisChangeKinds.IncorrectSpelling or AnalysisChangeKinds.RemoveAnalysis);

    private static CommandOutcome<PutDetails> RefusePut(string code, string message,
        params (string Name, string? Value)[] facts) => CommandOutcome<PutDetails>.Refused(
            new Refusal(code, FailureReason.Refused, message,
                facts.Where(item => item.Value is not null).ToDictionary(item => item.Name,
                    item => item.Value!, StringComparer.Ordinal)));
}
