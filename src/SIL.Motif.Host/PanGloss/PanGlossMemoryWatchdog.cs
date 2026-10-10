using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace SIL.Motif.Host.PanGloss;

/// <summary>
/// Measures the memory a parser process group holds, for an operating system whose kernel cannot cap it.
/// </summary>
internal interface IProcessGroupMemory
{
    /// <summary>The measure named in a refusal, such as "macOS physical-footprint".</summary>
    string Measure { get; }

    /// <summary>The live members of the group, or an empty list when none can be seen.</summary>
    int[] ListProcessIds(int processGroupId);

    /// <summary>The summed memory of the members, skipping any that exited since they were listed.</summary>
    ulong SumBytes(IEnumerable<int> processIds);
}

internal sealed class PanGlossMemoryWatchdog : IDisposable
{
    private static readonly TimeSpan SampleInterval = TimeSpan.FromMilliseconds(50);

    private readonly int _processGroupId;
    private readonly ulong _memoryLimitBytes;
    private readonly IProcessGroupMemory _memory;
    private readonly Action _terminate;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _sampling;
    private string? _failureReason;
    private bool _disposed;

    internal PanGlossMemoryWatchdog(int processGroupId, ulong memoryLimitBytes, IProcessGroupMemory memory,
        Action terminate)
    {
        _processGroupId = processGroupId;
        _memoryLimitBytes = memoryLimitBytes;
        _memory = memory;
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
                var processIds = _memory.ListProcessIds(_processGroupId);
                if (processIds.Length == 0)
                {
                    if (!ProcessGroupExists(_processGroupId)) return;
                    if (++emptySamples >= 3)
                        throw new InvalidOperationException("The parser process group could not be enumerated.");
                }
                else
                {
                    emptySamples = 0;
                    if (_memory.SumBytes(processIds) > _memoryLimitBytes)
                    {
                        _failureReason = $"The parser process group exceeded its {_memory.Measure} memory limit.";
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

    private static bool ProcessGroupExists(int processGroupId)
    {
        if (UnixNative.Kill(-processGroupId, 0) == 0) return true;
        var error = UnixNative.LastError;
        if (error == 1) return true;
        if (error == 3) return false;
        throw new IOException("Could not inspect the parser process group.", error);
    }
}

[SupportedOSPlatform("macos")]
internal sealed partial class MacProcessGroupMemory : IProcessGroupMemory
{
    private const int ProcessGroupOnly = 2;
    private const int RusageInfoV2 = 2;
    private const int PhysicalFootprintOffset = 72;
    private const int RusageInfoV2Size = 160;
    private const int InitialProcessCapacity = 64;
    private const int MaximumProcessCapacity = 1_048_576;

    internal static MacProcessGroupMemory Instance { get; } = new();

    public string Measure => "macOS physical-footprint";

    public int[] ListProcessIds(int processGroupId)
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

    public ulong SumBytes(IEnumerable<int> processIds)
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

    [LibraryImport("libproc.dylib", EntryPoint = "proc_listpids", SetLastError = true)]
    private static partial int ProcListPids(int type, uint typeInfo, IntPtr buffer, int bufferSize);

    [LibraryImport("libproc.dylib", EntryPoint = "proc_pid_rusage", SetLastError = true)]
    private static partial int ProcPidRusage(int processId, int flavor, IntPtr buffer);
}

/// <summary>
/// Counts what a process has written to memory, never what it has only reserved: PanGloss reserves a large
/// stack for every parser thread and touches little of it, so an address-space limit refuses many threads
/// (pinned by `SixteenThreadBatchStartsUnderTheDefaultMemoryCeiling`).
/// One instance watches one process group, because it remembers the group's members between full scans.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed class LinuxProcessGroupMemory : IProcessGroupMemory
{
    private const ulong BytesPerKibibyte = 1024;
    private const int SamplesPerScan = 10;
    private int[] _members = [];
    private int _samplesSinceScan;

    public string Measure => "Linux resident and swapped";

    // A full scan reads every process on the machine; between scans only known members are rechecked.
    public int[] ListProcessIds(int processGroupId)
    {
        if (++_samplesSinceScan < SamplesPerScan)
        {
            var alive = _members.Where(processId => ReadProcessGroup(processId) == processGroupId).ToArray();
            if (alive.Length > 0) return alive;
        }
        _samplesSinceScan = 0;
        _members = ScanProcessGroup(processGroupId);
        return _members;
    }

    private static int[] ScanProcessGroup(int processGroupId)
    {
        var processIds = new List<int>();
        foreach (var directory in Directory.EnumerateDirectories("/proc"))
        {
            if (!int.TryParse(Path.GetFileName(directory), NumberStyles.None, CultureInfo.InvariantCulture,
                    out var processId)) continue;
            if (ReadProcessGroup(processId) == processGroupId) processIds.Add(processId);
        }
        return [.. processIds];
    }

    public ulong SumBytes(IEnumerable<int> processIds)
    {
        ulong total = 0;
        foreach (var processId in processIds)
        {
            var bytes = ReadWrittenBytes(processId);
            total = ulong.MaxValue - total < bytes ? ulong.MaxValue : total + bytes;
        }
        return total;
    }

    // The stat fields after the parenthesised command are state, parent and process group.
    private static int? ReadProcessGroup(int processId)
    {
        string stat;
        try { stat = File.ReadAllText($"/proc/{processId}/stat"); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return null; }
        var fields = stat[(stat.LastIndexOf(')') + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return fields.Length > 2 && int.TryParse(fields[2], NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture, out var group) ? group : null;
    }

    // RssFile is left out: clean file pages, the executable among them, can be dropped and read back.
    private static ulong ReadWrittenBytes(int processId)
    {
        string[] lines;
        try { lines = File.ReadAllLines($"/proc/{processId}/status"); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return 0; }
        ulong kibibytes = 0;
        foreach (var line in lines)
        {
            if (!line.StartsWith("RssAnon:", StringComparison.Ordinal) &&
                !line.StartsWith("RssShmem:", StringComparison.Ordinal) &&
                !line.StartsWith("VmSwap:", StringComparison.Ordinal)) continue;
            var value = line[(line.IndexOf(':') + 1)..].Trim().Split(' ')[0];
            if (ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var amount))
                kibibytes += amount;
        }
        return kibibytes * BytesPerKibibyte;
    }
}
