using Avalonia.Controls;
using Avalonia.VisualTree;

namespace SIL.Motif.App.Views;

internal static class KeyboardShortcutStatus
{
    public static void Announce(Control source, string? message) =>
        source.FindAncestorOfType<MainWindow>()?.ShowKeyboardStatus(message);
}
