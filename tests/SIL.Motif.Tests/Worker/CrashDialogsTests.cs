using System.Runtime.InteropServices;
using SIL.Motif.Host;
using Xunit;

namespace SIL.Motif.Tests.Worker;

/// <summary>Pins that suppression reaches the process error mode, which child processes inherit.</summary>
public sealed class CrashDialogsTests
{
    private const uint SemNoGpFaultErrorBox = 0x0002;

    [Fact]
    public void SuppressTurnsOffTheWindowsCrashDialogForThisProcess()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        CrashDialogs.Suppress();

        Assert.NotEqual(0u, GetErrorMode() & SemNoGpFaultErrorBox);
    }

    [DllImport("kernel32.dll")]
    private static extern uint GetErrorMode();
}
