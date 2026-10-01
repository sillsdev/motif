using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace SIL.Motif.App.Views;

/// <summary>
/// The keyboard focus ring every control in the window shows: the <c>focusRing</c> component style gives it the
/// focus colour and stroke, and it copies the corners of the control it surrounds, so a rounded card and a square
/// cell each get a ring that follows their own edge.
/// </summary>
/// <remarks>
/// Avalonia builds the ring only for focus that arrives by Tab or the arrow keys, so a pointer click leaves none.
/// The component style installs it as the adorner layer's default focus adorner.
/// </remarks>
public sealed class FocusRing : Border
{
    public FocusRing() => Classes.Add("focusRing");

    protected override Type StyleKeyOverride => typeof(Border);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        CornerRadius = AdornerLayer.GetAdornedElement(this) switch
        {
            TemplatedControl templated => templated.CornerRadius,
            Border border => border.CornerRadius,
            _ => default,
        };
    }
}
