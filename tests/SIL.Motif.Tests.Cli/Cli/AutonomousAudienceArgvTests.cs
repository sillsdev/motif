using System.Diagnostics;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Cli;

/// <summary>Pins how the CLI gates the Autonomous audience: by the environment variable, the flag, or neither.</summary>
public sealed class AutonomousAudienceArgvTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-autonomous-" + Guid.NewGuid().ToString("N"));

    public AutonomousAudienceArgvTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void WithNeitherSwitchDiscardDraftIsRefusedNamingTheAutonomousSwitch()
    {
        var result = Run("discard-draft --json", developer: false, autonomousEnvironment: null);

        Assert.Equal(FailureEnvelope.ExitCodeFor(FailureReason.Refused), result.ExitCode);
        Assert.Contains("command.autonomous-required", result.Error, StringComparison.Ordinal);
        Assert.Contains("Autonomous-mode command", result.Error, StringComparison.Ordinal);
        Assert.Contains("MOTIF_AUTONOMOUS=1", result.Error, StringComparison.Ordinal);
        Assert.Contains("--autonomous", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void TheDeveloperSwitchAloneStillRefusesAndNamesTheAutonomousSwitch()
    {
        var result = Run("discard-empty-drafts --json", developer: true, autonomousEnvironment: null);

        Assert.Equal(FailureEnvelope.ExitCodeFor(FailureReason.Refused), result.ExitCode);
        Assert.Contains("command.autonomous-required", result.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("MOTIF_DEVELOPER_COMMANDS", result.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("true")]
    public void TheEnvironmentVariableDispatchesDiscardDraft(string value)
    {
        var result = Run("discard-draft", developer: false, autonomousEnvironment: value);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Usage: motif discard-draft", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void TheGlobalFlagDispatchesDiscardDraft()
    {
        var result = Run("discard-draft --autonomous", developer: false, autonomousEnvironment: null);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Usage: motif discard-draft", result.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("preflight --project missing.fwdata --autonomous P-1")]
    [InlineData("--autonomous preflight --project missing.fwdata P-1")]
    public void TheAutonomousFlagDoesNotSwallowAPositionalArgument(string commandLine)
    {
        var result = Run(commandLine, developer: false, autonomousEnvironment: null);

        Assert.DoesNotContain("Usage: motif preflight", result.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("command.autonomous-required", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void TheDeveloperSurfaceStillNeedsItsOwnSwitchEvenWhenAutonomousIsSet()
    {
        var result = Run("word read-state --json --autonomous", developer: false, autonomousEnvironment: "1");

        Assert.Equal(FailureEnvelope.ExitCodeFor(FailureReason.Refused), result.ExitCode);
        Assert.Contains("command.developer-commands-required", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void HelpListsAutonomousCommandsWhenEnabledAndSaysHowToEnableThemWhenNot()
    {
        var off = Run("help", developer: false, autonomousEnvironment: null).Output;
        var on = Run("help", developer: false, autonomousEnvironment: "1").Output;

        Assert.Contains("Autonomous commands (set MOTIF_AUTONOMOUS=1 or pass --autonomous", off, StringComparison.Ordinal);
        Assert.Contains("Autonomous commands (enabled):", on, StringComparison.Ordinal);
        Assert.Contains("discard-draft", on, StringComparison.Ordinal);
        Assert.DoesNotContain("discard-draft", off.Split("Autonomous commands")[0], StringComparison.Ordinal);
    }

    [Fact]
    public void HelpAutonomousExplainsTheAudienceAndListsItsCommands()
    {
        var result = Run("help autonomous", developer: false, autonomousEnvironment: null);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("MOTIF_AUTONOMOUS=1", result.Output, StringComparison.Ordinal);
        Assert.Contains("never MCP tools", result.Output, StringComparison.Ordinal);
        Assert.Contains("discard-draft", result.Output, StringComparison.Ordinal);
        Assert.Contains("discard-empty-drafts", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void TheDiscardCommandsHaveHelpPages()
    {
        var result = Run("help discard-draft --full --autonomous", developer: false, autonomousEnvironment: null);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("pending-changes Draft", result.Output, StringComparison.Ordinal);
        Assert.Contains("never an MCP tool", result.Output, StringComparison.Ordinal);
    }

    private (int ExitCode, string Output, string Error) Run(string commandLine, bool developer, string? autonomousEnvironment)
    {
        var start = CliProcess.CreateStartInfo(Path.Combine(_root, "machine"), FakeParser.ExecutablePath, developer);
        if (autonomousEnvironment is not null)
            start.Environment[CommandSurfacePolicy.AutonomousEnvironmentVariable] = autonomousEnvironment;
        return CliInProcess.RunCommandLine(start, commandLine);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }
}
