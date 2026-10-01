using Avalonia;
using Avalonia.Controls;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views;

/// <summary>
/// Puts a <see cref="Mark"/>'s kind and value classes on any control, so a bar's part or a chip takes the mark's
/// colour without each view binding a class per value by hand.
/// </summary>
public static class MarkClasses
{
    public static readonly AttachedProperty<Mark?> MarkProperty =
        AvaloniaProperty.RegisterAttached<Control, Mark?>("Mark", typeof(MarkClasses));

    static MarkClasses()
    {
        MarkProperty.Changed.AddClassHandler<Control>((control, args) =>
        {
            if (args.OldValue is Mark old) control.Classes.RemoveAll([old.KindClass, old.Value]);
            if (args.NewValue is Mark mark) control.Classes.AddRange([mark.KindClass, mark.Value]);
        });
    }

    public static void SetMark(Control control, Mark? value) => control.SetValue(MarkProperty, value);

    public static Mark? GetMark(Control control) => control.GetValue(MarkProperty);
}
