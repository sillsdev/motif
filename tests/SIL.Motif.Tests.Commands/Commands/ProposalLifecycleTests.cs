using SIL.Motif.Commands;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Host;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Worker.Jobs;
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
        // The Proposal stays Declined until its Draft is Finalized; discarding the Draft leaves it as it was.
        Assert.Equal("rejected", record.Status);
    }

    [Fact]
    public void DecliningWithAReasonSavesACommentAndFreezesIt()
    {
        var (path, id) = Finalized("declined-with-reason");

        var declined = ProposalLifecycleCommand.Decline(new DeclineProposalRequest(path, id, "Needs another analysis."));

        Assert.True(declined.Succeeded, declined.Refusal?.Message);
        var shown = ProposalCommands.Show(new ShowProposalRequest(path, Version, id));
        Assert.True(shown.Succeeded, shown.Refusal?.Message);
        Assert.Equal("Needs another analysis.", Assert.Single(shown.Value!.Comments!).Markdown);
        Assert.False(ProposalComments.Add(new AddCommentRequest(path, Version, id, "Too late", "")).Succeeded);
    }

    [Fact]
    public void AnUndeferredProposalIsWaitingAgainAndTheConversationRecordsIt()
    {
        var (path, id) = Finalized("undeferred");
        Assert.True(ProposalCommands.Defer(new DeferRequest(path, Version, id)).Succeeded);
        Assert.Equal("deferred", Record(path, id).Status);

        var back = ProposalCommands.Undefer(new UndeferRequest(path, Version, id));

        Assert.True(back.Succeeded, back.Refusal?.Message);
        Assert.Equal("proposed", back.Value!.Status);
        Assert.Equal("proposed", Record(path, id).Status);
        var shown = ProposalCommands.Show(new ShowProposalRequest(path, Version, id)).Value!;
        var note = Assert.Single(shown.Comments!);
        Assert.Equal(("system", ProposalCommands.UndeferNote), (note.Kind, note.Markdown));
        Assert.False(shown.CommentsChanged);
    }

    [Fact]
    public void AnUndeferNoteNeverHidesAPersonsNewerComment()
    {
        var (path, id) = Finalized("undefer-comment");
        Assert.True(ProposalCommands.Defer(new DeferRequest(path, Version, id)).Succeeded);
        Assert.True(ProposalComments.Add(new AddCommentRequest(path, Version, id, "Please look again", "")).Succeeded);

        Assert.True(ProposalCommands.Undefer(new UndeferRequest(path, Version, id)).Succeeded);

        Assert.True(ProposalCommands.Show(new ShowProposalRequest(path, Version, id)).Value!.CommentsChanged);
    }

    [Theory]
    [InlineData("proposed")]
    [InlineData("rejected")]
    [InlineData("applied")]
    public void OnlyADeferredProposalCanBeUndeferred(string status)
    {
        var (path, id) = Finalized("not-deferred-" + status);
        using (var database = ProjectMotifDatabase.Open(path))
            new ProposalRepository(database).SetStatus(CanonicalId.Parse(id), status, null);

        var refused = ProposalCommands.Undefer(new UndeferRequest(path, Version, id));

        Assert.False(refused.Succeeded);
        Assert.Equal("proposal.invalid-status", refused.Refusal!.Code);
        Assert.Equal(status, Record(path, id).Status);
    }

    [Fact]
    public void ARefusedDeclineDoesNotSaveItsReason()
    {
        var (path, id) = Finalized("already-applied");
        using (var database = ProjectMotifDatabase.Open(path))
            new ProposalRepository(database).SetStatus(CanonicalId.Parse(id), "applied", null);

        var declined = ProposalLifecycleCommand.Decline(new DeclineProposalRequest(path, id, "Not applicable."));

        Assert.False(declined.Succeeded);
        var shown = ProposalCommands.Show(new ShowProposalRequest(path, Version, id));
        Assert.True(shown.Succeeded, shown.Refusal?.Message);
        Assert.Empty(shown.Value!.Comments!);
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
    public void DecliningADraftWithAReasonFinalizesDeclinesAndSavesTheReasonInOneStep()
    {
        var path = NewProject();
        var id = Draft(path, "draft-declined-why");

        Assert.True(ProposalLifecycleCommand.Decline(new DeclineProposalRequest(path, id, "Not this one.")).Succeeded);

        var record = Record(path, id);
        Assert.Null(record.DraftName);
        Assert.Equal("rejected", record.Status);
        var shown = ProposalCommands.Show(new ShowProposalRequest(path, Version, id));
        Assert.Equal("Not this one.", Assert.Single(shown.Value!.Comments!).Markdown);
    }

    [Fact]
    public void AnAssistantWithdrawsItsProposalAndAPersonMayReopenIt()
    {
        var (path, id) = Finalized("withdrawn");

        Assert.True(ProposalLifecycleCommand.Withdraw(new WithdrawProposalRequest(path, id)).Succeeded);
        Assert.Equal("withdrawn", Record(path, id).Status);

        Assert.True(ProposalLifecycleCommand.Reopen(new ReopenProposalRequest(path, id)).Succeeded);
        Assert.Equal("withdrawn", Record(path, id).Status);
    }

    [Fact]
    public void AnEmptyDraftCanBeWithdrawnWithoutFinalizingAnEmptyProposal()
    {
        var path = NewProject();
        var created = ProposalCommands.New(new NewDraftRequest(path, Version, "empty-withdrawal", "Empty draft"));
        Assert.True(created.Succeeded, created.Refusal?.Message);
        Assert.True(DraftRationale.Write(path, Version, "empty-withdrawal", "No change was needed.").Succeeded);

        var withdrawn = ProposalLifecycleCommand.Withdraw(
            new WithdrawProposalRequest(path, created.Value!.ProposalId));

        Assert.True(withdrawn.Succeeded, withdrawn.Refusal?.Message);
        Assert.Equal("withdrawn", withdrawn.Value!.Status);
        using (var database = ProjectMotifDatabase.Open(path))
            Assert.Throws<KeyNotFoundException>(() => new ProposalRepository(database).GetDraft("empty-withdrawal"));

        var reused = ProposalCommands.New(new NewDraftRequest(path, Version, "empty-withdrawal", "A replacement draft"));
        Assert.True(reused.Succeeded, reused.Refusal?.Message);
        Assert.NotEqual(created.Value.ProposalId, reused.Value!.ProposalId);
    }

    [Fact]
    public void WithdrawingAFinalizableDraftFinalizesAndWithdrawsItInOneStep()
    {
        var path = NewProject();
        var id = Draft(path, "draft-withdrawn");

        var withdrawn = ProposalLifecycleCommand.Withdraw(new WithdrawProposalRequest(path, id));

        Assert.True(withdrawn.Succeeded, withdrawn.Refusal?.Message);
        var record = Record(path, id);
        Assert.Null(record.DraftName);
        Assert.NotNull(record.IntentDigest);
        Assert.Equal("withdrawn", record.Status);
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

    [Fact]
    public void TheDefaultListShowsAReopenedProposalUntilItsDraftIsDiscarded()
    {
        var (path, id) = Finalized("listed");
        Assert.True(ProposalLifecycleCommand.Decline(new DeclineProposalRequest(path, id)).Succeeded);
        Assert.DoesNotContain(id, ListedIds(path));

        Assert.True(ProposalLifecycleCommand.Reopen(new ReopenProposalRequest(path, id)).Succeeded);
        Assert.Contains(id, ListedIds(path));

        string revision;
        using (var database = ProjectMotifDatabase.Open(path))
            revision = DraftRevision.Compute(new ProposalRepository(database).GetDraft("review/" + id).ProposalJson);
        var discarded = ProposalCommands.DiscardDraft(new DiscardDraftRequest(path, Version, "review/" + id, revision));
        Assert.True(discarded.Succeeded, discarded.Refusal?.Message);
        Assert.DoesNotContain(id, ListedIds(path));
    }

    [Fact]
    public void ApplyingAReopenedProposalByIdIsRefusedBecauseItHasAnOpenDraft()
    {
        var (path, id) = Finalized("apply-reopened");
        Assert.True(ProposalLifecycleCommand.Decline(new DeclineProposalRequest(path, id)).Succeeded);
        Assert.True(ProposalLifecycleCommand.Reopen(new ReopenProposalRequest(path, id)).Succeeded);

        var applied = ProposalCommands.Apply(new ApplyRequest(path, Version, id));

        Assert.Equal("proposal.has-draft", applied.Refusal?.Code);
        Assert.Equal(SIL.Motif.Contract.Responses.FailureReason.Refused, applied.Refusal?.Reason);
    }

    [Fact]
    public void WithdrawingAProposalCancelsItsDryRunAndTrialAndLeavesAnotherProposalsJobsQueued()
    {
        var path = NewProject();
        var withdrawn = Draft(path, "withdrawn-jobs");
        var kept = Draft(path, "kept-jobs");
        var dryRun = Queue(JobCommands.EnqueueDryRun(new EnqueueDryRunRequest(path, Version, withdrawn)));
        var trial = Queue(JobCommands.EnqueueTrial(new EnqueueTrialRequest(path, Version, withdrawn, Words: ["word"])));
        var other = Queue(JobCommands.EnqueueTrial(new EnqueueTrialRequest(path, Version, kept, Words: ["word"])));

        var result = ProposalLifecycleCommand.Withdraw(new WithdrawProposalRequest(path, withdrawn));

        Assert.True(result.Succeeded, result.Refusal?.Message);
        Assert.Equal(new[] { dryRun, trial }.Order(), result.Value!.CancelledJobIds!.Order());
        Assert.Equal(JobStatus.Cancelled, JobStatusOf(path, dryRun));
        Assert.Equal(JobStatus.Cancelled, JobStatusOf(path, trial));
        Assert.Equal(JobStatus.Queued, JobStatusOf(path, other));
    }

    [Fact]
    public void DecliningAProposalCancelsItsQueuedTrial()
    {
        var (path, id) = Finalized("declined-jobs");
        var trial = Queue(JobCommands.EnqueueTrial(new EnqueueTrialRequest(path, Version, id, Words: ["word"])));

        var result = ProposalLifecycleCommand.Decline(new DeclineProposalRequest(path, id, "No."));

        Assert.True(result.Succeeded, result.Refusal?.Message);
        Assert.Equal(trial, Assert.Single(result.Value!.CancelledJobIds!));
        Assert.Equal(JobStatus.Cancelled, JobStatusOf(path, trial));
    }

    [Fact]
    public void AWithdrawnProposalsRunningTrialIsReportedAsAskedToStopNotCancelled()
    {
        var path = NewProject();
        var id = Draft(path, "running-jobs");
        var trial = Queue(JobCommands.EnqueueTrial(new EnqueueTrialRequest(path, Version, id, Words: ["word"])));
        using (var database = ProjectMotifDatabase.Open(path))
            new JobRepository(database).Transition(trial, JobStatus.Running);

        var result = ProposalLifecycleCommand.Withdraw(new WithdrawProposalRequest(path, id));

        Assert.True(result.Succeeded, result.Refusal?.Message);
        Assert.Null(result.Value!.CancelledJobIds);
        Assert.Equal(trial, Assert.Single(result.Value.CancelRequestedJobIds!));
        using var check = ProjectMotifDatabase.Open(path);
        var job = new JobRepository(check).Get(trial)!;
        Assert.Equal(JobStatus.Running, job.Status);
        Assert.True(job.CancellationRequested);
    }

    [Fact]
    public void AJobThatCannotBeCancelledIsReportedWithItsReasonNotDropped()
    {
        var path = NewProject();
        var id = Draft(path, "failing-cancel");
        var trial = Queue(JobCommands.EnqueueTrial(new EnqueueTrialRequest(path, Version, id, Words: ["word"])));

        var result = ProposalLifecycleCommand.CancelOpenJobs(path, id, request => CommandOutcome<JobStatusResponse>.Refused(
            new Refusal("store.busy", FailureReason.Refused, "The project store is busy.")));

        Assert.Equal(trial, Assert.Single(result.Failed));
        Assert.Empty(result.Cancelled);
        Assert.Contains("The project store is busy.", result.Warning, StringComparison.Ordinal);
        Assert.Equal(JobStatus.Queued, JobStatusOf(path, trial));
    }

    [Fact]
    public void AClosedProposalWithNoJobsReportsNoneCancelled()
    {
        var (path, id) = Finalized("no-jobs");

        var result = ProposalLifecycleCommand.Withdraw(new WithdrawProposalRequest(path, id));

        Assert.True(result.Succeeded, result.Refusal?.Message);
        Assert.Null(result.Value!.CancelledJobIds);
    }

    private static string Queue(CommandOutcome<JobEnqueuedResponse> queued)
    {
        Assert.True(queued.Succeeded, queued.Refusal?.Message);
        return queued.Value!.JobId;
    }

    private static JobStatus JobStatusOf(string path, string jobId)
    {
        using var database = ProjectMotifDatabase.Open(path);
        return new JobRepository(database).Get(jobId)!.Status;
    }

    private static string[] ListedIds(string path)
    {
        var listed = ProposalCommands.List(new ListProposalsRequest(path, Version));
        Assert.True(listed.Succeeded, listed.Refusal?.Message);
        return listed.Value!.Proposals.Select(item => item.ProposalId).ToArray();
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
