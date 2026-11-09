using System.Diagnostics;
using System.Text;
using System.Text.Json;
using SIL.Motif.Host.Assistants;

namespace SIL.Motif.App.Services;

/// <summary>Owns a tunnel-client process for the lifetime of a desktop session.</summary>
public sealed class TunnelClient : ITunnelClient
{
    private readonly object _gate = new();
    private readonly string _executable;
    private readonly string _motif;
    private readonly TunnelProcessRegistry? _recovery;
    private readonly string _stateDirectory;
    private readonly int _ownerPid;
    private readonly long _ownerStart;
    private ITunnelProcess? _child;
    private readonly Func<ProcessStartInfo, ITunnelProcess> _start;
    private string? _profileDirectory;
    private int _generation;
    private TunnelStatus _status;
    private bool _disposed;

    /// <summary>Creates a tunnel for the bundled client and the adjacent Motif command.</summary>
    public TunnelClient() : this(
        Path.Combine(AppContext.BaseDirectory, "tunnel-client", OperatingSystem.IsWindows() ? "tunnel-client.exe" : "tunnel-client"),
        Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "motif.exe" : "motif"),
        stateDirectory: TunnelProcessRegistry.DefaultDirectory) { }

    internal TunnelClient(string executable, string motif, Func<ProcessStartInfo, ITunnelProcess>? start = null,
        string? stateDirectory = null)
    {
        (_executable, _motif) = (executable, motif);
        _stateDirectory = stateDirectory ?? TunnelProcessRegistry.DefaultDirectory;
        using var owner = Process.GetCurrentProcess();
        _ownerPid = owner.Id;
        _ownerStart = TunnelProcessIdentity.Start(owner);
        if (stateDirectory is not null)
        {
            _recovery = new TunnelProcessRegistry(stateDirectory);
            try { _recovery.Recover(); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or
                System.ComponentModel.Win32Exception) { }
        }
        _start = start ?? (info => new TunnelProcess(Process.Start(info) ?? throw new InvalidOperationException(),
            _recovery, _ownerPid, _ownerStart, _executable));
    }

    /// <inheritdoc />
    public TunnelStatus Status { get { lock (_gate) return _status; } }
    /// <inheritdoc />
    public event Action<TunnelStatus>? StatusChanged;

    /// <inheritdoc />
    public async Task ConnectAsync(string key, string tunnelId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(tunnelId);
        Disconnect();
        int generation;
        lock (_gate)
        {
            if (_disposed) return;
            generation = ++_generation;
        }
        SetStatus(generation, TunnelStatus.Connecting);
        try
        {
            _recovery?.Recover();
            var directory = Directory.CreateTempSubdirectory("motif-tunnel-").FullName;
            lock (_gate)
            {
                if (generation != _generation) { Directory.Delete(directory, true); return; }
                _profileDirectory = directory;
            }
            var start = NewStart();
            start.ArgumentList.Add("init");
            Add(start, "--sample", "sample_mcp_stdio_local", "--profile", "local-stdio", "--profile-dir", directory,
                "--tunnel-id", tunnelId, "--mcp-command", QuoteCommand(_motif) + " mcp");
            var init = StartOwned(start, generation);
            if (init is null) return;
            var stdout = init.StandardOutput.ReadToEndAsync();
            var stderr = init.StandardError.ReadToEndAsync();
            var initExit = await init.WaitForExitAsync().ConfigureAwait(false);
            await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
            lock (_gate) if (ReferenceEquals(_child, init)) _child = null;
            init.Dispose();
            if (initExit != 0) throw new InvalidOperationException();
            start = NewStart();
            Add(start, "run", "--profile", "local-stdio", "--profile-dir", directory,
                "--log.level", "debug", "--log.format", "json", "--log.http-raw-unsafe=false",
                "--health.listen-addr", "127.0.0.1:0");
            start.Environment["CONTROL_PLANE_API_KEY"] = key;
            var running = StartOwned(start, generation);
            if (running is null) return;
            _ = ObserveAsync(running, generation);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or
            System.ComponentModel.Win32Exception or JsonException)
        {
            SetStatus(generation, TunnelStatus.Error);
        }
    }

    private ProcessStartInfo NewStart()
    {
        var start = new ProcessStartInfo(_motif)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true,
            RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
        };
        Add(start, "tunnel-supervise", "--owner-pid", _ownerPid.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--owner-start", _ownerStart.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--executable", _executable, "--state-dir", _stateDirectory, "--");
        foreach (var name in new[] { "CONTROL_PLANE_API_KEY", "OPENAI_API_KEY", "CONTROL_PLANE_TUNNEL_ID",
                     "MCP_COMMAND", "MCP_SERVER_URL", "TUNNEL_CLIENT_CONFIG", "TUNNEL_CLIENT_PROFILE",
                     "TUNNEL_CLIENT_PROFILE_FILE", "ICU_DATA", "LOG_FILE", "LOG_HTTP_RAW_UNSAFE" })
            start.Environment.Remove(name);
        return start;
    }

    private static void Add(ProcessStartInfo start, params string[] arguments)
    {
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
    }

    private static string QuoteCommand(string path)
    {
        if (OperatingSystem.IsWindows()) path = path.Replace('\\', '/');
        else path = path.Replace("\\", "\\\\", StringComparison.Ordinal);
        return '"' + path.Replace("\"", "\\\"", StringComparison.Ordinal) + '"';
    }

    private ITunnelProcess? StartOwned(ProcessStartInfo start, int generation)
    {
        lock (_gate)
        {
            if (generation != _generation) return null;
            _child = _start(start);
            return _child;
        }
    }

    private async Task ObserveAsync(ITunnelProcess child, int generation)
    {
        var output = ObserveStreamAsync(child.StandardOutput, generation);
        var error = ObserveStreamAsync(child.StandardError, generation);
        await child.WaitForExitAsync().ConfigureAwait(false);
        await Task.WhenAll(output, error).ConfigureAwait(false);
        SetStatus(generation, TunnelStatus.Error);
        child.Dispose();
    }

    private async Task ObserveStreamAsync(TextReader stream, int generation)
    {
        while (await stream.ReadLineAsync().ConfigureAwait(false) is { } line)
        {
            try
            {
                using var record = JsonDocument.Parse(line);
                if (record.RootElement.ValueKind != JsonValueKind.Object ||
                    !record.RootElement.TryGetProperty("msg", out var message) || message.ValueKind != JsonValueKind.String) continue;
                switch (message.GetString())
                {
                    case "poll cycle complete":
                    case "poller recovered; polling operational":
                        SetStatus(generation, TunnelStatus.Connected);
                        break;
                    case "poll failed; backing off":
                    case "poll timed out; backing off":
                        SetStatus(generation, TunnelStatus.Error);
                        break;
                }
            }
            catch (JsonException) { }
        }
    }

    private void SetStatus(int generation, TunnelStatus status)
    {
        lock (_gate)
        {
            if (generation != _generation || _status == status) return;
            _status = status;
        }
        StatusChanged?.Invoke(status);
    }

    /// <inheritdoc />
    public void Disconnect()
    {
        ITunnelProcess? child;
        string? directory;
        lock (_gate)
        {
            ++_generation;
            child = _child;
            directory = _profileDirectory;
            _child = null;
            _profileDirectory = null;
            _status = TunnelStatus.NotConnected;
        }
        if (child is not null)
        {
            try { child.Kill(); }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
        }
        if (directory is not null)
        {
            try { Directory.Delete(directory, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        StatusChanged?.Invoke(TunnelStatus.NotConnected);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate) _disposed = true;
        Disconnect();
    }
}

internal interface ITunnelProcess : IDisposable
{
    TextReader StandardOutput { get; }
    TextReader StandardError { get; }
    Task<int> WaitForExitAsync();
    void Kill();
}

internal sealed class TunnelProcess : ITunnelProcess
{
    private readonly Process _process;
    private readonly TunnelProcessRegistry? _registry;
    private readonly int _ownerPid;
    private readonly long _ownerStart;
    private readonly int _groupId;
    private readonly long _leaderStart;
    private readonly string _bundledDirectory;
    private readonly object _cleanupGate = new();

    internal TunnelProcess(Process process, TunnelProcessRegistry? registry, int ownerPid, long ownerStart, string executable)
    {
        _process = process;
        _registry = registry;
        _ownerPid = ownerPid;
        _ownerStart = ownerStart;
        _groupId = process.Id;
        _leaderStart = TunnelProcessIdentity.Start(process);
        _bundledDirectory = OperatingSystem.IsWindows() ? string.Empty :
            TunnelUnixGroup.CanonicalDirectory(Path.GetDirectoryName(Path.GetFullPath(executable))!);
    }

    public TextReader StandardOutput => _process.StandardOutput;
    public TextReader StandardError => _process.StandardError;
    public async Task<int> WaitForExitAsync()
    {
        await _process.WaitForExitAsync().ConfigureAwait(false);
        var result = _process.ExitCode;
        CleanupGroup();
        return result;
    }
    public void Kill()
    {
        if (!_process.HasExited)
        {
            _process.StandardInput.Close();
            if (!OperatingSystem.IsWindows()) CleanupGroup();
            if (!_process.WaitForExit(5000)) _process.Kill(entireProcessTree: true);
        }
        _process.WaitForExit();
        if (!OperatingSystem.IsWindows()) CleanupGroup();
    }
    private void CleanupGroup()
    {
        if (OperatingSystem.IsWindows()) return;
        lock (_cleanupGate)
        {
            if (TunnelUnixGroup.Stop(_groupId, _leaderStart, bundledDirectory: _bundledDirectory))
                _registry?.ForgetGroup(_ownerPid, _ownerStart, _groupId, _leaderStart);
        }
    }
    public void Dispose() => _process.Dispose();
}
