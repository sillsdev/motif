using System.Collections;
using System.Collections.Specialized;
using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Controls.Presenters;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SIL.Motif.App.Services;

namespace SIL.Motif.App.Views;

/// <summary>
/// Pages wrapping collections that cannot use a vertical virtualizing panel. Indexed or asynchronous sources
/// supply one display page; navigation replaces its containers and releases the previous owned page.
/// </summary>
public sealed class ProgressiveItemsControl : ItemsControl
{
    public static readonly StyledProperty<object?> FullItemsSourceProperty =
        AvaloniaProperty.Register<ProgressiveItemsControl, object?>(nameof(FullItemsSource));

    private const int PageSize = 20;
    private int _offset;
    private bool _updating;
    private bool _attached;
    private IDataTemplate? _rowTemplate;
    private IDataTemplate? _pageTemplate;
    private readonly ObservableCollection<object> _displayed = [];
    private INotifyCollectionChanged? _observed;
    private CancellationTokenSource? _pageCancellation;
    private long _pageGeneration;
    internal Task PageRefresh { get; private set; } = Task.CompletedTask;

    protected override Type StyleKeyOverride => typeof(ItemsControl);

    /// <summary>The complete wrapping collection, independent of the currently displayed page.</summary>
    public object? FullItemsSource
    {
        get => GetValue(FullItemsSourceProperty);
        set => SetValue(FullItemsSourceProperty, value);
    }

    /// <summary>Displays the page containing an item so a caller can then focus its container.</summary>
    public void ShowItem(object item)
    {
        if (FullItemsSource is IProgressivePageSource pages)
        {
            ShowIndex(pages.IndexOf(item));
            return;
        }
        if (FullItemsSource is IList indexed)
        {
            ShowIndex(indexed.IndexOf(item));
            return;
        }
        var index = 0;
        foreach (var candidate in FullItemsSource as IEnumerable ?? Array.Empty<object>())
        {
            if (ReferenceEquals(candidate, item) || Equals(candidate, item))
            {
                ShowIndex(index);
                return;
            }
            index++;
        }
    }

    /// <summary>Displays an exact source position and waits until that page's containers can be realized.</summary>
    public async Task ShowSourceIndexAsync(int index)
    {
        ShowIndex(index);
        await PageRefresh.ConfigureAwait(true);
    }

    private void ShowIndex(int index)
    {
        if (index < 0) return;
        var offset = index / PageSize * PageSize;
        if (_offset == offset) return;
        _offset = offset;
        RefreshPage();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (_updating) return;
        if (change.Property == FullItemsSourceProperty)
        {
            (change.OldValue as IProgressivePageSource)?.ReleasePage();
            Observe(null);
            if (_attached) Observe(FullItemsSource as INotifyCollectionChanged);
            _offset = FullItemsSource is IProgressivePageSource pages ? pages.InitialOffset : 0;
            RefreshPage();
        }
        else if (change.Property == ItemTemplateProperty)
        {
            _rowTemplate = ItemTemplate;
            _pageTemplate = new PageTemplate(this, _rowTemplate);
            RefreshPage();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        Observe(FullItemsSource as INotifyCollectionChanged);
        RefreshPage();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Observe(null);
        _attached = false;
        CancelPage();
        if (FullItemsSource is IProgressivePageSource pages)
        {
            // Detached visuals retain Content; pinned by `DetachedOccurrencePageReleasesRowsBeforeCardClosure`.
            foreach (var presenter in GetRealizedContainers().OfType<ContentPresenter>())
            {
                presenter.Content = null;
                presenter.DataContext = null;
            }
            ItemsSource = null;
            _displayed.Clear();
            pages.ReleasePage();
        }
        base.OnDetachedFromVisualTree(e);
    }

    private void Observe(INotifyCollectionChanged? source)
    {
        if (_observed is not null) _observed.CollectionChanged -= OnSourceChanged;
        _observed = source;
        if (_observed is not null) _observed.CollectionChanged += OnSourceChanged;
    }

    private void OnSourceChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (FullItemsSource is not IProgressivePageSource || e.Action == NotifyCollectionChangedAction.Reset) _offset = 0;
        RefreshPage();
    }

    private void RefreshPage()
    {
        CancelPage();
        if (FullItemsSource is IProgressivePageSource pages)
        {
            _offset = Math.Min(_offset, Math.Max(0, (pages.Count - 1) / PageSize * PageSize));
            var cancellation = new CancellationTokenSource();
            _pageCancellation = cancellation;
            PageRefresh = ReadPageAsync(pages, _offset, _pageGeneration, cancellation);
            return;
        }
        IList source = FullItemsSource as IList ?? (FullItemsSource as IEnumerable)?.Cast<object>().ToArray() ?? [];
        _offset = Math.Min(_offset, Math.Max(0, (source.Count - 1) / PageSize * PageSize));
        PublishPage(source, source.Count, _offset);
    }

