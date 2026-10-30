using System.Resources;

namespace SIL.Motif.Cli.Rendering;

internal static class CliStrings
{
    private static readonly ResourceManager Resources = new("SIL.Motif.Cli.Rendering.CliStrings", typeof(CliStrings).Assembly);

    internal static string StorePruneFormat => Resources.GetString(nameof(StorePruneFormat))!;
    internal static string StorePruneSizeIncomplete => Resources.GetString(nameof(StorePruneSizeIncomplete))!;
    internal static string StorePruneIncomplete => Resources.GetString(nameof(StorePruneIncomplete))!;
}
