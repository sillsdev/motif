using System.Reflection;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace SIL.Motif.Tests.App;

/// <summary>
/// The control an open flyout presents. A flyout whose content is a view model builds its menu from a
/// template only when it opens, so the menu lives in the flyout's popup rather than in its Content.
/// </summary>
internal static class FlyoutContent
{
    private static readonly PropertyInfo PopupProperty = typeof(PopupFlyoutBase)
        .GetProperty("Popup", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Avalonia's flyouts no longer expose their popup.");

    internal static Control Of(Flyout flyout) =>
        flyout.Content as Control
        ?? (PopupProperty.GetValue(flyout) as Popup)?.Child as Control
        ?? throw new InvalidOperationException("The flyout is not showing any content.");
}
