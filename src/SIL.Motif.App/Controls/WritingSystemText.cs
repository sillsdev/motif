using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.VisualTree;
using System.Runtime.CompilerServices;
using SIL.Motif.App.Services;

namespace SIL.Motif.App.Controls;

/// <summary>Marks language text and lets the shared resolver apply its FieldWorks display settings.</summary>
public sealed class WritingSystemText
{
    private WritingSystemText() { }

    private static readonly ConditionalWeakTable<Control, object> TrackedControls = new();
    private static readonly List<WeakReference<Control>> Controls = [];

    public static readonly AttachedProperty<WritingSystemTextStyleResolver?> ResolverProperty =
        AvaloniaProperty.RegisterAttached<WritingSystemText, Control, WritingSystemTextStyleResolver?>(
            "Resolver", inherits: true);

    public static readonly AttachedProperty<string?> IdProperty =
        AvaloniaProperty.RegisterAttached<WritingSystemText, Control, string?>("Id");

    public static readonly AttachedProperty<string?> StyleNameProperty =
        AvaloniaProperty.RegisterAttached<WritingSystemText, Control, string?>("StyleName");

    public static readonly AttachedProperty<string?> FlowIdProperty =
        AvaloniaProperty.RegisterAttached<WritingSystemText, Control, string?>("FlowId", inherits: true);

    public static readonly AttachedProperty<bool> FlowEnabledProperty =
        AvaloniaProperty.RegisterAttached<WritingSystemText, Control, bool>("FlowEnabled");

    public static readonly AttachedProperty<bool> WindowTextProperty =
        AvaloniaProperty.RegisterAttached<WritingSystemText, Control, bool>("WindowText");

    static WritingSystemText()
    {
        ResolverProperty.Changed.AddClassHandler<Control>((control, _) => Apply(control));
        IdProperty.Changed.AddClassHandler<Control>((control, _) => Apply(control));
        StyleNameProperty.Changed.AddClassHandler<Control>((control, _) => Apply(control));
        FlowIdProperty.Changed.AddClassHandler<Control>((control, _) => Apply(control));
        FlowEnabledProperty.Changed.AddClassHandler<Control>((control, _) => Apply(control));
        WindowTextProperty.Changed.AddClassHandler<Control>((control, _) => Apply(control));
        TextBlock.TextProperty.Changed.AddClassHandler<TextBlock>((control, _) => CheckMissingFontNotice(control));
        TextBox.TextProperty.Changed.AddClassHandler<TextBox>((control, _) => CheckMissingFontNotice(control));
        Visual.IsVisibleProperty.Changed.AddClassHandler<Control>((control, _) => RefreshVisibleControls(control));
    }

    /// <summary>Sets the resolver inherited by language-text controls below <paramref name="control"/>.</summary>
    public static void SetResolver(Control control, WritingSystemTextStyleResolver resolver) =>
        control.SetValue(ResolverProperty, resolver);

    /// <summary>Reapplies saved settings to live text controls after the project inventory changes.</summary>
    public static void RefreshResolver(WritingSystemTextStyleResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        for (var index = Controls.Count - 1; index >= 0; index--)
        {
            if (!Controls[index].TryGetTarget(out var control))
            {
                Controls.RemoveAt(index);
                continue;
            }
            if (ReferenceEquals(GetResolver(control), resolver)) Apply(control);
        }
    }

    /// <summary>Sets the writing-system id that selects a language-text control's saved settings.</summary>
    public static void SetId(Control control, string? value) => control.SetValue(IdProperty, value);

    /// <summary>Sets the FieldWorks style name used for a language-text control.</summary>
    public static void SetStyleName(Control control, string? value) => control.SetValue(StyleNameProperty, value);
    public static void SetFlowId(Control control, string? value) => control.SetValue(FlowIdProperty, value);
    public static void SetFlowEnabled(Control control, bool value) => control.SetValue(FlowEnabledProperty, value);
    public static void SetWindowText(Control control, bool value) => control.SetValue(WindowTextProperty, value);

    public static WritingSystemTextStyleResolver? GetResolver(Control control) => control.GetValue(ResolverProperty);
    public static string? GetId(Control control) => control.GetValue(IdProperty);
    public static string? GetStyleName(Control control) => control.GetValue(StyleNameProperty);
    public static string? GetFlowId(Control control) => control.GetValue(FlowIdProperty);
    public static bool GetFlowEnabled(Control control) => control.GetValue(FlowEnabledProperty);
    public static bool GetWindowText(Control control) => control.GetValue(WindowTextProperty);

    private static void Apply(Control control)
    {
        if (GetStyleName(control) is null && !GetFlowEnabled(control) && !GetWindowText(control)) return;
        if (!TrackedControls.TryGetValue(control, out _))
        {
            TrackedControls.Add(control, new object());
            Controls.Add(new WeakReference<Control>(control));
            control.AttachedToVisualTree += (_, _) => Apply(control);
        }
        if (GetResolver(control) is not { } resolver)
        {
            control.ClearValue(TextElement.FontFamilyProperty);
            control.ClearValue(TextElement.FontFeaturesProperty);
            control.ClearValue(TextElement.FontSizeProperty);
            control.ClearValue(Visual.FlowDirectionProperty);
            return;
        }

        if (GetStyleName(control) is { } styleName)
            resolver.Apply(control, GetId(control), styleName);
        else
        {
            control.ClearValue(TextElement.FontFamilyProperty);
            control.ClearValue(TextElement.FontFeaturesProperty);
            control.ClearValue(TextElement.FontSizeProperty);
        }

        if (GetWindowText(control)) resolver.ApplyWindowDirection(control);
        else if (GetFlowEnabled(control)) resolver.ApplyFlowDirection(control, GetFlowId(control));
    }

    private static void RefreshVisibleControls(Control root)
    {
        foreach (var control in root.GetVisualDescendants().Prepend(root).OfType<Control>())
            if (TrackedControls.TryGetValue(control, out _) && control.IsEffectivelyVisible)
                CheckMissingFontNotice(control);
    }

    private static void CheckMissingFontNotice(Control control)
    {
        if (GetStyleName(control) is { } styleName && GetResolver(control) is { } resolver)
            resolver.RegisterMissingFontNotice(control, GetId(control), styleName, control switch
            {
                TextBlock textBlock => textBlock.Text,
                TextBox textBox => textBox.Text,
                _ => null,
            });
    }
}
