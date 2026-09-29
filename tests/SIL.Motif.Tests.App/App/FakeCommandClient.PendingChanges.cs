using SIL.Motif.App.Services;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Tests.App;

public sealed partial class FakeCommandClient
{
    public List<MeasurePendingRequest> MeasurePendingRequests { get; } = [];
    public List<ApplyPendingRequest> ApplyPendingRequests { get; } = [];
    private MeasurePendingResult? _measurement;
    private ApplyProjection? _apply;
    private string? _applySummary;
    public Refusal? ApplyPendingRefusal { get; set; }
    public Refusal? MeasurePendingRefusal { get; set; }
    public Func<ApplyPendingRequest, CancellationToken, Task<CommandOutcome<ApplyPendingResult>>>? ApplyPendingHandler
        { get; set; }

    public void MeasurePendingCompletesWith(MeasurePendingResult result) => _measurement = result;

    public static ReviewNumbersResponse CompleteNumbers { get; } =
        new(ReviewComparability.Compared, 1, 0, 1, 1, 1, EvidenceComplete: true);
    public void ApplyPendingCompletesWith(ApplyProjection result, string summary)
    {
        _apply = result;
        _applySummary = summary;
    }

    public Task<CommandOutcome<ApplyPendingResult>> ApplyPendingAsync(
        ApplyPendingRequest request, CancellationToken cancellationToken)
    {
        ApplyPendingRequests.Add(request);
        if (ApplyPendingHandler is { } handler) return handler(request, cancellationToken);
        if (ApplyPendingRefusal is { } refusal)
            return Task.FromResult(CommandOutcome<ApplyPendingResult>.Refused(refusal));
        if (_apply is not { } result || _applySummary is not { } summary)
            throw NotConfigured(nameof(ApplyPendingAsync));
        _pending = new PendingChangesSnapshot(null, "none", [], []);
        return Completed(ApplyPendingResult.AppliedWith(result, summary));
    }

    public Task<CommandOutcome<MeasurePendingResult>> MeasurePendingAsync(
        MeasurePendingRequest request, IProgress<MeasureProgress> progress, CancellationToken cancellationToken)
    {
        MeasurePendingRequests.Add(request);
        if (MeasurePendingRefusal is { } refusal)
            return Task.FromResult(CommandOutcome<MeasurePendingResult>.Refused(refusal));
        return _measurement is { } result ? Completed(result) : throw NotConfigured(nameof(MeasurePendingAsync));
    }
    private PendingChangesSnapshot _pending = new(null, "none", [], []);
    public List<PendingChangesRequest> PendingLoadRequests { get; } = [];
    public List<RemovePendingChangeRequest> PendingRemoveRequests { get; } = [];
    public List<RemoveAnalysisRequest> RemoveAnalysisRequests { get; } = [];
    public List<AcceptNewSetRequest> AcceptNewSetRequests { get; } = [];

    public Refusal? PendingPutRefusal { get; set; }
    public int? PendingPutRefusalOnCall { get; set; }
    public PendingChangesSnapshot? PendingPutResponse { get; set; }
    public Func<PendingChangesRequest, CancellationToken, Task<CommandOutcome<PendingChangesSnapshot>>>?
        PendingLoadHandler { get; set; }
    public Func<PutPendingChangeRequest, CancellationToken, Task<CommandOutcome<PendingChangesSnapshot>>>?
        PendingPutHandler { get; set; }
    public Func<RemovePendingChangeRequest, CancellationToken, Task<CommandOutcome<PendingChangesSnapshot>>>?
        PendingRemoveHandler { get; set; }
    public Func<RemoveAnalysisRequest, CancellationToken, Task<CommandOutcome<PendingChangesSnapshot>>>?
        RemoveAnalysisHandler { get; set; }
    public Func<AcceptNewSetRequest, CancellationToken, Task<CommandOutcome<PendingChangesSnapshot>>>?
        AcceptNewSetHandler { get; set; }
    private PendingChangesSnapshot? _removeAnalysisResponse;
    private PendingChangesSnapshot? _acceptNewSetResponse;

    public void RemoveAnalysisCompletesWith(PendingChangesSnapshot response) => _removeAnalysisResponse = response;

    public void AcceptNewSetCompletesWith(PendingChangesSnapshot response) => _acceptNewSetResponse = response;

    public List<PutPendingChangeRequest> PendingPutRequests { get; } = [];
    public List<RemoveAnalysisRequest> AnalysisRemovalRequests { get; } = [];
    public List<AcceptNewSetRequest> AcceptNewSetRequests { get; } = [];
    public List<RecheckPendingChangesRequest> PendingRecheckRequests { get; } = [];
    public List<ReconfirmPendingChangeRequest> PendingReconfirmRequests { get; } = [];
    public PendingChangesSnapshot? AnalysisRemovalResponse { get; set; }
    public PendingChangesSnapshot? AcceptNewSetResponse { get; set; }
    public Func<RemoveAnalysisRequest, CancellationToken, Task<CommandOutcome<PendingChangesSnapshot>>>?
        AnalysisRemovalHandler { get; set; }
    public Func<AcceptNewSetRequest, CancellationToken, Task<CommandOutcome<PendingChangesSnapshot>>>?
        AcceptNewSetHandler { get; set; }
    private PendingChangesSnapshot? _recheckResponse;
    private PendingChangesSnapshot? _reconfirmResponse;

    public void RecheckCompletesWith(PendingChangesSnapshot response) => _recheckResponse = response;

    public void ReconfirmCompletesWith(PendingChangesSnapshot response) => _reconfirmResponse = response;

