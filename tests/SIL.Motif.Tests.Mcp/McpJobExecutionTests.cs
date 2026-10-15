using System.Diagnostics;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Preferences;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Host.Parser;
using SIL.Motif.Host.Store;
using SIL.Motif.Mcp;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Mcp;

[Collection(LcmCacheParallelCollections.Group3)]
public sealed class McpJobExecutionTests(PristineProjectFixture pristine)
{
    [Fact]
    public async Task StdioMcpStartsItsSiblingRunnerAndReturnsACompletedDryRun()
    {
        using var project = await CreateKnownProjectAsync(pristine);
        var rootNamespace = "motif-mcp-root-isolation-" + Guid.NewGuid().ToString("N");
        await using var firstRootOwner = await RunningOwner.StartAsync(
            Path.Combine(project.ManagedRoot, "first-root"), rootNamespace);
        await using var session = await McpProcessSession.StartAsync(project.ManagedRoot, null, rootNamespace);

        var proposal = await session.CallAsync("motif_start_proposal", project.FwDataPath,
            ("draft", "empty-draft"), ("label", "Check an empty Draft"), ("rationale", "No operations are expected."));
        var proposalId = proposal.GetProperty("result").GetProperty("proposalId").GetString()!;
        var dryRun = await session.CallAsync("motif_dry_run", project.FwDataPath,
            ("proposal", proposalId), ("wait_seconds", 60));
        Assert.True(dryRun.GetProperty("ok").GetBoolean(), dryRun.ToString());
        Assert.False(dryRun.GetProperty("result").TryGetProperty("status", out var status) &&
            status.GetString() == "running", dryRun.ToString());

        var result = dryRun.GetProperty("result");
        Assert.True(result.TryGetProperty("evidenceCurrent", out var evidenceCurrent), dryRun.ToString());
        Assert.True(evidenceCurrent.GetBoolean(), dryRun.ToString());
        Assert.False(firstRootOwner.Process.HasExited,
            "The first root's runner exited before the MCP Dry Run completed.");
    }

    [Fact]
    public async Task ADryRunWhoseRunnerCannotStartSaysSoInsteadOfWaitingForever()
    {
        using var project = await CreateKnownProjectAsync(pristine);
        var missing = Path.Combine(project.ManagedRoot, "absent", "SIL.Motif.Worker");
        await using var session = await McpProcessSession.StartAsync(project.ManagedRoot, missing);

        var proposal = await session.CallAsync("motif_start_proposal", project.FwDataPath,
            ("draft", "empty-draft"), ("label", "Check an empty Draft"), ("comment", "No operations are expected."));
        var proposalId = proposal.GetProperty("result").GetProperty("proposalId").GetString()!;
        var dryRun = await session.CallAsync("motif_dry_run", project.FwDataPath,
            ("proposal", proposalId), ("wait_seconds", 1));

        var result = dryRun.GetProperty("result");
        Assert.Equal("running", result.GetProperty("status").GetString());
        Assert.Contains("does not exist", result.GetProperty("runnerWarning").GetString(), StringComparison.Ordinal);
    }

