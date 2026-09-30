using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using SIL.Motif.Commands;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker;
using Xunit;

namespace SIL.Motif.Tests.Integration;

[Collection(LcmCacheTestCollection.Name)]
public sealed class PortableWorkerPackageTests(PristineProjectFixture projects)
{
    private static readonly TimeSpan PublishBound = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan CliBound = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan ProcessCleanupBound = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task PortablePackageRunsCliAndSiblingWorkerWithTheRuntimeLibrary()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "motif-portable-worker-" + Guid.NewGuid().ToString("N"));
        var workspacePath = Path.GetFullPath(workspace);
        var package = Path.Combine(workspacePath, "package");
        var suffix = OperatingSystem.IsWindows() ? ".exe" : "";
        var workerHost = Path.Combine(package, "SIL.Motif.Worker" + suffix);
        var ownerNamespace = "portable-" + Guid.NewGuid().ToString("N");
        Exception? testFailure = null;

        try
        {
            Directory.CreateDirectory(workspacePath);
            var workerPublish = Path.Combine(workspacePath, "worker-publish");
            var intermediateRoot = Path.Combine(workspacePath, "intermediate");
            var workerBuildRoot = Path.Combine(workspacePath, "worker-build") + Path.DirectorySeparatorChar;
            Directory.CreateDirectory(package);

            var repoRoot = FindRepoRoot();
            var rid = RuntimeIdentifier();
            await PublishAsync(Path.Combine(repoRoot, "src", "SIL.Motif.App", "SIL.Motif.App.csproj"),
                package, rid, intermediateRoot, null);
            Assert.True(File.Exists(Path.Combine(package, "SIL.Motif.Worker.Runtime.dll")),
                "The App publish did not include the reusable Worker runtime library.");
            await PublishAsync(Path.Combine(repoRoot, "src", "SIL.Motif.Cli", "SIL.Motif.Cli.csproj"),
                package, rid, intermediateRoot, null);
            Assert.True(File.Exists(Path.Combine(package, "SIL.Motif.Worker.Runtime.dll")),
                "The CLI publish did not include the reusable Worker runtime library.");
            await PublishAsync(Path.Combine(repoRoot, "src", "SIL.Motif.Worker", "SIL.Motif.Worker.csproj"),
                workerPublish, rid, intermediateRoot, workerBuildRoot);

            var workerAssets = new[]
            {
                "SIL.Motif.Worker" + suffix,
                "SIL.Motif.Worker.dll",
                "SIL.Motif.Worker.deps.json",
                "SIL.Motif.Worker.runtimeconfig.json",
            };

            foreach (var frontEndExcludedAsset in new[]
            {
                "SIL.Motif.Worker" + suffix,
                "SIL.Motif.Worker.dll",
                "SIL.Motif.Worker.deps.json",
                "SIL.Motif.Worker.runtimeconfig.json",
            })
                Assert.False(File.Exists(Path.Combine(package, frontEndExcludedAsset)),
                    "A front-end publish unexpectedly included " + frontEndExcludedAsset + ".");
            Assert.True(File.Exists(Path.Combine(workerPublish, "SIL.Motif.Worker.Runtime.dll")),
                "The Worker publish did not include its runtime library dependency.");
            foreach (var asset in workerAssets)
            {
                var source = Path.Combine(workerPublish, asset);
                Assert.True(File.Exists(source), "The Worker publish is missing " + asset + ".");
                var destination = Path.Combine(package, asset);
                File.Copy(source, destination, overwrite: true);
                if (asset == "SIL.Motif.Worker" + suffix && !OperatingSystem.IsWindows())
                    File.SetUnixFileMode(destination, UnixFileMode.UserRead | UnixFileMode.UserWrite |
                        UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                        UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            }

            var appHost = Path.Combine(package, "SIL.Motif.App" + suffix);
            var cliHost = Path.Combine(package, "motif" + suffix);
            Assert.True(File.Exists(appHost), "The portable package is missing the App apphost.");
            Assert.True(File.Exists(cliHost), "The portable package is missing the CLI apphost.");
            Assert.True(File.Exists(workerHost), "The portable package is missing the Worker apphost.");
            Assert.Equal(Path.GetDirectoryName(cliHost), Path.GetDirectoryName(workerHost));
            AssertSelfContained(package, "SIL.Motif.App.runtimeconfig.json");
            AssertSelfContained(package, "motif.runtimeconfig.json");
            AssertSelfContained(package, "SIL.Motif.Worker.runtimeconfig.json");

            var project = projects.CopyProjectFile();
            var root = Path.Combine(workspacePath, "runner-root");
            var run = await RunCliAsync(cliHost, project, root, ownerNamespace);
            Assert.True(run.ExitCode == 0, run.Error);

            var jobId = run.Output.Trim();
            Assert.False(string.IsNullOrWhiteSpace(jobId));
            var completed = JobProgress.WaitUntilFinished(project, jobId,
                "The packaged CLI's sibling Worker Baseline refresh");
            Assert.Equal(JobStatus.Completed, completed.Status);
            await WaitForWorkerExitAsync(workerHost, ownerNamespace);
        }
        catch (Exception exception)
        {
            testFailure = exception;
            throw;
        }
        finally
        {
            Exception? cleanupFailure = null;
            try { await StopPackagedWorkerAsync(workerHost); }
            catch (Exception exception) { cleanupFailure = exception; }
            try { DeleteTemporaryDirectory(workspacePath); }
            catch (Exception exception) { cleanupFailure ??= exception; }
            if (testFailure is null && cleanupFailure is not null)
                throw cleanupFailure;
        }
    }

    private static async Task PublishAsync(string projectPath, string outputPath, string rid,
        string intermediateRoot, string? workerBuildRoot)
    {
        Directory.CreateDirectory(outputPath);
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = FindRepoRoot(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        var arguments = new List<string>
        {
            "publish", projectPath, "--configuration", "Release", "--runtime", rid,
            "--self-contained", "true", "--output", outputPath,
            "-p:MotifPortablePackage=true", "--nologo",
        };
        if (workerBuildRoot is not null) arguments.Add("-p:MotifBinRoot=" + workerBuildRoot);
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        start.Environment["MOTIF_PACKAGE_INTERMEDIATE_ROOT"] = intermediateRoot;
        start.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        start.Environment["UseSharedCompilation"] = "false";
        start.Environment["AVALONIA_TELEMETRY_OPTOUT"] = "1";

        using var process = Process.Start(start) ?? throw new InvalidOperationException("dotnet publish did not start.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync(PublishBound);
        }
        catch (TimeoutException)
        {
            var cleanup = await StopProcessAndDrainAsync(process, standardOutput, standardError);
            throw new Xunit.Sdk.XunitException(
                "Portable publish exceeded " + PublishBound + "." + Environment.NewLine +
                cleanup.StandardOutput + Environment.NewLine + cleanup.StandardError +
                (cleanup.Exited ? "" : Environment.NewLine + "The publish process did not exit after termination."));
        }

        var output = await standardOutput;
        var error = await standardError;
        Assert.True(process.ExitCode == 0,
            "dotnet publish failed for " + Path.GetFileName(projectPath) + "." + Environment.NewLine +
            output + Environment.NewLine + error);
    }

    private static async Task<CliResult> RunCliAsync(string executable, string project, string root,
        string ownerNamespace)
    {
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = Path.GetDirectoryName(executable)!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add("baseline-refresh");
        start.ArgumentList.Add("--project");
        start.ArgumentList.Add(project);
        start.Environment[RunnerOptions.RootVariable] = root;
        start.Environment[RunnerOptions.NamespaceVariable] = ownerNamespace;
        start.Environment[RunnerOptions.IdleVariable] = "1";
        start.Environment.Remove(ProcessRunnerLauncher.ExecutableVariable);
        start.Environment.Remove(ProcessRunnerLauncher.SuppressVariable);

        using var process = Process.Start(start) ?? throw new InvalidOperationException("The packaged CLI did not start.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync(CliBound);
        }
        catch (TimeoutException)
        {
            var cleanup = await StopProcessAndDrainAsync(process, standardOutput, standardError);
            throw new Xunit.Sdk.XunitException(
                "The packaged CLI exceeded " + CliBound + "." + Environment.NewLine +
                cleanup.StandardOutput + Environment.NewLine + cleanup.StandardError +
                (cleanup.Exited ? "" : Environment.NewLine + "The CLI process did not exit after termination."));
        }

        return new CliResult(process.ExitCode, await standardOutput, await standardError);
    }

    private static async Task WaitForWorkerExitAsync(string executable, string ownerNamespace)
    {
        using var owner = JobRunnerHost.ForNamespace(ownerNamespace);
        var stopwatch = Stopwatch.StartNew();
        while (!owner.TryAcquireOwnership())
        {
            if (stopwatch.Elapsed > CliBound)
                Assert.Fail("The packaged Worker did not release its runner ownership lock.");
            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }

        var expectedPath = Path.GetFullPath(executable);
        var processName = Path.GetFileNameWithoutExtension(executable);
        while (WorkerProcessIsRunning(processName, expectedPath))
        {
            if (stopwatch.Elapsed > CliBound)
                Assert.Fail("The packaged Worker released ownership but did not exit.");
            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }
    }

    private static bool WorkerProcessIsRunning(string processName, string expectedPath)
    {
        foreach (var process in Process.GetProcessesByName(processName))
        {
            using (process)
            {
                try
                {
                    if (process.MainModule is { FileName: { } path } &&
                        string.Equals(Path.GetFullPath(path), expectedPath,
                            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                        return true;
                }
                catch (System.ComponentModel.Win32Exception)
                {
                }
                catch (InvalidOperationException)
                {
                }
            }
        }
        return false;
    }

    private static async Task StopPackagedWorkerAsync(string executable)
    {
        if (!File.Exists(executable)) return;
        var expectedPath = Path.GetFullPath(executable);
        var processName = Path.GetFileNameWithoutExtension(executable);
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < ProcessCleanupBound)
        {
            var process = FindWorkerProcess(processName, expectedPath);
            if (process is null) return;
            using (process)
            {
                try
                {
                    if (!process.HasExited && IsExactWorkerProcess(process, expectedPath))
                        process.Kill();
                }
                catch (InvalidOperationException)
                {
                }
                catch (System.ComponentModel.Win32Exception exception)
                {
                    throw new InvalidOperationException("Could not stop the private packaged Worker process.", exception);
                }

                var remaining = ProcessCleanupBound - stopwatch.Elapsed;
                if (remaining > TimeSpan.Zero)
                {
                    try { await process.WaitForExitAsync().WaitAsync(remaining); }
                    catch (TimeoutException) { }
                }
            }
            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }
        if (FindWorkerProcess(processName, expectedPath) is { } remainingProcess)
        {
            remainingProcess.Dispose();
            throw new TimeoutException("The private packaged Worker did not exit within " + ProcessCleanupBound + ".");
        }
    }

    private static Process? FindWorkerProcess(string processName, string expectedPath)
    {
        foreach (var process in Process.GetProcessesByName(processName))
        {
            if (IsExactWorkerProcess(process, expectedPath)) return process;
            process.Dispose();
        }
        return null;
    }

    private static bool IsExactWorkerProcess(Process process, string expectedPath)
    {
        try
        {
            return process.MainModule is { FileName: { } path } &&
                string.Equals(Path.GetFullPath(path), expectedPath,
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static async Task<ProcessCleanup> StopProcessAndDrainAsync(Process process,
        Task<string> standardOutput, Task<string> standardError)
    {
        var exitedTask = WaitForProcessExitAsync(process);
        var outputTask = DrainPipeAsync(standardOutput, "stdout");
        var errorTask = DrainPipeAsync(standardError, "stderr");
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
        await Task.WhenAll(exitedTask, outputTask, errorTask);
        return new ProcessCleanup(exitedTask.Result, outputTask.Result, errorTask.Result);
    }

    private static async Task<bool> WaitForProcessExitAsync(Process process)
    {
        try
        {
            await process.WaitForExitAsync().WaitAsync(ProcessCleanupBound);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    private static async Task<string> DrainPipeAsync(Task<string> read, string name)
    {
        try
        {
            return await read.WaitAsync(ProcessCleanupBound);
        }
        catch (TimeoutException)
        {
            return "(" + name + " did not close within " + ProcessCleanupBound + ")";
        }
    }

    private static string RuntimeIdentifier()
    {
        var architecture = RuntimeInformation.ProcessArchitecture;
        if (OperatingSystem.IsWindows() && architecture == Architecture.X64) return "win-x64";
        if (OperatingSystem.IsLinux() && architecture == Architecture.X64) return "linux-x64";
        if (OperatingSystem.IsMacOS() && architecture == Architecture.Arm64) return "osx-arm64";
        if (OperatingSystem.IsMacOS() && architecture == Architecture.X64) return "osx-x64";
        throw new PlatformNotSupportedException("No portable package RID is defined for this platform.");
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Motif.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Motif.sln was not found above the test output.");
    }

    private static void AssertSelfContained(string package, string runtimeConfigName)
    {
        var path = Path.Combine(package, runtimeConfigName);
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        Assert.True(document.RootElement.GetProperty("runtimeOptions")
            .TryGetProperty("includedFrameworks", out var frameworks) && frameworks.GetArrayLength() > 0,
            "The portable apphost is missing included frameworks: " + runtimeConfigName);
    }

    private static void DeleteTemporaryDirectory(string path)
    {
        var temp = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
        var root = Path.GetFullPath(path);
        var prefix = temp + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!root.StartsWith(prefix, comparison))
            throw new InvalidOperationException("Refusing to delete a path outside the temporary root: " + root);
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    private sealed record CliResult(int ExitCode, string Output, string Error);
    private sealed record ProcessCleanup(bool Exited, string StandardOutput, string StandardError);
}
