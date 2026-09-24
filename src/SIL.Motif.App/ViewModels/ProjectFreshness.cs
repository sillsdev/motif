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

/// <summary>A Known project the project menu can reopen: its full path, and the name a person knows it by.</summary>
public sealed record RecentProjectViewModel(string FullFwDataPath)
{
    /// <summary>The project's file name without its extension, as FieldWorks names projects.</summary>
    public string Name => Path.GetFileNameWithoutExtension(FullFwDataPath);

    /// <summary>The accessible name of the menu entry that reopens this project.</summary>
    public string AutomationName => $"Open {Name}";
}
