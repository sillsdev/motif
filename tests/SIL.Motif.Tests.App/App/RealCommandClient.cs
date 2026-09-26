using SIL.Motif.App.Services;
using SIL.Motif.Commands;
using SIL.Motif.Host.Parser;
using SIL.Motif.Tests.TestFixtures;

namespace SIL.Motif.Tests.App;

/// <summary>The product's command client over a test's own root, with queued work drained in process.</summary>
internal static class RealCommandClient
{
    /// <summary>Builds the client; a <see langword="null"/> <paramref name="parserPath"/> locates the real parser.</summary>
    public static CommandClient Create(string managedRoot, string? parserPath = null)
    {
        parserPath ??= PanGlossExecutable.TryLocate();
        return new CommandClient(new CommandClientOptions(managedRoot, parserPath,
            new InProcessRunnerLauncher(new JobRunnerLaunchOptions(managedRoot, parserPath))));
    }
}
