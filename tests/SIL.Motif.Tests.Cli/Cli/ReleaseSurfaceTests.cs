using SIL.Motif.Tests.TestFixtures;
using System;
using System.IO;
using System.Linq;
using SIL.Motif.Cli;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Store;
using SIL.Motif.Host;
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
    public void AllPendingAfterTheOptionTerminatorDoesNotSelectReleasedApply()
    {
        var result = Run("apply P --project p --force --json -- --all-pending", developerCommands: false);

        Assert.Equal(FailureEnvelope.ExitCodeFor(FailureReason.InvalidArgument), result.ExitCode);
        Assert.Empty(result.Output);
        Assert.DoesNotContain("command.developer-commands-required", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplyEntryPointsAreHumanOnlyAndOnlyReleasedRoutesArePublic()
    {
        var apply = Assert.Single(CommandCatalog.All, command => command.Name == "apply");
        Assert.Equal(CommandSurface.Released, apply.Surface);
        Assert.Equal(AgentClass.HumanOnly, apply.Agent);
        Assert.False(apply.IsAgentTool);
        foreach (var name in new[] { "apply --proposal", "apply --all-pending" })
        {
            var releasedApply = Assert.Single(CommandCatalog.All, command => command.Name == name);
            Assert.Equal(CommandSurface.Released, releasedApply.Surface);
            Assert.Equal(AgentClass.HumanOnly, releasedApply.Agent);
            Assert.False(releasedApply.IsAgentTool);
        }
    }

    [Fact]
    public void PlainApplyDispatchesWithoutDeveloperOptIn()
    {
        var result = Run("apply", developerCommands: false);

        Assert.Equal(FailureEnvelope.ExitCodeFor(FailureReason.InvalidArgument), result.ExitCode);
        Assert.Contains("Usage: motif apply <proposalId>", result.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("MOTIF_DEVELOPER_COMMANDS=1", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void PlainApplyForceDispatchesWithoutDeveloperOptIn()
    {
        var result = Run("apply proposal --project p --force --json", developerCommands: false);

        Assert.DoesNotContain("command.developer-commands-required", result.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("MOTIF_DEVELOPER_COMMANDS=1", result.Error, StringComparison.Ordinal);
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
    public void AdvancedAiCommandsRequireThatPreferenceAndDoNotFollowDeveloperOptIn()
    {
        var command = new CommandDescriptor(
            "future-composer", typeof(string), typeof(string), CommandSurface.AdvancedAi, AgentClass.Draft);

        Assert.False(CommandSurfacePolicy.FromEnvironment("1").IsAvailable(command));
        Assert.False(CommandSurfacePolicy.FromEnvironment(null, advancedAiModeEnabled: false).IsAvailable(command));
        Assert.True(CommandSurfacePolicy.FromEnvironment(null, advancedAiModeEnabled: true).IsAvailable(command));
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
            // `reopen` and `reject` also have a Released `--proposal` form, so their Developer lines are matched whole.
            var usagePrefix = name switch
            {
                "apply" => "  apply <proposalId> ",
                "reopen" or "reject" => "  " + name + " --project ",
                _ => "  " + name + " ",
            };
            var usageLine = result.Error.Split(Environment.NewLine)
                .FirstOrDefault(line => line.StartsWith(usagePrefix, StringComparison.Ordinal));
            Assert.Null(usageLine);
        }
        Assert.Contains("Configuration (the declared", result.Error, StringComparison.Ordinal);
        Assert.Contains($"The default per-word limit is {StepCap.DefaultSteps:N0} analysis attempts.", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryDeveloperCommandIsRefusedOnTheReleasedSurfaceInTextAndJson()
    {
        foreach (var name in CommandCatalog.All
                     .Where(command => command.Surface == CommandSurface.Developer)
                     .Select(command => command.Name))
        {
            var descriptor = CommandCatalog.All.Single(command => command.Name == name);
            var text = Run(name, developerCommands: false);
            var expectedExitCode = FailureEnvelope.ExitCodeFor(FailureReason.Refused);
            Assert.True(text.ExitCode == expectedExitCode,
                $"Expected exit code {expectedExitCode}; got {text.ExitCode}.{Environment.NewLine}{text.Error}");
            Assert.Contains(name, text.Error, StringComparison.Ordinal);
            Assert.Contains("MOTIF_DEVELOPER_COMMANDS=1", text.Error, StringComparison.Ordinal);
            if (descriptor.Agent == AgentClass.HumanOnly)
                Assert.Contains("human-only", text.Error, StringComparison.Ordinal);

            var json = Run(name + " --json", developerCommands: false);
            Assert.True(json.ExitCode == expectedExitCode,
                $"Expected exit code {expectedExitCode}; got {json.ExitCode}.{Environment.NewLine}{json.Error}");
            Assert.Equal(string.Empty, json.Output);
            var envelope = ProjectionJson.Deserialize<FailureEnvelope>(json.Error)!;
            Assert.Equal(FailureReason.Refused, envelope.Reason);
            Assert.Equal("command.developer-commands-required", envelope.Code);
            Assert.Contains(name, envelope.Message, StringComparison.Ordinal);
            Assert.Contains("MOTIF_DEVELOPER_COMMANDS=1", envelope.Message, StringComparison.Ordinal);
            if (descriptor.Agent == AgentClass.HumanOnly)
                Assert.Contains("human-only", envelope.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void AdvancedAiHelpIdentifiesHumanOnlyCommands()
    {
        var result = Run("help \"project initialize\"", developerCommands: false);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Advanced AI mode", result.Output, StringComparison.Ordinal);
        Assert.Contains("MOTIF_ADVANCED_AI_MODE_PATH", result.Output, StringComparison.Ordinal);
        Assert.Contains("human-only", result.Output, StringComparison.Ordinal);
        Assert.Contains("MCP tool", result.Output, StringComparison.Ordinal);
        var json = Run("help \"project initialize\" --json", developerCommands: false);
        var enablement = System.Text.Json.Nodes.JsonNode.Parse(json.Output)!["enablement"]!.GetValue<string>();
        Assert.Contains("human-only", enablement, StringComparison.Ordinal);
        Assert.Contains("MOTIF_ADVANCED_AI_MODE_PATH", enablement, StringComparison.Ordinal);
    }

    [Fact]
    public void DraftAuthoringAndEvaluationUseTheAdvancedAiSurface()
    {
        var authoring = new[]
        {
            "new", "compose-author-phoneme", "compose-batch", "label", "comment", "finalize",
        };
        var evaluating = new[] { "dry-run", "trial" };

        Assert.All(authoring, name => Assert.Equal(CommandSurface.AdvancedAi,
            Assert.Single(CommandCatalog.All, command => command.Name == name).Surface));
        Assert.All(evaluating, name => Assert.Equal(CommandSurface.AdvancedAi,
            Assert.Single(CommandCatalog.All, command => command.Name == name).Surface));

        var apply = Assert.Single(CommandCatalog.All, command => command.Name == "apply --all-pending");
        Assert.Equal(CommandSurface.Released, apply.Surface);
        Assert.Equal(AgentClass.HumanOnly, apply.Agent);
        Assert.False(apply.IsAgentTool);
    }

    [Fact]
    public void AdvancedAiRefusalNamesTheSettingAndSandboxPreferencePath()
    {
        var result = Run("new --project p --draft proposal", developerCommands: false);

        Assert.Equal(FailureEnvelope.ExitCodeFor(FailureReason.Refused), result.ExitCode);
        Assert.Contains("motif settings advanced-ai on", result.Error, StringComparison.Ordinal);
        Assert.Contains("MOTIF_ADVANCED_AI_MODE_PATH", result.Error, StringComparison.Ordinal);

        var json = Run("new --project p --draft proposal --json", developerCommands: false);
        var envelope = ProjectionJson.Deserialize<FailureEnvelope>(json.Error)!;
        Assert.Equal("command.advanced-ai-mode-required", envelope.Code);
        Assert.Contains("motif settings advanced-ai on", envelope.Message, StringComparison.Ordinal);
        Assert.Contains("MOTIF_ADVANCED_AI_MODE_PATH", envelope.Message, StringComparison.Ordinal);
        var humanOnly = Run("project initialize --project p --confirm x", developerCommands: false);
        Assert.Contains("human-only", humanOnly.Error, StringComparison.Ordinal);
        Assert.Contains("MOTIF_ADVANCED_AI_MODE_PATH", humanOnly.Error, StringComparison.Ordinal);
        var humanOnlyJson = Run("project initialize --project p --confirm x --json", developerCommands: false);
        var humanOnlyEnvelope = ProjectionJson.Deserialize<FailureEnvelope>(humanOnlyJson.Error)!;
        Assert.Contains("human-only", humanOnlyEnvelope.Message, StringComparison.Ordinal);
        Assert.Contains("MOTIF_ADVANCED_AI_MODE_PATH", humanOnlyEnvelope.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReleasedDeveloperCommandRefusalIsJsonAndDoesNotRegisterTheProject()
    {
        var projectPath = Path.Combine(_root, "Project.fwdata");
        File.WriteAllText(projectPath, string.Empty);

        var result = Run($"add-delete-lexeme-form --project \"{projectPath}\" --json", developerCommands: false);

        Assert.Equal(FailureEnvelope.ExitCodeFor(FailureReason.Refused), result.ExitCode);
        Assert.Equal(string.Empty, result.Output);
        var envelope = ProjectionJson.Deserialize<FailureEnvelope>(result.Error)!;
        Assert.Equal(FailureReason.Refused, envelope.Reason);
        Assert.Equal("command.developer-commands-required", envelope.Code);
        Assert.Contains("add-delete-lexeme-form", envelope.Message, StringComparison.Ordinal);
        Assert.Contains("MOTIF_DEVELOPER_COMMANDS=1", envelope.Message, StringComparison.Ordinal);

        using var machine = MachineDatabase.Open(Path.Combine(_root, "machine"));
        Assert.Empty(new KnownProjectRegistry(machine).List());
    }

    [Fact]
    public void DeveloperCommandsDispatchWhenTheOptInIsSet()
    {
        var result = Run("new", developerCommands: true);

        Assert.Equal(1, result.ExitCode);
        Assert.DoesNotContain("MOTIF_DEVELOPER_COMMANDS=1", result.Error, StringComparison.Ordinal);
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
