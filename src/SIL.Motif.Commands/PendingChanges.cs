using System.Security.Cryptography;
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
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Store;
using SIL.Motif.Runner.Composers;
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
            if (change.Kind is AnalysisChangeKinds.Approve or AnalysisChangeKinds.Reject or AnalysisChangeKinds.Candidate &&
                change.StoredAnalysisId is null && change.ReadingIndex is null)
                return Refuse("change.analysis-identity-required",
                    "Choose one analysis explicitly before changing its opinion.", ("changeId", change.ChangeId));
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

            var draft = current is null ? new DraftDocument
            {
                ProposalId = CanonicalId.Mint().Value,
                Label = "Changes to word analyses",
                Comment = "Changes to word analyses and spelling.",
            } :
                ParseDraft(current.ProposalJson!);
            using var cache = new FwDataProjectLoader().LoadScratchCache(project.FullFwDataPath);
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
                        return Refuse("change.wordform-missing", "The selected wordform is no longer in the project.",
                            ("changeId", change.ChangeId), ("word", change.Word));
                    if (matches.Length > 1)
                        return Refuse("change.wordform-ambiguous", "More than one wordform has this form.",
                            ("changeId", change.ChangeId), ("word", change.Word));
                    wordform = matches[0];
                }
                else wordform = words.GetObject(CanonicalId.Parse(change.WordformId).ToGuid());
            }
            catch (Exception exception) when (exception is FormatException or KeyNotFoundException or InvalidOperationException)
            {
                return Refuse("change.wordform-missing", "The selected wordform is no longer in the project.",
                    ("changeId", change.ChangeId), ("wordformId", change.WordformId));
            }
            change = change with { WordformId = CanonicalId.FromGuid(wordform.Guid).Value };
            var form = wordform.Form.VernacularDefaultWritingSystem?.Text ?? "";
            if (form.Normalize(NormalizationForm.FormD) != change.Word.Normalize(NormalizationForm.FormD))
                return Refuse("change.wordform-changed", "The selected wordform changed its form.",
                    ("changeId", change.ChangeId), ("wordformId", change.WordformId));

            if (change.Kind == AnalysisChangeKinds.AddCandidate &&
                draft.ComposerProvenance.Any(entry => Property(entry, "wordformId") == change.WordformId &&
                    Property(entry, "kind") is AnalysisChangeKinds.Approve or AnalysisChangeKinds.Reject or
                        AnalysisChangeKinds.Candidate))
                return CommandOutcome<PendingChangesSnapshot>.Success(
                    Snapshot(database, project, repository) with { SkippedWord = change.Word });

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
                    return Refuse("change.stored-analysis-missing", "The chosen analysis is no longer under this wordform.",
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
                        return Refuse("change.assessment-stale", "The Assessment belongs to an older Baseline.",
                            ("changeId", change.ChangeId), ("wordformId", change.WordformId),
                            ("assessmentId", assessmentId));
                    var word = assessment.Words?.SingleOrDefault(item =>
                        item.Word.Normalize(NormalizationForm.FormD) == form.Normalize(NormalizationForm.FormD));
                    if (change.StoredAnalysisId is null &&
                        (reading is null || (change.ReadingIndex is { } index
                            ? index < 0 || index >= (word?.Morphology?.Analyses.Count ?? 0) ||
                              ChangeFitPreflight.ReadingDigest(word!.Morphology!.Analyses[index]) !=
                              ChangeFitPreflight.ReadingDigest(reading)
                            : word?.Morphology?.Analyses.Any(item =>
                                ChangeFitPreflight.ReadingDigest(item) == ChangeFitPreflight.ReadingDigest(reading)) != true)))
                        return Refuse("change.reading-missing", "The chosen reading is absent from the Assessment.",
                            ("changeId", change.ChangeId), ("wordformId", change.WordformId),
                            ("assessmentId", assessmentId));
                }
                catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException)
                {
                    return Refuse("change.assessment-missing", "The Assessment is unavailable.",
                        ("changeId", change.ChangeId), ("assessmentId", assessmentId));
                }
            }
            if (change.Kind != AnalysisChangeKinds.IncorrectSpelling && reading is null)
                return Refuse("change.reading-missing", "Choose an exact reading for this change.",
                    ("changeId", change.ChangeId), ("wordformId", change.WordformId));

            var occupied = reading is null ? default : draft.Operations.Select(operation =>
            {
                if (operation.Extensions is not { ValueKind: JsonValueKind.Object } extensions ||
                    !extensions.TryGetProperty("changeFit", out var fit) ||
                    fit.ValueKind != JsonValueKind.Object) return (ChangeId: (string?)null, Fits: false);
                return (ChangeId: ChangeIdOf(operation), Fits:
                    Property(fit, "wordformId") == change.WordformId &&
                    Property(fit, "readingContentDigest") == ChangeFitPreflight.ReadingDigest(reading!));
            }).FirstOrDefault(item => item.Fits);
            if (occupied.Fits && occupied.ChangeId is null)
                return Refuse("change.slot-occupied", "An unmapped change addresses this word and reading.",
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
                return Refuse("change.cannot-compose", exception.Message,
                    ("changeId", change.ChangeId), ("wordformId", change.WordformId));
            }
            var removed = RemoveChange(draft, change.ChangeId);
            if (occupied.ChangeId is { } occupiedId && occupiedId != change.ChangeId)
                removed += RemoveChange(draft, occupiedId);
            foreach (var group in draft.ContractVersions.Keys.ToArray())
                if (!draft.Operations.Any(operation => OperationKind.GetGroup(operation.Kind) == group))
                    draft.ContractVersions.Remove(group);
            if (operations.Count == 0 && removed == 0)
                return Refuse("change.no-effect", "The chosen analysis already has that state.",
                    ("changeId", change.ChangeId));
            var existingOperations = draft.Operations.Count == 0
                ? Array.Empty<OperationEnvelope>()
                : ProposalJsonParser.Parse(ProposalCommands.BuildProposalJson(draft)).Operations;
            if (AnalysisOpinionSlotValidator.FindConflict(existingOperations.Concat(operations)) is { } collision)
            {
                var existingChangeId = collision.Existing.Extensions is { ValueKind: JsonValueKind.Object } extension &&
                    extension.TryGetProperty("changeId", out var id) ? id.GetString() : null;
                return Refuse("change.slot-occupied", "Another change already addresses that project slot.",
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
                        ? wordform.SpellingStatus : null);
                draft.Operations.Add(ToDraft(operation, fingerprint, change.ChangeId));
                draft.ContractVersions[OperationKind.GetGroup(operation.Kind)] = "1.0";
            }
            if (operations.Count > 0)
                draft.ComposerProvenance.Add(JsonSerializer.SerializeToElement(new
                {
                    composer = "AnalysisChange", change.ChangeId, change.Kind, change.WordformId,
                    change.Word, change.AssessmentId, change.DisplayReading, change.StoredAnalysisId, change.ReadingIndex,
                    change.OriginPage,
                    operationIds = operations.Select(operation => operation.OperationId.Value).ToArray(),
                }, JsonOptions));
            var json = JsonSerializer.Serialize(draft, JsonOptions);
            var saved = current is null
                ? repository.TryCreateDraft(DraftName, CanonicalId.Parse(draft.ProposalId), json)
                : repository.TrySaveDraft(DraftName, current.ProposalJson!, json);
            if (!saved)
                return Refuse("change.revision-conflict", "The pending Draft changed. Reload it and try again.",
                    ("changeId", change.ChangeId), ("expectedRevision", request.ExpectedRevision));
            return CommandOutcome<PendingChangesSnapshot>.Success(
                Snapshot(database, project, repository) with
                {
                    ReplacedChangeId = operations.Count > 0 && occupied.ChangeId != change.ChangeId
                        ? occupied.ChangeId : null,
                    CancelledChangeId = operations.Count == 0 && removed > 0
                        ? occupied.ChangeId ?? change.ChangeId : null,
                });
        });

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
        ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion, (database, project) =>
        {
            var repository = new ProposalRepository(database);
            var current = Current(repository);
            if (Revision(current?.ProposalJson) != request.ExpectedRevision)
                return Refuse("change.revision-conflict", "The pending Draft changed. Reload it and try again.");
            if (current is null) return Refuse("change.not-found", "There are no pending changes to check.");
            var baseline = new BaselineRepository(database).GetCurrent(ProjectWorkspaceKey.Compute(project));
            if (baseline is null || File.GetLastWriteTimeUtc(project.FullFwDataPath) >
                baseline.SourceLastWriteUtc.UtcDateTime)
                return Refuse("change.refresh-required", "Refresh the project before checking changes again.");
            var draft = ParseDraft(current.ProposalJson!);
            var proposal = ProposalJsonParser.Parse(ProposalCommands.BuildProposalJson(draft));
            using var cache = new FwDataProjectLoader().LoadScratchCache(project.FullFwDataPath);
            var fits = ChangeFitPreflight.Check(cache, proposal, baseline.Token, requireSameBaseline: false)
                .ToDictionary(fit => fit.OperationId, StringComparer.Ordinal);
            var token = JsonSerializer.Serialize(baseline.Token, JsonOptions);
            var changed = false;
            foreach (var operation in draft.Operations)
            {
                if (!fits.TryGetValue(operation.OperationId, out var fit) || !fit.StillFits ||
                    operation.Extensions is not { ValueKind: JsonValueKind.Object } extensions ||
                    !extensions.TryGetProperty("changeFit", out var stored)) continue;
                var fingerprint = JsonSerializer.Deserialize<ChangeFitFingerprint>(stored.GetRawText(), JsonOptions);
                if (fingerprint is null || fingerprint.BaselineToken == token) continue;
                var properties = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                    extensions.GetRawText(), JsonOptions)!;
                properties["changeFit"] = JsonSerializer.SerializeToElement(
                    fingerprint with { BaselineToken = token }, JsonOptions);
                operation.Extensions = JsonSerializer.SerializeToElement(properties, JsonOptions);
                changed = true;
            }
            if (changed && !repository.TrySaveDraft(DraftName, current.ProposalJson!,
                JsonSerializer.Serialize(draft, JsonOptions)))
                return Refuse("change.revision-conflict", "The pending Draft changed. Reload it and try again.");
            return CommandOutcome<PendingChangesSnapshot>.Success(Snapshot(database, project, repository));
        });

    private static ProposalRecord? Current(ProposalRepository repository) =>
        repository.DraftNameExists(DraftName) ? repository.GetDraft(DraftName) : null;

    private static DraftDocument ParseDraft(string json) =>
        JsonSerializer.Deserialize<DraftDocument>(json, JsonOptions)
        ?? throw new InvalidDataException("The pending Draft has no content.");

    private static string Revision(string? json) => json is null ? "none" :
        "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();

    private static PendingChangesSnapshot Snapshot(MotifDatabase database, ProjectLocator project,
        ProposalRepository repository)
    {
        var current = Current(repository);
        if (current is null) return new PendingChangesSnapshot(null, "none", [], []);
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
            };
        }).ToArray();
        if (changes.Length == 0)
            return new PendingChangesSnapshot(draft.ProposalId, Revision(current.ProposalJson), changes, []);

        using var cache = new FwDataProjectLoader().LoadScratchCache(project.FullFwDataPath);
        var assessments = new AssessmentRepository(database);
        var projectName = Path.GetFileNameWithoutExtension(project.FullFwDataPath);
        changes = changes.Select(change =>
        {
            var stored = StoredAnalyses(cache, projectName, change.WordformId);
            if (change.AssessmentId is not { } assessmentId ||
                !provenance.TryGetValue(change.ChangeId, out var entry))
                return change with { Analyses = stored.Select(item => item.Analysis).ToArray() };
            try
            {
                var word = assessments.Get(assessmentId).Words?.SingleOrDefault(item => item.Word == change.Word);
                if (word?.Morphology is not { } morphology)
                    return change with { Analyses = stored.Select(item => item.Analysis).ToArray() };
                var readings = ParserReadingReader.Read(cache, projectName, morphology);
                var selected = Number(entry, "readingIndex");
                if (selected is null)
                {
                    var authored = draft.Operations.Where(operation => ChangeIdOf(operation) == change.ChangeId)
                        .Select(operation => operation.Extensions)
                        .Where(extensions => extensions is { ValueKind: JsonValueKind.Object })
                        .Select(extensions => extensions!.Value.TryGetProperty("changeFit", out var fit) ? fit : default)
                        .FirstOrDefault(fit => fit.ValueKind == JsonValueKind.Object);
                    var readingDigest = Property(authored, "readingContentDigest");
                    if (readingDigest is not null)
                    {
                        var matching = morphology.Analyses.Select((analysis, index) => (analysis, index))
                            .Where(candidate => ChangeFitPreflight.ReadingDigest(candidate.analysis) == readingDigest)
                            .Take(2).ToArray();
                        if (matching.Length == 1) selected = matching[0].index;
                    }
                }
                var parserKeys = morphology.Analyses.Select(ProjectAnalysisKey.For).ToHashSet(StringComparer.Ordinal);
                return change with
                {
                    Analyses = readings.Select((reading, index) =>
                    {
                        var key = ProjectAnalysisKey.For(morphology.Analyses[index]);
                        var match = stored.FirstOrDefault(item => item.Key == key);
                        return new ReviewAnalysis(reading,
                            match.Analysis?.Opinion ?? word.ReadingGrades?.ElementAtOrDefault(index) ?? "no-opinion",
                            selected == index, match.Analysis is not null);
                    }).Concat(stored.Where(item => !parserKeys.Contains(item.Key))
                        .Select(item => item.Analysis)).ToArray(),
                };
            }
            catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException)
            {
                return change with { Analyses = stored.Select(item => item.Analysis).ToArray() };
            }
        }).ToArray();
        var proposal = ProposalJsonParser.Parse(ProposalCommands.BuildProposalJson(draft));
        var baseline = new BaselineRepository(database)
            .GetCurrent(ProjectWorkspaceKey.Compute(project))?.Token;
        var operationFits = ChangeFitPreflight.Check(cache, proposal, baseline)
            .ToDictionary(item => item.OperationId, StringComparer.Ordinal);
        var fits = changes.Select(change =>
        {
            var reasons = change.OperationIds.Select(id =>
                draft.Operations.Any(operation => operation.OperationId == id &&
                    ChangeIdOf(operation) == change.ChangeId) && operationFits.TryGetValue(id, out var fit)
                    ? fit.StillFits ? null : fit.Reason : "Change mapping or fingerprint is missing.")
                .Where(reason => reason is not null).Select(reason => reason!).Distinct().ToArray();
            if (!provenance.ContainsKey(change.ChangeId) || change.OperationIds.Count == 0)
                reasons = ["Change mapping or fingerprint is missing."];
            return new ChangeFit(change.ChangeId, reasons.Length == 0, reasons);
        }).ToArray();
        return new PendingChangesSnapshot(draft.ProposalId, Revision(current.ProposalJson), changes, fits);
    }

    private static IReadOnlyList<(string Key, ReviewAnalysis Analysis)> StoredAnalyses(
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
            var opinion = wordform.HumanApprovedAnalyses.Contains(analysis) ? "approved" :
                wordform.HumanDisapprovedParses.Contains(analysis) ? "disapproved" : "candidate";
            var reading = new ParserReading(ParserReadingReader.ReadMorphs(cache, projectName, morphs));
            return (key, new ReviewAnalysis(reading, opinion, false, true));
        }).ToArray();
    }

    private static string? ChangeIdOf(DraftOperation operation) =>
        operation.Extensions is { ValueKind: JsonValueKind.Object } extensions &&
        extensions.TryGetProperty("changeId", out var id)
            ? id.GetString() : null;

    private static string? ChangeIdOf(JsonElement entry) =>
        entry.ValueKind == JsonValueKind.Object && entry.TryGetProperty("changeId", out var id)
            ? id.GetString() : null;

    private static string? Property(JsonElement entry, string name) =>
        entry.ValueKind == JsonValueKind.Object && entry.TryGetProperty(name, out var value) &&
            value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int? Number(JsonElement entry, string name) =>
        entry.ValueKind == JsonValueKind.Object && entry.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : null;

    private static int RemoveChange(DraftDocument draft, string changeId)
    {
        var operationIds = draft.ComposerProvenance.Where(entry => ChangeIdOf(entry) == changeId)
            .SelectMany(OperationIdsOf).ToHashSet(StringComparer.Ordinal);
        var removed = draft.Operations.RemoveAll(operation =>
            ChangeIdOf(operation) == changeId || operationIds.Contains(operation.OperationId));
        return removed + draft.ComposerProvenance.RemoveAll(entry => ChangeIdOf(entry) == changeId);
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
}
