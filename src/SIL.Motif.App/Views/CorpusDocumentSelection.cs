namespace SIL.Motif.App.Views;

internal sealed class CorpusDocumentSelection
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _version;

    public async Task SwitchAsync<T>(Func<Task> clear, Func<T> open, Action<T> present)
    {
        var version = ++_version;
        await _gate.WaitAsync();
        try
        {
            if (version != _version) return;
            await clear();
            if (version == _version) present(open());
        }
        finally { _gate.Release(); }
    }
}
