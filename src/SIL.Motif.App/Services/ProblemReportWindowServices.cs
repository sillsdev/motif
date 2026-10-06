using Avalonia.Controls;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Services;

/// <summary>Creates a report preview using the text copier and browser associated with its window.</summary>
/// <param name="Clipboard">The text copier, or the window's native copier when absent.</param>
/// <param name="Launcher">The browser adapter, or the window's native launcher when absent.</param>
public sealed record ProblemReportWindowServices(IClipboard? Clipboard = null, IUriLauncher? Launcher = null)
{
    /// <summary>Creates the native adapters for one window.</summary>
    /// <param name="window">The owner of the native adapters.</param>
    /// <param name="launcher">The adapter that opens online links.</param>
    public static ProblemReportWindowServices ForWindow(TopLevel window, IUriLauncher launcher)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(launcher);
        return new ProblemReportWindowServices(new AvaloniaClipboard(window), launcher);
    }

    /// <summary>Copies text using the adapter associated with one window.</summary>
    /// <param name="text">The text to copy.</param>
    /// <param name="window">The owner of the native text service.</param>
    public Task CopyTextAsync(string text, TopLevel window)
    {
        ArgumentNullException.ThrowIfNull(window);
        return (Clipboard ?? new AvaloniaClipboard(window)).SetTextAsync(text);
    }

    /// <summary>Builds a preview model whose external actions belong to <paramref name="window"/>.</summary>
    /// <param name="report">The report to show.</param>
    /// <param name="window">The top-level window that owns the clipboard and browser.</param>
    public ProblemReportPreviewViewModel ModelFor(ProblemReport report, TopLevel window)
    {
        ArgumentNullException.ThrowIfNull(window);
        return new ProblemReportPreviewViewModel(report,
            Clipboard ?? new AvaloniaClipboard(window), Launcher ?? new AvaloniaLauncher(window));
    }
}
