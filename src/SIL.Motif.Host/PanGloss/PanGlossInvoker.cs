using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Host.Parser;

namespace SIL.Motif.Host.PanGloss;

/// <summary>
/// Runs the <c>pangloss</c> executable: one queue slot, one containment job, both streams drained, an applicable
/// wall-clock deadline, and an outcome for whatever happened.
/// </summary>
/// <remarks>
/// <para>
/// Six launchers used to own copies of this sequence, and the containment they were each meant to call was
/// wired at none of them. Here a caller cannot obtain a process at all, only an outcome, so admission and
/// containment cannot be skipped and a failure cannot escape as an exception.
/// </para>
/// <para>
/// The default cap is the parser's own ratified execution limit rather than a number Motif chose. A batch
/// without a per-word time limit has no wall-clock deadline and remains cancellable. The per-word limit is a
/// <see cref="PanGlossRequest.Batch"/> argument and bounds a word, not the process.
/// </para>
/// </remarks>
public sealed class PanGlossInvoker : IPanGlossInvoker, IDisposable
{
    /// <summary>The parser's ratified execution limit for invocations that need a default deadline.</summary>
    public static readonly TimeSpan DefaultWallClockCap = TimeSpan.FromMinutes(10);

    private readonly string? _executable;
    private readonly MachinePanGlossQueue _queue;
    private readonly SemaphoreSlim _surfaceGate = new(1, 1);
    private readonly Dictionary<string, PanGlossSurfaceCheck> _surfaceChecks = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Locates the executable and competes for the machine's real parser slots.</summary>
    public PanGlossInvoker() : this(PanGlossExecutable.TryLocate(), new MachinePanGlossQueue())
    {
    }

    /// <summary>Uses an explicitly selected parser path, or reports that the parser is unavailable.</summary>
    public PanGlossInvoker(string? executablePath) : this(executablePath, new MachinePanGlossQueue())
    {
    }

    /// <summary>An explicit executable (or none) and an explicit queue, so a test can isolate both.</summary>
    internal PanGlossInvoker(string? executablePath, MachinePanGlossQueue queue)
    {
        _executable = executablePath;
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
    }

    /// <inheritdoc />
    public bool ExecutableMissing => _executable is null;

    /// <inheritdoc />
    public async Task<PanGlossOutcome> RunAsync(
        PanGlossRequest request, string label, CancellationToken cancellationToken, TimeSpan? wallClockCap = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(label)) throw new ArgumentException("Required.", nameof(label));
        var cap = ResolveWallClockCap(request, wallClockCap);
        if (cap != Timeout.InfiniteTimeSpan && cap <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(wallClockCap), "A cap must be positive or infinite.");
        request.Validate();

        if (_executable is null)
            return new PanGlossOutcome.Unavailable(MissingExecutableMessage) { ExecutableMissing = true };

