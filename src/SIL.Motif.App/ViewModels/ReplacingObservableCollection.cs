using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace SIL.Motif.App.ViewModels;

/// <summary>Publishes a replacement list after every item is available to collection observers.</summary>
internal sealed class ReplacingObservableCollection<T> : ObservableCollection<T>
{
    /// <summary>Replaces all items and sends one collection reset, with count and indexer notifications.</summary>
    public void ReplaceWith(IReadOnlyList<T> items)
    {
        CheckReentrancy();
        Items.Clear();
        foreach (var item in items) Items.Add(item);
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
