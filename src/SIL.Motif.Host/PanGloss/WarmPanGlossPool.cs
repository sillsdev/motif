using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Host.Parser;

namespace SIL.Motif.Host.PanGloss;

/// <summary>Where one project's resident parser is.</summary>
public enum WarmSessionPhase
{
    /// <summary>No process: never started, evicted after sitting idle, or stopped by a cancel.</summary>
    Idle,
    /// <summary>The process is starting or the project is loading.</summary>
    Loading,
    /// <summary>The project is loaded and a word costs a parse, not a load.</summary>
    Ready,
    /// <summary>The last load or session failed; see the code and message. Words use the cold path.</summary>
    Failed,
}

/// <summary>A resident parser's state, with the reason when it is <see cref="WarmSessionPhase.Failed"/>.</summary>
public sealed record WarmSessionState(WarmSessionPhase Phase, string? FailureCode = null, string? FailureMessage = null)
{
    public static readonly WarmSessionState Idle = new(WarmSessionPhase.Idle);
    public static readonly WarmSessionState Loading = new(WarmSessionPhase.Loading);
    public static readonly WarmSessionState Ready = new(WarmSessionPhase.Ready);
}

/// <summary>Raised, on a thread-pool thread, when a project's <see cref="WarmSessionState"/> changes.</summary>
public sealed class WarmSessionStateChangedEventArgs(string projectKey, WarmSessionState state) : EventArgs
{
    public string ProjectKey { get; } = projectKey;
    public WarmSessionState State { get; } = state;
}

/// <summary>What asking a resident parser for a word came to.</summary>
public abstract record WarmParseResult
{
    private WarmParseResult() { }

    /// <summary>
    /// The parser's reply: <paramref name="Json"/> is exactly the JSON a cold <c>parse --trace-format json --trace-details</c> prints
    /// and <paramref name="ExitCode"/> the cold command's exit code, to be read as if from the cold process.
    /// </summary>
    public sealed record Completed(string Json, int ExitCode, TimeSpan Elapsed) : WarmParseResult;

    /// <summary>The caller cancelled; the resident process was killed and the next call restarts it.</summary>
    public sealed record Cancelled : WarmParseResult;

    /// <summary>The resident parser could not answer; the caller runs the cold path for this word.</summary>
    public sealed record Fallback(string Code, string Message) : WarmParseResult;
}

/// <summary>Limits of the resident parsers, with the owner-decided defaults.</summary>
public sealed record WarmPanGlossOptions
{
    /// <summary>The memory ceiling for the life of a session (3 GB, ADR 0044).</summary>
    public ulong MemoryCeilingBytes { get; init; } = 3UL * 1024 * 1024 * 1024;
    /// <summary>A session unused this long is stopped.</summary>
    public TimeSpan IdleTimeout { get; init; } = TimeSpan.FromMinutes(10);
    /// <summary>A load still not ready after this long becomes Failed.</summary>
    public TimeSpan LoadTimeout { get; init; } = TimeSpan.FromSeconds(120);
    /// <summary>After a failure a word takes the cold path for this long before the session is tried again.</summary>
    public TimeSpan RetryCooldown { get; init; } = TimeSpan.FromSeconds(30);
}

/// <summary>
/// Resident <c>pangloss serve</c> processes, one per project, so a word costs a parse instead of a load (ADR 0044,
/// long-lived parser session). Each request takes machine-queue admission; the process lives in its own containment
/// job with the memory ceiling for its whole life. Requests to one session run one at a time, in order.
/// A cancel kills the process; any failure falls back to the cold path for that word. A session is keyed by project and
/// by the step cap it was started with, because <c>serve</c> applies its caps to every request.
/// </summary>
public sealed class WarmPanGlossPool : IDisposable
{
    /// <summary>The <c>serve</c> protocol version Motif speaks.</summary>
    public const int ServeProtocol = 1;

