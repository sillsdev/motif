using System.Diagnostics;
using System.Text.Json;

namespace SIL.Motif.Host.Assistants;

/// <summary>Serializes per-owner tunnel identities and recovers tunnels whose owning app has exited.</summary>
public sealed class TunnelProcessRegistry(string directory)
{
    private readonly string _directory = Path.GetFullPath(directory);

    /// <summary>The per-user location used by the desktop app and its supervisor.</summary>
    public static string DefaultDirectory => Path.Combine(UserDataHome.Path, "SIL", "Motif", "tunnel-processes");

    /// <summary>Stops only recorded tunnels whose owner is gone and whose own identity still matches.</summary>
    public void Recover()
    {
        using var guard = Acquire();
        foreach (var path in Directory.EnumerateFiles(_directory, "tunnel-*.json"))
        {
            TunnelProcessRecord? record;
            try { record = JsonSerializer.Deserialize<TunnelProcessRecord>(File.ReadAllText(path)); }
            catch (JsonException) { continue; }
            if (record is null || IsOwnerAlive(record.OwnerPid, record.OwnerStart)) continue;
            if (!OperatingSystem.IsWindows() && record.Pgid > 1)
            {
                if (TunnelUnixGroup.Stop(record.Pgid, record.LeaderStart, bundledDirectory: record.BundledDirectory)) File.Delete(path);
                continue;
            }
            try
            {
                using var tunnel = Process.GetProcessById(record.TunnelPid);
                if (!tunnel.HasExited && TunnelProcessIdentity.Start(tunnel) == record.TunnelStart &&
                    string.Equals(tunnel.MainModule?.FileName, record.Executable, StringComparison.Ordinal))
                {
                    tunnel.Kill(entireProcessTree: true);
                    tunnel.WaitForExit();
                }
            }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
            File.Delete(path);
        }
    }

    /// <summary>Records the actual child executable and start time under the owning app's identity.</summary>
    public void Record(int ownerPid, long ownerStart, Process tunnel, int pgid = 0, long leaderStart = 0, string bundledDirectory = "")
    {
        using var guard = Acquire();
        var executable = tunnel.MainModule?.FileName ?? throw new InvalidOperationException();
        var record = new TunnelProcessRecord(ownerPid, ownerStart, tunnel.Id, TunnelProcessIdentity.Start(tunnel), executable, pgid, leaderStart, bundledDirectory);
        Write(record);
    }

    /// <summary>Persists group ownership before the tunnel child is started.</summary>
    public void RecordLeader(int ownerPid, long ownerStart, int pgid, long leaderStart, string bundledDirectory = "")
    {
        using var guard = Acquire();
        Write(new TunnelProcessRecord(ownerPid, ownerStart, 0, 0, string.Empty, pgid, leaderStart, bundledDirectory));
    }

    private void Write(TunnelProcessRecord record)
    {
        var path = RecordPath(record.OwnerPid, record.OwnerStart);
        var temporary = path + "." + Guid.NewGuid().ToString("N");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                JsonSerializer.Serialize(stream, record);
            File.Move(temporary, path, true);
        }
        finally { File.Delete(temporary); }
    }

    /// <summary>Removes only the record still belonging to this exact tunnel.</summary>
    public void Forget(int ownerPid, long ownerStart, int tunnelPid, long tunnelStart)
    {
        using var guard = Acquire();
        var path = RecordPath(ownerPid, ownerStart);
        if (!File.Exists(path)) return;
        var record = JsonSerializer.Deserialize<TunnelProcessRecord>(File.ReadAllText(path));
        if (record?.TunnelPid == tunnelPid && record.TunnelStart == tunnelStart &&
            (record.Pgid == 0 || TunnelUnixGroup.CanForget(record.Pgid, record.LeaderStart, record.BundledDirectory))) File.Delete(path);
    }

    /// <summary>Removes the matching group record when no verified owned member remains.</summary>
    public void ForgetGroup(int ownerPid, long ownerStart, int pgid, long leaderStart)
    {
        using var guard = Acquire();
        var path = RecordPath(ownerPid, ownerStart);
        if (!File.Exists(path)) return;
        var record = JsonSerializer.Deserialize<TunnelProcessRecord>(File.ReadAllText(path));
        if (record?.Pgid == pgid && record.LeaderStart == leaderStart && TunnelUnixGroup.CanForget(pgid, leaderStart, record.BundledDirectory))
            File.Delete(path);
    }

    /// <summary>Checks both PID and start time so a reused PID cannot stand in for the original owner.</summary>
    public static bool IsOwnerAlive(int pid, long start)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited && TunnelProcessIdentity.Start(process) == start;
        }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
        catch (IOException) { return true; }
        catch (UnauthorizedAccessException) { return true; }
        catch (System.ComponentModel.Win32Exception) { return true; }
    }

    private string RecordPath(int pid, long start) => Path.Combine(_directory, $"tunnel-{pid}-{start}.json");

    private FileStream Acquire()
    {
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(_directory);
        else Directory.CreateDirectory(_directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var path = Path.Combine(_directory, "tunnels.lock");
        while (true)
        {
            try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException error) when ((error.HResult & 0xffff) is 11 or 32 or 33) { Thread.Sleep(25); }
        }
    }
}

/// <summary>The owning app and tunnel identities persisted without credentials.</summary>
public sealed record TunnelProcessRecord(int OwnerPid, long OwnerStart, int TunnelPid, long TunnelStart, string Executable, int Pgid = 0, long LeaderStart = 0, string BundledDirectory = "");
