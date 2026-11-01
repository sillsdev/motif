using SIL.LCModel;

namespace SIL.Motif.Host.LcmUtils;

/// <summary>
/// Opening the project file failed because another process has that file open. It is a
/// <see cref="LcmFileLockedException"/> because it means the same thing to a caller: the project is held and the
/// command can be retried once the holder lets go.
/// </summary>
/// <remarks>
/// Only <see cref="FwDataProjectLoader"/> raises it, around opening the project, so a sharing violation on any other
/// file a command touches (a words file, an export, a Motif artifact) keeps the refusal it always had.
/// </remarks>
public sealed class ProjectFileHeldException(string fwDataPath)
    : LcmFileLockedException($"The project file '{Path.GetFileName(fwDataPath)}' is open in another process.")
{
    /// <summary>
    /// Whether <paramref name="exception"/>, or one inside it, is an IOException that another process holding a file
    /// causes.
    /// </summary>
    public static bool IsSharingViolation(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is IOException io && IsHeldHResult(io.HResult)) return true;
        }
        return false;
    }

    /// <summary>
    /// Whether another process has the file open in a way that stops this one reading it. LibLCM reports that as an
    /// <see cref="LcmInitializationException"/> that carries only the operating system's message, so a failed open
    /// asks the file itself.
    /// </summary>
    public static bool IsHeld(string path)
    {
        try
        {
            using var probe = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return false;
        }
        catch (IOException probeFailure)
        {
            return IsSharingViolation(probeFailure);
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool IsHeldHResult(int hResult)
    {
        // The Windows sharing and lock violations; .NET on Unix may report the same values.
        if (hResult is unchecked((int)0x80070020) or unchecked((int)0x80070021)) return true;
        // Unix reports the flock EWOULDBLOCK: 11 on Linux, 35 on macOS. Elsewhere those numbers mean other things.
        var errno = hResult & 0xffff;
        if (OperatingSystem.IsLinux()) return errno == 11;
        if (OperatingSystem.IsMacOS()) return errno == 35;
        return false;
    }
}
