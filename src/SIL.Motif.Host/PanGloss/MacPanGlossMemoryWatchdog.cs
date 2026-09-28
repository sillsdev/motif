using System.ComponentModel;
using System.Runtime.InteropServices;

namespace SIL.Motif.Host.PanGloss;

internal sealed partial class MacPanGlossMemoryWatchdog : IDisposable
{
    private const int ProcessGroupOnly = 2;
    private const int RusageInfoV2 = 2;
    private const int PhysicalFootprintOffset = 72;
    private const int RusageInfoV2Size = 160;
    private const int InitialProcessCapacity = 64;
    private const int MaximumProcessCapacity = 1_048_576;
    private static readonly TimeSpan SampleInterval = TimeSpan.FromMilliseconds(50);

    private readonly int _processGroupId;
    private readonly ulong _memoryLimitBytes;
    private readonly Action _terminate;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _sampling;
    private string? _failureReason;
    private bool _disposed;

    internal MacPanGlossMemoryWatchdog(int processGroupId, ulong memoryLimitBytes, Action terminate)
    {
        _processGroupId = processGroupId;
        _memoryLimitBytes = memoryLimitBytes;
        _terminate = terminate;
        _sampling = Task.Run(SampleAsync);
    }

    internal string? FailureReason => Volatile.Read(ref _failureReason);

    internal Task WaitForCompletionAsync(CancellationToken cancellationToken) =>
        _sampling.WaitAsync(cancellationToken);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _stop.Cancel();
        try { _sampling.GetAwaiter().GetResult(); }
        catch (OperationCanceledException) { }
        _stop.Dispose();
    }

    private async Task SampleAsync()
    {
        var emptySamples = 0;
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var processIds = ListProcessIds(_processGroupId);
                if (processIds.Length == 0)
                {
                    if (!ProcessGroupExists(_processGroupId)) return;
                    if (++emptySamples >= 3)
                        throw new InvalidOperationException("The parser process group could not be enumerated.");
                }
                else
                {
                    emptySamples = 0;
                    var footprint = SumPhysicalFootprint(processIds);
                    if (footprint > _memoryLimitBytes)
                    {
                        _failureReason = "The parser process group exceeded its macOS physical-footprint memory limit.";
                        _terminate();
                        return;
                    }
                }

                await Task.Delay(SampleInterval, _stop.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (Exception exception)
        {
            _failureReason = "The parser process group was stopped because its memory footprint could not be " +
                $"sampled: {exception.Message}";
            try { _terminate(); }
            catch (IOException) { }
        }
    }

    private static int[] ListProcessIds(int processGroupId)
    {
        for (var capacity = InitialProcessCapacity; capacity <= MaximumProcessCapacity; capacity *= 2)
        {
            var size = checked(capacity * sizeof(int));
            var buffer = Marshal.AllocHGlobal(size);
            try
            {
                var bytes = ProcListPids(ProcessGroupOnly, unchecked((uint)processGroupId), buffer, size);
                if (bytes < 0 || bytes > size || bytes % sizeof(int) != 0)
                    throw new Win32Exception(Marshal.GetLastPInvokeError(),
                        "Could not enumerate the parser process group.");
                if (bytes == size) continue;

                var processIds = new int[bytes / sizeof(int)];
                for (var index = 0; index < processIds.Length; index++)
                    processIds[index] = Marshal.ReadInt32(buffer, index * sizeof(int));
                return processIds.Where(processId => processId > 0).ToArray();
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        throw new InvalidOperationException("The parser process group exceeded the process-count limit.");
    }

    private static ulong SumPhysicalFootprint(IEnumerable<int> processIds)
    {
        ulong total = 0;
        var usage = Marshal.AllocHGlobal(RusageInfoV2Size);
        try
        {
            foreach (var processId in processIds)
            {
                var result = ProcPidRusage(processId, RusageInfoV2, usage);
                if (result != 0)
                {
                    var error = Marshal.GetLastPInvokeError();
                    if (error == 3) continue;
                    throw new Win32Exception(error, "Could not read a parser process footprint.");
                }

                var footprint = unchecked((ulong)Marshal.ReadInt64(usage, PhysicalFootprintOffset));
                total = ulong.MaxValue - total < footprint ? ulong.MaxValue : total + footprint;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(usage);
        }

        return total;
    }

    private static bool ProcessGroupExists(int processGroupId)
    {
        if (UnixNative.Kill(-processGroupId, 0) == 0) return true;
        var error = UnixNative.LastError;
        if (error == 1) return true;
        if (error == 3) return false;
        throw new IOException("Could not inspect the parser process group.", error);
    }

    [LibraryImport("libproc.dylib", EntryPoint = "proc_listpids", SetLastError = true)]
    private static partial int ProcListPids(int type, uint typeInfo, IntPtr buffer, int bufferSize);

    [LibraryImport("libproc.dylib", EntryPoint = "proc_pid_rusage", SetLastError = true)]
    private static partial int ProcPidRusage(int processId, int flavor, IntPtr buffer);
}
