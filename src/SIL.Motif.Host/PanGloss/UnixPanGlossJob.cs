using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace SIL.Motif.Host.PanGloss;

[SupportedOSPlatform("linux")]
internal sealed class LinuxPanGlossJob(ulong memoryLimitBytes) : UnixPanGlossJob(memoryLimitBytes, linux: true);

[SupportedOSPlatform("macos")]
internal sealed class MacPanGlossJob(ulong memoryLimitBytes) : UnixPanGlossJob(memoryLimitBytes, linux: false);

internal class UnixPanGlossJob : PanGlossContainmentJob
{
    private const string ShellPath = "/bin/sh";
    private const short SpawnSetProcessGroup = 2;
    private const int KillSignal = 9;
    private readonly bool _linux;
    private readonly ulong _memoryLimitBytes;
    private readonly UnixCgroup? _cgroup;
    private readonly HashSet<int> _processGroups = [];
    private bool _disposed;

    internal UnixPanGlossJob(ulong memoryLimitBytes, bool linux)
        : this(memoryLimitBytes, linux, TryCreateCgroup(memoryLimitBytes, linux))
    {
    }

    private UnixPanGlossJob(ulong memoryLimitBytes, bool linux, UnixCgroup? cgroup)
        : base(CreateReport(memoryLimitBytes, linux, cgroup))
    {
        _linux = linux;
        _memoryLimitBytes = memoryLimitBytes;
        _cgroup = cgroup;
    }

    public override PanGlossChildProcess Start(ProcessStartInfo startInfo)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (startInfo.UseShellExecute)
            throw new ArgumentException("Contained parser processes must disable shell execution.", nameof(startInfo));
        if (startInfo.ArgumentList.Count == 0 && !string.IsNullOrEmpty(startInfo.Arguments))
            throw new ArgumentException("Contained parser arguments must use ArgumentList.", nameof(startInfo));
        EnsureExecutablePathExists(startInfo);

