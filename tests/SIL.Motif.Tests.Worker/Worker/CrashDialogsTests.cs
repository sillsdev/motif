using System.Diagnostics;
using System.Runtime.InteropServices;
using SIL.Motif.Host;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Worker;

/// <summary>Pins that no process this suite starts can be held open by a Windows crash dialog.</summary>
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

    // While a crash dialog is up Windows keeps the crashed process alive, so a prompt exit proves there was none.
    [Fact]
    public void AChildThatCrashesExitsWithTheCrashCodeRatherThanWaitingOnADialog()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        var start = new ProcessStartInfo(FakeParser.ExecutablePath, "--crash-unhandled")
        {
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var child = Process.Start(start)!;
        _ = child.StandardError.ReadToEndAsync();

        var exited = child.WaitForExit(30_000);
        if (!exited) child.Kill(entireProcessTree: true);

        Assert.True(exited, "The crashed child never exited: a crash dialog was holding it open.");
        Assert.Equal(unchecked((int)0xE0434352), child.ExitCode);
    }

    [DllImport("kernel32.dll")]
    private static extern uint GetErrorMode();
}
