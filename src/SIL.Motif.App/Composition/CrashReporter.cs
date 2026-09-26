using Avalonia.Controls;
using Avalonia.Threading;
using SIL.Motif.App.Services;
using SIL.Motif.App.Views;

namespace SIL.Motif.App.Composition;

/// <summary>
/// Opens Motif's error window for the first error that escapes the UI thread, and closes Motif when that
/// window closes.
/// </summary>
/// <remarks>
/// <para>
/// The workspace is not resumed after such an error: its window is disabled while the error window is open,
/// because whatever state the error left behind is unknown. The dispatcher's exception is marked handled only
/// so the person can read, copy or save the report first.
/// </para>
/// <para>
/// Any error the reporter cannot present is left unhandled, so Motif exits rather than carrying on without a
/// report. That includes a second error while the window is open, since it may be the error window's own.
/// </para>
/// </remarks>
public sealed class CrashReporter
{
    private readonly TimeProvider _clock;
    private readonly CrashWindowServices _services;
    private EventHandler? _shutdownOnClose;

    /// <summary>Reports through a window acting with <paramref name="services"/>, timed by <paramref name="clock"/>.</summary>
    public CrashReporter(TimeProvider clock, CrashWindowServices services)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(services);
        _clock = clock;
        _services = services;
    }

    /// <summary>The error window, once an error has opened it; otherwise <see langword="null"/>.</summary>
    public CrashWindow? Window { get; private set; }

    /// <summary>
    /// Reports errors that escape <paramref name="dispatcher"/>, disabling <paramref name="workspace"/> while
    /// the error window is open and calling <paramref name="shutdown"/> when it closes.
    /// </summary>
    /// <returns>
    /// Stops reporting and closes an open error window without calling <paramref name="shutdown"/>. A session that
    /// closes calls it, so a later session in the same process attaches its own.
    /// </returns>
    public Action Attach(Dispatcher dispatcher, Window workspace, Action shutdown)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(shutdown);

        void OnUnhandledException(object? sender, DispatcherUnhandledExceptionEventArgs e)
        {
            if (Window is not null) return;
            try
            {
                Show(e.Exception, workspace, shutdown);
                e.Handled = true;
            }
            catch (Exception)
            {
                // The window could not be shown, so the original error stays unhandled and Motif exits.
            }
        }

        dispatcher.UnhandledException += OnUnhandledException;
        return () =>
        {
            dispatcher.UnhandledException -= OnUnhandledException;
            if (Window is not { IsVisible: true } open) return;
            open.Closed -= _shutdownOnClose;
            open.Close();
        };
    }

    private void Show(Exception exception, Window workspace, Action shutdown)
    {
        var window = new CrashWindow(CrashReport.For(exception, _clock), _services);
        _shutdownOnClose = (_, _) => shutdown();
        window.Closed += _shutdownOnClose;
        window.Show();
        Window = window;
        workspace.IsEnabled = false;
        window.Activate();
    }
}
