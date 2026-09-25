using SIL.Motif.App.Services;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Tests.App;

public sealed partial class FakeCommandClient
{
    public List<ReviewTrialRequest> ReviewTrialRequests { get; } = [];
    public List<ReviewApplyRequest> ReviewApplyRequests { get; } = [];
    private ReviewTrialResult? _reviewTrial;
    private ApplyProjection? _reviewApply;

    public void ReviewTrialCompletesWith(ReviewTrialResult result) => _reviewTrial = result;
    public void ReviewApplyCompletesWith(ApplyProjection result) => _reviewApply = result;

    public Task<CommandOutcome<ApplyProjection>> ApplyReviewAsync(
        ReviewApplyRequest request, CancellationToken cancellationToken)
    {
        ReviewApplyRequests.Add(request);
        if (_reviewApply is not { } result) throw NotConfigured(nameof(ApplyReviewAsync));
        _pending = new PendingChangesSnapshot(null, "none", [], []);
        return Completed(result);
    }

    public Task<CommandOutcome<ReviewTrialResult>> RunReviewTrialAsync(
        ReviewTrialRequest request, IProgress<ReviewTrialProgress> progress, CancellationToken cancellationToken)
    {
        ReviewTrialRequests.Add(request);
        return _reviewTrial is { } result ? Completed(result) : throw NotConfigured(nameof(RunReviewTrialAsync));
    }
    private PendingChangesSnapshot _pending = new(null, "none", [], []);

    public Refusal? PendingPutRefusal { get; set; }
    public int? PendingPutRefusalOnCall { get; set; }

    public List<PutPendingChangeRequest> PendingPutRequests { get; } = [];

    public void PendingChangesIs(PendingChangesSnapshot snapshot) => _pending = snapshot;

    public Task<CommandOutcome<PendingChangesSnapshot>> LoadPendingChangesAsync(
        PendingChangesRequest request, CancellationToken cancellationToken) => Completed(_pending);

    public Task<CommandOutcome<PendingChangesSnapshot>> PutPendingChangeAsync(
        PutPendingChangeRequest request, CancellationToken cancellationToken)
    {
        PendingPutRequests.Add(request);
        if (PendingPutRefusal is { } refusal &&
            (PendingPutRefusalOnCall is null || PendingPutRefusalOnCall == PendingPutRequests.Count))
            return Task.FromResult(CommandOutcome<PendingChangesSnapshot>.Refused(refusal));
        var change = request.Change;
        var changes = _pending.Changes.Where(item => item.ChangeId != change.ChangeId).ToList();
        changes.Add(new PendingChange(change.ChangeId, change.WordformId, change.Word, change.Kind,
            change.AssessmentId, change.DisplayReading, [change.ChangeId]));
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
