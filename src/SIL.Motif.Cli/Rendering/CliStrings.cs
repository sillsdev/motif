using System.Resources;

namespace SIL.Motif.Cli.Rendering;

internal static class CliStrings
{
    private static readonly ResourceManager Resources = new("SIL.Motif.Cli.Rendering.CliStrings", typeof(CliStrings).Assembly);

    internal static string ProjectBackupFormat => Resources.GetString(nameof(ProjectBackupFormat))!;
    internal static string ProjectRestoreFormat => Resources.GetString(nameof(ProjectRestoreFormat))!;
    internal static string StoreRelocateFormat => Resources.GetString(nameof(StoreRelocateFormat))!;
    internal static string StoreRelocateOldStillExists => Resources.GetString(nameof(StoreRelocateOldStillExists))!;
    internal static string StoreRelocateUnchangedFormat => Resources.GetString(nameof(StoreRelocateUnchangedFormat))!;
    internal static string WorkerStopStopped => Resources.GetString(nameof(WorkerStopStopped))!;
    internal static string WorkerStopSkipped => Resources.GetString(nameof(WorkerStopSkipped))!;
    internal static string WorkerStopNoneForRoot => Resources.GetString(nameof(WorkerStopNoneForRoot))!;
    internal static string WorkerStopNoneForProject => Resources.GetString(nameof(WorkerStopNoneForProject))!;
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
