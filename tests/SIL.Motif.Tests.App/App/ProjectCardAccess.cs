using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using SIL.Motif.App.Views;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>Opens and closes the project card the way the chip does, so a test can read what the card tells.</summary>
internal static class ProjectCardAccess
{
    /// <summary>The chip that opens the card.</summary>
    public static Button Chip(MainWindow window) => window.FindInWindowOrWorkspace<Button>("ProjectChip")!;

    /// <summary>Opens the card under the chip and returns it, laid out and rendered.</summary>
    public static ProjectCardView Open(MainWindow window)
    {
        var chip = Chip(window);
        var flyout = Assert.IsType<Flyout>(chip.Flyout);
        flyout.ShowAt(chip);
        PageScreenshots.Settle(window);
        Assert.True(flyout.IsOpen, "The project card did not open.");
        return Assert.IsType<ProjectCardView>(flyout.Content);
    }

    /// <summary>Closes the card and lets focus settle.</summary>
    public static void Close(MainWindow window)
    {
        Assert.IsType<Flyout>(Chip(window).Flyout).Hide();
        Dispatcher.UIThread.RunJobs();
        PageScreenshots.Settle(window);
    }
}
