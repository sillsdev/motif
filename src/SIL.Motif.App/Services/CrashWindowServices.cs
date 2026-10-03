namespace SIL.Motif.App.Services;

/// <summary>
/// The clipboard and browser the error window's report preview uses. Each one left <see langword="null"/>
/// belongs to the preview window rather than the stopped main window.
/// </summary>
/// <param name="Clipboard">Takes Copy report, or <see langword="null"/> for the preview's own clipboard.</param>
/// <param name="Launcher">Opens the issue form, or <see langword="null"/> for the preview's own launcher.</param>
public sealed record CrashWindowServices(
    IClipboard? Clipboard = null, IUriLauncher? Launcher = null)
{
    /// <summary>The report preview adapters used by the error window.</summary>
    public ProblemReportWindowServices PreviewServices => new(Clipboard, Launcher);
}
