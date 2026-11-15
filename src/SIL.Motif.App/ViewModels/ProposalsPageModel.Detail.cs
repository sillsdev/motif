using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
    private CancellationTokenSource? _detailCancellation;
    private readonly List<Task> _detailReads = [];
    private RationaleEditorModel? _lastRationaleEditor;
    private ObservableCollection<RationaleEditorModel>? _lastComments;

    /// <summary>The selected Proposal's Comments in rich fields.</summary>
    [ObservableProperty]
    private ObservableCollection<RationaleEditorModel> _comments = [];

    /// <summary>Whether a Comment can be added to the selected Proposal.</summary>
    public bool CanAddComment => Detail is { } detail && (detail.DraftName is not null ||
        detail.Status is not ("applied" or "rejected" or "superseded" or "withdrawn"));

    /// <summary>Starts a new Comment.</summary>
    [RelayCommand(CanExecute = nameof(CanAddComment))]
    private void AddComment()
    {
        if (Detail is not { } detail) return;
        var editor = new RationaleEditorModel(Context, detail, null, RemoveComment);
        Comments.Add(editor);
        TrackPausedEditor(editor);
    }

    private void RemoveComment(RationaleEditorModel editor) => Comments.Remove(editor);

    partial void OnDetailChanged(ProposalDetailProjection? value)
    {
        OnPropertyChanged(nameof(CanAddComment));
        AddCommentCommand.NotifyCanExecuteChanged();
    }

    private string? _markingReadId;
    private string? _pendingUserSelectionId;
    private bool _selectionByUser;

    /// <summary>The selected Proposal's stored detail, if it could be read.</summary>
    [ObservableProperty]
    private ProposalDetailProjection? _detail;

    /// <summary>The selected Proposal's Rationale in the rich field, editable while the Proposal is open.</summary>
    [ObservableProperty]
    private RationaleEditorModel? _rationaleEditor;

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
        _detailCancellation?.Cancel();
        _detailCancellation = null;
        Detail = null;
        RationaleEditor = null;
        Comments = [];
        DetailRefusal = null;
        OperationDescriptions = [];
        ReviewProgress = string.Empty;
        if (card is null || Context.ProjectPath is not { } path) return;
        var cancellation = _detailCancellation = new CancellationTokenSource();
        DetailReading = ReadDetailUntilStoppedAsync(path, card.ProposalId, generation, cancellation);
        _detailReads.RemoveAll(task => task.IsCompleted);
        _detailReads.Add(DetailReading);
    }

    private async Task ReadDetailUntilStoppedAsync(string path, string proposalId, int generation,
        CancellationTokenSource cancellation)
    {
        try
        {
            await ReadDetailAsync(path, proposalId, generation, cancellation.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        finally
        {
            if (ReferenceEquals(_detailCancellation, cancellation)) _detailCancellation = null;
            cancellation.Dispose();
        }
    }

    private async Task ReadDetailAsync(string path, string proposalId, int generation, CancellationToken cancellationToken)
    {
        var shown = await Context.Commands.ShowProposalAsync(
            new ShowProposalRequest(path, MotifProductVersion.CurrentText, proposalId), cancellationToken)
            .ConfigureAwait(true);
        if (generation != _detailGeneration || Context.ProjectPath != path) return;
        if (!shown.Succeeded)
        {
            DetailRefusal = WindowRefusal.From(shown.Refusal!);
            return;
        }
        Detail = shown.Value;
        // A re-read of the list keeps a Rationale the person is editing, and says so if it changed meanwhile.
        if (_lastRationaleEditor is { IsEditing: true } editing && editing.ProposalId == proposalId)
        {
            if (editing.Markdown != (shown.Value!.Rationale?.Markdown ?? string.Empty)) editing.MarkChangedElsewhere();
            RationaleEditor = editing;
        }
        else RationaleEditor = new RationaleEditorModel(Context, shown.Value!);
        _lastRationaleEditor = RationaleEditor;
        var detail = shown.Value!;
        var edits = _lastComments?.Where(editor => editor.ProposalId == proposalId && editor.IsEditing).ToArray() ?? [];
        var fresh = detail.Comments ?? [];
        var reconciled = new List<RationaleEditorModel>();
        foreach (var comment in fresh)
        {
            var editingComment = edits.FirstOrDefault(editor => editor.CommentId == comment.CommentId);
            if (editingComment is null) reconciled.Add(new RationaleEditorModel(Context, detail, comment, RemoveComment));
            else
            {
                editingComment.ReconcileComment(comment, detail, ReplaceAfterCancel);
                reconciled.Add(editingComment);
            }
        }
        foreach (var editingComment in edits.Where(editor => !editor.CommentId.HasValue ||
                     fresh.All(comment => comment.CommentId != editor.CommentId)))
        {
            editingComment.ReconcileComment(null, detail, ReplaceAfterCancel);
            reconciled.Add(editingComment);
        }
        Comments = new ObservableCollection<RationaleEditorModel>(reconciled);
        _lastComments = Comments;
        TrackPausedEditors();

        void ReplaceAfterCancel(RationaleEditorModel editor)
        {
            var index = Comments.IndexOf(editor);
            if (index < 0) return;
            var current = fresh.FirstOrDefault(comment => comment.CommentId == editor.CommentId);
            if (current is null) Comments.RemoveAt(index);
            else Comments[index] = new RationaleEditorModel(Context, detail, current, RemoveComment);
        }
        MarkSelectedRead();
        var review = await Context.Commands.ReadProposalReviewAsync(new ProposalReviewRequest(path, proposalId),
            cancellationToken).ConfigureAwait(true);
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
