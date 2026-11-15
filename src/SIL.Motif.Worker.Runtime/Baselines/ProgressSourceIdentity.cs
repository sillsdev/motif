using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SIL.Motif.Worker.Baselines;

internal sealed record ProgressFileStamp(string Name, long Length, long LastWriteTicks);
internal sealed record ProgressSourceIdentity(string Digest, ProgressFileStamp[] Files)
{
    internal static readonly AsyncLocal<Action?> BeforeHash = new();

    public static ProgressFileStamp[] Metadata(string path)
    {
        var root = Path.GetDirectoryName(path)!;
        var store = Path.Combine(root, "WritingSystemStore");
        var files = new List<(string Name, string Path)> { ("project", path) };
        if (Directory.Exists(store))
            files.AddRange(Directory.EnumerateFiles(store, "*.ldml", SearchOption.TopDirectoryOnly)
                .OrderBy(file => Path.GetFileName(file), StringComparer.Ordinal)
                .Select(file => ("WritingSystemStore/" + Path.GetFileName(file), file)));
        return files.Select(file =>
        {
            var info = new FileInfo(file.Path);
            return new ProgressFileStamp(file.Name, info.Length, info.LastWriteTimeUtc.Ticks);
        }).ToArray();
    }

    public static ProgressSourceIdentity? Capture(string path)
    {
        var before = Metadata(path);
        var digest = ComputeDigest(path, before);
        var after = Metadata(path);
        return before.SequenceEqual(after) ? new ProgressSourceIdentity(digest, after) : null;
    }

    public static ProgressSourceIdentity? CapturedCopy(string copyPath, string livePath, ProgressFileStamp[] before)
    {
        var copy = Metadata(copyPath);
        if (!copy.Select(f => (f.Name, f.Length)).SequenceEqual(before.Select(f => (f.Name, f.Length)))) return null;
        var digest = ComputeDigest(copyPath, copy);
        return before.SequenceEqual(Metadata(livePath)) ? new ProgressSourceIdentity(digest, before) : null;
    }

    public bool Current(string path)
    {
        try { return Files.SequenceEqual(Metadata(path)); }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    public string Serialize() => JsonSerializer.Serialize(this);
    public static ProgressSourceIdentity? Parse(string? value)
    {
        if (value is null) return null;
        try
        {
            var identity = JsonSerializer.Deserialize<ProgressSourceIdentity>(value);
            return identity is { Digest.Length: > 0, Files: not null } ? identity : null;
        }
        catch (JsonException) { return null; }
    }

    private static string ComputeDigest(string projectPath, ProgressFileStamp[] files)
    {
        BeforeHash.Value?.Invoke();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in files)
        {
            var name = Encoding.UTF8.GetBytes(file.Name);
            hash.AppendData(BitConverter.GetBytes(name.Length));
            hash.AppendData(name);
            var path = file.Name == "project" ? projectPath : Path.Combine(Path.GetDirectoryName(projectPath)!,
                file.Name.Replace('/', Path.DirectorySeparatorChar));
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete,
                128 * 1024, FileOptions.SequentialScan);
            hash.AppendData(SHA256.HashData(stream));
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }
}
