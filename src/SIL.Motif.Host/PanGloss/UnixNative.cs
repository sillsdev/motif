using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace SIL.Motif.Host.PanGloss;

internal static partial class UnixNative
{
    internal const int LockExclusive = 2;
    internal const int LockNonBlocking = 4;
    internal const int LockUnlock = 8;

    [LibraryImport("libc", EntryPoint = "geteuid")]
    internal static partial uint GetEffectiveUserId();

    [LibraryImport("libc", EntryPoint = "getpgid", SetLastError = true)]
    internal static partial int GetProcessGroupId(int processId);

    [LibraryImport("libc", EntryPoint = "kill", SetLastError = true)]
    internal static partial int Kill(int processId, int signal);

    [LibraryImport("libc", EntryPoint = "flock", SetLastError = true)]
    internal static partial int Flock(int fileDescriptor, int operation);

    [LibraryImport("libc", EntryPoint = "close", SetLastError = true)]
    internal static partial int Close(int fileDescriptor);

    [LibraryImport("libc", EntryPoint = "fchmod", SetLastError = true)]
    internal static partial int Fchmod(int fileDescriptor, uint mode);

    [LibraryImport("libc", EntryPoint = "open", SetLastError = true)]
    internal static partial int Open(IntPtr path, int flags, uint mode);

    [LibraryImport("libc", EntryPoint = "waitpid", SetLastError = true)]
    internal static partial int WaitPid(int processId, out int status, int options);

    [LibraryImport("libc", EntryPoint = "posix_spawn_file_actions_init")]
    internal static partial int SpawnFileActionsInit(IntPtr actions);

    [LibraryImport("libc", EntryPoint = "posix_spawn_file_actions_destroy")]
    internal static partial int SpawnFileActionsDestroy(IntPtr actions);

    [LibraryImport("libc", EntryPoint = "posix_spawn_file_actions_adddup2")]
    internal static partial int SpawnFileActionsAddDup2(IntPtr actions, int source, int destination);

    [LibraryImport("libc", EntryPoint = "posix_spawn_file_actions_addclose")]
    internal static partial int SpawnFileActionsAddClose(IntPtr actions, int fileDescriptor);

    [LibraryImport("libc", EntryPoint = "posix_spawnattr_init")]
    internal static partial int SpawnAttributesInit(IntPtr attributes);

    [LibraryImport("libc", EntryPoint = "posix_spawnattr_destroy")]
    internal static partial int SpawnAttributesDestroy(IntPtr attributes);

    [LibraryImport("libc", EntryPoint = "posix_spawnattr_setflags")]
    internal static partial int SpawnAttributesSetFlags(IntPtr attributes, short flags);

    [LibraryImport("libc", EntryPoint = "posix_spawnattr_setpgroup")]
    internal static partial int SpawnAttributesSetProcessGroup(IntPtr attributes, int processGroupId);

    [LibraryImport("libc", EntryPoint = "posix_spawn")]
    internal static partial int Spawn(out int processId, IntPtr path, IntPtr actions, IntPtr attributes,
        IntPtr arguments, IntPtr environment);

    internal static IntPtr Utf8(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value + "\0");
        var pointer = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, pointer, bytes.Length);
        return pointer;
    }

    internal static IntPtr StringVector(IReadOnlyList<string> values, out IntPtr[] strings)
    {
        strings = new IntPtr[values.Count];
        var vector = Marshal.AllocHGlobal((values.Count + 1) * IntPtr.Size);
        try
        {
            for (var index = 0; index < values.Count; index++)
            {
                strings[index] = Utf8(values[index]);
                Marshal.WriteIntPtr(vector, index * IntPtr.Size, strings[index]);
            }
            Marshal.WriteIntPtr(vector, values.Count * IntPtr.Size, IntPtr.Zero);
            return vector;
        }
        catch
        {
            foreach (var pointer in strings)
                if (pointer != IntPtr.Zero) Marshal.FreeHGlobal(pointer);
            Marshal.FreeHGlobal(vector);
            throw;
        }
    }

    internal static void FreeVector(IntPtr vector, IEnumerable<IntPtr> strings)
    {
        foreach (var pointer in strings) Marshal.FreeHGlobal(pointer);
        Marshal.FreeHGlobal(vector);
    }

    internal static void CheckSpawn(int error, string operation)
    {
        if (error != 0) throw new Win32Exception(error, $"Unix process setup failed while {operation}.");
    }

    internal static int LastError => Marshal.GetLastPInvokeError();
}

