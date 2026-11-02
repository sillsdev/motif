namespace SIL.Motif.App.Services;

/// <summary>Remembers one view's filter, or its sort when <c>isSort</c> is set, per project in <see cref="ProjectViewMemory"/>.</summary>
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

    public ProjectFilterMemory(ProjectViewMemory memory, string view, Func<string?> current, Action<string?> restore,
        bool isSort = false)
    {
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
        Restore();
    }

    private void Restore()
    {
        _key = _memory.IsOpenPath(_path) ? _memory.ProjectKey : null;
        _restoring = true;
        try
        {
            var saved = _memory.Load(_key, _view);
            _restore(_isSort ? saved?.SortColumn : saved?.Filter);
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
        var choice = _current();
        _memory.Save(_key, _view, string.IsNullOrEmpty(choice) ? null
            : _isSort ? new ViewState(choice, false, null) : new ViewState(null, false, choice));
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
