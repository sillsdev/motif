using Avalonia;
using Avalonia.Controls;

namespace SIL.Motif.App.Controls.WordPresentation;

/// <summary>Aligns word marks with the word even when the drawn control includes morphology rows.</summary>
public sealed class WordLinePanel : StackPanel
{
    protected override Size MeasureOverride(Size availableSize)
    {
        var drawn = Children.OfType<InterlinearWord>().FirstOrDefault();
        if (drawn is null) return base.MeasureOverride(availableSize);

        var note = drawn.FieldWorksNote;
        var width = 0d;
        var others = 0d;
        var any = false;
        foreach (var child in Children)
        {
            if (!child.IsVisible || ReferenceEquals(child, note)) continue;
            child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
            width += (any ? Spacing : 0) + child.DesiredSize.Width;
            any = true;
            if (!ReferenceEquals(child, drawn)) others = Math.Max(others, child.DesiredSize.Height);
        }
        return new Size(width, Math.Max(others, Offset(drawn, others) + drawn.DesiredSize.Height));
    }

    // Centres the drawn word's own line on the marks, in whole device pixels so the measure holds the arrangement.
    private double Offset(InterlinearWord drawn, double lineHeight) => InterlinearWord.WholePixels(
        Math.Max(0, lineHeight - drawn.LineMetrics.Word) / 2, TopLevel.GetTopLevel(this)?.RenderScaling ?? 1);

    protected override Size ArrangeOverride(Size finalSize)
    {
        var drawn = Children.OfType<InterlinearWord>().FirstOrDefault();
        if (drawn is null) return base.ArrangeOverride(finalSize);

        var note = drawn.FieldWorksNote;
        var visible = Children.Where(child => child.IsVisible && !ReferenceEquals(child, note)).ToArray();
        var wordHeight = Math.Max(drawn.LineMetrics.Word,
            visible.Where(child => !ReferenceEquals(child, drawn)).Select(child => child.DesiredSize.Height)
                .DefaultIfEmpty().Max());
        var x = 0d;
        var drawnTop = 0d;
        var drawnLeft = 0d;
        foreach (var child in visible)
        {
            var height = ReferenceEquals(child, drawn) ? drawn.LineMetrics.Word : child.DesiredSize.Height;
            var top = ReferenceEquals(child, drawn) ? Offset(drawn, wordHeight) : Math.Max(0, (wordHeight - height) / 2);
            child.Arrange(new Rect(x, top, child.DesiredSize.Width, child.DesiredSize.Height));
            if (ReferenceEquals(child, drawn)) (drawnLeft, drawnTop) = (x, top);
            x += child.DesiredSize.Width + Spacing;
        }
        if (note is { IsVisible: true } && drawn.NoteBand.Height > 0)
            note.Arrange(new Rect(drawnLeft + drawn.NoteBand.X, drawnTop + drawn.NoteBand.Y,
                drawn.NoteBand.Width, drawn.NoteBand.Height));
        return finalSize;
    }
}