    public Task<CommandOutcome<PendingChangesSnapshot>> ReconfirmPendingChangeAsync(
        ReconfirmPendingChangeRequest request, CancellationToken cancellationToken)
    {
        PendingReconfirmRequests.Add(request);
        _pending = _reconfirmResponse ?? _pending;
        return Completed(_pending);
    }

    public Task<CommandOutcome<PendingChangesSnapshot>> RecheckPendingChangesAsync(
        RecheckPendingChangesRequest request, CancellationToken cancellationToken)
    {
        PendingRecheckRequests.Add(request);
        _pending = _recheckResponse ?? _pending;
        return Completed(_pending);
    }

    public void PendingChangesIs(PendingChangesSnapshot snapshot) => _pending = snapshot;

    public Task<CommandOutcome<PendingChangesSnapshot>> LoadPendingChangesAsync(
        PendingChangesRequest request, CancellationToken cancellationToken)
    {
        PendingLoadRequests.Add(request);
        if (PendingLoadHandler is { } handler) return handler(request, cancellationToken);
        return Completed(_pending);
    }

    public Task<CommandOutcome<PendingChangesSnapshot>> PutPendingChangeAsync(
        PutPendingChangeRequest request, CancellationToken cancellationToken)
    {
        PendingPutRequests.Add(request);
        if (PendingPutHandler is { } handler) return handler(request, cancellationToken);
        if (PendingPutRefusal is { } refusal &&
            (PendingPutRefusalOnCall is null || PendingPutRefusalOnCall == PendingPutRequests.Count))
            return Task.FromResult(CommandOutcome<PendingChangesSnapshot>.Refused(refusal));
        if (PendingPutResponse is { } response)
        {
            _pending = response;
            PendingPutResponse = null;
            return Completed(response);
        }
        var change = request.Change;
        var changes = _pending.Changes.Where(item => item.ChangeId != change.ChangeId).ToList();
        changes.Add(new PendingChange(change.ChangeId, change.WordformId, change.Word, change.Kind,
            change.AssessmentId, change.DisplayReading, [change.ChangeId])
        {
            OriginPage = change.OriginPage,
            Occurrence = change.Occurrence,
            StoredAnalysisId = change.StoredAnalysisId,
        });
        _pending = _pending with { DraftId = _pending.DraftId ?? "draft/test", Revision = Guid.NewGuid().ToString("N"),
            Changes = changes, FitSummary = changes.Select(item => new ChangeFit(item.ChangeId, true, [])).ToArray() };
        return Completed(_pending);
    }

    public Task<CommandOutcome<PendingChangesSnapshot>> RemoveAnalysisAsync(
        RemoveAnalysisRequest request, CancellationToken cancellationToken)
    {
        AnalysisRemovalRequests.Add(request);
        if (AnalysisRemovalHandler is { } handler) return handler(request, cancellationToken);
        if (request.AnalysisId is { } analysisId && request.ChangeId is { } changeId &&
            request.WordformId is { } wordformId && request.Word is { } word)
            return PutPendingChangeAsync(new PutPendingChangeRequest(request.FwDataPath, request.ProductVersion,
                request.ExpectedRevision, new ChangeIntent(changeId, "remove-analysis", wordformId, word,
                    StoredAnalysisId: analysisId)), cancellationToken);
        if (AnalysisRemovalResponse is { } response)
        {
            _pending = response;
            return Completed(response);
        }
        throw NotConfigured(nameof(RemoveAnalysisAsync));
    }

    public Task<CommandOutcome<PendingChangesSnapshot>> AcceptNewSetAsync(
        AcceptNewSetRequest request, CancellationToken cancellationToken)
    {
        AcceptNewSetRequests.Add(request);
        if (AcceptNewSetHandler is { } handler) return handler(request, cancellationToken);
        if (AcceptNewSetResponse is { } response)
        {
            _pending = response;
            return Completed(response);
        }
        throw NotConfigured(nameof(AcceptNewSetAsync));
    }

    public Task<CommandOutcome<PendingChangesSnapshot>> RemovePendingChangeAsync(
        RemovePendingChangeRequest request, CancellationToken cancellationToken)
    {
        PendingRemoveRequests.Add(request);
        if (PendingRemoveHandler is { } handler) return handler(request, cancellationToken);
        var changes = _pending.Changes.Where(item => item.ChangeId != request.ChangeId &&
            item.GroupId != request.ChangeId).ToArray();
        var changeIds = changes.Select(item => item.ChangeId).ToHashSet(StringComparer.Ordinal);
        _pending = _pending with { Revision = Guid.NewGuid().ToString("N"),
            Changes = changes,
            FitSummary = _pending.FitSummary.Where(item => changeIds.Contains(item.ChangeId)).ToArray() };
        return Completed(_pending);
    }

    public Task<CommandOutcome<PendingChangesSnapshot>> RemoveAnalysisAsync(
        RemoveAnalysisRequest request, CancellationToken cancellationToken)
    {
        RemoveAnalysisRequests.Add(request);
        if (RemoveAnalysisHandler is { } handler) return handler(request, cancellationToken);
        if (_removeAnalysisResponse is not { } response) throw NotConfigured(nameof(RemoveAnalysisAsync));
        _pending = response;
        return Completed(_pending);
    }

    public Task<CommandOutcome<PendingChangesSnapshot>> AcceptNewSetAsync(
        AcceptNewSetRequest request, CancellationToken cancellationToken)
    {
        AcceptNewSetRequests.Add(request);
        if (AcceptNewSetHandler is { } handler) return handler(request, cancellationToken);
        if (_acceptNewSetResponse is not { } response) throw NotConfigured(nameof(AcceptNewSetAsync));
        _pending = response;
        return Completed(_pending);
    }
}
