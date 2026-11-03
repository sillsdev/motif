using System.Security.Cryptography;
using System.Text;
using SIL.Motif.Host.PanGloss;
using Xunit;

namespace SIL.Motif.Tests.Assess;

public sealed class StatsCacheLockTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "SIL.Motif.Tests.CommandScratch", Guid.NewGuid().ToString("N"));

    public StatsCacheLockTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }

    private string RunFile(string invocation)
    {
        var directory = Path.Combine(_root, "assessment-runs", invocation);
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "source.fwdata");
    }

    // The old lease was one of 256 machine-wide stripes named by the first two hex digits of this hash.
    private static string OldStripe(string directory)
    {
        var path = Path.GetFullPath(directory);
        if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()) path = path.ToUpperInvariant();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path)))[..2];
    }

    private (string First, string Second) CollidingInvocations()
    {
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < 100000; index++)
        {
            var name = "run" + index;
            var stripe = OldStripe(Path.Combine(_root, "assessment-runs", name));
            if (seen.TryGetValue(stripe, out var other)) return (other, name);
            seen[stripe] = name;
        }
        throw new InvalidOperationException("No two names share a stripe.");
    }

    [Fact]
    public async Task UnrelatedDirectoriesWhoseOldStripesCollideDoNotBlockEachOther()
    {
        var (first, second) = CollidingInvocations();
        Assert.Equal(OldStripe(Path.GetDirectoryName(RunFile(first))!), OldStripe(Path.GetDirectoryName(RunFile(second))!));
        using var holder = StatsCacheLock.Create(Path.GetDirectoryName(RunFile(first))!);
        Assert.True(holder.TryAcquire());

        var reader = Task.Run(() => StatsCacheLock.AcquireForFile(RunFile(second)));

        using var acquired = await reader.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.NotNull(acquired);
    }

    [Fact]
    public async Task AReaderOnTheSameDirectoryWaitsInsteadOfRefusingThenSucceeds()
    {
        var file = RunFile("same");
        using var holder = StatsCacheLock.Create(Path.GetDirectoryName(file)!);
        Assert.True(holder.TryAcquire());
        var reader = Task.Run(() => StatsCacheLock.AcquireForFile(file));

        await Task.Delay(TimeSpan.FromSeconds(1));
        Assert.False(reader.IsCompleted, "the reader waits instead of refusing");
        holder.Release();

        using var acquired = await reader.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.NotNull(acquired);
    }

    [Fact]
    public async Task CancellationEndsTheWait()
    {
        var file = RunFile("cancel");
        using var holder = StatsCacheLock.Create(Path.GetDirectoryName(file)!);
        Assert.True(holder.TryAcquire());
        using var cancel = new CancellationTokenSource();
        var reader = Task.Run(() => StatsCacheLock.AcquireForFile(file, cancel.Token));

        await Task.Delay(300);
        Assert.False(reader.IsCompleted);
        cancel.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reader.WaitAsync(TimeSpan.FromSeconds(10)));
        holder.Release();
        using var after = StatsCacheLock.AcquireForFile(file);
        Assert.NotNull(after);
    }

    [Fact]
    public void AReleasedLeaseLeavesNoLockFileInTheTemporaryFolder()
    {
        if (OperatingSystem.IsWindows()) return;
        var directory = Path.GetDirectoryName(RunFile("cleanup"))!;
        var identity = "machine:Global\\Motif.StatsCache." +
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(directory))));
        var lockFolder = Environment.GetEnvironmentVariable("MOTIF_TEST_WORKER_LOCK_NAMESPACE") is { Length: > 0 } ns
            ? Path.GetFullPath(ns) : "/tmp";
        var lockFile = Path.Combine(lockFolder, "motif-lock-" +
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant());

        using (var lease = StatsCacheLock.Create(directory))
        {
            Assert.True(lease.TryAcquire());
            Assert.True(File.Exists(lockFile));
        }

        Assert.False(File.Exists(lockFile));
    }
}
