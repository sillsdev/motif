using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Commands.Store;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.Store;
using SIL.Motif.Runner.Composers;
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
            var baseline = new BaselineRepository(database).GetCurrent(ProjectWorkspaceKey.Compute(project));
            if (baseline is null)
                return Refuse("change.baseline-missing", "Capture a Baseline before collecting changes.",
                    ("changeId", change.ChangeId));

            var draft = current is null ? new DraftDocument { ProposalId = CanonicalId.Mint().Value } :
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
                    change = change with { WordformId = CanonicalId.FromGuid(wordform.Guid).Value };
                }
                else wordform = words.GetObject(CanonicalId.Parse(change.WordformId).ToGuid());
            }
            catch (Exception exception) when (exception is FormatException or KeyNotFoundException or InvalidOperationException)
            {
                return Refuse("change.wordform-missing", "The selected wordform is no longer in the project.",
                    ("changeId", change.ChangeId), ("wordformId", change.WordformId));
            }
            var form = wordform.Form.VernacularDefaultWritingSystem?.Text ?? "";
            if (form.Normalize(NormalizationForm.FormD) != change.Word.Normalize(NormalizationForm.FormD))
                return Refuse("change.wordform-changed", "The selected wordform changed its form.",
                    ("changeId", change.ChangeId), ("wordformId", change.WordformId));

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
                        (reading is null || word?.Morphology?.Analyses.Any(item =>
                            ChangeFitPreflight.ReadingDigest(item) == ChangeFitPreflight.ReadingDigest(reading)) != true))
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

            IReadOnlyList<OperationEnvelope> operations;
            try
            {
                operations = AnalysisChangeComposer.Build(cache, new AnalysisChangeIntent(change.Kind,
                    CanonicalId.Parse(change.WordformId), reading,
                    change.StoredAnalysisId is null ? null : CanonicalId.Parse(change.StoredAnalysisId),
                    change.ChangeId));
            }
            catch (Exception exception) when (exception is InvalidOperationException or FormatException)
            {
                return Refuse("change.cannot-compose", exception.Message,
                    ("changeId", change.ChangeId), ("wordformId", change.WordformId));
            }
            if (operations.Count == 0)
                return Refuse("change.no-effect", "The chosen analysis already has that state.",
                    ("changeId", change.ChangeId));

            RemoveChange(draft, change.ChangeId);
            var token = JsonSerializer.Serialize(baseline.Token, JsonOptions);
            foreach (var operation in operations)
            {
                var analysis = operation.Target is { } target
                    ? wordform.AnalysesOC.FirstOrDefault(item => item.Guid == target.ToGuid()) : null;
                var fingerprint = new ChangeFitFingerprint(change.WordformId,
                    analysis is null ? null : CanonicalId.FromGuid(analysis.Guid).Value,
                    form.Normalize(NormalizationForm.FormD), token,
                    analysis is null ? null : ChangeFitPreflight.ContentDigest(analysis),
                    reading is null ? null : ChangeFitPreflight.ReadingDigest(reading), reading);
                draft.Operations.Add(ToDraft(operation, fingerprint, change.ChangeId));
                draft.ContractVersions[OperationKind.GetGroup(operation.Kind)] = "1.0";
            }
            draft.ComposerProvenance.Add(JsonSerializer.SerializeToElement(new
            {
                composer = "AnalysisChange", change.ChangeId, change.Kind, change.WordformId,
                change.Word, change.AssessmentId, change.DisplayReading, change.StoredAnalysisId,
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
                Snapshot(database, project, repository));
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
                Property(entry, "displayReading"), operationIds);
        }).ToArray();
        if (changes.Length == 0)
            return new PendingChangesSnapshot(draft.ProposalId, Revision(current.ProposalJson), changes, []);

        using var cache = new FwDataProjectLoader().LoadScratchCache(project.FullFwDataPath);
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
