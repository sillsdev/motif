using System;
using System.Collections.Generic;
using System.IO;
using SIL.Motif.Cli;
using SIL.Motif.Commands.Preferences;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker;
using Xunit;

namespace SIL.Motif.Tests.Cli;

/// <summary>The verbs that read one Proposal whose stored content no longer parses.</summary>
public sealed class UnreadableProposalRefusalCliTests : IDisposable
{
    private readonly UnreadableProposalProject _project = UnreadableProposalProject.Create();
    private readonly string _workerRoot = Path.Combine(Path.GetTempPath(),
        "motif-cli-unreadable-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        _project.Dispose();
        if (Directory.Exists(_workerRoot)) Directory.Delete(_workerRoot, recursive: true);
    }

    [Fact]
    public void OverviewOfAnUnreadableProposalIsRefusedInJson()
    {
        var result = Run("overview", "--project", _project.ProjectPath, "--proposal", _project.ProposalId, "--json");

        AssertRefusal(result);
    }

    [Fact]
    public void DifferenceProduceOfAnUnreadableProposalIsRefusedInJson()
    {
        var result = Run("difference", "produce", "--project", _project.ProjectPath, "--proposal", _project.ProposalId,
            "--text", Guid.NewGuid().ToString("D"), "--json");

        AssertRefusal(result);
    }

    [Fact]
    public void TrialOfAnUnreadableDraftIsRefusedInJson()
    {
        var words = Path.Combine(_workerRoot, "words.txt");
        Directory.CreateDirectory(_workerRoot);
        File.WriteAllText(words, "cat\n");

        var result = Run("trial", "--project", _project.ProjectPath, _project.ProposalId, "--words", words, "--json");

        AssertRefusal(result);
    }

    [Fact]
    public void OverviewOfAnUnreadableProposalIsRefusedInText()
    {
        var result = Run("overview", "--project", _project.ProjectPath, "--proposal", _project.ProposalId);

        Assert.Equal(FailureEnvelope.ExitCodeFor(FailureReason.Refused), result.ExitCode);
        Assert.Contains(UnreadableProposalProject.UnknownKind, result.Error, StringComparison.Ordinal);
    }

    private static void AssertRefusal(CliProcessResult result)
    {
        Assert.True(FailureEnvelope.ExitCodeFor(FailureReason.Refused) == result.ExitCode, result.Error);
        var envelope = ProjectionJson.Deserialize<FailureEnvelope>(result.Error)!;
        Assert.Equal(RefusalCodes.ProposalUnreadable, envelope.Code);
        Assert.Equal(FailureReason.Refused, envelope.Reason);
        Assert.Contains(UnreadableProposalProject.UnknownKind, envelope.Message, StringComparison.Ordinal);
    }

    private CliProcessResult Run(params string[] arguments)
    {
        Directory.CreateDirectory(_workerRoot);
        var preferencePath = Path.Combine(_workerRoot, "advanced-ai-mode.json");
        new FileAdvancedAiModePreferenceStore(preferencePath).SetEnabled(true);
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [RunnerOptions.RootVariable] = _workerRoot,
            [FileAdvancedAiModePreferenceStore.PathEnvironmentVariable] = preferencePath,
        };
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = global::SIL.Motif.Cli.Program.Run(arguments, output, error, environment, static (_, _) => { });
        return new CliProcessResult(exitCode, output.ToString(), error.ToString());
    }
}
