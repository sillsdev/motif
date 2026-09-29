using CommunityToolkit.Mvvm.Input;

namespace SIL.Motif.App.ViewModels;

/// <summary>A group of pending changes with one Undo all action.</summary>
public sealed class ReviewChangeGroupViewModel
{
    private readonly ChangesViewModel _changes;

    public ReviewChangeGroupViewModel(string title, IReadOnlyList<ChangeViewModel> items,
        ChangesViewModel changes)
    {
        Title = title;
        Items = items;
        _changes = changes;
        UndoAllCommand = new AsyncRelayCommand(UndoAllAsync);
    }

    /// <summary>The title that describes what these changes do.</summary>
    public string Title { get; }

    /// <summary>The changes shown inside this group.</summary>
    public IReadOnlyList<ChangeViewModel> Items { get; }

    /// <summary>Removes each change or accepted set in this group.</summary>
    public IAsyncRelayCommand UndoAllCommand { get; }

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
