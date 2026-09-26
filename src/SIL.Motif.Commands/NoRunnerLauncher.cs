namespace SIL.Motif.Commands;

/// <summary>Starts nothing: queued work waits for a runner started some other way.</summary>
public sealed class NoRunnerLauncher(JobRunnerLaunchOptions options) : IJobRunnerLauncher
{
    public JobRunnerLaunchOptions Options { get; } = options ?? throw new ArgumentNullException(nameof(options));

    public void Start(string projectPath, Action<string>? reportWarning = null)
    {
    }
}
