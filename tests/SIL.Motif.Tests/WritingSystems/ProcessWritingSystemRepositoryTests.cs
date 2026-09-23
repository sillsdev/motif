using System.Diagnostics;
using System.Security.Cryptography;
using SIL.Motif.Cli;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker;
using Xunit;

namespace SIL.Motif.Tests.WritingSystems;

/// <summary>
/// Pins that caches opened by this test process save their shared writing systems into the process's
/// own repository and never into the machine-wide one, which concurrent shards would contend on.
/// </summary>
[Collection(LcmCacheTestCollection.Name)]
public sealed class ProcessWritingSystemRepositoryTests
{
    private readonly PristineProjectFixture _pristine;

    public ProcessWritingSystemRepositoryTests(PristineProjectFixture pristine)
    {
        _pristine = pristine;
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
    public void LaunchedMotifProcessUsesTheSelectedRepositoryInsteadOfTheMachineWideRepository()
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
            start.Environment[RunnerKick.SuppressVariable] = "1";
            start.ArgumentList.Add("open");
            start.ArgumentList.Add(fwDataPath);
            start.ArgumentList.Add("--json");
            using var process = Process.Start(start)!;
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit((int)TimeSpan.FromSeconds(90).TotalMilliseconds))
            {
                var executable = Path.GetFullPath(start.FileName);
                var productDirectory = Path.GetFullPath(BuildOutput.ProductDirectory);
                var repositoryRoot = Path.GetFullPath(Path.Combine(productDirectory, "..", ".."));
                if (executable.StartsWith(Path.Combine(repositoryRoot, "bin") + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
                    process.Kill();
                throw new TimeoutException($"motif open did not exit before the test timeout: {executable}");
            }
            var output = outputTask.GetAwaiter().GetResult();
            var error = errorTask.GetAwaiter().GetResult();

            Assert.True(process.ExitCode == 0, $"motif open failed: {error}\n{output}");
            Assert.Equal(machineStoreBefore, SnapshotFiles(machineStore));
            Assert.Equal(processStoreBefore, SnapshotFiles(ProcessWritingSystemRepository.BasePath));
            Assert.NotEmpty(SnapshotFiles(childStore));
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
}
