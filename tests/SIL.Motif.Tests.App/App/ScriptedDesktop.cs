using SIL.Motif.App.Services;

namespace SIL.Motif.Tests.App;

/// <summary>Records every text a view model puts on the clipboard, or fails each copy when told to.</summary>
public sealed class RecordingClipboard : IClipboard
{
    /// <summary>The texts copied, oldest first.</summary>
    public List<string> Copied { get; } = [];

    /// <summary>When set, every copy throws this instead of recording.</summary>
    public Exception? Failure { get; set; }

    public Task SetTextAsync(string text, CancellationToken cancellationToken = default)
    {
        if (Failure is { } failure) return Task.FromException(failure);
        Copied.Add(text);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Answers the diagnostic open and save dialogs from a script: each open returns the next scripted text, and
/// cancels once the script is empty; each save records what it was given unless told to cancel or fail.
/// </summary>
public sealed class ScriptedDiagnosticFiles : IDiagnosticFilePicker
{
    private readonly Queue<Func<string?>> _opens = new();

    /// <summary>The saves that were written, oldest first.</summary>
    public List<(string SuggestedFileName, string Json)> Saved { get; } = [];

    /// <summary>How many times a save dialog was shown, whether or not it wrote.</summary>
    public int SavePrompts { get; private set; }

    /// <summary>How many times an open dialog was shown, whether or not a file was chosen.</summary>
    public int OpenPrompts { get; private set; }

    /// <summary>When true, the save dialog is cancelled.</summary>
    public bool CancelSaves { get; set; }

    /// <summary>When set, the save throws this after the dialog.</summary>
    public Exception? SaveFailure { get; set; }

    /// <summary>The next open chooses a file holding <paramref name="json"/>.</summary>
    public void NextOpenReads(string json) => _opens.Enqueue(() => json);

    /// <summary>The next open is cancelled.</summary>
    public void NextOpenCancels() => _opens.Enqueue(() => null);

    /// <summary>The next open chooses a file that cannot be read.</summary>
    public void NextOpenFails(Exception failure) => _opens.Enqueue(() => throw failure);

    public Task<string?> OpenDiagnosticAsync(CancellationToken cancellationToken = default)
    {
        OpenPrompts++;
        try
        {
            return Task.FromResult(_opens.Count > 0 ? _opens.Dequeue()() : null);
        }
        catch (Exception exception)
        {
            return Task.FromException<string?>(exception);
        }
    }

    public Task<bool> SaveDiagnosticAsync(string suggestedFileName, string json, CancellationToken cancellationToken = default)
    {
        SavePrompts++;
        if (CancelSaves) return Task.FromResult(false);
        if (SaveFailure is { } failure) return Task.FromException<bool>(failure);
        Saved.Add((suggestedFileName, json));
        return Task.FromResult(true);
    }
}