        try
        {
            return await _queue.RunAsync(label,
                async (containment, token) =>
                {
                    try
                    {
                        if (request is PanGlossRequest.Batch or PanGlossRequest.Stats or PanGlossRequest.GrammarHealth)
                        {
                            var surface = await VerifySurfaceAsync(_executable, containment, token)
                                .ConfigureAwait(false);
                            if (!surface.IsValid)
                                return new PanGlossOutcome.Unavailable(surface.Message) { Containment = containment.Report };
                        }
                        var outcome = await LaunchAsync(_executable, request, containment, cap, token).ConfigureAwait(false);
                        return outcome with { Containment = containment.Report };
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        return new PanGlossOutcome.Cancelled() { Containment = containment.Report };
                    }
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return new PanGlossOutcome.Cancelled();
        }
    }

    internal static TimeSpan ResolveWallClockCap(PanGlossRequest request, TimeSpan? wallClockCap) =>
        wallClockCap ?? (request is PanGlossRequest.Batch { PerWordLimit: null }
            ? Timeout.InfiniteTimeSpan
            : DefaultWallClockCap);

    /// <summary>Releases the queue; a run still in flight completes or cancels on its own terms.</summary>
    public void Dispose()
    {
        _queue.Dispose();
    }

    private async Task<PanGlossSurfaceCheck> VerifySurfaceAsync(
        string executable, PanGlossContainmentJob containment, CancellationToken cancellationToken)
    {
        var path = Path.GetFullPath(executable);
        await _surfaceGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_surfaceChecks.TryGetValue(path, out var cached)) return cached;
            var check = await PanGlossSurface.CheckAsync(path, containment, cancellationToken).ConfigureAwait(false);
            _surfaceChecks[path] = check;
            return check;
        }
        finally
        {
            _surfaceGate.Release();
        }
    }

    /// <summary>
    /// The launch itself, after admission, under the supplied platform-specific containment job.
    /// </summary>
    internal static async Task<PanGlossOutcome> LaunchAsync(
        string executable, PanGlossRequest request, PanGlossContainmentJob containment, TimeSpan cap,
        CancellationToken cancellationToken, Action<PanGlossChildProcess>? onStarted = null)
    {
        var scratch = Path.Combine(Path.GetTempPath(), "SIL.Motif.PanGloss", Guid.NewGuid().ToString("N"));
        var retained = request is PanGlossRequest.Batch { ArtifactDirectory: not null };
        var created = false;
        var published = false;
        string? sourceDigest = null;
        string? executableDigest = null;
        string? wordsDigest = null;
        if (request is PanGlossRequest.Batch { ArtifactDirectory: { } artifactDirectory })
            scratch = Path.GetFullPath(artifactDirectory);
        try
        {
            try
            {
                if (retained && Directory.Exists(scratch))
                    return new PanGlossOutcome.Unavailable($"The artifact directory already exists: '{scratch}'.");
                Directory.CreateDirectory(scratch);
                created = true;
                if (retained && request is PanGlossRequest.Batch batch)
                {
                    var stagedSource = Path.Combine(scratch, "source.fwdata");
                    File.Copy(batch.ProjectFilePath, stagedSource, overwrite: false);
                    request = batch with { ProjectFilePath = stagedSource };
                    sourceDigest = BatchInvocationEvidence.DigestFile(stagedSource);
                    executableDigest = BatchInvocationEvidence.DigestFile(executable);
                }
                await request.PrepareAsync(scratch, cancellationToken).ConfigureAwait(false);
                if (retained)
                    wordsDigest = BatchInvocationEvidence.DigestFile(Path.Combine(scratch, "words.txt"));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Staging failed before any process existed: an environment fault, reported like an absent parser.
                return new PanGlossOutcome.Unavailable(
                    $"Could not stage the parser's inputs under '{scratch}': {exception.Message}");
            }


            var startInfo = new ProcessStartInfo(executable)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            PanGlossProcessEnvironment.Configure(startInfo);
            request.AddArguments(startInfo, scratch);

            PanGlossChildProcess? process;
            try
            {
                process = containment.Start(startInfo);
            }
            catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
            {
                return new PanGlossOutcome.Unavailable($"Could not start '{executable}': {exception.Message}");
            }
            if (process is null) return new PanGlossOutcome.Unavailable($"Could not start '{executable}'.");

            using (process)
            {
                onStarted?.Invoke(process);
                using var progressStop = new CancellationTokenSource();
                var progressTask = request is PanGlossRequest.Batch { OnProgress: not null } batch
                    ? MonitorBatchProgressAsync(batch, Path.Combine(scratch, "out.tsv"), progressStop.Token)
                    : Task.CompletedTask;
                try
                {
                var clock = Stopwatch.StartNew();

                var stdErrTask = process.ReadStandardErrorAsync();
                var stdOutTask = process.ReadStandardOutputAsync();

                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                deadline.CancelAfter(cap);
                try
                {
                    await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
                    await process.WaitForContainmentAsync(deadline.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    containment.Terminate(process);
                    using var stopped = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    try
                    {
                        await process.WaitForExitAsync(stopped.Token).ConfigureAwait(false);
                        await process.WaitForContainmentAsync(stopped.Token).ConfigureAwait(false);
                        await Task.WhenAll(stdOutTask, stdErrTask).WaitAsync(stopped.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return new PanGlossOutcome.Incomplete(
                            "Parser termination could not be confirmed after requesting cancellation.", string.Empty);
                    }
                    return cancellationToken.IsCancellationRequested
                        ? new PanGlossOutcome.Cancelled()
                        : new PanGlossOutcome.TimedOut(cap,
                            $"pangloss {request.Subcommand} did not finish within {cap.TotalMinutes:0.#} minutes and was stopped.");
                }

                var standardError = await stdErrTask.ConfigureAwait(false);
                var standardOutput = await stdOutTask.ConfigureAwait(false);
                if (process.ExitCode != 0 && !request.AcceptsNonzeroExit)
                {
                    return new PanGlossOutcome.Refused(process.ExitCode, StandardError: standardError,
                        StandardOutput: standardOutput,
                        Detail: $"pangloss {request.Subcommand} exited {process.ExitCode}:" + Environment.NewLine +
                            standardError.Trim());
                }
                var outcome = request.Finish(scratch, standardOutput, standardError, clock.Elapsed,
                    out var batchFileDigests);
                if (process.ExitCode != 0 && outcome is not PanGlossOutcome.Completed)
                    return new PanGlossOutcome.Refused(process.ExitCode, standardError, standardOutput,
                        $"pangloss {request.Subcommand} exited {process.ExitCode}; {outcome.Message}" +
                        (string.IsNullOrWhiteSpace(standardError) ? string.Empty : Environment.NewLine + standardError.Trim()));
                if (!retained || outcome is not PanGlossOutcome.Completed completed)
                    return outcome;
                var capturedBatch = (PanGlossRequest.Batch)request;
                if (sourceDigest != BatchInvocationEvidence.DigestFile(capturedBatch.ProjectFilePath) ||
                    executableDigest != BatchInvocationEvidence.DigestFile(executable) ||
                    wordsDigest != BatchInvocationEvidence.DigestFile(Path.Combine(scratch, "words.txt")))
                    return new PanGlossOutcome.Incomplete(
                        "The grammar source, word list, or parser executable changed during the invocation.", standardError);
                var wordsPath = Path.Combine(scratch, "words.txt");
                var tsvPath = Path.Combine(scratch, "out.tsv");
                if (!File.Exists(tsvPath))
                    return new PanGlossOutcome.Incomplete("The batch wrote no TSV evidence.", standardError);
                var stderrPath = Path.Combine(scratch, "stderr.txt");
                var tsvDigest = BatchInvocationEvidence.DigestFile(tsvPath);
                await File.WriteAllTextAsync(stderrPath, standardError, cancellationToken).ConfigureAwait(false);
                var analysesPath = capturedBatch.CollectAnalyses ? Path.Combine(scratch, "analyses.jsonl") : null;
                var analysesDigest = analysesPath is null ? null : BatchInvocationEvidence.DigestFile(analysesPath);
                if (batchFileDigests?.TsvSha256 != tsvDigest ||
                    batchFileDigests.AnalysesSha256 != analysesDigest)
                    return new PanGlossOutcome.Incomplete("The batch artifacts changed while being retained.", standardError);
                var evidence = new BatchInvocationEvidence(
                    Path.GetFileName(scratch), capturedBatch.ProjectFilePath, sourceDigest!, executableDigest!,
                    wordsPath, wordsDigest!,
                    tsvPath, tsvDigest,
                    stderrPath, BatchInvocationEvidence.DigestFile(stderrPath),
                    capturedBatch.PerWordLimit is { } timeLimit
                        ? (int)timeLimit.TotalMilliseconds : null,
                    capturedBatch.PerWordStepLimit ?? StepCap.Default,
                    1, capturedBatch.StatsCachePath is not null)
                {
                    AnalysesPath = analysesPath,
                    AnalysesSha256 = analysesDigest,
                };
                published = true;
                return completed with
                {
                    BatchEvidence = evidence,
                    ArtifactLease = new Assess.AssessmentArtifactLease(scratch)
                };
                }
                finally
                {
                    progressStop.Cancel();
                    await progressTask.ConfigureAwait(false);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new PanGlossOutcome.Incomplete($"Could not retain invocation evidence: {exception.Message}", string.Empty);
        }
        finally
        {
            // Best effort: a leaked scratch directory must not turn a completed run into a failure.
            try { if (created && !published) Directory.Delete(scratch, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static async Task MonitorBatchProgressAsync(PanGlossRequest.Batch batch, string path,
        CancellationToken cancellationToken)
    {
        var progressReader = new BatchProgressReader(batch.Words);
        TrialWordProgress? previous = null;
        void Publish(TrialWordProgress? current)
        {
            if (current is null || current == previous) return;
            try
            {
                batch.OnProgress!(current);
                previous = current;
            }
            // A progress-store failure must not turn a completed parser run into an Assessment failure.
            catch (Exception) { }
        }
        while (!cancellationToken.IsCancellationRequested)
        {
            Publish(progressReader.Read(path));
            try { await Task.Delay(100, cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
        Publish(progressReader.Read(path));
    }

    private static string MissingExecutableMessage => PanGlossExecutable.NotFoundMessage;
}
