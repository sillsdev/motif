using Avalonia;
using Avalonia.Controls;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views;

/// <summary>
/// Puts a <see cref="Verdict"/>'s style class on any control, so a word, a row or a line takes the meaning's
/// colour without each view binding six classes by hand.
/// </summary>
public static class VerdictClasses
{
    public static readonly AttachedProperty<Verdict?> VerdictProperty =
        AvaloniaProperty.RegisterAttached<Control, Verdict?>("Verdict", typeof(VerdictClasses));

    static VerdictClasses()
    {
        VerdictProperty.Changed.AddClassHandler<Control>((control, args) =>
        {
            foreach (var name in Names) control.Classes.Remove(name);
            if (args.NewValue is Verdict verdict) control.Classes.Add(Verdicts.ClassOf(verdict));
        });
    }

    private static readonly string[] Names = ["agrees", "differs", "new", "noresult", "limit", "several"];

    public static void SetVerdict(Control control, Verdict? value) => control.SetValue(VerdictProperty, value);

    public static Verdict? GetVerdict(Control control) => control.GetValue(VerdictProperty);
}
