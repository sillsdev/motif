namespace SIL.Motif.App.Services;

/// <summary>
/// Lets a view model open and save diagnostic JSON without depending on Avalonia's storage APIs. The adapter
/// shows the dialog and reads or writes the file the person chose, so only text crosses this seam.
/// </summary>
public interface IDiagnosticFilePicker
{
    /// <summary>
    /// Prompts for a saved diagnostic and returns the chosen file's text. Returns <c>null</c> when the user
    /// cancels; throws when the chosen file cannot be read.
    /// </summary>
    Task<string?> OpenDiagnosticAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Prompts for where to save a diagnostic, offering <paramref name="suggestedFileName"/>, and writes
    /// <paramref name="json"/> there, replacing what the file held. Returns <c>false</c> when the user cancels;
    /// throws when the file cannot be written.
    /// </summary>
    Task<bool> SaveDiagnosticAsync(string suggestedFileName, string json, CancellationToken cancellationToken = default);
}
