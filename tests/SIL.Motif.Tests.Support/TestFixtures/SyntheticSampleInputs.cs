using System.Security.Cryptography;
using System.Text.Json;

namespace SIL.Motif.Tests.TestFixtures;

internal static class SyntheticSampleInputs
{
    private const string LockName = "synthetic-samples.lock.json";
    private const string Release = "fixture-inputs-v1";

    internal static string ResolveRoot(string repositoryRoot)
    {
        var lockPath = Path.Combine(repositoryRoot, "evals", LockName);
        using var manifest = JsonDocument.Parse(File.ReadAllText(lockPath));
        var configured = Environment.GetEnvironmentVariable("MOTIF_SYNTHETIC_SAMPLES");
        var root = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(repositoryRoot, "bin", ".cache", "synthetic-samples",
                manifest.RootElement.GetProperty("commit").GetString()!, "samples")
            : Path.GetFullPath(configured);
        VerifyRoot(root, manifest.RootElement);
        return root;
    }

    internal static string SampleDirectory(string repositoryRoot, string sampleId)
    {
        if (sampleId is not ("synthetic-bantu" or "synthetic-turkic"))
            throw new ArgumentException($"Unknown required synthetic sample identity: {sampleId}", nameof(sampleId));
        return Path.Combine(ResolveRoot(repositoryRoot), sampleId);
    }

    internal static void VerifyRoot(string root, JsonElement manifest)
    {
        if (!Directory.Exists(root))
            throw MissingRoot(root, manifest.GetProperty("release").GetString()!);
        RejectLink(root);

        var expectedDirectories = new HashSet<string>(["synthetic-bantu", "synthetic-turkic"], StringComparer.Ordinal);
        var directories = Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories).ToArray();
        if (directories.Length != expectedDirectories.Count || directories.Any(path =>
                !expectedDirectories.Contains(Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/'))))
            throw new InvalidDataException($"Synthetic sample input root must contain exactly the catalog sample directories: {root}");
        foreach (var path in directories) RejectLink(path);

        var manifestFiles = manifest.GetProperty("files").EnumerateArray().ToArray();
        if (manifestFiles.Length != 6)
            throw new InvalidDataException($"{LockName} must name exactly six files.");
        var expectedFiles = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in manifestFiles)
        {
            var portablePath = entry.GetProperty("path").GetString()!;
            if (!portablePath.StartsWith("samples/", StringComparison.Ordinal))
                throw new InvalidDataException($"Invalid path in {LockName}: {portablePath}");
            var relative = portablePath["samples/".Length..].Replace('/', Path.DirectorySeparatorChar);
            var path = Path.GetFullPath(Path.Combine(root, relative));
            var prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
            if (!path.StartsWith(prefix, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                throw new InvalidDataException($"Synthetic sample path escapes its root: {portablePath}");
            if (!File.Exists(path)) throw MissingFile(path, manifest.GetProperty("release").GetString()!);
            RejectLink(path);
            var info = new FileInfo(path);
            if (info.Length != entry.GetProperty("bytes").GetInt64())
                throw new InvalidDataException($"Synthetic sample input has the wrong size: {path}");
            var actualHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
            if (!string.Equals(actualHash, entry.GetProperty("sha256").GetString(), StringComparison.Ordinal))
                throw new InvalidDataException($"Synthetic sample input failed SHA-256 verification: {path}");
            expectedFiles.Add(relative.Replace(Path.DirectorySeparatorChar, '/'));
        }
        if (expectedFiles.Count != 6)
            throw new InvalidDataException($"{LockName} must name six distinct files.");

        var actualFiles = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).ToArray();
        foreach (var path in actualFiles) RejectLink(path);
        if (actualFiles.Length != expectedFiles.Count || actualFiles.Any(path =>
                !expectedFiles.Contains(Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/'))))
            throw new InvalidDataException($"Synthetic sample input root must contain exactly the catalog files: {root}");
    }

    private static void RejectLink(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException($"Synthetic sample inputs may not contain links: {path}");
    }

    private static DirectoryNotFoundException MissingRoot(string path, string release) =>
        new($"Verified synthetic sample inputs are missing at {path}. Provision release {release} with ./evals/Get-SyntheticSamples.ps1 -Download.");

    private static FileNotFoundException MissingFile(string path, string release) =>
        new($"Required synthetic sample input is missing at {path}. Provision release {release} with ./evals/Get-SyntheticSamples.ps1 -Download.", path);
}
