using System.Security.Cryptography;
using SIL.Motif.Contract.Baselines;

namespace SIL.Motif.Worker.Jobs;

/// <summary>The exact published Baseline files a Dry Run was queued against.</summary>
public sealed record DryRunSourceBinding(BaselineToken Token, string RootDirectory, string FwDataPath,
    string SourceSha256)
{
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Token);
        if (string.IsNullOrWhiteSpace(RootDirectory) || string.IsNullOrWhiteSpace(FwDataPath) ||
            !File.Exists(FwDataPath) || !Directory.Exists(RootDirectory))
            throw new FileNotFoundException("The original Baseline source for this Dry Run is unavailable.", FwDataPath);
        var root = Path.GetFullPath(RootDirectory);
        var project = Path.GetFullPath(FwDataPath);
        var relative = Path.GetRelativePath(root, project);
        if (Path.IsPathRooted(relative) || relative is "." or ".." ||
            relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidDataException("The Dry Run project path is outside its recorded Baseline bundle.");
        var actual = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(project)));
        if (!StringComparer.Ordinal.Equals(actual, SourceSha256))
            throw new InvalidDataException("The recorded Baseline project bytes changed after the Dry Run was queued.");
    }
}
