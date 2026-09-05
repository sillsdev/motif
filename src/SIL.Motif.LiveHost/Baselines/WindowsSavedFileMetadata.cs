using System;
using System.IO;
using Microsoft.Win32.SafeHandles;

namespace SIL.Motif.LiveHost.Baselines;

/// <summary>
/// Reads a saved file's last-write time from an already-open handle rather than its path.
/// </summary>
/// <remarks>
/// FieldWorks saves by renaming its temp file over the current <c>.fwdata</c>; once that rename has
/// happened, a path lookup at the original name observes the replacement file, not the one a held
/// handle is still reading. Resolving the timestamp through the handle instead makes the answer
/// correct regardless of when the rename lands relative to the read.
/// </remarks>
internal static class WindowsSavedFileMetadata
{
    /// <summary>The UTC last-write time of the file underlying <paramref name="handle"/>.</summary>
    public static DateTimeOffset GetLastWriteTimeUtc(SafeFileHandle handle) =>
        new(File.GetLastWriteTimeUtc(handle), TimeSpan.Zero);
}
