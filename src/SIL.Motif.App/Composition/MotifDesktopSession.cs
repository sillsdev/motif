using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;

namespace SIL.Motif.App.Composition;

/// <summary>
/// One desktop session's window and workspace, with the background work its startup and close begin, so a
/// caller that must wait for them — a test deleting its root, or restarting in one process — can.
/// </summary>
public sealed class MotifDesktopSession
{
    private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Action? _detach;
    private bool _closing;

    internal MotifDesktopSession(MotifAppCompositionResult composition, Task knownProjectsLoaded)
    {
        Window = composition.Window;
        Workspace = composition.Workspace;
        Crashes = composition.Crashes;
        KnownProjectsLoaded = knownProjectsLoaded;
    }

    /// <summary>The window installed as the lifetime's main window.</summary>
    public MainWindow Window { get; }

    /// <summary>The workspace the window shows.</summary>
    public WorkspaceShellViewModel Workspace { get; }

    /// <summary>Opens the error window for an error that escapes the UI thread, until <see cref="CloseAsync"/>.</summary>
    public CrashReporter Crashes { get; }

    /// <summary>Completes when startup's Known-project load has finished; it never faults.</summary>
    public Task KnownProjectsLoaded { get; }

    /// <summary>Completes once the session has closed and its workspace is disposed; it never faults.</summary>
    public Task Closed => _closed.Task;

    /// <summary>
    /// Closes the window and disposes the workspace. The lifetime's exit calls it; so does an in-process
    /// restart before it composes a new session over the same root. Later calls return the first close.
    /// </summary>
    public Task CloseAsync()
    {
        if (_closing) return Closed;
        _closing = true;
        _detach?.Invoke();
        if (Window.IsVisible) Window.Close();
        // Nothing waits on disposal at exit, so a failure is observed here rather than left unobserved.
        Workspace.DisposeAsync().AsTask().ContinueWith(disposal =>
        {
            _ = disposal.Exception;
            _closed.TrySetResult();
        }, TaskScheduler.Default);
        return Closed;
    }

    internal void DetachWith(Action detach) => _detach = detach;
}
