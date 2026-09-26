using SIL.Motif.Commands;
using SIL.Motif.Host.Parser;

namespace SIL.Motif.App.Services;

/// <summary>Supplies the App's explicit parser, worker root, and worker launcher.</summary>
public sealed record CommandClientOptions(
    string ManagedRoot,
    string? ParserPath,
    IJobRunnerLauncher RunnerLauncher)
{
    /// <summary>Builds worker settings from these same root and parser values.</summary>
    public JobRunnerLaunchOptions LaunchOptions => new(ManagedRoot, ParserPath);

    /// <summary>The local per-user root used when no client supplies an alternate location.</summary>
    public static string DefaultManagedRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SIL", "Motif");

    /// <summary>Uses installation discovery without reading process-wide overrides.</summary>
    public static CommandClientOptions ForInstallation() => new(
        DefaultManagedRoot,
        PanGlossExecutable.TryLocateFromInstallation(),
        new ProcessRunnerLauncher());
}
