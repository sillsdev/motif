using SIL.Motif.Commands;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.Services;

public sealed partial class CommandClient
{
    public Task<CommandOutcome<ApplyPendingResult>> ApplyPendingAsync(
        ApplyPendingRequest request, CancellationToken cancellationToken) =>
        // The workflow finalizes the changes before it looks at the token, so a cancelled call never starts it.
        cancellationToken.IsCancellationRequested
            ? Task.FromResult(WaitCancelled<ApplyPendingResult>())
            : OneAtATime(() => PendingChangesWorkflow.Apply(request, cancellationToken,
                runnerLauncher: _options.RunnerLauncher), cancellationToken);

    public Task<CommandOutcome<MeasurePendingResult>> MeasurePendingAsync(
        MeasurePendingRequest request, IProgress<MeasureProgress> progress, CancellationToken cancellationToken) =>
        PendingChangesWorkflow.Measure(request, progress, cancellationToken,
            runnerLauncher: _options.RunnerLauncher);

    public Task<CommandOutcome<PendingChangesSnapshot>> LoadPendingChangesAsync(
        PendingChangesRequest request, CancellationToken cancellationToken) =>
        OneAtATime(() => PendingChanges.Load(request), cancellationToken);

    public Task<CommandOutcome<PendingChangesSnapshot>> PutPendingChangeAsync(
        PutPendingChangeRequest request, CancellationToken cancellationToken) =>
        OneAtATime(() => PendingChanges.Put(request), cancellationToken);

    public Task<CommandOutcome<PendingChangesSnapshot>> RemovePendingChangeAsync(
        RemovePendingChangeRequest request, CancellationToken cancellationToken) =>
        OneAtATime(() => PendingChanges.Remove(request), cancellationToken);

    public Task<CommandOutcome<PendingChangesSnapshot>> RecheckPendingChangesAsync(
        RecheckPendingChangesRequest request, CancellationToken cancellationToken) =>
        OneAtATime(() => PendingChanges.Recheck(request), cancellationToken);
}
