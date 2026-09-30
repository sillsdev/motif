using System.Diagnostics;
using SIL.Motif.Host.Installation;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.LibLcm.Installation;

public sealed class MotifUpdateGateTests
{
    private const string ChildGateNameVariable = "MOTIF_UPDATE_GATE_CHILD_NAME";
    private const string ChildReadyPathVariable = "MOTIF_UPDATE_GATE_CHILD_READY";
    private const string ChildReleasePathVariable = "MOTIF_UPDATE_GATE_CHILD_RELEASE";
    private static readonly TimeSpan ChildHangGuard = TimeSpan.FromMinutes(2);

    [Fact]
    public void ConcurrentActivitiesCanShareTheUpdateGate()
    {
        var gateName = UniqueGateName();
        using var first = MotifUpdateGate.TryAcquire(gateName);
        using var second = MotifUpdateGate.TryAcquire(gateName);
        Assert.NotNull(first);
        Assert.NotNull(second);
    }

    [Fact]
    public void UpdaterCannotEnterWhileAnActivityIsRunning()
    {
        var gateName = UniqueGateName();
        using var activity = MotifUpdateGate.TryAcquire(gateName);

        Assert.NotNull(activity);
        Assert.Null(MotifUpdateGate.TryAcquireForUpdate(gateName));
    }

    [Fact]
    public void ActivityCannotEnterWhileUpdaterHoldsTheGate()
    {
        var gateName = UniqueGateName();
        using var updater = MotifUpdateGate.TryAcquireForUpdate(gateName);

        Assert.NotNull(updater);
        Assert.Null(MotifUpdateGate.TryAcquire(gateName));
    }

    [Fact]
    public async Task ActivitiesCanShareTheGateAcrossProcesses()
    {
        if (Environment.GetEnvironmentVariable(ChildGateNameVariable) is { Length: > 0 } childGateName)
        {
            using var childActivity = MotifUpdateGate.TryAcquire(childGateName)
                ?? throw new InvalidOperationException("The child process could not acquire an activity lease.");
            var readyPath = Environment.GetEnvironmentVariable(ChildReadyPathVariable)
                ?? throw new InvalidOperationException("The child ready path is required.");
            var releasePath = Environment.GetEnvironmentVariable(ChildReleasePathVariable)
                ?? throw new InvalidOperationException("The child release path is required.");
            await File.WriteAllTextAsync(readyPath, string.Empty);
            await WaitUntilAsync(() => File.Exists(releasePath), TimeSpan.FromSeconds(15));
            return;
        }

        var gateName = UniqueGateName();
        var scratch = Path.Combine(Path.GetTempPath(), "MotifUpdateGate-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        var readyFile = Path.Combine(scratch, "ready");
        var releaseFile = Path.Combine(scratch, "release");
        var childStart = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        childStart.ArgumentList.Add("test");
        childStart.ArgumentList.Add(Path.Combine(FindRepositoryRoot(), "tests", "SIL.Motif.Tests.LibLcm",
            "SIL.Motif.Tests.LibLcm.csproj"));
        childStart.ArgumentList.Add("--configuration");
        childStart.ArgumentList.Add(new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name
            ?? throw new InvalidOperationException("The test configuration directory is missing."));
        childStart.ArgumentList.Add("--no-build");
        childStart.ArgumentList.Add("--no-restore");
        childStart.ArgumentList.Add("--filter");
        childStart.ArgumentList.Add("FullyQualifiedName=SIL.Motif.Tests.LibLcm.Installation.MotifUpdateGateTests.ActivitiesCanShareTheGateAcrossProcesses");
        // The child runs one named test, so it must not inherit this process's shard, which may exclude it.
        childStart.Environment.Remove(ShardedTestFramework.ShardVariable);
        childStart.Environment.Remove(ShardedTestFramework.WeightsVariable);
        childStart.Environment[ChildGateNameVariable] = gateName;
        childStart.Environment[ChildReadyPathVariable] = readyFile;
        childStart.Environment[ChildReleasePathVariable] = releaseFile;

        using var child = Process.Start(childStart)
            ?? throw new InvalidOperationException("Could not start the child test process.");
        var output = child.StandardOutput.ReadToEndAsync();
        var error = child.StandardError.ReadToEndAsync();
        try
        {
            await WaitForChildReadyAsync(child, readyFile, output, error, ChildHangGuard);
            using var parentActivity = MotifUpdateGate.TryAcquire(gateName);
            Assert.NotNull(parentActivity);
            Assert.Null(MotifUpdateGate.TryAcquireForUpdate(gateName));

            await File.WriteAllTextAsync(releaseFile, string.Empty);
            if (!await WaitForChildExitAsync(child, ChildHangGuard))
            {
                await KillChildTreeAndWaitForExitAsync(child);
                throw new TimeoutException(
                    $"Child test did not exit after release. {await ReadChildOutputAsync(output, error)}");
            }
            Assert.True(child.ExitCode == 0,
                $"Child test exited with code {child.ExitCode}. {await ReadChildOutputAsync(output, error)}");
        }
        finally
        {
            try
            {
                await File.WriteAllTextAsync(releaseFile, string.Empty);
            }
            finally
            {
                try
                {
                    await StopChildAsync(child);
                }
                finally
                {
                    if (Directory.Exists(scratch)) Directory.Delete(scratch, recursive: true);
                }
            }
        }
    }

    private static string UniqueGateName() => "SIL.Motif.UpdateActivity.Test." + Guid.NewGuid().ToString("N");

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Motif.sln"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not find Motif.sln above the test output directory.");
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Condition was not met in time.");
            await Task.Delay(10);
        }
    }

    private static async Task WaitForChildReadyAsync(
        Process child, string readyPath, Task<string> output, Task<string> error, TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!File.Exists(readyPath))
        {
            if (child.HasExited)
            {
                await child.WaitForExitAsync();
                throw new InvalidOperationException(
                    $"Child test exited with code {child.ExitCode} before signaling readiness. " +
                    await ReadChildOutputAsync(output, error));
            }
            if (stopwatch.Elapsed >= timeout)
            {
                throw new TimeoutException(
                    $"Child test did not signal readiness within {timeout}. " +
                    await ReadChildOutputAsync(output, error));
            }
            await Task.Delay(10);
        }
    }

    private static async Task<string> ReadChildOutputAsync(Task<string> output, Task<string> error)
    {
        var captures = Task.WhenAll(output, error);
        await Task.WhenAny(captures, Task.Delay(TimeSpan.FromSeconds(5)));
        _ = captures.Exception;
        return $"stdout: {CaptureResult(output)}{Environment.NewLine}stderr: {CaptureResult(error)}";
    }

    private static string CaptureResult(Task<string> capture)
    {
        if (capture.IsCompletedSuccessfully) return capture.Result;
        if (capture.IsFaulted) return $"<capture failed: {capture.Exception?.GetBaseException().Message}>";
        return "<capture did not complete before the diagnostic deadline>";
    }

    private static async Task<bool> WaitForChildExitAsync(Process child, TimeSpan timeout)
    {
        try
        {
            await child.WaitForExitAsync().WaitAsync(timeout);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    private static async Task StopChildAsync(Process child)
    {
        if (await WaitForChildExitAsync(child, TimeSpan.FromSeconds(15))) return;
        await KillChildTreeAndWaitForExitAsync(child);
    }

    private static async Task KillChildTreeAndWaitForExitAsync(Process child)
    {
        if (!child.HasExited)
        {
            try { child.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) when (child.HasExited) { }
        }
        await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
    }
}
