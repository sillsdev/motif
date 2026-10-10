using System.Diagnostics;
using System.Globalization;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Worker;

public sealed class RequiresLinuxFactAttribute : FactAttribute
{
    public RequiresLinuxFactAttribute()
    {
        if (!OperatingSystem.IsLinux())
            Skip = "The cgroup-free Linux containment path exists only on Linux.";
    }
}

public sealed class PanGlossMemoryCeilingTests
{
    private static readonly TimeSpan BoundedWait = TimeSpan.FromSeconds(30);
    private const int StackBytes = 1024 * 1024 * 1024;

    // Twelve 1 GiB stacks reserve more than the 10 GiB ceiling while touching almost none of it.
    private const int Threads = 12;

    [Fact]
    public async Task ReservedThreadStacksDoNotCountAgainstTheDefaultCeiling()
    {
        using var job = PanGlossContainment.CreateJob();

        var (exitCode, standardOutput, standardError) = await RunAsync(job, Threads, StackBytes);

        Assert.True(exitCode == 0, $"exit {exitCode}: {standardError}");
        Assert.Equal($"threads-started {Threads}", standardOutput.Trim());
    }

    [RequiresLinuxFact]
    public async Task ReservedThreadStacksDoNotCountAgainstTheCeilingWithoutACgroup()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var job = UnixPanGlossJob.WithoutCgroup(PanGlossContainment.DefaultMemoryLimitBytes, linux: true);

        var (exitCode, standardOutput, standardError) = await RunAsync(job, Threads, StackBytes);

        Assert.True(exitCode == 0, $"exit {exitCode}: {standardError}");
        Assert.Equal($"threads-started {Threads}", standardOutput.Trim());
        Assert.True(job.Report.AggregateMemoryLimit);
        Assert.Equal(PanGlossContainment.DefaultMemoryLimitBytes, job.Report.MemoryLimitBytes);
    }

    [RequiresLinuxFact]
    public async Task WithoutACgroupTheCeilingStopsAProcessGroupThatWritesPastIt()
    {
        if (!OperatingSystem.IsLinux()) return;
        const ulong limit = 256UL * 1024 * 1024;
        using var job = UnixPanGlossJob.WithoutCgroup(limit, linux: true);
        var parser = ShellQuote(FakeParser.ExecutablePath);
        var bytes = (160 * 1024 * 1024).ToString(CultureInfo.InvariantCulture);
        var startInfo = new ProcessStartInfo("/bin/sh") { UseShellExecute = false };
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add($"{parser} --allocate-memory {bytes} 5000 & first=$!; " +
            $"{parser} --allocate-memory {bytes} 5000 & second=$!; wait \"$first\"; wait \"$second\"");
        using var process = job.Start(startInfo);

        await process.WaitForExitAsync().WaitAsync(BoundedWait);

        var standardError = await process.ReadStandardErrorAsync();
        Assert.True(process.ExitCode == 137, $"exit {process.ExitCode}: {standardError}");
        Assert.Contains("exceeded its Linux resident and swapped memory limit", standardError);
    }

    private static async Task<(int ExitCode, string StandardOutput, string StandardError)> RunAsync(
        PanGlossContainmentJob job, int threads, int stackBytes)
    {
        var startInfo = new ProcessStartInfo(FakeParser.ExecutablePath)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("--reserve-thread-stacks");
        startInfo.ArgumentList.Add(threads.ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add(stackBytes.ToString(CultureInfo.InvariantCulture));
        using var process = job.Start(startInfo);
        await process.WaitForExitAsync().WaitAsync(BoundedWait);
        return (process.ExitCode, await process.ReadStandardOutputAsync(), await process.ReadStandardErrorAsync());
    }

    private static string ShellQuote(string value) => "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";
}
