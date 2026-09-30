using System;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace SIL.Motif.Host.PanGloss;

public sealed class WorkerMutexOwner : IDisposable
{
    private readonly Mutex? _mutex;
    private readonly UnixFileLock? _unixLock;
    private readonly WindowsFileLock? _windowsFileLock;
    private readonly BlockingCollection<Command> _commands = new BlockingCollection<Command>();
    private readonly Thread _thread;
    private bool _disposed;
    private bool _ownsMutex;

    public WorkerMutexOwner(string name) : this(name, machineWide: false)
    {
    }

    internal WorkerMutexOwner(string name, bool machineWide)
    {
        if (OperatingSystem.IsWindows()) _mutex = new Mutex(false, name);
        else _unixLock = new UnixFileLock(name, machineWide);
        _thread = new Thread(Run) { IsBackground = true, Name = "Motif worker mutex owner" };
        _thread.Start();
    }

    internal WorkerMutexOwner(string name, bool machineWide, WorkerLockAccess access)
    {
        if (OperatingSystem.IsWindows()) _windowsFileLock = new WindowsFileLock(name, machineWide, access);
        else _unixLock = new UnixFileLock(name, machineWide, access);
        _thread = new Thread(Run) { IsBackground = true, Name = "Motif worker mutex owner" };
        _thread.Start();
    }

    public bool TryAcquire()
    {
        return Invoke(() =>
        {
            if (_ownsMutex)
                return true;
            if (_unixLock is not null)
            {
                _ownsMutex = _unixLock.TryAcquire();
                return _ownsMutex;
            }
            if (_windowsFileLock is not null)
            {
                _ownsMutex = _windowsFileLock.TryAcquire();
                return _ownsMutex;
            }
            try
            {
                _ownsMutex = _mutex!.WaitOne(TimeSpan.Zero);
            }
            catch (AbandonedMutexException)
            {
                _ownsMutex = true;
            }
            return _ownsMutex;
        });
    }

    public void Release()
    {
        Invoke(() =>
        {
            if (!_ownsMutex)
                return true;
            if (_unixLock is not null) _unixLock.Release();
            else if (_windowsFileLock is not null) _windowsFileLock.Release();
            else _mutex!.ReleaseMutex();
            _ownsMutex = false;
            return true;
        });
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        Exception? failure = null;
        try
        {
            Release();
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        _disposed = true;
        _commands.CompleteAdding();
        _thread.Join();
        _mutex?.Dispose();
        _unixLock?.Dispose();
        _windowsFileLock?.Dispose();
        if (failure is not null)
            throw new InvalidOperationException("The worker owner mutex could not be released.", failure);
    }

    private T Invoke<T>(Func<T> operation)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(WorkerMutexOwner));
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _commands.Add(new Command(() => completion.TrySetResult(operation()),
            exception => completion.TrySetException(exception)));
        return completion.Task.GetAwaiter().GetResult();
    }

    private void Run()
    {
        foreach (var command in _commands.GetConsumingEnumerable())
        {
            try { command.Run(); }
            catch (Exception exception) { command.Fail(exception); }
        }
    }

    private sealed class Command
    {
        private readonly Action _run;
        private readonly Action<Exception> _fail;

        public Command(Action run, Action<Exception> fail)
        {
            _run = run;
            _fail = fail;
        }

        public void Run() => _run();

        public void Fail(Exception exception)
        {
            _fail(exception);
        }
    }
}

internal enum WorkerLockAccess
{
    Shared,
    Exclusive,
}

internal sealed class WindowsFileLock : IDisposable
{
    private readonly string _path;
    private readonly WorkerLockAccess _access;
    private FileStream? _stream;
    private bool _disposed;

    internal WindowsFileLock(string name, bool machineWide, WorkerLockAccess access)
    {
        var identity = machineWide ? "machine:" + name :
            "user:" + Environment.UserName + ":" + name;
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
        _path = Path.Combine(Path.GetTempPath(), "motif-lock-" + digest);
        _access = access;
    }

    internal bool TryAcquire()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_stream is not null) return true;

        try
        {
            _stream = new FileStream(_path, FileMode.OpenOrCreate,
                _access == WorkerLockAccess.Shared ? FileAccess.Read : FileAccess.ReadWrite,
                _access == WorkerLockAccess.Shared ? FileShare.Read : FileShare.None);
            return true;
        }
        catch (IOException exception) when (IsSharingViolation(exception))
        {
            return false;
        }
    }

    internal void Release()
    {
        _stream?.Dispose();
        _stream = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        Release();
        _disposed = true;
    }

    private static bool IsSharingViolation(IOException exception) =>
        (exception.HResult & 0xffff) is 32 or 33;
}