    private async Task ReadPageAsync(IProgressivePageSource pages, int offset, long generation,
        CancellationTokenSource cancellation)
    {
        try
        {
            var pending = pages.ReadPageAsync(offset, PageSize, cancellation.Token);
            if (!pending.IsCompleted)
            {
                if (_rowTemplate is ResultsModelTemplate models)
                    foreach (var presenter in GetRealizedContainers().OfType<ContentPresenter>())
                        models.ReleaseModel(presenter.Child);
                else _displayed.Clear();
            }
            var page = await pending.ConfigureAwait(true);
            if (generation != _pageGeneration || cancellation.IsCancellationRequested ||
                !ReferenceEquals(pages, FullItemsSource)) return;
            PublishPage(page as IList ?? page.ToArray(), pages.Count, 0);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            if (generation == _pageGeneration) _displayed.Clear();
        }
        finally
        {
            if (ReferenceEquals(_pageCancellation, cancellation)) _pageCancellation = null;
            cancellation.Dispose();
        }
    }

    private void CancelPage()
    {
        ++_pageGeneration;
        try { _pageCancellation?.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    private void PublishPage(IList source, int totalCount, int sourceOffset)
    {
        _updating = true;
        try
        {
            var page = new List<object>();
            if (_offset > 0) page.Add(new Navigation($"Previous {PageSize}", _offset - PageSize));
            for (var index = sourceOffset; index < Math.Min(source.Count, sourceOffset + PageSize); index++)
                page.Add(source[index]!);
            var remaining = totalCount - _offset - PageSize;
            if (remaining > 0) page.Add(new Navigation($"Show {Math.Min(PageSize, remaining)} more", _offset + PageSize));
            ItemTemplate = _pageTemplate ??= new PageTemplate(this, _rowTemplate);
            for (var index = 0; index < Math.Min(_displayed.Count, page.Count); index++)
                if (!ReferenceEquals(_displayed[index], page[index]) &&
                    !(_rowTemplate is ResultsModelTemplate && ResultsModelTemplate.RepresentsSameModel(_displayed[index], page[index])))
                    _displayed[index] = page[index];
            while (_displayed.Count > page.Count) _displayed.RemoveAt(_displayed.Count - 1);
            for (var index = _displayed.Count; index < page.Count; index++) _displayed.Add(page[index]);
            ItemsSource = _displayed;
            if (_rowTemplate is ResultsModelTemplate models)
                foreach (var presenter in GetRealizedContainers().OfType<ContentPresenter>())
                    if (presenter.Content is { } position) models.RestoreModel(presenter.Child, position);
        }
        finally { _updating = false; }
    }

    private Control BuildNavigation(Navigation navigation, Control? existing)
    {
        if (existing is Button { Tag: Navigation } recycled)
        {
            recycled.Content = navigation.Label;
            recycled.Tag = navigation;
            return recycled;
        }
        var button = new Button { Content = navigation.Label, Tag = navigation, Classes = { "filterChip" } };
        button.Click += OnNavigate;
        return button;
    }

    private async void OnNavigate(object? sender, Avalonia.Interactivity.RoutedEventArgs args)
    {
        if (sender is not Button { Tag: Navigation navigation } button) return;
        var restoreFocus = button.IsFocused;
        var forward = navigation.Offset > _offset;
        _offset = navigation.Offset;
        RefreshPage();
        var generation = _pageGeneration;
        await PageRefresh.ConfigureAwait(true);
        if (generation != _pageGeneration) return;
        if (restoreFocus) Dispatcher.UIThread.Post(() =>
        {
            if (generation != _pageGeneration) return;
            var buttons = this.GetVisualDescendants().OfType<Button>()
                .Where(candidate => candidate.Tag is Navigation).ToArray();
            var target = buttons.FirstOrDefault(candidate =>
                (((Navigation)candidate.Tag!).Offset > _offset) == forward) ?? buttons.FirstOrDefault();
            target?.BringIntoView();
            target?.Focus();
        });
    }

    private sealed class PageTemplate(ProgressiveItemsControl owner, IDataTemplate? inner) : IRecyclingDataTemplate
    {
        public bool Match(object? data) => data is Navigation || inner?.Match(data) == true;
        public Control? Build(object? data) => Build(data, null);
        public Control? Build(object? data, Control? existing)
        {
            if (data is Navigation navigation) return owner.BuildNavigation(navigation, existing);
            if (existing is Button { Tag: Navigation }) existing = null;
            return inner is IRecyclingDataTemplate recycling ? recycling.Build(data, existing) :
                inner?.Build(data) ?? new TextBlock { Text = data?.ToString() };
        }
    }

    private sealed record Navigation(string Label, int Offset);
}
