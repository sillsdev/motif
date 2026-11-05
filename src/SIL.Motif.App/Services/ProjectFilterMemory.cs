namespace SIL.Motif.App.Services;

/// <summary>
/// Remembers one view's filter, or its sort when <c>isSort</c> is set, and optionally its selected row's stable key,
/// per project in <see cref="ProjectViewMemory"/>.
/// </summary>
internal sealed class ProjectFilterMemory
{
    private readonly bool _isSort;
    private readonly ProjectViewMemory _memory;
    private readonly string _view;
    private readonly Func<string?> _current;
    private readonly Action<string?> _restore;
    private string? _path;
    private string? _key;
    private bool _restoring;
    private bool _changed;
    private readonly Action<string?>? _restoreSelection;

    public ProjectFilterMemory(ProjectViewMemory memory, string view, Func<string?> current, Action<string?> restore,
        bool isSort = false, Action<string?>? restoreSelection = null)
    {
        _restoreSelection = restoreSelection;
        _isSort = isSort;
        _memory = memory;
        _view = view;
        _current = current;
        _restore = restore;
        memory.KeyResolved += OnKeyResolved;
    }

    public void Open(string? path)
    {
        _path = path;
        _key = null;
        _changed = false;
        _selection = null;
        Restore();
    }

    private string? _selection;

    /// <summary>The selected row's key saved for the open project, or null when none is saved.</summary>
    public string? SavedSelection => _selection;

    /// <summary>Remembers <paramref name="key"/> as the selected row; a null key is not remembered, so a view that is emptied keeps it.</summary>
    public void SelectionChanged(string? key)
    {
        if (_restoring || key is null || key == _selection) return;
        _selection = key;
        Changed();
    }

    private void Restore()
    {
        _key = _memory.IsOpenPath(_path) ? _memory.ProjectKey : null;
        _restoring = true;
        try
        {
            var saved = _memory.Load(_key, _view);
            _selection = saved?.SelectedKey;
            _restore(_isSort ? saved?.SortColumn : saved?.Filter);
            _restoreSelection?.Invoke(_selection);
        }
        finally { _restoring = false; }
    }

    public void Changed()
    {
        if (_restoring) return;
        if (_key is null)
        {
            _changed = _memory.IsPending && _memory.IsOpenPath(_path);
            return;
        }
        var choice = string.IsNullOrEmpty(_current()) ? null : _current();
        _memory.Save(_key, _view, choice is null && _selection is null ? null
            : _isSort ? new ViewState(choice, false, null, _selection) : new ViewState(null, false, choice, _selection));
    }

    private void OnKeyResolved()
    {
        if (!_memory.IsOpenPath(_path)) return;
        if (!_changed) { Restore(); return; }
        _changed = false;
        _key = _memory.ProjectKey;
        Changed();
    }
}
