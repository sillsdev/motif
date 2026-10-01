using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.Views;

/// <summary>One kind in a <see cref="TimingKindBar"/>'s legend, with the brush its part is drawn in.</summary>
/// <param name="Row">The kind and its share of the time.</param>
/// <param name="Brush">The brush the bar fills this kind's part with.</param>
public sealed record TimingKindLegendEntry(TimingAggregateRow Row, IBrush? Brush);

/// <summary>Draws the command's kind shares across one full-width bar, and names each part's colour for a legend.</summary>
public sealed class TimingKindBar : Control
{
    public static readonly StyledProperty<IReadOnlyList<TimingAggregateRow>?> RowsProperty =
        AvaloniaProperty.Register<TimingKindBar, IReadOnlyList<TimingAggregateRow>?>(nameof(Rows));
    public static readonly StyledProperty<IBrush?> FirstBrushProperty =
        AvaloniaProperty.Register<TimingKindBar, IBrush?>(nameof(FirstBrush));
    public static readonly StyledProperty<IBrush?> SecondBrushProperty =
        AvaloniaProperty.Register<TimingKindBar, IBrush?>(nameof(SecondBrush));
    public static readonly StyledProperty<IBrush?> ThirdBrushProperty =
        AvaloniaProperty.Register<TimingKindBar, IBrush?>(nameof(ThirdBrush));
    public static readonly StyledProperty<IBrush?> FourthBrushProperty =
        AvaloniaProperty.Register<TimingKindBar, IBrush?>(nameof(FourthBrush));
    public static readonly StyledProperty<IBrush?> OtherBrushProperty =
        AvaloniaProperty.Register<TimingKindBar, IBrush?>(nameof(OtherBrush));
    public static readonly DirectProperty<TimingKindBar, IReadOnlyList<TimingKindLegendEntry>> LegendProperty =
        AvaloniaProperty.RegisterDirect<TimingKindBar, IReadOnlyList<TimingKindLegendEntry>>(nameof(Legend), bar => bar.Legend);

    private IReadOnlyList<TimingKindLegendEntry> _legend = [];

    static TimingKindBar() => AffectsRender<TimingKindBar>(RowsProperty, FirstBrushProperty,
        SecondBrushProperty, ThirdBrushProperty, FourthBrushProperty, OtherBrushProperty);

    public IReadOnlyList<TimingAggregateRow>? Rows
    {
        get => GetValue(RowsProperty);
        set => SetValue(RowsProperty, value);
    }

    public IBrush? FirstBrush
    {
        get => GetValue(FirstBrushProperty);
        set => SetValue(FirstBrushProperty, value);
    }

    public IBrush? SecondBrush
    {
        get => GetValue(SecondBrushProperty);
        set => SetValue(SecondBrushProperty, value);
    }

    public IBrush? ThirdBrush
    {
        get => GetValue(ThirdBrushProperty);
        set => SetValue(ThirdBrushProperty, value);
    }

    public IBrush? FourthBrush
    {
        get => GetValue(FourthBrushProperty);
        set => SetValue(FourthBrushProperty, value);
    }

    public IBrush? OtherBrush
    {
        get => GetValue(OtherBrushProperty);
        set => SetValue(OtherBrushProperty, value);
    }

    /// <summary>Each kind the bar draws, in order, with the brush of its part.</summary>
    public IReadOnlyList<TimingKindLegendEntry> Legend
    {
        get => _legend;
        private set => SetAndRaise(LegendProperty, ref _legend, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == RowsProperty || change.Property == FirstBrushProperty ||
            change.Property == SecondBrushProperty || change.Property == ThirdBrushProperty ||
            change.Property == FourthBrushProperty || change.Property == OtherBrushProperty)
            Legend = Rows is { } rows ? [.. rows.Select((row, index) => new TimingKindLegendEntry(row, BrushAt(index)))] : [];
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Rows is not { Count: > 0 } || Bounds.Width <= 0) return;
        var x = 0d;
        for (var index = 0; index < Rows.Count; index++)
        {
            var width = Math.Max(0, Math.Min(Bounds.Width - x, Rows[index].ShareOfTotal * Bounds.Width));
            if (BrushAt(index) is { } brush)
                context.FillRectangle(brush, new Rect(x, 0, width, Bounds.Height));
            x += width;
        }
    }

    // The first four kinds get their own colour; every later kind shares the Other brush.
    private IBrush? BrushAt(int index) => index switch
    {
        0 => FirstBrush,
        1 => SecondBrush,
        2 => ThirdBrush,
        3 => FourthBrush,
        _ => OtherBrush,
    };
}
