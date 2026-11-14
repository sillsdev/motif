using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Commands;

/// <summary>
/// Excludes pending-Draft writes from an Apply, and admits one Apply at a time across processes.
/// </summary>
/// <remarks>
/// Each of those steps opens the project on its own, so without this a second Apply could read the pending changes,
/// slip between the first one's steps, and reach Finalize after the first had already finalized the same Draft. The
/// lease also keeps a pending change from being saved between authorization and the live mutation. It is an
/// exclusive open of a file beside the project, released when its process ends. The file is never deleted, so
/// two callers never hold locks on two different files of one name.
/// </remarks>
internal sealed class ProjectApplyLease : IDisposable
{
    private const string FileSuffix = ".motif-apply.lock";

    private readonly FileStream _stream;

    private ProjectApplyLease(FileStream stream) => _stream = stream;

    /// <summary>Returns the project-adjacent lease path for a FieldWorks project.</summary>
    public static string PathFor(string fwDataPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fwDataPath);
        return Path.GetFullPath(fwDataPath) + FileSuffix;
    }

    /// <summary>Takes the lease, or returns <see langword="null"/> while another caller holds it.</summary>
    public static ProjectApplyLease? TryAcquire(string fwDataPath)
    {
        try
        {
            return new ProjectApplyLease(new FileStream(PathFor(fwDataPath), FileMode.OpenOrCreate,
                FileAccess.ReadWrite, FileShare.None));
        }
        catch (IOException exception) when (ProjectFileHeldException.IsSharingViolation(exception))
        {
            return null;
        }
    }

    internal static Refusal Busy(string projectPath) => new("apply.in-progress", FailureReason.Busy,
        "Another Apply or pending change is in progress for this project. Try again when it finishes.",
        new Dictionary<string, string>(StringComparer.Ordinal) { ["fwDataPath"] = projectPath });

    public void Dispose() => _stream.Dispose();
}
