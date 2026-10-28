namespace SIL.Motif.App.Services;

/// <summary>
/// Where a view keeps its sort and filter for the project that is open: the preferences store plus the key of that
/// project. The workspace sets the key when a project opens and clears it when one closes, so a view never reads
/// or writes under a key that is not the open project's.
/// </summary>
public sealed class ProjectViewMemory
{
    private IUserPreferencesStore _store;

    public ProjectViewMemory(IUserPreferencesStore? store = null) =>
        _store = store ?? new MemoryUserPreferencesStore();

    /// <summary>The open project's key, or null when no project is open.</summary>
    public string? ProjectKey { get; private set; }

    /// <summary>Replaces the store the views save to; the workspace window supplies the real one.</summary>
    public void UseStore(IUserPreferencesStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    private string? _pathKey;

    /// <summary>
    /// Makes <paramref name="fullFwDataPath"/> the open project, or none when null. The key is the project's stable
    /// <paramref name="stableId"/> when it has one, else its path.
    /// </summary>
    public void OpenProject(string? fullFwDataPath, string? stableId = null)
    {
        var blank = string.IsNullOrWhiteSpace(fullFwDataPath);
        ProjectKey = blank ? null : ProjectPreferenceKey.For(stableId, fullFwDataPath!);
        _pathKey = blank ? null : KeyFor(fullFwDataPath!);
        IsPending = false;
    }

    /// <summary>Whether a project is open whose stable id is still being read, so it has no key yet.</summary>
    public bool IsPending { get; private set; }

    /// <summary>Raised once the pending project's key is known; views then restore or save under it.</summary>
    public event Action? KeyResolved;

    /// <summary>
    /// Opens <paramref name="fullFwDataPath"/> with no key yet: views keep their defaults and nothing is saved until
    /// <see cref="Resolve"/> supplies the stable id (or its absence).
    /// </summary>
    public void BeginOpen(string fullFwDataPath)
    {
        OpenProject(fullFwDataPath);
        ProjectKey = null;
        IsPending = true;
    }

    /// <summary>Gives the pending project its key; ignored when that project is no longer the open one.</summary>
    public void Resolve(string fullFwDataPath, string? stableId)
    {
        if (!IsPending || !IsOpenPath(fullFwDataPath)) return;
        ProjectKey = ProjectPreferenceKey.For(stableId, fullFwDataPath);
        IsPending = false;
        KeyResolved?.Invoke();
    }

    /// <summary>Whether <paramref name="fullFwDataPath"/> is the open project's path.</summary>
    public bool IsOpenPath(string? fullFwDataPath) =>
        !string.IsNullOrWhiteSpace(fullFwDataPath) && _pathKey is not null && KeyFor(fullFwDataPath) == _pathKey;

    /// <summary>The path-only key of a project, used when it has no stable id.</summary>
    public static string KeyFor(string fullFwDataPath) => ProjectPreferenceKey.For(null, fullFwDataPath);

    /// <summary>The saved state of <paramref name="view"/> for <paramref name="projectKey"/>, if it is the open project.</summary>
    public ViewState? Load(string? projectKey, string view) =>
        projectKey is not null && projectKey == ProjectKey ? _store.GetViewState(projectKey, view) : null;

    /// <summary>Saves <paramref name="state"/> when <paramref name="projectKey"/> is still the open project; null clears it.</summary>
    public void Save(string? projectKey, string view, ViewState? state)
    {
        if (projectKey is null || projectKey != ProjectKey) return;
        if (Equals(_store.GetViewState(projectKey, view), state)) return;
        _store.SetViewState(projectKey, view, state);
    }
}
