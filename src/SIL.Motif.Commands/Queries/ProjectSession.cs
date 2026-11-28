using SIL.LCModel;
using SIL.Motif.Contract.Query;
using SIL.Motif.Host.Baselines;

namespace SIL.Motif.Commands.Queries;

/// <summary>
/// Keeps the one project a run of query blocks reads open, so several <c>--project</c> blocks (one window session, one CLI call)
/// load it once instead of once per block. It holds one project at a time and reloads when the project file changes.
/// </summary>
public sealed class ProjectSession(TimeSpan? idleRelease = null) : IDisposable
{
    private readonly object _gate = new();
    private Timer? _idle;
    private BaselineReadCache? _read;
    private MorphCatalog? _catalog;
    private string? _path;
    private (DateTime Written, long Length) _stamp;

    /// <summary>How many times a project was loaded; a test reads it to see that blocks shared one load.</summary>
    public int Loads { get; private set; }

    /// <summary>The private project copy the session holds open, or null when it holds none.</summary>
    internal string? CopyDirectory
    {
        get { lock (_gate) return _read?.CopyDirectory; }
    }

    private int _epoch;

    /// <summary>The copy path read without the lock, for a test that holds a load open.</summary>
    internal string? CopyDirectoryUnlocked => _read?.CopyDirectory;

    /// <summary>Counts the releases asked for; a run that began under an older count belongs to a project that was left.</summary>
    public int Epoch => Volatile.Read(ref _epoch);

    /// <summary>Closes the held project and deletes its private copy now; the next block loads it again.</summary>
    public void Release()
    {
        Interlocked.Increment(ref _epoch);
        lock (_gate) Close();
    }

    /// <summary>
    /// As <see cref="Release"/> without waiting for a load in progress or for the copy to be deleted, so a window thread never
    /// stalls; the epoch moves at once. A failure is passed to <paramref name="failed"/>.
    /// </summary>
    public Task ReleaseInBackground(Action<Exception>? failed = null)
    {
        Interlocked.Increment(ref _epoch);
        return Task.Run(() =>
        {
            try { lock (_gate) Close(); }
            catch (Exception exception) { failed?.Invoke(exception); }
        });
    }

    internal MorphCatalog? Catalog(string path, out string? failure, Action<LcmCache>? alsoRead)
    {
        failure = null;
        lock (_gate)
        {
            try
            {
                var info = new FileInfo(path);
                var stamp = info.Exists ? (info.LastWriteTimeUtc, info.Length) : default;
                if (_read is null || _catalog is null || !string.Equals(_path, path, StringComparison.Ordinal) || _stamp != stamp)
                {
                    Close();
                    _read = BaselineReadCache.Open(path);
                    _catalog = MorphCatalog.FromProject(_read.Cache);
                    _path = path;
                    _stamp = stamp;
                    Loads++;
                }
                alsoRead?.Invoke(_read.Cache);
                ArmIdle();
                return _catalog;
            }
            catch (Exception exception) when (exception is not (OperationCanceledException or QueryRefusalException))
            {
                Close();
                failure = $"The project '{path}' could not be opened to name the morphs' entries: {exception.Message}";
                return null;
            }
        }
    }

    private void ArmIdle()
    {
        if (idleRelease is not { } after) return;
        _idle ??= new Timer(_ => { lock (_gate) Close(); }, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _idle.Change(after, Timeout.InfiniteTimeSpan);
    }

    private void Close()
    {
        _idle?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _read?.Dispose();
        _read = null;
        _catalog = null;
        _path = null;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            Close();
            _idle?.Dispose();
            _idle = null;
        }
    }
}
