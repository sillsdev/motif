using Avalonia.Controls;
using Avalonia.Platform.Storage;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Services;

/// <summary>Reads saved diagnostic JSON and sends the validated result or error to a view.</summary>
internal static class SavedDiagnosticOpener
{
    /// <summary>Opens the diagnostic file picker and processes the selected file for the caller.</summary>
    /// <param name="owner">The top-level window that owns the file picker.</param>
    /// <param name="showDiagnostic">Displays a diagnostic after it has been read and validated.</param>
    /// <param name="showError">Displays an error encountered while choosing, reading, or parsing the file.</param>
    /// <returns>A task that completes after the selected diagnostic has been handled.</returns>
    internal static Task OpenFromPickerAsync(
        TopLevel owner,
        Action<TraceWordViewModel> showDiagnostic,
        Action<string> showError)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(showDiagnostic);
        ArgumentNullException.ThrowIfNull(showError);

        return OpenAsync(async () =>
        {
            var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Open diagnostic JSON",
                AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("Motif diagnostic JSON") { Patterns = ["*.json"] }],
            });
            if (files.Count == 0) return null;

            await using var stream = await files[0].OpenReadAsync();
            using var reader = new StreamReader(stream);
            return await reader.ReadToEndAsync();
        }, showDiagnostic, showError);
    }

    /// <summary>Reads and parses diagnostic JSON supplied by the caller.</summary>
    /// <param name="readDiagnostic">Reads the selected file, or returns null when the picker was cancelled.</param>
    /// <param name="showDiagnostic">Displays a diagnostic after it has been read and validated.</param>
    /// <param name="showError">Displays an error encountered while reading or parsing the file.</param>
    /// <returns>A task that completes after the diagnostic has been handled.</returns>
    internal static async Task OpenAsync(
        Func<Task<string?>> readDiagnostic,
        Action<TraceWordViewModel> showDiagnostic,
        Action<string> showError)
    {
        ArgumentNullException.ThrowIfNull(readDiagnostic);
        ArgumentNullException.ThrowIfNull(showDiagnostic);
        ArgumentNullException.ThrowIfNull(showError);

        try
        {
            var json = await readDiagnostic();
            if (json is null) return;
            showDiagnostic(TraceWordViewModel.FromDiagnosticJson(json));
        }
        catch (Exception exception)
        {
            showError(exception.Message);
        }
    }
}
