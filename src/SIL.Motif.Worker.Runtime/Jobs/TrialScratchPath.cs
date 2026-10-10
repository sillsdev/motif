using System.Security.Cryptography;
using System.Text;

namespace SIL.Motif.Worker.Jobs;

/// <summary>Names the private Trial copy by durable job identity.</summary>
public static class TrialScratchPath
{
    /// <summary>Relocates the Trial copies, so two test processes or installations never share a folder.</summary>
    public const string RootVariable = "MOTIF_TRIAL_SCRATCH_ROOT";

    /// <summary>Returns the root directory that holds private Trial copies.</summary>
    public static string Root => Environment.GetEnvironmentVariable(RootVariable) is { Length: > 0 } relocated
        ? relocated
        : Path.Combine(Path.GetTempPath(), "SIL.Motif.Trial");

    /// <summary>Returns the per-job scratch directory under the system temporary folder.</summary>
    public static string ForJob(string jobId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);
        var name = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(jobId))).ToLowerInvariant();
        return Path.Combine(Root, name);
    }

    /// <summary>Finds the one FieldWorks project file saved in a Trial scratch directory.</summary>
    public static string? ProjectFile(string scratchRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scratchRoot);
        if (!Directory.Exists(scratchRoot)) return null;
        var projects = Directory.EnumerateFiles(scratchRoot, "*.fwdata", SearchOption.AllDirectories).ToArray();
        return projects.Length == 1 ? projects[0] : null;
    }
}
