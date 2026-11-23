using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.ViewModels;

public sealed partial class ProposalsPageModel
{
    private readonly ProposalDocumentViewModel _emptyDocument;
    private readonly HashSet<ProposalDocumentViewModel> _watchedDocuments = [];
    private string? _markingReadId;
    private string? _pendingUserSelectionId;
    private bool _selectionByUser;
    private IRelayCommand? _addCommentCommand;
    private object? _comparisonContent;
    private string _comparisonNotice = string.Empty;
    public string ComparisonNotice { get => _comparisonNotice; private set => SetProperty(ref _comparisonNotice, value); }

    public OpenProposalDocuments Documents { get; }
    public ProposalDocumentViewModel? SelectedDocument => Documents.Selected;
    private ProposalDocumentViewModel ActiveDocument => SelectedDocument ?? _emptyDocument;
    public ProposalDetailProjection? Detail => SelectedDocument?.Detail;
    public RationaleEditorModel? RationaleEditor => SelectedDocument?.RationaleEditor;
    public ObservableCollection<RationaleEditorModel> Comments => ActiveDocument.Comments;
    public IReadOnlyList<string> OperationDescriptions => ActiveDocument.OperationDescriptions;
    public WindowRefusal? DetailRefusal => SelectedDocument?.DetailRefusal;
    public string ReviewProgress => ActiveDocument.ReviewProgress;
    public bool CanAddComment => SelectedDocument?.CanAddComment == true;
    public IRelayCommand AddCommentCommand => _addCommentCommand ??= new RelayCommand(() =>
        SelectedDocument?.AddCommentCommand.Execute(null), () => CanAddComment);
    public Task DetailReading { get; private set; } = Task.CompletedTask;
    public Task ReadMarkWriting { get; private set; } = Task.CompletedTask;
    public IAsyncRelayCommand<ProposalDocumentViewModel> CloseDocumentCommand { get; }
    public IRelayCommand<ProposalDocumentViewModel> SelectDocumentCommand { get; }
    public IRelayCommand CompareCommand { get; }

    /// <summary>The selected, explicitly checked pair, supplied to the comparison host.</summary>
    private Action<IReadOnlyList<ProposalDocumentViewModel>>? _comparisonRequested;
    public event Action<IReadOnlyList<ProposalDocumentViewModel>> ComparisonRequested
    {
        add
        {
            _comparisonRequested += value;
            OnPropertyChanged(nameof(HasComparisonHost));
            CompareCommand.NotifyCanExecuteChanged();
        }
        remove
        {
            _comparisonRequested -= value;
            OnPropertyChanged(nameof(HasComparisonHost));
            CompareCommand.NotifyCanExecuteChanged();
        }
    }
    public bool HasComparisonHost => _comparisonRequested is not null;
    private void ComparePair() => _comparisonRequested?.Invoke(Documents.ComparisonPair);

    /// <summary>The comparison view provided by the Proposal comparison feature.</summary>
    public object? ComparisonContent
    {
        get => _comparisonContent;
        set
        {
            if (SetProperty(ref _comparisonContent, value)) OnPropertyChanged(nameof(IsComparing));
        }
    }

    public bool IsComparing => ComparisonContent is not null;

    private bool CheckComparison(ProposalCardViewModel card, bool value)
    {
        // A refused check opens nothing, so a third check cannot leave a document open behind the notice.
        if (!Documents.CanCheck(card.ProposalId, value))
        {
            ComparisonNotice = MotifAppStrings.ProposalComparisonLimit;
            return false;
        }
        if (!value && Documents.Find(card.ProposalId) is null)
        {
            ComparisonNotice = string.Empty;
            return true;
        }
        var document = Documents.Open(card.ProposalId, select: false);
        document.Card = card;
        var accepted = Documents.Check(document, value);
        ComparisonNotice = accepted ? string.Empty : MotifAppStrings.ProposalComparisonLimit;
        if (accepted) ComparisonContent = null;
        if (accepted && document.Detail is null) _ = document.EnsureLoadedAsync();
        return accepted;
    }

    private void UnwatchDocuments()
    {
        foreach (var document in _watchedDocuments)
        {
            document.PropertyChanged -= OnDocumentChanged;
            document.BroughtBackToWaiting -= OnBroughtBackToWaiting;
        }
        _watchedDocuments.Clear();
    }

    private void ReadDetail(ProposalCardViewModel? card)
    {
        if (card is null || Context.ProjectPath is null) return;
        var document = Documents.Open(card.ProposalId);
        document.Card = card;
        if (_watchedDocuments.Add(document))
        {
            document.PropertyChanged += OnDocumentChanged;
            document.BroughtBackToWaiting += OnBroughtBackToWaiting;
        }
        NotifyDocument();
        DetailReading = ReadDocumentAsync(document, _rereading);
    }

    private async Task ReadDocumentAsync(ProposalDocumentViewModel document, bool refresh)
    {
        await (refresh ? document.RefreshAsync() : document.EnsureLoadedAsync()).ConfigureAwait(true);
        if (SelectedDocument != document) return;
        DifferenceReading = document.DifferenceReading;
        PicturesLoading = document.PicturesLoading;
        TrackPausedEditors();
        NotifyDocument();
        MarkSelectedRead();
    }

    private void NotifyDocument()
    {
        foreach (var name in new[] { nameof(SelectedDocument), nameof(Detail), nameof(RationaleEditor), nameof(Comments),
            nameof(OperationDescriptions), nameof(DetailRefusal), nameof(ReviewProgress), nameof(CanAddComment),
            nameof(Difference), nameof(Pictures), nameof(Parsimony) }) OnPropertyChanged(name);
        AddCommentCommand.NotifyCanExecuteChanged();
    }

    /// <summary>The list reload that follows a Proposal coming back to Waiting from its own page.</summary>
    public Task ListReloading { get; private set; } = Task.CompletedTask;

    private void OnBroughtBackToWaiting()
    {
        if (Context.ProjectPath is { } path) ListReloading = LoadAsync(path, CancellationToken.None);
    }

    private void OnDocumentChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (ReferenceEquals(sender, SelectedDocument)) NotifyDocument();
    }

    private void SelectDocument(ProposalDocumentViewModel? document)
    {
        if (document is null) return;
        var card = Groups.SelectMany(group => group.Cards).FirstOrDefault(card => card.ProposalId == document.ProposalId);
        if (card is not null) SelectByUser(card);
        else
        {
            Documents.Select(document);
            NotifyDocument();
        }
    }

    private async Task CloseDocumentAsync(ProposalDocumentViewModel? document)
    {
        if (document is null || !await Documents.CloseAsync(document).ConfigureAwait(true)) return;
        ComparisonContent = null;
        document.PropertyChanged -= OnDocumentChanged;
        document.BroughtBackToWaiting -= OnBroughtBackToWaiting;
        _watchedDocuments.Remove(document);
        if (SelectedDocument is { } selected) SelectDocument(selected);
        else SetSelectedProposal(null);
        NotifyDocument();
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
        else if (SelectedProposal?.ProposalId == card.ProposalId && SelectedDocument is { } document)
            document.DetailRefusal = WindowRefusal.From(mark.Refusal!);
    }
}