    private static Task<WalkthroughProject> CreateKnownProjectAsync(PristineProjectFixture pristine)
    {
        var project = new WalkthroughProject(pristine);
        Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(project.FwDataPath), project.ManagedRoot).Succeeded);
        Assert.True(ProposalCommands.Open(new OpenRequest(project.FwDataPath)).Succeeded);
        using (var machine = MachineDatabase.Open(project.ManagedRoot))
            new KnownProjectRegistry(machine).Record(ProjectWorkspaceKey.Compute(
                new ProjectLocator(project.FwDataPath, Path.GetFileNameWithoutExtension(project.FwDataPath))),
                project.FwDataPath, DateTimeOffset.UtcNow);
        return Task.FromResult(project);
    }

    private sealed class McpProcessSession : IAsyncDisposable
    {
        private readonly Process _process;
        private readonly Task<string> _stderr;

        private McpProcessSession(Process process, McpClient client)
        {
            _process = process;
            Client = client;
            _stderr = process.StandardError.ReadToEndAsync();
        }

        public McpClient Client { get; }

        public static async Task<McpProcessSession> StartAsync(string root, string? workerExecutable,
            string? ownerNamespace = null)
        {
            var preferencePath = Path.Combine(root, "advanced-ai-mode.json");
            new FileAdvancedAiModePreferenceStore(preferencePath).SetEnabled(true);
            var start = new ProcessStartInfo(BuildOutput.Cli)
            {
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            start.ArgumentList.Add("mcp");
            start.Environment[RunnerOptions.RootVariable] = root;
            start.Environment[RunnerOptions.NamespaceVariable] = ownerNamespace ??
                "motif-mcp-job-" + Guid.NewGuid().ToString("N");
            start.Environment[RunnerOptions.IdleVariable] = "1";
            start.Environment[RunnerOptions.LeaseVariable] = "1";
            start.Environment[PanGlossExecutable.PathVariable] = FakeParser.ExecutablePath;
            start.Environment[FileAdvancedAiModePreferenceStore.PathEnvironmentVariable] = preferencePath;
            start.Environment.Remove(ProcessRunnerLauncher.SuppressVariable);
            start.Environment.Remove(ProcessRunnerLauncher.ExecutableVariable);
            if (workerExecutable is not null)
                start.Environment[ProcessRunnerLauncher.ExecutableVariable] = workerExecutable;
            start.Environment.Remove("ICU_DATA");

            var process = Process.Start(start) ?? throw new InvalidOperationException("motif mcp did not start.");
            try
            {
                var client = await McpClient.CreateAsync(new StreamClientTransport(
                    process.StandardInput.BaseStream, process.StandardOutput.BaseStream));
                return new McpProcessSession(process, client);
            }
            catch
            {
                process.Kill(entireProcessTree: true);
                process.Dispose();
                throw;
            }
        }

        public async Task<JsonElement> CallAsync(string tool, string project, params (string Name, object? Value)[] arguments)
        {
            var input = arguments.ToDictionary(pair => pair.Name, pair => pair.Value);
            input["project"] = project;
            var response = await Client.CallToolAsync(tool, input);
            return response.StructuredContent!.Value;
        }

        public async ValueTask DisposeAsync()
        {
            await Client.DisposeAsync();
            _process.StandardInput.Close();
            try { await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15)); }
            catch (TimeoutException)
            {
                _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync();
            }
            var stderr = await _stderr;
            Assert.True(_process.ExitCode == 0, "motif mcp exited " + _process.ExitCode + ". stderr: " + stderr);
            _process.Dispose();
        }
    }

    private sealed class RunningOwner(Process process) : IAsyncDisposable
    {
        public Process Process { get; } = process;

        public static async Task<RunningOwner> StartAsync(string root, string ownerNamespace)
        {
            Directory.CreateDirectory(root);
            var start = new ProcessStartInfo(BuildOutput.Worker)
            {
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            start.ArgumentList.Add(RunnerOptions.RootArgument);
            start.ArgumentList.Add(root);
            start.ArgumentList.Add(RunnerOptions.NamespaceArgument);
            start.ArgumentList.Add(ownerNamespace);
            start.ArgumentList.Add(RunnerOptions.NoParserArgument);
            start.ArgumentList.Add(RunnerOptions.IdleArgument);
            start.ArgumentList.Add("300000");
            var process = Process.Start(start) ?? throw new InvalidOperationException("The Worker did not start.");
            try
            {
                var owner = await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(30));
                Assert.False(string.IsNullOrWhiteSpace(owner), "The Worker did not report acquiring ownership.");
                _ = process.StandardOutput.ReadToEndAsync();
                _ = process.StandardError.ReadToEndAsync();
                return new RunningOwner(process);
            }
            catch
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                process.Dispose();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (!Process.HasExited) Process.Kill(entireProcessTree: true);
            await Process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Process.Dispose();
        }
    }
}
