using Avalonia.Controls;
using Avalonia.Platform.Storage;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.Services;

/// <summary>Reads saved diagnostic JSON and sends the validated result, or why it failed, to a view.</summary>
internal static class SavedDiagnosticOpener
{
    /// <summary>Opens the diagnostic file picker and processes the selected file for the caller.</summary>
    /// <param name="owner">The top-level window that owns the file picker.</param>
    /// <param name="showDiagnostic">Displays a diagnostic after it has been read and validated.</param>
    /// <param name="showError">Displays why choosing, reading, or parsing the file failed.</param>
    /// <returns>A task that completes after the selected diagnostic has been handled.</returns>
    internal static Task OpenFromPickerAsync(
        TopLevel owner,
        Action<TraceWordViewModel> showDiagnostic,
        Action<WindowRefusal> showError)
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
