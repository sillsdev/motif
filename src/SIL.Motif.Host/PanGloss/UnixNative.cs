using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace SIL.Motif.Host.PanGloss;

internal static partial class UnixNative
{
    internal const int LockExclusive = 2;
    internal const int LockShared = 1;
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

    // open is variadic, and Apple arm64 passes a variadic mode on the stack, so this import never creates a file.
    [LibraryImport("libc", EntryPoint = "open", SetLastError = true)]
    internal static partial int Open(IntPtr path, int flags);

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
    private const int Interrupted = 4;
    private readonly string _lockPath;
    private readonly string _registryPath;
    private readonly bool _machineWide;
    private readonly WorkerLockAccess _access;
    private int _fileDescriptor = -1;
    private bool _held;
    private bool _disposed;

    internal static string GetLockPath(string name, bool machineWide) => WorkerLockPaths.GetLockPath(name, machineWide);

    internal UnixFileLock(string name, bool machineWide) : this(name, machineWide, WorkerLockAccess.Exclusive)
    {
    }

    internal UnixFileLock(string name, bool machineWide, WorkerLockAccess access)
    {
        if (OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        _machineWide = machineWide;
        _access = access;
        _lockPath = GetLockPath(name, machineWide);
        _registryPath = WorkerLockPaths.GetRegistryPath(machineWide);
    }

    internal bool TryAcquire()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_held) return true;

        var registryDescriptor = OpenFile(_registryPath, _machineWide, "worker lock registry");
        var lockDescriptor = -1;
        try
        {
            AcquireRegistry(registryDescriptor);
            lockDescriptor = OpenFile(_lockPath, _machineWide, "worker lock file");
            var access = _access == WorkerLockAccess.Shared ? UnixNative.LockShared : UnixNative.LockExclusive;
            if (UnixNative.Flock(lockDescriptor, access | UnixNative.LockNonBlocking) != 0)
            {
                var error = UnixNative.LastError;
                if (error is 11 or 35) return false;
                throw new IOException("Could not acquire the worker lock.", error);
            }

            _fileDescriptor = lockDescriptor;
            lockDescriptor = -1;
            _held = true;
            return true;
        }
        finally
        {
            if (lockDescriptor >= 0) _ = UnixNative.Close(lockDescriptor);
            _ = UnixNative.Close(registryDescriptor);
        }
    }

    internal void Release()
    {
        if (!_held) return;

        Exception? failure = null;
        var registryDescriptor = -1;
        try
        {
            registryDescriptor = OpenFile(_registryPath, _machineWide, "worker lock registry");
            AcquireRegistry(registryDescriptor);
            if (_access == WorkerLockAccess.Shared)
            {
                if (UnixNative.Flock(_fileDescriptor, UnixNative.LockExclusive | UnixNative.LockNonBlocking) == 0)
                    File.Delete(_lockPath);
                else
                {
                    var error = UnixNative.LastError;
                    if (error is not (11 or 35))
                        throw new IOException("Could not release the shared worker lock.", error);
                }
            }
            else File.Delete(_lockPath);
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        finally
        {
            if (UnixNative.Flock(_fileDescriptor, UnixNative.LockUnlock) != 0 && failure is null)
                failure = new IOException("Could not release the worker lock.", UnixNative.LastError);
            _held = false;
            _ = UnixNative.Close(_fileDescriptor);
            _fileDescriptor = -1;
            if (registryDescriptor >= 0) _ = UnixNative.Close(registryDescriptor);
        }

        if (failure is not null) throw failure;
    }

    public void Dispose()
    {
        if (_disposed) return;
        Release();
        _disposed = true;
    }

    // .NET's own open passes the mode correctly on every ABI; the umask is overridden once the file exists.
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    private static void CreateIfMissing(string lockPath, UnixFileMode mode)
    {
        FileStream stream;
        try
        {
            stream = new FileStream(lockPath, new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.ReadWrite,
                Share = FileShare.ReadWrite | FileShare.Delete,
                UnixCreateMode = mode,
            });
        }
        catch (IOException) when (Path.Exists(lockPath) || File.ResolveLinkTarget(lockPath, false) is not null)
        {
            return;
        }
        using (stream)
        {
            File.SetUnixFileMode(stream.SafeFileHandle, mode);
        }
    }

    private static int OpenFile(string path, bool machineWide, string description)
    {
        if (OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var mode = machineWide ? (UnixFileMode)0x1b6 : (UnixFileMode)0x180;
        CreateIfMissing(path, mode);
        var nativePath = UnixNative.Utf8(path);
        try
        {
            var descriptor = OpenRetryingInterrupts(nativePath, 2 | CloseOnExec | NoFollow);
            if (descriptor >= 0) return descriptor;

            var error = UnixNative.LastError;
            var errorName = error == AccessDenied ? " (EACCES)" : string.Empty;
            var recovery = error == AccessDenied
                ? " Check the file's owner and read/write permissions; remove it only if it is stale."
                : string.Empty;
            throw new IOException(
                $"Could not open {description} '{path}' (errno {error}{errorName}).{recovery}", error);
        }
        finally
        {
            Marshal.FreeHGlobal(nativePath);
        }
    }

    // The registry keeps every opener out while a released lock file is unlinked.
    private static void AcquireRegistry(int fileDescriptor)
    {
        while (UnixNative.Flock(fileDescriptor, UnixNative.LockExclusive) != 0)
        {
            var error = UnixNative.LastError;
            if (error == Interrupted) continue;
            throw new IOException("Could not coordinate worker lock file access.", error);
        }
    }

    private static int OpenRetryingInterrupts(IntPtr path, int flags)
    {
        int descriptor;
        do descriptor = UnixNative.Open(path, flags);
        while (descriptor < 0 && UnixNative.LastError == Interrupted);
        return descriptor;
    }

    private static int CloseOnExec => OperatingSystem.IsLinux() ? 0x80000 : 0x1000000;
    private static int NoFollow => OperatingSystem.IsLinux() ? 0x20000 : 0x100;
}