        var stdout = CreateCaptureStream();
        var stderr = CreateCaptureStream();
        var processId = 0;
        var actions = Marshal.AllocHGlobal(1024);
        var attributes = Marshal.AllocHGlobal(1024);
        var shellPath = UnixNative.Utf8(ShellPath);
        IntPtr arguments = IntPtr.Zero;
        IntPtr environment = IntPtr.Zero;
        IntPtr[] argumentStrings = [];
        IntPtr[] environmentStrings = [];
        var actionsInitialized = false;
        var attributesInitialized = false;
        try
        {
            UnixNative.CheckSpawn(UnixNative.SpawnFileActionsInit(actions), "initializing process file actions");
            actionsInitialized = true;
            UnixNative.CheckSpawn(UnixNative.SpawnFileActionsAddDup2(actions,
                stdout.SafeFileHandle.DangerousGetHandle().ToInt32(), 1), "redirecting standard output");
            UnixNative.CheckSpawn(UnixNative.SpawnFileActionsAddDup2(actions,
                stderr.SafeFileHandle.DangerousGetHandle().ToInt32(), 2), "redirecting standard error");
            var stdoutHandle = stdout.SafeFileHandle.DangerousGetHandle().ToInt32();
            var stderrHandle = stderr.SafeFileHandle.DangerousGetHandle().ToInt32();
            if (stdoutHandle != 1 && stdoutHandle != 2)
                UnixNative.CheckSpawn(UnixNative.SpawnFileActionsAddClose(actions, stdoutHandle),
                    "closing the output capture handle");
            if (stderrHandle != 1 && stderrHandle != 2)
                UnixNative.CheckSpawn(UnixNative.SpawnFileActionsAddClose(actions, stderrHandle),
                    "closing the error capture handle");

            UnixNative.CheckSpawn(UnixNative.SpawnAttributesInit(attributes), "initializing process attributes");
            attributesInitialized = true;
            UnixNative.CheckSpawn(UnixNative.SpawnAttributesSetFlags(attributes, SpawnSetProcessGroup),
                "enabling process-group creation");
            UnixNative.CheckSpawn(UnixNative.SpawnAttributesSetProcessGroup(attributes, 0),
                "assigning the parser to its process group");

            var argv = new List<string> { "sh", "-c", BuildScript(startInfo), startInfo.FileName };
            argv.AddRange(startInfo.ArgumentList);
            arguments = UnixNative.StringVector(argv, out argumentStrings);
            environment = UnixNative.StringVector(BuildEnvironment(startInfo), out environmentStrings);
            UnixNative.CheckSpawn(UnixNative.Spawn(out processId, shellPath, actions, attributes,
                arguments, environment), "starting the parser");
            _processGroups.Add(processId);
            return new PanGlossChildProcess(new UnixPanGlossChildProcess(processId, stdout, stderr,
                () => KillProcessGroup(processId)));
        }
        catch
        {
            stdout.Dispose();
            stderr.Dispose();
            throw;
        }
        finally
        {
            if (attributesInitialized) _ = UnixNative.SpawnAttributesDestroy(attributes);
            if (actionsInitialized) _ = UnixNative.SpawnFileActionsDestroy(actions);
            Marshal.FreeHGlobal(attributes);
            Marshal.FreeHGlobal(actions);
            Marshal.FreeHGlobal(shellPath);
            if (arguments != IntPtr.Zero) UnixNative.FreeVector(arguments, argumentStrings);
            if (environment != IntPtr.Zero) UnixNative.FreeVector(environment, environmentStrings);
        }
    }

    public override void Terminate(PanGlossChildProcess process)
    {
        ArgumentNullException.ThrowIfNull(process);
        if (_cgroup is not null) _cgroup.Kill();
        foreach (var processGroup in _processGroups) KillProcessGroup(processGroup);
    }

    public override void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        TerminateAll();
        _cgroup?.Dispose();
    }

    private void TerminateAll()
    {
        if (_cgroup is not null) _cgroup.Kill();
        foreach (var processGroup in _processGroups) KillProcessGroup(processGroup);
    }

    private void KillProcessGroup(int processGroupId)
    {
        if (UnixNative.Kill(-processGroupId, KillSignal) == 0) return;
        var error = UnixNative.LastError;
        if (error is not (3 or 1)) throw new IOException("Could not stop the parser process group.", error);
    }

    private string BuildScript(ProcessStartInfo startInfo)
    {
        var script = new StringBuilder();
        script.Append("cd ").Append(ShellQuote(string.IsNullOrWhiteSpace(startInfo.WorkingDirectory)
            ? Environment.CurrentDirectory : startInfo.WorkingDirectory)).Append(" || exit 125; ");
        if (_cgroup is not null)
        {
            script.Append("printf '%s\\n' \"$$\" > ")
                .Append(ShellQuote(Path.Combine(_cgroup.Path, "cgroup.procs"))).Append(" || exit 125; ");
        }
        if (_cgroup is null)
        {
            var kibibytes = (_memoryLimitBytes + 1023) / 1024;
            var resource = _linux ? 'v' : 'd';
            var limit = kibibytes.ToString(System.Globalization.CultureInfo.InvariantCulture);
            script.Append("ulimit -H -").Append(resource).Append(' ').Append(limit)
                .Append(" 2>/dev/null && ulimit -S -").Append(resource).Append(' ').Append(limit)
                .Append(" 2>/dev/null || exit 125; ");
        }
        script.Append("exec \"$0\" \"$@\"");
        return script.ToString();
    }

    private static List<string> BuildEnvironment(ProcessStartInfo startInfo)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
            values[(string)entry.Key] = (string)entry.Value!;
        foreach (var pair in startInfo.Environment)
        {
            if (pair.Value is null) values.Remove(pair.Key);
            else values[pair.Key] = pair.Value;
        }
        return values.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => pair.Key + "=" + pair.Value).ToList();
    }

    private static FileStream CreateCaptureStream() => new(Path.Combine(Path.GetTempPath(),
        "motif-pangloss-" + Guid.NewGuid().ToString("N") + ".out"), FileMode.CreateNew,
        FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.DeleteOnClose);

    private static string ShellQuote(string value) => "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";

    private static void EnsureExecutablePathExists(ProcessStartInfo startInfo)
    {
        if (!Path.IsPathRooted(startInfo.FileName) &&
            !startInfo.FileName.Contains(Path.DirectorySeparatorChar) &&
            !startInfo.FileName.Contains(Path.AltDirectorySeparatorChar)) return;
        var path = Path.IsPathRooted(startInfo.FileName)
            ? startInfo.FileName
            : Path.Combine(string.IsNullOrWhiteSpace(startInfo.WorkingDirectory)
                ? Environment.CurrentDirectory : startInfo.WorkingDirectory, startInfo.FileName);
        if (!File.Exists(path) || Directory.Exists(path))
            throw new Win32Exception(2, $"Executable not found: '{startInfo.FileName}'.");
    }

    private static PanGlossContainmentReport CreateReport(ulong memoryLimitBytes, bool linux, UnixCgroup? cgroup)
    {
        if (cgroup is not null)
            return new PanGlossContainmentReport(2500, memoryLimitBytes, true,
                "Linux cgroup v2 quota at 2500 basis points of one CPU.",
                "Linux cgroup v2 aggregate memory.max hard limit.",
                "The child joins the cgroup before exec; cgroup.kill stops members still in that subtree.",
                ["A descendant with permission to leave the delegated cgroup and process group can escape termination."]);

        var limitations = linux
            ? new[]
            {
                "No delegated writable cgroup v2 with cpu and memory controls was available; CPU rate and aggregate memory are uncapped.",
                "A descendant that deliberately leaves the process group can outlive the job."
            }
            : new[]
            {
                "macOS has no cgroup equivalent; CPU rate and aggregate memory are uncapped, and RLIMIT_DATA covers only each process's data segment.",
                "A descendant that deliberately leaves the process group can outlive the job."
            };
        return new PanGlossContainmentReport(null, memoryLimitBytes, false,
            linux ? "No hard CPU rate limit; RLIMIT_CPU is a total-time limit and is not substituted." : "No hard CPU rate limit is available.",
            linux ? "RLIMIT_AS address-space limit per process." : "RLIMIT_DATA data-segment limit per process.",
            "The child starts in its own process group; members are killed on close, but a detached descendant can escape.",
            limitations);
    }

    private static UnixCgroup? TryCreateCgroup(ulong memoryLimitBytes, bool linux)
    {
        if (!linux) return null;
        var parent = UnixCgroup.FindDelegatedParent();
        if (parent is null) return null;
        var path = Path.Combine(parent, "motif-pangloss-" + Environment.ProcessId + "-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(path);
            if (!File.Exists(Path.Combine(path, "cgroup.kill"))) throw new IOException("cgroup.kill is unavailable.");
            File.WriteAllText(Path.Combine(path, "cpu.max"), "25000 100000");
            File.WriteAllText(Path.Combine(path, "memory.max"), memoryLimitBytes.ToString(System.Globalization.CultureInfo.InvariantCulture));
            var swapLimit = Path.Combine(path, "memory.swap.max");
            if (File.Exists(swapLimit)) File.WriteAllText(swapLimit, "0");
            return new UnixCgroup(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            try { Directory.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return null;
        }
    }
}

internal sealed class UnixPanGlossChildProcess : IPanGlossChildProcess
{
    private readonly FileStream _stdout;
    private readonly FileStream _stderr;
    private readonly Action _kill;
    private readonly Task<int> _exitCode;
    private bool _disposed;

    internal UnixPanGlossChildProcess(int processId, FileStream stdout, FileStream stderr, Action kill)
    {
        Id = processId;
        _stdout = stdout;
        _stderr = stderr;
        _kill = kill;
        _exitCode = Task.Run(() => WaitForExitCode(processId));
    }

    public int Id { get; }
    public int ExitCode => _exitCode.GetAwaiter().GetResult();
    public async Task WaitForExitAsync(CancellationToken cancellationToken) =>
        _ = await _exitCode.WaitAsync(cancellationToken).ConfigureAwait(false);

    public Task<string> ReadStandardOutputAsync() => ReadAsync(_stdout);
    public Task<string> ReadStandardErrorAsync() => ReadAsync(_stderr);

    public void KillProcessTree() => _kill();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _stdout.Dispose();
        _stderr.Dispose();
    }

    private static int WaitForExitCode(int processId)
    {
        int status;
        while (UnixNative.WaitPid(processId, out status, 0) < 0)
        {
            if (UnixNative.LastError == 4) continue;
            throw new IOException("Could not wait for the parser process.", UnixNative.LastError);
        }
        return (status & 0x7f) == 0 ? (status >> 8) & 0xff : 128 + (status & 0x7f);
    }

    private static async Task<string> ReadAsync(FileStream stream)
    {
        await stream.FlushAsync().ConfigureAwait(false);
        stream.Position = 0;
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true,
            bufferSize: 4096, leaveOpen: true);
        return await reader.ReadToEndAsync().ConfigureAwait(false);
    }
}

