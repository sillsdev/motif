using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SIL.Motif.App.ViewModels;

/// <summary>Retains independently open Proposals and an explicit pair for comparison within one project.</summary>
public sealed class OpenProposalDocuments : ObservableObject
{
    private readonly WorkspaceContext _context;
    private readonly Func<TimeSpan, CancellationToken, Task>? _parsimonyDelay;
    private readonly ObservableCollection<ProposalDocumentViewModel> _items = [];
    private ProposalDocumentViewModel? _selected;

    public OpenProposalDocuments(WorkspaceContext context,
        Func<TimeSpan, CancellationToken, Task>? parsimonyDelay = null)
    {
        _context = context;
        _parsimonyDelay = parsimonyDelay;
        Items = new ReadOnlyObservableCollection<ProposalDocumentViewModel>(_items);
    }

    public ReadOnlyObservableCollection<ProposalDocumentViewModel> Items { get; }
    public ProposalDocumentViewModel? Selected
    {
        get => _selected;
        private set => SetProperty(ref _selected, value);
    }
    public IReadOnlyList<ProposalDocumentViewModel> ComparisonPair =>
        _items.Where(document => document.ProjectPath == _context.ProjectPath && document.IsCheckedForComparison).ToArray();
    public bool CanCompare => ComparisonPair.Count == 2;
    public RationaleEditorModel? UnsavedEditor => _items.Select(document => document.UnsavedEditor)
        .FirstOrDefault(editor => editor is not null);

    public ProposalDocumentViewModel Open(string proposalId, bool select = true)
    {
        var path = _context.ProjectPath ?? throw new InvalidOperationException("Open a project first.");
        var document = _items.FirstOrDefault(item => item.ProjectPath == path && item.ProposalId == proposalId);
        if (document is null)
        {
            document = new ProposalDocumentViewModel(_context, path, proposalId, _parsimonyDelay);
            _items.Add(document);
        }
        if (select) Selected = document;
        return document;
    }

    public void Select(ProposalDocumentViewModel document)
    {
        if (_items.Contains(document) && document.ProjectPath == _context.ProjectPath) Selected = document;
    }

    /// <summary>The open document for a Proposal of the current project, or null when none is open.</summary>
    public ProposalDocumentViewModel? Find(string proposalId) =>
        _items.FirstOrDefault(item => item.ProjectPath == _context.ProjectPath && item.ProposalId == proposalId);

    /// <summary>Whether checking or unchecking this Proposal would be accepted, without opening anything.</summary>
    public bool CanCheck(string proposalId, bool value) =>
        !value || Find(proposalId)?.IsCheckedForComparison == true || ComparisonPair.Count < 2;

    public bool Check(ProposalDocumentViewModel document, bool value)
    {
        if (!_items.Contains(document) || document.ProjectPath != _context.ProjectPath) return false;
        if (value && !document.IsCheckedForComparison && ComparisonPair.Count == 2) return false;
        document.SetChecked(value);
        OnPropertyChanged(nameof(ComparisonPair));
        OnPropertyChanged(nameof(CanCompare));
        return true;
    }

    public async Task<bool> CloseAsync(ProposalDocumentViewModel document)
    {
        if (!_items.Contains(document)) return true;
        if (document.UnsavedEditor is { } editor)
        {
            editor.MarkSwitchRefused();
            return false;
        }
        await document.StopAsync().ConfigureAwait(true);
        _items.Remove(document);
        if (ReferenceEquals(Selected, document)) Selected = _items.LastOrDefault();
        OnPropertyChanged(nameof(ComparisonPair));
        OnPropertyChanged(nameof(CanCompare));
        return true;
    }

    public void InvalidateEvidence()
    {
        foreach (var document in _items) document.InvalidateEvidence();
    }

    public Task RefreshAsync(CancellationToken cancellationToken = default) =>
        Task.WhenAll(_items.ToArray().Select(document => document.RefreshAsync(cancellationToken)));

    public async Task StopAsync()
    {
        var closing = _items.ToArray();
        _items.Clear();
        Selected = null;
        OnPropertyChanged(nameof(ComparisonPair));
        OnPropertyChanged(nameof(CanCompare));
        await Task.WhenAll(closing.Select(document => document.StopAsync())).ConfigureAwait(true);
    }
}
