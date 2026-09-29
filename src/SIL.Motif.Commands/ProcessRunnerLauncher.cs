using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using WorkerRunnerOptions = SIL.Motif.Worker.RunnerOptions;

namespace SIL.Motif.Commands;

/// <summary>
/// Starts the job runner process after a verb enqueues durable work, so a pseudo-daemon that idled out is
/// woken again rather than left stopped until someone happens to start it by hand.
/// </summary>
/// <remarks>
/// No protocol exists between this and the runner it starts (ADR 0041 decision 5): the two rendezvous
/// through nothing but the named ownership mutex the runner already guards itself with, so spawning is
/// unconditional — an already-alive runner makes this a no-op, and keeps its own settings — and
/// best-effort: a runner that fails to start leaves the row queued for the next enqueue to wake instead of
/// failing the one that just queued it. Every runner setting in <see cref="Options"/> travels as a launch
/// argument, pinned by `EveryRunnerSettingTravelsAsALaunchArgumentAndTheExecutableDoesNot`. The runner prefers
/// an argument over its environment, pinned by `ExplicitArgumentsSelectTheRootParserNamespaceIdleAndLease`.
/// <see cref="JobRunnerLaunchOptions.WorkerExecutable"/> is not a runner setting: it chooses the executable
/// to start, and a configured one that does not exist starts nothing, pinned by
/// `AConfiguredRunnerThatDoesNotExistIsNotStarted`.
/// </remarks>
public sealed class ProcessRunnerLauncher : IJobRunnerLauncher
{
    /// <summary>Skips the spawn. <b>Test-only</b>: a test that starts and manages a runner itself sets this.</summary>
    public const string SuppressVariable = "MOTIF_SUPPRESS_KICK";

    /// <summary>Overrides where the runner executable is found. <b>Test-only.</b></summary>
    public const string ExecutableVariable = "MOTIF_WORKER_EXE";

    public ProcessRunnerLauncher(JobRunnerLaunchOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Root);
        Options = options;
    }

    public JobRunnerLaunchOptions Options { get; }

    /// <summary>
    /// The launcher a process's environment selects: <see cref="JobRunnerLaunchOptions.FromEnvironment"/>,
    /// or one that starts nothing when <see cref="SuppressVariable"/> is set.
    /// </summary>
    public static IJobRunnerLauncher FromEnvironment()
    {
        var options = JobRunnerLaunchOptions.FromEnvironment();
        return string.IsNullOrEmpty(Environment.GetEnvironmentVariable(SuppressVariable))
            ? new ProcessRunnerLauncher(options)
            : new NoRunnerLauncher(options);
    }

    /// <summary>The runner executable <see cref="ExecutableVariable"/> names, or <see langword="null"/>.</summary>
    public static string? ConfiguredExecutable()
    {
        var configured = Environment.GetEnvironmentVariable(ExecutableVariable);
        return string.IsNullOrWhiteSpace(configured) ? null : configured;
    }

    /// <summary>Spawns the runner with this launcher's settings.</summary>
    /// <remarks>
    /// The caller's output pipes are not kept open until the runner idles out. Windows marks this
    /// process's standard handles non-inheritable; Unix redirects and drains the runner's standard streams,
    /// pinned by
    /// `ACapturingCallerGetsEndOfFileWithoutWaitingForTheRunnerItKicked`.
    /// </remarks>
    public void Start(string projectPath, Action<string>? reportWarning = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        var executable = Locate(Options.WorkerExecutable);
        if (executable is null) return;

        try
        {
            var isWindows = OperatingSystem.IsWindows();
            if (isWindows)
                MakeOwnStandardHandlesNonInheritable();
            var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true };
            start.Environment.Remove("ICU_DATA");
            if (!isWindows)
            {
                start.RedirectStandardInput = true;
                start.RedirectStandardOutput = true;
                start.RedirectStandardError = true;
            }
            foreach (var argument in LaunchArguments(Options))
                start.ArgumentList.Add(argument);
            var process = Process.Start(start);
            if (process is null) return;
            if (isWindows)
                process.Dispose();
            else
                _ = DrainAndDisposeAsync(process);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or Win32Exception)
        {
            // Reported, not thrown: the enqueue already succeeded, and the next enqueue will kick again.
            reportWarning?.Invoke("warning: could not start the background runner (" + exception.Message +
                "). Queued work will run once one is started.");
        }
    }

    /// <summary>The arguments a launched runner is started with.</summary>
    /// <remarks>
    /// <see cref="WorkerRunnerOptions.Read"/> reads every runner setting in <paramref name="options"/> back from
    /// them, and <see cref="JobRunnerLaunchOptions.WorkerExecutable"/> is not among them, pinned by
    /// `EveryRunnerSettingTravelsAsALaunchArgumentAndTheExecutableDoesNot`.
    /// </remarks>
    public static IEnumerable<string> LaunchArguments(JobRunnerLaunchOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return LaunchArgumentsOf(options);
    }

    private static IEnumerable<string> LaunchArgumentsOf(JobRunnerLaunchOptions options)
    {
        yield return WorkerRunnerOptions.RootArgument;
        yield return options.Root;
        if (options.ParserPath is { } parserPath)
        {
            yield return WorkerRunnerOptions.ParserArgument;
            yield return parserPath;
        }
        else
        {
            yield return WorkerRunnerOptions.NoParserArgument;
        }
        if (options.OwnerNamespace is { } ownerNamespace)
        {
            yield return WorkerRunnerOptions.NamespaceArgument;
            yield return ownerNamespace;
        }
        if (options.IdleTimeout is { } idle)
        {
            yield return WorkerRunnerOptions.IdleArgument;
            yield return Milliseconds(idle);
        }
        if (options.Lease is { } lease)
        {
            yield return WorkerRunnerOptions.LeaseArgument;
            yield return Milliseconds(lease);
        }
    }

    private static string Milliseconds(TimeSpan value) =>
        ((long)Math.Ceiling(value.TotalMilliseconds)).ToString(CultureInfo.InvariantCulture);

    private const int StdInputHandle = -10;
    private const int StdOutputHandle = -11;
    private const int StdErrorHandle = -12;
    private const uint HandleFlagInherit = 1;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetHandleInformation(IntPtr hObject, uint dwMask, uint dwFlags);

    private static string? Locate(string? configured)
    {
        if (configured is not null)
            return File.Exists(configured) ? configured : null;

        var fileName = OperatingSystem.IsWindows()
            ? "SIL.Motif.Worker.exe"
            : "SIL.Motif.Worker";

        // Beside the host, built or published alike: one artifact, one version (ADR 0040 decision 5).
        var sibling = Path.Combine(AppContext.BaseDirectory, fileName);
        return File.Exists(sibling) ? sibling : null;
    }

    private static void MakeOwnStandardHandlesNonInheritable()
    {
        foreach (var which in new[] { StdInputHandle, StdOutputHandle, StdErrorHandle })
        {
            var handle = GetStdHandle(which);
            if (handle != IntPtr.Zero && handle != new IntPtr(-1))
                SetHandleInformation(handle, HandleFlagInherit, 0);
        }
    }

    private static async Task DrainAndDisposeAsync(Process process)
    {
        try
        {
            process.StandardInput.Close();
            await Task.WhenAll(process.StandardOutput.ReadToEndAsync(), process.StandardError.ReadToEndAsync())
                .ConfigureAwait(false);
        }
        catch (IOException)
        {
        }
        catch (InvalidOperationException)
        {
        }
        finally
        {
            process.Dispose();
        }
    }
}
