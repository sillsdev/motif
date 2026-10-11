using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Tests.App;

public sealed partial class FakeCommandClient
{
    public List<ProposalReviewRequest> ProposalReviewRequests { get; } = [];
    public List<ReviewMarkRequest> ReviewMarkRequests { get; } = [];
    public List<RemoveOperationsRequest> RemoveOperationsRequests { get; } = [];
    public List<ReopenRequest> ReopenRequests { get; } = [];
    public List<ApplyProposalRequest> ApplyProposalRequests { get; } = [];

    /// <summary>The review list the fake returns; marks and removals edit it as the real commands would.</summary>
    public ProposalReviewResponse Review { get; set; } = new("", "", "none", [], null, null, [], 0);

    public Refusal? RemoveOperationsRefusal { get; set; }

    public Func<ApplyProposalRequest, CancellationToken, Task<CommandOutcome<ApplyPendingResult>>>?
        ApplyProposalHandler { get; set; }

    public Task<CommandOutcome<ProposalReviewResponse>> ReadProposalReviewAsync(
        ProposalReviewRequest request, CancellationToken cancellationToken)
    {
        ProposalReviewRequests.Add(request);
        return Completed(Review);
    }

    public Task<CommandOutcome<ReviewMarkResponse>> MarkLookedAtAsync(
        ReviewMarkRequest request, CancellationToken cancellationToken)
    {
        ReviewMarkRequests.Add(request);
        var entries = Review.Entries.Select(entry => entry.EntryKey == request.EntryKey
            ? Marked(entry, request.LookedAt) : entry).ToArray();
        Review = Review with { Entries = entries, LookedAtCount = entries.Count(entry => entry.LookedAt) };
        return Completed(new ReviewMarkResponse(request.ProposalId, request.EntryKey, request.LookedAt));
    }

    public Task<CommandOutcome<OperationsRemovedResponse>> RemoveOperationsAsync(
        RemoveOperationsRequest request, CancellationToken cancellationToken)
    {
        RemoveOperationsRequests.Add(request);
        if (RemoveOperationsRefusal is { } refusal)
            return Task.FromResult(CommandOutcome<OperationsRemovedResponse>.Refused(refusal));
        var removed = request.OperationIds.ToHashSet(StringComparer.Ordinal);
        var entries = Review.Entries.Where(entry => !entry.OperationIds.Any(removed.Contains))
            .Select((entry, index) => entry with { Ordinal = index }).ToArray();
        Review = Review with
        {
            Entries = entries, LookedAtCount = entries.Count(entry => entry.LookedAt), Revision = "sha256:after-undo",
            IsDraft = true, DraftName = request.DraftName,
        };
        return Completed(new OperationsRemovedResponse(request.DraftName, request.OperationIds, [], entries.Length));
    }

    public Task<CommandOutcome<ReopenedResponse>> ReopenAsync(ReopenRequest request, CancellationToken cancellationToken)
    {
        ReopenRequests.Add(request);
        Review = Review with { IsDraft = true, DraftName = request.DraftName };
        return Completed(new ReopenedResponse(request.ProposalId, request.DraftName, "digest", Review.Entries.Count));
    }

    public Task<CommandOutcome<ApplyPendingResult>> ApplyProposalAsync(
        ApplyProposalRequest request, CancellationToken cancellationToken)
    {
        ApplyProposalRequests.Add(request);
        return ApplyProposalHandler?.Invoke(request, cancellationToken)
            ?? throw NotConfigured(nameof(ApplyProposalAsync));
    }

    public List<DeclineProposalRequest> DeclineProposalRequests { get; } = [];

    public List<ReopenProposalRequest> ReopenProposalRequests { get; } = [];

    public List<DiscardDraftRequest> DiscardDraftRequests { get; } = [];

    public Refusal? DiscardDraftRefusal { get; set; }

    public Task<CommandOutcome<ProposalStatusChangedResponse>> DeclineProposalAsync(
        DeclineProposalRequest request, CancellationToken cancellationToken)
    {
        DeclineProposalRequests.Add(request);
        return Completed(new ProposalStatusChangedResponse(request.ProposalId, "rejected", null));
    }

    public Task<CommandOutcome<ReopenedResponse>> ReopenProposalAsync(
        ReopenProposalRequest request, CancellationToken cancellationToken)
    {
        ReopenProposalRequests.Add(request);
        return Completed(new ReopenedResponse(request.ProposalId, "review/" + request.ProposalId, "digest", 1));
    }

    public Task<CommandOutcome<DraftDiscardedResponse>> DiscardDraftAsync(
        DiscardDraftRequest request, CancellationToken cancellationToken)
    {
        DiscardDraftRequests.Add(request);
        if (DiscardDraftRefusal is { } refusal)
            return Task.FromResult(CommandOutcome<DraftDiscardedResponse>.Refused(refusal));
        OnDraftDiscarded?.Invoke(request);
        return Completed(new DraftDiscardedResponse(request.DraftName, false));
    }

    /// <summary>Runs after a discard succeeds, so a test can empty the pending changes the fake returns.</summary>
    public Action<DiscardDraftRequest>? OnDraftDiscarded { get; set; }

    private static ReviewEntry Marked(ReviewEntry entry, bool lookedAt) => entry switch
    {
        WordChangeEntry word => word with { LookedAt = lookedAt },
        GrammarChangeEntry grammar => grammar with { LookedAt = lookedAt },
        _ => entry,
    };
}
