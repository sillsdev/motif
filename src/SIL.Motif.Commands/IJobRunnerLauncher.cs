namespace SIL.Motif.Commands;

/// <summary>Starts a job runner after a command has queued durable project work.</summary>
public interface IJobRunnerLauncher
{
    /// <summary>The root and parser this launcher's runner works with.</summary>
    JobRunnerLaunchOptions Options { get; }

    /// <summary>Starts work queued for <paramref name="projectPath"/>, best-effort.</summary>
    /// <param name="projectPath">
    /// The project whose work was just queued. A runner process sweeps every Known project and does not
    /// need it; a launcher that drains one project in the calling process does.
    /// </param>
    /// <param name="reportWarning">Receives a warning when the runner could not be started.</param>
    void Start(string projectPath, Action<string>? reportWarning = null);
}
