using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Tests.App;

public sealed partial class FakeCommandClient
{
    private PendingChangesSnapshot _pending = new(null, "none", [], []);

    public void PendingChangesIs(PendingChangesSnapshot snapshot) => _pending = snapshot;

    public Task<CommandOutcome<PendingChangesSnapshot>> LoadPendingChangesAsync(
        PendingChangesRequest request, CancellationToken cancellationToken) => Completed(_pending);

    public Task<CommandOutcome<PendingChangesSnapshot>> PutPendingChangeAsync(
        PutPendingChangeRequest request, CancellationToken cancellationToken)
    {
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
