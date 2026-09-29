using SIL.Motif.App.Services;
using SIL.Motif.Commands;
using SIL.Motif.Host.Parser;

namespace SIL.Motif.Tests.App;

/// <summary>The product's command client over a test's own root.</summary>
internal static class RealCommandClient
{
    /// <summary>
    /// Builds the client. A <see langword="null"/> <paramref name="parserPath"/> locates the real parser, and a
    /// <see langword="null"/> <paramref name="runner"/> starts nothing, so a test that queues work passes the
    /// runner it owns and disposes.
    /// </summary>
    public static CommandClient Create(
        string managedRoot, string? parserPath = null, IJobRunnerLauncher? runner = null,
        TimeProvider? timeProvider = null)
    {
        parserPath ??= PanGlossExecutable.TryLocate();
        return new CommandClient(new CommandClientOptions(managedRoot, parserPath,
            runner ?? new NoRunnerLauncher(new JobRunnerLaunchOptions(managedRoot, parserPath)),
            TimeProvider: timeProvider));
    }
}
