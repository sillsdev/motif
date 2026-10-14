using SIL.Motif.Commands.Queries;

namespace SIL.Motif.App.ViewModels;

/// <summary>
/// Whether the numbers on screen describe the project as FieldWorks last saved it. Motif never reruns on its
/// own: a stale project stays stale until the person presses Refresh.
/// </summary>
public enum ProjectFreshness
{
    /// <summary>No project is open, so there is nothing to be fresh about.</summary>
    NoProject,

    /// <summary>The project is open but no Baseline has been captured for it yet.</summary>
    NoBaseline,

    /// <summary>The Baseline copies the save FieldWorks last made.</summary>
    Current,

    /// <summary>FieldWorks has written the project since the save the Baseline copies.</summary>
    SavedSince,

    /// <summary>A Refresh is capturing a new Baseline or assessing the Selection against it.</summary>
    Refreshing,

    /// <summary>A Refresh finished: a new Baseline, and the Selection assessed against it.</summary>
    Refreshed,
}

/// <summary>A Known project the window can reopen, with any identity and summary already stored by Motif.</summary>
public sealed record RecentProjectViewModel(string FullFwDataPath)
{
    /// <summary>The project's file name without its extension, as FieldWorks names projects.</summary>
    public string Name => Path.GetFileNameWithoutExtension(PlatformPath);

    /// <summary>The accessible name of the menu entry that reopens this project.</summary>
    public string AutomationName => $"Open {Name}";

    /// <summary>The LangProject GUID, used to group copies of one project in Recent.</summary>
    public string? ProjectIdentity { get; init; }

    /// <summary>The state Motif has already recorded, in the window's words.</summary>
    public string? StateText { get; init; }

    /// <summary>Whether Motif has stored a state to show on the row.</summary>
    public bool HasStateText => !string.IsNullOrWhiteSpace(StateText);

    /// <summary>The number of wordforms in the stored Baseline, when available.</summary>
    public int? WordformCount { get; init; }

    /// <summary>Whether Motif has a stored wordform count to show on the row.</summary>
    public bool HasWordformCount => WordformCount.HasValue;

    /// <summary>Whether the row has any stored state or count details.</summary>
    public bool HasRecentDetails => HasStateText || HasWordformCount;

    /// <summary>The row's count text, or an empty string when Motif has not stored one.</summary>
    public string WordformCountText => WordformCount is { } count ? $"{count:N0} wordforms" : string.Empty;

    /// <summary>The parent folder containing the project's <c>.fwdata</c> file.</summary>
    public string? FolderPath => Path.GetDirectoryName(PlatformPath);

    /// <summary>Whether the project's containing folder still exists.</summary>
    public bool CanOpenFolder => FolderPath is { } folder && Directory.Exists(folder);

    private string PlatformPath => FullFwDataPath.Replace('\\', Path.DirectorySeparatorChar);

    /// <summary>Copies the fields the Known-project query read from the machine and project stores.</summary>
    public static RecentProjectViewModel FromKnownProject(KnownProjectSummary known) =>
        new(known.FullFwDataPath)
        {
            ProjectIdentity = known.ProjectIdentity,
            StateText = known.StateText,
            WordformCount = known.WordformCount,
        };

    /// <summary>Returns one row per LangProject identity, keeping that project's most recently opened copy.</summary>
    public static IReadOnlyList<RecentProjectViewModel> MostRecentlySeenCopies(
        IEnumerable<KnownProjectSummary> knownProjects)
    {
        ArgumentNullException.ThrowIfNull(knownProjects);
        var pathComparer = OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;
        return knownProjects
            .GroupBy(project => project.ProjectIdentity is { Length: > 0 } identity
                ? "identity:" + identity.ToUpperInvariant()
                : "path:" + Path.GetFullPath(project.FullFwDataPath), pathComparer)
            .Select(group => group.MaxBy(project => project.LastSeenUtc)!)
            .OrderByDescending(project => project.LastSeenUtc)
            .Select(FromKnownProject)
            .ToArray();
    }
}
