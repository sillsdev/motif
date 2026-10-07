using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using SIL.Motif.App.Controls;
using SIL.Motif.App.Controls.WordPresentation;
using SIL.Motif.App.Services;
using ModuleWordRowHeader = SIL.Motif.App.Controls.WordPresentation.WordRowHeader;

namespace SIL.Motif.App.Views;

/// <summary>Owns one constrained word list and shares its measured tracks with the heading and every row.</summary>
public sealed class WordListSlot : ScrollViewer
{
    public static readonly StyledProperty<WordListSchema> SchemaProperty =
        AvaloniaProperty.Register<WordListSlot, WordListSchema>(nameof(Schema), WordListSchemas.All);

    public static readonly StyledProperty<bool> ShowsMeaningProperty =
        AvaloniaProperty.Register<WordListSlot, bool>(nameof(ShowsMeaning), true);

    private readonly WordListPolicy _policy = new();
    private readonly WordListSizingAdapter _sizing = new();
    private WordListLayout? _layout;
    private double? _lastFiniteOuterWidth;
    private TextStyles? _subscribedTextStyles;

    static WordListSlot()
    {
        WritingSystemText.ResolverProperty.Changed.AddClassHandler<WordListSlot>((slot, _) => slot.RefreshTextStyles());
    }

    public WordListSlot()
    {
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
    }

    /// <summary>The semantic columns and morphology producers displayed by the whole list.</summary>
    public WordListSchema Schema
    {
        get => GetValue(SchemaProperty);
        set => SetValue(SchemaProperty, value ?? throw new ArgumentNullException(nameof(value)));
    }

    /// <summary>Whether this list displays the meaning column.</summary>
    public bool ShowsMeaning
    {
        get => GetValue(ShowsMeaningProperty);
        set => SetValue(ShowsMeaningProperty, value);
    }

    internal int LayoutResolutionCount { get; private set; }
    internal int LayoutPublicationCount { get; private set; }
    internal int LayoutPassCount { get; private set; }
    internal int HeadingMeasurementCount => _sizing.HeadingMeasurementCount;
    internal WordListLayout? CurrentLayout => _layout;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SchemaProperty || change.Property == ShowsMeaningProperty)
            InvalidateMeasure();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        RefreshTextStyles();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_subscribedTextStyles is not null)
            _subscribedTextStyles.ContextChanged -= OnTextStyleContextChanged;
        _subscribedTextStyles = null;
        base.OnDetachedFromVisualTree(e);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        LayoutPassCount++;
        var outerWidth = availableSize.Width;
        if (!double.IsFinite(outerWidth))
            outerWidth = (_lastFiniteOuterWidth is > 0 ? _lastFiniteOuterWidth : null) ?? FiniteAncestorWidth() ?? double.NaN;
        else if (outerWidth > 0)
            _lastFiniteOuterWidth = outerWidth;
        if (!double.IsFinite(outerWidth) || outerWidth < 0)
            throw new InvalidOperationException("A word list must receive a finite outer width.");
        if (Content is not Grid content)
            throw new InvalidOperationException("A word-list slot needs one header and a grid content root.");
        var header = (Control?)content.Children.OfType<ModuleWordRowHeader>().FirstOrDefault()
            ?? throw new InvalidOperationException("A word-list slot needs one header and a grid content root.");
        var rows = content.Children.OfType<ItemsControl>().FirstOrDefault();
        if (rows is null) throw new InvalidOperationException("A word-list slot needs one row collection.");

        var sizing = _sizing.GetSizing(header, Schema, ShowsMeaning,
            WritingSystemText.GetResolver(this), out var styleRevision, out var uiLocale);
        var measureSize = new Size(outerWidth, availableSize.Height);
        var resolved = _policy.Resolve(outerWidth, Schema, ShowsMeaning,
            styleRevision, uiLocale, sizing);
        if (!ReferenceEquals(_layout, resolved))
        {
            _layout = resolved;
            LayoutResolutionCount++;
            LayoutPublicationCount++;
            WordListLayoutScope.SetLayout(this, resolved);
            ApplySlotGeometry(content, header, rows, resolved);
        }
        HorizontalScrollBarVisibility = resolved.UsesHorizontalViewport
            ? ScrollBarVisibility.Auto
            : ScrollBarVisibility.Disabled;
        return base.MeasureOverride(measureSize);
    }

    private static void ApplySlotGeometry(Grid content, Control header, ItemsControl rows, WordListLayout layout)
    {
        var rowWidth = layout.TrackAreaWidth + layout.OuterChromeWidth;
        var slotWidth = rowWidth + layout.ReservedVerticalScrollbarWidth;
        content.Width = slotWidth;
        header.Width = rowWidth;
        if (rows is ListBox list)
        {
            list.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Visible);
            list.Width = slotWidth;
        }
        else
        {
            rows.Width = rowWidth;
        }
    }

    private double? FiniteAncestorWidth()
    {
        foreach (var ancestor in this.GetVisualAncestors())
        {
            var width = ancestor switch
            {
                ScrollViewer scroll when double.IsFinite(scroll.Viewport.Width) => scroll.Viewport.Width,
                Control control when double.IsFinite(control.Bounds.Width) => control.Bounds.Width,
                _ => double.NaN,
            };
            if (double.IsFinite(width) && width > 0) return width;
        }
        return null;
    }

    private void RefreshTextStyles()
    {
        if (_subscribedTextStyles is not null)
            _subscribedTextStyles.ContextChanged -= OnTextStyleContextChanged;
        _subscribedTextStyles = WritingSystemText.GetResolver(this);
        if (_subscribedTextStyles is not null)
            _subscribedTextStyles.ContextChanged += OnTextStyleContextChanged;
        InvalidateMeasure();
    }

    private void OnTextStyleContextChanged(object? sender, EventArgs args) => InvalidateMeasure();
}
