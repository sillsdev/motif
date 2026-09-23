using System;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Projection.Store;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Cli;

/// <summary>
/// The status transitions that survive the removal of approval: <c>defer</c>, <c>reject</c> and
/// <c>supersede</c> as explicit moves, each refusing from a status it is not legal from. Nothing here
/// authorises an apply — a Proposal is applied because it is ready, not because someone signed it.
/// </summary>
[Collection(TestFixtures.LcmCacheTestCollection.Name)]
public sealed class ProposalStatusTransitionTests
{
    private const string ProductVersion = "1.0";

    private readonly string _fwDataPath;

    public ProposalStatusTransitionTests(PristineProjectFixture pristine)
    {
        using var scratch = pristine.NewScratch();
        _fwDataPath = scratch.ProjectId.Path;
    }

    private string CommitFreshProposal(string draftName = "d")
    {
        Assert.True(ProposalCommands.New(new NewDraftRequest(_fwDataPath, ProductVersion, draftName, null)).Succeeded);
        var target = CanonicalId.Mint().Value;
        Assert.True(ProposalCommands.AddSetGloss(
            new AddSetGlossRequest(_fwDataPath, ProductVersion, draftName, target, "en", "a gloss")).Succeeded);
        DraftRationale.Author(
            _fwDataPath, draftName, "Clarify a lexical analysis", "Record the intended gloss so reviewers can assess the change.");
        var finalize = ProposalCommands.Finalize(new FinalizeRequest(_fwDataPath, ProductVersion, draftName));
        Assert.True(finalize.Succeeded);
        return finalize.Value!.ProposalId;
    }

    private ProposalRecord GetRecord(string proposalId)
    {
        using var database = ProjectMotifDatabase.Open(_fwDataPath);
        return new ProposalRepository(database).Get(CanonicalId.Parse(proposalId));
    }

    private void SetStatusRaw(string proposalId, string status)
    {
        using var database = ProjectMotifDatabase.Open(_fwDataPath);
        new ProposalRepository(database).SetStatus(CanonicalId.Parse(proposalId), status, supersededBy: null);
    }

    [Fact]
    public void Defer_MovesAProposedProposalToDeferred()
    {
        var id = CommitFreshProposal();

        var result = ProposalCommands.Defer(new DeferRequest(_fwDataPath, ProductVersion, id));
        Assert.True(result.Succeeded);
        Assert.Equal(ManifestStatus.Deferred, result.Value!.Status);

        Assert.Equal(ManifestStatus.Deferred, GetRecord(id).Status);
    }

    [Fact]
    public void Reject_IsAllowedFromProposedAndFromDeferred()
    {
        var proposed = CommitFreshProposal("straight");
        Assert.True(ProposalCommands.Reject(new RejectRequest(_fwDataPath, ProductVersion, proposed)).Succeeded);
        Assert.Equal(ManifestStatus.Rejected, GetRecord(proposed).Status);

        var deferred = CommitFreshProposal("later");
        Assert.True(ProposalCommands.Defer(new DeferRequest(_fwDataPath, ProductVersion, deferred)).Succeeded);
        Assert.True(ProposalCommands.Reject(new RejectRequest(_fwDataPath, ProductVersion, deferred)).Succeeded);
        Assert.Equal(ManifestStatus.Rejected, GetRecord(deferred).Status);
    }

    [Fact]
    public void Reject_ANotFoundProposal_Refuses()
    {
        var result = ProposalCommands.Reject(
            new RejectRequest(_fwDataPath, ProductVersion, CanonicalId.Mint().Value));

        Assert.False(result.Succeeded);
        Assert.Equal("proposal.not-found", result.Refusal!.Code);
        Assert.Equal(FailureReason.NotFound, result.Refusal.Reason);
    }

    [Fact]
    public void Reject_AnAlreadyAppliedProposal_Refuses_NamingTheDisallowedTransition()
    {
        var id = CommitFreshProposal();
        SetStatusRaw(id, ManifestStatus.Applied);

        var result = ProposalCommands.Reject(new RejectRequest(_fwDataPath, ProductVersion, id));

        Assert.False(result.Succeeded);
        Assert.Equal("proposal.invalid-status", result.Refusal!.Code);
        Assert.Contains("applied", result.Refusal.Message, StringComparison.Ordinal);
        Assert.Contains("rejected", result.Refusal.Message, StringComparison.Ordinal);
        Assert.Equal(ManifestStatus.Applied, GetRecord(id).Status); // left untouched
    }

    [Fact]
    public void Supersede_NamesTheReplacement()
    {
        var id = CommitFreshProposal("old");
        var replacementId = CommitFreshProposal("new");

        var result = ProposalCommands.Supersede(new SupersedeRequest(
            _fwDataPath, ProductVersion, id, replacementId));

        Assert.True(result.Succeeded);
        Assert.Equal(ManifestStatus.Superseded, result.Value!.Status);
        Assert.Equal(replacementId, result.Value.RelatedProposalId);
        var record = GetRecord(id);
        Assert.Equal(ManifestStatus.Superseded, record.Status);
        Assert.Equal(replacementId, record.SupersededBy);
    }

    [Fact]
    public void Supersede_IsAllowedFromRejected_SoAReplacementCanPointAtHoweverItEnded()
    {
        var id = CommitFreshProposal("old");
        Assert.True(ProposalCommands.Reject(new RejectRequest(_fwDataPath, ProductVersion, id)).Succeeded);
        var replacementId = CommitFreshProposal("new");

        Assert.True(ProposalCommands.Supersede(new SupersedeRequest(
            _fwDataPath, ProductVersion, id, replacementId)).Succeeded);

        Assert.Equal(ManifestStatus.Superseded, GetRecord(id).Status);
    }

}
