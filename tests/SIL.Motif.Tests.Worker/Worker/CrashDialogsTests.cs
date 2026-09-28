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
    public void SuppressIsSafeOnEveryPlatform() => CrashDialogs.Suppress();

    [WindowsFact]
    public void SuppressTurnsOffTheWindowsCrashDialogForThisProcess()
    {
        CrashDialogs.Suppress();

        Assert.NotEqual(0u, GetErrorMode() & SemNoGpFaultErrorBox);
    }

    // A dialog would keep the child alive on Windows; prompt exit is required on every OS.
    [Fact]
    public async Task AChildThatCrashesExitsWithTheCrashCodeRatherThanWaitingOnADialog()
    {
        var start = new ProcessStartInfo(FakeParser.ExecutablePath, "--crash-unhandled")
        {
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var child = Process.Start(start)!;
        var standardErrorTask = child.StandardError.ReadToEndAsync();

        var exited = child.WaitForExit(30_000);
        if (!exited) child.Kill(entireProcessTree: true);

        Assert.True(exited, "The crashed child never exited: a crash dialog was holding it open.");
        var expectedCrashCode = OperatingSystem.IsWindows() ? unchecked((int)0xE0434352) : 134;
        var standardError = await standardErrorTask;
        Assert.True(child.ExitCode == expectedCrashCode,
            $"Expected exit code {expectedCrashCode}; got {child.ExitCode}.{Environment.NewLine}{standardError}");
    }

    [DllImport("kernel32.dll")]
    private static extern uint GetErrorMode();
}

internal sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows()) Skip = "Windows error-mode APIs are only available on Windows.";
    }
}
