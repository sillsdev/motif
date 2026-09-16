using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Generator;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Cli;

/// <summary>Pins the released command boundary at the real executable and the shared command catalog.</summary>
public sealed class ReleaseSurfaceTests : IDisposable
{
    private static readonly string[] ReleasedNames =
    [
        "open", "analyses", "config show", "report", "report --list-kinds", "compare",
        "baseline capture", "assess", "stats", "handoff", "add-corpus", "add-document",
        "add-corpus-bundle", "corpora", "show-corpus", "baseline-refresh", "jobs show",
        "jobs assessments", "jobs list", "jobs cancel", "jobs requeue", "jobs move",
    ];

    private static readonly string[] DeveloperNames =
    [
        "new", "add-set-gloss", "add-delete-lexeme-form", "compose-author-lexeme-form",
        "compose-author-feature-structure", "promote-gloss", "label", "comment", "finalize",
        "discard-draft", "reopen", "duplicate", "remove-operations", "split", "defer", "reject",
        "supersede", "list", "show", "dry-run", "dry-run --wait", "trial", "trial --wait", "apply", "log",
    ];

    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-release-surface-" + Guid.NewGuid().ToString("N"));

    public ReleaseSurfaceTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void EveryCataloguedCommandDeclaresAReleasedOrDeveloperSurface()
    {
        var surface = typeof(CommandDescriptor).GetProperty("Surface", BindingFlags.Public | BindingFlags.Instance);

        Assert.NotNull(surface);
        var names = CommandCatalog.All
            .Select(command => (command.Name, Surface: surface!.GetValue(command)?.ToString()))
            .ToList();

        Assert.Equal(CommandCatalog.All.Count, names.Count);
        Assert.All(names, item => Assert.True(item.Surface is "Released" or "Developer"));
        Assert.Equal(ReleasedNames.Order(StringComparer.Ordinal),
            names.Where(item => item.Surface == "Released").Select(item => item.Name).Order(StringComparer.Ordinal));
        Assert.Equal(DeveloperNames.Order(StringComparer.Ordinal),
            names.Where(item => item.Surface == "Developer").Select(item => item.Name).Order(StringComparer.Ordinal));
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

        Assert.Equal(1, result.ExitCode);
        foreach (var name in ReleasedNames)
            Assert.Contains(name, result.Error, StringComparison.Ordinal);
        foreach (var name in DeveloperNames)
        {
            var usageLine = result.Error.Split(Environment.NewLine)
                .FirstOrDefault(line => line.StartsWith("  " + name + " ", StringComparison.Ordinal));
            Assert.Null(usageLine);
        }
        Assert.Contains("Configuration (the declared", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryDeveloperCommandIsRefusedOnTheReleasedSurfaceInTextAndJson()
    {
        foreach (var name in DeveloperNames)
        {
            var text = Run(name, developerCommands: false);
            Assert.Equal(FailureEnvelope.ExitCodeFor(FailureReason.Refused), text.ExitCode);
            Assert.Contains(name, text.Error, StringComparison.Ordinal);
            Assert.Contains("not part of Motif 0.1.0", text.Error, StringComparison.Ordinal);

            var json = Run(name + " --json", developerCommands: false);
            Assert.Equal(FailureEnvelope.ExitCodeFor(FailureReason.Refused), json.ExitCode);
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
    public void EveryReleasedCommandStillDispatchesOnTheReleasedSurface()
    {
        // Invoked bare: reaching its own usage complaint is enough to show the boundary let the verb past.
        foreach (var name in ReleasedNames)
        {
            var result = Run(name + " --json", developerCommands: false);

            Assert.DoesNotContain("command.not-in-release", result.Error, StringComparison.Ordinal);
            Assert.DoesNotContain("not part of Motif 0.1.0", result.Error, StringComparison.Ordinal);
            Assert.NotEqual(FailureEnvelope.ExitCodeFor(FailureReason.Refused), result.ExitCode);
        }
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }

    private CliRun Run(string arguments, bool developerCommands)
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
        start.Environment[RunnerOptions.RootVariable] = Path.Combine(_root, "machine");
        if (developerCommands)
            start.Environment["MOTIF_DEVELOPER_COMMANDS"] = "1";
        else
            start.Environment.Remove("MOTIF_DEVELOPER_COMMANDS");

        using var process = Process.Start(start)!;
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        var output = outputTask.GetAwaiter().GetResult();
        var error = errorTask.GetAwaiter().GetResult();
        Assert.True(process.WaitForExit(60000), "The CLI did not exit within its bound.");
        return new CliRun(process.ExitCode, output, error);
    }

    private sealed record CliRun(int ExitCode, string Output, string Error);
}