    private readonly string? _executable;
    private readonly MachinePanGlossQueue _queue;
    private readonly bool _ownsQueue;
    private readonly WarmPanGlossOptions _options;
    private readonly object _gate = new();
    private readonly Dictionary<string, Session> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, PanGlossSurfaceCheck> _surfaces = new(StringComparer.OrdinalIgnoreCase);
    private readonly Timer _sweeper;
    private bool _disposed;
    private long _nextId;

    public WarmPanGlossPool(string? executablePath, WarmPanGlossOptions? options = null)
        : this(executablePath, new MachinePanGlossQueue(), ownsQueue: true, options)
    {
    }

    internal WarmPanGlossPool(string? executablePath, MachinePanGlossQueue queue, bool ownsQueue,
        WarmPanGlossOptions? options = null)
    {
        _executable = executablePath;
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _ownsQueue = ownsQueue;
        _options = options ?? new WarmPanGlossOptions();
        var period = TimeSpan.FromMilliseconds(Math.Clamp(_options.IdleTimeout.TotalMilliseconds / 4, 25, 30_000));
        _sweeper = new Timer(_ => SweepIdle(), null, period, period);
    }

    /// <summary>Raised off the caller's thread when any project's state changes.</summary>
    public event EventHandler<WarmSessionStateChangedEventArgs>? StateChanged;

    /// <summary>The state of a project's session; <see cref="WarmSessionState.Idle"/> when it has none.</summary>
    public WarmSessionState GetState(string projectKey, StepCap? stepCap = null)
    {
        lock (_gate)
            return _sessions.TryGetValue(SessionKey(projectKey, stepCap), out var session) ? session.State : WarmSessionState.Idle;
    }

