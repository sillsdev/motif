using Avalonia;
using Avalonia.Controls;

namespace SIL.Motif.App.Controls.WordPresentation;

internal sealed class WordListLayoutScope
{
    private WordListLayoutScope() { }

    internal static readonly AttachedProperty<WordListLayout?> LayoutProperty =
        AvaloniaProperty.RegisterAttached<WordListLayoutScope, Control, WordListLayout?>("Layout", inherits: true);

    internal static WordListLayout? GetLayout(Control control) => control.GetValue(LayoutProperty);

    internal static void SetLayout(Control control, WordListLayout? layout) => control.SetValue(LayoutProperty, layout);
}
