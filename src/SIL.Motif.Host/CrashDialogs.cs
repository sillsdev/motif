using System.Runtime.InteropServices;

namespace SIL.Motif.Host;

/// <summary>
/// Keeps Windows from showing its crash dialog for a Motif process that runs without a window of its own.
/// </summary>
/// <remarks>
/// <para>
/// The runner and the CLI are started by other programs, often in the background, where nobody is watching.
/// When one of them dies from an unhandled exception, Windows Error Reporting would otherwise put up a
/// modal "unknown software exception (0xe0434352)" box that blocks nothing useful and cannot be answered
/// by the program that started the process. The failure is still reported where it belongs: on standard
/// error, in the exit code, and in the Windows Application event log.
/// </para>
/// <para>
/// The error mode is inherited by child processes, so the parser a runner starts is covered too. On other
/// platforms this does nothing, since no such dialog exists there.
/// </para>
/// </remarks>
public static class CrashDialogs
{
    private const uint SemFailCriticalErrors = 0x0001;
    private const uint SemNoGpFaultErrorBox = 0x0002;
    private const uint SemNoOpenFileErrorBox = 0x8000;
    private const uint WerFaultReportingNoUi = 0x0020;

    /// <summary>Turns the crash dialog off for this process and every process it starts afterwards.</summary>
    public static void Suppress()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
        SetErrorMode(GetErrorMode() | SemFailCriticalErrors | SemNoGpFaultErrorBox | SemNoOpenFileErrorBox);
        WerSetFlags(WerFaultReportingNoUi);
    }

    [DllImport("kernel32.dll")]
    private static extern uint SetErrorMode(uint mode);

    [DllImport("kernel32.dll")]
    private static extern uint GetErrorMode();

    [DllImport("kernel32.dll")]
    private static extern int WerSetFlags(uint flags);
}
