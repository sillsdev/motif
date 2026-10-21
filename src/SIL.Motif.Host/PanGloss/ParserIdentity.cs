using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace SIL.Motif.Host.PanGloss;

/// <summary>
/// Which parser an enqueuer expects a job to run on, and which one a Worker is: the SHA-256 of its bytes.
/// </summary>
/// <remarks>
/// The bytes fully determine the parser, and a development build keeps its version across rebuilds, so
/// <see cref="Key"/> is the hash alone and a job and a Worker match only when their keys are equal. The version
/// and path are information: the version is recorded only when already known, else "unknown", so reading an
/// identity never runs the parser. Two copies of one build are one parser, and a rebuild in place is another.
/// </remarks>
public sealed record ParserIdentity(string Version, string Sha256, string Path)
{
    private static readonly ConcurrentDictionary<(string Path, long Stamp, long Length), string> Hashes = new();

    /// <summary>The value a job and a Worker compare: the content hash alone.</summary>
    [JsonIgnore]
    public string Key => Sha256;

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
            return new ParserIdentity(ParserExecutableIdentity.CachedVersion(file.FullName) ?? "unknown", sha, file.FullName);
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
