using System.Diagnostics;
using SIL.Motif.Host.Installation;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.LibLcm.Installation;

/// <summary>Skips on Windows, where a link to an executable needs a privilege the suite does not assume.</summary>
public sealed class UnixFactAttribute : FactAttribute
{
    public UnixFactAttribute()
    {
        if (OperatingSystem.IsWindows()) Skip = "Needs symbolic links and Unix process listing.";
    }
}

public sealed class InstalledWorkerLocatorTests : IDisposable
{
    private readonly string _scratch =
        Path.Combine(Path.GetTempPath(), "MotifWorkerLocator-" + Guid.NewGuid().ToString("N"));
    private readonly List<Process> _started = [];

    public InstalledWorkerLocatorTests() => Directory.CreateDirectory(_scratch);

    public void Dispose()
    {
        foreach (var process in _started)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            process.Dispose();
        }
        try { Directory.Delete(_scratch, recursive: true); }
        catch (IOException) { }
    }

    [Theory]
    [InlineData("/opt/motif/current", "/opt/motif/current/SIL.Motif.Worker", true)]
    [InlineData("/opt/motif/current/", "/opt/motif/current/SIL.Motif.Worker", true)]
    [InlineData("/opt/motif/current", "/opt/motif/current-old/SIL.Motif.Worker", false)]
    [InlineData("/opt/motif/current", "/opt/motif/curren/SIL.Motif.Worker", false)]
    [InlineData("/opt/motif/current", "/opt/motif/current/other", false)]
    public void ARunningFileMatchesOnlyAWorkerBeneathTheInstallDirectoryAtADirectoryBoundary(
        string install, string executable, bool expected)
    {
        if (OperatingSystem.IsWindows()) return;
        Assert.Equal(expected, InstalledWorkerLocator.IsWorkerUnder(install, executable));
    }

    [UnixFact]
    public async Task AnIdleRealWorkerIsFoundAndRetiredAndASiblingDirectoryIsNotTouched()
    {
        var gate = "SIL.Motif.UpdateActivity.Test." + Guid.NewGuid().ToString("N");
        var worker = await StartWorkerAsync(BuildOutput.Worker, gate);
        var install = BuildOutput.ProductDirectory;

        var found = InstalledWorkerLocator.Find(install);
        Assert.Contains(found, info => info.ProcessId == worker.Id);
        Assert.DoesNotContain(InstalledWorkerLocator.Find(install + "x"), info => info.ProcessId == worker.Id);
        Assert.DoesNotContain(InstalledWorkerLocator.Find(_scratch), info => info.ProcessId == worker.Id);
        Assert.DoesNotContain(InstalledWorkerLocator.Find(install[..^1]), info => info.ProcessId == worker.Id);

        MotifUpdateGate.RetireIdleWorkers(gate, install, TimeSpan.FromSeconds(30), InstalledWorkerLocator.Find);

        Assert.True(worker.WaitForExit(TimeSpan.FromSeconds(10)), "The Worker did not exit after retirement.");
        Assert.DoesNotContain(InstalledWorkerLocator.Find(install), info => info.ProcessId == worker.Id);
    }

    [UnixFact]
    public async Task AWorkerStartedThroughASymlinkIsFoundByItsExecutablePath()
    {
        var link = Path.Combine(_scratch, "w");
        File.CreateSymbolicLink(link, BuildOutput.Worker);
        var worker = await StartWorkerAsync(link, "SIL.Motif.UpdateActivity.Test." + Guid.NewGuid().ToString("N"));

        // The process name is the link's, so a name lookup cannot see this Worker at all.
        Assert.DoesNotContain(Process.GetProcessesByName("SIL.Motif.Worker"), p => p.Id == worker.Id);
        Assert.Contains(InstalledWorkerLocator.Find(BuildOutput.ProductDirectory),
            info => info.ProcessId == worker.Id);
    }

    [UnixFact]
    public async Task AWorkerOfTheSameAppImageIsFoundFromAnotherMount()
    {
        if (!OperatingSystem.IsLinux()) return;
        var image = Path.Combine(_scratch, "Motif.AppImage");
        var worker = await StartWorkerAsync(BuildOutput.Worker,
            "SIL.Motif.UpdateActivity.Test." + Guid.NewGuid().ToString("N"), appImage: image);
        var otherMount = Path.Combine(_scratch, ".mount_other");

        Assert.Contains(InstalledWorkerLocator.FindOnLinux("/proc", otherMount, image),
            info => info.ProcessId == worker.Id);
        Assert.DoesNotContain(InstalledWorkerLocator.FindOnLinux("/proc", otherMount,
            Path.Combine(_scratch, "Other.AppImage")), info => info.ProcessId == worker.Id);
        Assert.DoesNotContain(InstalledWorkerLocator.FindOnLinux("/proc", otherMount, null),
            info => info.ProcessId == worker.Id);
    }

    private async Task<Process> StartWorkerAsync(string executable, string gate, string? appImage = null)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in new[] { "--root", Path.Combine(_scratch, "root"), "--namespace",
            Guid.NewGuid().ToString("N"), "--no-parser", "--idle-ms", "120000" })
            start.ArgumentList.Add(argument);
        start.Environment.Remove("ICU_DATA");
        start.Environment["MOTIF_TEST_UPDATE_GATE_NAMESPACE"] = gate["SIL.Motif.UpdateActivity.Test.".Length..];
        if (appImage is not null) start.Environment[InstalledWorkerLocator.AppImageVariable] = appImage;
        var process = Process.Start(start) ?? throw new InvalidOperationException("The Worker did not start.");
        _started.Add(process);
        var error = process.StandardError.ReadToEndAsync();
        // The owner name is the first line a Worker writes once it holds ownership and is idling.
        var line = process.StandardOutput.ReadLineAsync();
        if (await Task.WhenAny(line, Task.Delay(TimeSpan.FromSeconds(60))) != line ||
            string.IsNullOrEmpty(await line))
            throw new InvalidOperationException("The Worker never reported ready. " +
                (process.HasExited ? await error : "It is still starting."));
        return process;
    }
}
