using CommunityToolkit.Mvvm.Input;

namespace SIL.Motif.App.ViewModels;

/// <summary>A group of pending changes with one Undo all action.</summary>
public sealed class ReviewChangeGroupViewModel
{
    private readonly ChangesViewModel _changes;
    private readonly Action<ChangeViewModel> _openChange;
    private int _nextWordIndex;

    public ReviewChangeGroupViewModel(string title, IReadOnlyList<ChangeViewModel> items,
        ChangesViewModel changes, Action<ChangeViewModel> openChange)
    {
        Title = title;
        Items = items;
        _changes = changes;
        _openChange = openChange;
        GoToTextCommand = new RelayCommand(GoToText);
        UndoAllCommand = new AsyncRelayCommand(UndoAllAsync);
    }

    /// <summary>The title that describes what these changes do.</summary>
    public string Title { get; }

    /// <summary>The changes shown inside this group.</summary>
    public IReadOnlyList<ChangeViewModel> Items { get; }

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
            var key = item.GroupId ?? item.ChangeId;
            if (!removals.Add(key)) continue;
            var current = _changes.Items.FirstOrDefault(change => (change.GroupId ?? change.ChangeId) == key);
            if (current is not null)
                await _changes.RemoveCommand.ExecuteAsync(current).ConfigureAwait(true);
        }
    }
}
