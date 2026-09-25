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
    public Refusal? ApplyPendingRefusal { get; set; }
    public Refusal? MeasurePendingRefusal { get; set; }
    public Func<ApplyPendingRequest, CancellationToken, Task<CommandOutcome<ApplyPendingResult>>>? ApplyPendingHandler
        { get; set; }

    public void MeasurePendingCompletesWith(MeasurePendingResult result) => _measurement = result;
    public void ApplyPendingCompletesWith(ApplyProjection result) => _apply = result;

    public Task<CommandOutcome<ApplyPendingResult>> ApplyPendingAsync(
        ApplyPendingRequest request, CancellationToken cancellationToken)
    {
        ApplyPendingRequests.Add(request);
        if (ApplyPendingHandler is { } handler) return handler(request, cancellationToken);
        if (ApplyPendingRefusal is { } refusal)
            return Task.FromResult(CommandOutcome<ApplyPendingResult>.Refused(refusal));
        if (_apply is not { } result) throw NotConfigured(nameof(ApplyPendingAsync));
        _pending = new PendingChangesSnapshot(null, "none", [], []);
        return Completed(new ApplyPendingResult(true, result));
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

    public Refusal? PendingPutRefusal { get; set; }
    public int? PendingPutRefusalOnCall { get; set; }
    public PendingChangesSnapshot? PendingPutResponse { get; set; }

    public List<PutPendingChangeRequest> PendingPutRequests { get; } = [];
    public List<RecheckPendingChangesRequest> PendingRecheckRequests { get; } = [];
    private PendingChangesSnapshot? _recheckResponse;

    public void RecheckCompletesWith(PendingChangesSnapshot response) => _recheckResponse = response;

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
        return Completed(_pending);
    }

    public Task<CommandOutcome<PendingChangesSnapshot>> PutPendingChangeAsync(
        PutPendingChangeRequest request, CancellationToken cancellationToken)
    {
        PendingPutRequests.Add(request);
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
        });
        _pending = _pending with { DraftId = _pending.DraftId ?? "draft/test", Revision = Guid.NewGuid().ToString("N"),
            Changes = changes, FitSummary = changes.Select(item => new ChangeFit(item.ChangeId, true, [])).ToArray() };
        return Completed(_pending);
    }

    public Task<CommandOutcome<PendingChangesSnapshot>> RemovePendingChangeAsync(
        RemovePendingChangeRequest request, CancellationToken cancellationToken)
    {
        _pending = _pending with { Revision = Guid.NewGuid().ToString("N"),
            Changes = _pending.Changes.Where(item => item.ChangeId != request.ChangeId).ToArray(),
            FitSummary = _pending.FitSummary.Where(item => item.ChangeId != request.ChangeId).ToArray() };
        return Completed(_pending);
    }
}
