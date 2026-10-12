using System.Diagnostics;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Worker;

public sealed class UnixWorkerLockFactAttribute : FactAttribute
{
    public UnixWorkerLockFactAttribute()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
            Skip = "This test exercises flock-based worker locks, which are available only on Linux and macOS.";
    }
}

public sealed class UnixWorkerMutexTests
{
    private const string LockNameVariable = "MOTIF_UNIX_WORKER_LOCK_TEST_NAME";
    private const string ReadyPathVariable = "MOTIF_UNIX_WORKER_LOCK_TEST_READY";
    private const string StartPathVariable = "MOTIF_UNIX_WORKER_LOCK_TEST_START";
    private const string OccupancyPathVariable = "MOTIF_UNIX_WORKER_LOCK_TEST_OCCUPANCY";
    private const string ClearRegistryNamespaceVariable = "MOTIF_UNIX_WORKER_LOCK_TEST_CLEAR_REGISTRY_NAMESPACE";
    private const string IterationsVariable = "MOTIF_UNIX_WORKER_LOCK_TEST_ITERATIONS";

    [UnixWorkerLockFact]
    public async Task ReleaseKeepsLockFilesAndConcurrentProcessesNeverOverlap()
    {
        if (Environment.GetEnvironmentVariable(LockNameVariable) is { Length: > 0 } childLockName)
        {
            await RunCompetingWorkerAsync(childLockName,
                Environment.GetEnvironmentVariable(ReadyPathVariable)!,
                Environment.GetEnvironmentVariable(StartPathVariable)!,
                Environment.GetEnvironmentVariable(OccupancyPathVariable)!);
            return;
        }

        var lockName = "MotifWorkerLockCleanupTest-" + Guid.NewGuid().ToString("N");
        var lockPath = UnixFileLock.GetLockPath(lockName, machineWide: true);
        using var lockFileCleanup = new DeleteLockFileOnDispose(lockPath);
        Assert.False(File.Exists(lockPath));

        using (var owner = new WorkerMutexOwner(lockName, machineWide: true))
        {
            for (var index = 0; index < 20; index++)
            {
                Assert.True(owner.TryAcquire());
                Assert.True(File.Exists(lockPath));
                owner.Release();
                Assert.True(File.Exists(lockPath));
            }
        }

        var scratch = Path.Combine(Path.GetTempPath(), "MotifWorkerLockCleanup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        var readyPath = Path.Combine(scratch, "ready");
        var startPath = Path.Combine(scratch, "start");
        var occupancyPath = Path.Combine(scratch, "occupied");
        var projectPath = Path.Combine(FindRepositoryRoot(), "tests", "SIL.Motif.Tests.Worker",
            "SIL.Motif.Tests.Worker.csproj");
        var childStart = DotnetTestProcess.CreateTestStartInfo(projectPath,
            "FullyQualifiedName=SIL.Motif.Tests.Worker.UnixWorkerMutexTests.ReleaseKeepsLockFilesAndConcurrentProcessesNeverOverlap");
        childStart.Environment[LockNameVariable] = lockName;
        childStart.Environment[ReadyPathVariable] = readyPath;
        childStart.Environment[StartPathVariable] = startPath;
        childStart.Environment[OccupancyPathVariable] = occupancyPath;

        using var child = Process.Start(childStart)
            ?? throw new InvalidOperationException("Could not start the lock contention test process.");
        var output = child.StandardOutput.ReadToEndAsync();
        var error = child.StandardError.ReadToEndAsync();
        try
        {
            await WaitForFileAsync(readyPath, child);
            await File.WriteAllTextAsync(startPath, string.Empty);
            await RunCompetingWorkerAsync(lockName, Path.Combine(scratch, "parent-ready"), startPath, occupancyPath);
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));

            Assert.True(child.ExitCode == 0, $"Child test failed. stdout: {await output} stderr: {await error}");
            Assert.True(File.Exists(lockPath));
            Assert.False(File.Exists(occupancyPath));
        }
        finally
        {
            await File.WriteAllTextAsync(startPath, string.Empty);
            if (!child.HasExited)
            {
                try
                {
                    await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                }
                catch (TimeoutException)
                {
                    child.Kill(entireProcessTree: true);
                    await child.WaitForExitAsync();
                }
            }
            if (File.Exists(occupancyPath)) File.Delete(occupancyPath);
            Directory.Delete(scratch, recursive: true);
        }
    }

    [UnixWorkerLockFact]
    public async Task ProcessesWithDifferentRegistriesCanReacquireOneLockFile()
    {
        if (Environment.GetEnvironmentVariable(LockNameVariable) is { Length: > 0 } childLockName)
        {
            if (Environment.GetEnvironmentVariable(ClearRegistryNamespaceVariable) == "1")
                Environment.SetEnvironmentVariable(WorkerLockPaths.TestRegistryNamespaceVariable, null);
            await RunLockStressWorkerAsync(childLockName,
                Environment.GetEnvironmentVariable(ReadyPathVariable)!,
                Environment.GetEnvironmentVariable(StartPathVariable)!);
            return;
        }

        var lockName = "MotifWorkerLockRegistryRaceTest-" + Guid.NewGuid().ToString("N");
        var lockPath = UnixFileLock.GetLockPath(lockName, machineWide: true);
        var scratch = Path.Combine(Path.GetTempPath(), "MotifWorkerLockRegistryRace-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        var startPath = Path.Combine(scratch, "start");
        const int childCount = 3;
        const int iterations = 20_000;
        var projectPath = Path.Combine(FindRepositoryRoot(), "tests", "SIL.Motif.Tests.Worker",
            "SIL.Motif.Tests.Worker.csproj");
        var children = new List<(Process Process, Task<string> Output, Task<string> Error)>();

        try
        {
            for (var index = 0; index < childCount; index++)
            {
                var childStart = DotnetTestProcess.CreateTestStartInfo(projectPath,
                    "FullyQualifiedName=SIL.Motif.Tests.Worker.UnixWorkerMutexTests.ProcessesWithDifferentRegistriesCanReacquireOneLockFile");
                childStart.Environment[LockNameVariable] = lockName;
                childStart.Environment[ReadyPathVariable] = Path.Combine(scratch, $"ready-{index}");
                childStart.Environment[StartPathVariable] = startPath;
                childStart.Environment[IterationsVariable] = iterations.ToString();
                if (index == childCount - 1 &&
                    Environment.GetEnvironmentVariable(ClearRegistryNamespaceVariable) != "0")
                    childStart.Environment[ClearRegistryNamespaceVariable] = "1";

                var child = Process.Start(childStart)
                    ?? throw new InvalidOperationException("Could not start the lock stress process.");
                children.Add((child, child.StandardOutput.ReadToEndAsync(), child.StandardError.ReadToEndAsync()));
            }

            for (var index = 0; index < children.Count; index++)
                await WaitForFileAsync(Path.Combine(scratch, $"ready-{index}"), children[index].Process);

            var parentWorker = RunLockStressWorkerAsync(lockName, Path.Combine(scratch, "parent-ready"), startPath);
            await WaitForFileAsync(Path.Combine(scratch, "parent-ready"), children[0].Process);
            await File.WriteAllTextAsync(startPath, string.Empty);
            await parentWorker.WaitAsync(TimeSpan.FromSeconds(90));
            foreach (var child in children)
                await child.Process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(90));

            foreach (var child in children)
                Assert.True(child.Process.ExitCode == 0,
                    $"Child stress process failed. stdout: {await child.Output} stderr: {await child.Error}");
        }
        finally
        {
            await File.WriteAllTextAsync(startPath, string.Empty);
            foreach (var child in children)
            {
                if (!child.Process.HasExited)
                {
                    try { await child.Process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
                    catch (TimeoutException)
                    {
                        child.Process.Kill(entireProcessTree: true);
                        await child.Process.WaitForExitAsync();
                    }
                }
                child.Process.Dispose();
            }
            if (File.Exists(lockPath)) File.Delete(lockPath);
            Directory.Delete(scratch, recursive: true);
        }
    }

    [UnixWorkerLockFact]
    public void SharedWorkersKeepTheLockFileUntilTheLastReaderLeaves()
    {
        var lockName = "MotifWorkerSharedLockCleanupTest-" + Guid.NewGuid().ToString("N");
        var lockPath = UnixFileLock.GetLockPath(lockName, machineWide: false);
        using var lockFileCleanup = new DeleteLockFileOnDispose(lockPath);
        using var readerA = new WorkerMutexOwner(lockName, machineWide: false, WorkerLockAccess.Shared);
        using var readerB = new WorkerMutexOwner(lockName, machineWide: false, WorkerLockAccess.Shared);
        using var writer = new WorkerMutexOwner(lockName, machineWide: false, WorkerLockAccess.Exclusive);

        Assert.True(readerA.TryAcquire());
        Assert.True(readerB.TryAcquire());
        Assert.True(File.Exists(lockPath));
        Assert.False(writer.TryAcquire());

        readerA.Release();
        Assert.True(File.Exists(lockPath));
        Assert.False(writer.TryAcquire());

        readerB.Release();
        Assert.True(File.Exists(lockPath));
        Assert.True(writer.TryAcquire());
        writer.Release();
        Assert.True(File.Exists(lockPath));
    }

    private static async Task RunCompetingWorkerAsync(string lockName, string readyPath, string startPath,
        string occupancyPath)
    {
        using var owner = new WorkerMutexOwner(lockName, machineWide: true);
        await File.WriteAllTextAsync(readyPath, string.Empty);
        await WaitUntilAsync(() => File.Exists(startPath), TimeSpan.FromSeconds(20));

        for (var index = 0; index < 40; index++)
        {
            while (!owner.TryAcquire()) await Task.Delay(1);
            FileStream? occupancy = null;
            try
            {
                occupancy = new FileStream(occupancyPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                await Task.Delay(1);
            }
            catch (IOException exception)
            {
                throw new InvalidOperationException("Two processes entered one worker lock at the same time.",
                    exception);
            }
            finally
            {
                if (occupancy is not null)
                {
                    occupancy.Dispose();
                    File.Delete(occupancyPath);
                }
                owner.Release();
            }
        }
    }

    private static async Task RunLockStressWorkerAsync(string lockName, string readyPath, string startPath)
    {
        using var owner = new WorkerMutexOwner(lockName, machineWide: true);
        await File.WriteAllTextAsync(readyPath, string.Empty);
        await WaitUntilAsync(() => File.Exists(startPath), TimeSpan.FromSeconds(30));

        var iterations = int.Parse(Environment.GetEnvironmentVariable(IterationsVariable) ?? "20000");
        for (var index = 0; index < iterations; index++)
        {
            while (!owner.TryAcquire()) await Task.Yield();
            owner.Release();
        }
    }

    private static async Task WaitForFileAsync(string path, Process child)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (!File.Exists(path))
        {
            if (child.HasExited)
                throw new InvalidOperationException($"Child test exited before signaling readiness with code {child.ExitCode}.");
            await Task.Delay(10, timeout.Token);
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        using var cancellation = new CancellationTokenSource(timeout);
        while (!condition()) await Task.Delay(10, cancellation.Token);
    }

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

    private sealed class DeleteLockFileOnDispose(string lockPath) : IDisposable
    {
        public void Dispose()
        {
            if (File.Exists(lockPath)) File.Delete(lockPath);
        }
    }
}
