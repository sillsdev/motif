using Avalonia.Controls;
using SIL.Motif.App.Views;

namespace SIL.Motif.Tests.App;

internal static class WindowControlLookup
{
    /// <summary>Finds a named control, looking inside the hosted workspace view when the window is a <see cref="MainWindow"/>.</summary>
    public static T? FindInWindowOrWorkspace<T>(this Window window, string name) where T : Control =>
        window is MainWindow main
            ? main.FindControl<T>(name)
            : NameScope.GetNameScope(window)?.Find(name) as T;
}
