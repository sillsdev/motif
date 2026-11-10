using System.Diagnostics;
using System.Runtime.InteropServices;
using SIL.Motif.App;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Host.Assistants;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class ChatGptSetupTests
{
    [Fact]
    public async Task PageKeepsAnUnsavedKeyForTheSessionAndExplainsTheFallback()
    {
        var store = new FakeStore();
        var tunnel = new FakeTunnel();
        var context = WorkspaceContextTests.NewContext(new FakeCommandClient(), credentials: store, tunnelClient: tunnel);
        var page = new AssistantConnectionsPageModel(context) { ChatGptKey = "private-key", ChatGptTunnelId = "tunnel-id" };

        await page.ConnectChatGptCommand.ExecuteAsync(null);

        Assert.Equal("private-key", store.Value);
        Assert.Equal(("private-key", "tunnel-id"), tunnel.Connection);
        Assert.Equal(string.Empty, page.ChatGptKey);
        Assert.Equal(MotifAppStrings.ChatGptKeyNotSaved, page.ChatGptSaveWarning);
        await page.ConnectChatGptCommand.ExecuteAsync(null);
        Assert.Equal(("private-key", "tunnel-id"), tunnel.Connection);
        page.DisconnectChatGptCommand.Execute(null);
        Assert.True(tunnel.Disconnected);
    }

    [Fact]
    public async Task PollFailureIsReportedWithoutExposingProcessOutput()
    {
        var calls = 0;
        var init = new FakeProcess(complete: true);
        var run = new FakeProcess(complete: false, "{\"msg\":\"poll failed; backing off\",\"error\":\"private-key\"}\n");
        using var client = new TunnelClient("fake-tunnel", "/installed/motif", _ => ++calls == 1 ? init : run);
        await client.ConnectAsync("private-key", "tunnel-id");
        await UntilAsync(() => client.Status == TunnelStatus.Error);
        Assert.Equal(TunnelStatus.Error, client.Status);
    }

    [Fact]
    public async Task TunnelPassesKeyOnlyInEnvironmentAndStopsTheProcessItStarted()
    {
        var starts = new List<ProcessStartInfo>();
        var init = new FakeProcess(complete: true);
        var run = new FakeProcess(complete: false, "{\"msg\":\"poll cycle complete\"}\n");
        using var client = new TunnelClient("fake-tunnel", "/installed/motif", start =>
        {
            starts.Add(start);
            return starts.Count == 1 ? init : run;
        });
        await client.ConnectAsync("private-key", "tunnel-id");
        await UntilAsync(() => client.Status == TunnelStatus.Connected);

        Assert.All(starts, start => Assert.Equal("/installed/motif", start.FileName));
        Assert.All(starts, start => Assert.Equal("tunnel-supervise", start.ArgumentList[0]));
        Assert.False(starts[0].Environment.ContainsKey("CONTROL_PLANE_API_KEY"));
        Assert.Equal("private-key", starts[1].Environment["CONTROL_PLANE_API_KEY"]);
        Assert.All(starts, start => Assert.DoesNotContain(start.ArgumentList, arg => arg.Contains("private-key", StringComparison.Ordinal)));
        Assert.Contains(starts[0].ArgumentList, arg => arg.Contains("motif", StringComparison.Ordinal) && arg.Contains(" mcp", StringComparison.Ordinal));
        client.Disconnect();
        Assert.True(run.Killed);
        Assert.Equal(TunnelStatus.NotConnected, client.Status);
    }

    [Fact]
    public async Task ClosingDuringSetupStopsTheInitializerAndCannotStartAnotherProcess()
    {
        var starts = 0;
        var initializer = new FakeProcess(complete: false);
        var client = new TunnelClient("fake-tunnel", "/installed/motif", _ => { starts++; return initializer; });
        var connecting = client.ConnectAsync("private-key", "tunnel-id");
        client.Dispose();
        await connecting;
        await client.ConnectAsync("private-key", "tunnel-id");

        Assert.True(initializer.Killed);
        Assert.Equal(1, starts);
        Assert.Equal(TunnelStatus.NotConnected, client.Status);
    }

    [NativeCredentialFact]
    public void NativeStoreRoundTripsAnIsolatedCredential()
    {
        var store = new NativeCredentialStore("SIL.Motif/Test/" + Guid.NewGuid().ToString("N"));
        try
        {
            Assert.True(store.Write("temporary-test-key"));
            Assert.Equal("temporary-test-key", store.Read());
            store.Delete();
            Assert.Null(store.Read());
        }
        finally { store.Delete(); }
    }

    [Fact]
    public async Task McpProcessExitsWhenItsInputCloses()
    {
        var start = new ProcessStartInfo(SIL.Motif.Tests.TestFixtures.BuildOutput.Cli)
        {
            ArgumentList = { "mcp" }, RedirectStandardInput = true, RedirectStandardOutput = true,
            RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
        };
        start.Environment.Remove("ICU_DATA");
        using var process = Process.Start(start)!;
        var stderr = process.StandardError.ReadToEndAsync();
        try
        {
            process.StandardInput.Close();
            await UntilAsync(() => process.HasExited);
            Assert.True(process.ExitCode == 0, await stderr);
        }
        finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
    }

    [LinuxFact]
    public async Task TunnelClientConnectsThroughSupervisorAndForwardsStatus()
    {
        if (!OperatingSystem.IsLinux()) return;
        var directory = Path.Combine(Path.GetTempPath(), "motif-tunnel-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var executable = Path.Combine(directory, "fake-tunnel");
        await File.WriteAllTextAsync(executable,
            "#!/bin/sh\nif [ \"$1\" = init ]; then exit 0; fi\n" +
            "if [ \"$CONTROL_PLANE_API_KEY\" != private-key ]; then exit 1; fi\n" +
            "printf '%s\\n' '{\"msg\":\"poll cycle complete\"}'\nexec /bin/sleep 300\n");
        File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        try
        {
            using var client = new TunnelClient(executable, SIL.Motif.Tests.TestFixtures.BuildOutput.Cli, stateDirectory: directory);
            await client.ConnectAsync("private-key", "tunnel-id");
            await UntilAsync(() => client.Status is TunnelStatus.Connected or TunnelStatus.Error);
            Assert.Equal(TunnelStatus.Connected, client.Status);
            client.Disconnect();
            Assert.Equal(TunnelStatus.NotConnected, client.Status);
            Assert.Empty(Directory.EnumerateFiles(directory, "tunnel-*.json"));
        }
        finally { Directory.Delete(directory, true); }
    }

    [LinuxFact]
    public async Task SupervisorStopsTunnelWhenOwnerDies()
    {
        var directory = Path.Combine(Path.GetTempPath(), "motif-tunnel-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        using var owner = Process.Start("/bin/sleep", "300")!;
        using var supervisor = StartSupervisor(owner, directory, "/bin/sleep", "300");
        Process? tunnel = null;
        try
        {
            var record = await ReadRecordAsync(directory, supervisor);
            tunnel = Process.GetProcessById(record.TunnelPid);
            Assert.False(tunnel.HasExited);
            owner.Kill();
            await owner.WaitForExitAsync();
            await UntilAsync(() => supervisor.HasExited && tunnel.HasExited);
            Assert.Single(Directory.EnumerateFiles(directory, "tunnel-*.json"));
            new TunnelProcessRegistry(directory).ForgetGroup(record.OwnerPid, record.OwnerStart, record.Pgid, record.LeaderStart);
            Assert.Empty(Directory.EnumerateFiles(directory, "tunnel-*.json"));
        }
        finally
        {
            if (!supervisor.HasExited) supervisor.Kill(entireProcessTree: true);
            if (tunnel is not null) { if (!tunnel.HasExited) tunnel.Kill(entireProcessTree: true); tunnel.Dispose(); }
            if (!owner.HasExited) owner.Kill(entireProcessTree: true);
            Directory.Delete(directory, true);
        }
    }

    [LinuxFact]
    public async Task ClosingSupervisorInputStopsItsTunnel()
    {
        var directory = Path.Combine(Path.GetTempPath(), "motif-tunnel-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        using var owner = Process.GetCurrentProcess();
        using var supervisor = StartSupervisor(owner, directory, "/bin/sleep", "300");
        Process? tunnel = null;
        try
        {
            var record = await ReadRecordAsync(directory, supervisor);
            tunnel = Process.GetProcessById(record.TunnelPid);
            supervisor.StandardInput.Close();
            await UntilAsync(() => supervisor.HasExited && tunnel.HasExited);
            Assert.Single(Directory.EnumerateFiles(directory, "tunnel-*.json"));
            new TunnelProcessRegistry(directory).ForgetGroup(record.OwnerPid, record.OwnerStart, record.Pgid, record.LeaderStart);
            Assert.Empty(Directory.EnumerateFiles(directory, "tunnel-*.json"));
        }
        finally
        {
            if (!supervisor.HasExited) supervisor.Kill(entireProcessTree: true);
            if (tunnel is not null) { if (!tunnel.HasExited) tunnel.Kill(entireProcessTree: true); tunnel.Dispose(); }
            Directory.Delete(directory, true);
        }
    }

    [LinuxFact]
    public async Task RecoveryPreservesLiveOwnersAndFindsOrphansAcrossPathChanges()
    {
        if (!OperatingSystem.IsLinux()) return;
        var directory = Path.Combine(Path.GetTempPath(), "motif-tunnel-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var bundle = Path.Combine(directory, "tunnel-client");
        Directory.CreateDirectory(bundle);
        var executable = Path.Combine(bundle, "sleep");
        File.Copy("/bin/sleep", executable);
        File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        using var owner = Process.Start("/bin/sleep", "300")!;
        using var supervisor = StartSupervisor(owner, directory, executable, "300");
        TunnelProcessRecord? record = null;
        try
        {
            record = await ReadRecordAsync(directory, supervisor);
            using (var second = new TunnelClient("/new/location/tunnel-client", "/installed/motif", _ => new FakeProcess(true), directory))
                Assert.True(TunnelUnixGroup.HasMembers(record.Pgid));
            Assert.True(TunnelUnixGroup.Stop(record.Pgid, record.LeaderStart + 1));
            Assert.True(TunnelUnixGroup.HasMembers(record.Pgid));
            supervisor.Kill();
            await supervisor.WaitForExitAsync();
            owner.Kill();
            await owner.WaitForExitAsync();
            using (var next = new TunnelClient("/another/location/tunnel-client", "/installed/motif", _ => new FakeProcess(true), directory))
                Assert.False(TunnelUnixGroup.HasMembers(record.Pgid));
            Assert.Empty(Directory.EnumerateFiles(directory, "tunnel-*.json"));
        }
        finally
        {
            if (record is not null) TunnelUnixGroup.Stop(record.Pgid, record.LeaderStart, bundledDirectory: record.BundledDirectory);
            if (!supervisor.HasExited) supervisor.Kill(entireProcessTree: true);
            if (!owner.HasExited) owner.Kill(entireProcessTree: true);
            Directory.Delete(directory, true);
        }
    }

    // A saturated machine slows process starts 3-10x.
    private static readonly TimeSpan ProcessDeadline = TimeSpan.FromSeconds(120);

    private static Task UntilAsync(Func<bool> condition, string? why = null, TimeSpan? timeout = null, Func<string>? detail = null,
        [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(condition))] string conditionText = "") =>
        SIL.Motif.Tests.TestFixtures.Wait.UntilAsync(condition, why ?? "Timed out waiting for " + conditionText,
            timeout ?? ProcessDeadline, detail);

    [LinuxFact]
    public async Task KillingSupervisorThenDisconnectStopsItsEntireGroup()
    {
        if (!OperatingSystem.IsLinux()) return;
        var directory = Path.Combine(Path.GetTempPath(), "motif-tunnel-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var bundle = Path.Combine(directory, "tunnel-client");
        Directory.CreateDirectory(bundle);
        var shell = Path.Combine(bundle, "shell");
        var sleep = Path.Combine(bundle, "sleep");
        File.Copy("/bin/sh", shell);
        File.Copy("/bin/sleep", sleep);
        File.SetUnixFileMode(shell, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        File.SetUnixFileMode(sleep, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var executable = Path.Combine(bundle, "fake-tunnel");
        var marker = Path.Combine(directory, "child");
        await File.WriteAllTextAsync(executable,
             $"#!{shell}\nif [ \"$1\" = init ]; then exit 0; fi\n" +
            $"'{sleep}' 300 &\necho $! > '{marker}'\n" +
            "printf '%s\\n' '{\"msg\":\"poll cycle complete\"}'\nwait\n");
        File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        TunnelProcessRecord? record = null;
        try
        {
            using var client = new TunnelClient(executable, SIL.Motif.Tests.TestFixtures.BuildOutput.Cli, stateDirectory: directory);
            await client.ConnectAsync("private-key", "tunnel-id");
            // Two cold apphost starts come before the first line; measured 3-4x slower on a saturated machine.
            var started = System.Diagnostics.Stopwatch.StartNew();
            await UntilAsync(() => client.Status == TunnelStatus.Connected && File.Exists(marker),
                "the tunnel to report Connected and its child to write the marker", null,
                () => $"status {client.Status}, marker {(File.Exists(marker) ? "written" : "missing")}, after {started.Elapsed.TotalSeconds:F1} s");
            var path = Directory.EnumerateFiles(directory, "tunnel-*.json").Single();
            record = System.Text.Json.JsonSerializer.Deserialize<TunnelProcessRecord>(await File.ReadAllTextAsync(path))!;
            using var supervisor = Process.GetProcessById(record.Pgid);
            supervisor.Kill();
            await supervisor.WaitForExitAsync();
            client.Disconnect();
            await UntilAsync(() => !TunnelUnixGroup.HasMembers(record.Pgid),
                "the whole group to be gone after the supervisor was killed");
            Assert.Empty(Directory.EnumerateFiles(directory, "tunnel-*.json"));
        }
        finally
        {
            if (record is not null) TunnelUnixGroup.Stop(record.Pgid, record.LeaderStart, bundledDirectory: record.BundledDirectory);
            Directory.Delete(directory, true);
        }
    }

    [LinuxFact]
    public async Task ExitingTunnelRootKillsSurvivingChildBeforeRecordIsRemoved()
    {
        if (!OperatingSystem.IsLinux()) return;
        var directory = Path.Combine(Path.GetTempPath(), "motif-tunnel-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var executable = Path.Combine(directory, "fake-tunnel");
        var marker = Path.Combine(directory, "child");
        var release = Path.Combine(directory, "release");
        await File.WriteAllTextAsync(executable,
            "#!/bin/sh\n/bin/sh -c 'trap \"\" TERM; exec /bin/sleep 300' &\n" +
            $"echo $! > '{marker}'\nwhile [ ! -f '{release}' ]; do sleep 0.1; done\nexit 0\n");
        File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        using var owner = Process.GetCurrentProcess();
        using var supervisor = StartSupervisor(owner, directory, executable);
        TunnelProcessRecord? record = null;
        try
        {
            record = await ReadRecordAsync(directory, supervisor);
            await UntilAsync(() => File.Exists(marker));
            using var child = Process.GetProcessById(int.Parse(await File.ReadAllTextAsync(marker)));
            Assert.False(child.HasExited);
            new TunnelProcessRegistry(directory).ForgetGroup(record.OwnerPid, record.OwnerStart, record.Pgid, record.LeaderStart);
            Assert.Single(Directory.EnumerateFiles(directory, "tunnel-*.json"));
            await File.WriteAllTextAsync(release, string.Empty);
            await UntilAsync(() =>
                !TunnelProcessRegistry.IsOwnerAlive(record.TunnelPid, record.TunnelStart));
            Assert.Single(Directory.EnumerateFiles(directory, "tunnel-*.json"));
            await UntilAsync(() => supervisor.HasExited && !TunnelUnixGroup.HasMembers(record.Pgid));
            Assert.Single(Directory.EnumerateFiles(directory, "tunnel-*.json"));
            new TunnelProcessRegistry(directory).ForgetGroup(record.OwnerPid, record.OwnerStart, record.Pgid, record.LeaderStart);
            Assert.Empty(Directory.EnumerateFiles(directory, "tunnel-*.json"));
        }
        finally
        {
            if (record is not null) TunnelUnixGroup.Stop(record.Pgid, record.LeaderStart, bundledDirectory: record.BundledDirectory);
            if (!supervisor.HasExited) supervisor.Kill(entireProcessTree: true);
            Directory.Delete(directory, true);
        }
    }

    [LinuxFact]
    public async Task RecoveryDropsLeaderlessUnrelatedGroupWithoutSignallingIt()
    {
        if (!OperatingSystem.IsLinux()) return;
        var directory = Path.Combine(Path.GetTempPath(), "motif-tunnel-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var bundle = Path.Combine(directory, "tunnel-client");
        Directory.CreateDirectory(bundle);
        var marker = Path.Combine(directory, "child");
        var release = Path.Combine(directory, "release");
        var script = Path.Combine(directory, "leader.sh");
        await File.WriteAllTextAsync(script,
            $"/bin/sleep 300 &\necho $! > '{marker}'\nwhile [ ! -f '{release}' ]; do sleep 0.1; done\n");
        var start = new ProcessStartInfo("/usr/bin/setsid") { UseShellExecute = false };
        start.ArgumentList.Add("/bin/sh");
        start.ArgumentList.Add(script);
        start.Environment.Remove("ICU_DATA");
        using var leader = Process.Start(start)!;
        using var owner = Process.GetCurrentProcess();
        var registry = new TunnelProcessRegistry(directory);
        Process? child = null;
        try
        {
            var leaderStart = TunnelProcessIdentity.Start(leader);
            await UntilAsync(() => File.Exists(marker));
            child = Process.GetProcessById(int.Parse(await File.ReadAllTextAsync(marker)));
            registry.RecordLeader(owner.Id, TunnelProcessIdentity.Start(owner) + 1, leader.Id, leaderStart + 1,
                TunnelUnixGroup.CanonicalDirectory(bundle));
            await File.WriteAllTextAsync(release, string.Empty);
            await leader.WaitForExitAsync();
            Assert.False(child.HasExited);
            registry.Recover();
            Assert.False(child.HasExited);
            Assert.Empty(Directory.EnumerateFiles(directory, "tunnel-*.json"));
        }
        finally
        {
            if (!leader.HasExited) leader.Kill(entireProcessTree: true);
            if (child is not null) { if (!child.HasExited) child.Kill(); child.Dispose(); }
            Directory.Delete(directory, true);
        }
    }

    [LinuxFact]
    public async Task RecoveryStopsLeaderlessBundledMember()
    {
        if (!OperatingSystem.IsLinux()) return;
        var directory = Path.Combine(Path.GetTempPath(), "motif-tunnel-test-" + Guid.NewGuid().ToString("N"));
        var bundle = Path.Combine(directory, "tunnel-client");
        Directory.CreateDirectory(bundle);
        var executable = Path.Combine(bundle, "sleep");
        File.Copy("/bin/sleep", executable);
        File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var marker = Path.Combine(directory, "child");
        var unrelatedMarker = Path.Combine(directory, "unrelated");
        var release = Path.Combine(directory, "release");
        var script = Path.Combine(directory, "leader.sh");
        await File.WriteAllTextAsync(script,
            $"'{executable}' 300 &\necho $! > '{marker}'\n" +
            $"/bin/sleep 300 &\necho $! > '{unrelatedMarker}'\nwhile [ ! -f '{release}' ]; do sleep 0.1; done\n");
        var start = new ProcessStartInfo("/usr/bin/setsid") { UseShellExecute = false };
        start.ArgumentList.Add("/bin/sh");
        start.ArgumentList.Add(script);
        start.Environment.Remove("ICU_DATA");
        using var leader = Process.Start(start)!;
        using var owner = Process.GetCurrentProcess();
        var registry = new TunnelProcessRegistry(directory);
        Process? child = null;
        Process? unrelated = null;
        try
        {
            var leaderStart = TunnelProcessIdentity.Start(leader);
            await UntilAsync(() => File.Exists(marker) && File.Exists(unrelatedMarker));
            unrelated = Process.GetProcessById(int.Parse(await File.ReadAllTextAsync(unrelatedMarker)));
            child = Process.GetProcessById(int.Parse(await File.ReadAllTextAsync(marker)));
            await UntilAsync(() =>
                File.ResolveLinkTarget($"/proc/{child.Id}/exe", true)?.FullName == executable);
            registry.RecordLeader(owner.Id, TunnelProcessIdentity.Start(owner) + 1, leader.Id, leaderStart,
                TunnelUnixGroup.CanonicalDirectory(bundle));
            await File.WriteAllTextAsync(release, string.Empty);
            await leader.WaitForExitAsync();
            Assert.False(child.HasExited);
            registry.Recover();
            await UntilAsync(() => child.HasExited);
            Assert.False(unrelated.HasExited);
            Assert.Empty(Directory.EnumerateFiles(directory, "tunnel-*.json"));
        }
        finally
        {
            if (!leader.HasExited) leader.Kill(entireProcessTree: true);
            if (child is not null) { if (!child.HasExited) child.Kill(); child.Dispose(); }
            if (unrelated is not null) { if (!unrelated.HasExited) unrelated.Kill(); unrelated.Dispose(); }
            Directory.Delete(directory, true);
        }
    }

    [WindowsFact]
    public async Task KillingSupervisorClosesItsJobAndStopsTunnel()
    {
        var directory = Path.Combine(Path.GetTempPath(), "motif-tunnel-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        using var owner = Process.GetCurrentProcess();
        using var supervisor = StartSupervisor(owner, directory, "ping.exe", "-n", "300", "127.0.0.1");
        Process? tunnel = null;
        try
        {
            var record = await ReadRecordAsync(directory, supervisor);
            tunnel = Process.GetProcessById(record.TunnelPid);
            supervisor.Kill();
            await supervisor.WaitForExitAsync();
            await UntilAsync(() => tunnel.HasExited);
        }
        finally
        {
            if (!supervisor.HasExited) supervisor.Kill(entireProcessTree: true);
            if (tunnel is not null) { if (!tunnel.HasExited) tunnel.Kill(entireProcessTree: true); tunnel.Dispose(); }
            Directory.Delete(directory, true);
        }
    }

    private static Process StartSupervisor(Process owner, string directory, string executable, params string[] arguments)
    {
        var start = new ProcessStartInfo(SIL.Motif.Tests.TestFixtures.BuildOutput.Cli)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (var arg in new[] { "tunnel-supervise", "--owner-pid", owner.Id.ToString(), "--owner-start",
                     TunnelProcessIdentity.Start(owner).ToString(), "--executable", executable, "--state-dir", directory, "--" })
            start.ArgumentList.Add(arg);
        foreach (var arg in arguments) start.ArgumentList.Add(arg);
        start.Environment.Remove("ICU_DATA");
        return Process.Start(start)!;
    }

    private static async Task<TunnelProcessRecord> ReadRecordAsync(string directory, Process supervisor)
    {
        await UntilAsync(() =>
        {
            var path = Directory.EnumerateFiles(directory, "tunnel-*.json").FirstOrDefault();
            return supervisor.HasExited || path is not null &&
                System.Text.Json.JsonSerializer.Deserialize<TunnelProcessRecord>(File.ReadAllText(path))?.TunnelPid > 0;
        });
        Assert.False(supervisor.HasExited, supervisor.HasExited ? await supervisor.StandardError.ReadToEndAsync() : "Supervisor exited.");
        var path = Directory.EnumerateFiles(directory, "tunnel-*.json").Single();
        return System.Text.Json.JsonSerializer.Deserialize<TunnelProcessRecord>(await File.ReadAllTextAsync(path))!;
    }

    private sealed class FakeStore : ICredentialStore
    {
        public string? Value { get; private set; }
        public string? Read() => Value;
        public bool Write(string credential) { Value = credential; return false; }
        public void Delete() => Value = null;
    }

    private sealed class FakeTunnel : ITunnelClient
    {
        public TunnelStatus Status => TunnelStatus.NotConnected;
        public event Action<TunnelStatus>? StatusChanged { add { } remove { } }
        public (string, string) Connection { get; private set; }
        public bool Disconnected { get; private set; }
        public Task ConnectAsync(string key, string tunnelId) { Connection = (key, tunnelId); return Task.CompletedTask; }
        public void Disconnect() => Disconnected = true;
        public void Dispose() => Disconnect();
    }

    private sealed class FakeProcess : ITunnelProcess
    {
        private readonly TaskCompletionSource<int> _exit = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public FakeProcess(bool complete, string output = "")
        {
            StandardOutput = new StringReader(output);
            StandardError = new StringReader(string.Empty);
            if (complete) _exit.SetResult(0);
        }
        public TextReader StandardOutput { get; }
        public TextReader StandardError { get; }
        public bool Killed { get; private set; }
        public Task<int> WaitForExitAsync() => _exit.Task;
        public void Kill() { Killed = true; _exit.TrySetResult(1); }
        public void Dispose() { }
    }
}

internal sealed class LinuxFactAttribute : FactAttribute
{
    public LinuxFactAttribute() { if (!OperatingSystem.IsLinux()) Skip = "Requires Linux."; }
}

internal sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute() { if (!OperatingSystem.IsWindows()) Skip = "Requires Windows."; }
}

internal sealed partial class NativeCredentialFactAttribute : FactAttribute
{
    public NativeCredentialFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("MOTIF_TEST_CREDENTIAL_STORE_AVAILABLE") == "1") return;
        if (OperatingSystem.IsLinux())
        {
            if (!NativeLibrary.TryLoad("libsecret-1.so.0", out var library))
            {
                Skip = "libsecret is unavailable.";
                return;
            }
            NativeLibrary.Free(library);
            var service = SecretServiceGet(0, IntPtr.Zero, out var error);
            if (error != IntPtr.Zero) ErrorFree(error);
            if (service == IntPtr.Zero) Skip = "Secret Service is unavailable.";
            else ObjectUnref(service);
        }
        else if (OperatingSystem.IsMacOS())
        {
            if (KeychainCopyDefault(out var keychain) != 0) { Skip = "The default Keychain is unavailable."; return; }
            try
            {
                if (KeychainGetStatus(keychain, out var status) != 0 || (status & 1) == 0)
                    Skip = "The default Keychain is unavailable or locked.";
            }
            finally { Release(keychain); }
        }
        else if (!OperatingSystem.IsWindows()) Skip = "No credential store is available on this operating system.";
    }

    [LibraryImport("libsecret-1.so.0", EntryPoint = "secret_service_get_sync")]
    private static partial IntPtr SecretServiceGet(int flags, IntPtr cancellable, out IntPtr error);
    [LibraryImport("libglib-2.0.so.0", EntryPoint = "g_error_free")]
    private static partial void ErrorFree(IntPtr error);
    [LibraryImport("libgobject-2.0.so.0", EntryPoint = "g_object_unref")]
    private static partial void ObjectUnref(IntPtr value);
    [LibraryImport("/System/Library/Frameworks/Security.framework/Security", EntryPoint = "SecKeychainCopyDefault")]
    private static partial int KeychainCopyDefault(out IntPtr keychain);
    [LibraryImport("/System/Library/Frameworks/Security.framework/Security", EntryPoint = "SecKeychainGetStatus")]
    private static partial int KeychainGetStatus(IntPtr keychain, out uint status);
    [LibraryImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation", EntryPoint = "CFRelease")]
    private static partial void Release(IntPtr value);
}
