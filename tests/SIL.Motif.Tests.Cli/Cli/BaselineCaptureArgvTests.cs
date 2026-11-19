using System;
using System.Diagnostics;
using SIL.Motif.Commands.Store;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker;
using Xunit;

namespace SIL.Motif.Tests.Cli;

/// <summary>
/// Drives the real <c>motif</c> apphost for <c>baseline capture</c>: a successful capture's exit code, its
/// human and JSON renderings, and the usage/refusal shape of a malformed or absent invocation.
/// </summary>
[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group1)]
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

        var result = Run($"baseline capture --project \"{fwDataPath}\"");

        Assert.True(result.ExitCode == 0, $"exit {result.ExitCode}: {result.Error}{result.Output}");
        Assert.Equal(string.Empty, result.Error);
        Assert.Contains("as of FieldWorks' last save", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void CapturingAsJsonBindsToTheTypedResponse()
    {
        var fwDataPath = _pristine.CopyProjectFile();

        var result = Run($"baseline capture --project \"{fwDataPath}\" --json");

        Assert.True(result.ExitCode == 0, $"exit {result.ExitCode}: {result.Error}{result.Output}");
        var response = ProjectionJson.Deserialize<BaselineCaptureResponse>(result.Output);
        Assert.NotNull(response);
        Assert.False(response!.ReusedExistingBytes);
        Assert.False(response.FieldWorksHeldProject);
        Assert.True(File.Exists(response.FwDataPath));
    }

    // Issue 296: no Source index or Parsimony job may exist, and no runner may be woken, once the command returns.
    [Fact]
    public void NoBackgroundCapturesAUsableBaselineAndLeavesNoJobBehind()
    {
        var fwDataPath = _pristine.CopyProjectFile();

        var result = Run($"baseline capture --project \"{fwDataPath}\" --no-background --json");

        Assert.True(result.ExitCode == 0, $"exit {result.ExitCode}: {result.Error}{result.Output}");
        Assert.Equal(string.Empty, result.Error);
        Assert.NotNull(ProjectionJson.Deserialize<BaselineCaptureResponse>(result.Output));
        var kinds = ProjectStoreCommand.Run(fwDataPath, MotifProductVersion.CurrentText, (database, project) =>
            SIL.Motif.Contract.Commands.CommandOutcome<string[]>.Success(
                new JobRepository(database).ListActive(ProjectWorkspaceKey.Compute(project))
                    .Select(job => job.Kind).ToArray())).Value!;
        Assert.Empty(kinds);
    }

    [Fact]
    public void UsageNamesTheNoBackgroundSwitch()
    {
        var result = Run("baseline capture");

        Assert.Contains("[--no-background]", result.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("baseline capture --project x.fwdata --no-background=false")]
    [InlineData("assess --project x.fwdata --no-background=false")]
    [InlineData("dry-run --project x.fwdata P --wait --no-background=false")]
    [InlineData("trial --project x.fwdata P --wait --no-background=0")]
    [InlineData("apply --project x.fwdata --no-background=false")]
    [InlineData("baseline refresh-please --project x.fwdata --no-background")]
    public void ANoBackgroundValueIsRefusedNotTreatedAsTrue(string command)
    {
        var result = Run(command);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("--no-background is a switch without a value", result.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("dry-run --project x.fwdata P --no-background")]
    [InlineData("trial --project x.fwdata P --no-background")]
    public void ForegroundDryRunAndTrialNeedWait(string command)
    {
        var result = Run(command);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("--no-background needs --wait", result.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("assess --project x.fwdata --no-background")]
    [InlineData("apply --project x.fwdata --no-background")]
    public void TheNoBackgroundSwitchPassesTheGateOnAssessAndApply(string command)
    {
        var result = Run(command);

        Assert.DoesNotContain("--no-background is a switch without a value", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void OmittingTheProjectIsAUsageFailureNamingTheVerb()
    {
        var result = Run("baseline capture");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Usage: motif baseline capture --project <fwdata>", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnknownBaselineSubcommandIsAUsageFailure()
    {
        var result = Run("baseline refresh-please");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Usage: motif baseline capture --project <fwdata>", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void ANonexistentProjectRefusesTheWayEveryOtherVerbDoes()
    {
        var missing = Path.Combine(_workerRoot, "absent.fwdata");

        var result = Run($"baseline capture --project \"{missing}\" --json");

        var envelope = Envelope(result.Error);
        // Code carries not-found; reason carries retry class. Pinned for other verbs by FailureContractTests.
        Assert.Equal("project.not-found", envelope.Code);
        Assert.Equal(FailureReason.InvalidArgument, envelope.Reason);
        Assert.Equal(1, result.ExitCode);
    }

    [Fact]
    public void AKnownProjectNameCanBeCapturedAndTheOldPositionalFormIsRefused()
    {
        var fwDataPath = _pristine.CopyProjectFile();
        var capture = Run($"baseline capture --project \"{fwDataPath}\" --json");
        Assert.Equal(0, capture.ExitCode);

        var projectName = Path.GetFileNameWithoutExtension(fwDataPath);
        var byName = Run($"baseline capture --project \"{projectName}\" --json");
        Assert.Equal(0, byName.ExitCode);
        Assert.NotNull(ProjectionJson.Deserialize<BaselineCaptureResponse>(byName.Output));

        var oldForm = Run($"baseline capture \"{fwDataPath}\" --json");
        Assert.Equal(1, oldForm.ExitCode);
        Assert.Empty(oldForm.Output);
        var refusal = Envelope(oldForm.Error);
        Assert.Contains("Usage: motif baseline capture --project <fwdata>", refusal.Message,
            StringComparison.Ordinal);
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
        Assert.True(process.WaitForExit((int)Wait.Scaled(TimeSpan.FromMinutes(30)).TotalMilliseconds),
            "The CLI process did not exit within 30 minutes (scaled by MOTIF_TEST_DEADLINE_SCALE).");
        return new CliRun(process.ExitCode, output, error);
    }

    private sealed record CliRun(int ExitCode, string Output, string Error);
}