    /// <summary>
    /// Starts loading <paramref name="grammarPath"/> in the background and returns at once. Idempotent: while the
    /// project is loading or ready it returns the same task. The task completes with <see cref="WarmSessionPhase.Ready"/>
    /// or <see cref="WarmSessionPhase.Failed"/> (with code and message) and never faults. A Failed session is retried.
    /// A different <paramref name="stateToken"/> or grammar replaces the session.
    /// </summary>
    public Task<WarmSessionState> Prewarm(string projectKey, string stateToken, string grammarPath, StepCap? stepCap = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectKey);
        return Acquire(projectKey, stateToken, grammarPath, stepCap, retryFailed: true).Loaded;
    }

    /// <summary>
    /// Parses a word on the project's resident parser. Waits for a load in progress (honoring
    /// <paramref name="cancellationToken"/>, which then abandons only the wait), starts one if there is none, and
    /// returns <see cref="WarmParseResult.Fallback"/> when the session failed, the parser answered the word with an error,
    /// or a reply line was malformed or unterminated, so the caller runs the cold path.
    /// <paramref name="stepCap"/> is passed to <c>serve</c> as <c>--step-cap</c> and is part of the session key.
    /// Cancelling during a parse kills the process.
    /// </summary>
    public async Task<WarmParseResult> TryWordAsync(string projectKey, string stateToken, string grammarPath,
        string word, bool traceDetails, CancellationToken cancellationToken, StepCap? stepCap = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectKey);
        ArgumentException.ThrowIfNullOrEmpty(word);
        var session = Acquire(projectKey, stateToken, grammarPath, stepCap, retryFailed: false);
        if (session.CoolingDown(_options.RetryCooldown) is { } cooling) return cooling;
        try
        {
            var state = await session.Loaded.WaitAsync(cancellationToken).ConfigureAwait(false);
            if (state.Phase != WarmSessionPhase.Ready)
                return new WarmParseResult.Fallback(state.FailureCode ?? "session-unavailable",
                    state.FailureMessage ?? "The resident parser is not available.");
            return await session.ParseAsync(word, traceDetails, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return new WarmParseResult.Cancelled();
        }
    }

    /// <summary>Kills and drops every session of the project, whatever its caps, so nothing holds its Baseline files open. Idempotent.</summary>
    public void Evict(string projectKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectKey);
        var project = Normalize(projectKey);
        List<Session> evicted;
        lock (_gate)
        {
            if (_disposed) return;
            evicted = _sessions.Values.Where(session => string.Equals(session.ProjectKey, project,
                StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var session in evicted) _sessions.Remove(session.Key);
        }
        if (evicted.Count == 0) return;
        foreach (var session in evicted) session.Kill(WarmSessionState.Idle, raise: false);
        Raise(project, WarmSessionState.Idle);
    }

    public void Dispose()
    {
        Session[] all;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            all = _sessions.Values.ToArray();
            _sessions.Clear();
        }
        _sweeper.Dispose();
        foreach (var session in all) session.Kill(WarmSessionState.Idle, raise: false);
        if (_ownsQueue) _queue.Dispose();
    }

    private static string Normalize(string projectKey) => Path.GetFullPath(projectKey);

    /// <summary>PanGloss's own per-word runaway guard, which a parse without <c>--step-cap</c> applies.</summary>
    private const long ParserDefaultSteps = 50_000_000;

    /// <summary>No cap and the parser's default cap share a session; any other cap has its own.</summary>
    private static string SessionKey(string projectKey, StepCap? stepCap) =>
        Normalize(projectKey) + "\n" + (stepCap is null || stepCap.Steps == ParserDefaultSteps
            ? string.Empty : stepCap.ToArgument());

    private Session Acquire(string projectKey, string token, string grammarPath, StepCap? stepCap, bool retryFailed)
    {
        var key = SessionKey(projectKey, stepCap);
        Session? stale = null;
        Session session;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_sessions.TryGetValue(key, out var existing) &&
                (existing.Token != token || existing.GrammarPath != grammarPath))
            {
                stale = existing;
                _sessions.Remove(key);
                existing = null;
            }
            else if (existing is not null && existing.State.Phase == WarmSessionPhase.Failed &&
                     (retryFailed || existing.CoolingDown(_options.RetryCooldown) is null))
            {
                stale = existing;
                _sessions.Remove(key);
                existing = null;
            }
            else if (existing is not null && existing.State.Phase == WarmSessionPhase.Idle)
            {
                _sessions.Remove(key);
                existing = null;
            }
            if (existing is null)
            {
                existing = new Session(this, key, Normalize(projectKey), token, grammarPath, stepCap?.ToArgument());
                _sessions[key] = existing;
            }
            session = existing;
        }
        stale?.Kill(WarmSessionState.Idle, raise: false);
        session.StartLoadOnce();
        return session;
    }

    private void SweepIdle()
    {
        List<Session> idle = [];
        lock (_gate)
        {
            if (_disposed) return;
            foreach (var session in _sessions.Values)
                if (session.IsIdleFor(_options.IdleTimeout)) idle.Add(session);
            foreach (var session in idle) _sessions.Remove(session.Key);
        }
        foreach (var session in idle) session.Shutdown();
    }

    private void Raise(string key, WarmSessionState state)
    {
        var handler = StateChanged;
        if (handler is null) return;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try { handler(this, new WarmSessionStateChangedEventArgs(key, state)); }
            catch (Exception exception) { Trace.TraceWarning("A warm-session state handler failed: {0}", exception.Message); }
        });
    }

    private sealed class Session(WarmPanGlossPool pool, string key, string projectKey, string token, string grammarPath,
        string? stepCapArgument)
    {
        private readonly object _lock = new();
        private readonly TaskCompletionSource<WarmSessionState> _loaded =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly SemaphoreSlim _requests = new(1, 1);
        private PanGlossContainmentJob? _job;
        private PanGlossChildProcess? _process;
        private TerminatedLineReader? _reader;
        private Task<string>? _stderr;
        private WarmSessionState _state = WarmSessionState.Loading;
        private DateTime _lastUsedUtc = DateTime.UtcNow;
        private DateTime _failedUtc;
        private int _busy;
        private bool _started;
        private bool _retired;

        public string Key { get; } = key;
        public string ProjectKey { get; } = projectKey;
        public string Token { get; } = token;
        public string GrammarPath { get; } = grammarPath;
        public Task<WarmSessionState> Loaded => _loaded.Task;
        public WarmSessionState State { get { lock (_lock) return _state; } }

        public WarmParseResult.Fallback? CoolingDown(TimeSpan cooldown)
        {
            lock (_lock)
                return _state.Phase == WarmSessionPhase.Failed && DateTime.UtcNow - _failedUtc < cooldown
                    ? new WarmParseResult.Fallback(_state.FailureCode!, _state.FailureMessage!) : null;
        }

        public bool IsIdleFor(TimeSpan idle)
        {
            lock (_lock)
                return _state.Phase == WarmSessionPhase.Ready && _busy == 0 && DateTime.UtcNow - _lastUsedUtc >= idle;
        }

        public void StartLoadOnce()
        {
            lock (_lock)
            {
                if (_started) return;
                _started = true;
            }
            pool.Raise(ProjectKey, WarmSessionState.Loading);
            _ = Task.Run(LoadAsync);
        }

        private async Task LoadAsync()
        {
            using var timeout = new CancellationTokenSource(pool._options.LoadTimeout);
            try
            {
                if (pool._executable is null)
                {
                    Fail("parser-unavailable", PanGlossExecutable.NotFoundMessage);
                    return;
                }
                await pool._queue.RunAsync("warm-load:" + Path.GetFileName(GrammarPath),
                    (_, token) => StartAndAwaitReadyAsync(token), timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Fail("load-timeout",
                    $"The parser did not finish loading within {pool._options.LoadTimeout.TotalSeconds:0.#} seconds.");
            }
            catch (Exception exception)
            {
                Fail("load-failed", exception.Message);
            }
        }

        private async Task<bool> StartAndAwaitReadyAsync(CancellationToken token)
        {
            var executable = pool._executable!;
            var job = PanGlossContainment.CreateJob(pool._options.MemoryCeilingBytes);
            lock (_lock)
            {
                if (_retired) { job.Dispose(); return false; }
                _job = job;
            }
            var surface = pool._surfaces.TryGetValue(executable, out var known) ? known
                : pool._surfaces[executable] = await PanGlossSurface.CheckAsync(executable, job, token).ConfigureAwait(false);
            if (!surface.IsValid)
            {
                Fail("serve-unsupported", surface.Message);
                return false;
            }
            if (surface.Capabilities is null || !surface.Capabilities.Commands.TryGetValue("serve", out var serve) ||
                serve.Hidden || surface.Capabilities.ServeProtocol != ServeProtocol)
            {
                Fail("serve-unsupported", "This parser has no resident-session mode (serve protocol " +
                    $"{ServeProtocol}), so words run one process each.");
                return false;
            }

            var start = new ProcessStartInfo(executable)
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            PanGlossProcessEnvironment.Configure(start);
            start.ArgumentList.Add("serve");
            start.ArgumentList.Add("--project");
            start.ArgumentList.Add(GrammarPath);
            if (stepCapArgument is not null)
            {
                start.ArgumentList.Add("--step-cap");
                start.ArgumentList.Add(stepCapArgument);
            }
            PanGlossChildProcess process;
            try { process = job.Start(start); }
            catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException
                                                  or IOException or UnauthorizedAccessException)
            {
                Fail("serve-start-failed", $"Could not start '{executable}': {exception.Message}");
                return false;
            }
            var reader = new TerminatedLineReader(process.InteractiveOutput!, PanGlossProcessEnvironment.StreamEncoding);
            lock (_lock)
            {
                if (_retired) { process.Dispose(); return false; }
                _process = process;
                _reader = reader;
                _stderr = process.ReadStandardErrorAsync();
            }
            _ = process.WaitForExitAsync(CancellationToken.None).ContinueWith(_ => OnExited(process), TaskScheduler.Default);
            using var registration = token.Register(() => Kill(null, raise: false));
            string? line;
            try { line = await reader.ReadLineAsync().ConfigureAwait(false); }
            catch (Exception exception) when (exception is IOException or ObjectDisposedException)
            {
                line = null;
            }
            if (token.IsCancellationRequested) throw new OperationCanceledException(token);
            if (line is null)
            {
                Fail(Died().Code, Died().Message);
                return false;
            }
            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                var type = root.TryGetProperty("type", out var t) ? t.GetString() : null;
                if (type == "fatal")
                {
                    Fail(root.TryGetProperty("code", out var c) ? c.GetString() ?? "fatal" : "fatal",
                        root.TryGetProperty("message", out var m) ? m.GetString() ?? "The parser failed to load." : "The parser failed to load.");
                    return false;
                }
                if (type == "ready" && root.TryGetProperty("protocol", out var p) && p.TryGetInt32(out var version) &&
                    version == ServeProtocol)
                {
                    lock (_lock) _lastUsedUtc = DateTime.UtcNow;
                    SetState(WarmSessionState.Ready);
                    _loaded.TrySetResult(WarmSessionState.Ready);
                    return true;
                }
                Fail("serve-protocol", $"The parser's first line was not a ready line for protocol {ServeProtocol}.");
            }
            catch (JsonException)
            {
                Fail("serve-protocol", "The parser's first line was not JSON.");
            }
            return false;
        }

        public async Task<WarmParseResult> ParseAsync(string word, bool traceDetails, CancellationToken cancellationToken)
        {
            await _requests.WaitAsync(cancellationToken).ConfigureAwait(false);
            Interlocked.Increment(ref _busy);
            try
            {
                return await pool._queue.RunAsync("warm-parse:" + word,
                    (_, token) => RequestAsync(word, traceDetails, token), cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                lock (_lock) _lastUsedUtc = DateTime.UtcNow;
                Interlocked.Decrement(ref _busy);
                _requests.Release();
            }
        }

        private async Task<WarmParseResult> RequestAsync(string word, bool traceDetails, CancellationToken token)
        {
            PanGlossChildProcess? process;
            TerminatedLineReader? reader;
            lock (_lock) { process = _process; reader = _reader; }
            if (State.Phase != WarmSessionPhase.Ready || process is null || reader is null)
                return new WarmParseResult.Fallback("session-ended", "The resident parser is no longer running.");
            var id = Interlocked.Increment(ref pool._nextId).ToString(System.Globalization.CultureInfo.InvariantCulture);
            var clock = Stopwatch.StartNew();
            using var registration = token.Register(() => Kill(WarmSessionState.Idle, raise: true));
            try
            {
                var request = JsonSerializer.Serialize(new { id, op = "parse", word, traceDetails }) + "\n";
                var bytes = PanGlossProcessEnvironment.StreamEncoding.GetBytes(request);
                await process.InteractiveInput!.WriteAsync(bytes, CancellationToken.None).ConfigureAwait(false);
                await process.InteractiveInput.FlushAsync(CancellationToken.None).ConfigureAwait(false);
                while (true)
                {
                    var line = await reader.ReadLineAsync().ConfigureAwait(false);
                    if (token.IsCancellationRequested) return new WarmParseResult.Cancelled();
                    if (line is null)
                    {
                        var died = Died();
                        Fail(died.Code, died.Message);
                        return new WarmParseResult.Fallback(died.Code, died.Message);
                    }
                    using var document = JsonDocument.Parse(line);
                    var root = document.RootElement;
                    if (!root.TryGetProperty("id", out var replyId) || replyId.GetString() != id) continue;
                    var type = root.TryGetProperty("type", out var t) ? t.GetString() : null;
                    if (type == "result" && root.TryGetProperty("result", out var result))
                    {
                        if (!root.TryGetProperty("exitCode", out var exit) || !exit.TryGetInt32(out var exitCode))
                            return new WarmParseResult.Fallback("serve-protocol", "A result line carried no exit code.");
                        return new WarmParseResult.Completed(result.GetRawText(), exitCode, clock.Elapsed);
                    }
                    var code = root.TryGetProperty("code", out var c) ? c.GetString() ?? "error" : "error";
                    var message = root.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "";
                    return new WarmParseResult.Fallback(code, message);
                }
            }
            catch (Exception exception) when (exception is IOException or ObjectDisposedException or JsonException)
            {
                if (token.IsCancellationRequested) return new WarmParseResult.Cancelled();
                var died = Died();
                Fail(died.Code, died.Message);
                return new WarmParseResult.Fallback(died.Code, died.Message);
            }
        }

        private (string Code, string Message) Died()
        {
            PanGlossChildProcess? process;
            Task<string>? stderr;
            lock (_lock) { process = _process; stderr = _stderr; }
            try
            {
                if (process is not null)
                {
                    process.WaitForExitAsync().Wait(TimeSpan.FromSeconds(5));
                    if (process.MemoryKill is { } kill) return ("memory-ceiling", kill.Describe());
                }
            }
            catch (Exception exception) when (exception is AggregateException or InvalidOperationException or IOException) { }
            string detail = string.Empty;
            try { if (stderr is not null && stderr.Wait(TimeSpan.FromSeconds(2))) detail = stderr.Result.Trim(); }
            catch (AggregateException) { }
            return ("process-exited", "The resident parser stopped unexpectedly." +
                (detail.Length == 0 ? string.Empty : " " + detail));
        }

        private void OnExited(PanGlossChildProcess process)
        {
            lock (_lock)
            {
                if (_retired || !ReferenceEquals(_process, process) || _state.Phase != WarmSessionPhase.Ready) return;
            }
            var died = Died();
            Fail(died.Code, died.Message);
        }

        private void SetState(WarmSessionState state)
        {
            lock (_lock)
            {
                if (_state == state) return;
                _state = state;
                if (state.Phase == WarmSessionPhase.Failed) _failedUtc = DateTime.UtcNow;
            }
            pool.Raise(ProjectKey, state);
        }

        private void Fail(string code, string message)
        {
            var state = new WarmSessionState(WarmSessionPhase.Failed, code, message);
            Kill(state, raise: true);
            _loaded.TrySetResult(state);
        }

        /// <summary>Kills the process and its job. With a state, records it (and, when Idle, retires the session).</summary>
        public void Kill(WarmSessionState? state, bool raise)
        {
            PanGlossContainmentJob? job;
            PanGlossChildProcess? process;
            lock (_lock)
            {
                job = _job;
                process = _process;
                if (state is not null)
                {
                    if (_state.Phase == WarmSessionPhase.Failed && state.Phase == WarmSessionPhase.Idle) { }
                    else
                    {
                        _state = state;
                        if (state.Phase == WarmSessionPhase.Failed) _failedUtc = DateTime.UtcNow;
                    }
                    if (state.Phase == WarmSessionPhase.Idle) _retired = true;
                }
            }
            try
            {
                if (job is not null && process is not null) job.Terminate(process);
                job?.Dispose();
                process?.Dispose();
            }
            catch (Exception exception) when (exception is IOException or ObjectDisposedException)
            {
                Trace.TraceWarning("Stopping the resident parser failed: {0}", exception.Message);
            }
            if (state is not null && state.Phase == WarmSessionPhase.Idle)
            {
                lock (pool._gate)
                    if (pool._sessions.TryGetValue(Key, out var current) && ReferenceEquals(current, this))
                        pool._sessions.Remove(Key);
                _loaded.TrySetResult(state);
            }
            if (raise && state is not null) pool.Raise(ProjectKey, state);
        }

        /// <summary>Ends an idle session politely: close its input, then kill whatever is left.</summary>
        public void Shutdown()
        {
            PanGlossChildProcess? process;
            lock (_lock) { process = _process; _retired = true; _state = WarmSessionState.Idle; }
            try { process?.InteractiveInput?.Dispose(); process?.WaitForExitAsync().Wait(TimeSpan.FromSeconds(2)); }
            catch (Exception exception) when (exception is AggregateException or IOException or ObjectDisposedException) { }
            Kill(null, raise: false);
            pool.Raise(ProjectKey, WarmSessionState.Idle);
        }
    }
}
