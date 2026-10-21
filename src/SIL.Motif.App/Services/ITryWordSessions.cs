using SIL.Motif.Host.PanGloss;

namespace SIL.Motif.App.Services;

/// <summary>
/// The resident parser sessions behind Try a Word, as the window sees them. The product implementation is
/// <see cref="TryWordSessionsAdapter"/>; a test substitutes its own so the window's reactions can be driven without a parser.
/// </summary>
public interface ITryWordSessions
{
    /// <summary>Starts loading the project's grammar in the background; returns at once and never faults.</summary>
    void Prewarm(string projectPath);

    /// <summary>The phase of the project's session.</summary>
    WarmSessionPhase GetPhase(string projectPath);

    /// <summary>Raised on the UI thread when any project's session changes phase; read <see cref="GetPhase"/> for the new one.</summary>
    event EventHandler? PhaseChanged;

    /// <summary>Drops the project's sessions before the window closes, deletes, moves or renames its files.</summary>
    void Evict(string projectPath);

    /// <summary>Drops every session when the App exits.</summary>
    void Shutdown();
}

/// <summary>The sessions of a client that has none: every word runs cold.</summary>
public sealed class NoTryWordSessions : ITryWordSessions
{
    public static readonly NoTryWordSessions Instance = new();

    public void Prewarm(string projectPath) { }

    public WarmSessionPhase GetPhase(string projectPath) => WarmSessionPhase.Idle;

    public event EventHandler? PhaseChanged { add { } remove { } }

    public void Evict(string projectPath) { }

    public void Shutdown() { }
}
