using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace SIL.Motif.Host.PanGloss;

/// <summary>
/// Which parser an enqueuer expects a job to run on, and which one a Worker is: the version the executable
/// reports and the SHA-256 of its bytes.
/// </summary>
/// <remarks>
/// The version alone is too weak: a development build of PanGloss keeps its version across rebuilds, and the
/// 0.8.6 and 0.8.8 incident was one stale executable of the wrong release. The content hash is what makes two
/// executables the same parser, so <see cref="Key"/> is the version and the hash, and a job and a Worker
/// match only when their keys are equal. The path is recorded as information alone: two copies of one build
/// are one parser, and a rebuild in place is another.
/// </remarks>
public sealed record ParserIdentity(string Version, string Sha256, string Path)
{
    private static readonly ConcurrentDictionary<(string Path, long Stamp, long Length), string> Hashes = new();

    /// <summary>The value a job and a Worker compare: version and content hash.</summary>
    [JsonIgnore]
    public string Key => Version + "|" + Sha256;

    /// <summary>Reads the identity of the executable at <paramref name="path"/>, or null when there is none.</summary>
    public static ParserIdentity? Read(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists) return null;
            var sha = Hashes.GetOrAdd((file.FullName, file.LastWriteTimeUtc.Ticks, file.Length), key =>
            {
                using var stream = File.OpenRead(key.Path);
                return "sha256:" + Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            });
            return new ParserIdentity(ParserExecutableIdentity.ReadVersion(file.FullName), sha, file.FullName);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            ArgumentException)
        {
            return null;
        }
    }

    /// <summary>The key of the executable at <paramref name="path"/>, or null when there is none.</summary>
    public static string? KeyOf(string? path) => Read(path)?.Key;

    /// <summary>A short human form for warnings: the version and the start of the hash.</summary>
    public static string Describe(string? version, string? sha256) =>
        (string.IsNullOrEmpty(version) ? "unknown version" : version) + " (" +
        (string.IsNullOrEmpty(sha256) ? "no hash" : sha256[..Math.Min(sha256.Length, 19)]) + ")";
}
