using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;
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
    private readonly SemaphoreSlim _surfaceGate = new(1, 1);
    private readonly Dictionary<string, PanGlossSurfaceCheck> _surfaceChecks = new(StringComparer.OrdinalIgnoreCase);

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
                async (cpuJob, token) =>
                {
                    if (request is PanGlossRequest.Batch or PanGlossRequest.Stats or PanGlossRequest.GrammarHealth)
                    {
                        var surface = await VerifySurfaceAsync(_executable, cpuJob.AssignProcess, token)
                            .ConfigureAwait(false);
                        if (!surface.IsValid) return new PanGlossOutcome.Unavailable(surface.Message);
                    }
                    return await LaunchAsync(_executable, request, cpuJob.AssignProcess, cap, token)
                        .ConfigureAwait(false);
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return new PanGlossOutcome.Cancelled();
        }
    }

    /// <summary>Releases the queue; a run still in flight completes or cancels on its own terms.</summary>
    public void Dispose()
    {
        _queue.Dispose();
    }

    private async Task<PanGlossSurfaceCheck> VerifySurfaceAsync(
        string executable, Action<Process> contain, CancellationToken cancellationToken)
    {
        var path = Path.GetFullPath(executable);
        await _surfaceGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_surfaceChecks.TryGetValue(path, out var cached)) return cached;
            var check = await PanGlossSurface.CheckAsync(path, contain, cancellationToken).ConfigureAwait(false);
            _surfaceChecks[path] = check;
            return check;
        }
        finally
        {
            _surfaceGate.Release();
        }
    }

    /// <summary>
    /// The launch itself, after admission: <paramref name="contain"/> receives the process the instant it
    /// starts, before either side knows whether the run will succeed.
    /// </summary>
    internal static async Task<PanGlossOutcome> LaunchAsync(
        string executable, PanGlossRequest request, Action<Process> contain, TimeSpan cap,
        CancellationToken cancellationToken)
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
                    using var stopped = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    try
                    {
                        await process.WaitForExitAsync(stopped.Token).ConfigureAwait(false);
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
                if (process.ExitCode != 0)
                {
                    return new PanGlossOutcome.Refused(process.ExitCode, StandardError: standardError,
                        StandardOutput: standardOutput,
                        Detail: $"pangloss {request.Subcommand} exited {process.ExitCode}:" + Environment.NewLine +
                            standardError.Trim());
                }
                var outcome = request.Finish(scratch, standardOutput, standardError, clock.Elapsed);
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
                var tsvBytes = await File.ReadAllBytesAsync(tsvPath, cancellationToken).ConfigureAwait(false);
                var tsvDigest = "sha256:" + Convert.ToHexString(
                    System.Security.Cryptography.SHA256.HashData(tsvBytes)).ToLowerInvariant();
                var tsvText = Encoding.UTF8.GetString(tsvBytes).TrimStart('\uFEFF');
                await File.WriteAllTextAsync(stderrPath, standardError, cancellationToken).ConfigureAwait(false);
                var analysesPath = capturedBatch.CollectAnalyses ? Path.Combine(scratch, "analyses.jsonl") : null;
                var analysesBytes = analysesPath is null ? null
                    : await File.ReadAllBytesAsync(analysesPath, cancellationToken).ConfigureAwait(false);
                var evidence = new BatchInvocationEvidence(
                    Path.GetFileName(scratch), capturedBatch.ProjectFilePath, sourceDigest!, executableDigest!,
                    wordsPath, wordsDigest!,
                    tsvPath, tsvDigest,
                    stderrPath, BatchInvocationEvidence.DigestFile(stderrPath),
                    (int)capturedBatch.PerWordLimit.TotalMilliseconds, capturedBatch.PerWordStepLimit,
                    1, capturedBatch.StatsCachePath is not null)
                {
                    AnalysesPath = analysesPath,
                    AnalysesSha256 = analysesBytes is null ? null : "sha256:" + Convert.ToHexString(
                        System.Security.Cryptography.SHA256.HashData(analysesBytes)).ToLowerInvariant(),
                };
                var morphology = analysesBytes is null ? null : Encoding.UTF8.GetString(analysesBytes).TrimStart('\uFEFF');
                if (morphology != completed.MorphologyOutput)
                    return new PanGlossOutcome.Incomplete("Morphology evidence changed while being retained.", standardError);
                published = true;
                return completed with
                {
                    Output = tsvText, BatchEvidence = evidence, MorphologyOutput = morphology,
                    ArtifactLease = new Assess.AssessmentArtifactLease(scratch)
                };
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

    private static string MissingExecutableMessage =>
        "Could not find the pangloss executable. Build it with `cargo build --release -p pg-cli` in the " +
        $"PanGloss checkout, or set {PanGlossExecutable.PathVariable} to its path.";
}
