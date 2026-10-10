namespace SIL.Motif.App.Services;

/// <summary>A wrapping collection whose display objects are loaded and owned one page at a time.</summary>
public interface IProgressivePageSource
{
    /// <summary>The complete source count, including items outside the displayed page.</summary>
    int Count { get; }

    /// <summary>The page containing the focused source item when the collection is first attached.</summary>
    int InitialOffset { get; }

    /// <summary>Finds an exact source position without projecting the complete collection.</summary>
    int IndexOf(object item);

    /// <summary>Replaces the owned display page with at most the requested number of items.</summary>
    Task<IReadOnlyList<object>> ReadPageAsync(int offset, int count, CancellationToken cancellationToken);

    /// <summary>Releases the view’s page request and cancels its reads when the view is detached.</summary>
    void ReleasePage();
}
