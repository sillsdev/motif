using System.Diagnostics;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Worker;

public sealed class PanGlossMemoryKillTests : IDisposable
{
    private const ulong GiB = 1024UL * 1024 * 1024;
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "motif-memkill-" + Guid.NewGuid().ToString("N"));

    public PanGlossMemoryKillTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void TheWatchdogRecordsTheLimitAndTheSampleThatExceededItBeforeItKills()
    {
        var killed = new ManualResetEventSlim();
        var memory = new FakeGroupMemory(13 * GiB);
        using var watchdog = new PanGlossMemoryWatchdog(1, 12 * GiB, memory, killed.Set);

        Assert.True(killed.Wait(TimeSpan.FromSeconds(10)));

        var kill = Assert.IsType<PanGlossMemoryKill>(watchdog.MemoryKill);
        Assert.Equal(12 * GiB, kill.LimitBytes);
        Assert.Equal(13 * GiB, kill.PeakBytes);
    }

    [Fact]
    public async Task TheWatchdogReportsNoMemoryKillForAGroupUnderItsLimit()
    {
        var memory = new FakeGroupMemory(GiB);
        using var watchdog = new PanGlossMemoryWatchdog(1, 12 * GiB, memory, () => Assert.Fail("killed"));

        await Task.Delay(300);

        Assert.Null(watchdog.MemoryKill);
    }

    [Fact]
    public void ACgroupMemoryMaxIsTheByteCountOrMaxWhenThereIsNoLimit()
    {
        Assert.Equal("12884901888", UnixCgroup.MemoryMaxText(12 * GiB));
        Assert.Equal("max", UnixCgroup.MemoryMaxText(null));
    }

    [Fact]
    public void ACgroupThatOomKilledAMemberIsAMemoryKillWithItsPeak()
    {
        File.WriteAllText(Path.Combine(_directory, "memory.events"), "low 0\nhigh 0\nmax 4\noom 1\noom_kill 1\n");
        File.WriteAllText(Path.Combine(_directory, "memory.peak"), "12884901888\n");

        var kill = UnixCgroup.ReadMemoryKill(_directory, 12 * GiB);

        Assert.Equal(new PanGlossMemoryKill(12 * GiB, 12 * GiB), kill);
    }

    [Fact]
    public void ACgroupWithoutAMemoryPeakFileStillReportsTheKillWithAnUnknownPeak()
    {
        File.WriteAllText(Path.Combine(_directory, "memory.events"), "oom_kill 2\n");

        Assert.Equal(new PanGlossMemoryKill(GiB, null), UnixCgroup.ReadMemoryKill(_directory, GiB));
    }

    [Fact]
    public void ACgroupThatKilledNothingIsNotAMemoryKill()
    {
        File.WriteAllText(Path.Combine(_directory, "memory.events"), "max 0\noom_kill 0\n");

        Assert.Null(UnixCgroup.ReadMemoryKill(_directory, GiB));
    }

    [Theory]
    [InlineData(10UL, 10UL, 1, true)]
    [InlineData(10UL, 10UL, 0, false)]
    [InlineData(10UL, 5UL, 1, false)]
    public void AJobObjectThatPeakedAtItsLimitAndFailedIsAMemoryKill(
        ulong limit, ulong peak, int exitCode, bool expected)
    {
        var kill = PanGlossMemoryKill.FromJobPeak(limit * GiB, peak * GiB, exitCode);

        Assert.Equal(expected, kill is not null);
        if (expected) Assert.Equal(new PanGlossMemoryKill(limit * GiB, peak * GiB), kill);
    }

    [Fact]
    public void TheMessageNamesTheLimitThePeakTheSettingAndTheRemedy()
    {
        var message = new PanGlossMemoryKill(12 * GiB, 13 * GiB).Describe();

        Assert.Contains("12 GiB", message, StringComparison.Ordinal);
        Assert.Contains("13 GiB", message, StringComparison.Ordinal);
        Assert.Contains("MOTIF_PARSER_MEMORY_LIMIT", message, StringComparison.Ordinal);
        Assert.Contains("threads", message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("peak", new PanGlossMemoryKill(12 * GiB, null).Describe(),
            StringComparison.OrdinalIgnoreCase);
    }

    [RequiresLinuxFact]
    public async Task WithoutACeilingNoWatchdogRunsAndTheReportSaysSo()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var job = UnixPanGlossJob.WithoutCgroup(null, linux: true);
        var startInfo = new ProcessStartInfo(FakeParser.ExecutablePath) { UseShellExecute = false };
        startInfo.ArgumentList.Add("--reserve-thread-stacks");
        startInfo.ArgumentList.Add("1");
        startInfo.ArgumentList.Add("1048576");
        using var process = job.Start(startInfo);

        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
        await process.ReadStandardErrorAsync();

        Assert.Null(job.Report.MemoryLimitBytes);
        Assert.False(job.Report.AggregateMemoryLimit);
        Assert.Contains("no memory ceiling", job.Report.Memory, StringComparison.OrdinalIgnoreCase);
        Assert.Null(process.MemoryKill);
    }

    [RequiresLinuxFact]
    public async Task AProcessGroupKilledByTheWatchdogCarriesATypedMemoryKill()
    {
        if (!OperatingSystem.IsLinux()) return;
        const ulong limit = 256UL * 1024 * 1024;
        using var job = UnixPanGlossJob.WithoutCgroup(limit, linux: true);
        var startInfo = new ProcessStartInfo(FakeParser.ExecutablePath) { UseShellExecute = false };
        startInfo.ArgumentList.Add("--allocate-memory");
        startInfo.ArgumentList.Add((400 * 1024 * 1024).ToString());
        startInfo.ArgumentList.Add("5000");
        using var process = job.Start(startInfo);

        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
        await process.ReadStandardErrorAsync();

        var kill = Assert.IsType<PanGlossMemoryKill>(process.MemoryKill);
        Assert.Equal(limit, kill.LimitBytes);
        Assert.True(kill.PeakBytes > limit);
    }

    private sealed class FakeGroupMemory(ulong bytes) : IProcessGroupMemory
    {
        public string Measure => "fake";
        public int[] ListProcessIds(int processGroupId) => [processGroupId];
        public ulong SumBytes(IEnumerable<int> processIds) => bytes;
    }
}
