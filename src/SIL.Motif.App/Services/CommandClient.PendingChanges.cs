using SIL.Motif.Commands;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.Services;

public sealed partial class CommandClient
{
    public Task<CommandOutcome<ApplyProjection>> ApplyPendingAsync(
        ApplyPendingRequest request, CancellationToken cancellationToken) =>
        OneAtATime(() => PendingChangesWorkflow.Apply(request, cancellationToken), cancellationToken,
            () => CommandOutcome<ApplyProjection>.Refused(new Refusal(
                "project.wait-cancelled", FailureReason.Cancelled, "Waiting to use the project was cancelled.")));

    public Task<CommandOutcome<MeasurePendingResult>> MeasurePendingAsync(
        MeasurePendingRequest request, IProgress<MeasureProgress> progress, CancellationToken cancellationToken) =>
        PendingChangesWorkflow.Measure(request, progress, cancellationToken);

    public Task<CommandOutcome<PendingChangesSnapshot>> LoadPendingChangesAsync(
        PendingChangesRequest request, CancellationToken cancellationToken) =>
        OneAtATime(() => PendingChanges.Load(request));

    public Task<CommandOutcome<PendingChangesSnapshot>> PutPendingChangeAsync(
        PutPendingChangeRequest request, CancellationToken cancellationToken) =>
        OneAtATime(() => PendingChanges.Put(request));

    public Task<CommandOutcome<PendingChangesSnapshot>> RemovePendingChangeAsync(
        RemovePendingChangeRequest request, CancellationToken cancellationToken) =>
        OneAtATime(() => PendingChanges.Remove(request));

    public Task<CommandOutcome<PendingChangesSnapshot>> RecheckPendingChangesAsync(
        RecheckPendingChangesRequest request, CancellationToken cancellationToken) =>
        OneAtATime(() => PendingChanges.Recheck(request));

}
