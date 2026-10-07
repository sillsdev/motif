using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using SIL.Motif.App.Controls.WordPresentation;

namespace SIL.Motif.App.Controls;

internal readonly record struct RunningTextLineMetrics(double Word, double FieldWorks, double PanGloss)
{
    public RunningTextLineMetrics Max(RunningTextLineMetrics other) => new(
        Math.Max(Word, other.Word), Math.Max(FieldWorks, other.FieldWorks), Math.Max(PanGloss, other.PanGloss));

    public double HeightAt(int index) => index switch
    {
        0 => Word,
        1 => FieldWorks,
        2 => PanGloss,
        _ => 0,
    };
}

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
        SetGutterRows(parts.Gutter, StripMetrics(parts.Strips));
        parts.Gutter.Measure(availableSize);
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

    private static RunningTextLineMetrics StripMetrics(ItemsControl strips)
    {
        var metrics = strips.GetRealizedContainers()
            .Select(container => container switch
            {
                WordStripToken { IsVisible: true } strip => strip.LineMetrics,
                ContentPresenter { Child: WordStripToken { IsVisible: true } strip } => strip.LineMetrics,
                _ => default,
            })
            .Aggregate(default(RunningTextLineMetrics), static (current, next) => current.Max(next));
        return metrics;
    }

    private void SetGutterRows(StackPanel gutter, RunningTextLineMetrics metrics)
    {
        for (var index = 0; index < gutter.Children.Count; index++)
        {
            if (gutter.Children[index] is not Control label) continue;
            label.Classes.Set("rtl", TextDirection == FlowDirection.RightToLeft);
            var baseline = OriginalMinHeights.GetValue(label, control => new MinHeightState(control.MinHeight)).Value;
            label.SetValue(MinHeightProperty, Math.Max(baseline, metrics.HeightAt(index)));
        }
    }

    private (StackPanel Gutter, ItemsControl Strips)? Parts()
    {
        var gutter = Children.OfType<StackPanel>().FirstOrDefault(child => child.Classes.Contains("gutter"));
        var strips = Children.OfType<ItemsControl>().FirstOrDefault();
        return gutter is null || strips is null ? null : (gutter, strips);
    }

    private sealed record MinHeightState(double Value);
}
