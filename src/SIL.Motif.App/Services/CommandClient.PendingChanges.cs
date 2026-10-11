using SIL.Motif.Commands;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Commands.Review;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.Services;

public sealed partial class CommandClient
{
    public Task<CommandOutcome<ApplyPendingResult>> ApplyPendingAsync(
        ApplyPendingRequest request, CancellationToken cancellationToken) =>
        // An already-cancelled token never starts the workflow.
        cancellationToken.IsCancellationRequested
            ? Task.FromResult(WaitCancelled<ApplyPendingResult>())
            : OneAtATime(() => PendingChangesWorkflow.Apply(request, cancellationToken,
                runnerLauncher: _options.RunnerLauncher), cancellationToken);

    public Task<CommandOutcome<MeasurePendingResult>> MeasurePendingAsync(
        MeasurePendingRequest request, IProgress<MeasureProgress> progress, CancellationToken cancellationToken) =>
        ParseOneAtATime(() => PendingChangesWorkflow.Measure(request, progress, cancellationToken,
            runnerLauncher: _options.RunnerLauncher));

    public Task<CommandOutcome<PendingChangesSnapshot>> LoadPendingChangesAsync(
        PendingChangesRequest request, CancellationToken cancellationToken) =>
        OneAtATime(() => PendingChanges.Load(request), cancellationToken);

    public Task<CommandOutcome<PendingChangesSnapshot>> PutPendingChangeAsync(
        PutPendingChangeRequest request, CancellationToken cancellationToken) =>
        OneAtATime(() => PendingChanges.Put(request), cancellationToken);

    public Task<CommandOutcome<PendingChangesSnapshot>> RemovePendingChangeAsync(
        RemovePendingChangeRequest request, CancellationToken cancellationToken) =>
        OneAtATime(() => PendingChanges.Remove(request), cancellationToken);

    public Task<CommandOutcome<PendingChangesSnapshot>> RemoveAnalysisAsync(
        RemoveAnalysisRequest request, CancellationToken cancellationToken) =>
        OneAtATime(() => PendingChanges.RemoveAnalysis(request), cancellationToken);

    public Task<CommandOutcome<PendingChangesSnapshot>> AcceptNewSetAsync(
        AcceptNewSetRequest request, CancellationToken cancellationToken) =>
        OneAtATime(() => PendingChanges.AcceptNewSet(request), cancellationToken);

    public Task<CommandOutcome<PendingChangesSnapshot>> RecheckPendingChangesAsync(
        RecheckPendingChangesRequest request, CancellationToken cancellationToken) =>
        OneAtATime(() => PendingChanges.Recheck(request), cancellationToken);

    public Task<CommandOutcome<PendingChangesSnapshot>> ReconfirmPendingChangeAsync(
        ReconfirmPendingChangeRequest request, CancellationToken cancellationToken) =>
        OneAtATime(() => PendingChanges.Reconfirm(request), cancellationToken);

    public Task<CommandOutcome<ProposalReviewResponse>> ReadProposalReviewAsync(
        ProposalReviewRequest request, CancellationToken cancellationToken) =>
        OneAtATime(() => ProposalReview.Read(request), cancellationToken);

    public Task<CommandOutcome<ReviewMarkResponse>> MarkLookedAtAsync(
        ReviewMarkRequest request, CancellationToken cancellationToken) =>
        OneAtATime(() => ReviewMarks.Set(request), cancellationToken);

    public Task<CommandOutcome<OperationsRemovedResponse>> RemoveOperationsAsync(
        RemoveOperationsRequest request, CancellationToken cancellationToken) =>
        OneAtATime(() => ProposalCommands.RemoveOperations(request), cancellationToken);

    public Task<CommandOutcome<ReopenedResponse>> ReopenAsync(
        ReopenRequest request, CancellationToken cancellationToken) =>
        OneAtATime(() => ProposalCommands.Reopen(request), cancellationToken);

    public Task<CommandOutcome<ApplyPendingResult>> ApplyProposalAsync(
        ApplyProposalRequest request, CancellationToken cancellationToken) =>
        cancellationToken.IsCancellationRequested
            ? Task.FromResult(WaitCancelled<ApplyPendingResult>())
            : OneAtATime(() => PendingChangesWorkflow.Apply(request, cancellationToken), cancellationToken);

    public Task<CommandOutcome<ProposalStatusChangedResponse>> DeclineProposalAsync(
        DeclineProposalRequest request, CancellationToken cancellationToken) =>
        OneAtATime(() => ProposalLifecycleCommand.Decline(request), cancellationToken);

    public Task<CommandOutcome<ReopenedResponse>> ReopenProposalAsync(
        ReopenProposalRequest request, CancellationToken cancellationToken) =>
        OneAtATime(() => ProposalLifecycleCommand.Reopen(request), cancellationToken);

    public Task<CommandOutcome<DraftDiscardedResponse>> DiscardDraftAsync(
        DiscardDraftRequest request, CancellationToken cancellationToken) =>
        OneAtATime(() => ProposalCommands.DiscardDraft(request), cancellationToken);
}
