using System.Diagnostics;
using System.Runtime.InteropServices;
using SIL.Motif.Host.Assistants;

namespace SIL.Motif.Cli;

internal static class TunnelSuperviseCommand
{
    internal static async Task<int> RunAsync(string[] args)
    {
        if (args.Length < 9 || args[0] != "--owner-pid" || !int.TryParse(args[1], out var ownerPid) ||
            args[2] != "--owner-start" || !long.TryParse(args[3], out var ownerStart) ||
            args[4] != "--executable" || args[6] != "--state-dir" || args[8] != "--") return 1;
        if (!TunnelProcessRegistry.IsOwnerAlive(ownerPid, ownerStart)) return 1;
        Microsoft.Win32.SafeHandles.SafeFileHandle? job = null;
        Process? child = null;
        var registry = new TunnelProcessRegistry(args[7]);
        long childStart = 0;
        long leaderStart = 0;
        var bundledDirectory = string.Empty;
        using var terminate = OperatingSystem.IsWindows() ? null :
            PosixSignalRegistration.Create(PosixSignal.SIGTERM, context => context.Cancel = true);
        try
        {
            if (OperatingSystem.IsWindows()) job = TunnelWindowsJob.Create(Process.GetCurrentProcess());
            else
            {
                bundledDirectory = TunnelUnixGroup.CanonicalDirectory(Path.GetDirectoryName(Path.GetFullPath(args[5]))!);
                TunnelUnixGroup.StartSession();
                using var leader = Process.GetCurrentProcess();
                leaderStart = TunnelProcessIdentity.Start(leader);
                registry.RecordLeader(ownerPid, ownerStart, leader.Id, leaderStart, bundledDirectory);
            }
            if (!TunnelProcessRegistry.IsOwnerAlive(ownerPid, ownerStart)) return 1;
            var start = new ProcessStartInfo(args[5]) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true };
            foreach (var argument in args[9..]) start.ArgumentList.Add(argument);
            start.Environment.Remove("ICU_DATA");
            child = Process.Start(start) ?? throw new InvalidOperationException();
            child.StandardInput.Close();
            if (!child.HasExited)
            {
                try
                {
                    childStart = TunnelProcessIdentity.Start(child);
                    registry.Record(ownerPid, ownerStart, child, OperatingSystem.IsWindows() ? 0 : Environment.ProcessId, leaderStart, bundledDirectory);
                }
                catch (IOException) when (child.HasExited) { }
                catch (InvalidOperationException) when (child.HasExited) { }
                catch (System.ComponentModel.Win32Exception) when (child.HasExited) { }
            }
            using var stop = new CancellationTokenSource();
            var exit = child.WaitForExitAsync();
            var inputClosed = WaitForInputClosedAsync();
            var ownerGone = WaitForOwnerAsync(ownerPid, ownerStart, stop.Token);
            var completed = await Task.WhenAny(exit, inputClosed, ownerGone).ConfigureAwait(false);
            stop.Cancel();
            if (OperatingSystem.IsWindows())
            {
                if (completed != exit && !child.HasExited) child.Kill(entireProcessTree: true);
            }
            else TunnelUnixGroup.Stop(Environment.ProcessId, leaderStart, supervisor: true, bundledDirectory: bundledDirectory);
            await child.WaitForExitAsync().ConfigureAwait(false);
            return child.ExitCode;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or
            System.ComponentModel.Win32Exception or System.Text.Json.JsonException)
        {
            return 1;
        }
        finally
        {
            if (child is not null)
            {
                if (OperatingSystem.IsWindows())
                {
                    if (!child.HasExited) { child.Kill(entireProcessTree: true); child.WaitForExit(); }
                }
                else if (leaderStart != 0)
                {
                    TunnelUnixGroup.Stop(Environment.ProcessId, leaderStart, supervisor: true, bundledDirectory: bundledDirectory);
                    if (!child.HasExited) child.WaitForExit();
                    if (OperatingSystem.IsLinux()) TunnelUnixGroup.Reap(Environment.ProcessId);
                }
                if (OperatingSystem.IsWindows() && childStart != 0)
                {
                    try { registry.Forget(ownerPid, ownerStart, child.Id, childStart); }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { }
                }
                child.Dispose();
            }
            GC.KeepAlive(job);
        }
    }

    private static async Task WaitForInputClosedAsync()
    {
        var input = Console.OpenStandardInput();
        var buffer = new byte[1];
        while (await input.ReadAsync(buffer).ConfigureAwait(false) != 0) { }
    }

    private static async Task WaitForOwnerAsync(int pid, long start, CancellationToken cancellation)
    {
        while (TunnelProcessRegistry.IsOwnerAlive(pid, start))
            await Task.Delay(200, cancellation).ConfigureAwait(false);
    }
}
