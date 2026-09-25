using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Tests.App.Walkthrough;

internal sealed partial class HoldingCommandClient
{
    public Task<CommandOutcome<PendingChangesSnapshot>> LoadPendingChangesAsync(
        PendingChangesRequest request, CancellationToken cancellationToken) =>
        _inner.LoadPendingChangesAsync(request, cancellationToken);

    public Task<CommandOutcome<PendingChangesSnapshot>> PutPendingChangeAsync(
        PutPendingChangeRequest request, CancellationToken cancellationToken) =>
        _inner.PutPendingChangeAsync(request, cancellationToken);

    public Task<CommandOutcome<PendingChangesSnapshot>> RemovePendingChangeAsync(
        RemovePendingChangeRequest request, CancellationToken cancellationToken) =>
        _inner.RemovePendingChangeAsync(request, cancellationToken);

    public Task<CommandOutcome<PendingChangesSnapshot>> RecheckPendingChangesAsync(
        RecheckPendingChangesRequest request, CancellationToken cancellationToken) =>
        _inner.RecheckPendingChangesAsync(request, cancellationToken);
}
