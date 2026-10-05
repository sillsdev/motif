using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace SIL.Motif.App.Controls;

/// <summary>Arranges a running Text's gutter and word strips in the direction selected by its writing system.</summary>
public sealed class RunningTextPanel : Panel
{
    private static readonly ConditionalWeakTable<Control, MinHeightState> OriginalMinHeights = new();
    private FlowDirection _textDirection;

    /// <summary>The direction used to place the gutter and word strips.</summary>
    public FlowDirection TextDirection
    {
        get => _textDirection;
        set
        {
            if (_textDirection == value) return;
            _textDirection = value;
            InvalidateMeasure();
            InvalidateArrange();
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Parts() is not { } parts) return base.MeasureOverride(availableSize);
        parts.Gutter.Measure(availableSize);
        var contentWidth = AvailableContentWidth(availableSize.Width, parts.Gutter.DesiredSize.Width);
        parts.Strips.Measure(new Size(contentWidth, availableSize.Height));
        MatchGutterRows(parts.Gutter, parts.Strips);
        parts.Gutter.Measure(availableSize);
        parts.Strips.Measure(new Size(contentWidth, availableSize.Height));
        var desiredWidth = parts.Gutter.DesiredSize.Width + parts.Strips.DesiredSize.Width;
        var width = double.IsInfinity(availableSize.Width) ? desiredWidth : availableSize.Width;
        return new Size(width, Math.Max(parts.Gutter.DesiredSize.Height, parts.Strips.DesiredSize.Height));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Parts() is not { } parts) return base.ArrangeOverride(finalSize);
        var gutterWidth = Math.Min(finalSize.Width, parts.Gutter.DesiredSize.Width);
        var contentWidth = Math.Max(0, finalSize.Width - gutterWidth);
        var gutterX = TextDirection == FlowDirection.RightToLeft ? contentWidth : 0;
        var stripsX = TextDirection == FlowDirection.RightToLeft ? 0 : gutterWidth;
        parts.Gutter.Arrange(new Rect(gutterX, 0, gutterWidth, finalSize.Height));
        parts.Strips.Arrange(new Rect(stripsX, 0, contentWidth, finalSize.Height));
        return finalSize;
    }

    private static double AvailableContentWidth(double availableWidth, double gutterWidth) =>
        double.IsInfinity(availableWidth) ? double.PositiveInfinity : Math.Max(0, availableWidth - gutterWidth);

    private void MatchGutterRows(StackPanel gutter, ItemsControl strips)
    {
        var heights = strips.GetVisualDescendants().OfType<Control>()
            .Where(control => control.Classes.Contains("stripWordRow") || control.Classes.Contains("analysisRow"))
            .GroupBy(control => control.Classes.Contains("stripWordRow") ? "wordRow" : "analysisRow",
                StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Max(HeightOf), StringComparer.Ordinal);
        foreach (var label in gutter.Children.OfType<Control>())
        {
            label.Classes.Set("rtl", TextDirection == FlowDirection.RightToLeft);
            var key = label.Classes.Contains("wordRow") ? "wordRow" : "analysisRow";
            var baseline = OriginalMinHeights.GetValue(label, control => new MinHeightState(control.MinHeight)).Value;
            label.SetValue(MinHeightProperty, Math.Max(baseline, heights.GetValueOrDefault(key)));
        }
    }

    private static double HeightOf(Control control) => control.DesiredSize.Height;

    private (StackPanel Gutter, ItemsControl Strips)? Parts()
    {
        var gutter = Children.OfType<StackPanel>().FirstOrDefault(child => child.Classes.Contains("gutter"));
        var strips = Children.OfType<ItemsControl>().FirstOrDefault();
        return gutter is null || strips is null ? null : (gutter, strips);
    }

    private sealed record MinHeightState(double Value);
}
