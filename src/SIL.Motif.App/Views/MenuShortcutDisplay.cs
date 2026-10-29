using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;

namespace SIL.Motif.App.Views;

/// <summary>
/// Shows each menu item's keyboard shortcut from <see cref="KeyboardShortcutRegistry"/> and nowhere else. A menu item names
/// the behavior it stands for in its <c>Tag</c>; its <c>InputGesture</c> is the registry's first Window-scope binding for it.
/// The gesture is for display: <see cref="MainWindow"/> routes the key itself, so the menu never doubles a binding.
/// </summary>
internal static class MenuShortcutDisplay
{
    /// <summary>Sets <see cref="MenuItem.InputGesture"/> on every item under <paramref name="menu"/> that names a behavior.</summary>
    public static void Apply(Menu menu)
    {
        foreach (var item in menu.GetLogicalDescendants().OfType<MenuItem>())
        {
            NameFromHeader(item);
            item.InputGesture = item.Tag is string name ? GestureFor(name) : null;
            if (item.InputGesture is null || item.Tag is not string behaviorName) continue;
            var text = DisplayTextFor(behaviorName);
            // Avalonia prints Ctrl++ and Shift+/; the registry's text is what Help and the tooltips show.
            item.TemplateApplied += (_, e) => ShowText(item, e.NameScope.Find<TextBlock>("PART_InputGestureText"), text);
            if (item.IsLoaded) ShowText(item, null, text);
        }
    }

    // A screen reader reads the header without its access-key underscore, so that is the item's accessible name.
    private static void NameFromHeader(MenuItem item)
    {
        if (item.Header is string header && string.IsNullOrEmpty(AutomationProperties.GetName(item)))
            AutomationProperties.SetName(item, header.Replace("_", string.Empty, StringComparison.Ordinal));
    }

    private static void ShowText(MenuItem item, TextBlock? part, string? text)
    {
        var block = part ?? item.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(candidate =>
            candidate.Text == item.InputGesture?.ToString());
        if (block is not null) block.Text = text;
    }

    /// <summary>The registry's readable shortcut text for the behavior called <paramref name="behaviorName"/>, or null.</summary>
    public static string? DisplayTextFor(string behaviorName) =>
        Enum.TryParse<KeyboardShortcutBehavior>(behaviorName, out var behavior) &&
        KeyboardShortcutRegistry.FirstBindingFor(KeyboardShortcutScope.Window, behavior) is { } binding
            ? KeyboardShortcutRegistry.DisplayTextFor(binding)
            : null;

    /// <summary>The gesture the registry binds to the behavior called <paramref name="behaviorName"/>, or null.</summary>
    public static KeyGesture? GestureFor(string behaviorName)
    {
        if (!Enum.TryParse<KeyboardShortcutBehavior>(behaviorName, out var behavior)) return null;
        return KeyboardShortcutRegistry.FirstBindingFor(KeyboardShortcutScope.Window, behavior) is { } binding
            ? new KeyGesture(binding.Key, binding.Modifiers)
            : null;
    }
}
