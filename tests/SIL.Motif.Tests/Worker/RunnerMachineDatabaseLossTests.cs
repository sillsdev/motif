using System.Diagnostics;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker;
using Xunit;

namespace SIL.Motif.Tests.Worker;

/// <summary>
/// A runner whose machine database disappears reports it and exits, rather than dying from an unhandled
/// exception, which Windows would surface as a crash dialog nobody can answer.
/// </summary>
public sealed class RunnerMachineDatabaseLossTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "motif-runner-db-loss-" + Guid.NewGuid().ToString("N"));
    private Process? _runner;

    [Fact]
    public void ARunnerWhoseRootIsDeletedReportsItAndExitsWithTheEscapedFailureCode()
    {
        Directory.CreateDirectory(_root);
        var start = new ProcessStartInfo(BuildOutput.Worker)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add("--idle-ms");
        start.ArgumentList.Add("120000");
        start.Environment[RunnerOptions.RootVariable] = _root;
        start.Environment[RunnerOptions.NamespaceVariable] = "motif-db-loss-" + Guid.NewGuid().ToString("N");
        _runner = Process.Start(start)!;
        var error = _runner.StandardError.ReadToEndAsync();
        Assert.False(string.IsNullOrWhiteSpace(_runner.StandardOutput.ReadLine()), "The runner never started.");
        // The name is printed before the database opens; deleting any earlier would let the runner recreate it.
        var database = Path.Combine(_root, "motif.db");
        var opened = DateTime.UtcNow + TimeSpan.FromSeconds(20);
        while (!File.Exists(database) && DateTime.UtcNow < opened) Thread.Sleep(50);
        Assert.True(File.Exists(database), "The runner never opened its machine database.");

        DeleteWithRetry(_root);

        Assert.True(_runner.WaitForExit(30_000), "The runner kept running after its machine database was deleted.");
        Assert.Equal(FailureEnvelope.ExitCodeFor(FailureReason.StoreInconsistent), _runner.ExitCode);
        Assert.Contains("machine database", error.GetAwaiter().GetResult(), StringComparison.OrdinalIgnoreCase);
    }

    // The runner holds its files only while a sweep touches them, so a deletion succeeds between sweeps.
    private static void DeleteWithRetry(string path)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
        while (true)
        {
            try
            {
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (IOException) when (DateTime.UtcNow < deadline) { Thread.Sleep(50); }
            catch (UnauthorizedAccessException) when (DateTime.UtcNow < deadline) { Thread.Sleep(50); }
        }
    }

    public void Dispose()
    {
        try { if (_runner is { HasExited: false }) _runner.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
        _runner?.Dispose();
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }
}
