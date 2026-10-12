using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.Services;

public partial interface ICommandClient
{
    Task<CommandOutcome<MeasurePendingResult>> MeasurePendingAsync(
        MeasurePendingRequest request, IProgress<MeasureProgress> progress, CancellationToken cancellationToken);
    Task<CommandOutcome<ApplyPendingResult>> ApplyPendingAsync(
        ApplyPendingRequest request, CancellationToken cancellationToken);
    Task<CommandOutcome<PendingChangesSnapshot>> LoadPendingChangesAsync(
        PendingChangesRequest request, CancellationToken cancellationToken);

    Task<CommandOutcome<PendingChangesSnapshot>> PutPendingChangeAsync(
        PutPendingChangeRequest request, CancellationToken cancellationToken);

    Task<CommandOutcome<PendingChangesSnapshot>> RemovePendingChangeAsync(
        RemovePendingChangeRequest request, CancellationToken cancellationToken);

    Task<CommandOutcome<PendingChangesSnapshot>> RemoveAnalysisAsync(
        RemoveAnalysisRequest request, CancellationToken cancellationToken);

    Task<CommandOutcome<PendingChangesSnapshot>> AcceptNewSetAsync(
        AcceptNewSetRequest request, CancellationToken cancellationToken);

    Task<CommandOutcome<PendingChangesSnapshot>> RecheckPendingChangesAsync(
        RecheckPendingChangesRequest request, CancellationToken cancellationToken);

    Task<CommandOutcome<PendingChangesSnapshot>> ReconfirmPendingChangeAsync(
        ReconfirmPendingChangeRequest request, CancellationToken cancellationToken);

    Task<CommandOutcome<ProposalReviewResponse>> ReadProposalReviewAsync(
        ProposalReviewRequest request, CancellationToken cancellationToken);

    Task<CommandOutcome<ProposalReviewEntryCountResponse>> ReadProposalReviewEntryCountAsync(
        ProposalReviewRequest request, CancellationToken cancellationToken);

    Task<CommandOutcome<ReviewMarkResponse>> MarkLookedAtAsync(
        ReviewMarkRequest request, CancellationToken cancellationToken);

    Task<CommandOutcome<OperationsRemovedResponse>> RemoveOperationsAsync(
        RemoveOperationsRequest request, CancellationToken cancellationToken);

    Task<CommandOutcome<ReopenedResponse>> ReopenAsync(
        ReopenRequest request, CancellationToken cancellationToken);

    Task<CommandOutcome<ApplyPendingResult>> ApplyProposalAsync(
        ApplyProposalRequest request, CancellationToken cancellationToken);

    /// <summary>Declines a Proposal; it is kept with its Trials as history.</summary>
    Task<CommandOutcome<ProposalStatusChangedResponse>> DeclineProposalAsync(
        DeclineProposalRequest request, CancellationToken cancellationToken);

    /// <summary>Opens a declined or withdrawn Proposal again as a Draft under the same id.</summary>
    Task<CommandOutcome<ReopenedResponse>> ReopenProposalAsync(
        ReopenProposalRequest request, CancellationToken cancellationToken);

    /// <summary>Discards a Draft, the pending changes included, if it still holds the revision read.</summary>
    Task<CommandOutcome<DraftDiscardedResponse>> DiscardDraftAsync(
        DiscardDraftRequest request, CancellationToken cancellationToken);
}
