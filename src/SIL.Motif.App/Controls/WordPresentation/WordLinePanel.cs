using Avalonia;
using Avalonia.Controls;

namespace SIL.Motif.App.Controls.WordPresentation;

/// <summary>Aligns word marks with the word even when the drawn control includes morphology rows.</summary>
public sealed class WordLinePanel : StackPanel
{
    protected override Size ArrangeOverride(Size finalSize)
    {
        var drawn = Children.OfType<InterlinearWord>().FirstOrDefault();
        if (drawn is null) return base.ArrangeOverride(finalSize);

        var visible = Children.Where(child => child.IsVisible).ToArray();
        var wordHeight = Math.Max(drawn.LineMetrics.Word,
            visible.Where(child => !ReferenceEquals(child, drawn)).Select(child => child.DesiredSize.Height)
                .DefaultIfEmpty().Max());
        var x = 0d;
        foreach (var child in visible)
        {
            var height = ReferenceEquals(child, drawn) ? drawn.LineMetrics.Word : child.DesiredSize.Height;
            child.Arrange(new Rect(x, Math.Max(0, (wordHeight - height) / 2),
                child.DesiredSize.Width, child.DesiredSize.Height));
            x += child.DesiredSize.Width + Spacing;
        }
        return finalSize;
    }
}
