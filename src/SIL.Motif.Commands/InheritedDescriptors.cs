using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

namespace SIL.Motif.Commands;

/// <summary>Keeps a child process from inheriting the descriptors, and so the locks, this process holds.</summary>
internal static class InheritedDescriptors
{
    private const int GetFlags = 1;
    private const int SetFlags = 2;
    private const int CloseOnExec = 1;
    private const int FirstInheritable = 3;

    /// <summary>
    /// Marks every open descriptor above standard error close-on-exec, so the next exec in this process
    /// leaves them behind. A descriptor a caller handed down, such as the one `flock &lt;file&gt; motif ...`
    /// leaves open, would otherwise keep its lock alive for as long as the runner lives.
    /// </summary>
    /// <remarks>
    /// This process never execs itself, so its own use of the descriptors is unchanged. Linux only: `fcntl` is
    /// variadic, and Apple arm64 passes variadic arguments differently from a fixed-signature P/Invoke, so macOS
    /// and Windows are a no-op rather than a call that could set the wrong flags.
    /// </remarks>
    public static void MarkCloseOnExec()
    {
        if (!OperatingSystem.IsLinux())
            return;
        foreach (var descriptor in Open())
        {
            var flags = fcntl(descriptor, GetFlags, 0);
            if (flags >= 0 && (flags & CloseOnExec) == 0)
                _ = fcntl(descriptor, SetFlags, flags | CloseOnExec);
        }
    }

    private static List<int> Open()
    {
        var descriptors = new List<int>();
        const string directory = "/proc/self/fd";
        try
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
                if (int.TryParse(Path.GetFileName(entry), NumberStyles.None, CultureInfo.InvariantCulture, out var number) &&
                    number >= FirstInheritable)
                    descriptors.Add(number);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
        return descriptors;
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int fcntl(int fd, int command, int argument);
}
