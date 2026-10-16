using System.Runtime.InteropServices;

namespace SIL.Motif.Host;

/// <summary>
/// Makes SIGINT reach a Motif process on Linux and macOS even when the program that started it ignored the signal.
/// </summary>
/// <remarks>
/// <para>
/// A POSIX shell starts every background job of a script (<c>command &amp;</c>) with SIGINT ignored, and an ignored
/// disposition survives exec. .NET honours it: <see cref="Console.CancelKeyPress"/> then never fires, so an
/// interrupt sent to the CLI or the runner is dropped, the parser keeps running, and the process holds the machine's
/// parser capacity until something kills it. Motif's commands promise that an interrupt cancels them cleanly, so
/// the process restores the default disposition before it subscribes; .NET then installs its own handler as usual
/// (pinned by `InterruptingHandoffStartedWithInterruptIgnoredStillStopsTheParser`).
/// </para>
/// <para>
/// Only an ignored SIGINT is changed: a handler the runtime has already installed is left alone. On Windows this
/// does nothing, since Ctrl+Break is not a Unix signal.
/// </para>
/// </remarks>
public static partial class InterruptSignal
{
    private const int SigInt = 2;
    private static readonly IntPtr SigDfl = IntPtr.Zero;
    private static readonly IntPtr SigIgn = 1;

    // Larger than struct sigaction on every supported Unix; only its leading handler field is read.
    private const int SigactionBufferSize = 512;

    /// <summary>Restores the default SIGINT disposition when this process inherited it ignored.</summary>
    /// <returns>True when SIGINT had been ignored and is now delivered.</returns>
    public static bool RestoreIfIgnored()
    {
        if (OperatingSystem.IsWindows()) return false;
        var current = Marshal.AllocHGlobal(SigactionBufferSize);
        try
        {
            if (Sigaction(SigInt, IntPtr.Zero, current) != 0) return false;
            if (Marshal.ReadIntPtr(current) != SigIgn) return false;
            return Signal(SigInt, SigDfl) == SigIgn;
        }
        finally
        {
            Marshal.FreeHGlobal(current);
        }
    }

    [LibraryImport("libc", EntryPoint = "sigaction")]
    private static partial int Sigaction(int signal, IntPtr action, IntPtr oldAction);

    [LibraryImport("libc", EntryPoint = "signal")]
    private static partial IntPtr Signal(int signal, IntPtr handler);
}
