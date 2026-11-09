using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace SIL.Motif.Host.Assistants;

/// <summary>Owns the Unix session used by one tunnel supervisor and its descendants.</summary>
public static partial class TunnelUnixGroup
{
    /// <summary>Creates a new session before starting the tunnel, and adopts orphaned descendants on Linux.</summary>
    public static void StartSession()
    {
        if (OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        if (SetSession() < 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError());
        if (OperatingSystem.IsLinux() && Prctl(36, 1, 0, 0, 0) != 0)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError());
    }

    /// <summary>Resolves a bundled client directory while it exists, for later recovery after an update.</summary>
    public static string CanonicalDirectory(string directory)
    {
        if (OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var resolved = RealPath(directory, IntPtr.Zero);
        if (resolved == IntPtr.Zero) return string.Empty;
        try { return Marshal.PtrToStringUTF8(resolved) ?? string.Empty; }
        finally { Free(resolved); }
    }

    /// <summary>Stops a verified group, or only verified bundled executables when its leader cannot be identified.</summary>
    public static bool Stop(int groupId, long leaderStart, bool supervisor = false, string bundledDirectory = "")
    {
        if (OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        if (groupId <= 1 || (!supervisor && groupId == GetProcessGroup(0))) return false;
        if (!LeaderMatches(groupId, leaderStart)) return StopBundledMembers(groupId, bundledDirectory);
        var exclude = supervisor ? Environment.ProcessId : 0;
        if (!HasMembers(groupId, exclude)) return true;
        if (!LeaderMatches(groupId, leaderStart)) return StopBundledMembers(groupId, bundledDirectory);
        if (Kill(-groupId, 15) != 0 && Marshal.GetLastPInvokeError() != 3) return false;
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromMilliseconds(500))
        {
            if (!LeaderMatches(groupId, leaderStart)) return StopBundledMembers(groupId, bundledDirectory);
            if (!HasMembers(groupId, exclude)) return true;
            Thread.Sleep(25);
        }
        if (!LeaderMatches(groupId, leaderStart)) return StopBundledMembers(groupId, bundledDirectory);
        if (Kill(-groupId, 9) != 0 && Marshal.GetLastPInvokeError() != 3) return false;
        if (supervisor) return false;
        deadline.Restart();
        while (deadline.Elapsed < TimeSpan.FromSeconds(5))
        {
            if (!LeaderMatches(groupId, leaderStart)) return StopBundledMembers(groupId, bundledDirectory);
            if (!HasMembers(groupId)) return true;
            Thread.Sleep(25);
        }
        return LeaderMatches(groupId, leaderStart)
            ? !HasMembers(groupId) : StopBundledMembers(groupId, bundledDirectory);
    }

    /// <summary>Reports whether the identity evidence still identifies a live group or bundled member.</summary>
    public static bool CanForget(int groupId, long leaderStart, string bundledDirectory)
        => LeaderMatches(groupId, leaderStart) ? !HasMembers(groupId) : !BundledMembers(groupId, bundledDirectory).Any();

    private static bool LeaderMatches(int groupId, long leaderStart)
    {
        try
        {
            using var leader = Process.GetProcessById(groupId);
            return !leader.HasExited && TunnelProcessIdentity.Start(leader) == leaderStart && GetProcessGroup(groupId) == groupId;
        }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
        catch (System.ComponentModel.Win32Exception) { return false; }
    }

    private static bool StopBundledMembers(int groupId, string directory)
    {
        SignalBundledMembers(groupId, directory, 15);
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromMilliseconds(500))
        {
            if (!BundledMembers(groupId, directory).Any()) return true;
            Thread.Sleep(25);
        }
        SignalBundledMembers(groupId, directory, 9);
        deadline.Restart();
        while (deadline.Elapsed < TimeSpan.FromSeconds(5))
        {
            if (!BundledMembers(groupId, directory).Any()) return true;
            Thread.Sleep(25);
        }
        return !BundledMembers(groupId, directory).Any();
    }

    private static void SignalBundledMembers(int groupId, string directory, int signal)
    {
        foreach (var member in BundledMembers(groupId, directory))
        {
            try
            {
                using var process = Process.GetProcessById(member.Pid);
                if (TunnelProcessIdentity.Start(process) == member.Start && IsBundledMember(member.Pid, groupId, directory))
                    Kill(member.Pid, signal);
            }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (System.ComponentModel.Win32Exception) { }
        }
    }

    private static IEnumerable<(int Pid, long Start)> BundledMembers(int groupId, string directory)
    {
        if (string.IsNullOrEmpty(directory) || !Path.IsPathFullyQualified(directory)) yield break;
        foreach (var pid in GroupMembers(groupId))
        {
            if (pid == Environment.ProcessId || !IsBundledMember(pid, groupId, directory)) continue;
            long start;
            try
            {
                using var process = Process.GetProcessById(pid);
                if (process.HasExited) continue;
                start = TunnelProcessIdentity.Start(process);
            }
            catch (ArgumentException) { continue; }
            catch (InvalidOperationException) { continue; }
            catch (IOException) { continue; }
            catch (UnauthorizedAccessException) { continue; }
            catch (System.ComponentModel.Win32Exception) { continue; }
            yield return (pid, start);
        }
    }

