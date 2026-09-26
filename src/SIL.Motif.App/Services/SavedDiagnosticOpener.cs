using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.Services;

/// <summary>Reads saved diagnostic JSON and sends the validated result, or why it failed, to its caller.</summary>
internal static class SavedDiagnosticOpener
{
    /// <summary>Asks <paramref name="files"/> for a saved diagnostic and processes the chosen file for the caller.</summary>
    /// <param name="files">Shows the open dialog and reads the chosen file.</param>
    /// <param name="showDiagnostic">Displays a diagnostic after it has been read and validated.</param>
    /// <param name="showError">Displays why reading or parsing the chosen file failed.</param>
    /// <param name="cancellationToken">Cancels the dialog or the read.</param>
    /// <returns>A task that completes after the chosen diagnostic has been handled.</returns>
    internal static Task OpenFromPickerAsync(
        IDiagnosticFilePicker files,
        Action<TraceWordViewModel> showDiagnostic,
        Action<WindowRefusal> showError,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(files);
        return OpenAsync(() => files.OpenDiagnosticAsync(cancellationToken), showDiagnostic, showError);
    }

    /// <summary>Reads and parses diagnostic JSON supplied by the caller.</summary>
    /// <param name="readDiagnostic">
    /// Reads the selected file, or returns null when the picker was cancelled; a read that is cancelled counts as a
    /// cancelled pick, not a file that cannot be read.
    /// </param>
    /// <param name="showDiagnostic">Displays a diagnostic after it has been read and validated.</param>
    /// <param name="showError">Displays why reading or parsing the file failed.</param>
    /// <returns>A task that completes after the diagnostic has been handled.</returns>
    internal static async Task OpenAsync(
        Func<Task<string?>> readDiagnostic,
        Action<TraceWordViewModel> showDiagnostic,
        Action<WindowRefusal> showError)
    {
        ArgumentNullException.ThrowIfNull(readDiagnostic);
        ArgumentNullException.ThrowIfNull(showDiagnostic);
        ArgumentNullException.ThrowIfNull(showError);

        string? json;
        try
        {
            json = await readDiagnostic();
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception exception)
        {
            showError(WindowRefusal.Failure(WindowRefusal.DiagnosticUnreadableCode,
                "Motif could not read that diagnostic file.", exception));
            return;
        }
        if (json is null) return;
        if (string.IsNullOrWhiteSpace(json))
        {
            showError(WindowRefusal.From(new Refusal(RefusalCodes.WordTraceMalformedDiagnostic, FailureReason.Refused,
                "The diagnostic file is empty.")));
            return;
        }

        var loaded = TraceWordViewModel.LoadDiagnostic(json);
        if (loaded.Succeeded) showDiagnostic(loaded.Value!);
        else showError(WindowRefusal.From(loaded.Refusal!));
    }
}
