using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.Views;

/// <summary>Draws the command's kind shares across one full-width bar.</summary>
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

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Rows is not { Count: > 0 } || Bounds.Width <= 0) return;
        var brushes = new[] { FirstBrush, SecondBrush, ThirdBrush, FourthBrush, OtherBrush };
        var x = 0d;
        for (var index = 0; index < Rows.Count; index++)
        {
            var width = Math.Max(0, Math.Min(Bounds.Width - x, Rows[index].ShareOfTotal * Bounds.Width));
            if (brushes[Math.Min(index, brushes.Length - 1)] is { } brush)
                context.FillRectangle(brush, new Rect(x, 0, width, Bounds.Height));
            x += width;
        }
    }
}
