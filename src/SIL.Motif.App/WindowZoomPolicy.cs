using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace SIL.Motif.App;

internal static class WindowZoomPolicy
{
    public const string TransformResourceKey = "App.ZoomTransform";
    private static readonly ScaleTransform SharedTransform = new(1, 1);
    private static readonly ConditionalWeakTable<Control, object> ObservedControls = new();
    private static readonly object Observed = new();

    public static ScaleTransform Transform => SharedTransform;

    public static void Apply(Application application, int percent)
    {
        ArgumentNullException.ThrowIfNull(application);
        var scale = percent / 100d;
        SharedTransform.ScaleX = scale;
        SharedTransform.ScaleY = scale;
        application.Resources[TransformResourceKey] = SharedTransform;
    }

    public static void EnablePopupTransforms(Control root)
    {
        ArgumentNullException.ThrowIfNull(root);
        var pending = new Stack<Control>();
        var visited = new HashSet<Control>(ReferenceEqualityComparer.Instance);
        pending.Push(root);
        while (pending.TryPop(out var control))
        {
            if (!visited.Add(control)) continue;
            if (ObservedControls.TryGetValue(control, out _) == false)
            {
                ObservedControls.Add(control, Observed);
                control.AttachedToVisualTree += OnControlAttached;
            }
            if (control is Button { Flyout: PopupFlyoutBase flyout })
            {
                flyout.Popup.InheritsTransform = true;
                if (flyout is Flyout { Content: Control content }) pending.Push(content);
                if (flyout is MenuFlyout menu)
                    foreach (var item in menu.Items.OfType<Control>()) pending.Push(item);
            }

            if (control.ContextFlyout is PopupFlyoutBase contextFlyout)
                contextFlyout.Popup.InheritsTransform = true;
            foreach (var child in control.GetLogicalChildren().OfType<Control>()) pending.Push(child);
            foreach (var child in control.GetVisualChildren().OfType<Control>()) pending.Push(child);
        }
    }

    private static void OnControlAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is Control control) EnablePopupTransforms(control);
    }
}
