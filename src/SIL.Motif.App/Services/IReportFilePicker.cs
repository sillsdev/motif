namespace SIL.Motif.App.Services;

/// <summary>
/// Lets the error window save its report without depending on Avalonia's storage APIs. The adapter shows the
/// dialog and writes the file the person chose, so only text crosses this seam.
/// </summary>
public interface IReportFilePicker
{
    /// <summary>
    /// Prompts for where to save a text report, offering <paramref name="suggestedFileName"/>, and writes
    /// <paramref name="text"/> there, replacing what the file held. Returns <c>false</c> when the person cancels;
    /// throws when the file cannot be written.
    /// </summary>
    Task<bool> SaveReportAsync(string suggestedFileName, string text, CancellationToken cancellationToken = default);
}
