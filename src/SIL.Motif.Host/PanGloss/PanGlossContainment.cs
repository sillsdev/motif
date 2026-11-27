using System.Diagnostics;

namespace SIL.Motif.Host.PanGloss;

/// <summary>Describes the limits and process-tree controls applied to one PanGloss invocation.</summary>
public sealed record PanGlossContainmentReport(
    int? CpuRateBasisPoints,
    ulong? MemoryLimitBytes,
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

    internal static readonly TimeSpan TerminationBudget = TimeSpan.FromSeconds(5);

    internal async Task TerminateAndConfirmAsync(PanGlossChildProcess process, Task? outputDrained = null)
    {
        var scope = PanGlossCleanupScope.Current;
        using var deadline = new CancellationTokenSource(scope?.Limit(TerminationBudget) ?? TerminationBudget);
        try
        {
            Terminate(process);
            await process.WaitForExitAsync(deadline.Token).WaitAsync(deadline.Token).ConfigureAwait(false);
            await WaitForTerminatedDescendantsAsync(process.Id, deadline.Token)
                .WaitAsync(deadline.Token).ConfigureAwait(false);
            await process.StopContainmentMonitoringAsync().WaitAsync(deadline.Token).ConfigureAwait(false);
            if (outputDrained is not null) await outputDrained.WaitAsync(deadline.Token).ConfigureAwait(false);
        }
        catch
        {
            scope?.MarkUnconfirmed();
            throw;
        }
    }

    internal virtual Task WaitForTerminatedDescendantsAsync(int processId, CancellationToken cancellationToken) =>
        Task.CompletedTask;

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

    /// <summary>The diagnostic exit number, including 128 plus the signal for a Unix signal stop.</summary>
    public int ExitCode => _process.ExitCode;

    /// <summary>The Unix terminating signal; null for a normal exit or when the launcher cannot distinguish it.</summary>
    public int? TerminationSignal => _process.TerminationSignal;

    /// <summary>Waits for the process to exit, without changing its containment group.</summary>
    public Task WaitForExitAsync(CancellationToken cancellationToken = default) =>
        _process.WaitForExitAsync(cancellationToken);

    /// <summary>Reads all text written to standard output after the process exits.</summary>
    public Task<string> ReadStandardOutputAsync() => _process.ReadStandardOutputAsync();

    /// <summary>Reads all text written to standard error after the process exits.</summary>
    public Task<string> ReadStandardErrorAsync() => _process.ReadStandardErrorAsync();

    /// <summary>The pipe a resident parser reads requests from; null unless started with redirected standard input.</summary>
    public Stream? InteractiveInput => _process.InteractiveInput;

    /// <summary>The pipe a resident parser writes replies to; null unless started with redirected standard input.</summary>
    public Stream? InteractiveOutput => _process.InteractiveOutput;

    internal void KillProcessTree() => _process.KillProcessTree();

    /// <summary>The memory stop that ended this process, once it has exited; null when none did.</summary>
    internal PanGlossMemoryKill? MemoryKill => _process.MemoryKill;

    internal Task WaitForContainmentAsync(CancellationToken cancellationToken) =>
        _process is UnixPanGlossChildProcess unixProcess
            ? unixProcess.WaitForContainmentAsync(cancellationToken)
            : Task.CompletedTask;

    internal Task StopContainmentMonitoringAsync() => _process is UnixPanGlossChildProcess unixProcess
        ? unixProcess.StopContainmentMonitoringAsync()
        : Task.CompletedTask;

    /// <inheritdoc />
    public void Dispose() => _process.Dispose();
}

internal interface IPanGlossChildProcess : IDisposable
{
    int Id { get; }
    int ExitCode { get; }
    /// <summary>The signal from native wait status, or null when absent or unavailable.</summary>
    int? TerminationSignal => null;
    Stream? InteractiveInput => null;
    Stream? InteractiveOutput => null;
    Task WaitForExitAsync(CancellationToken cancellationToken);
    Task<string> ReadStandardOutputAsync();
    Task<string> ReadStandardErrorAsync();
    void KillProcessTree();

    /// <summary>What the memory ceiling did to this process, read after it exits; null when nothing.</summary>
    PanGlossMemoryKill? MemoryKill { get; }
}

internal static class PanGlossContainment
{
    /// <summary>A job held to the ceiling the environment asks for; throws when that setting is invalid.</summary>
    internal static PanGlossContainmentJob CreateJob() => CreateJob(PanGlossMemoryLimit.FromEnvironment());

    /// <summary>A job held to <paramref name="memoryLimitBytes"/>, or to no memory ceiling when null.</summary>
    internal static PanGlossContainmentJob CreateJob(ulong? memoryLimitBytes)
    {
        if (OperatingSystem.IsWindows()) return new WindowsCpuJob(memoryLimitBytes);
        if (OperatingSystem.IsLinux()) return new LinuxPanGlossJob(memoryLimitBytes);
        if (OperatingSystem.IsMacOS()) return new MacPanGlossJob(memoryLimitBytes);
        throw new PlatformNotSupportedException("PanGloss containment supports Windows, Linux and macOS.");
    }
}
