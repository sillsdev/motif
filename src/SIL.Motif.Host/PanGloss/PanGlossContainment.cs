using System.Diagnostics;

namespace SIL.Motif.Host.PanGloss;

/// <summary>Describes the limits and process-tree controls applied to one PanGloss invocation.</summary>
public sealed record PanGlossContainmentReport(
    int? CpuRateBasisPoints,
    ulong MemoryLimitBytes,
    bool AggregateMemoryLimit,
    string Cpu,
    string Memory,
    string ProcessTree,
    IReadOnlyList<string> Limitations);

/// <summary>Owns the operating-system controls for one admitted PanGloss job.</summary>
public abstract class PanGlossContainmentJob : IDisposable
{
    protected PanGlossContainmentJob(PanGlossContainmentReport report) => Report = report;

    /// <summary>How this invocation is limited and what its process-tree controls can guarantee.</summary>
    public PanGlossContainmentReport Report { get; protected set; }

    /// <summary>Starts a process under this job's platform-specific containment controls.</summary>
    public abstract PanGlossChildProcess Start(ProcessStartInfo startInfo);

    /// <summary>Stops the process and the descendants covered by this job's controls.</summary>
    public abstract void Terminate(PanGlossChildProcess process);

    /// <inheritdoc />
    public abstract void Dispose();
}

/// <summary>One process started by a <see cref="PanGlossContainmentJob"/>.</summary>
public sealed class PanGlossChildProcess : IDisposable
{
    private readonly IPanGlossChildProcess _process;

    internal PanGlossChildProcess(IPanGlossChildProcess process) =>
        _process = process ?? throw new ArgumentNullException(nameof(process));

    /// <summary>The operating-system process identifier, which remains stable across an exec.</summary>
    public int Id => _process.Id;

    /// <summary>The process exit code, after it has exited.</summary>
    public int ExitCode => _process.ExitCode;

    /// <summary>Waits for the process to exit, without changing its containment group.</summary>
    public Task WaitForExitAsync(CancellationToken cancellationToken = default) =>
        _process.WaitForExitAsync(cancellationToken);

    /// <summary>Reads all text written to standard output after the process exits.</summary>
    public Task<string> ReadStandardOutputAsync() => _process.ReadStandardOutputAsync();

    /// <summary>Reads all text written to standard error after the process exits.</summary>
    public Task<string> ReadStandardErrorAsync() => _process.ReadStandardErrorAsync();

    internal void KillProcessTree() => _process.KillProcessTree();

    internal Task WaitForContainmentAsync(CancellationToken cancellationToken) =>
        _process is UnixPanGlossChildProcess unixProcess
            ? unixProcess.WaitForContainmentAsync(cancellationToken)
            : Task.CompletedTask;

    /// <inheritdoc />
    public void Dispose() => _process.Dispose();
}

internal interface IPanGlossChildProcess : IDisposable
{
    int Id { get; }
    int ExitCode { get; }
    Task WaitForExitAsync(CancellationToken cancellationToken);
    Task<string> ReadStandardOutputAsync();
    Task<string> ReadStandardErrorAsync();
    void KillProcessTree();
}

internal static class PanGlossContainment
{
    internal const ulong DefaultMemoryLimitBytes = 10UL * 1024 * 1024 * 1024;

    internal static PanGlossContainmentJob CreateJob(ulong memoryLimitBytes = DefaultMemoryLimitBytes)
    {
        if (OperatingSystem.IsWindows()) return new WindowsCpuJob();
        if (OperatingSystem.IsLinux()) return new LinuxPanGlossJob(memoryLimitBytes);
        if (OperatingSystem.IsMacOS()) return new MacPanGlossJob(memoryLimitBytes);
        throw new PlatformNotSupportedException("PanGloss containment supports Windows, Linux and macOS.");
    }
}
