using Avalonia.Controls;
using Avalonia.VisualTree;

namespace SIL.Motif.App.Views;

internal static class KeyboardShortcutStatus
{
    public static void Announce(Control source, string? message) =>
        source.FindAncestorOfType<MainWindow>()?.ShowKeyboardStatus(message);

    /// <summary>Announces through a window found earlier, so a control that detached meanwhile still speaks.</summary>
    public static void Announce(MainWindow? window, string? message) => window?.ShowKeyboardStatus(message);
}
