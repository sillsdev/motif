using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
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
/// argument, which the runner prefers over its environment, pinned by
/// `ALaunchedRunnerDrainsTheQueueUnderItsOwnRootWithoutProcessEnvironment`. The exception is
/// <see cref="JobRunnerLaunchOptions.WorkerExecutable"/>, which chooses the executable to start rather than
/// travelling to it, pinned by `AConfiguredRunnerThatDoesNotExistIsNotStarted`.
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
    /// This process's standard handles are marked non-inheritable before the spawn, so a caller capturing
    /// this process's own stdio — a redirecting parent, a shell's command substitution — reaches end-of-file
    /// when this process exits rather than when the runner idles out, pinned by
    /// `ACapturingCallerGetsEndOfFileWithoutWaitingForTheRunnerItKicked`.
    /// </remarks>
    public void Start(string projectPath, Action<string>? reportWarning = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        var executable = Locate(Options.WorkerExecutable);
        if (executable is null) return;

        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                MakeOwnStandardHandlesNonInheritable();
            var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true };
            foreach (var argument in Arguments(Options))
                start.ArgumentList.Add(argument);
            Process.Start(start);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            // Reported, not thrown: the enqueue already succeeded, and the next enqueue will kick again.
            reportWarning?.Invoke("warning: could not start the background runner (" + exception.Message +
                "). Queued work will run once one is started.");
        }
    }

    private static IEnumerable<string> Arguments(JobRunnerLaunchOptions options)
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

        var fileName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
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
}
