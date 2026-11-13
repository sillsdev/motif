using System.Resources;

namespace SIL.Motif.Cli.Rendering;

internal static class CliStrings
{
    private static readonly ResourceManager Resources = new("SIL.Motif.Cli.Rendering.CliStrings", typeof(CliStrings).Assembly);

    internal static string StorePruneFormat => Resources.GetString(nameof(StorePruneFormat))!;
    internal static string StorePruneSizeIncomplete => Resources.GetString(nameof(StorePruneSizeIncomplete))!;
    internal static string StorePruneIncomplete => Resources.GetString(nameof(StorePruneIncomplete))!;
    internal static string StorePruneBudgetFormat => Resources.GetString(nameof(StorePruneBudgetFormat))!;
    internal static string StorePruneOverBudget => Resources.GetString(nameof(StorePruneOverBudget))!;
    internal static string StorePruneKeptAssessments => Resources.GetString(nameof(StorePruneKeptAssessments))!;
    internal static string StorePruneKeptRuns => Resources.GetString(nameof(StorePruneKeptRuns))!;
    internal static string StorePruneKeptUserData => Resources.GetString(nameof(StorePruneKeptUserData))!;
    internal static string StorePruneUserDataFormat => Resources.GetString(nameof(StorePruneUserDataFormat))!;
}
