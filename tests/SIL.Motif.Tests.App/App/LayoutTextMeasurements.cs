using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace SIL.Motif.Tests.App;

/// <summary>Reuses unconstrained text widths within one invariant run across its sizes and zoom levels.</summary>
internal sealed class LayoutTextMeasurements
{
    private readonly Dictionary<MeasurementKey, double> _widths = [];

    internal int ComputedWidths => _widths.Count;
    internal int ReusedWidths { get; private set; }

    internal double UnwrappedWidth(TextBlock text)
    {
        var key = MeasurementKey.From(text);
        if (_widths.TryGetValue(key, out var width))
        {
            ReusedWidths++;
            return width;
        }
        width = key.MeasureWidth();
        _widths.Add(key, width);
        return width;
    }

    internal static double FreshWidth(TextBlock text) => MeasurementKey.From(text).MeasureWidth();

    private readonly record struct MeasurementKey(string? Text, FontFamily FontFamily, double FontSize,
        FontStyle FontStyle, FontWeight FontWeight, FontStretch FontStretch, FlowDirection FlowDirection,
        TextAlignment TextAlignment, double LineHeight, string Culture, string UiCulture)
    {
        internal static MeasurementKey From(TextBlock text) => new(text.Text, text.FontFamily, text.FontSize,
            text.FontStyle, text.FontWeight, text.FontStretch, text.FlowDirection, text.TextAlignment, text.LineHeight,
            CultureInfo.CurrentCulture.Name, CultureInfo.CurrentUICulture.Name);

        internal double MeasureWidth()
        {
            var measure = new TextBlock
            {
                Text = Text, FontFamily = FontFamily, FontSize = FontSize, FontStyle = FontStyle,
                FontWeight = FontWeight, FontStretch = FontStretch, FlowDirection = FlowDirection,
                TextAlignment = TextAlignment, LineHeight = LineHeight,
                TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.None,
            };
            measure.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return measure.DesiredSize.Width;
        }
    }
}
