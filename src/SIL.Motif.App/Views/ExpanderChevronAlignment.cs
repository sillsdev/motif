using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.VisualTree;

namespace SIL.Motif.App.Views;

/// <summary>Keeps the standard Expander chevron inside the header's layout column.</summary>
public static class ExpanderChevronAlignment
{
    /// <summary>Whether the Expander's template chevron receives the app's inset.</summary>
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<Expander, bool>("IsEnabled", typeof(ExpanderChevronAlignment));

    private sealed class State
    {
        public bool WaitingForLayout { get; set; }
    }

    private static readonly ConditionalWeakTable<Expander, State> States = new();

    static ExpanderChevronAlignment()
    {
        IsEnabledProperty.Changed.AddClassHandler<Expander>(OnEnabledChanged);
    }

    /// <summary>Gets whether <paramref name="expander"/> keeps its chevron inside its header column.</summary>
    public static bool GetIsEnabled(Expander expander) => expander.GetValue(IsEnabledProperty);

    /// <summary>Sets whether <paramref name="expander"/> keeps its chevron inside its header column.</summary>
    public static void SetIsEnabled(Expander expander, bool value) => expander.SetValue(IsEnabledProperty, value);

    private static void OnEnabledChanged(Expander expander, AvaloniaPropertyChangedEventArgs change)
    {
        if (change.GetNewValue<bool>())
        {
            States.GetValue(expander, _ => new State());
            expander.TemplateApplied += OnTemplateApplied;
            expander.AttachedToVisualTree += OnAttachedToVisualTree;
            expander.DetachedFromVisualTree += OnDetachedFromVisualTree;
            ApplyOrWait(expander);
            return;
        }

        expander.TemplateApplied -= OnTemplateApplied;
        expander.AttachedToVisualTree -= OnAttachedToVisualTree;
        expander.DetachedFromVisualTree -= OnDetachedFromVisualTree;
        StopWaitingForLayout(expander);
        States.Remove(expander);
    }

    private static void OnTemplateApplied(object? sender, TemplateAppliedEventArgs e)
    {
        if (sender is Expander expander) ApplyOrWait(expander);
    }

    private static void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is Expander expander) ApplyOrWait(expander);
    }

    private static void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is Expander expander) StopWaitingForLayout(expander);
    }

    private static void OnLayoutUpdated(object? sender, EventArgs e)
    {
        if (sender is Expander expander) ApplyOrWait(expander);
    }

    private static void ApplyOrWait(Expander expander)
    {
        if (Application.Current?.TryGetResource(
                "Component.Interaction.ExpanderChevronMargin", expander.ActualThemeVariant, out var value) == true &&
            value is Thickness margin)
        {
            var chevron = expander.GetVisualDescendants().OfType<PathIcon>()
                .FirstOrDefault(icon => icon.Name == "PART_PathIcon");
            if (chevron is not null)
            {
                chevron.SetCurrentValue(Layoutable.MarginProperty, margin);
                StopWaitingForLayout(expander);
                return;
            }
        }

        var state = States.GetValue(expander, _ => new State());
        if (state.WaitingForLayout) return;
        state.WaitingForLayout = true;
        expander.LayoutUpdated += OnLayoutUpdated;
    }

    private static void StopWaitingForLayout(Expander expander)
    {
        if (!States.TryGetValue(expander, out var state) || !state.WaitingForLayout) return;
        expander.LayoutUpdated -= OnLayoutUpdated;
        state.WaitingForLayout = false;
    }
}
