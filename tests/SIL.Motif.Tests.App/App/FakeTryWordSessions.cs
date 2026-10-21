using SIL.Motif.App.Services;
using SIL.Motif.Host.PanGloss;

namespace SIL.Motif.Tests.App;

/// <summary>Records what the window asks of the resident parser sessions and lets a test move a project's phase.</summary>
public sealed class FakeTryWordSessions : ITryWordSessions
{
    private readonly Dictionary<string, WarmSessionPhase> _phases = new(StringComparer.OrdinalIgnoreCase);
    private EventHandler? _changed;

    /// <summary>Every call in order, for example <c>prewarm C:\p.fwdata</c>.</summary>
    public List<string> Calls { get; } = [];

    /// <summary>How many listeners are subscribed to <see cref="PhaseChanged"/>.</summary>
    public int ListenerCount => _changed?.GetInvocationList().Length ?? 0;

    public void Prewarm(string projectPath) => Calls.Add("prewarm " + projectPath);

    public WarmSessionPhase GetPhase(string projectPath) =>
        _phases.TryGetValue(projectPath, out var phase) ? phase : WarmSessionPhase.Idle;

    public event EventHandler? PhaseChanged
    {
        add => _changed += value;
        remove => _changed -= value;
    }

    public void Evict(string projectPath) => Calls.Add("evict " + projectPath);

    public void Shutdown() => Calls.Add("shutdown");

    /// <summary>Moves the project to <paramref name="phase"/> and raises the change, as the UI-thread adapter would.</summary>
    public void Move(string projectPath, WarmSessionPhase phase)
    {
        _phases[projectPath] = phase;
        _changed?.Invoke(this, EventArgs.Empty);
    }
}
