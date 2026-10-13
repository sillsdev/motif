using SIL.LCModel.Core.WritingSystems;
using SIL.Motif.Host.LcmUtils;
using System.Diagnostics;
using System.Security.Cryptography;
using SIL.Motif.Cli;
using SIL.Motif.Commands;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker;
using Xunit;

namespace SIL.Motif.Tests.WritingSystems;

/// <summary>
/// Pins that caches opened by this test process save their shared writing systems into the process's
/// own repository and never into the machine-wide one, which concurrent test processes would contend on.
/// </summary>
[Collection(LcmCacheTestCollection.Name)]
public sealed class ProcessWritingSystemRepositoryTests
{
    private static readonly TimeSpan ChildHangGuard = TimeSpan.FromSeconds(90);

    private readonly PristineProjectFixture _pristine;

    public ProcessWritingSystemRepositoryTests(PristineProjectFixture pristine)
    {
        _pristine = pristine;
    }

    [Fact]
    public void RepositoryLockNameMatchesFilesystemAliases()
    {
        var root = Path.Combine(BuildOutput.ProductDirectory, "RepositoryLockIdentity",
            Guid.NewGuid().ToString("N"));
        var repositoryPath = Path.Combine(root, "WritingSystems");
        var aliasPath = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? Path.Combine(root, "WRITINGSYSTEMS")
            : Path.Combine(root, "writing-systems-link");
        Directory.CreateDirectory(repositoryPath);

        try
        {
            if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS())
                Directory.CreateSymbolicLink(aliasPath, repositoryPath);

            Assert.Equal(
                SerializedGlobalWritingSystemRepository.GetRepositoryLockName(repositoryPath),
                SerializedGlobalWritingSystemRepository.GetRepositoryLockName(aliasPath));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    [Fact]
    public void ConcurrentScratchLoadsLeaveTheProcessRepositoryInstalled()
    {
        var installed = ProcessWritingSystemRepository.CurrentOutsideScratchLoads();
        var projects = Enumerable.Range(0, 8).Select(_ => _pristine.CopyProjectFile()).ToArray();

        Parallel.ForEach(projects, new ParallelOptions { MaxDegreeOfParallelism = projects.Length }, project =>
        {
            using var cache = new FwDataProjectLoader().LoadScratchCache(project);
        });

        Assert.Same(installed, ProcessWritingSystemRepository.CurrentOutsideScratchLoads());
    }

    [Fact]
    public void ConcurrentCacheDisposalsNeverCollideInTheSharedWritingSystemRepository()
    {
        FwDataProjectLoader.Init();
        var repository = Assert.IsType<SerializedGlobalWritingSystemRepository>(
            ProcessWritingSystemRepository.CurrentOutsideScratchLoads());
        const string tag = "qaa-x-race";
        try
        {
            for (var round = 0; round < 20; round++)
            {
                var definitions = Enumerable.Range(0, 8).Select(_ => new CoreWritingSystemDefinition(tag)).ToArray();
                Parallel.ForEach(definitions, new ParallelOptions { MaxDegreeOfParallelism = definitions.Length },
                    definition => repository.Set(definition));
                repository.Remove(tag);
            }
        }
        finally
        {
            if (repository.Contains(tag)) repository.Remove(tag);
        }
    }

    [Fact]
    public async Task DiscardingRepositoryReadsWaitForSharedRepositoryWriters()
    {
        FwDataProjectLoader.Init();
        var decoy = DiscardingGlobalWritingSystemRepository.Create();
        var repository = Assert.IsType<SerializedGlobalWritingSystemRepository>(
            ProcessWritingSystemRepository.CurrentOutsideScratchLoads());
        const string tag = "qaa-x-discarding-remove";
        try
        {
            repository.Set(new CoreWritingSystemDefinition(tag));
            decoy.Remove(tag);
            Assert.True(repository.Contains(tag));
        }
        finally
        {
            if (repository.Contains(tag)) repository.Remove(tag);
        }

        using var writerGate = new WorkerMutexOwner(
            SerializedGlobalWritingSystemRepository.GetRepositoryLockName(decoy.PathToWritingSystems));
        var gateWait = Stopwatch.StartNew();
        var gateAcquired = false;
        while (!gateAcquired && gateWait.Elapsed < ChildHangGuard)
        {
            gateAcquired = writerGate.TryAcquire();
            if (!gateAcquired) Thread.Sleep(10);
        }
        Assert.True(gateAcquired);

        var readStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var read = Task.Run(() =>
        {
            readStarted.SetResult();
            decoy.TryGet("en", out _);
        });

        try
        {
            await readStarted.Task.WaitAsync(ChildHangGuard);
            Assert.NotSame(read, await Task.WhenAny(read, Task.Delay(TimeSpan.FromMilliseconds(100))));
        }
        finally
        {
            writerGate.Release();
        }

        await read.WaitAsync(ChildHangGuard);
    }

    [Fact]
    public async Task ConcurrentSampleBuildersKeepTheSharedWritingSystemRepositoryReadable()
    {
        const int rounds = 3;
        const int buildersPerRound = 16;
        var root = Path.Combine(BuildOutput.ProductDirectory, "ConcurrentSampleBuilders",
            Guid.NewGuid().ToString("N"));
        var repositoryRoot = Path.GetFullPath(Path.Combine(BuildOutput.ProductDirectory, "..", ".."));
        var builderPath = Path.Combine(BuildOutput.ProductDirectory,
            OperatingSystem.IsWindows() ? "SIL.Motif.SampleProjects.exe" : "SIL.Motif.SampleProjects");
        var samplePath = Path.Combine(repositoryRoot, "samples", "synthetic-turkic", "sample.json");
        Directory.CreateDirectory(root);

        try
        {
            for (var round = 0; round < rounds; round++)
            {
                var builds = await Task.WhenAll(Enumerable.Range(0, buildersPerRound).Select(async index =>
                {
                    var outputRoot = Path.Combine(root, $"build-{round}-{index}");
                    var start = new ProcessStartInfo(builderPath)
                    {
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                    };
                    start.ArgumentList.Add("build");
                    start.ArgumentList.Add(samplePath);
                    start.ArgumentList.Add(outputRoot);
                    using var process = Process.Start(start)
                        ?? throw new InvalidOperationException("Could not start the sample builder.");
                    var output = process.StandardOutput.ReadToEndAsync();
                    var error = process.StandardError.ReadToEndAsync();
                    var exited = await WaitForChildExitAsync(process, ChildHangGuard);
                    return (
                        OutputRoot: outputRoot,
                        TimedOut: !exited,
                        ExitCode: process.HasExited ? process.ExitCode : (int?)null,
                        Output: await CaptureChildTextAsync(output),
                        Error: await CaptureChildTextAsync(error));
                }));

                Assert.All(builds, build =>
                {
                    var outcome = build.TimedOut
                        ? $"timed out after {ChildHangGuard}"
                        : $"exited with code {build.ExitCode}";
                    Assert.True(!build.TimedOut && build.ExitCode == 0,
                        $"Sample builder {outcome}.\nstderr: {build.Error}\nstdout: {build.Output}");
                });
                Assert.All(builds, build => Assert.True(File.Exists(Path.Combine(build.OutputRoot,
                    "MotifSyntheticTurkic", "WritingSystemStore", "en.ldml"))));
            }
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    [Fact]
    public void ScratchCacheRestoresTheRepositorySelectedForTheProcess()
    {
        var repository = ProcessWritingSystemRepository.CurrentOutsideScratchLoads();

        using (_pristine.NewScratch()) { }

        Assert.Same(repository, ProcessWritingSystemRepository.CurrentOutsideScratchLoads());
    }

    [Fact]
    public void TheSeededMastersWritingSystemsWereSavedIntoTheProcessRepository()
    {
        var saved = Directory.EnumerateFiles(ProcessWritingSystemRepository.BasePath, "*.ldml", SearchOption.AllDirectories)
            .Select(Path.GetFileNameWithoutExtension)
            .ToArray();

        Assert.Contains(NewLangProjFixture.VernacularTag, saved);
    }

    [Fact]
    public async Task ReadOnlyMotifProcessLeavesEveryWritingSystemRepositoryUnchanged()
    {
        var machineStore = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "SIL", "WritingSystemRepository");
        var machineStoreBefore = SnapshotFiles(machineStore);
        var processStoreBefore = SnapshotFiles(ProcessWritingSystemRepository.BasePath);
        var childStore = Path.Combine(
            Path.GetTempPath(), "SIL.Motif.Tests.WritingSystems",
            Environment.ProcessId + "-child-" + Guid.NewGuid().ToString("N"));
        var previousRepositoryPath = Environment.GetEnvironmentVariable(
            ProcessWritingSystemRepository.RepositoryPathVariable);
        var childWorkerRoot = Path.Combine(
            Path.GetTempPath(), "SIL.Motif.Tests.Cli", Environment.ProcessId + "-" + Guid.NewGuid().ToString("N"));

        try
        {
            Environment.SetEnvironmentVariable(ProcessWritingSystemRepository.RepositoryPathVariable, childStore);
            var fwDataPath = _pristine.CopyProjectFile();
            var start = new ProcessStartInfo(BuildOutput.Cli)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            start.Environment[RunnerOptions.RootVariable] = childWorkerRoot;
            start.Environment[ProcessRunnerLauncher.SuppressVariable] = "1";
            start.ArgumentList.Add("open");
            start.ArgumentList.Add(fwDataPath);
            start.ArgumentList.Add("--json");
            using var process = Process.Start(start)!;
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            var exited = await WaitForChildExitAsync(process, ChildHangGuard);
            var output = await CaptureChildTextAsync(outputTask);
            var error = await CaptureChildTextAsync(errorTask);
            Assert.True(exited,
                $"motif open did not exit before {ChildHangGuard}.\nstderr: {error}\nstdout: {output}");

            Assert.True(process.ExitCode == 0, $"motif open failed: {error}\n{output}");
            Assert.Equal(machineStoreBefore, SnapshotFiles(machineStore));
            Assert.Equal(processStoreBefore, SnapshotFiles(ProcessWritingSystemRepository.BasePath));
            Assert.Empty(SnapshotFiles(childStore));
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                ProcessWritingSystemRepository.RepositoryPathVariable, previousRepositoryPath);
            try { Directory.Delete(childStore, recursive: true); }
            catch (DirectoryNotFoundException) { }
            try { Directory.Delete(childWorkerRoot, recursive: true); }
            catch (DirectoryNotFoundException) { }
        }
    }

    private static string[] SnapshotFiles(string root)
    {
        if (!Directory.Exists(root)) return [];

        return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .OrderBy(path => Path.GetRelativePath(root, path), StringComparer.Ordinal)
            .Select(path => string.Join("|",
                Path.GetRelativePath(root, path),
                new FileInfo(path).Length,
                File.GetLastWriteTimeUtc(path).Ticks,
                Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))))
            .ToArray();
    }

    private static async Task<bool> WaitForChildExitAsync(Process process, TimeSpan timeout)
    {
        try
        {
            await process.WaitForExitAsync().WaitAsync(timeout);
            return true;
        }
        catch (TimeoutException)
        {
            if (process.HasExited) return true;

            process.Kill(entireProcessTree: true);
            try
            {
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            }
            catch (TimeoutException)
            {
            }

            return false;
        }
    }

    private static async Task<string> CaptureChildTextAsync(Task<string> capture)
    {
        try
        {
            return await capture.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (TimeoutException)
        {
            return "<stream did not close before the diagnostic deadline>";
        }
    }
}