internal sealed class UnixFileLock : IDisposable
{
    private const int AccessDenied = 13;
    private readonly int _fileDescriptor;
    private bool _held;
    private bool _disposed;

    internal static string GetLockPath(string name, bool machineWide)
    {
        var identity = machineWide ? "machine:" + name :
            "user:" + UnixNative.GetEffectiveUserId() + ":" + name;
        var digest = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(identity)))
            .ToLowerInvariant();
        return Path.Combine("/tmp", "motif-lock-" + digest);
    }

    internal UnixFileLock(string name, bool machineWide)
    {
        var lockPath = GetLockPath(name, machineWide);
        var path = UnixNative.Utf8(lockPath);
        try
        {
            var flags = 2 | Create | CloseOnExec | NoFollow;
            _fileDescriptor = OpenRetryingInterrupts(path, flags | CreateExclusive, machineWide ? 0x1b6U : 0x180U);
            if (_fileDescriptor >= 0)
            {
                if (UnixNative.Fchmod(_fileDescriptor, machineWide ? 0x1b6U : 0x180U) != 0)
                {
                    var error = Marshal.GetLastPInvokeError();
                    _ = UnixNative.Close(_fileDescriptor);
                    throw new IOException("Could not set the worker lock's permissions.", error);
                }
            }
            else if (UnixNative.LastError == 17)
            {
                _fileDescriptor = OpenRetryingInterrupts(path, flags, 0);
            }

            if (_fileDescriptor < 0)
            {
                var error = UnixNative.LastError;
                var errorName = error == AccessDenied ? " (EACCES)" : string.Empty;
                var recovery = error == AccessDenied
                    ? " Check the file's owner and read/write permissions; remove it only if it is stale."
                    : string.Empty;
                throw new IOException(
                    $"Could not open worker lock file '{lockPath}' (errno {error}{errorName}).{recovery}", error);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(path);
        }
    }

    internal bool TryAcquire()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_held) return true;
        if (UnixNative.Flock(_fileDescriptor, UnixNative.LockExclusive | UnixNative.LockNonBlocking) == 0)
        {
            _held = true;
            return true;
        }
        var error = UnixNative.LastError;
        if (error is 11 or 35) return false;
        throw new IOException("Could not acquire the worker lock.", error);
    }

    internal void Release()
    {
        if (!_held) return;
        if (UnixNative.Flock(_fileDescriptor, UnixNative.LockUnlock) != 0)
            throw new IOException("Could not release the worker lock.", UnixNative.LastError);
        _held = false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        Release();
        _disposed = true;
        _ = UnixNative.Close(_fileDescriptor);
    }

    private static int OpenRetryingInterrupts(IntPtr path, int flags, uint mode)
    {
        const int interrupted = 4;
        int descriptor;
        do descriptor = UnixNative.Open(path, flags, mode);
        while (descriptor < 0 && UnixNative.LastError == interrupted);
        return descriptor;
    }

    private static int Create => OperatingSystem.IsLinux() ? 0x40 : 0x200;
    private static int CreateExclusive => OperatingSystem.IsLinux() ? 0x80 : 0x800;
    private static int CloseOnExec => OperatingSystem.IsLinux() ? 0x80000 : 0x1000000;
    private static int NoFollow => OperatingSystem.IsLinux() ? 0x20000 : 0x100;
}
