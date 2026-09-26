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
        using var process = job.Start(startInfo);

        await process.WaitForExitAsync().WaitAsync(BoundedWait);

        Assert.True(process.ExitCode is 73 or 137);
        Assert.Equal(MemoryLimitBytes, job.Report.MemoryLimitBytes);
    }

    private static ProcessStartInfo Shell(string script, string argument)
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
        startInfo.ArgumentList.Add(argument);
        return startInfo;
    }

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
