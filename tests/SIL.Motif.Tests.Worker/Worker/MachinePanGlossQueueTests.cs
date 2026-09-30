using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Threading;
using SIL.Motif.Host.PanGloss;
using Xunit;

namespace SIL.Motif.Tests.Worker;

public sealed class MachinePanGlossQueueTests
{
    private const string ChildSlotVariable = "MOTIF_PAN_GLOSS_QUEUE_CHILD_SLOT";
    private const string ChildReadyPathVariable = "MOTIF_PAN_GLOSS_QUEUE_CHILD_READY";
    private const string ChildReleasePathVariable = "MOTIF_PAN_GLOSS_QUEUE_CHILD_RELEASE";

    [Fact]
    public void DisposingSlotLeaseDisposesItsLockOwner()
    {
        using var owner = new WorkerMutexOwner("MotifPanGlossLeaseTest-" + Guid.NewGuid().ToString("N"));
        Assert.True(owner.TryAcquire());

        using (new MachineSlotLease(owner, 0, "job", null)) { }

        Assert.Throws<ObjectDisposedException>(() => owner.TryAcquire());
    }

    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("macos")]
    [RequiresUnixFact]
    public async Task InaccessibleMachineLockFailsItsQueuedJobWithPathAndRecoveryGuidance()
    {
        var name = "MotifPanGlossRestrictedLockTest-" + Guid.NewGuid().ToString("N");
        var path = UnixFileLock.GetLockPath(name, machineWide: true);
        using (new FileStream(path, FileMode.CreateNew, FileAccess.Write)) { }

        try
        {
            File.SetUnixFileMode(path, UnixFileMode.None);
            using var queue = new MachinePanGlossQueue(new[] { name });
            var error = await Assert.ThrowsAsync<IOException>(() => queue.RunAsync("denied",
                (_, _) => Task.FromResult(0), CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5)));

            Assert.Contains(path, error.Message);
            Assert.Contains("read/write permissions", error.Message);
            Assert.Contains("only if it is stale", error.Message);
        }
        finally
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.Delete(path);
        }
    }

    [Fact]
    public void DefaultSlotsStayMachineWideOutsideAnIsolatedTestProcess()
    {
        Assert.Equal(new[]
        {
            "Global\\MotifPanGlossSlot-0",
            "Global\\MotifPanGlossSlot-1",
        }, MachinePanGlossQueue.GetDefaultSlotNames(testProcessNamespace: null));

        Assert.Equal(new[]
        {
            "Global\\MotifPanGlossSlot-worker-123-0",
            "Global\\MotifPanGlossSlot-worker-123-1",
        }, MachinePanGlossQueue.GetDefaultSlotNames("worker-123"));
    }

    [RequiresUnixFact]
    public async Task RunAsync_AcrossTwoQueuesNeverExceedsMachineCapacity()
    {
        var slotNames = UniqueSlotNames(2);
        using var userA = new MachinePanGlossQueue(slotNames);
        using var userB = new MachinePanGlossQueue(slotNames);
        var gate = new object();
        var running = 0;
        var peak = 0;
        var releaseAll = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Task Enqueue(MachinePanGlossQueue queue, string jobId) => queue.RunAsync(jobId, async (job, ct) =>
        {
            Assert.NotNull(job.Report);
            lock (gate) peak = Math.Max(peak, ++running);
            try { await releaseAll.Task.WaitAsync(TimeSpan.FromSeconds(15), ct); }
            finally { lock (gate) running--; }
            return 0;
        }, CancellationToken.None);

        var tasks = new[]
        {
            Enqueue(userA, "a-1"), Enqueue(userA, "a-2"),
            Enqueue(userB, "b-1"), Enqueue(userB, "b-2"),
        };
        await WaitUntilAsync(() => Volatile.Read(ref peak) == 2, TimeSpan.FromSeconds(10));
        releaseAll.SetResult();
        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(20));

        Assert.Equal(2, peak);
        Assert.Equal(0, running);
    }

    [RequiresUnixFact]
    public async Task RunAsync_AcrossProcessesWaitsUntilAMachineSlotIsReleased()
    {
        if (Environment.GetEnvironmentVariable(ChildSlotVariable) is { Length: > 0 } childSlotName)
        {
            using var owner = new WorkerMutexOwner(childSlotName, machineWide: true);
            Assert.True(owner.TryAcquire());
            var readyPath = Environment.GetEnvironmentVariable(ChildReadyPathVariable)
                ?? throw new InvalidOperationException("The child ready path is required.");
            var releasePath = Environment.GetEnvironmentVariable(ChildReleasePathVariable)
                ?? throw new InvalidOperationException("The child release path is required.");
            await File.WriteAllTextAsync(readyPath, string.Empty);
            await WaitUntilAsync(() => File.Exists(releasePath), TimeSpan.FromSeconds(15));
            return;
        }

        var slotNames = UniqueSlotNames(2);
        var scratch = Path.Combine(Path.GetTempPath(), "MotifPanGlossQueue-" + Guid.NewGuid().ToString("N"));
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
        childStart.ArgumentList.Add(Path.Combine(FindRepositoryRoot(), "tests", "SIL.Motif.Tests.Worker",
            "SIL.Motif.Tests.Worker.csproj"));
        childStart.ArgumentList.Add("--configuration");
        childStart.ArgumentList.Add(new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name
            ?? throw new InvalidOperationException("The test configuration directory is missing."));
        childStart.ArgumentList.Add("--no-build");
        childStart.ArgumentList.Add("--no-restore");
        childStart.ArgumentList.Add("--filter");
        childStart.ArgumentList.Add("FullyQualifiedName=SIL.Motif.Tests.Worker.MachinePanGlossQueueTests.RunAsync_AcrossProcessesWaitsUntilAMachineSlotIsReleased");
        childStart.Environment[ChildSlotVariable] = slotNames[0];
        childStart.Environment[ChildReadyPathVariable] = readyFile;
        childStart.Environment[ChildReleasePathVariable] = releaseFile;

        using var child = Process.Start(childStart)
            ?? throw new InvalidOperationException("Could not start the child test process.");
        var output = child.StandardOutput.ReadToEndAsync();
        var error = child.StandardError.ReadToEndAsync();
        var releaseJobs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await WaitUntilAsync(() => File.Exists(readyFile), TimeSpan.FromSeconds(15));

            using var queue = new MachinePanGlossQueue(slotNames);
            var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var first = queue.RunAsync("first", async (_, cancellationToken) =>
            {
                firstStarted.SetResult();
                await releaseJobs.Task.WaitAsync(cancellationToken);
                return 1;
            }, CancellationToken.None);
            await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));

            var second = queue.RunAsync("second", (_, _) =>
            {
                secondStarted.SetResult();
                return Task.FromResult(2);
            }, CancellationToken.None);
            var admission = await Task.WhenAny(secondStarted.Task, Task.Delay(TimeSpan.FromMilliseconds(250)));
            Assert.NotSame(secondStarted.Task, admission);

            await File.WriteAllTextAsync(releaseFile, string.Empty);
            await secondStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            releaseJobs.SetResult();
            Assert.Equal(1, await first.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.Equal(2, await second.WaitAsync(TimeSpan.FromSeconds(10)));
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            Assert.True(child.ExitCode == 0, $"Child test failed. stdout: {await output} stderr: {await error}");
        }
        finally
        {
            releaseJobs.TrySetResult();
            await File.WriteAllTextAsync(releaseFile, string.Empty);
            if (!child.HasExited)
            {
                try { await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15)); }
                catch (TimeoutException)
                {
                    child.Kill(entireProcessTree: true);
                    await child.WaitForExitAsync();
                }
            }
            Directory.Delete(scratch, recursive: true);
        }
    }

    [RequiresWindowsFact]
    public async Task RunAsync_AdmitsThreeProjectsInSubmissionOrder()
    {
        var slotNames = UniqueSlotNames(2);
        using var queue = new MachinePanGlossQueue(slotNames);
        var admissionOrder = new ConcurrentQueue<string>();
        var firstTwoAdmitted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thirdAdmitted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseGates = new[] { "project-a", "project-b", "project-c" }
            .ToDictionary(id => id, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));

        Task<int> Enqueue(string jobId) => queue.RunAsync(jobId, async (_, ct) =>
        {
            admissionOrder.Enqueue(jobId);
            if (admissionOrder.Count == 2) firstTwoAdmitted.TrySetResult();
            if (jobId == "project-c") thirdAdmitted.TrySetResult();
            await releaseGates[jobId].Task.WaitAsync(ct);
            return 0;
        }, CancellationToken.None);

        var first = Enqueue("project-a");
        var second = Enqueue("project-b");
        var third = Enqueue("project-c");

        // Only two slots exist, so project-c cannot be admitted until one of the first two releases.
        await firstTwoAdmitted.Task;
        releaseGates["project-a"].SetResult();
        await thirdAdmitted.Task;
        releaseGates["project-b"].SetResult();
        releaseGates["project-c"].SetResult();

        await Task.WhenAll(first, second, third);
        Assert.Equal(new[] { "project-a", "project-b", "project-c" }, admissionOrder.ToArray());
    }

    [RequiresWindowsFact]
    [SupportedOSPlatform("windows")]
    public async Task RunAsync_AcrossTwoUserNamespaces_NeverExceedsMachineCapacity()
    {
        var slotNames = UniqueSlotNames(2);
        // Two queues sharing the same machine-global slot names, standing in for two user sessions.
        using var userA = new MachinePanGlossQueue(slotNames);
        using var userB = new MachinePanGlossQueue(slotNames);

        var gate = new object();
        var currentlyRunning = 0;
        var peakRunning = 0;
        var observedRates = new ConcurrentBag<uint>();
        var releaseAll = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var bothSlotsFilled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Task<int> Enqueue(MachinePanGlossQueue queue, string jobId) => queue.RunAsync(jobId, async (cpuJob, ct) =>
        {
            observedRates.Add(Assert.IsType<WindowsCpuJob>(cpuJob).QueryCpuRateControl().CpuRate);
            lock (gate)
            {
                peakRunning = Math.Max(peakRunning, ++currentlyRunning);
                if (currentlyRunning == 2) bothSlotsFilled.TrySetResult();
            }
            try { await releaseAll.Task.WaitAsync(TimeSpan.FromSeconds(15), ct); }
            finally { lock (gate) currentlyRunning--; }
            return 0;
        }, CancellationToken.None);

        // Four admissions against two slots: the cap holds regardless of which queue's jobs get them.
        var tasks = new[]
        {
            Enqueue(userA, "a-1"), Enqueue(userA, "a-2"),
            Enqueue(userB, "b-1"), Enqueue(userB, "b-2"),
        };

        await bothSlotsFilled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        releaseAll.SetResult();
        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(15));

        Assert.Equal(2, peakRunning);
        Assert.Equal(0, currentlyRunning);
        Assert.All(observedRates, rate => Assert.Equal((uint)WindowsCpuJob.CpuRateHardCapBasisPoints, rate));
    }

    [RequiresWindowsFact]
    public async Task RunAsync_CancelsAJobStillWaitingForASlotWithoutBlockingOthers()
    {
        var slotNames = UniqueSlotNames(1);
        using var queue = new MachinePanGlossQueue(slotNames);
        var holderStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var holderRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var holderTask = queue.RunAsync("holder", async (_, ct) =>
        {
            holderStarted.SetResult();
            await holderRelease.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
            return 0;
        }, CancellationToken.None);
        await holderStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        using var waitingJobCancellation = new CancellationTokenSource();
        var waitingTask = queue.RunAsync("waiting", (_, _) => Task.FromResult(0), waitingJobCancellation.Token);
        waitingJobCancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waitingTask)
            .WaitAsync(TimeSpan.FromSeconds(5));

        holderRelease.SetResult();
        Assert.Equal(0, await holderTask.WaitAsync(TimeSpan.FromSeconds(5)));

        // The cancelled waiter must not have leaked the slot it never held.
        var afterTask = queue.RunAsync("after", (_, _) => Task.FromResult(7), CancellationToken.None);
        Assert.Equal(7, await afterTask.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [RequiresWindowsFact]
    public async Task MachinePanGlossQueue_RecoversASlotAbandonedByADeadOwner()
    {
        var abandonedSlotName = UniqueSlotNames(1)[0];
        AbandonMutex(abandonedSlotName);

        using var queue = new MachinePanGlossQueue(new[] { abandonedSlotName });

        var result = await queue.RunAsync("recovered", (_, _) => Task.FromResult(42), CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(42, result);
    }

    private static string[] UniqueSlotNames(int count)
    {
        var suffix = Guid.NewGuid().ToString("N");
        return Enumerable.Range(0, count).Select(i => $"Global\\MotifPanGlossSlotTest-{suffix}-{i}").ToArray();
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

    // Simulates a dead owner: a Win32 mutex abandons when its owning thread exits without releasing.
    private static void AbandonMutex(string name)
    {
        var acquired = new ManualResetEventSlim(false);
        var thread = new Thread(() =>
        {
            var mutex = new Mutex(false, name);
            mutex.WaitOne();
            acquired.Set();
        });
        thread.IsBackground = true;
        thread.Start();
        if (!acquired.Wait(TimeSpan.FromSeconds(5)))
            throw new TimeoutException("The victim thread did not acquire the mutex in time.");
        thread.Join(TimeSpan.FromSeconds(5));
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
}
