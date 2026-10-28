namespace SIL.Motif.App.Services;

internal sealed class ProjectFilterMemory
{
    private readonly ProjectViewMemory _memory;
    private readonly string _view;
    private readonly Func<string?> _current;
    private readonly Action<string?> _restore;
    private string? _path;
    private string? _key;
    private bool _restoring;
    private bool _changed;

    public ProjectFilterMemory(ProjectViewMemory memory, string view, Func<string?> current, Action<string?> restore)
    {
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
        try { _restore(_memory.Load(_key, _view)?.Filter); }
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
        var filter = _current();
        _memory.Save(_key, _view, string.IsNullOrEmpty(filter) ? null : new ViewState(null, false, filter));
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
