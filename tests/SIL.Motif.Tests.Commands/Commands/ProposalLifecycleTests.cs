using SIL.Motif.Commands;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Host;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[Collection(LcmCacheParallelCollections.Group2)]
public sealed class ProposalLifecycleTests(PristineProjectFixture pristine)
{
    private static string Version => MotifProductVersion.CurrentText;

    [Fact]
    public void ADeclinedProposalIsKeptAndReopensAsADraftUnderTheSameId()
    {
        var (path, id) = Finalized("declined");

        var declined = ProposalLifecycleCommand.Decline(new DeclineProposalRequest(path, id));
        Assert.True(declined.Succeeded, declined.Refusal?.Message);
        Assert.Equal("rejected", Record(path, id).Status);

        var reopened = ProposalLifecycleCommand.Reopen(new ReopenProposalRequest(path, id));
        Assert.True(reopened.Succeeded, reopened.Refusal?.Message);
        var record = Record(path, id);
        Assert.Equal("review/" + id, record.DraftName);
        Assert.Equal("proposed", record.Status);
        Assert.Null(record.ArchivedUtc);
    }

    [Fact]
    public void ADraftIsFinalizedBeforeItIsDeclinedSoWhatWasDeclinedStaysReadable()
    {
        var path = NewProject();
        var id = Draft(path, "draft-declined");

        Assert.True(ProposalLifecycleCommand.Decline(new DeclineProposalRequest(path, id)).Succeeded);

        var record = Record(path, id);
        Assert.Null(record.DraftName);
        Assert.NotNull(record.IntentDigest);
        Assert.Equal("rejected", record.Status);
    }

    [Fact]
    public void AnAssistantWithdrawsItsProposalAndAPersonMayReopenIt()
    {
        var (path, id) = Finalized("withdrawn");

        Assert.True(ProposalLifecycleCommand.Withdraw(new WithdrawProposalRequest(path, id)).Succeeded);
        Assert.Equal("withdrawn", Record(path, id).Status);

        Assert.True(ProposalLifecycleCommand.Reopen(new ReopenProposalRequest(path, id)).Succeeded);
        Assert.Equal("proposed", Record(path, id).Status);
    }

    [Fact]
    public void ASupersededProposalNamesItsReplacementInsteadOfReopening()
    {
        var (path, id) = Finalized("old");
        var replacement = CanonicalId.Mint().Value;
        using (var database = ProjectMotifDatabase.Open(path))
            new ProposalRepository(database).SetStatus(CanonicalId.Parse(id), "superseded", replacement);

        var reopened = ProposalLifecycleCommand.Reopen(new ReopenProposalRequest(path, id));

        Assert.Equal("proposal.superseded", reopened.Refusal?.Code);
        Assert.Equal(replacement, reopened.Refusal!.Facts["supersededBy"]);
        Assert.Null(Record(path, id).DraftName);
    }

    [Fact]
    public void AnAppliedProposalIsFinal()
    {
        var (path, id) = Finalized("applied");
        using (var database = ProjectMotifDatabase.Open(path))
            new ProposalRepository(database).SetStatus(CanonicalId.Parse(id), "applied", null);

        Assert.Equal("proposal.applied-final",
            ProposalLifecycleCommand.Reopen(new ReopenProposalRequest(path, id)).Refusal?.Code);
        Assert.Equal("proposal.applied-final",
            ProposalLifecycleCommand.Delete(new DeleteProposalRequest(path, id)).Refusal?.Code);
        Assert.Equal("applied", Record(path, id).Status);
    }

    [Fact]
    public void ThePendingChangesAreNeverDeclinedOrWithdrawn()
    {
        var path = NewProject();
        var id = CanonicalId.Mint();
        using (var database = ProjectMotifDatabase.Open(path))
            new ProposalRepository(database).CreateDraft(PendingChanges.DraftName, id,
                $$"""{"proposalId":"{{id.Value}}","contractVersions":{},"requires":[],"operations":[]}""",
                ProposalRoles.PendingChanges);

        Assert.Equal("proposal.pending-changes",
            ProposalLifecycleCommand.Decline(new DeclineProposalRequest(path, id.Value)).Refusal?.Code);
        Assert.Equal("proposal.pending-changes",
            ProposalLifecycleCommand.Withdraw(new WithdrawProposalRequest(path, id.Value)).Refusal?.Code);
    }

    [Fact]
    public void ThePendingChangesAreFoundByTheirRoleNotByTheirName()
    {
        var path = NewProject();
        var id = CanonicalId.Mint();
        using (var database = ProjectMotifDatabase.Open(path))
            new ProposalRepository(database).CreateDraft(PendingChanges.DraftName, id,
                $$"""{"proposalId":"{{id.Value}}","contractVersions":{},"requires":[],"operations":[]}""");
        Assert.True(ProposalCommands.AddSetGloss(new AddSetGlossRequest(path, Version, PendingChanges.DraftName,
            CanonicalId.Mint().Value, "en", "a gloss")).Succeeded);

        var applied = PendingChangesWorkflow.Apply(new ApplyPendingRequest(path, null, null));

        Assert.True(applied.Succeeded, applied.Refusal?.Message);
        Assert.False(applied.Value!.Applied);
        Assert.Null(PendingChanges.Load(new PendingChangesRequest(path, Version)).Value!.DraftId);
    }

    [Fact]
    public void DiscardingADraftIsRefusedWhenItChangedSinceItWasRead()
    {
        var path = NewProject();
        Draft(path, "discarded");
        string revision;
        using (var database = ProjectMotifDatabase.Open(path))
            revision = DraftRevision.Compute(new ProposalRepository(database).GetDraft("discarded").ProposalJson);
        Assert.True(ProposalCommands.Label(new LabelRequest(path, Version, "discarded", "changed")).Succeeded);

        var stale = ProposalCommands.DiscardDraft(new DiscardDraftRequest(path, Version, "discarded", revision));

        Assert.Equal("draft.revision-conflict", stale.Refusal?.Code);
        using (var database = ProjectMotifDatabase.Open(path))
            Assert.True(new ProposalRepository(database).DraftNameExists("discarded"));
    }

    private string NewProject()
    {
        using var scratch = pristine.NewScratch();
        return scratch.ProjectId.Path;
    }

    private static string Draft(string path, string name)
    {
        var created = ProposalCommands.New(new NewDraftRequest(path, Version, name, name));
        Assert.True(created.Succeeded, created.Refusal?.Message);
        Assert.True(ProposalCommands.AddSetGloss(new AddSetGlossRequest(path, Version, name,
            CanonicalId.Mint().Value, "en", name)).Succeeded);
        DraftRationale.Author(path, name, name, "Why " + name + " is right.");
        return created.Value!.ProposalId;
    }

    private (string Path, string Id) Finalized(string name)
    {
        var path = NewProject();
        var id = Draft(path, name);
        Assert.True(ProposalCommands.Finalize(new FinalizeRequest(path, Version, name)).Succeeded);
        return (path, id);
    }

    private static ProposalRecord Record(string path, string id)
    {
        using var database = ProjectMotifDatabase.Open(path);
        return new ProposalRepository(database).Get(CanonicalId.Parse(id));
    }
}
