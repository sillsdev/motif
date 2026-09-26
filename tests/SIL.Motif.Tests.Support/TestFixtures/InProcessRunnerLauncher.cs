using SIL.Motif.Commands;

namespace SIL.Motif.Tests.TestFixtures;

/// <summary>Drains an already queued Dry Run with the same worker loop used by the product.</summary>
public sealed class InProcessRunnerLauncher : IJobRunnerLauncher
{
    public void Start(string projectPath, JobRunnerLaunchOptions options, Action<string>? reportWarning = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        ArgumentNullException.ThrowIfNull(options);
        DryRunJobRunner.DrainQueued(projectPath);
    }
}
