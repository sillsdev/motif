using System.Collections.Concurrent;
using SIL.Motif.Host.PanGloss;

namespace SIL.Motif.Worker.Parsimony;

/// <summary>
/// Remembers the streamed digest of large files that a job reads again and again, such as the parser executable
/// and a frozen project file. An entry is trusted only while the file's full path, length and last-write time all
/// match, so a replaced file is hashed afresh; <see cref="Fresh"/> never consults the memory.
/// </summary>
internal sealed class FileDigestCache
{
    private readonly Func<string, string> _hash;
    private readonly ConcurrentDictionary<(string Path, long Length, long WriteTicks), string> _known = new();

    /// <summary>Creates a cache that digests with <see cref="BatchInvocationEvidence.DigestFile"/> unless told otherwise.</summary>
    /// <param name="hash">The streamed hasher, returning a <c>sha256:</c> digest; tests substitute a counting one.</param>
    public FileDigestCache(Func<string, string>? hash = null) => _hash = hash ?? BatchInvocationEvidence.DigestFile;

    /// <summary>The process-wide cache the product uses.</summary>
    public static FileDigestCache Shared { get; } = new();

    /// <summary>The digest of the file, reusing an earlier digest of the same unchanged file.</summary>
    /// <param name="path">The file to digest.</param>
    public string Of(string path)
    {
        var info = new FileInfo(Path.GetFullPath(path));
        if (!info.Exists) throw new FileNotFoundException("The file to digest does not exist.", info.FullName);
        var key = (info.FullName, info.Length, info.LastWriteTimeUtc.Ticks);
        if (_known.TryGetValue(key, out var known)) return known;
        var digest = _hash(info.FullName);
        _known[key] = digest;
        return digest;
    }

    /// <summary>The digest of the file's bytes as they are now, read through and never remembered.</summary>
    /// <param name="path">The file to digest.</param>
    public string Fresh(string path) => _hash(path);
}
