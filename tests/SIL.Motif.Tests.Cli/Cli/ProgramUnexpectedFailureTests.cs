using System;
using System.Collections.Generic;
using System.IO;
using SIL.Motif.Cli;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Preferences;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker;
using Xunit;

namespace SIL.Motif.Tests.Cli;

[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group3)]
public sealed class ProgramUnexpectedFailureTests(PristineProjectFixture pristine)
{
    [Fact]
    public void UnexpectedExceptionInJsonModeUsesAFailureEnvelope()
    {
        var (path, proposalId) = CreateProposal();

        var result = Run(path, proposalId, asJson: true,
            new InvalidOperationException("injected runner failure"));

        Assert.Equal(FailureEnvelope.ExitCodeFor(FailureReason.StoreInconsistent), result.ExitCode);
        Assert.Empty(result.Output);
        var envelope = ProjectionJson.Deserialize<FailureEnvelope>(result.Error)!;
        Assert.Equal("cli.unexpected", envelope.Code);
        Assert.Equal(FailureReason.StoreInconsistent, envelope.Reason);
        Assert.Contains("injected runner failure", envelope.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CancellationUsesTheCancelledFailureInJsonAndText(bool asJson)
    {
        var (path, proposalId) = CreateProposal();

        var result = Run(path, proposalId, asJson, new OperationCanceledException("injected cancellation"));

        Assert.Equal(FailureEnvelope.ExitCodeFor(FailureReason.Cancelled), result.ExitCode);
        Assert.Empty(result.Output);
        if (asJson)
        {
            var envelope = ProjectionJson.Deserialize<FailureEnvelope>(result.Error)!;
            Assert.Equal("cli.cancelled", envelope.Code);
            Assert.Equal(FailureReason.Cancelled, envelope.Reason);
            Assert.Contains("injected cancellation", envelope.Message, StringComparison.Ordinal);
        }
        else
        {
            Assert.Equal("cli.cancelled: injected cancellation" + Environment.NewLine, result.Error);
        }
    }

    private (string Path, string ProposalId) CreateProposal()
    {
        var seed = pristine.Seed;
        string path;
        using (var scratch = pristine.NewScratch()) path = scratch.ProjectId.Path;
        Assert.True(ProposalCommands.New(new NewDraftRequest(path, "1.0", "cli-exception", "CLI exception handling")).Succeeded);
        Assert.True(ProposalCommands.Comment(new CommentRequest(path, "1.0", "cli-exception",
            "Exercise CLI failures after a valid Proposal is finalized.")).Succeeded);
        Assert.True(ProposalCommands.AddSetGloss(new AddSetGlossRequest(path, "1.0", "cli-exception",
            CanonicalId.FromGuid(seed.FirstSenseId).Value, NewLangProjFixture.AnalysisTag, "injected")).Succeeded);
        var finalized = ProposalCommands.Finalize(new FinalizeRequest(path, "1.0", "cli-exception"));
        Assert.True(finalized.Succeeded, finalized.Refusal?.Message);
        return (path, finalized.Value!.ProposalId);
    }

    private static CliProcessResult Run(string path, string proposalId, bool asJson, Exception failure)
    {
        var workerRoot = Path.Combine(Path.GetTempPath(), "motif-cli-unexpected-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workerRoot);
        var preferencePath = Path.Combine(workerRoot, "advanced-ai-mode.json");
        new FileAdvancedAiModePreferenceStore(preferencePath).SetEnabled(true);
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [RunnerOptions.RootVariable] = workerRoot,
            [FileAdvancedAiModePreferenceStore.PathEnvironmentVariable] = preferencePath,
        };
        using var output = new StringWriter();
        using var error = new StringWriter();

        try
        {
            var arguments = asJson
                ? new[] { "dry-run", "--project", path, proposalId, "--json" }
                : new[] { "dry-run", "--project", path, proposalId };
            var exitCode = global::SIL.Motif.Cli.Program.Run(arguments, output, error, environment,
                (_, _) => throw failure);
            return new CliProcessResult(exitCode, output.ToString(), error.ToString());
        }
        finally
        {
            if (Directory.Exists(workerRoot)) Directory.Delete(workerRoot, recursive: true);
        }
    }
}
