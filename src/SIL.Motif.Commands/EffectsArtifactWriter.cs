using System.IO;
using System.Security.Cryptography;
using System.Text;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Commands;

/// <summary>Writes complete operation effects under the worker root for one FieldWorks project.</summary>
public static class EffectsArtifactWriter
{
    /// <summary>Writes a completed Dry Run's effects and returns their worker-root path.</summary>
    public static string WriteDryRun(string workerRoot, string projectPath, string jobId,
        DryRunProjection projection) => Write(workerRoot, projectPath, "dry-run", jobId, projection.Effects);

    /// <summary>Writes an Apply's effects and returns their worker-root path.</summary>
    public static string WriteApply(string workerRoot, string projectPath, string proposalId,
        string effectDigest, IReadOnlyList<EffectView> effects) =>
        Write(workerRoot, projectPath, "apply", proposalId + "\n" + effectDigest, effects);

    private static string Write(string workerRoot, string projectPath, string kind, string identity,
        IReadOnlyList<EffectView> effects)
    {
        var fullProjectPath = Path.GetFullPath(projectPath);
        var workspaceKey = ProjectWorkspaceKey.Compute(new ProjectLocator(fullProjectPath,
            Path.GetFileNameWithoutExtension(fullProjectPath)));
        var projectSegment = ProjectWorkspaceKey.StorageSegment(workspaceKey);
        var identityHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))
            .ToLowerInvariant()[..16];
        var directory = Path.Combine(Path.GetFullPath(workerRoot), projectSegment, "artifacts", "effects");
        var ownership = WorkspaceOwnership.Bootstrap(workerRoot);
        if (!ownership.IsOwned(directory)) throw new IOException("The effects directory is not worker-owned.");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{kind}-{identityHash}-{Guid.NewGuid():N}.json");
        var staging = path + ".tmp";
        try
        {
            using (var stream = new FileStream(staging, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                stream.Write(ProjectionJson.ToUtf8Bytes(effects));
            File.Move(staging, path);
            return path;
        }
        finally
        {
            File.Delete(staging);
        }
    }
}
