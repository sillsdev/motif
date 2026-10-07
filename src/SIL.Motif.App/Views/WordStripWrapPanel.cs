using Avalonia;
using Avalonia.Controls;

namespace SIL.Motif.App.Views;

internal sealed class WordStripWrapPanel : WrapPanel
{
    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children)
            child.MaxWidth = availableSize.Width;
        return base.MeasureOverride(availableSize);
    }
}
