using SIL.Motif.Tests.TestFixtures;
using System;
using System.IO;
using System.Linq;
using SIL.Motif.Cli;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Cli;

/// <summary>Pins catalog dispatch and release policy through the in-process CLI.</summary>
public sealed class ReleaseSurfaceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-release-surface-" + Guid.NewGuid().ToString("N"));

    public ReleaseSurfaceTests() => Directory.CreateDirectory(_root);

    [Theory]
    [InlineData("collect-change")]
    [InlineData("remove-collected-change")]
    [InlineData("remove-nonfitting-changes")]
    [InlineData("preflight --draft")]
    public void RetiredDraftChangeCommandsHaveNoEntryPoint(string name)
    {
        Assert.DoesNotContain(CommandCatalog.All, command => command.Name == name);
        Assert.DoesNotContain(CliVerbCatalog.All, command => command.CommandName == name);
    }

    [Fact]
    public void AllPendingAfterTheOptionTerminatorIsRefusedAsDeveloperOnly()
    {
        var result = Run("apply P --project p --user u --force --json -- --all-pending", developerCommands: false);

        Assert.Equal(FailureEnvelope.ExitCodeFor(FailureReason.Refused), result.ExitCode);
        Assert.Empty(result.Output);
        var envelope = ProjectionJson.Deserialize<FailureEnvelope>(result.Error)!;
        Assert.Equal("command.not-in-release", envelope.Code);
        Assert.Equal(FailureReason.Refused, envelope.Reason);
        Assert.Contains("Command 'apply'", envelope.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplyingOneProposalIsRefusedAsDeveloperOnly()
    {
        var result = Run("apply P", developerCommands: false);

        Assert.Equal(FailureEnvelope.ExitCodeFor(FailureReason.Refused), result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("Command 'apply' is not part of Motif", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void DocumentedUsageTokensResolveThroughTheInProcessCli()
    {
        foreach (var descriptor in CliVerbCatalog.All)
        {
            var usage = descriptor.UsageLines.FirstOrDefault();
            var line = usage is null ? descriptor.CommandName : usage;
            var marker = line.IndexOf(" OR motif ", StringComparison.Ordinal);
            if (marker >= 0) line = line[..marker];
            if (line.StartsWith("Usage: motif ", StringComparison.Ordinal))
                line = line["Usage: motif ".Length..];
            else if (line.StartsWith("motif ", StringComparison.Ordinal))
                line = line["motif ".Length..];
            var invocation = line.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Take(2).ToArray();

            var command = CommandCatalog.All.Single(item => item.Name == descriptor.CommandName);
            var surface = typeof(CommandDescriptor).GetProperty("Surface")!
                .GetValue(command)?.ToString();
            var result = Run(string.Join(' ', invocation), developerCommands: surface == "Developer");
            Assert.DoesNotContain("Unknown command", result.Error, StringComparison.Ordinal);
            Assert.DoesNotContain("command.not-in-release", result.Error, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("recheck-pending-changes", "--expected-revision")]
    [InlineData("review-numbers", "--touched-words")]
    public void ReviewCommandsHaveDeveloperCliUsage(string verb, string requiredFlag)
    {
        var result = Run(verb, developerCommands: true);

        Assert.Contains("Usage: motif " + verb, result.Error, StringComparison.Ordinal);
        Assert.Contains(requiredFlag, result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnknownCompoundVerbIsResolvedToTheUsageFailurePath()
    {
        var result = Run("jobs future", developerCommands: false);

        Assert.Equal(FailureEnvelope.ExitCodeFor(FailureReason.InvalidArgument), result.ExitCode);
        Assert.Contains("Usage: motif jobs", result.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("__unresolved_command__", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void TheEnvironmentPolicyEnablesOnlyTheDeveloperOptInValue()
    {
        Assert.False(CommandSurfacePolicy.FromEnvironment(null).DeveloperCommandsEnabled);
        Assert.False(CommandSurfacePolicy.FromEnvironment(string.Empty).DeveloperCommandsEnabled);
        Assert.False(CommandSurfacePolicy.FromEnvironment("0").DeveloperCommandsEnabled);
        Assert.False(CommandSurfacePolicy.FromEnvironment("true").DeveloperCommandsEnabled);
        Assert.True(CommandSurfacePolicy.FromEnvironment("1").DeveloperCommandsEnabled);
    }

    [Fact]
    public void ReleasedHelpContainsOnlyReleasedCommandsAndNoEmptySections()
    {
        var result = Run(string.Empty, developerCommands: false);
        var releasedNames = CommandCatalog.All
            .Where(command => command.Surface == CommandSurface.Released)
            .Select(command => command.Name);
        var developerNames = CommandCatalog.All
            .Where(command => command.Surface == CommandSurface.Developer)
            .Select(command => command.Name);

        Assert.Equal(1, result.ExitCode);
        foreach (var name in releasedNames)
            Assert.Contains(name, result.Error, StringComparison.Ordinal);
        foreach (var name in developerNames)
        {
            var usagePrefix = name == "apply" ? "  apply <proposalId> " : "  " + name + " ";
            var usageLine = result.Error.Split(Environment.NewLine)
                .FirstOrDefault(line => line.StartsWith(usagePrefix, StringComparison.Ordinal));
            Assert.Null(usageLine);
        }
        Assert.Contains("Configuration (the declared", result.Error, StringComparison.Ordinal);
        Assert.Contains("The default per-word step cap is 1,000,000 steps.", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryDeveloperCommandIsRefusedOnTheReleasedSurfaceInTextAndJson()
    {
        foreach (var name in CommandCatalog.All
                     .Where(command => command.Surface == CommandSurface.Developer)
                     .Select(command => command.Name))
        {
            var text = Run(name, developerCommands: false);
            var expectedExitCode = FailureEnvelope.ExitCodeFor(FailureReason.Refused);
            Assert.True(text.ExitCode == expectedExitCode,
                $"Expected exit code {expectedExitCode}; got {text.ExitCode}.{Environment.NewLine}{text.Error}");
            Assert.Contains(name, text.Error, StringComparison.Ordinal);
            Assert.Contains("not part of Motif 0.1.0", text.Error, StringComparison.Ordinal);

            var json = Run(name + " --json", developerCommands: false);
            Assert.True(json.ExitCode == expectedExitCode,
                $"Expected exit code {expectedExitCode}; got {json.ExitCode}.{Environment.NewLine}{json.Error}");
            Assert.Equal(string.Empty, json.Output);
            var envelope = ProjectionJson.Deserialize<FailureEnvelope>(json.Error)!;
            Assert.Equal(FailureReason.Refused, envelope.Reason);
            Assert.Equal("command.not-in-release", envelope.Code);
            Assert.Contains(name, envelope.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ReleasedDeveloperCommandRefusalIsJsonAndDoesNotRegisterTheProject()
    {
        var projectPath = Path.Combine(_root, "Project.fwdata");
        File.WriteAllText(projectPath, string.Empty);

        var result = Run($"new --project \"{projectPath}\" --json", developerCommands: false);

        Assert.Equal(FailureEnvelope.ExitCodeFor(FailureReason.Refused), result.ExitCode);
        Assert.Equal(string.Empty, result.Output);
        var envelope = ProjectionJson.Deserialize<FailureEnvelope>(result.Error)!;
        Assert.Equal(FailureReason.Refused, envelope.Reason);
        Assert.Equal("command.not-in-release", envelope.Code);
        Assert.Contains("new", envelope.Message, StringComparison.Ordinal);
        Assert.Contains("Motif 0.1.0", envelope.Message, StringComparison.Ordinal);

        using var machine = MachineDatabase.Open(Path.Combine(_root, "machine"));
        Assert.Empty(new KnownProjectRegistry(machine).List());
    }

    [Fact]
    public void DeveloperCommandsDispatchWhenTheOptInIsSet()
    {
        var result = Run("new", developerCommands: true);

        Assert.Equal(1, result.ExitCode);
        Assert.DoesNotContain("not part of Motif 0.1.0", result.Error, StringComparison.Ordinal);
        Assert.Contains("Usage: motif new", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void StatsRequestHasNoProposalSelector()
    {
        var parameters = typeof(SIL.Motif.Contract.Requests.StatsRequest)
            .GetConstructors()
            .Single()
            .GetParameters();

        Assert.DoesNotContain(parameters, parameter => parameter.Name == "proposalId");
        Assert.Null(typeof(SIL.Motif.App.ViewModels.StatisticsViewModel).GetProperty("ProposalId"));
    }

    [Fact]
    public void BareReleasedCommandsReturnTheirUsageFailureOrDocumentedResult()
    {
        foreach (var name in CommandCatalog.All
                     .Where(command => command.Surface == CommandSurface.Released)
                     .Select(command => command.Name))
        {
            var result = Run(name + " --json", developerCommands: false);

            if (name == "report --list-kinds")
            {
                Assert.True(result.ExitCode == 0, result.Error);
                Assert.Empty(result.Error);
                Assert.NotEmpty(ProjectionJson.Deserialize<ReportKindListResponse>(result.Output)!.Kinds);
            }
            else
            {
                Assert.True(result.ExitCode == FailureEnvelope.ExitCodeFor(FailureReason.InvalidArgument),
                    $"{name}: exit {result.ExitCode}: {result.Error}");
                Assert.Empty(result.Output);
                var failure = ProjectionJson.Deserialize<FailureEnvelope>(result.Error)!;
                Assert.Equal(FailureReason.InvalidArgument, failure.Reason);
                Assert.Null(failure.Code);
                Assert.Contains(name.Split(' ')[0], failure.Message, StringComparison.Ordinal);
            }
        }
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }

    private CliRun Run(string arguments, bool developerCommands)
    {
        var result = CliInProcess.RunCommandLine(
            Path.Combine(_root, "machine"), FakeParser.ExecutablePath, developerCommands, arguments);
        return new CliRun(result.ExitCode, result.Output, result.Error);
    }

    private sealed record CliRun(int ExitCode, string Output, string Error);
}
