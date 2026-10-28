using System.Collections.Immutable;

namespace SIL.Motif.App.Services;

/// <summary>Builds the key under which per-project view state is stored in user preferences.</summary>
public static class ProjectPreferenceKey
{
    /// <summary>
    /// The stable LangProject GUID when one is known, else the normalised full path: the GUID survives a moved or
    /// renamed project and is shared by its copies; the path is the only identity left when no GUID is available.
    /// </summary>
    public static string For(string? stableId, string fullFwDataPath)
    {
        if (!string.IsNullOrWhiteSpace(stableId))
            return Guid.TryParse(stableId, out var guid)
                ? "id:" + guid.ToString("D")
                : "id:" + stableId.Trim().ToLowerInvariant();
        ArgumentException.ThrowIfNullOrWhiteSpace(fullFwDataPath);
        // Matches App project comparison: ignore case, unify separators.
        var path = fullFwDataPath.Trim().Replace('\\', '/').ToLowerInvariant();
        var root = path.Length > 1 ? path.TrimEnd('/') : path;
        return "path:" + (root.Length == 0 ? "/" : root);
    }
}

/// <summary>Minimal reads and writes of the format 2 expander and per-project view maps.</summary>
public static class UserPreferenceViewState
{
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
        return store.Update(preferences =>
        {
            var views = preferences.ProjectViews.TryGetValue(projectKey, out var existing)
                ? existing.ToImmutableDictionary()
                : ImmutableDictionary<string, ViewState>.Empty;
            views = state is null ? views.Remove(viewName) : views.SetItem(viewName, state);
            var projects = preferences.ProjectViews.ToImmutableDictionary();
            projects = views.IsEmpty
                ? projects.Remove(projectKey)
                : projects.SetItem(projectKey, views);
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
