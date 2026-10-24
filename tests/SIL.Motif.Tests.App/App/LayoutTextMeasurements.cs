using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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
        // An AccessText draws its text without the access-key underscore, so that is the width to measure.
        internal static MeasurementKey From(TextBlock text) => new(RenderedText(text), text.FontFamily, text.FontSize,
            text.FontStyle, text.FontWeight, text.FontStretch, text.FlowDirection, text.TextAlignment, text.LineHeight,
            CultureInfo.CurrentCulture.Name, CultureInfo.CurrentUICulture.Name);

        private static string? RenderedText(TextBlock text) =>
            text is AccessText ? string.Concat(text.Text?.Replace("__", "\0", StringComparison.Ordinal).Replace("_", string.Empty, StringComparison.Ordinal).Replace("\0", "_", StringComparison.Ordinal)) : text.Text;

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
