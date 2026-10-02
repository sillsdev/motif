using CommunityToolkit.Mvvm.ComponentModel;
using SIL.Motif.App.Services;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.ViewModels;

/// <summary>A saved diagnostic that was chosen, to be shown in a window of its own.</summary>
/// <param name="Trace">The diagnostic's trace, or an empty trace when the file could not be shown.</param>
/// <param name="Refusal">Why the file could not be shown, or <see langword="null"/> when it was.</param>
public sealed record OpenedDiagnostic(TraceWordViewModel Trace, WindowRefusal? Refusal);

/// <summary>
/// The diagnostic tools beside one trace: copy its JSON or the instructions for a chat model, save its JSON,
/// or open a saved diagnostic. A failure shows as <see cref="Error"/> beside the trace; a diagnostic opened
/// here is announced through <see cref="DiagnosticOpened"/> for the view to present in a window of its own.
/// </summary>
/// <remarks>
/// Each new action clears the last action's <see cref="Error"/> before it starts, so an error left from an earlier
/// attempt never stands beside a new one.
/// </remarks>
public sealed partial class DiagnosticToolsViewModel : ObservableObject
{
    /// <summary>Where a chat model reads how a saved diagnostic is laid out.</summary>
    public const string FormatGuide =
        "https://github.com/sillsdev/motif/blob/main/docs/handoff/trace-diagnostic-format.md";

    /// <summary>The evidence-handling instructions copied beside a trace.</summary>
    public const string ChatInstructions =
        "Interpret this saved Motif diagnostic as recorded evidence. Read analyses before attempts, keep parser order, " +
        "treat incomplete search as incomplete even with a success, do not infer trace completion from a clean exit, " +
        "and label category counts aggregate rather than step timing. Format guide: " + FormatGuide;

    private readonly IClipboard _clipboard;
    private readonly IDiagnosticFilePicker _files;

    /// <summary>Puts the tools beside <paramref name="trace"/>.</summary>
    /// <param name="trace">The trace whose diagnostic the tools copy and save.</param>
    /// <param name="clipboard">Where the copy actions put their text.</param>
    /// <param name="files">The open and save dialogs of the window hosting these tools.</param>
    /// <param name="windowDialogs">Gives the window a diagnostic opens in dialogs of its own.</param>
    public DiagnosticToolsViewModel(
        TraceWordViewModel trace, IClipboard clipboard, IDiagnosticFilePicker files, IDiagnosticWindowDialogs windowDialogs)
    {
        ArgumentNullException.ThrowIfNull(trace);
        ArgumentNullException.ThrowIfNull(clipboard);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(windowDialogs);
        Trace = trace;
        _clipboard = clipboard;
        _files = files;
        WindowDialogs = windowDialogs;
    }

    /// <summary>The trace whose diagnostic the tools act on.</summary>
    public TraceWordViewModel Trace { get; }

    /// <summary>Why the last action failed, or <see langword="null"/> when it did not.</summary>
    [ObservableProperty]
    private WindowRefusal? _error;

    /// <summary>The file name the save dialog offers: the traced word's, or a generic one without a result.</summary>
    public string SuggestedFileName => $"{Trace.Result?.Word ?? "diagnostic"}.trace.json";

    /// <summary>
    /// Gives the window a diagnostic opens in dialogs of its own. The view that builds that window calls it; these
    /// tools only carry it.
    /// </summary>
    public IDiagnosticWindowDialogs WindowDialogs { get; }

    /// <summary>Raised with a saved diagnostic that <see cref="OpenAsync"/> read and validated.</summary>
    public event Action<OpenedDiagnostic>? DiagnosticOpened;

    /// <summary>
    /// The tools for a window showing <paramref name="opened"/>: its trace, with its refusal as the
    /// <see cref="Error"/>, this clipboard, and <paramref name="files"/>, the dialogs of that window.
    /// </summary>
    public DiagnosticToolsViewModel ForOpened(OpenedDiagnostic opened, IDiagnosticFilePicker files)
    {
        ArgumentNullException.ThrowIfNull(opened);
        return new DiagnosticToolsViewModel(opened.Trace, _clipboard, files, WindowDialogs) { Error = opened.Refusal };
    }

    /// <summary>Copies the trace's full diagnostic JSON.</summary>
    public Task CopyJsonAsync() => CopyAsync(Trace.DiagnosticJson, "Motif could not copy the diagnostic.");

    /// <summary>Copies instructions, a summary, and the original trace diagnostic for a chat.</summary>
    public Task CopyInstructionsAsync() => CopyAsync(BuildChatText(), "Motif could not copy the instructions.");

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
            _files, trace => DiagnosticOpened?.Invoke(new OpenedDiagnostic(trace, null)), refusal => Error = refusal);
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

    private string BuildChatText()
    {
        var summary = string.IsNullOrWhiteSpace(Trace.SummaryText) ? "Not recorded." : Trace.SummaryText;
        var completion = Trace.Result?.SearchCompletion switch
        {
            TraceSearchCompletion.InvalidShape => "not run (invalid shape recorded)",
            TraceSearchCompletion.Incomplete =>
                $"incomplete{(string.IsNullOrWhiteSpace(Trace.Result.StopReason) ? "; reason not recorded" : "; " + Trace.Result.StopReason)}",
            TraceSearchCompletion.Complete => "complete",
            _ => "not recorded",
        };
        var diagnostic = string.IsNullOrWhiteSpace(Trace.DiagnosticJson) ? "Not recorded." : Trace.DiagnosticJson;
        return $"{ChatInstructions}\n\nSummary:\n{summary}\nSearch completion: {completion}.\n\nTrace diagnostic JSON:\n{diagnostic}";
    }

    private static WindowRefusal NotWritten(string sentence, Exception exception) =>
        WindowRefusal.Failure(WindowRefusal.DiagnosticNotWrittenCode, sentence, exception);
}
