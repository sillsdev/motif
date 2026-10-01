using System.Diagnostics;
using System.Globalization;
using SIL.Motif.Commands;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker;
using Xunit;

namespace SIL.Motif.Tests.Integration;

/// <summary>Covers the CLI's durable enqueue-and-kick process boundary with a competing runner owner.</summary>
[Collection(LcmCacheTestCollection.Name)]
public sealed class RunnerKickRaceTests : IDisposable
{
    private readonly PristineProjectFixture _projects;
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "motif-kick-" + Guid.NewGuid().ToString("N"));
    private readonly string _ownerNamespace = "motif-kick-" + Guid.NewGuid().ToString("N");

    public RunnerKickRaceTests(PristineProjectFixture projects)
    {
        _projects = projects;
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public async Task ARealCliKickRunsTheJobAfterTheOtherOwnerOutlastsTheInitialRetry()
    {
        var project = _projects.CopyProjectFile();
        using var occupying = JobRunnerHost.ForNamespace(_ownerNamespace);
        Assert.True(occupying.TryAcquireOwnership());

        var run = await Cli(project);
        Assert.True(run.ExitCode == 0, run.Error);
        var jobId = run.Output.Trim();
        Assert.False(string.IsNullOrWhiteSpace(jobId));
        Assert.Equal(JobStatus.Queued, JobProgress.Read(project, jobId).Status);

        await Task.Delay(TimeSpan.FromSeconds(3));
        occupying.Dispose();

        var completed = JobProgress.WaitUntilFinished(project, jobId, "The kicked runner's Baseline refresh");
        Assert.Equal(JobStatus.Completed, completed.Status);
    }

    [Fact]
    public async Task ACapturingCallerGetsEndOfFileWithoutWaitingForTheRunnerItKicked()
    {
        var project = _projects.CopyProjectFile();
        var elapsed = Stopwatch.StartNew();

        var run = await Cli(project, idleSeconds: 12);

        Assert.True(run.ExitCode == 0, run.Error);
        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(8),
            $"Reading the CLI's output took {elapsed.Elapsed}: the kicked runner held its standard handles.");
    }

    private async Task<CliRun> Cli(string project, int idleSeconds = 2)
    {
        var options = new JobRunnerLaunchOptions(_root, null) { OwnerNamespace = _ownerNamespace };
        var start = CliProcess.Start(options, "baseline-refresh", "--project", project);
        start.Environment[RunnerOptions.IdleVariable] = idleSeconds.ToString(CultureInfo.InvariantCulture);
        start.Environment.Remove(ProcessRunnerLauncher.SuppressVariable);
        using var process = Process.Start(start)!;
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        var exited = process.WaitForExitAsync();

        try
        {
            await Task.WhenAll(outputTask, errorTask, exited).WaitAsync(CliBound);
        }
        catch (TimeoutException)
        {
            try { process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
            try { await Task.WhenAll(outputTask, errorTask, exited).WaitAsync(TimeSpan.FromSeconds(10)); }
            catch (TimeoutException) { }
            var output = outputTask.IsCompletedSuccessfully ? outputTask.Result : "(stdout did not close)";
            var error = errorTask.IsCompletedSuccessfully ? errorTask.Result : "(stderr did not close)";
            Assert.Fail("The CLI did not exit and close its output within " + CliBound + "." +
                Environment.NewLine + "Standard error:" + Environment.NewLine + error +
                Environment.NewLine + "Standard output:" + Environment.NewLine + output);
        }

        return new CliRun(process.ExitCode, outputTask.Result, errorTask.Result);
    }

    private static readonly TimeSpan CliBound = TimeSpan.FromMinutes(2);

    private sealed record CliRun(int ExitCode, string Output, string Error);

    public void Dispose()
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            try { Directory.Delete(_root, true); return; }
            catch (DirectoryNotFoundException) { return; }
            catch (IOException) { Thread.Sleep(250); }
            catch (UnauthorizedAccessException) { Thread.Sleep(250); }
        }
    }
}
