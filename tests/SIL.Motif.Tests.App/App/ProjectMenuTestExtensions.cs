using Avalonia.Controls;
using SIL.Motif.App.Views;

namespace SIL.Motif.Tests.App;

/// <summary>Opens the File menu and its Open Recent submenu in a headless window, as a person would with the pointer.</summary>
internal static class ProjectMenuTestExtensions
{
    public static MenuItem OpenFileMenu(this MainWindow window)
    {
        var file = window.FindControl<MenuItem>("FileMenu")!;
        file.IsSubMenuOpen = true;
        return file;
    }

    public static MenuItem OpenRecentMenu(this MainWindow window)
    {
        window.OpenFileMenu();
        var recent = window.FindControl<MenuItem>("OpenRecentMenuItem")!;
        recent.IsSubMenuOpen = true;
        return recent;
    }
}
