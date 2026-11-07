using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;

namespace SIL.Motif.App.ViewModels;

public sealed partial class ProposalsPageModel
{
    private int _detailGeneration;
    private string? _markingReadId;
    private string? _pendingUserSelectionId;
    private bool _selectionByUser;

    /// <summary>The selected Proposal's stored detail, if it could be read.</summary>
    [ObservableProperty]
    private ProposalDetailProjection? _detail;

    /// <summary>The selected Proposal's operation descriptions.</summary>
    [ObservableProperty]
    private IReadOnlyList<string> _operationDescriptions = [];

    /// <summary>The outcome of reading the selected Proposal's detail.</summary>
    [ObservableProperty]
    private WindowRefusal? _detailRefusal;

    /// <summary>The number of review entries looked at for the selected Proposal.</summary>
    [ObservableProperty]
    private string _reviewProgress = string.Empty;

    /// <summary>The most recent selected Proposal detail read.</summary>
    public Task DetailReading { get; private set; } = Task.CompletedTask;

    /// <summary>The latest request to record that a Proposal was viewed.</summary>
    public Task ReadMarkWriting { get; private set; } = Task.CompletedTask;

    private void ReadDetail(ProposalCardViewModel? card)
    {
        var generation = ++_detailGeneration;
        Detail = null;
        DetailRefusal = null;
        OperationDescriptions = [];
        ReviewProgress = string.Empty;
        if (card is null || Context.ProjectPath is not { } path) return;
        DetailReading = ReadDetailAsync(path, card.ProposalId, generation);
    }

    private async Task ReadDetailAsync(string path, string proposalId, int generation)
    {
        var shown = await Context.Commands.ShowProposalAsync(
            new ShowProposalRequest(path, MotifProductVersion.CurrentText, proposalId), CancellationToken.None)
            .ConfigureAwait(true);
        if (generation != _detailGeneration || Context.ProjectPath != path) return;
        if (!shown.Succeeded)
        {
            DetailRefusal = WindowRefusal.From(shown.Refusal!);
            return;
        }
        Detail = shown.Value;
        MarkSelectedRead();
        var review = await Context.Commands.ReadProposalReviewAsync(new ProposalReviewRequest(path, proposalId),
            CancellationToken.None).ConfigureAwait(true);
        if (generation != _detailGeneration || Context.ProjectPath != path) return;
        var words = review.Succeeded ? review.Value!.Entries.OfType<WordChangeEntry>().ToArray() : [];
        if (review.Succeeded)
            ReviewProgress = string.Format(CultureInfo.CurrentCulture, MotifAppStrings.ProposalReviewProgressFormat,
                review.Value!.LookedAtCount, review.Value.Entries.Count);
        OperationDescriptions = Detail!.Operations.Select(operation =>
        {
            var count = words.Where(word => word.OperationIds.Contains(operation.OperationId))
                .Select(word => word.Change.WordformId).Distinct(StringComparer.Ordinal).Count();
            return ProposalOperationSentence.For(operation, count);
        }).ToArray();
    }

    private void MarkSelectedRead()
    {
        if (!_selectionByUser || Context.CurrentPage != WorkspacePage.Proposals || Context.ProjectPath is not { } path ||
            SelectedProposal is not { IsUnread: true } card || Detail?.ProposalId != card.ProposalId ||
            _markingReadId == card.ProposalId) return;
        _markingReadId = card.ProposalId;
        ReadMarkWriting = MarkReadAsync(path, card);
    }

    private async Task MarkReadAsync(string path, ProposalCardViewModel card)
    {
        // The mark names the revision that was read, so a later revision shows unread again.
        var digest = Detail?.CurrentIntentDigest ?? "none";
        var mark = await Context.Commands.MarkLookedAtAsync(new ReviewMarkRequest(path, card.ProposalId,
            ReviewMarkKeys.ProposalRead, digest, "none", true), CancellationToken.None).ConfigureAwait(true);
        if (_markingReadId == card.ProposalId) _markingReadId = null;
        if (Context.ProjectPath != path) return;
        if (mark.Succeeded)
        {
            _standings = _standings with
            {
                Proposals = _standings.Proposals.Select(item => item.ProposalId == card.ProposalId
                    ? item with { IsUnread = false } : item).ToArray(),
            };
            card.MarkRead();
            if (SelectedProposal?.ProposalId == card.ProposalId) SelectedProposal.MarkRead();
        }
        else if (SelectedProposal?.ProposalId == card.ProposalId) DetailRefusal = WindowRefusal.From(mark.Refusal!);
    }
}
