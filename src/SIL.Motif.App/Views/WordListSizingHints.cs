using Avalonia;
using Avalonia.Controls;

namespace SIL.Motif.App.Views;

internal sealed class WordListSizingHints
{
    private WordListSizingHints() { }

    public static readonly AttachedProperty<double> MinimumProperty =
        AvaloniaProperty.RegisterAttached<WordListSizingHints, Control, double>("Minimum");

    public static readonly AttachedProperty<double> PreferredProperty =
        AvaloniaProperty.RegisterAttached<WordListSizingHints, Control, double>("Preferred");

    public static readonly AttachedProperty<double> MaximumProperty =
        AvaloniaProperty.RegisterAttached<WordListSizingHints, Control, double>("Maximum");

    public static readonly AttachedProperty<double> ColumnGapProperty =
        AvaloniaProperty.RegisterAttached<WordListSizingHints, Control, double>("ColumnGap", inherits: true);

    public static readonly AttachedProperty<double> ScrollbarGutterProperty =
        AvaloniaProperty.RegisterAttached<WordListSizingHints, Control, double>("ScrollbarGutter", inherits: true);

    public static readonly AttachedProperty<double> MinimumViewportWidthProperty =
        AvaloniaProperty.RegisterAttached<WordListSizingHints, Control, double>("MinimumViewportWidth", inherits: true);

    public static double GetMinimum(Control control) => control.GetValue(MinimumProperty);
    public static void SetMinimum(Control control, double value) => control.SetValue(MinimumProperty, value);
    public static double GetPreferred(Control control) => control.GetValue(PreferredProperty);
    public static void SetPreferred(Control control, double value) => control.SetValue(PreferredProperty, value);
    public static double GetMaximum(Control control) => control.GetValue(MaximumProperty);
    public static void SetMaximum(Control control, double value) => control.SetValue(MaximumProperty, value);
    public static double GetColumnGap(Control control) => control.GetValue(ColumnGapProperty);
    public static void SetColumnGap(Control control, double value) => control.SetValue(ColumnGapProperty, value);
    public static double GetScrollbarGutter(Control control) => control.GetValue(ScrollbarGutterProperty);
    public static void SetScrollbarGutter(Control control, double value) => control.SetValue(ScrollbarGutterProperty, value);
    public static double GetMinimumViewportWidth(Control control) => control.GetValue(MinimumViewportWidthProperty);
    public static void SetMinimumViewportWidth(Control control, double value) => control.SetValue(MinimumViewportWidthProperty, value);
}
