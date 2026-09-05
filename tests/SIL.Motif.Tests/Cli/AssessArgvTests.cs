using System;
using System.Diagnostics;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Generator;
using SIL.Motif.Host.Parser;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker;
using Xunit;

namespace SIL.Motif.Tests.Cli;

/// <summary>
/// Drives the real <c>motif.exe</c> for <c>assess</c>, restricted to the argv-shaped cases that fail
/// before any subprocess starts: usage, unparseable flags, a missing <c>--words</c> file (Gap 1), and a
/// nonexistent project. A run that actually reaches the Assessor needs a real <c>pangloss</c> executable
/// resolvable on the machine, which this suite does not assume.
/// </summary>
/// <remarks>
/// <c>AssessCommand.Assess</c> resolves the <c>pangloss</c> executable while constructing its Assessor,
/// before it ever checks whether the project exists, so even the nonexistent-project case needs a
/// resolvable executable to reach that check at all. Pointing <see cref="PanGlossExecutable.PathVariable"/>
/// at the built <see cref="FakeParser"/> satisfies that construction without starting any subprocess: the
/// refusal fires before <c>ProduceAsync</c> or <c>QueryAsync</c> is ever called.
/// </remarks>
public sealed class AssessArgvTests : IDisposable
{
    private readonly string _workerRoot =
        Path.Combine(Path.GetTempPath(), "motif-assess-argv-" + Guid.NewGuid().ToString("N"));

    public AssessArgvTests()
    {
        Directory.CreateDirectory(_workerRoot);
    }

    public void Dispose()
    {
        try { Directory.Delete(_workerRoot, recursive: true); }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public void OmittingTheProjectIsAUsageFailureNamingTheVerb()
    {
        var result = Run("assess");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Usage: motif assess <project>", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnparseableTextsGuidListIsAUsageFailure()
    {
        var missing = Path.Combine(_workerRoot, "absent.fwdata");

        var result = Run($"assess \"{missing}\" --texts not-a-guid");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Usage: motif assess <project>", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnparseableRetrySlowerThanIsAUsageFailure()
    {
        var missing = Path.Combine(_workerRoot, "absent.fwdata");

        var result = Run($"assess \"{missing}\" --retry-slower-than not-a-number");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Usage: motif assess <project>", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void ANegativeRetrySlowerThanIsAUsageFailure()
    {
        var missing = Path.Combine(_workerRoot, "absent.fwdata");

        var result = Run($"assess \"{missing}\" --retry-slower-than -5");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Usage: motif assess <project>", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void AMissingWordsFileIsAUsageFailureNotAStoreInconsistency()
    {
        var missingProject = Path.Combine(_workerRoot, "absent.fwdata");
        var missingWordsFile = Path.Combine(_workerRoot, "absent-words.txt");

        var result = Run($"assess \"{missingProject}\" --words \"{missingWordsFile}\"");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("does not exist", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void AMissingWordsFileAsJsonReportsInvalidArgumentNotStoreInconsistent()
    {
        var missingProject = Path.Combine(_workerRoot, "absent.fwdata");
        var missingWordsFile = Path.Combine(_workerRoot, "absent-words.txt");

        var result = Run($"assess \"{missingProject}\" --words \"{missingWordsFile}\" --json");

        var envelope = Envelope(result.Error);
        Assert.Equal(FailureReason.InvalidArgument, envelope.Reason);
        Assert.Equal(1, result.ExitCode);
    }

    [Fact]
    public void ANonexistentProjectRefusesTheWayEveryOtherVerbDoes()
    {
        var missing = Path.Combine(_workerRoot, "absent.fwdata");

        var result = Run($"assess \"{missing}\" --all-wordforms --json", FakeParser.ExecutablePath);

        var envelope = Envelope(result.Error);
        Assert.Equal("project.not-found", envelope.Code);
        Assert.Equal(FailureReason.InvalidArgument, envelope.Reason);
        Assert.Equal(1, result.ExitCode);
    }

    private static FailureEnvelope Envelope(string stderr) =>
        ProjectionJson.Deserialize<FailureEnvelope>(stderr)!;

    private CliRun Run(string arguments, string? panGlossExecutablePath = null)
    {
        var executable = Path.Combine(
            RepoPaths.FindRepoRoot(), "src", "SIL.Motif.Cli", "bin", "Debug", "net10.0", "motif.exe");
        var start = new ProcessStartInfo(executable)
        {
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.Environment[RunnerOptions.RootVariable] = _workerRoot;
        if (panGlossExecutablePath is not null)
            start.Environment[PanGlossExecutable.PathVariable] = panGlossExecutablePath;
        using var process = Process.Start(start)!;
        // Both pipes drain concurrently: a sequential read deadlocks past the pipe buffer.
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        var output = outputTask.GetAwaiter().GetResult();
        var error = errorTask.GetAwaiter().GetResult();
        Assert.True(process.WaitForExit(60000), "The CLI did not exit within its bound.");
        return new CliRun(process.ExitCode, output, error);
    }

    private sealed record CliRun(int ExitCode, string Output, string Error);
}
