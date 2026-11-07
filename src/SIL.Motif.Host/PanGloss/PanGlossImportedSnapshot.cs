using SIL.Motif.Host.Assess;

namespace SIL.Motif.Host.PanGloss;

/// <summary>The exact snapshot and source project produced together, bound to both files' hashes.</summary>
public sealed record PanGlossImportedSnapshot(string Path, string ProjectSha256, string SnapshotSha256)
{
    /// <summary>Allows publication to move this caller-owned temporary file into the artifact store.</summary>
    public bool TransferOwnership { get; init; }
    /// <summary>Binds a completed import's two files for use by the same run's batch.</summary>
    public static PanGlossImportedSnapshot Capture(string projectPath, string snapshotPath) =>
        new(snapshotPath, BatchInvocationEvidence.DigestFile(projectPath), BatchInvocationEvidence.DigestFile(snapshotPath));

    /// <summary>Whether both files still contain the imported bytes.</summary>
    public bool Matches(string projectPath) => File.Exists(Path) && File.Exists(projectPath) &&
        ProjectSha256 == BatchInvocationEvidence.DigestFile(projectPath) &&
        SnapshotSha256 == BatchInvocationEvidence.DigestFile(Path);
}
