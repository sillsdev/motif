using System.Collections;

namespace SIL.Motif.App.ViewModels;

/// <summary>
/// The words of one move, made when a row is first read: a list scrolls only the rows it draws, so a move of a
/// few hundred thousand words costs their positions and the rows looked at, not a view model each.
/// </summary>
internal sealed class LazyMovedWords(int[] order, Func<int, MovedWordViewModel> make) : IReadOnlyList<MovedWordViewModel>, IList
{
    private readonly Dictionary<int, MovedWordViewModel> _made = [];

    /// <summary>How many rows have been made, so a test can see that opening a move did not make them all.</summary>
    public int Made => _made.Count;

    public int Count => order.Length;

    public MovedWordViewModel this[int index]
    {
        get
        {
            if (!_made.TryGetValue(index, out var row)) _made[index] = row = make(order[index]);
            return row;
        }
    }

    object? IList.this[int index]
    {
        get => this[index];
        set => throw new NotSupportedException();
    }

    public IEnumerator<MovedWordViewModel> GetEnumerator()
    {
        for (var index = 0; index < Count; index++) yield return this[index];
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    bool IList.IsFixedSize => true;
    bool IList.IsReadOnly => true;
    bool ICollection.IsSynchronized => false;
    object ICollection.SyncRoot => this;

    int IList.Add(object? value) => throw new NotSupportedException();
    void IList.Clear() => throw new NotSupportedException();
    void IList.Insert(int index, object? value) => throw new NotSupportedException();
    void IList.Remove(object? value) => throw new NotSupportedException();
    void IList.RemoveAt(int index) => throw new NotSupportedException();

    bool IList.Contains(object? value) => IndexOf(value) >= 0;

    int IList.IndexOf(object? value) => IndexOf(value);

    // Only rows already made can be asked for, so a lookup never makes the rest.
    private int IndexOf(object? value) => value is MovedWordViewModel row
        ? _made.FirstOrDefault(pair => ReferenceEquals(pair.Value, row), new(-1, row)).Key : -1;

    void ICollection.CopyTo(Array array, int index)
    {
        foreach (var row in this) array.SetValue(row, index++);
    }
}
