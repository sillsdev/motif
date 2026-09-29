using System.Diagnostics;
using System.Globalization;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Worker;

public sealed class RequiresUnixFactAttribute : FactAttribute
{
    public RequiresUnixFactAttribute()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
            Skip = "POSIX process containment is only available on Linux and macOS.";
    }
}

public sealed class RequiresMacFactAttribute : FactAttribute
{
    public RequiresMacFactAttribute()
    {
        if (!OperatingSystem.IsMacOS())
            Skip = "The process-footprint watchdog is available only on macOS.";
    }
}

public sealed class UnixPanGlossContainmentTests
{
    private static readonly TimeSpan BoundedWait = TimeSpan.FromSeconds(15);
    private const ulong MemoryLimitBytes = 256UL * 1024 * 1024;

    [RequiresUnixFact]
    public async Task DisposingTheJobKillsAChildInTheProcessGroup()
    {
        var childPidPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var startInfo = Shell("sleep 60 & printf '%s' $! > \"$1\"; wait", childPidPath);
        var job = PanGlossContainment.CreateJob(MemoryLimitBytes);
        using var process = job.Start(startInfo);
        try
        {
            await WaitUntilAsync(() => File.Exists(childPidPath), BoundedWait);
            var childId = int.Parse(await File.ReadAllTextAsync(childPidPath), CultureInfo.InvariantCulture);
            Assert.Equal(process.Id, GetProcessGroupId(childId));

            job.Dispose();

            await process.WaitForExitAsync().WaitAsync(BoundedWait);
            await WaitUntilAsync(() => !ProcessExists(childId), BoundedWait);
        }
        finally
        {
            job.Dispose();
            try { File.Delete(childPidPath); }
            catch (IOException) { }
        }
    }

    [RequiresUnixFact]
    public async Task MemoryLimitRefusesAnAllocation()
    {
        using var job = PanGlossContainment.CreateJob(MemoryLimitBytes);
        var startInfo = new ProcessStartInfo(FakeParser.ExecutablePath)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("--allocate-memory");
        startInfo.ArgumentList.Add((320 * 1024 * 1024).ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("5000");
        using var process = job.Start(startInfo);

        await process.WaitForExitAsync().WaitAsync(BoundedWait);

        Assert.True(process.ExitCode is 73 or 137);
        Assert.Equal(MemoryLimitBytes, job.Report.MemoryLimitBytes);
        if (OperatingSystem.IsMacOS())
        {
            var standardError = await process.ReadStandardErrorAsync();
            Assert.True(job.Report.AggregateMemoryLimit);
            Assert.Contains("watchdog", job.Report.Memory);
            Assert.Contains(job.Report.Limitations,
                limitation => limitation.Contains("sampling", StringComparison.OrdinalIgnoreCase));
            Assert.Contains("exceeded its macOS physical-footprint memory limit", standardError);
        }
    }

    [RequiresMacFact]
    public async Task MemoryLimitCountsTheCombinedFootprintOfAProcessGroup()
    {
        using var job = PanGlossContainment.CreateJob(MemoryLimitBytes);
        var parser = ShellQuote(FakeParser.ExecutablePath);
        var bytes = (160 * 1024 * 1024).ToString(CultureInfo.InvariantCulture);
        var script = $"{parser} --allocate-memory {bytes} 5000 & first=$!; " +
            $"{parser} --allocate-memory {bytes} 5000 & second=$!; " +
            "wait \"$first\"; wait \"$second\"";
        using var process = job.Start(Shell(script));

        await process.WaitForExitAsync().WaitAsync(BoundedWait);

        Assert.Equal(137, process.ExitCode);
        var standardError = await process.ReadStandardErrorAsync();
        Assert.Contains("exceeded its macOS physical-footprint memory limit", standardError);
    }

    private static ProcessStartInfo Shell(string script, string? argument = null)
    {
        var startInfo = new ProcessStartInfo("/bin/sh")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add(script);
        startInfo.ArgumentList.Add("containment-test");
        if (argument is not null) startInfo.ArgumentList.Add(argument);
        return startInfo;
    }

    private static string ShellQuote(string value) => "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";

    private static int GetProcessGroupId(int processId) => UnixProcessIdentity.GetProcessGroupId(processId);

    private static bool ProcessExists(int processId) => UnixProcessIdentity.ProcessExists(processId);

    private static async Task WaitUntilAsync(Func<bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!predicate())
        {
            if (DateTime.UtcNow >= deadline) throw new TimeoutException("The process condition was not met.");
            await Task.Delay(20);
        }
    }
}
