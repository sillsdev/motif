using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace SIL.Motif.Worker.Parsimony;

/// <summary>Builds compact, project-owned paths for derived Parsimony artifacts.</summary>
public static class ParsimonyArtifactPath
{
    /// <summary>Returns the worker-owned Parsimony root for one project.</summary>
    /// <param name="workerRoot">The worker-owned root directory.</param>
    /// <param name="projectKey">The exact project workspace key.</param>
    /// <returns>A deterministic project-specific Parsimony directory.</returns>
    public static string ProjectRoot(string workerRoot, string projectKey) =>
        Path.Combine(workerRoot, "p", ProjectDirectoryName(projectKey));

    /// <summary>Returns the exact worker-owned directory for one registered evidence bundle.</summary>
    /// <param name="workerRoot">The worker-owned root directory.</param>
    /// <param name="projectKey">The exact project workspace key.</param>
    /// <param name="baselineDigest">The Baseline bundle digest.</param>
    /// <param name="modelFingerprint">The model fingerprint.</param>
    /// <param name="materialKey">The Baseline material digest.</param>
    /// <param name="inputKind">Whether the material is a baseline or candidate.</param>
    /// <param name="candidateIdentity">The candidate identity when input kind is candidate.</param>
    /// <param name="bundleId">The complete bundle identity recorded in the Motif store.</param>
    /// <returns>The deterministic directory for the exact registered bundle.</returns>
    public static string BundleDirectory(string workerRoot, string projectKey, string baselineDigest,
        string modelFingerprint, string materialKey, string inputKind, string? candidateIdentity, string bundleId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bundleId);
        return Path.Combine(MaterialDirectory(ProjectRoot(workerRoot, projectKey), baselineDigest, modelFingerprint,
            materialKey, inputKind, candidateIdentity), BundleDirectoryName(bundleId));
    }

    // 64-bit project and 128-bit material names keep a 140-character worker root below 260 characters.
    internal static string ProjectDirectoryName(string projectKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectKey);
        return Hash("project", projectKey)[..16];
    }

    internal static string MaterialDirectory(string projectRoot, string baselineDigest, string modelFingerprint,
        string materialDigest, string inputKind, string? candidateIdentity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelFingerprint);
        ValidateInputBinding(inputKind, candidateIdentity);
        return Path.Combine(projectRoot, Hash("material", Digest(baselineDigest, nameof(baselineDigest)), modelFingerprint,
            Digest(materialDigest, nameof(materialDigest)), inputKind, candidateIdentity ?? string.Empty)[..32]);
    }

    /// <summary>Returns the compact directory name for one bundle identity.</summary>
    /// <param name="bundleId">The complete bundle identity recorded in the Motif store.</param>
    /// <returns>A deterministic directory name for that bundle.</returns>
    public static string BundleDirectoryName(string bundleId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bundleId);
        return Hash("bundle", bundleId)[..32];
    }

    internal static string StagingDirectoryName(string bundleId) => "s" + BundleDirectoryName(bundleId);

    private static void ValidateInputBinding(string inputKind, string? candidateIdentity)
    {
        if (inputKind is not ("baseline" or "candidate") ||
            (inputKind == "candidate") != !string.IsNullOrWhiteSpace(candidateIdentity))
            throw new ArgumentException("Candidate identity must be present exactly for candidate material.",
                nameof(candidateIdentity));
    }

    private static string Digest(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        var digest = value.StartsWith("sha256:", StringComparison.Ordinal) ? value[7..] : value;
        if (digest.Length != 64 || digest.Any(character => !Uri.IsHexDigit(character)))
            throw new ArgumentException("A SHA-256 digest is required.", parameterName);
        return digest.ToLowerInvariant();
    }

    private static string Hash(params string[] values)
    {
        using var input = new MemoryStream();
        Span<byte> length = stackalloc byte[sizeof(int)];
        // Length framing prevents distinct identity tuples from sharing the same hash input.
        foreach (var value in values)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
            input.Write(length);
            input.Write(bytes);
        }
        return Convert.ToHexString(SHA256.HashData(input.ToArray())).ToLowerInvariant();
    }
}