    private static bool IsBundledMember(int pid, int groupId, string directory)
    {
        try
        {
            if (GetProcessGroup(pid) != groupId) return false;
            string? path;
            if (OperatingSystem.IsLinux())
            {
                var stat = File.ReadAllText($"/proc/{pid}/stat");
                var end = stat.LastIndexOf(')');
                if (end < 0) return false;
                var fields = stat[(end + 2)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (fields.Length <= 2 || fields[0] == "Z" || !int.TryParse(fields[2], out var group) || group != groupId)
                    return false;
                path = File.ResolveLinkTarget($"/proc/{pid}/exe", true)?.FullName;
            }
            else path = MacProcessPath(pid);
            var prefix = directory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return path is not null && path.StartsWith(prefix, StringComparison.Ordinal);
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private static IEnumerable<int> GroupMembers(int groupId)
    {
        if (OperatingSystem.IsLinux())
            return Directory.EnumerateDirectories("/proc").Select(Path.GetFileName)
                .Select(name => int.TryParse(name, out var pid) ? pid : 0).Where(pid => pid > 0).ToArray();
        return MacGroupMembers(groupId);
    }

    private static unsafe int[] MacGroupMembers(int groupId)
    {
        var size = 128;
        while (true)
        {
            var members = new int[size];
            int count;
            fixed (int* buffer = members) count = ProcListGroup(groupId, buffer, members.Length * sizeof(int));
            if (count < 0) throw new IOException("Could not inspect tunnel process group.");
            if (count < members.Length) return members[..count];
            size = checked(size * 2);
        }
    }

    private static unsafe string? MacProcessPath(int pid)
    {
        var path = new byte[4096];
        int length;
        fixed (byte* buffer = path) length = ProcPidPath(pid, buffer, (uint)path.Length);
        if (length <= 0) return null;
        if (path[length - 1] == 0) length--;
        return Encoding.UTF8.GetString(path, 0, length);
    }

    /// <summary>Reports whether a group still has a running member, excluding an optional supervisor.</summary>
    public static bool HasMembers(int groupId, int excludePid = 0)
    {
        if (OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        if (OperatingSystem.IsLinux())
        {
            foreach (var directory in Directory.EnumerateDirectories("/proc"))
            {
                if (!int.TryParse(Path.GetFileName(directory), out var pid) || pid == excludePid) continue;
                try
                {
                    var stat = File.ReadAllText(Path.Combine(directory, "stat"));
                    var end = stat.LastIndexOf(')');
                    if (end < 0) continue;
                    var fields = stat[(end + 2)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (fields.Length > 2 && fields[0] != "Z" && int.TryParse(fields[2], out var group) && group == groupId)
                        return true;
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            return false;
        }
        var start = new ProcessStartInfo("/bin/ps")
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true,
        };
        start.ArgumentList.Add("-A");
        start.ArgumentList.Add("-o");
        start.ArgumentList.Add("pid=,pgid=,stat=");
        start.Environment.Remove("ICU_DATA");
        using var ps = Process.Start(start) ?? throw new InvalidOperationException();
        var output = ps.StandardOutput.ReadToEnd();
        var error = ps.StandardError.ReadToEnd();
        ps.WaitForExit();
        if (ps.ExitCode != 0) throw new IOException("Could not inspect tunnel process groups: " + error);
        foreach (var line in output.Split('\n'))
        {
            var fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length >= 3 && int.TryParse(fields[0], out var pid) && pid != excludePid && pid != ps.Id &&
                int.TryParse(fields[1], out var group) && group == groupId && !fields[2].StartsWith('Z')) return true;
        }
        return false;
    }

    /// <summary>Reaps exited descendants after the managed tunnel process has been observed.</summary>
    public static void Reap(int groupId)
    {
        while (WaitPid(-groupId, out _, 1) > 0) { }
    }

    [LibraryImport("/usr/lib/libproc.dylib", EntryPoint = "proc_listpgrppids", SetLastError = true)]
    private static unsafe partial int ProcListGroup(int groupId, int* buffer, int size);
    [LibraryImport("/usr/lib/libproc.dylib", EntryPoint = "proc_pidpath", SetLastError = true)]
    private static unsafe partial int ProcPidPath(int pid, byte* buffer, uint size);
    [LibraryImport("libc", EntryPoint = "realpath", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr RealPath(string path, IntPtr resolved);
    [LibraryImport("libc", EntryPoint = "free")]
    private static partial void Free(IntPtr pointer);
    [LibraryImport("libc", EntryPoint = "setsid", SetLastError = true)]
    private static partial int SetSession();
    [LibraryImport("libc", EntryPoint = "getpgid", SetLastError = true)]
    private static partial int GetProcessGroup(int pid);
    [LibraryImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static partial int Kill(int pid, int signal);
    [LibraryImport("libc", EntryPoint = "waitpid", SetLastError = true)]
    private static partial int WaitPid(int pid, out int status, int options);
    [LibraryImport("libc", EntryPoint = "prctl", SetLastError = true)]
    private static partial int Prctl(int option, nuint arg2, nuint arg3, nuint arg4, nuint arg5);
}
