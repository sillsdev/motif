namespace SIL.Motif.App.Services;

/// <summary>The long-running commands an <see cref="ICommandStartGate"/> may hold.</summary>
public enum GatedCommand
{
    Assess,
    Handoff,
}

/// <summary>
/// Holds a long-running command before the command client starts it, so a test can observe the window
/// while the run is pending. It can only delay the command: the real client still runs it once the returned
/// task completes, and still reports the command's own outcome, including its cancellation.
/// </summary>
public interface ICommandStartGate
{
    /// <summary>Completes when <paramref name="command"/> may start. It must not fault.</summary>
    Task WaitToStartAsync(GatedCommand command);
}
