using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.Services;

public partial interface ICommandClient
{
    Task<CommandOutcome<ReviewTrialResult>> RunReviewTrialAsync(
        ReviewTrialRequest request, IProgress<ReviewTrialProgress> progress, CancellationToken cancellationToken);
    Task<CommandOutcome<ApplyProjection>> ApplyReviewAsync(
        ReviewApplyRequest request, CancellationToken cancellationToken);
    Task<CommandOutcome<PendingChangesSnapshot>> LoadPendingChangesAsync(
        PendingChangesRequest request, CancellationToken cancellationToken);

    Task<CommandOutcome<PendingChangesSnapshot>> PutPendingChangeAsync(
        PutPendingChangeRequest request, CancellationToken cancellationToken);

    Task<CommandOutcome<PendingChangesSnapshot>> RemovePendingChangeAsync(
        RemovePendingChangeRequest request, CancellationToken cancellationToken);

    Task<CommandOutcome<PendingChangesSnapshot>> RecheckPendingChangesAsync(
        RecheckPendingChangesRequest request, CancellationToken cancellationToken);
}
