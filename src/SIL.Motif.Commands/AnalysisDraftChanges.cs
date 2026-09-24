using System.Text.Json;
using SIL.Motif.Commands.Store;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.Analysis;
using SIL.Motif.Host.Store;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;
using SIL.LCModel;

namespace SIL.Motif.Commands;

/// <summary>The fields retained by a collected App change, plus the Assessment that supplied its reading.</summary>
public sealed record CollectedChangeRequest(
    string FwDataPath, string ProductVersion, string DraftName,
    string Kind, string Word, string Reading, string? AssessmentId);

/// <summary>Removes one word's collected change from the persistent Draft.</summary>
public sealed record RemoveCollectedChangeRequest(
    string FwDataPath, string ProductVersion, string DraftName, string Word);

/// <summary>Removes every collected change that no longer fits the live project.</summary>
public sealed record RemoveNonFittingChangesRequest(
    string FwDataPath, string ProductVersion, string DraftName);

/// <summary>The saved Draft after one change was added, replaced, or removed.</summary>
public sealed record CollectedChangeResponse(string DraftName, string ProposalId, int OperationCount);

/// <summary>Maps individual Changes entries into one durable Draft in the paired project store.</summary>
public static class AnalysisDraftChanges
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public static CommandOutcome<CollectedChangeResponse> AddOrReplace(CollectedChangeRequest request) =>
        ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion, (database, project) =>
        {
            try
            {
                var baseline = new BaselineRepository(database)
                    .GetCurrent(ProjectWorkspaceKey.Compute(project));
                if (baseline is null)
                    throw new InvalidOperationException("Capture a Baseline before collecting changes.");
                var repository = new ProposalRepository(database);
                var exists = repository.DraftNameExists(request.DraftName);
                var draft = exists
                    ? ReadDraft(repository, request.DraftName)
                    : new DraftDocument { ProposalId = CanonicalId.Mint().Value };
                using var cache = new FwDataProjectLoader().LoadScratchCache(project.FullFwDataPath);
                var matches = cache.ServiceLocator.GetInstance<IWfiWordformRepository>().AllInstances()
                    .Where(wordform => string.Equals(
                        wordform.Form.VernacularDefaultWritingSystem?.Text, request.Word, StringComparison.Ordinal))
                    .Take(2).ToArray();
                if (matches.Length != 1)
                    throw new InvalidOperationException($"Expected one wordform for '{request.Word}', found {matches.Length}.");
                var wordform = matches[0];
                var wordformId = CanonicalId.FromGuid(wordform.Guid);
                var (reading, readingBaseline) = ReadFirstReading(database, request);
                var operations = AnalysisChangeComposer.Build(cache,
                    new AnalysisChangeIntent(request.Kind, wordformId, reading));
                var baselineToken = readingBaseline ?? JsonSerializer.Serialize(baseline.Token, JsonOptions);
                draft.Operations.RemoveAll(operation => FitsWord(operation, request.Word));
                draft.ComposerProvenance.RemoveAll(entry => ProvenanceFitsWord(entry, request.Word));
                foreach (var operation in operations)
                {
                    var analysisId = operation.Target is { } target &&
                        wordform.AnalysesOC.Any(analysis => analysis.Guid == target.ToGuid())
                            ? target.Value : null;
                    var analysis = analysisId is null ? null : wordform.AnalysesOC.Single(item =>
                        CanonicalId.FromGuid(item.Guid).Value == analysisId);
                    var readingDigest = reading is null ? null : ChangeFitPreflight.ReadingDigest(reading);
                    var fingerprint = new ChangeFitFingerprint(
                        wordformId.Value, analysisId, request.Word, baselineToken,
                        analysis is null ? null : ChangeFitPreflight.ContentDigest(analysis), readingDigest, reading);
                    draft.Operations.Add(ToDraft(operation, fingerprint));
                    draft.ContractVersions[OperationKind.GetGroup(operation.Kind)] = "1.0";
                }
                if (operations.Count > 0)
                    draft.ComposerProvenance.Add(JsonSerializer.SerializeToElement(new
                    {
                        composer = "AnalysisChange",
                        wordformId = wordformId.Value,
                        word = request.Word,
                        kind = request.Kind,
                        assessmentId = request.AssessmentId,
                        reading,
                    }, JsonOptions));
                if (exists) repository.SaveDraft(request.DraftName, JsonSerializer.Serialize(draft, JsonOptions));
                else repository.CreateDraft(request.DraftName, CanonicalId.Parse(draft.ProposalId),
                    JsonSerializer.Serialize(draft, JsonOptions));
                return CommandOutcome<CollectedChangeResponse>.Success(new CollectedChangeResponse(
                    request.DraftName, draft.ProposalId, draft.Operations.Count));
            }
            catch (Exception ex)
            {
                return CommandOutcome<CollectedChangeResponse>.Refused(new Refusal(
                    "change.cannot-collect", FailureReason.Refused, ex.Message));
            }
        });

    public static CommandOutcome<CollectedChangeResponse> Remove(RemoveCollectedChangeRequest request) =>
        ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion, (database, _) =>
        {
            try
            {
                var repository = new ProposalRepository(database);
                var draft = ReadDraft(repository, request.DraftName);
                draft.Operations.RemoveAll(operation => FitsWord(operation, request.Word));
                draft.ComposerProvenance.RemoveAll(entry => ProvenanceFitsWord(entry, request.Word));
                repository.SaveDraft(request.DraftName, JsonSerializer.Serialize(draft, JsonOptions));
                return CommandOutcome<CollectedChangeResponse>.Success(new CollectedChangeResponse(
                    request.DraftName, draft.ProposalId, draft.Operations.Count));
            }
            catch (Exception ex)
            {
                return CommandOutcome<CollectedChangeResponse>.Refused(new Refusal(
                    "change.cannot-remove", FailureReason.Refused, ex.Message));
            }
        });

    public static CommandOutcome<CollectedChangeResponse> RemoveNoLongerFitting(
        RemoveNonFittingChangesRequest request) =>
        ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion, (database, project) =>
        {
            try
            {
                var repository = new ProposalRepository(database);
                var draft = ReadDraft(repository, request.DraftName);
                var proposal = ProposalJsonParser.Parse(ProposalCommands.BuildProposalJson(draft));
                using var cache = new FwDataProjectLoader().LoadScratchCache(project.FullFwDataPath);
                var failing = ChangeFitPreflight.Check(cache, proposal).Where(change => !change.StillFits)
                    .Select(change => change.OperationId).ToHashSet(StringComparer.Ordinal);
                if (draft.Operations.Any(operation => failing.Contains(operation.OperationId) &&
                    OperationDependencyGraph.IsCascadingDelete(operation.Kind)))
                    throw new InvalidOperationException("A cascading delete cannot be removed automatically.");
                foreach (var dependent in OperationDependencyGraph.TransitiveDependents(draft.Operations, failing))
                    failing.Add(dependent.DependentOperationId);
                var removedWords = draft.Operations.Where(operation => failing.Contains(operation.OperationId))
                    .Select(FingerprintWord).Where(word => word is not null).ToHashSet(StringComparer.Ordinal);
                draft.Operations.RemoveAll(operation => failing.Contains(operation.OperationId));
                draft.ComposerProvenance.RemoveAll(entry =>
                    entry.TryGetProperty("word", out var word) && removedWords.Contains(word.GetString()));
                repository.SaveDraft(request.DraftName, JsonSerializer.Serialize(draft, JsonOptions));
                return CommandOutcome<CollectedChangeResponse>.Success(new CollectedChangeResponse(
                    request.DraftName, draft.ProposalId, draft.Operations.Count));
            }
            catch (Exception ex)
            {
                return CommandOutcome<CollectedChangeResponse>.Refused(new Refusal(
                    "change.cannot-remove-nonfitting", FailureReason.Refused, ex.Message));
            }
        });

    public static CommandOutcome<PreflightResponse> PreflightDraft(RemoveNonFittingChangesRequest request) =>
        ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion, (database, project) =>
        {
            try
            {
                var draft = ReadDraft(new ProposalRepository(database), request.DraftName);
                var proposal = ProposalJsonParser.Parse(ProposalCommands.BuildProposalJson(draft));
                using var cache = new FwDataProjectLoader().LoadScratchCache(project.FullFwDataPath);
                return CommandOutcome<PreflightResponse>.Success(new PreflightResponse(
                    draft.ProposalId, ChangeFitPreflight.Check(cache, proposal)));
            }
            catch (Exception ex)
            {
                return CommandOutcome<PreflightResponse>.Refused(new Refusal(
                    "change.preflight-unavailable", FailureReason.Refused, ex.Message));
            }
        });

    private static DraftDocument ReadDraft(ProposalRepository repository, string name) =>
        JsonSerializer.Deserialize<DraftDocument>(repository.GetDraft(name).ProposalJson!, JsonOptions)
        ?? throw new InvalidDataException($"Draft '{name}' has no content.");

    private static bool FitsWord(DraftOperation operation, string word)
    {
        if (operation.Extensions is not { } extensions ||
            !extensions.TryGetProperty("changeFit", out var fit)) return false;
        return fit.TryGetProperty("wordformForm", out var form) && form.GetString() == word;
    }

    private static string? FingerprintWord(DraftOperation operation)
    {
        if (operation.Extensions is not { } extensions ||
            !extensions.TryGetProperty("changeFit", out var fit) ||
            !fit.TryGetProperty("wordformForm", out var form)) return null;
        return form.GetString();
    }

    private static bool ProvenanceFitsWord(JsonElement provenance, string word) =>
        provenance.ValueKind == JsonValueKind.Object &&
        provenance.TryGetProperty("word", out var value) && value.GetString() == word;

    private static (ParseAnalysis? Reading, string? BaselineToken) ReadFirstReading(
        MotifDatabase database, CollectedChangeRequest request)
    {
        if (request.Kind == "incorrect-spelling") return (null, null);
        if (request.AssessmentId is null)
            throw new InvalidOperationException("An Assessment is required to identify the parser reading.");
        var assessment = new AssessmentRepository(database).Get(request.AssessmentId);
        var matches = assessment.Words?.Where(word => word.Word == request.Word).Take(2).ToArray() ?? [];
        if (matches.Length != 1)
            throw new InvalidOperationException($"Assessment has no unique reading for '{request.Word}'.");
        var reading = matches[0].Morphology?.Analyses.FirstOrDefault()
            ?? throw new InvalidOperationException($"Assessment has no parser reading for '{request.Word}'.");
        return (reading, assessment.BaselineToken);
    }

    private static DraftOperation ToDraft(OperationEnvelope operation, ChangeFitFingerprint fingerprint)
    {
        var after = operation.After ?? throw new InvalidDataException("Composed operation has no payload.");
        return new DraftOperation
        {
            OperationId = operation.OperationId.Value,
            Kind = operation.Kind,
            Target = operation.Target?.Value,
            EntityId = operation.EntityId?.Value,
            DependsOn = operation.DependsOn.Select(dependency => dependency.OperationId.Value).ToList(),
            After = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(after.GetRawText())!,
            Extensions = JsonSerializer.SerializeToElement(new { changeFit = fingerprint }, JsonOptions),
        };
    }
}
