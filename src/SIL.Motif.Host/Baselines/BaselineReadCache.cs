using SIL.LCModel;
using SIL.Motif.Host.LcmUtils;

namespace SIL.Motif.Host.Baselines;

/// <summary>Owns a private file-backed Baseline copy for one read, including its disposable cache and files.</summary>
public sealed class BaselineReadCache : IDisposable
{
    private readonly string _root;

    private BaselineReadCache(string root, LcmCache cache)
    {
        _root = root;
        Cache = cache;
    }

    /// <summary>The privately owned cache; callers project values and never save it.</summary>
    public LcmCache Cache { get; }

    /// <summary>Copies the captured project and writing systems before opening an independent reader.</summary>
    public static BaselineReadCache Open(string fwDataPath)
    {
        var root = Path.Combine(Path.GetTempPath(), "SIL.Motif.BaselineRead", Guid.NewGuid().ToString("N"));
        try
        {
            return new(root, new ScratchCacheFactory().CreateFromFileCopy(fwDataPath, root));
        }
        catch
        {
            Delete(root);
            throw;
        }
    }

    /// <summary>Releases LibLCM ownership before deleting the private project files.</summary>
    public void Dispose()
    {
        try { Cache.Dispose(); }
        finally { Delete(_root); }
    }

    private static void Delete(string root)
    {
        try { Directory.Delete(root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
