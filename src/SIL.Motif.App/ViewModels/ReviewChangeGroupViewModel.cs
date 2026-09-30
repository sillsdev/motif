using CommunityToolkit.Mvvm.Input;

namespace SIL.Motif.App.ViewModels;

/// <summary>Whether a group of pending changes can be applied as it stands, or needs attention first.</summary>
public enum ReviewGroupKind
{
    /// <summary>Changes that still fit, grouped by what they do.</summary>
    Ordinary,

    /// <summary>Changes made against a word or analysis FieldWorks has since changed.</summary>
    NoLongerFits,

    /// <summary>Changes whose sentence changed in FieldWorks since they were made.</summary>
    Uncertain,
}

/// <summary>A group of pending changes with one Undo all action.</summary>
public sealed class ReviewChangeGroupViewModel
{
    private readonly ChangesViewModel _changes;
    private readonly Action<ChangeViewModel> _openChange;
    private readonly int _projectGeneration;
    private readonly ReviewGroupKind _kind;
    private int _nextWordIndex;

    public ReviewChangeGroupViewModel(string title, IReadOnlyList<ChangeViewModel> items,
        ChangesViewModel changes, Action<ChangeViewModel> openChange, ReviewGroupKind kind = ReviewGroupKind.Ordinary)
    {
        Title = title;
        _kind = kind;
        Items = items;
        _changes = changes;
        _projectGeneration = changes.ProjectGeneration;
        _openChange = openChange;
        GoToTextCommand = new RelayCommand(GoToText);
        UndoAllCommand = new AsyncRelayCommand(UndoAllAsync);
    }

    /// <summary>The title that describes what these changes do.</summary>
    public string Title { get; }

    /// <summary>The changes shown inside this group.</summary>
    public IReadOnlyList<ChangeViewModel> Items { get; }

    /// <summary>Whether these changes no longer fit the project and must go before anything is applied.</summary>
    public bool IsNoLongerFits => _kind == ReviewGroupKind.NoLongerFits;

    /// <summary>Whether these changes' sentences changed, so each needs reconfirming or undoing.</summary>
    public bool IsUncertain => _kind == ReviewGroupKind.Uncertain;

    /// <summary>Why the whole group needs attention, beside its title; empty for an ordinary group.</summary>
    public string Note => _kind switch
    {
        ReviewGroupKind.NoLongerFits => "FieldWorks changed this word since you decided. It can't be applied as it is.",
        ReviewGroupKind.Uncertain => "The sentence changed in FieldWorks since you decided.",
        _ => string.Empty,
    };

    public bool HasNote => Note.Length > 0;

    /// <summary>The words on the action that takes back every change in the group.</summary>
    public string UndoAllText => IsNoLongerFits ? "Remove the ones that no longer fit" : "Undo all";

    public string UndoAllAutomationName => $"Undo all: {Title}";

    public string WordCountText
    {
        get
        {
            var count = Items.Select(item => item.Word).Distinct(StringComparer.Ordinal).Count();
            return $"· {count} {(count == 1 ? "word" : "words")}";
        }
    }

    public string GoToTextAutomationName => $"Go to text in {Title}";

    /// <summary>Removes each change or accepted set in this group.</summary>
    public IAsyncRelayCommand UndoAllCommand { get; }

    /// <summary>Opens the next word in this group in Analyze texts.</summary>
    public IRelayCommand GoToTextCommand { get; }

    private void GoToText()
    {
        if (Items.Count == 0) return;
        _openChange(Items[_nextWordIndex]);
        _nextWordIndex = (_nextWordIndex + 1) % Items.Count;
    }

    private async Task UndoAllAsync()
    {
        var removals = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in Items)
        {
            if (_changes.ProjectGeneration != _projectGeneration) return;
            var key = item.GroupId ?? item.ChangeId;
            if (!removals.Add(key)) continue;
            var current = _changes.Items.FirstOrDefault(change => (change.GroupId ?? change.ChangeId) == key);
            if (current is not null)
                await _changes.RemoveCommand.ExecuteAsync(current).ConfigureAwait(true);
        }
    }
}
