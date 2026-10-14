namespace SIL.Motif.Commands;

/// <summary>
/// Admits one Apply at a time to a FieldWorks project, across processes, for the whole Apply: its reads, Finalize,
/// Dry Run and unit of work.
/// </summary>
/// <remarks>
/// Each of those steps opens the project on its own, so without this a second Apply could read the pending changes,
/// slip between the first one's steps, and reach Finalize after the first had already finalized the same Draft. The
/// lease is an exclusive open of a file beside the project, which the operating system releases when its process
/// ends. The file is never deleted, so two callers never hold locks on two different files of one name.
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

    /// <summary>Takes the lease, or returns <see langword="null"/> while another Apply holds it.</summary>
    public static ProjectApplyLease? TryAcquire(string fwDataPath)
    {
        try
        {
            return new ProjectApplyLease(new FileStream(PathFor(fwDataPath), FileMode.OpenOrCreate,
                FileAccess.ReadWrite, FileShare.None));
        }
        catch (IOException exception) when (IsHeldElsewhere(exception))
        {
            return null;
        }
    }

    public void Dispose() => _stream.Dispose();

    // Windows reports a sharing or lock violation; Unix reports the flock EWOULDBLOCK (11 Linux, 35 macOS).
    private static bool IsHeldElsewhere(IOException exception) =>
        exception.HResult is unchecked((int)0x80070020) or unchecked((int)0x80070021) ||
        (exception.HResult & 0xffff) is 11 or 35;
}
