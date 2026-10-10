using System.Collections;

namespace SIL.Motif.App.ViewModels;

// The item source needs indexed access without projecting a whole list.
internal sealed class LazyProjectionList<TSource, TResult>(IReadOnlyList<TSource> source,
    Func<TSource, TResult> project, Func<TResult, TSource> sourceOf, Func<bool>? isCurrent = null) : IReadOnlyList<TResult>, IList
    where TSource : class where TResult : class
{
    public int Count => isCurrent?.Invoke() == false ? 0 : source.Count;
    public TResult this[int index] => index >= 0 && index < Count
        ? project(source[index]) : throw new ArgumentOutOfRangeException(nameof(index));
    object? IList.this[int index] { get => this[index]; set => throw new NotSupportedException(); }
    bool IList.IsReadOnly => true;
    bool IList.IsFixedSize => true;
    bool ICollection.IsSynchronized => false;
    object ICollection.SyncRoot => this;

    public IEnumerator<TResult> GetEnumerator()
    {
        for (var index = 0; index < Count; index++) yield return this[index];
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    int IList.IndexOf(object? value)
    {
        if (value is not TResult row) return -1;
        var original = sourceOf(row);
        for (var index = 0; index < Count; index++)
            if (ReferenceEquals(source[index], original)) return index;
        return -1;
    }

    bool IList.Contains(object? value) => ((IList)this).IndexOf(value) >= 0;

    void ICollection.CopyTo(Array array, int index)
    {
        for (var item = 0; item < Count; item++) array.SetValue(this[item], index + item);
    }

    int IList.Add(object? value) => throw new NotSupportedException();
    void IList.Clear() => throw new NotSupportedException();
    void IList.Insert(int index, object? value) => throw new NotSupportedException();
    void IList.Remove(object? value) => throw new NotSupportedException();
    void IList.RemoveAt(int index) => throw new NotSupportedException();
}
