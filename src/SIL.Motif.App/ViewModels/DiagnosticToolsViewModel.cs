using CommunityToolkit.Mvvm.ComponentModel;
using SIL.Motif.App.Services;

namespace SIL.Motif.App.ViewModels;

/// <summary>
/// The diagnostic tools beside one trace: copy its JSON or the instructions for a chat model, save its JSON,
/// or open a saved diagnostic. A failure shows as <see cref="Error"/> beside the trace; a diagnostic opened
/// here is announced through <see cref="DiagnosticOpened"/> for the view to present in a window of its own.
/// </summary>
public sealed partial class DiagnosticToolsViewModel : ObservableObject
{
    /// <summary>Where a chat model reads how a saved diagnostic is laid out.</summary>
    public const string FormatGuide =
        "https://github.com/sillsdev/motif/blob/main/docs/handoff/trace-diagnostic-format.md";

    /// <summary>What "Copy for a chat model" puts on the clipboard.</summary>
    public const string ChatInstructions =
        "Interpret this saved Motif diagnostic as recorded evidence. Read analyses before attempts, keep parser order, " +
        "treat incomplete search as incomplete even with a success, and label category counts aggregate rather than " +
        "step timing. Format guide: " + FormatGuide;

    private readonly IClipboard _clipboard;
    private readonly IDiagnosticFilePicker _files;

    /// <summary>Puts the tools beside <paramref name="trace"/>.</summary>
    /// <param name="trace">The trace whose diagnostic the tools copy and save.</param>
    /// <param name="clipboard">Where the copy actions put their text.</param>
    /// <param name="files">The dialogs that open and save diagnostic JSON.</param>
    public DiagnosticToolsViewModel(TraceWordViewModel trace, IClipboard clipboard, IDiagnosticFilePicker files)
    {
        ArgumentNullException.ThrowIfNull(trace);
        ArgumentNullException.ThrowIfNull(clipboard);
        ArgumentNullException.ThrowIfNull(files);
        Trace = trace;
        _clipboard = clipboard;
        _files = files;
    }

    /// <summary>The trace whose diagnostic the tools act on.</summary>
    public TraceWordViewModel Trace { get; }

    /// <summary>Why the last action failed, or <see langword="null"/> when it did not.</summary>
    [ObservableProperty]
    private WindowRefusal? _error;

    /// <summary>The file name the save dialog offers: the traced word's, or a generic one without a result.</summary>
    public string SuggestedFileName => $"{Trace.Result?.Word ?? "diagnostic"}.trace.json";

    /// <summary>Raised with the tools for a saved diagnostic that <see cref="OpenAsync"/> read and validated.</summary>
    public event Action<DiagnosticToolsViewModel>? DiagnosticOpened;

    /// <summary>The same tools beside another trace, sharing this one's clipboard and dialogs.</summary>
    public DiagnosticToolsViewModel For(TraceWordViewModel trace) => new(trace, _clipboard, _files);

    /// <summary>Tools beside an empty trace that already say why a saved diagnostic could not be shown.</summary>
    public DiagnosticToolsViewModel ForRefusal(WindowRefusal refusal)
    {
        ArgumentNullException.ThrowIfNull(refusal);
        return new DiagnosticToolsViewModel(new TraceWordViewModel(), _clipboard, _files) { Error = refusal };
    }

    /// <summary>Copies the trace's full diagnostic JSON.</summary>
    public Task CopyJsonAsync() => CopyAsync(Trace.DiagnosticJson, "Motif could not copy the diagnostic.");

    /// <summary>Copies <see cref="ChatInstructions"/>, for pasting beside the diagnostic in a chat.</summary>
    public Task CopyInstructionsAsync() => CopyAsync(ChatInstructions, "Motif could not copy the instructions.");

    /// <summary>Asks where to save the trace's diagnostic JSON and writes it there; a cancelled dialog writes nothing.</summary>
    public async Task SaveAsync()
    {
        Error = null;
        try
        {
            await _files.SaveDiagnosticAsync(SuggestedFileName, Trace.DiagnosticJson).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            Error = NotWritten("Motif could not save the diagnostic file.", exception);
        }
    }

    /// <summary>
    /// Asks for a saved diagnostic and raises <see cref="DiagnosticOpened"/> with it; a file that cannot be read
    /// or is not a diagnostic shows as <see cref="Error"/> here instead.
    /// </summary>
    public Task OpenAsync()
    {
        Error = null;
        return SavedDiagnosticOpener.OpenFromPickerAsync(
            _files, trace => DiagnosticOpened?.Invoke(For(trace)), refusal => Error = refusal);
    }

    private async Task CopyAsync(string text, string failure)
    {
        Error = null;
        try
        {
            await _clipboard.SetTextAsync(text).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            Error = NotWritten(failure, exception);
        }
    }

    private static WindowRefusal NotWritten(string sentence, Exception exception) =>
        WindowRefusal.Failure(WindowRefusal.DiagnosticNotWrittenCode, sentence, exception);
}
