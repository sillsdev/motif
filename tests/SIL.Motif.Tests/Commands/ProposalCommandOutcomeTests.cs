using System;
using System.IO;
using System.Linq;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Generator;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>
/// Pins <see cref="ProposalCommands"/>'s typed success shapes — the response records
/// <see cref="SIL.Motif.Tests.Cli.CommandsRefusalsTests"/> does not exercise, which focuses on refusals — and the source
/// boundary the extraction into <see cref="CommandOutcome{T}"/> is meant to hold.
/// </summary>
[Collection(TestFixtures.LcmCacheTestCollection.Name)]
public sealed class ProposalCommandOutcomeTests
{
    private const string ProductVersion = "1.0";

    private readonly string _fwDataPath;

    public ProposalCommandOutcomeTests(PristineProjectFixture pristine)
    {
        using var scratch = pristine.NewScratch();
        _fwDataPath = scratch.ProjectId.Path;
    }

    /// <summary>
    /// No file under <c>SIL.Motif.Commands</c> renders its own text any more: rendering, including the
    /// <c>error: </c> prefix, is the CLI's job alone (ADR 0043 decision 2).
    /// </summary>
    [Fact]
    public void CommandsRendersNothingItself()
    {
        var commandsRoot = Path.Combine(RepoPaths.FindRepoRoot(), "src", "SIL.Motif.Commands");
        var offendingLines = Directory.EnumerateFiles(commandsRoot, "*.cs", SearchOption.AllDirectories)
            .SelectMany(File.ReadLines)
            .Where(line => line.Contains("CommandResult", StringComparison.Ordinal)
                || line.Contains("error: ", StringComparison.Ordinal))
            .ToList();

        Assert.Empty(offendingLines);
    }

    [Fact]
    public void NewReturnsATypedDraftCreatedResponse()
    {
        var outcome = ProposalCommands.New(new NewDraftRequest(_fwDataPath, ProductVersion, "d", "a label"));

        Assert.True(outcome.Succeeded);
        var value = outcome.Value!;
        Assert.Equal("d", value.DraftName);
        Assert.Equal("a label", value.Label);
        Assert.True(CanonicalId.TryParse(value.ProposalId, out _));
    }

    [Fact]
    public void AddSetGlossReturnsATypedResponseCarryingWhatWasAdded()
    {
        ProposalCommands.New(new NewDraftRequest(_fwDataPath, ProductVersion, "d", null));
        var target = CanonicalId.Mint().Value;

        var outcome = ProposalCommands.AddSetGloss(
            new AddSetGlossRequest(_fwDataPath, ProductVersion, "d", target, "en", "a gloss"));

        Assert.True(outcome.Succeeded);
        var value = outcome.Value!;
        Assert.Equal("d", value.DraftName);
        Assert.Equal(target, value.Target);
        Assert.Equal("en", value.Ws);
        Assert.Equal("a gloss", value.Text);
        Assert.Equal(1, value.OperationCount);
        Assert.Empty(value.DependsOn);
    }

    [Fact]
    public void FinalizeThenReopenRoundTripsThroughTypedResponses()
    {
        ProposalCommands.New(new NewDraftRequest(_fwDataPath, ProductVersion, "d", null));
        ProposalCommands.AddSetGloss(
            new AddSetGlossRequest(_fwDataPath, ProductVersion, "d", CanonicalId.Mint().Value, "en", "a gloss"));
        DraftRationale.Author(_fwDataPath, "d", "Clarify a gloss", "Record the intended reading for review.");

        var finalized = ProposalCommands.Finalize(new FinalizeRequest(_fwDataPath, ProductVersion, "d"));
        Assert.True(finalized.Succeeded);
        Assert.False(finalized.Value!.IsAmend);

        var reopened = ProposalCommands.Reopen(
            new ReopenRequest(_fwDataPath, ProductVersion, "d2", finalized.Value.ProposalId));
        Assert.True(reopened.Succeeded);
        Assert.Equal(finalized.Value.ProposalId, reopened.Value!.ProposalId);
        Assert.Equal(1, reopened.Value.OperationCount);
    }

    [Fact]
    public void DeferReturnsATypedProposalStatusChangedResponse()
    {
        var proposalId = CommitOneOperationProposal("d");

        var outcome = ProposalCommands.Defer(new DeferRequest(_fwDataPath, ProductVersion, proposalId));

        Assert.True(outcome.Succeeded);
        Assert.Equal(proposalId, outcome.Value!.ProposalId);
        Assert.Equal(SIL.Motif.Projection.Store.ManifestStatus.Deferred, outcome.Value.Status);
        Assert.Null(outcome.Value.RelatedProposalId);
    }

    [Fact]
    public void SupersedeNamesTheReplacingProposalInFacts()
    {
        var supersededId = CommitOneOperationProposal("d1");
        var replacementId = CommitOneOperationProposal("d2");

        var outcome = ProposalCommands.Supersede(
            new SupersedeRequest(_fwDataPath, ProductVersion, supersededId, replacementId));

        Assert.True(outcome.Succeeded);
        Assert.Equal(replacementId, outcome.Value!.RelatedProposalId);
    }

    /// <summary>
    /// Before this refactor, a removal that would orphan a dependent operation returned a bare
    /// exit code with no <see cref="FailureReason"/> at all, so it fell outside the
    /// "every failure under --json carries an envelope" contract <c>FailureContractTests</c> pins. As a
    /// typed <see cref="Refusal"/> it now carries a stable code, so a caller can act
    /// on it identically to any other refusal.
    /// </summary>
    [Fact]
    public void RemovingAnOperationThatWouldOrphanADependentIsATypedRefusalNotABareExitCode()
    {
        ProposalCommands.New(new NewDraftRequest(_fwDataPath, ProductVersion, "d", null));
        var first = ProposalCommands.AddSetGloss(
            new AddSetGlossRequest(_fwDataPath, ProductVersion, "d", CanonicalId.Mint().Value, "en", "first"));
        var second = ProposalCommands.AddSetGloss(new AddSetGlossRequest(
            _fwDataPath, ProductVersion, "d", CanonicalId.Mint().Value, "en", "second",
            new[] { first.Value!.OperationId }));
        Assert.True(second.Succeeded);

        var outcome = ProposalCommands.RemoveOperations(new RemoveOperationsRequest(
            _fwDataPath, ProductVersion, "d", new[] { first.Value.OperationId }, Force: false));

        Assert.False(outcome.Succeeded);
        var refusal = outcome.Refusal!;
        Assert.Equal("operation.invalid-dependency", refusal.Code);
        Assert.Equal(FailureReason.InvalidArgument, refusal.Reason);
        Assert.Contains("would orphan", refusal.Message, StringComparison.Ordinal);
    }

    private string CommitOneOperationProposal(string draftName)
    {
        ProposalCommands.New(new NewDraftRequest(_fwDataPath, ProductVersion, draftName, null));
        ProposalCommands.AddSetGloss(new AddSetGlossRequest(
            _fwDataPath, ProductVersion, draftName, CanonicalId.Mint().Value, "en", "text for " + draftName));
        DraftRationale.Author(
            _fwDataPath, draftName, "Clarify a lexical gloss", "Record the intended lexical analysis for review.");
        var finalized = ProposalCommands.Finalize(new FinalizeRequest(_fwDataPath, ProductVersion, draftName));
        Assert.True(finalized.Succeeded);
        return finalized.Value!.ProposalId;
    }
}
