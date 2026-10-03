using Avalonia.Controls;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Services;

/// <summary>Creates a report preview using the clipboard and browser associated with its window.</summary>
/// <param name="Clipboard">The clipboard adapter, or the window's native clipboard when absent.</param>
/// <param name="Launcher">The browser adapter, or the window's native launcher when absent.</param>
public sealed record ProblemReportWindowServices(IClipboard? Clipboard = null, IUriLauncher? Launcher = null)
{
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
