using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;
using SIL.Motif.Host.Parser;

namespace SIL.Motif.Host.PanGloss;

/// <summary>
/// Runs the <c>pangloss</c> executable: one queue slot, one job object, both streams drained, one wall-clock
/// cap, and an outcome for whatever happened.
/// </summary>
/// <remarks>
/// <para>
/// Six launchers used to own copies of this sequence, and the containment they were each meant to call was
/// wired at none of them. Here a caller cannot obtain a process at all, only an outcome, so admission and
/// containment cannot be skipped and a failure cannot escape as an exception.
/// </para>
/// <para>
/// The default cap is the parser's own ratified execution limit rather than a number Motif chose. The
/// per-word limit is a <see cref="PanGlossRequest.Batch"/> argument and bounds a word, not the process.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class PanGlossInvoker : IPanGlossInvoker, IDisposable
{
    /// <summary>The parser's ratified execution limit, applied to every invocation that names no cap.</summary>
    public static readonly TimeSpan DefaultWallClockCap = TimeSpan.FromMinutes(10);

    private readonly string? _executable;
    private readonly MachinePanGlossQueue _queue;

    /// <summary>Locates the executable and competes for the machine's real parser slots.</summary>
    public PanGlossInvoker() : this(PanGlossExecutable.TryLocate(), new MachinePanGlossQueue())
    {
    }

    /// <summary>An explicit executable (or none) and an explicit queue, so a test can isolate both.</summary>
    internal PanGlossInvoker(string? executablePath, MachinePanGlossQueue queue)
    {
        _executable = executablePath;
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
    }

    /// <inheritdoc />
    public async Task<PanGlossOutcome> RunAsync(
        PanGlossRequest request, string label, CancellationToken cancellationToken, TimeSpan? wallClockCap = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(label)) throw new ArgumentException("Required.", nameof(label));
        var cap = wallClockCap ?? DefaultWallClockCap;
        if (cap <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(wallClockCap), "A cap must be positive.");
        request.Validate();

        if (_executable is null) return new PanGlossOutcome.Unavailable(MissingExecutableMessage);

        try
        {
            return await _queue.RunAsync(label,
                (cpuJob, token) => LaunchAsync(_executable, request, cpuJob.AssignProcess, cap, token),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return new PanGlossOutcome.Cancelled();
        }
    }

    public void Dispose() => _queue.Dispose();

    /// <summary>
    /// The launch itself, after admission: <paramref name="contain"/> receives the process the instant it
    /// starts, before either side knows whether the run will succeed.
    /// </summary>
    internal static async Task<PanGlossOutcome> LaunchAsync(
        string executable, PanGlossRequest request, Action<Process> contain, TimeSpan cap,
        CancellationToken cancellationToken)
    {
        var scratch = Path.Combine(Path.GetTempPath(), "SIL.Motif.PanGloss", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            await request.PrepareAsync(scratch, cancellationToken).ConfigureAwait(false);

            var startInfo = new ProcessStartInfo(executable)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            request.AddArguments(startInfo, scratch);

            Process? process;
            try
            {
                process = Process.Start(startInfo);
            }
            catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
            {
                return new PanGlossOutcome.Unavailable($"Could not start '{executable}': {exception.Message}");
            }
            if (process is null) return new PanGlossOutcome.Unavailable($"Could not start '{executable}'.");

            using (process)
            {
                contain(process);
                var clock = Stopwatch.StartNew();

                // Read both streams before waiting: a full pipe buffer deadlocks a process that is still writing.
                var stdErrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);
                var stdOutTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);

                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                deadline.CancelAfter(cap);
                try
                {
                    await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    try { process.Kill(entireProcessTree: true); }
                    catch (InvalidOperationException) { }
                    catch (Win32Exception) { }
                    return cancellationToken.IsCancellationRequested
                        ? new PanGlossOutcome.Cancelled()
                        : new PanGlossOutcome.TimedOut(cap,
                            $"pangloss {request.Subcommand} did not finish within {cap.TotalMinutes:0.#} minutes and was stopped.");
                }

                var standardError = await stdErrTask.ConfigureAwait(false);
                var standardOutput = await stdOutTask.ConfigureAwait(false);
                if (process.ExitCode != 0)
                {
                    return new PanGlossOutcome.Refused(process.ExitCode, standardError, standardOutput,
                        $"pangloss {request.Subcommand} exited {process.ExitCode}:" + Environment.NewLine + standardError.Trim());
                }
                return request.Finish(scratch, standardOutput, standardError, clock.Elapsed);
            }
        }
        finally
        {
            // Best effort: a leaked scratch directory must not turn a completed run into a failure.
            try { Directory.Delete(scratch, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static string MissingExecutableMessage =>
        "Could not find the pangloss executable. Build it with `cargo build --release -p pg-cli` in the " +
        $"PanGloss checkout, or set {PanGlossExecutable.PathVariable} to its path.";
}
