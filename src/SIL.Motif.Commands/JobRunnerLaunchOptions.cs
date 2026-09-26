using SIL.Motif.Host.Parser;
using SIL.Motif.Worker;

namespace SIL.Motif.Commands;

/// <summary>The worker root and parser selected by the command client that queued a job.</summary>
public sealed record JobRunnerLaunchOptions(string Root, string? ParserPath)
{
    /// <summary>Uses the command-line environment defaults when no explicit client options were supplied.</summary>
    public static JobRunnerLaunchOptions ForCommandDefaults() => new(
        RunnerOptions.ResolveRoot(), PanGlossExecutable.TryLocate());
}
