using Avalonia;
using Avalonia.Controls;

namespace SIL.Motif.App.Views;

internal sealed class WordStripWrapPanel : WrapPanel
{
    protected override Size MeasureOverride(Size availableSize)
    {
        // Only a width that moved is written, since a changed MaxWidth invalidates a child.
        var width = availableSize.Width;
        foreach (var child in Children)
            if (!child.MaxWidth.Equals(width)) child.MaxWidth = width;
        return base.MeasureOverride(availableSize);
    }
}
