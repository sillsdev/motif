using SIL.Motif.Commands;
using SIL.Motif.Host.Parser;

namespace SIL.Motif.App.Services;

/// <summary>Supplies the App's explicit parser, worker root, and worker launcher.</summary>
/// <param name="ManagedRoot">The worker root every command the window runs uses.</param>
/// <param name="ParserPath">The parser the window's commands run, or <see langword="null"/> for none.</param>
/// <param name="RunnerLauncher">
/// Starts the runner for queued work. Its <see cref="IJobRunnerLauncher.Options"/> must name the same root
/// and parser, so the window and its runner never work in two places.
/// </param>
public sealed record CommandClientOptions(
    string ManagedRoot,
    string? ParserPath,
    IJobRunnerLauncher RunnerLauncher)
{
    /// <summary>The local per-user root used when no client supplies an alternate location.</summary>
    public static string DefaultManagedRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SIL", "Motif");

    /// <summary>Uses installation discovery without reading process-wide overrides.</summary>
    public static CommandClientOptions ForInstallation()
    {
        var parser = PanGlossExecutable.TryLocateFromInstallation();
        return new(DefaultManagedRoot, parser,
            new ProcessRunnerLauncher(new JobRunnerLaunchOptions(DefaultManagedRoot, parser)));
    }
}
