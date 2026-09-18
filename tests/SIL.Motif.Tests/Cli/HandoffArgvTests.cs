using System;
using System.Diagnostics;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker;
using Xunit;

namespace SIL.Motif.Tests.Cli;

/// <summary>
/// Drives the real <c>motif.exe</c> for <c>handoff</c>, restricted to the argv-shaped cases that fail
/// before any subprocess starts: usage, an unparseable <c>--texts</c> list, and a nonexistent project. A
/// run that actually writes a folder needs a real <c>pangloss</c> executable resolvable on the machine,
/// which this suite does not assume.
/// </summary>
public sealed class HandoffArgvTests : IDisposable
{
    private readonly string _workerRoot =
        Path.Combine(Path.GetTempPath(), "motif-handoff-argv-" + Guid.NewGuid().ToString("N"));

    public HandoffArgvTests()
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
        var result = Run("handoff");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Usage: motif handoff <project>", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void OmittingOutIsAUsageFailure()
    {
        var missing = Path.Combine(_workerRoot, "absent.fwdata");

        var result = Run($"handoff \"{missing}\"");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Usage: motif handoff <project>", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnparseableTextsGuidListIsAUsageFailure()
    {
        var missing = Path.Combine(_workerRoot, "absent.fwdata");
        var outDir = Path.Combine(_workerRoot, "out");

        var result = Run($"handoff \"{missing}\" --out \"{outDir}\" --texts not-a-guid");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Usage: motif handoff <project>", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void TextsCannotBeCombinedWithAnInvocation()
    {
        var missing = Path.Combine(_workerRoot, "absent.fwdata");
        var outDir = Path.Combine(_workerRoot, "out");

        var result = Run($"handoff \"{missing}\" --out \"{outDir}\" --invocation invocation/one --texts not-a-guid");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Usage: motif handoff <project>", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void InvocationOrNoAssessIsRequiredBeforeTheProjectIsAccessed()
    {
        var missing = Path.Combine(_workerRoot, "absent.fwdata");
        var outDir = Path.Combine(_workerRoot, "out");

        var result = Run($"handoff \"{missing}\" --out \"{outDir}\"");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Usage: motif handoff <project>", result.Error, StringComparison.Ordinal);
    }

    // No parser is pointed at deliberately: the project must be resolved before one is ever built.
    [Fact]
    public void ANonexistentProjectRefusesTheWayEveryOtherVerbDoes()
    {
        var missing = Path.Combine(_workerRoot, "absent.fwdata");
        var outDir = Path.Combine(_workerRoot, "out");

        var result = Run($"handoff \"{missing}\" --out \"{outDir}\" --no-assess --json");

        var envelope = Envelope(result.Error);
        Assert.Equal("project.not-found", envelope.Code);
        Assert.Equal(FailureReason.InvalidArgument, envelope.Reason);
        Assert.Equal(1, result.ExitCode);
    }

    [Fact]
    public void AnExistingNonEmptyOutputDirectoryRefusesBeforeTheProjectEvenMatters()
    {
        var missing = Path.Combine(_workerRoot, "absent.fwdata");
        var outDir = Path.Combine(_workerRoot, "out");
        Directory.CreateDirectory(outDir);
        File.WriteAllText(Path.Combine(outDir, "keep.txt"), "do not touch");

        var result = Run($"handoff \"{missing}\" --out \"{outDir}\" --no-assess --json");

        var envelope = Envelope(result.Error);
        Assert.Equal("handoff.destination-exists", envelope.Code);
        Assert.Equal(1, result.ExitCode);
        Assert.True(File.Exists(Path.Combine(outDir, "keep.txt")));
    }

    private static FailureEnvelope Envelope(string stderr) =>
        ProjectionJson.Deserialize<FailureEnvelope>(stderr)!;

    private CliRun Run(string arguments)
    {
        var executable = BuildOutput.Cli;
        var start = new ProcessStartInfo(executable)
        {
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.Environment[RunnerOptions.RootVariable] = _workerRoot;
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
