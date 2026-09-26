namespace SIL.Motif.Commands;

/// <summary>Starts the worker after a command has queued durable project work.</summary>
public interface IJobRunnerLauncher
{
    /// <summary>Starts work for <paramref name="projectPath"/> using the supplied worker configuration.</summary>
    void Start(string projectPath, JobRunnerLaunchOptions options, Action<string>? reportWarning = null);
}
