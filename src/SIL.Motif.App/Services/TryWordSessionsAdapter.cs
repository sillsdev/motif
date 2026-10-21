using Avalonia.Threading;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Host.PanGloss;

namespace SIL.Motif.App.Services;

/// <summary>
/// <see cref="TryWordSessions"/> for the window: it names the parser the Workbench query uses and raises phase changes on the
/// UI thread. It holds the static event only while something listens, so an abandoned listener cannot keep a view model alive.
/// </summary>
public sealed class TryWordSessionsAdapter(string? parserPath) : ITryWordSessions
{
    private readonly object _gate = new();
    private EventHandler? _changed;

    public void Prewarm(string projectPath) => _ = TryWordSessions.Prewarm(projectPath, parserPath);

    public WarmSessionPhase GetPhase(string projectPath) => TryWordSessions.GetState(projectPath, parserPath).Phase;

    public event EventHandler? PhaseChanged
    {
        add
        {
            lock (_gate)
            {
                if (_changed is null) TryWordSessions.StateChanged += OnStateChanged;
                _changed += value;
            }
        }
        remove
        {
            lock (_gate)
            {
                _changed -= value;
                if (_changed is null) TryWordSessions.StateChanged -= OnStateChanged;
            }
        }
    }

    public void Evict(string projectPath) => TryWordSessions.Evict(projectPath);

    public void Shutdown() => TryWordSessions.Shutdown();

    // Raised on a thread-pool thread by the sessions.
    private void OnStateChanged(object? sender, WarmSessionStateChangedEventArgs e) =>
        Dispatcher.UIThread.Post(() =>
        {
            EventHandler? handlers;
            lock (_gate) handlers = _changed;
            handlers?.Invoke(this, EventArgs.Empty);
        });
}
