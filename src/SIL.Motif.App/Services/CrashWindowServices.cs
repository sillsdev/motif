using Avalonia.Controls;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Host;

namespace SIL.Motif.App.Services;

/// <summary>
/// The clipboard, save dialog and mail launcher Motif's error window uses. Each one left <see langword="null"/>
/// is that window's own native one, so its save dialog belongs to it rather than to the stopped main window.
/// </summary>
/// <param name="Clipboard">Takes Copy details, or <see langword="null"/> for the window's own clipboard.</param>
/// <param name="ReportFiles">Saves the report, or <see langword="null"/> for the window's own save dialog.</param>
/// <param name="Launcher">Opens the mail program, or <see langword="null"/> for the window's own launcher.</param>
public sealed record CrashWindowServices(
    IClipboard? Clipboard = null, IReportFilePicker? ReportFiles = null, IUriLauncher? Launcher = null)
{
    /// <summary>What <paramref name="window"/> shows about <paramref name="report"/>, acting through these services.</summary>
    public CrashReportViewModel ModelFor(CrashReport report, TopLevel window)
    {
        ArgumentNullException.ThrowIfNull(window);
        return new CrashReportViewModel(report,
            Clipboard ?? new AvaloniaClipboard(window),
            ReportFiles ?? new AvaloniaStoragePickers(window),
            Launcher ?? new AvaloniaLauncher(window),
            MotifSupport.SupportEmail);
    }
}
