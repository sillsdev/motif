using System.Collections;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace SIL.Motif.App.Views;

/// <summary>
/// Pages wrapping collections that cannot use a vertical virtualizing panel. The source retains every item;
/// only one page has containers. Navigation replaces that page rather than accumulating previously shown rows.
/// </summary>
public sealed class ProgressiveItemsControl : ItemsControl
{
    public static readonly StyledProperty<IEnumerable?> FullItemsSourceProperty =
        AvaloniaProperty.Register<ProgressiveItemsControl, IEnumerable?>(nameof(FullItemsSource));

    private const int PageSize = 20;
    private int _offset;
    private bool _updating;
    private bool _attached;
    private IDataTemplate? _rowTemplate;
    private INotifyCollectionChanged? _observed;

    protected override Type StyleKeyOverride => typeof(ItemsControl);

    /// <summary>The complete wrapping collection, independent of the currently displayed page.</summary>
    public IEnumerable? FullItemsSource
    {
        get => GetValue(FullItemsSourceProperty);
        set => SetValue(FullItemsSourceProperty, value);
    }

    /// <summary>Displays the page containing an item so a caller can then focus its container.</summary>
    public void ShowItem(object item)
    {
        var index = 0;
        foreach (var candidate in FullItemsSource ?? Array.Empty<object>())
        {
            if (ReferenceEquals(candidate, item) || Equals(candidate, item))
            {
                var offset = index / PageSize * PageSize;
                if (_offset == offset) return;
                _offset = offset;
                RefreshPage();
                return;
            }
            index++;
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (_updating) return;
        if (change.Property == FullItemsSourceProperty)
        {
            Observe(null);
            if (_attached) Observe(FullItemsSource as INotifyCollectionChanged);
            _offset = 0;
            RefreshPage();
        }
        else if (change.Property == ItemTemplateProperty)
        {
            _rowTemplate = ItemTemplate;
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
        _offset = 0;
        RefreshPage();
    }

    private void RefreshPage()
    {
        _updating = true;
        try
        {
            var source = FullItemsSource?.Cast<object>().ToArray() ?? [];
            _offset = Math.Min(_offset, Math.Max(0, (source.Length - 1) / PageSize * PageSize));
            var page = new List<object>();
            if (_offset > 0) page.Add(new Navigation($"Previous {PageSize}", _offset - PageSize));
            page.AddRange(source.Skip(_offset).Take(PageSize));
            var remaining = source.Length - _offset - PageSize;
            if (remaining > 0) page.Add(new Navigation($"Show {Math.Min(PageSize, remaining)} more", _offset + PageSize));
            ItemTemplate = new FuncDataTemplate<object>((item, _) =>
            {
                if (item is not Navigation navigation) return _rowTemplate?.Build(item) ?? new TextBlock { Text = item?.ToString() };
                var button = new Button { Content = navigation.Label, Tag = navigation, Classes = { "filterChip" } };
                button.Click += (_, _) =>
                {
                    var restoreFocus = button.IsFocused;
                    var forward = navigation.Offset > _offset;
                    _offset = navigation.Offset;
                    RefreshPage();
                    if (restoreFocus) Dispatcher.UIThread.Post(() =>
                    {
                        var buttons = this.GetVisualDescendants().OfType<Button>()
                            .Where(candidate => candidate.Tag is Navigation).ToArray();
                        var target = buttons.FirstOrDefault(candidate =>
                            (((Navigation)candidate.Tag!).Offset > _offset) == forward) ?? buttons.FirstOrDefault();
                        target?.BringIntoView();
                        target?.Focus();
                    });
                };
                return button;
            });
            ItemsSource = page;
        }
        finally { _updating = false; }
    }

    private sealed record Navigation(string Label, int Offset);
}
