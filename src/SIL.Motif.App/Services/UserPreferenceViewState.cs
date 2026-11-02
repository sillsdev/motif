using System.Collections.Immutable;

namespace SIL.Motif.App.Services;

/// <summary>Builds the key under which per-project view state is stored in user preferences.</summary>
public static class ProjectPreferenceKey
{
    /// <summary>
    /// The stable LangProject GUID when one is known (an all-zero GUID counts as unknown), else the full normalised
    /// path (<see cref="Path.GetFullPath(string)"/>, trailing separators dropped). Windows and macOS file systems
    /// ignore case by default, so there the path is also case folded with separators unified; on Linux case is
    /// part of the path, so two projects differing only by case keep separate keys. The GUID survives a moved or
    /// renamed project and is shared by its copies; the path is the only identity left when no GUID is available.
    /// </summary>
    public static string For(string? stableId, string fullFwDataPath)
    {
        if (!string.IsNullOrWhiteSpace(stableId) &&
            !(Guid.TryParse(stableId, out var empty) && empty == Guid.Empty))
            return Guid.TryParse(stableId, out var guid)
                ? "id:" + guid.ToString("D")
                : "id:" + stableId.Trim().ToLowerInvariant();
        ArgumentException.ThrowIfNullOrWhiteSpace(fullFwDataPath);
        var path = FullPathOrAsGiven(fullFwDataPath.Trim());
        // A backslash is an ordinary file name character on Unix, so only Windows unifies separators.
        if (OperatingSystem.IsWindows()) path = path.Replace('\\', '/');
        if (!OperatingSystem.IsLinux()) path = path.ToLowerInvariant();
        var root = path.Length > 1 ? path.TrimEnd('/') : path;
        return "path:" + (root.Length == 0 ? "/" : root);
    }

    // An unusable stored path is keyed as given, so opening a project never throws.
    private static string FullPathOrAsGiven(string path)
    {
        try { return Path.GetFullPath(path); }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException or System.Security.SecurityException)
        {
            return path;
        }
    }

    /// <summary>
    /// The key an earlier version stored a path-keyed project under (always lower-cased), or null when
    /// <paramref name="key"/> is not a path key or is already that form.
    /// </summary>
    public static string? EarlierFormOf(string key)
    {
        if (!key.StartsWith("path:", StringComparison.Ordinal)) return null;
        var earlier = key.ToLowerInvariant();
        return earlier == key ? null : earlier;
    }
}

/// <summary>Minimal reads and writes of the format 2 expander and per-project view maps.</summary>
public static class UserPreferenceViewState
{
    /// <summary>The most projects whose view state is kept; adding a new one beyond this evicts another.</summary>
    public const int MaxProjects = 200;

    /// <summary>The longest filter text kept; longer text is cut to this.</summary>
    public const int MaxFilterLength = 500;

    /// <summary>The saved sort and filter of one view in one project, or null when none is saved.</summary>
    public static ViewState? GetViewState(this IUserPreferencesStore store, string projectKey, string viewName)
    {
        ArgumentNullException.ThrowIfNull(store);
        return store.Current.ProjectViews.TryGetValue(projectKey, out var views) &&
            views.TryGetValue(viewName, out var state) ? state : null;
    }

    /// <summary>Saves the sort and filter of one view in one project; null clears it.</summary>
    public static PreferenceWriteResult SetViewState(this IUserPreferencesStore store, string projectKey,
        string viewName, ViewState? state)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(viewName);
        if (state?.Filter is { Length: > MaxFilterLength } longFilter)
            state = state with { Filter = longFilter[..MaxFilterLength] };
        return store.Update(preferences =>
        {
            var views = preferences.ProjectViews.TryGetValue(projectKey, out var existing)
                ? existing.ToImmutableDictionary()
                : ImmutableDictionary<string, ViewState>.Empty;
            views = state is null ? views.Remove(viewName) : views.SetItem(viewName, state);
            var projects = preferences.ProjectViews.ToImmutableDictionary();
            if (views.IsEmpty)
                projects = projects.Remove(projectKey);
            else
            {
                if (!projects.ContainsKey(projectKey))
                    while (projects.Count >= MaxProjects)
                        projects = projects.Remove(projects.Keys.First(key => key != projectKey));
                projects = projects.SetItem(projectKey, views);
            }
            return preferences with { ProjectViews = projects };
        });
    }

    /// <summary>Whether the named "More options" section was left expanded, or null when never set.</summary>
    public static bool? GetExpanded(this IUserPreferencesStore store, string expanderKey)
    {
        ArgumentNullException.ThrowIfNull(store);
        return store.Current.ExpandedSections.TryGetValue(expanderKey, out var expanded) ? expanded : null;
    }

    /// <summary>Remembers whether the named "More options" section is expanded.</summary>
    public static PreferenceWriteResult SetExpanded(this IUserPreferencesStore store, string expanderKey,
        bool expanded)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentException.ThrowIfNullOrWhiteSpace(expanderKey);
        return store.Update(preferences => preferences with
        {
            ExpandedSections = preferences.ExpandedSections.ToImmutableDictionary().SetItem(expanderKey, expanded),
        });
    }
}
