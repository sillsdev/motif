using System.Diagnostics;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker;
using Xunit;

namespace SIL.Motif.Tests.Worker;

public sealed class WorkerStartupTests
{
    [Fact]
    public async Task WorkerStartsBeforeTheBundledIcuPayloadIsNeeded()
    {
        var staging = Path.Combine(Path.GetTempPath(), "motif-worker-no-icu-" + Guid.NewGuid().ToString("N"));
        var app = Path.Combine(staging, "app");
        var root = Path.Combine(staging, "state");
        using var stagedFiles = new StagedWorkerFiles(staging);
        Directory.CreateDirectory(app);

        foreach (var file in Directory.EnumerateFiles(BuildOutput.ProductDirectory))
        {
            if (Path.GetFileName(file).StartsWith("libicu", StringComparison.OrdinalIgnoreCase)) continue;
            File.Copy(file, Path.Combine(app, Path.GetFileName(file)));
        }

        var worker = Path.Combine(app, Path.GetFileName(BuildOutput.Worker));
        Assert.True(File.Exists(worker), "The worker apphost was not copied into the isolated output.");

        var start = new ProcessStartInfo(worker)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = app,
        };
        start.Environment.Remove("ICU_DATA");
        start.ArgumentList.Add(RunnerOptions.RootArgument);
        start.ArgumentList.Add(root);
        start.ArgumentList.Add(RunnerOptions.NamespaceArgument);
        start.ArgumentList.Add("motif-no-icu-" + Guid.NewGuid().ToString("N"));
        start.ArgumentList.Add(RunnerOptions.IdleArgument);
        start.ArgumentList.Add("1000");
        start.ArgumentList.Add(RunnerOptions.NoParserArgument);

        using var process = Process.Start(start) ?? throw new InvalidOperationException("The isolated job runner did not start.");
        stagedFiles.MarkChildStarted();
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        var exitLimit = Wait.Scaled(TimeSpan.FromSeconds(15));
        if (!stagedFiles.WaitForRetirement(process.WaitForExit, process.Kill, exitLimit))
            Assert.Fail("The isolated job runner did not retire when idle. " + await error);

        var stderr = await error;
        Assert.True(process.ExitCode == 0, $"The job runner exited with {process.ExitCode}. {stderr}");
        var ready = (await output).Trim();
        Assert.True(ready.Length > 0 && !ready.StartsWith("existing runner:", StringComparison.Ordinal),
            $"The isolated job runner did not announce ownership. stdout: {ready}. stderr: {stderr}");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FailedTerminationLeavesTheStagedWorkerFilesInPlace(bool killThrows)
    {
        var staging = Directory.CreateTempSubdirectory("motif-worker-cleanup-").FullName;
        try
        {
            var library = Path.Combine(staging, "native.dll");
            File.WriteAllText(library, "staged worker dependency");
            var killError = new InvalidOperationException("Could not terminate the worker.");
            var failure = Record.Exception(() =>
            {
                using var stagedFiles = new StagedWorkerFiles(staging);
                stagedFiles.MarkChildStarted();
                stagedFiles.WaitForRetirement(_ => false, () => { if (killThrows) throw killError; }, TimeSpan.Zero);
            });

            if (killThrows)
                Assert.Same(killError, failure);
            else
                Assert.Equal("The isolated job runner did not exit after being killed.",
                    Assert.IsType<TimeoutException>(failure).Message);
            Assert.True(File.Exists(library), "Staging must survive until the child exit is confirmed.");
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
    }

    private sealed class StagedWorkerFiles(string path) : IDisposable
    {
        private bool _childMayStillBeRunning;

        public void MarkChildStarted() => _childMayStillBeRunning = true;

        public bool WaitForRetirement(Func<TimeSpan, bool> waitForExit, Action kill, TimeSpan exitLimit)
        {
            // Wait for the process handle before deleting its native DLLs; HasExited can use the exit code.
            var retired = waitForExit(exitLimit);
            if (!retired)
            {
                kill();
                if (!waitForExit(exitLimit))
                    throw new TimeoutException("The isolated job runner did not exit after being killed.");
            }

            _childMayStillBeRunning = false;
            return retired;
        }

        public void Dispose()
        {
            if (!_childMayStillBeRunning && Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
    }
}
