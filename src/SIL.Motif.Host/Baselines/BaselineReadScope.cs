using SIL.LCModel;

namespace SIL.Motif.Host.Baselines;

/// <summary>
/// Lets the reads of one command that name the same published Baseline file share a single private copy and load of it,
/// instead of each copying and loading the project again. The scope owns the copy and releases it when the command ends.
/// </summary>
public sealed class BaselineReadScope : IDisposable
{
    private static readonly AsyncLocal<BaselineReadScope?> CurrentScope = new();
    private readonly BaselineReadScope? _outer;
    private BaselineReadCache? _cache;
    private string? _path;
    private bool _disposed;

    private BaselineReadScope(BaselineReadScope? outer) => _outer = outer;

    /// <summary>Starts sharing for the calling context; a scope already open there is kept and this one defers to it.</summary>
    public static BaselineReadScope Begin()
    {
        var scope = new BaselineReadScope(CurrentScope.Value);
        CurrentScope.Value = scope._outer ?? scope;
        return scope;
    }

    /// <summary>
    /// Reads the Baseline file through the open scope when there is one, otherwise through a copy of its own that the
    /// lease releases. A scope holds one Baseline: a read of another file replaces its copy.
    /// </summary>
    public static BaselineReadLease Open(string fwDataPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fwDataPath);
        if (CurrentScope.Value is not { } scope) return new BaselineReadLease(BaselineReadCache.Open(fwDataPath), owned: true);
        return scope.Lease(fwDataPath);
    }

    private BaselineReadLease Lease(string fwDataPath)
    {
        var full = Path.GetFullPath(fwDataPath);
        if (_cache is null || !string.Equals(_path, full, OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
                ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            _cache?.Dispose();
            _cache = null;
            _cache = BaselineReadCache.Open(fwDataPath);
            _path = full;
        }
        return new BaselineReadLease(_cache, owned: false);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_outer is not null) return;
        CurrentScope.Value = null;
        _cache?.Dispose();
        _cache = null;
    }
}

/// <summary>One read of a Baseline: the cache to read, and a release that frees only what this read opened.</summary>
public sealed class BaselineReadLease : IDisposable
{
    private readonly BaselineReadCache _reader;
    private readonly bool _owned;

    internal BaselineReadLease(BaselineReadCache reader, bool owned)
    {
        _reader = reader;
        _owned = owned;
    }

    /// <summary>The cache to read; callers project values and never save it.</summary>
    public LcmCache Cache => _reader.Cache;

    /// <inheritdoc />
    public void Dispose()
    {
        if (_owned) _reader.Dispose();
    }
}
