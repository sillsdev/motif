using System;
using System.Diagnostics;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Generator;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker;
using Xunit;

namespace SIL.Motif.Tests.Cli;

/// <summary>
/// Drives the real <c>motif.exe</c> for <c>baseline capture</c>: a successful capture's exit code, its
/// human and JSON renderings, and the usage/refusal shape of a malformed or absent invocation.
/// </summary>
[Collection(LcmCacheTestCollection.Name)]
public sealed class BaselineCaptureArgvTests : IDisposable
{
    private readonly PristineProjectFixture _pristine;
    private readonly string _workerRoot =
        Path.Combine(Path.GetTempPath(), "motif-baseline-capture-argv-" + Guid.NewGuid().ToString("N"));

    public BaselineCaptureArgvTests(PristineProjectFixture pristine)
    {
        _pristine = pristine;
        Directory.CreateDirectory(_workerRoot);
    }

    public void Dispose()
    {
        try { Directory.Delete(_workerRoot, recursive: true); }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public void CapturingARealProjectSucceedsAndPrintsHumanTextNamingTheFreshness()
    {
        var fwDataPath = _pristine.CopyProjectFile();

        var result = Run($"baseline capture \"{fwDataPath}\"");

        Assert.True(result.ExitCode == 0, $"exit {result.ExitCode}: {result.Error}{result.Output}");
        Assert.Equal(string.Empty, result.Error);
        Assert.Contains("as of FieldWorks' last save", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void CapturingAsJsonBindsToTheTypedResponse()
    {
        var fwDataPath = _pristine.CopyProjectFile();

        var result = Run($"baseline capture \"{fwDataPath}\" --json");

        Assert.True(result.ExitCode == 0, $"exit {result.ExitCode}: {result.Error}{result.Output}");
        var response = ProjectionJson.Deserialize<BaselineCaptureResponse>(result.Output);
        Assert.NotNull(response);
        Assert.False(response!.ReusedExistingBytes);
        Assert.False(response.FieldWorksHeldProject);
        Assert.True(File.Exists(response.FwDataPath));
    }

    [Fact]
    public void OmittingTheProjectIsAUsageFailureNamingTheVerb()
    {
        var result = Run("baseline capture");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Usage: motif baseline capture <project>", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnknownBaselineSubcommandIsAUsageFailure()
    {
        var result = Run("baseline refresh-please");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Usage: motif baseline capture <project>", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void ANonexistentProjectRefusesTheWayEveryOtherVerbDoes()
    {
        var missing = Path.Combine(_workerRoot, "absent.fwdata");

        var result = Run($"baseline capture \"{missing}\" --json");

        var envelope = Envelope(result.Error);
        // Code carries not-found; reason carries retry class. Pinned for other verbs by FailureContractTests.
        Assert.Equal("project.not-found", envelope.Code);
        Assert.Equal(FailureReason.InvalidArgument, envelope.Reason);
        Assert.Equal(1, result.ExitCode);
    }

    private static FailureEnvelope Envelope(string stderr) =>
        ProjectionJson.Deserialize<FailureEnvelope>(stderr)!;

    private CliRun Run(string arguments)
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