internal sealed class UnixCgroup(string path) : IDisposable
{
    internal string Path { get; } = path;

    internal void Kill()
    {
        var path = System.IO.Path.Combine(Path, "cgroup.kill");
        if (!File.Exists(path)) return;
        try { File.WriteAllText(path, "1"); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public void Dispose()
    {
        Kill();
        try { Directory.Delete(Path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    internal static string? FindDelegatedParent()
    {
        try
        {
            var group = File.ReadLines("/proc/self/cgroup").FirstOrDefault(line => line.StartsWith("0::", StringComparison.Ordinal));
            if (group is null) return null;
            var current = group[3..];
            foreach (var line in File.ReadLines("/proc/self/mountinfo"))
            {
                var separator = line.IndexOf(" - ", StringComparison.Ordinal);
                if (separator < 0) continue;
                var before = line[..separator].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var after = line[(separator + 3)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (after.Length < 1 || after[0] != "cgroup2" || before.Length < 5) continue;
                var mountRoot = UnescapeMountPath(before[3]);
                var mountPoint = UnescapeMountPath(before[4]);
                if (current != mountRoot && !current.StartsWith(mountRoot.TrimEnd('/') + "/", StringComparison.Ordinal)) continue;
                var relative = current[mountRoot.Length..].TrimStart('/');
                var parent = System.IO.Path.GetFullPath(System.IO.Path.Combine(mountPoint, relative));
                var controllers = File.ReadAllText(System.IO.Path.Combine(parent, "cgroup.controllers"));
                var enabled = File.ReadAllText(System.IO.Path.Combine(parent, "cgroup.subtree_control"))
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(value => value.TrimStart('+')).ToArray();
                if (controllers.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains("cpu", StringComparer.Ordinal) &&
                    controllers.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains("memory", StringComparer.Ordinal) &&
                    enabled.Contains("cpu", StringComparer.Ordinal) && enabled.Contains("memory", StringComparer.Ordinal))
                    return parent;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
        return null;
    }

    private static string UnescapeMountPath(string value) => value
        .Replace("\\040", " ", StringComparison.Ordinal)
        .Replace("\\011", "\t", StringComparison.Ordinal)
        .Replace("\\134", "\\", StringComparison.Ordinal);
}

internal static class UnixProcessIdentity
{
    internal static uint GetEffectiveUserId() => UnixNative.GetEffectiveUserId();
    internal static int GetProcessGroupId(int processId) => UnixNative.GetProcessGroupId(processId);

    internal static bool ProcessExists(int processId)
    {
        if (UnixNative.Kill(processId, 0) == 0) return true;
        return UnixNative.LastError == 1;
    }
}
