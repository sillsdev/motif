using Avalonia;
using Avalonia.Controls;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views;

/// <summary>A decorative Overview table bar showing a recorded value as a share of its total.</summary>
public sealed class OverviewShareBar : Grid
{
    /// <summary>The property for the recorded value.</summary>
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<OverviewShareBar, double>(nameof(Value));
    /// <summary>The property for the matching total.</summary>
    public static readonly StyledProperty<double> TotalProperty =
        AvaloniaProperty.Register<OverviewShareBar, double>(nameof(Total));
    /// <summary>The property for the semantic colour mark.</summary>
    public static readonly StyledProperty<Mark?> MarkProperty =
        AvaloniaProperty.Register<OverviewShareBar, Mark?>(nameof(Mark));
    /// <summary>The property for the recorded timing kind.</summary>
    public static readonly StyledProperty<string?> KindProperty =
        AvaloniaProperty.Register<OverviewShareBar, string?>(nameof(Kind));

    private readonly Border _fill = new() { Classes = { "outcomeSegment", "overviewShareFill" } };

    /// <summary>Creates a decorative bar whose fill follows its semantic or timing classes.</summary>
    public OverviewShareBar()
    {
        Classes.Add("overviewShareBar");
        ColumnDefinitions.Add(new ColumnDefinition(0, GridUnitType.Star));
        ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
        Children.Add(_fill);
        IsHitTestVisible = false;
    }

    /// <summary>The recorded count or duration represented by this row.</summary>
    public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }

    /// <summary>The total in the same units as <see cref="Value"/>.</summary>
    public double Total { get => GetValue(TotalProperty); set => SetValue(TotalProperty, value); }

    /// <summary>The named outcome or meaning whose shared colour fills the bar.</summary>
    public Mark? Mark { get => GetValue(MarkProperty); set => SetValue(MarkProperty, value); }

    /// <summary>The parser's timing kind when the row represents a share of word time.</summary>
    public string? Kind { get => GetValue(KindProperty); set => SetValue(KindProperty, value); }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ValueProperty || change.Property == TotalProperty)
        {
            var share = Total > 0 && double.IsFinite(Value) && double.IsFinite(Total)
                ? Math.Clamp(Value / Total, 0, 1) : 0;
            var remainder = 1 - share;
            ColumnDefinitions[0].Width = new GridLength(share, GridUnitType.Star);
            ColumnDefinitions[1].Width = new GridLength(remainder, GridUnitType.Star);
        }
        if (change.Property == MarkProperty) MarkClasses.SetMark(_fill, Mark);
        if (change.Property == KindProperty)
        {
            if (change.OldValue is string oldKind) _fill.Classes.Remove(oldKind);
            if (Kind is { Length: > 0 } kind) _fill.Classes.Add(kind);
        }
    }
}
