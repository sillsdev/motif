using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace SIL.Motif.App.Views;

/// <summary>
/// One number in a table, with a shade behind it scaled to its share of the largest number in its column, so
/// the busiest row is visible before the figures are read. The text keeps full strength; only the shade
/// varies, and a zero carries no shade at all.
/// </summary>
public sealed class HeatCell : Panel
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<HeatCell, string?>(nameof(Text));

    public static readonly StyledProperty<double> IntensityProperty =
        AvaloniaProperty.Register<HeatCell, double>(nameof(Intensity));

    public static readonly StyledProperty<HorizontalAlignment> TextAlignmentProperty =
        AvaloniaProperty.Register<HeatCell, HorizontalAlignment>(nameof(TextAlignment), HorizontalAlignment.Right);

    private readonly Border _shade = new() { CornerRadius = new CornerRadius(3) };
    private readonly CopyableTextBlock _text = new() { Margin = new Thickness(6, 3), FontSize = 12 };

    static HeatCell()
    {
        TextProperty.Changed.AddClassHandler<HeatCell>((cell, _) => cell.Apply());
        IntensityProperty.Changed.AddClassHandler<HeatCell>((cell, _) => cell.Apply());
        TextAlignmentProperty.Changed.AddClassHandler<HeatCell>((cell, _) => cell.Apply());
    }

    public HeatCell()
    {
        _shade.Classes.Add("heat");
        Children.Add(_shade);
        Children.Add(_text);
        Apply();
    }

    /// <summary>The number as the table shows it, already formatted.</summary>
    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>The shade's opacity, from 0 for nothing to about 0.7 for the column's largest value.</summary>
    public double Intensity
    {
        get => GetValue(IntensityProperty);
        set => SetValue(IntensityProperty, value);
    }

    /// <summary>Where the text sits: numbers right, words left.</summary>
    public HorizontalAlignment TextAlignment
    {
        get => GetValue(TextAlignmentProperty);
        set => SetValue(TextAlignmentProperty, value);
    }

    private void Apply()
    {
        _text.Text = Text;
        _text.HorizontalAlignment = TextAlignment;
        _text.TextWrapping = TextAlignment == HorizontalAlignment.Left ? TextWrapping.Wrap : TextWrapping.NoWrap;
        _shade.Opacity = Math.Clamp(Intensity, 0, 1);
    }
}
